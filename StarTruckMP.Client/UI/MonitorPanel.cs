using System;
using System.Collections.Generic;
using System.Text;
using StarTruckMP.Client.Synchronization;
using StarTruckMP.Shared.Cmd;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace StarTruckMP.Client.UI;

/// <summary>
/// The multiplayer page on the truck's own right-hand monitor, in place of the docking camera.
///
/// The screen carries three bands and nothing else: who is on the server and where, the recent
/// chat, and a line to type into. It is a clone of one of the game's own interface pages, so the
/// monitor's typography comes along for free.
///
/// Only the docking-camera channel is taken, and only on the monitor this was installed onto.
/// Every other channel of that monitor, and every monitor beside it, is left to the game.
/// </summary>
internal static class MonitorPanel
{
    /// <summary>Lines of chat on screen. Fixed, so the log can never grow into the roster.</summary>
    private const int ChatLinesShown = 7;

    private const int MessageLimit = 120;

    private static MonitorChannelSwitcher _switcher;
    private static MonitorOverlaySwitcher _overlays;
    private static GameObject _panel;
    private static TextMeshProUGUI _header;
    private static TextMeshProUGUI _roster;
    private static TextMeshProUGUI _chat;
    private static TextMeshProUGUI _input;

    private static float _nextRefresh;

    private static bool _typing;
    private static string _draft = string.Empty;
    private static bool _inputTaken;
    private static string _notice = string.Empty;
    private static float _noticeUntil;

    public static bool Exists => _panel != null;

    /// <summary>
    /// True only for the monitor this page was built on.
    ///
    /// Every monitor in the cab has its own copy of the switcher component, and acting on all of
    /// them meant one screen changing channel reached over and toggled another screen's readout.
    /// </summary>
    public static bool Owns(MonitorChannelSwitcher switcher) => _switcher != null && _switcher.Equals(switcher);

    /// <summary>True for the overlay switcher of the monitor this page was built on.</summary>
    public static bool Owns(MonitorOverlaySwitcher overlays) => _overlays != null && _overlays.Equals(overlays);

    /// <summary>Whether the page is on the screen right now.</summary>
    public static bool IsShowing => _panel != null && _panel.activeSelf;

    /// <summary>
    /// Takes the game's pages back down while ours is up. Called after every Update and again
    /// straight after the game shows one of its own pages, because that is exactly when the
    /// trailer readout used to land on top of ours: a hitch puts it up on its own account, with
    /// no channel change for the channel patch to see.
    /// </summary>
    public static void Reassert()
    {
        if (!IsShowing) return;
        HoldScreen();
    }

    public static void LateTick() => Reassert();

    /// <summary>
    /// Builds the page against a switcher, once. Safe to call repeatedly — the truck's monitors
    /// come and go with the cab, and only the first usable switcher is taken.
    /// </summary>
    public static void Install(MonitorChannelSwitcher switcher)
    {
        if (_panel != null || switcher == null) return;

        try
        {
            var overlays = switcher.monitorOverlaySwitcher;
            if (overlays == null)
            {
                App.Log.LogWarning("[Monitor] The channel switcher has no overlay switcher to build against.");
                return;
            }

            // Any of the game's own interface pages would do as the shell; the docking readout is
            // taken because it is the one page this screen never needs to show by itself.
            var template = overlays.dockedStatus;
            if (template == null)
            {
                App.Log.LogWarning("[Monitor] The switcher has no docking page to clone.");
                return;
            }

            _switcher = switcher;
            _overlays = overlays;

            _panel = Object.Instantiate(template.gameObject, template.transform.parent);
            _panel.name = "StarTruckMP_MonitorPanel";
            _panel.SetActive(false);

            // Without this the game keeps driving the clone as a docking readout.
            foreach (var logic in _panel.GetComponents<MonitorOverlayDockedStatus>())
                Object.DestroyImmediate(logic);

            // Every label carries a binding that would restore the docking text under us.
            foreach (var lookup in _panel.GetComponentsInChildren<StringTableLookup>(true))
                Object.DestroyImmediate(lookup);

            var model = LargestText(_panel);
            if (model == null)
            {
                App.Log.LogWarning("[Monitor] The docking page has no text to take the monitor's typography from.");
                Object.Destroy(_panel);
                _panel = null;
                return;
            }

            // The ground comes off the core-power page rather than the docking one: that is the
            // black screen the player already knows, and it is opaque because it carries its own
            // background image.
            var ground = Ground(overlays);

            // The docking page is a station name, a bay number and a readout, laid out for that
            // and nothing else. Take the typography and leave the layout: the page's own children
            // all go dark, and four bands of our own take the screen.
            foreach (var child in Children(_panel.transform))
                child.gameObject.SetActive(false);

            StripLayout(_panel);

            Backdrop(ground);

            _header = Field(model, "Header", new Vector2(0.06f, 0.885f), new Vector2(0.94f, 0.975f),
                            1.0f, TextAlignmentOptions.Left);
            _roster = Field(model, "Roster", new Vector2(0.06f, 0.545f), new Vector2(0.94f, 0.870f),
                            0.78f, TextAlignmentOptions.TopLeft);
            _chat = Field(model, "Chat", new Vector2(0.06f, 0.125f), new Vector2(0.94f, 0.530f),
                          0.72f, TextAlignmentOptions.BottomLeft);
            _input = Field(model, "Input", new Vector2(0.06f, 0.030f), new Vector2(0.94f, 0.110f),
                           0.72f, TextAlignmentOptions.Left);

            App.Log.LogInfo($"[Monitor] Multiplayer page installed on {Path(_panel.transform)}");
        }
        catch (Exception ex)
        {
            App.Log.LogError($"[Monitor] Could not build the page: {ex}");
            _panel = null;
        }
    }

    /// <summary>
    /// Puts our page up on the monitor, or takes it back down again.
    ///
    /// Hiding must not switch any of the game's pages on. It used to force the docking readout
    /// back on, so every time the monitor moved to one of the game's own pages the readout was
    /// drawn over the top of it. The game activates and deactivates its own pages perfectly well.
    /// </summary>
    public static void SetVisible(bool visible)
    {
        if (_panel == null) return;

        if (!visible)
        {
            if (_typing) StopTyping();
            _panel.SetActive(false);
            return;
        }

        HoldScreen();
        _panel.SetActive(true);
        Refresh(force: true);
    }

    public static void Tick()
    {
        if (_panel == null || !_panel.activeSelf)
        {
            if (_typing) StopTyping();
            return;
        }

        HoldScreen();
        HandleTyping();
        Refresh(force: false);
    }

    /// <summary>
    /// Everything that has to stay true for as long as our page holds the screen, reasserted
    /// every frame rather than once at the channel change.
    ///
    /// The overlay switcher puts a page up on its own account — the hitching readout appears the
    /// moment a maglock target comes into range, with no channel change at all. And the camera
    /// settings set once in the channel postfix did not survive a single frame.
    /// </summary>
    private static void HoldScreen()
    {
        HideGamePages();
        BlankCamera();
    }

    /// <summary>
    /// The monitor camera, stopped from drawing the world behind our page.
    ///
    /// Narrowing to <c>overlayOnlyMask</c> — the way the game blanks the feed behind its own
    /// pages — is not enough on its own, and the reason is worth keeping: a culling mask hides
    /// objects, and the planet that filled this screen is drawn by the skybox, which a mask does
    /// not touch. The clear flags are what settle it.
    ///
    /// Nothing is saved and put back. An earlier version restored what it had captured when the
    /// page came down, and that was the bug where the core-power page suddenly showed the cab
    /// through it: the game configures this camera for the incoming channel *before* our postfix
    /// runs, so restoring anything afterwards overwrites correct values with stale ones. Leaving
    /// it alone is not neglect — it is the only correct thing to do.
    /// </summary>
    private static void BlankCamera()
    {
        var camera = _switcher != null ? _switcher.monitorCamera : null;
        if (camera == null) return;

        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.cullingMask = _switcher.overlayOnlyMask.value;
    }

    /// <summary>
    /// Every interface page the monitor can draw, taken down. They share the canvas with ours,
    /// and any one of them left up draws through it.
    /// </summary>
    private static void HideGamePages()
    {
        if (_overlays == null) return;

        Hide(_overlays.cameraIdOverlay);
        Hide(_overlays.hitching);
        Hide(_overlays.corePower);
        Hide(_overlays.systemOverview);
        Hide(_overlays.truckStatus);
        Hide(_overlays.lifeSupport);
        Hide(_overlays.trailerStatus);
        Hide(_overlays.dockedStatus);
        Hide(_overlays.multiTowStatus);
    }

    private static void Hide(Component page)
    {
        if (page == null) return;

        var go = page.gameObject;
        if (go != null && go.activeSelf) go.SetActive(false);
    }

    // ---------------------------------------------------------------------------------------
    // Typing
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The chat key opens the line, Enter sends it, Escape abandons it.
    ///
    /// The key is a setting rather than a constant, and the default is Enter: the first version
    /// used Tab, which the game binds itself. Only read while the page is the channel on screen
    /// and the player is in the seat in front of it — anywhere else the key belongs entirely to
    /// the game, and a line typed from the back of the cab would go to a screen nobody is facing.
    /// </summary>
    private static void HandleTyping()
    {
        var key = App.ChatKey.Value;

        if (!_typing)
        {
            if (InSeat() && Input.GetKeyDown(key)) StartTyping();
            return;
        }

        // Getting up ends the line: the monitor is out of reach, and the keys are needed to walk.
        if (!InSeat())
        {
            StopTyping();
            return;
        }

        // The game's own input stays off for as long as the line is open. Asserting it once was
        // not enough — some controls, the handbrake among them, came back on their own.
        TakeGameInput(false);

        // Escape alone abandons the line. The chat key must not close it: rebind chat to a letter
        // and that letter would then be the one letter the player could not type.
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            StopTyping();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            Send(_draft);
            StopTyping();
            return;
        }

        foreach (var c in Input.inputString)
        {
            if (c == '\b')
            {
                if (_draft.Length > 0) _draft = _draft.Substring(0, _draft.Length - 1);
                continue;
            }

            // Enter and Tab arrive here as well as through the key checks above.
            if (char.IsControl(c)) continue;

            if (_draft.Length < MessageLimit) _draft += c;
        }

        // The line has to answer the keyboard now, not at the next half-second refresh.
        DrawInput();
    }

    /// <summary>
    /// Whether the player is in the driver's seat. The game keeps the answer in its own binding;
    /// if that cannot be read the chat stays usable rather than silently locked.
    /// </summary>
    private static bool InSeat()
    {
        try
        {
            var location = PlayerLocation.instance;
            if (location != null && location.playerInSeat != null) return location.playerInSeat.Get();

            var truck = StarTruck.instance;
            if (truck != null && truck.playerInSeatBinding != null) return truck.playerInSeatBinding.Get();

            return true;
        }
        catch (Exception ex)
        {
            if (!_seatWarned)
            {
                _seatWarned = true;
                App.Log.LogWarning($"[Monitor] Could not read whether the player is seated; the chat is not gated. {ex.Message}");
            }

            return true;
        }
    }

    private static bool _seatWarned;

    private static void StartTyping()
    {
        _typing = true;
        _draft = string.Empty;

        TakeGameInput(false);
        DrawInput();
    }

    private static void StopTyping()
    {
        _typing = false;
        _draft = string.Empty;

        TakeGameInput(true);
        DrawInput();
    }

    /// <summary>
    /// The game's own input switch, so that typing does not also drive the truck.
    ///
    /// <c>StarTruckerInput.EnableAllInput</c> is what the game itself uses; nothing here has to
    /// guess at which keys to swallow.
    /// </summary>
    private static void TakeGameInput(bool enabled)
    {
        try
        {
            if (enabled && !_inputTaken) return;
            if (!StarTruckerInput.ready) return;

            var input = StarTruckerInput.Get();
            if (input == null) return;

            // All three, and every frame while the line is open. One call to EnableAllInput left
            // the handbrake live, so the truck still answered the letter B while it was being
            // typed; the game evidently puts some of this back on its own.
            input.EnableAllInput(enabled);
            input.EnablePlayerInput(enabled);
            input.EnableTruckInput(enabled);

            _inputTaken = !enabled;
        }
        catch (Exception ex)
        {
            App.Log.LogError($"[Monitor] Could not {(enabled ? "return" : "take")} game input: {ex.Message}");
            _inputTaken = false;
        }
    }

    private static void Send(string draft)
    {
        var message = draft.Trim();
        if (message.Length == 0) return;

        if (Network.NetId == -1)
        {
            Notice(Strings.Get("monitor.noconnection"));
            return;
        }

        // The server relays the line back to the sender too, so there is nothing to add locally.
        Network.SendServerMessage(new ChatCmd { Message = message, SectorOnly = false }, StarTruckMP.Shared.PacketType.Chat);
    }

    private static void Notice(string text)
    {
        _notice = text;
        _noticeUntil = Time.unscaledTime + 3f;
    }

    // ---------------------------------------------------------------------------------------
    // Drawing
    // ---------------------------------------------------------------------------------------

    private static void Refresh(bool force)
    {
        // A monitor readout does not need to keep pace with the frame rate, but the on-air marks
        // beside the names should light up while the voice is still being heard.
        if (!force && Time.unscaledTime < _nextRefresh) return;
        _nextRefresh = Time.unscaledTime + 0.2f;

        if (_header != null) _header.text = Strings.Get("monitor.title");
        if (_roster != null) _roster.text = BuildRoster();
        if (_chat != null) _chat.text = BuildChat();

        DrawInput();
    }

    private static void DrawInput()
    {
        if (_input == null) return;

        if (_typing)
        {
            var caret = Time.unscaledTime % 1f < 0.5f ? "_" : " ";
            _input.text = "> " + Tail(_draft, 44) + caret;
            return;
        }

        if (!string.IsNullOrEmpty(_notice) && Time.unscaledTime < _noticeUntil)
        {
            _input.text = _notice;
            return;
        }

        _notice = string.Empty;
        _input.text = InSeat()
            ? $"{KeyName(App.ChatKey.Value)} — " + Strings.Get("monitor.type")
            : Strings.Get("monitor.sit");
    }

    private static string BuildRoster()
    {
        if (Network.NetId == -1) return Strings.Get("monitor.noconnection");

        var lines = new StringBuilder();
        lines.Append(Strings.Get("monitor.online")).Append(MultiplayerState.Players.Count + 1).Append('\n');
        lines.Append("> ").Append(Own()).Append('\n');

        if (MultiplayerState.Players.Count == 0)
        {
            lines.Append("  ").Append(Strings.Get("monitor.nobody"));
            return lines.ToString();
        }

        foreach (var player in MultiplayerState.Players)
        {
            // The name in the player's own colour, the one their nameplate carries.
            lines.Append("  <color=")
                 .Append(MultiplayerState.ColorHex(player.NetId))
                 .Append('>')
                 .Append(player.Name)
                 .Append("</color>");

            // Lit while their voice is coming out of the radio.
            if (MultiplayerState.IsSpeaking(player.NetId)) lines.Append(OnAirMark);

            lines.Append("   ")
                 .Append(player.SameSector ? Whereabouts(player.NetId) : PrettySector(player.Sector));

            if (player.Ping >= 0) lines.Append("   ").Append(player.Ping).Append(Strings.Get("monitor.ms"));

            lines.Append('\n');
        }

        return lines.ToString().TrimEnd();
    }

    /// <summary>The on-air marker, beside a name while that player's voice is on the radio.</summary>
    private const string OnAirMark = " ((•))";

    /// <summary>
    /// For a player in our sector: how far their truck is from ours, or "nearby" while their truck
    /// has not been placed yet.
    /// </summary>
    private static string Whereabouts(int netId)
    {
        var truck = Components.NetworkEventsComponent.RemoteTruck(netId);
        var mine = PlayerState.Truck;
        if (truck == null || mine == null) return Strings.Get("monitor.nearby");

        return Components.NameplateComponent.FormatDistance(Vector3.Distance(mine.transform.position, truck.transform.position));
    }

    private static string Own()
    {
        var name = string.IsNullOrWhiteSpace(PlayerState.Name) ? Strings.Get("monitor.you") : PlayerState.Name;
        if (Audio.VoiceInputComponent.Transmitting) name += OnAirMark;

        var line = $"{name}   {PrettySector(PlayerState.Sector)}";

        if (MultiplayerState.OwnPing >= 0)
            line += "   " + MultiplayerState.OwnPing + Strings.Get("monitor.ms");

        return line;
    }

    private static string BuildChat()
    {
        if (MultiplayerState.Chat.Count == 0) return Strings.Get("monitor.nochat");

        var lines = new StringBuilder();
        var from = Math.Max(0, MultiplayerState.Chat.Count - ChatLinesShown);

        for (var i = from; i < MultiplayerState.Chat.Count; i++)
        {
            var line = MultiplayerState.Chat[i];
            lines.Append(line.Name).Append(": ").Append(line.Message).Append('\n');
        }

        return lines.ToString().TrimEnd();
    }

    /// <summary>
    /// The name the game gives a sector, not the id the wire carries.
    ///
    /// A sector arrives as "Sector_02_AtlasPrime", and the number in the middle is part of that
    /// id rather than anything a player needs to read. The game has the real name a keystroke
    /// away: the string table holds "STR_SECTOR_02" — «Атлас-Прайм» — so the number is used to
    /// look the name up and is then thrown away.
    /// </summary>
    private static string PrettySector(string sector)
    {
        if (string.IsNullOrWhiteSpace(sector) || sector == "none") return "—";

        var number = Number(sector);
        if (number != null)
        {
            var name = Lookup("STR_SECTOR_" + number);
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }

        // No name to be had: drop the leading "Sector_" and the number, and space out the rest.
        var parts = sector.Split('_');
        var words = new List<string>();

        foreach (var part in parts)
        {
            if (part.Length == 0) continue;
            if (string.Equals(part, "Sector", StringComparison.OrdinalIgnoreCase)) continue;
            if (IsNumber(part)) continue;
            words.Add(part);
        }

        return words.Count == 0 ? sector : string.Join(" ", words);
    }

    /// <summary>The first run of digits in a sector id, kept as written so "02" stays "02".</summary>
    private static string Number(string sector)
    {
        foreach (var part in sector.Split('_'))
        {
            if (IsNumber(part)) return part;
        }

        return null;
    }

    private static bool IsNumber(string part)
    {
        if (part.Length == 0) return false;

        foreach (var c in part)
        {
            if (!char.IsDigit(c)) return false;
        }

        return true;
    }

    /// <summary>A string from the game's own table, or null when it has none under that id.</summary>
    private static string Lookup(string id)
    {
        try
        {
            if (!StringTable.isReady) return null;
            return StringTable.Contains(id) ? StringTable.Get(id) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>A key as a player would write it: "ENTER" rather than "Return".</summary>
    public static string KeyName(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.Return: return "ENTER";
            case KeyCode.KeypadEnter: return "NUM ENTER";
            case KeyCode.BackQuote: return "~";
            case KeyCode.Backslash: return "\\";
            case KeyCode.Slash: return "/";
            case KeyCode.Semicolon: return ";";
            case KeyCode.Quote: return "'";
            case KeyCode.Comma: return ",";
            case KeyCode.Period: return ".";
            case KeyCode.LeftBracket: return "[";
            case KeyCode.RightBracket: return "]";
            case KeyCode.Minus: return "-";
            case KeyCode.Equals: return "=";
            default: return key.ToString().ToUpperInvariant();
        }
    }

    /// <summary>The end of a line being typed, so the caret never runs off the screen.</summary>
    private static string Tail(string text, int length) =>
        text.Length <= length ? text : text.Substring(text.Length - length);

    // ---------------------------------------------------------------------------------------
    // Building
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// One of our own bands, cloned from a label of the page so that it keeps the monitor's font,
    /// colour and glow, then anchored to a fraction of the screen rather than to whatever box the
    /// docking readout had put it in.
    ///
    /// Wrapping is off on purpose. A band of fixed height and one line per entry cannot grow into
    /// the band above it, however long a name or a message turns out to be.
    /// </summary>
    private static TextMeshProUGUI Field(TextMeshProUGUI model, string name, Vector2 min, Vector2 max,
                                         float scale, TextAlignmentOptions alignment)
    {
        var size = model.fontSize;

        var clone = Object.Instantiate(model.gameObject, _panel.transform);
        clone.name = "StarTruckMP_" + name;
        clone.SetActive(true);

        // A label of the docking page may carry decoration of its own; only the text is wanted.
        foreach (var child in Children(clone.transform))
            Object.DestroyImmediate(child.gameObject);

        StripLayout(clone);

        var text = clone.GetComponent<TextMeshProUGUI>();

        Stretch(text.rectTransform, min, max);

        text.enableAutoSizing = false;
        text.fontSize = size * scale;
        text.alignment = alignment;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Truncate;
        text.raycastTarget = false;
        text.margin = Vector4.zero;
        text.text = string.Empty;

        return text;
    }

    /// <summary>
    /// An opaque ground under the page, built from one of the game's own page backgrounds so that
    /// it sits in the canvas the same way. The material is dropped: the monitor's tint, glow and
    /// scanlines are applied to the whole render texture rather than by the page, so a plain quad
    /// picks them up just the same.
    /// </summary>
    private static void Backdrop(Image model)
    {
        if (model == null)
        {
            App.Log.LogWarning("[Monitor] No page image to build a ground from; " +
                               "the page will rely on the camera alone.");
            return;
        }

        var clone = Object.Instantiate(model.gameObject, _panel.transform);
        clone.name = "StarTruckMP_Backdrop";
        clone.SetActive(true);

        foreach (var child in Children(clone.transform))
            Object.DestroyImmediate(child.gameObject);

        foreach (var text in clone.GetComponents<TextMeshProUGUI>())
            Object.DestroyImmediate(text);

        StripLayout(clone);

        var image = clone.GetComponent<Image>();
        image.sprite = null;
        image.material = null;
        image.color = Color.black;
        image.raycastTarget = false;

        // A page's images are gauges as often as they are backgrounds. Sliced or filled, and with
        // its sprite taken away, one of those draws nothing at all however large it is stretched.
        image.type = Image.Type.Simple;
        image.fillAmount = 1f;
        image.preserveAspect = false;

        Stretch(image.rectTransform, Vector2.zero, Vector2.one);

        // Under our own bands, which are added after it.
        clone.transform.SetAsFirstSibling();
    }

    private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;

        // The model sits at some depth of its own inside the page; ours belong on the glass.
        var position = rect.localPosition;
        rect.localPosition = new Vector3(position.x, position.y, 0f);
    }

    /// <summary>
    /// A layout group or a size fitter would override the anchors set above, and a page of the
    /// game's own interface is laid out by them.
    /// </summary>
    private static void StripLayout(GameObject go)
    {
        foreach (var group in go.GetComponents<LayoutGroup>()) Object.DestroyImmediate(group);
        foreach (var fitter in go.GetComponents<ContentSizeFitter>()) Object.DestroyImmediate(fitter);
        foreach (var element in go.GetComponents<LayoutElement>()) Object.DestroyImmediate(element);
    }

    /// <summary>
    /// The image to build our ground from: the largest opaque one on the core-power page, and
    /// failing that on the docking page.
    ///
    /// Largest and opaque, not simply the first — the first image on one of these pages is as
    /// likely to be a gauge or an icon, and the first attempt at this cloned exactly that.
    /// </summary>
    private static Image Ground(MonitorOverlaySwitcher overlays)
    {
        var page = overlays.corePower != null ? overlays.corePower.gameObject : null;
        var found = page != null ? Largest(page) : null;
        if (found != null) return found;

        return overlays.dockedStatus != null ? Largest(overlays.dockedStatus.gameObject) : null;
    }

    private static Image Largest(GameObject page)
    {
        var best = (Image)null;
        var bestArea = 0f;

        foreach (var image in page.GetComponentsInChildren<Image>(true))
        {
            if (image == null) continue;
            if (image.color.a < 0.9f) continue;

            var size = image.rectTransform.rect.size;
            var area = Mathf.Abs(size.x * size.y);
            if (area <= bestArea) continue;

            best = image;
            bestArea = area;
        }

        return best;
    }

    /// <summary>The page's biggest label, as the model for the monitor's typography.</summary>
    private static TextMeshProUGUI LargestText(GameObject page)
    {
        var best = (TextMeshProUGUI)null;

        foreach (var text in page.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (text == null) continue;
            if (best == null || text.fontSize > best.fontSize) best = text;
        }

        return best;
    }

    /// <summary>The children of a transform, taken before anything is changed under it.</summary>
    private static List<Transform> Children(Transform parent)
    {
        var children = new List<Transform>();

        foreach (var child in parent)
        {
            var t = child.Cast<Transform>();
            if (t != null) children.Add(t);
        }

        return children;
    }

    private static string Path(Transform t)
    {
        var parts = new List<string>();
        while (t != null && parts.Count < 8)
        {
            parts.Insert(0, t.name);
            t = t.parent;
        }

        return string.Join("/", parts);
    }
}
