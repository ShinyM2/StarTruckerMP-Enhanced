using System;
using System.Collections.Generic;
using System.Text;
using Il2CppInterop.Runtime;
using StarTruckMP.Client.Audio;
using StarTruckMP.Client.Synchronization;
using StarTruckMP.Shared.Dto.Api;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace StarTruckMP.Client.UI;

/// <summary>
/// The multiplayer settings page, built from the main menu's own widgets.
///
/// Cloning the settings screen would have been the obvious route, but the settings UI lives in a
/// scene that is not loaded while the player is in the main menu — at that point the game holds
/// no <c>DisplayScreen</c>, no <c>OptionScreen</c> and not a single settings row, only its menu
/// buttons. So the page is a title plate and a column of cloned <see cref="MenuButton"/>s in the
/// menu's own container, laid out the way the game's settings hub is: pick a section first, then
/// see only what belongs to it. Every word on it comes from <see cref="Strings"/>, in the
/// language the game is showing.
/// </summary>
internal static class MultiplayerScreen
{
    /// <summary>
    /// The first page is for getting onto a server — a Steam friend, an address, a server of your
    /// own — and one entry, Settings, for everything that is not that. The settings page carries
    /// the mod's own switches directly and opens two pages for the two subjects with more than
    /// a switch or two to them: who else you see (<see cref="Page.Display"/>) and how you talk to
    /// them (<see cref="Page.Voice"/>). Every setting is on exactly one page, and a player looking
    /// for one has one place to look.
    /// </summary>
    private enum Page { Root, Host, Player, Settings, Display, Voice, Friends }

    /// <summary>The microphone volumes a click steps through, as multipliers.</summary>
    private static readonly float[] GainSteps = { 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f, 2.5f, 3f };

    /// <summary>The radio volumes a click steps through, as multipliers.</summary>
    private static readonly float[] VolumeSteps = { 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f };

    /// <summary>Amber, for the changeable half of a row at rest.</summary>
    private const string RestingValue = "#EFC806";

    /// <summary>Near-black, for the same value once the row is highlighted amber.</summary>
    private const string SelectedValue = "#2A1F04";

    private static Transform _column;
    private static MenuButton _rowTemplate;
    private static GameObject _titleTemplate;

    private static GameObject _title;
    private static readonly Dictionary<Page, List<GameObject>> _pages = new();
    private static readonly List<GameObject> _hidden = new();

    /// <summary>Rows whose label is fixed, so it can be redrawn when the language changes.</summary>
    private static readonly List<(GameObject Row, string Key)> _labelled = new();

    private static TextField _addressField;
    private static TextField _portField;

    private static GameObject _statusRow;
    private static GameObject _hostStateRow;
    private static GameObject _hostToggleRow;
    private static GameObject _shareRow;
    private static GameObject _copyRow;
    private static float _copiedUntil;
    private static GameObject _nameplatesRow;
    private static GameObject _collisionsRow;
    private static GameObject _ghostRow;
    private static GameObject _chatKeyRow;
    private static GameObject _micRow;
    private static GameObject _micTestRow;
    private static GameObject _micGainRow;
    private static GameObject _denoiseRow;
    private static GameObject _radioVolumeRow;
    private static GameObject _radioEffectRow;
    private static GameObject _muteDialogueRow;
    private static GameObject _nearbyRadiosRow;
    private static GameObject _updatesRow;
    private static GameObject _noPauseRow;
    private static GameObject _overlayRow;
    private static GameObject _versionRow;
    private static GameObject _updateRow;
    private static GameObject _copyAddressRow;
    private static float _addressCopiedUntil;

    // The lobby: the server line, who is online, the invite rows and the friends page.
    private static GameObject _serverRow;
    private static GameObject _onlineRow;

    /// <summary>
    /// The one Steam invite button, on the friends page. It used to sit on three pages at once,
    /// so the same control was met three times; the other two pages now carry a line that opens
    /// the friends page instead.
    /// </summary>
    private static GameObject _friendsInviteRow;
    private static GameObject _friendsHeaderRow;
    private const int FriendRowCount = 6;
    private static readonly GameObject[] _friendRows = new GameObject[FriendRowCount];
    private static string _inviteNotice;
    private static float _inviteNoticeUntil;

    /// <summary>Rows kept off even while their page is open: friend slots with nobody in them.</summary>
    private static readonly HashSet<GameObject> _suppressed = new();

    /// <summary>Whether each of the game's own entries was enabled when the page put it aside.</summary>
    private static readonly Dictionary<GameObject, bool> _hiddenState = new();

    /// <summary>The game's entries just handed back, kept in shape for a moment after the page closes.</summary>
    private static readonly List<GameObject> _restoring = new();

    /// <summary>Each of those entries' own <c>disableOnEnable</c>, to be given back with it.</summary>
    private static readonly Dictionary<GameObject, bool> _restoreFlag = new();

    private static float _restoreUntil;

    /// <summary>Set while the chat-key row is waiting for the player to press its replacement.</summary>
    private static bool _listening;

    private static Page _page = Page.Root;
    private static bool _open;
    private static string _language;

    public static bool IsOpen => _open;

    /// <summary>
    /// Remembers the widgets to clone. Called while the menu is being built, well before the
    /// player can open the page.
    /// </summary>
    public static void Prepare(Transform column, MenuButton rowTemplate)
    {
        _column = column;
        _rowTemplate = rowTemplate;
        _titleTemplate = FindTitlePlate(column);

        // The menu is rebuilt whenever the player returns to it, so the old rows are gone.
        _pages.Clear();
        _hidden.Clear();
        _hiddenState.Clear();
        _suppressed.Clear();
        _labelled.Clear();
        _fields.Clear();
        _typingIn = null;
        _title = null;
        _open = false;
    }

    public static void Show()
    {
        try
        {
            if (_column == null || _rowTemplate == null)
            {
                App.Log.LogWarning("[MP screen] No menu column to build into.");
                return;
            }

            if (_pages.Count == 0) Build();
            if (_pages.Count == 0) return;

            // Put the game's own entries aside rather than destroying them.
            _hidden.Clear();
            _hiddenState.Clear();
            _restoring.Clear();
            _restoreFlag.Clear();
            foreach (var child in _column)
            {
                var t = child.Cast<Transform>();
                if (t == null || IsOurs(t.gameObject)) continue;
                if (!t.gameObject.activeSelf) continue;

                // Remember whether the entry was enabled: it comes back the way it was.
                var entry = t.GetComponent<MenuButton>();
                _hiddenState[t.gameObject] = entry == null || entry.isInteractable;

                _hidden.Add(t.gameObject);
                t.gameObject.SetActive(false);
            }

            if (_title != null) _title.SetActive(true);

            NetworkAddresses.Refresh();
            _open = true;
            Open(Page.Root);
        }
        catch (Exception ex)
        {
            App.Log.LogError($"[MP screen] Show failed: {ex}");
            _open = false;
        }
    }

    public static void Hide()
    {
        foreach (var page in _pages.Values)
        {
            foreach (var row in page)
            {
                if (row != null) row.SetActive(false);
            }
        }

        if (_title != null) _title.SetActive(false);

        _restoring.Clear();
        _restoreFlag.Clear();

        foreach (var go in _hidden)
        {
            if (go == null) continue;

            // An entry carries disableOnEnable, so simply switching it back on drops it into its
            // Disabled animation state with the label faded to nothing — the blank gap the menu
            // came back with. The flag is off for the moment of the switch and goes back on once
            // the entry has settled.
            var button = go.GetComponent<MenuButton>();
            if (button != null)
            {
                _restoreFlag[go] = button.disableOnEnable;
                button.disableOnEnable = false;
            }

            go.SetActive(true);
            Restore(go);
            _restoring.Add(go);
        }

        _restoreUntil = Time.unscaledTime + 1f;

        _hidden.Clear();
        _open = false;
        SetTyping(null);
        VoiceInputComponent.SetTesting(false);
    }

    /// <summary>
    /// The game's own entries, switched back on once the page closes, came up faded: an entry
    /// carries <c>disableOnEnable</c>, so being enabled put it into its Disabled animation state
    /// with the label at nothing, and only the cursor passing over it woke it up again. Each
    /// goes back into the state it had, enabled, or greyed when the game had greyed it.
    /// </summary>
    private static void Restore(GameObject go)
    {
        var button = go.GetComponent<MenuButton>();
        if (button == null) return;

        try
        {
            var enabled = !_hiddenState.TryGetValue(go, out var was) || was;
            button.SetEnabled(enabled);
            button.ForceRefresh();
        }
        catch (Exception ex)
        {
            App.Log.LogWarning($"[MP screen] Could not restore a menu entry: {ex.Message}");
        }
    }

    /// <summary>Copes with the address being changed from outside the page: a Steam invitation, or a friend row.</summary>
    public static void AddressChanged()
    {
        if (_addressField != null) _addressField.Text = App.ServerAddress.Value;
        if (_portField != null) _portField.Text = App.ServerPort.Value;
    }

    /// <summary>How often the live rows are redrawn on their own; a click redraws at once.</summary>
    private const float RefreshSeconds = 0.25f;

    private static float _nextRefresh;

    /// <summary>
    /// Keeps the live rows — connection and hosting state — current while the page is up.
    ///
    /// Not every frame: each redraw hands new text to some forty TextMeshPro labels, and doing
    /// that at frame rate made the page stutter whenever a setting was changed. A quarter of a
    /// second is quick enough for a level meter and a status line.
    /// </summary>
    public static void Tick()
    {
        RestoreTick();
        if (!_open) return;

        if (_listening) Listen();
        HandleFields();

        if (Time.unscaledTime < _nextRefresh) return;
        _nextRefresh = Time.unscaledTime + RefreshSeconds;
        Refresh();
    }

    /// <summary>
    /// For a second after the page closes, the game's own entries are put back into shape every
    /// frame.
    ///
    /// One pass was not enough: the screen's controller runs its own enabling a frame or two
    /// later, and any entry it touched after that single fix stayed invisible — which is how the
    /// menu came back with a blank gap where its buttons are. Each entry's own
    /// <c>disableOnEnable</c> is handed back at the end of the second.
    /// </summary>
    private static void RestoreTick()
    {
        if (_restoring.Count == 0) return;

        var done = Time.unscaledTime >= _restoreUntil;

        foreach (var go in _restoring)
        {
            if (go == null || !go.activeInHierarchy) continue;

            Restore(go);
            if (!done) continue;

            var button = go.GetComponent<MenuButton>();
            if (button != null && _restoreFlag.TryGetValue(go, out var flag)) button.disableOnEnable = flag;
        }

        if (!done) return;

        _restoring.Clear();
        _restoreFlag.Clear();
        _hiddenState.Clear();
    }

    /// <summary>
    /// The next key the player presses becomes the chat key.
    ///
    /// Escape backs out without changing anything, and the mouse buttons are skipped: the row is
    /// reached with a click, and taking that same click as the answer would rebind the key to the
    /// press that opened the row.
    /// </summary>
    private static void Listen()
    {
        if (!Input.anyKeyDown) return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            _listening = false;
            return;
        }

        foreach (var key in Keys())
        {
            if (key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6) continue;
            if (!Input.GetKeyDown(key)) continue;

            App.ChatKey.Value = key;
            _listening = false;
            App.Log.LogInfo($"[MP screen] Chat key set to {key}.");
            return;
        }
    }

    /// <summary>Every key the runtime knows, read once.</summary>
    private static KeyCode[] Keys()
    {
        if (_keys != null) return _keys;

        try
        {
            var values = Enum.GetValues(typeof(KeyCode));
            var keys = new KeyCode[values.Length];
            for (var i = 0; i < values.Length; i++) keys[i] = (KeyCode)values.GetValue(i);
            _keys = keys;
        }
        catch (Exception ex)
        {
            App.Log.LogError($"[MP screen] Could not enumerate the keyboard: {ex.Message}");
            _keys = new[] { KeyCode.Return, KeyCode.T, KeyCode.Y, KeyCode.BackQuote, KeyCode.Backslash };
        }

        return _keys;
    }

    private static KeyCode[] _keys;

    /// <summary>Back out one level, or close the page when already at the top.</summary>
    public static void Back()
    {
        if (_page == Page.Root) Hide();
        else Open(Parent(_page));
    }

    /// <summary>The page a Back press from this one lands on.</summary>
    private static Page Parent(Page page) =>
        page == Page.Display || page == Page.Voice ? Page.Settings : Page.Root;

    #region Pages

    private static void Open(Page page)
    {
        _page = page;

        // Hearing yourself is a thing you do on the radio page and nowhere else.
        if (page != Page.Voice) VoiceInputComponent.SetTesting(false);

        foreach (var entry in _pages)
        {
            var visible = entry.Key == page;
            foreach (var row in entry.Value)
            {
                if (row == null) continue;

                var shown = visible && !_suppressed.Contains(row);
                row.SetActive(shown);
                if (shown) Revive(row.GetComponent<MenuButton>());
            }
        }

        SetTitle(page switch
        {
            Page.Host => Strings.Get("title.host"),
            Page.Player => Strings.Get("title.player"),
            Page.Settings => Strings.Get("title.settings"),
            Page.Display => Strings.Get("title.display"),
            Page.Voice => Strings.Get("title.radio"),
            Page.Friends => Strings.Get("title.friends"),
            _ => Strings.Get("title.multiplayer")
        });

        Refresh();
    }

    private static bool IsOurs(GameObject go)
    {
        if (go == _title) return true;

        foreach (var page in _pages.Values)
        {
            if (page.Contains(go)) return true;
        }

        return false;
    }

    #endregion

    #region Building

    private static void Build()
    {
        BuildTitle();

        // Root: the three ways onto a server first, in the order a player should try them —
        // a Steam friend, an address someone gave you, a server of your own — then one entry
        // for every setting there is, then the lines that only report.
        if (Invites.Available) LabelledRow(Page.Root, "root.friends", () => Open(Page.Friends));
        LabelledRow(Page.Root, "root.player", () => Open(Page.Player));
        LabelledRow(Page.Root, "root.host", () => Open(Page.Host));
        LabelledRow(Page.Root, "root.settings", () => Open(Page.Settings));
        _serverRow = InfoRow(Page.Root);
        _updateRow = InfoRow(Page.Root);
        BackRow(Page.Root, Hide);

        // Connect by address: the fallback for a server no Steam friend of yours is on, so it
        // says as much rather than leaving a player typing numbers they did not need.
        _addressField = TextRow(Page.Player, Strings.Get("player.address"), App.ServerAddress.Value);
        _portField = TextRow(Page.Player, Strings.Get("player.port"), App.ServerPort.Value);
        LabelledRow(Page.Player, "player.connect", Connect);
        _statusRow = InfoRow(Page.Player);
        _onlineRow = InfoRow(Page.Player);
        _copyAddressRow = ActionRow(Page.Player, string.Empty, CopyAddress);
        if (Invites.Available) InfoRow(Page.Player, "player.hint");
        BackRow(Page.Player, () => Open(Page.Root));

        // Host.
        _hostToggleRow = ActionRow(Page.Host, string.Empty, ToggleHost);
        _hostStateRow = InfoRow(Page.Host);
        _shareRow = InfoRow(Page.Host);
        _copyRow = ActionRow(Page.Host, string.Empty, CopyShareText);
        if (Invites.Available) LabelledRow(Page.Host, "root.friends", () => Open(Page.Friends));
        BackRow(Page.Host, () => Open(Page.Root));

        // Friends: the invite dialog, and every friend already on a server as a row to press.
        // The slots exist from the start and are kept off until a friend fills them.
        if (Invites.Available)
        {
            _friendsInviteRow = ActionRow(Page.Friends, string.Empty, InviteViaSteam);
            _friendsHeaderRow = InfoRow(Page.Friends);

            for (var i = 0; i < FriendRowCount; i++)
            {
                var slot = i;
                _friendRows[i] = ActionRow(Page.Friends, string.Empty, () => JoinFriend(slot));
                _suppressed.Add(_friendRows[i]);
            }

            BackRow(Page.Friends, () => Open(Page.Root));
        }

        // Settings: the two subjects with pages of their own first, then every switch that is a
        // single row — the chat key, the mod's own behaviour — then the version, so the whole of
        // what can be set is one page deep and read top to bottom.
        LabelledRow(Page.Settings, "root.display", () => Open(Page.Display));
        LabelledRow(Page.Settings, "root.radio", () => Open(Page.Voice));

        _chatKeyRow = ActionRow(Page.Settings, string.Empty, () =>
        {
            _listening = !_listening;
            Refresh();
        });

        _noPauseRow = ActionRow(Page.Settings, string.Empty, () =>
        {
            App.NoPauseInMultiplayer.Value = !App.NoPauseInMultiplayer.Value;
            Refresh();
        });

        _updatesRow = ActionRow(Page.Settings, string.Empty, () =>
        {
            App.CheckForUpdates.Value = !App.CheckForUpdates.Value;
            Refresh();
        });

        // The overlay is a second browser over the game, and on some machines it is the stutter.
        // Starting and stopping it mid-session is not worth the trouble; it takes at the next start.
        _overlayRow = ActionRow(Page.Settings, string.Empty, () =>
        {
            App.OverlayEnabled.Value = !App.OverlayEnabled.Value;
            Refresh();
        });

        _versionRow = InfoRow(Page.Settings);
        BackRow(Page.Settings, () => Open(Page.Root));

        // Other players: what their trucks do on your screen.
        _nameplatesRow = ActionRow(Page.Display, string.Empty, () =>
        {
            App.ShowNameplates.Value = !App.ShowNameplates.Value;
            Refresh();
        });

        _collisionsRow = ActionRow(Page.Display, string.Empty, () =>
        {
            App.RemoteCollisions.Value = !App.RemoteCollisions.Value;
            Refresh();
        });

        _ghostRow = ActionRow(Page.Display, string.Empty, () =>
        {
            App.GhostMode.Value = !App.GhostMode.Value;
            Refresh();
        });

        BackRow(Page.Display, () => Open(Page.Settings));

        // Radio and microphone: everything about the voice link. Values step round on a click,
        // the way the chat key does, because the menu's rows have no left/right arrows to borrow.
        _micRow = ActionRow(Page.Voice, string.Empty, CycleMicrophone);

        _micTestRow = ActionRow(Page.Voice, string.Empty, () =>
        {
            VoiceInputComponent.SetTesting(!VoiceInputComponent.Testing);
            Refresh();
        });

        _micGainRow = ActionRow(Page.Voice, string.Empty, () =>
        {
            App.MicrophoneGain.Value = NextStep(GainSteps, App.MicrophoneGain.Value);
            Refresh();
        });

        _denoiseRow = ActionRow(Page.Voice, string.Empty, () =>
        {
            App.NoiseSuppression.Value = !App.NoiseSuppression.Value;
            Refresh();
        });

        _radioVolumeRow = ActionRow(Page.Voice, string.Empty, () =>
        {
            App.RadioVolume.Value = NextStep(VolumeSteps, App.RadioVolume.Value);
            Refresh();
        });

        _radioEffectRow = ActionRow(Page.Voice, string.Empty, () =>
        {
            App.RadioEffectStrength.Value = (App.RadioEffectStrength.Value + 1) % 3;
            Refresh();
        });

        _muteDialogueRow = ActionRow(Page.Voice, string.Empty, () =>
        {
            App.MuteRadioDuringDialogue.Value = !App.MuteRadioDuringDialogue.Value;
            Refresh();
        });

        _nearbyRadiosRow = ActionRow(Page.Voice, string.Empty, () =>
        {
            App.HearNearbyRadios.Value = !App.HearNearbyRadios.Value;
            Refresh();
        });

        BackRow(Page.Voice, () => Open(Page.Settings));

        _language = Strings.Language;
        App.Log.LogInfo($"[MP screen] Built {_pages.Count} pages from the menu's own widgets.");
    }

    /// <summary>
    /// The dark plate with amber text that heads every settings screen. The main menu already
    /// carries one — its "STAR TRUCKER" caption — so the page borrows that rather than drawing
    /// an approximation of it.
    /// </summary>
    private static GameObject FindTitlePlate(Transform column)
    {
        foreach (var child in column)
        {
            var t = child.Cast<Transform>();
            if (t == null) continue;

            // A caption, unlike an entry, has text but nothing to press.
            if (t.GetComponent<MenuButton>() != null) continue;
            if (t.GetComponentInChildren<TextMeshProUGUI>(true) == null) continue;

            return t.gameObject;
        }

        return null;
    }

    private static void BuildTitle()
    {
        if (_titleTemplate == null)
        {
            App.Log.LogWarning("[MP screen] No caption plate found in the menu; the page goes without a title.");
            return;
        }

        _title = Object.Instantiate(_titleTemplate, _column);
        _title.name = "StarTruckMP_Title";
        _title.transform.SetAsFirstSibling();
        _title.SetActive(false);

        foreach (var lookup in _title.GetComponentsInChildren<StringTableLookup>(true))
            Object.DestroyImmediate(lookup);

        // The menu's caption plate is sized for the words "STAR TRUCKER" and stays that width
        // whatever it is given, so a short heading left a long empty bar. The game's own section
        // plates hug their text, and a fitter is what makes them do it.
        var fitter = _title.GetComponent<ContentSizeFitter>();
        if (fitter == null) fitter = _title.AddComponent<ContentSizeFitter>();

        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
    }

    private static void SetTitle(string caption)
    {
        if (_title == null) return;

        foreach (var text in _title.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            text.text = caption;

            // A fitter reads the preferred size, and TMP only recalculates that on its own next
            // frame — without this the plate stays at the previous heading's width for a moment.
            text.ForceMeshUpdate();
        }

        var rect = _title.GetComponent<RectTransform>();
        if (rect != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
    }

    /// <summary>A row whose label is one fixed string from the table.</summary>
    private static GameObject LabelledRow(Page page, string key, Action action)
    {
        var row = ActionRow(page, Strings.Get(key), action);
        _labelled.Add((row, key));
        return row;
    }

    /// <summary>The game's own word for Back, so it matches the menus around it.</summary>
    private static GameObject BackRow(Page page, Action action)
    {
        var row = ActionRow(page, Strings.Back, action);
        _labelled.Add((row, "common.back"));
        return row;
    }

    /// <summary>
    /// A row is a clone of a real menu entry, so it behaves and animates like one.
    /// </summary>
    private static GameObject ActionRow(Page page, string label, Action action)
    {
        var row = Object.Instantiate(_rowTemplate.gameObject, _column);
        row.SetActive(false);

        if (!_pages.TryGetValue(page, out var rows))
        {
            rows = new List<GameObject>();
            _pages[page] = rows;
        }

        row.name = $"StarTruckMP_{page}_{rows.Count}";
        rows.Add(row);

        // Every label carries a localisation binding that would put the original text back.
        foreach (var lookup in row.GetComponentsInChildren<StringTableLookup>(true))
            Object.DestroyImmediate(lookup);

        SetRowText(row, label);

        var button = row.GetComponent<MenuButton>();
        if (button != null)
        {
            var click = button.m_OnClick;

            // The clone inherits the original entry's baked-in action, which has to go.
            for (var i = 0; i < click.GetPersistentEventCount(); i++)
                click.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);

            click.RemoveAllListeners();
            if (action != null) click.AddListener(new Action(action));

            Revive(button);
        }

        return row;
    }

    /// <summary>
    /// Brings a cloned entry back to its normal look.
    ///
    /// A menu entry carries an animator with a "disabled" state that fades its resting label
    /// out, and a flag that puts it into that state every time it is enabled. Our rows are
    /// switched on and off as pages open, so left alone they came back invisible until the
    /// cursor passed over them and the hover animation woke them up.
    /// </summary>
    private static void Revive(MenuButton button)
    {
        if (button == null) return;

        try
        {
            button.disableOnEnable = false;
            button.isInteractable = true;
            button.SetEnabled(true);
            button.ForceRefresh();
        }
        catch (Exception ex)
        {
            App.Log.LogWarning($"[MP screen] Could not reset a menu row's state: {ex.Message}");
        }
    }

    /// <summary>A read-only line whose words are fixed, so it follows a language change too.</summary>
    private static GameObject InfoRow(Page page, string key)
    {
        var row = InfoRow(page);
        SetRowText(row, Strings.Get(key));
        _labelled.Add((row, key));
        return row;
    }

    /// <summary>
    /// A read-only line. Marking the button non-interactable is not enough — it still lights up
    /// under the cursor — so the button component goes entirely and the row stops taking clicks.
    /// </summary>
    private static GameObject InfoRow(Page page)
    {
        var row = ActionRow(page, string.Empty, null);

        foreach (var button in row.GetComponents<MenuButton>())
            Object.DestroyImmediate(button);

        // The entry's animator keeps driving the label's alpha after the button is gone, and
        // with nothing to tell it the row is enabled it fades the text out entirely — which is
        // how the status line came to show nothing at all. A read-only line needs no animation.
        foreach (var animator in row.GetComponentsInChildren<Animator>(true))
            Object.DestroyImmediate(animator);

        // Without this the row still swallows the pointer and shows a hover state.
        foreach (var graphic in row.GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;

        // The highlight plate would otherwise flash behind a line nobody can press.
        foreach (var image in row.GetComponentsInChildren<Image>(true))
            image.enabled = false;

        foreach (var text in row.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            text.color = new Color(text.color.r, text.color.g, text.color.b, 0.6f);
            text.alpha = 0.6f;
            text.fontSize *= 0.78f;

            // The entry keeps a second label for its highlighted state, which the animator used
            // to swap in. With the animator gone that one would sit on top of the first forever.
            if (text.name.Contains("_On_")) text.gameObject.SetActive(false);
        }

        return row;
    }

    /// <summary>
    /// Writes a label into every state of a row.
    ///
    /// A menu entry holds two — <c>Option_Off_Label</c> for the resting state and
    /// <c>Option_On_Label</c> for the highlighted one. Filling only the first left the original
    /// text showing the moment the row was selected, which is why every row of this page used to
    /// announce itself as "Options" under the cursor.
    /// </summary>
    private static void SetRowText(GameObject row, string label)
    {
        foreach (var text in row.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            // Assigning the same string still rebuilds the mesh; skip it.
            if (text.text != label) text.text = label;
        }
    }

    /// <summary>
    /// A row whose value is coloured for the state it is drawn in: amber while resting, and near
    /// black once the row itself turns amber under the cursor, where amber on amber vanished.
    /// </summary>
    private static void SetRowValue(GameObject row, string prefix, string value)
    {
        foreach (var text in row.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            var colour = text.name.Contains("_On_") ? SelectedValue : RestingValue;
            var label = $"{prefix}<color={colour}>{value}</color>";
            if (text.text != label) text.text = label;
        }
    }

    /// <summary>
    /// A row the player types into. The game's menus have no text field anywhere, so this one is
    /// built by hand — but it is dressed in the cloned row's own font, size and colour, and sits
    /// in the same slot, so it reads as part of the list.
    /// </summary>
    /// <summary>
    /// A row the player types into.
    ///
    /// The game's menus have no text field anywhere, and Unity's own input field cannot be made
    /// to work in one: the screen's controller hands the selection to a menu entry every frame,
    /// and an input field only hears the keyboard while it is the selected object — so it took one
    /// character and went deaf, and paste never landed. The box is therefore drawn by hand and
    /// the keys are read by hand in <see cref="HandleFields"/>, which needs nobody's permission.
    /// It is still dressed in the cloned row's font, size and colour, so it reads as part of the list.
    /// </summary>
    private static TextField TextRow(Page page, string label, string value)
    {
        var row = ActionRow(page, label, null);

        // The entry's button and animator go: the row must neither take the selection nor
        // animate its label. The template may have been the highlighted entry when it was cloned
        // — in the pause menu it always is — so its lit plate and "on" label come along too.
        foreach (var button in row.GetComponents<MenuButton>())
            Object.DestroyImmediate(button);

        foreach (var animator in row.GetComponentsInChildren<Animator>(true))
            Object.DestroyImmediate(animator);

        foreach (var image in row.GetComponentsInChildren<Image>(true))
            image.enabled = false;

        foreach (var text in row.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (text.name.Contains("_On_"))
            {
                text.gameObject.SetActive(false);
                continue;
            }

            text.alpha = 1f;
        }

        var style = FirstLabel(row);
        if (style == null) return null;

        var holder = new GameObject("StarTruckMP_Input", new Il2CppSystem.Type[]
        {
            Il2CppType.Of<RectTransform>(),
            Il2CppType.Of<Image>()
        });

        holder.transform.SetParent(row.transform, false);

        // Placed just past where the label actually ends, measured rather than guessed: a fixed
        // fraction of the row put the box on top of the longer labels.
        var labelWidth = style.GetPreferredValues(label).x;

        var rect = holder.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.offsetMin = new Vector2(labelWidth + 40f, 3f);
        rect.offsetMax = new Vector2(-8f, -3f);

        var frame = holder.GetComponent<Image>();
        frame.color = FieldIdle;
        frame.raycastTarget = false;

        var textGo = new GameObject("Text", new Il2CppSystem.Type[]
        {
            Il2CppType.Of<RectTransform>(),
            Il2CppType.Of<TextMeshProUGUI>()
        });
        textGo.transform.SetParent(holder.transform, false);
        Stretch(textGo.GetComponent<RectTransform>(), 8f);

        var display = textGo.GetComponent<TextMeshProUGUI>();
        display.font = style.font;
        display.fontSize = style.fontSize * 0.85f;
        display.color = new Color(0.93f, 0.91f, 0.87f, 1f);
        display.alignment = TextAlignmentOptions.MidlineLeft;
        display.enableAutoSizing = false;
        display.enableWordWrapping = false;
        display.overflowMode = TextOverflowModes.Truncate;
        display.raycastTarget = false;

        var field = new TextField { Box = rect, Frame = frame, Display = display, Value = value ?? string.Empty };
        _fields.Add(field);
        field.Draw(false);
        return field;
    }

    /// <summary>A hand-drawn text box: its value, the frame that lights up while it has the keyboard, and the label that shows the value.</summary>
    private sealed class TextField
    {
        public RectTransform Box;
        public Image Frame;
        public TextMeshProUGUI Display;
        public string Value = string.Empty;

        public string Text
        {
            get => Value;
            set
            {
                Value = value ?? string.Empty;
                Draw(_typingIn == this);
            }
        }

        public void Draw(bool active)
        {
            if (Display == null || Frame == null) return;

            var caret = active ? (Time.unscaledTime % 1f < 0.5f ? "_" : " ") : string.Empty;
            var shown = Value + caret;
            if (Display.text != shown) Display.text = shown;

            Frame.color = active ? FieldActive : FieldIdle;
        }
    }

    private static readonly Color FieldIdle = new(0f, 0f, 0f, 0.35f);
    private static readonly Color FieldActive = new(0.94f, 0.78f, 0.02f, 0.18f);
    private static readonly List<TextField> _fields = new();
    private static TextField _typingIn;
    private const int FieldLimit = 64;

    /// <summary>True while a text box has the keyboard, so Escape closes the box rather than the page.</summary>
    public static bool IsTyping => _typingIn != null;

    /// <summary>
    /// Runs every frame the page is up. A click lands the keyboard in the box under the cursor (or
    /// takes it away); while a box has it, the typed characters go into the value, Backspace takes
    /// one off, Ctrl+V pastes the clipboard, and Enter, Tab or Escape give the keyboard back.
    /// </summary>
    private static void HandleFields()
    {
        if (Input.GetMouseButtonDown(0))
        {
            TextField hit = null;
            foreach (var field in _fields)
            {
                if (field.Box == null || !field.Box.gameObject.activeInHierarchy) continue;
                if (!Under(field.Box, Input.mousePosition)) continue;

                hit = field;
                break;
            }

            SetTyping(hit);
        }

        var typing = _typingIn;
        if (typing == null) return;

        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Return) ||
            Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Tab))
        {
            SetTyping(null);
            return;
        }

        var control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        if (control)
        {
            if (Input.GetKeyDown(KeyCode.V)) Paste(typing);
        }
        else
        {
            foreach (var c in Input.inputString)
            {
                if (c == '\b')
                {
                    if (typing.Value.Length > 0) typing.Value = typing.Value.Substring(0, typing.Value.Length - 1);
                    continue;
                }

                // Addresses and ports have no spaces; Enter and Tab arrive here as well as above.
                if (char.IsControl(c) || char.IsWhiteSpace(c)) continue;
                if (typing.Value.Length < FieldLimit) typing.Value += c;
            }
        }

        typing.Draw(true);
    }

    private static void Paste(TextField field)
    {
        string clipboard;
        try { clipboard = GUIUtility.systemCopyBuffer; }
        catch (Exception ex)
        {
            App.Log.LogWarning($"[MP screen] Clipboard read failed: {ex.Message}");
            return;
        }

        if (string.IsNullOrWhiteSpace(clipboard)) return;

        var value = new StringBuilder(field.Value);
        foreach (var c in clipboard.Trim())
        {
            if (char.IsControl(c) || char.IsWhiteSpace(c)) continue;
            if (value.Length >= FieldLimit) break;
            value.Append(c);
        }

        field.Value = value.ToString();
    }

    private static void SetTyping(TextField field)
    {
        if (_typingIn == field) return;

        _typingIn?.Draw(false);
        _typingIn = field;

        if (field == null) return;

        // Nothing else may hold the selection while the keyboard is read here, or the entry the
        // cursor last touched would answer Enter and the arrows as well.
        try { EventSystem.current?.SetSelectedGameObject(null); }
        catch (Exception ex) { App.Log.LogWarning($"[MP screen] Could not clear the UI selection: {ex.Message}"); }

        field.Draw(true);
    }

    /// <summary>Whether a screen point is over a rectangle of the menu canvas, whichever way that canvas is rendered.</summary>
    private static bool Under(RectTransform rect, Vector3 screenPoint)
    {
        Camera camera = null;
        var canvas = rect.GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            var root = canvas.rootCanvas;
            if (root != null && root.renderMode != RenderMode.ScreenSpaceOverlay) camera = root.worldCamera;
        }

        return RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, camera);
    }

    private static TextMeshProUGUI FirstLabel(GameObject row)
    {
        foreach (var text in row.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (text != null) return text;
        }

        return null;
    }

    private static void Stretch(RectTransform rect, float padding)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(padding, padding);
        rect.offsetMax = new Vector2(-padding, -padding);
    }

    #endregion

    #region State

    private static void Refresh()
    {
        HostControl.Poll();

        // The game's screen controller disables entries it does not know about whenever it
        // reshuffles its own — a popup, a page change — and only its own get re-enabled. Ours are
        // brought back a few times a second rather than once, so a row cannot stay faded.
        if (_pages.TryGetValue(_page, out var visibleRows))
        {
            foreach (var row in visibleRows)
            {
                if (row != null) Revive(row.GetComponent<MenuButton>());
            }
        }

        // The game can switch language while the menu is up; the fixed labels follow it.
        if (_language != Strings.Language)
        {
            _language = Strings.Language;
            foreach (var (row, key) in _labelled)
            {
                if (row != null) SetRowText(row, key == "common.back" ? Strings.Back : Strings.Get(key));
            }
        }

        if (_statusRow != null) SetRowText(_statusRow, StatusLine());

        if (_copyAddressRow != null)
        {
            SetRowText(_copyAddressRow, Time.unscaledTime < _addressCopiedUntil
                ? Strings.Get("host.copied")
                : Strings.Get("player.copy"));
        }

        if (_updateRow != null)
        {
            var available = UpdateCheck.Available;
            var show = available != null;
            if (_updateRow.activeSelf != show && _page == Page.Root) _updateRow.SetActive(show);
            if (show) SetRowText(_updateRow, Strings.Get("update.available", available));
        }

        if (_updatesRow != null)
            SetRowValue(_updatesRow, Strings.Get("mod.updates"), OnOff(App.CheckForUpdates.Value));

        if (_noPauseRow != null)
            SetRowValue(_noPauseRow, Strings.Get("mod.nopause"), OnOff(App.NoPauseInMultiplayer.Value));

        if (_overlayRow != null)
        {
            // Says so when the switch and the running state disagree: the change waits for a restart.
            var overlay = OnOff(App.OverlayEnabled.Value);
            if (App.OverlayEnabled.Value != OverlayManager.Enabled) overlay += Strings.Get("mod.overlay.restart");
            SetRowValue(_overlayRow, Strings.Get("mod.overlay"), overlay);
        }

        if (_versionRow != null)
            SetRowText(_versionRow, Strings.Get("mod.version") + PluginInfo.PLUGIN_VERSION);

        if (_hostToggleRow != null)
        {
            SetRowValue(_hostToggleRow, Strings.Get("host.server"), HostControl.IsHosting
                ? Strings.Get("host.stop")
                : Strings.Get("host.start"));
        }

        if (_hostStateRow != null)
        {
            // A failure explains itself here rather than leaving the row to quietly flip back.
            SetRowText(_hostStateRow, HostControl.LastMessage
                                      ?? (HostControl.IsHosting
                                          ? Strings.Get("host.running")
                                          : Strings.Get("host.notrunning")));
        }

        if (_shareRow != null)
        {
            SetRowText(_shareRow, HostControl.IsHosting
                ? Strings.Get("host.share", NetworkAddresses.Share, App.ServerPort.Value)
                : Strings.Get("host.share.hint"));
        }

        if (_copyRow != null)
        {
            SetRowText(_copyRow, Time.unscaledTime < _copiedUntil
                ? Strings.Get("host.copied")
                : Strings.Get("host.copy"));
        }

        if (_nameplatesRow != null)
            SetRowValue(_nameplatesRow, Strings.Get("display.nameplates"), OnOff(App.ShowNameplates.Value));

        if (_collisionsRow != null)
            SetRowValue(_collisionsRow, Strings.Get("display.collisions"), OnOff(App.RemoteCollisions.Value));

        if (_ghostRow != null)
            SetRowValue(_ghostRow, Strings.Get("display.ghost"), OnOff(App.GhostMode.Value));

        if (_chatKeyRow != null)
            SetRowValue(_chatKeyRow, Strings.Get("comms.chatkey"),
                _listening
                    ? Strings.Get("comms.presskey")
                    : MonitorPanel.KeyName(App.ChatKey.Value));

        RefreshLobby();
        RefreshVoice();
    }

    /// <summary>
    /// What the connection is doing, in words a player can act on.
    ///
    /// "Not connected" alone was the whole story before, and it read as "the button did nothing":
    /// the mod authenticates from the main menu but only joins the game server once a save is
    /// loaded, so the honest states are that the server cannot be reached, that it answered and
    /// the join waits for a save, that the join is in progress, or that it is done.
    /// </summary>
    private static string StatusLine()
    {
        if (Network.NetId != -1)
            return Strings.Get("player.status.connected", PlayerState.Name);

        var where = $"{App.ServerAddress.Value}:{App.ServerPort.Value}";

        if (!string.IsNullOrEmpty(Network.AuthProblem))
            return Strings.Get("player.status.unreachable", where);

        if (string.IsNullOrEmpty(PlayerState.Token))
            return Strings.Get("player.status.signingin", where);

        return Network.InWorld
            ? Strings.Get("player.status.connecting", where)
            : Strings.Get("player.status.waitsave");
    }

    /// <summary>
    /// The lobby rows: the server line on the root page, who is online on the player page, the
    /// invite rows, and the friends page. The server is asked every few seconds while any page
    /// is up, so a player in the main menu sees the server answer and their friends on it before
    /// loading a save.
    /// </summary>
    private static void RefreshLobby()
    {
        ServerStatus.Poll();

        if (_serverRow != null) SetRowText(_serverRow, ServerLine());
        if (_onlineRow != null) SetRowText(_onlineRow, OnlineLine());

        if (_friendsInviteRow != null) SetRowText(_friendsInviteRow, InviteLabel());

        if (_page == Page.Friends) RefreshFriends();
    }

    private static string InviteLabel()
    {
        if (_inviteNotice != null && Time.unscaledTime < _inviteNoticeUntil) return _inviteNotice;
        return Invites.CanInvite ? Strings.Get("invite.steam") : Strings.Get("invite.noaddress");
    }

    /// <summary>An older server answers the sign-in but has no status page; that is not "down".</summary>
    private static bool ServerIsOlder =>
        !ServerStatus.Checking && ServerStatus.Error != null && ServerStatus.Error.StartsWith("HTTP", StringComparison.Ordinal);

    /// <summary>One line about the server the menu is pointed at, for the root page.</summary>
    private static string ServerLine()
    {
        var target = ServerStatus.Target;

        // Fresh from a Steam invitation and not in the world yet: say what to do next.
        if (Invites.LastJoin != null && !Network.InWorld && Time.unscaledTime - Invites.LastJoinAt < 60f)
            return Strings.Get("join.notice", Invites.LastJoin);

        if (ServerStatus.Checking) return Strings.Get("server.checking", target);

        var status = ServerStatus.Result;
        if (status == null)
            return Strings.Get(ServerIsOlder ? "server.old" : "server.down", target);

        return status.Players == 0
            ? Strings.Get("server.empty", target)
            : Strings.Get("server.online", target, status.Players, Names(status));
    }

    /// <summary>Who is on the server, for the player page.</summary>
    private static string OnlineLine()
    {
        var status = ServerStatus.Result;
        if (status == null)
            return Strings.Get(ServerIsOlder ? "player.online.old" : "player.online.unknown");

        return status.Players == 0
            ? Strings.Get("player.online.none")
            : Strings.Get("player.online", status.Players, Names(status));
    }

    /// <summary>The first few names, and how many more there are.</summary>
    private static string Names(ServerStatusDto status)
    {
        const int shown = 5;
        var names = new List<string>();
        foreach (var p in status.Names)
        {
            if (names.Count == shown) break;
            names.Add(p.Name);
        }

        var text = string.Join(", ", names);
        var more = status.Players - names.Count;
        return more > 0 ? $"{text} +{more}" : text;
    }

    /// <summary>Fills the friend slots from Steam and shows exactly as many rows as there are friends on servers.</summary>
    private static void RefreshFriends()
    {
        Invites.RefreshFriends();
        var friends = Invites.Friends;

        if (_friendsHeaderRow != null)
        {
            SetRowText(_friendsHeaderRow, !Invites.SteamReady
                ? Strings.Get("friends.waiting")
                : friends.Count == 0 ? Strings.Get("friends.none") : Strings.Get("friends.header"));
        }

        for (var i = 0; i < FriendRowCount; i++)
        {
            var row = _friendRows[i];
            if (row == null) continue;

            if (i < friends.Count)
            {
                SetRowText(row, Strings.Get("friends.join", friends[i].Name, friends[i].Connect));
                if (_suppressed.Remove(row))
                {
                    row.SetActive(true);
                    Revive(row.GetComponent<MenuButton>());
                }
            }
            else if (_suppressed.Add(row))
            {
                row.SetActive(false);
            }
        }
    }

    private static void InviteViaSteam()
    {
        var possible = Invites.CanInvite;
        var opened = possible && Invites.OpenSteamDialog();

        _inviteNotice = opened
            ? Strings.Get("invite.opened")
            : Strings.Get(possible ? "invite.failed" : "invite.noaddress");
        _inviteNoticeUntil = Time.unscaledTime + 4f;
        Refresh();
    }

    private static void JoinFriend(int slot)
    {
        var friends = Invites.Friends;
        if (slot >= friends.Count) return;

        Invites.Join(friends[slot].Connect, $"friend {friends[slot].Name}", false);
        Open(Page.Player);
    }

    private static void RefreshVoice()
    {
        if (_micRow != null)
            SetRowValue(_micRow, Strings.Get("voice.microphone"), MicrophoneName());

        if (_micTestRow != null)
        {
            // The microphone is only open on a server or during the test, so "no microphone" is
            // only known, and only said, once the test has asked for one and got none.
            if (VoiceInputComponent.Testing && VoiceInputComponent.MicrophoneRunning)
                SetRowValue(_micTestRow, Strings.Get("voice.testing"), LevelBar(VoiceInputComponent.InputLevel) + Strings.Get("voice.test.stop"));
            else if (VoiceInputComponent.Testing)
                SetRowValue(_micTestRow, Strings.Get("voice.microphone"), Strings.Get("voice.test.nomic") + Strings.Get("voice.test.stop"));
            else
                SetRowText(_micTestRow, Strings.Get("voice.test"));
        }

        if (_micGainRow != null)
            SetRowValue(_micGainRow, Strings.Get("voice.micvolume"), Percent(App.MicrophoneGain.Value));

        if (_denoiseRow != null)
        {
            SetRowValue(_denoiseRow, Strings.Get("voice.denoise"),
                VoiceInputComponent.DenoiserAvailable || !App.NoiseSuppression.Value
                    ? OnOff(App.NoiseSuppression.Value)
                    : Strings.Get("voice.unavailable"));
        }

        if (_radioVolumeRow != null)
            SetRowValue(_radioVolumeRow, Strings.Get("voice.radiovolume"), Percent(App.RadioVolume.Value));

        if (_radioEffectRow != null)
        {
            var name = RadioVoiceEffectProcessor.Current switch
            {
                RadioVoiceEffectProcessor.Strength.Off => Strings.Get("voice.effect.off"),
                RadioVoiceEffectProcessor.Strength.Light => Strings.Get("voice.effect.light"),
                _ => Strings.Get("voice.effect.full")
            };

            SetRowValue(_radioEffectRow, Strings.Get("voice.effect"), name);
        }

        if (_muteDialogueRow != null)
            SetRowValue(_muteDialogueRow, Strings.Get("voice.mutedialogue"), OnOff(App.MuteRadioDuringDialogue.Value));

        if (_nearbyRadiosRow != null)
            SetRowValue(_nearbyRadiosRow, Strings.Get("voice.trucks"), OnOff(App.HearNearbyRadios.Value));
    }

    /// <summary>The address as typed, for passing on to a friend who should join the same server.</summary>
    private static void CopyAddress()
    {
        var address = _addressField != null && !string.IsNullOrWhiteSpace(_addressField.Text) ? _addressField.Text.Trim() : App.ServerAddress.Value;
        var port = _portField != null && !string.IsNullOrWhiteSpace(_portField.Text) ? _portField.Text.Trim() : App.ServerPort.Value;

        try
        {
            GUIUtility.systemCopyBuffer = $"{address}:{port}";
            _addressCopiedUntil = Time.unscaledTime + 3f;
        }
        catch (Exception ex)
        {
            App.Log.LogWarning($"[MP screen] Clipboard copy failed: {ex.Message}");
        }

        Refresh();
    }

    /// <summary>
    /// Steps to the next microphone: automatic choice first, then every device Windows lists.
    /// The change is applied at once so the test row can confirm it.
    /// </summary>
    private static void CycleMicrophone()
    {
        var devices = VoiceInputComponent.Devices();
        var current = App.MicrophoneDeviceName.Value ?? VoiceInputComponent.AutoDevice;

        var index = Array.FindIndex(devices, d => string.Equals(d, current, StringComparison.OrdinalIgnoreCase));
        var next = index + 1 < devices.Length ? devices[index + 1] : VoiceInputComponent.AutoDevice;

        App.Log.LogInfo($"[MP screen] Microphone set to '{(next.Length == 0 ? "auto" : next)}'.");
        VoiceInputComponent.SelectDevice(next);
        Refresh();
    }

    private static string MicrophoneName()
    {
        var configured = App.MicrophoneDeviceName.Value;
        if (string.IsNullOrWhiteSpace(configured))
        {
            var actual = VoiceInputComponent.DeviceLabel;
            return actual == null || actual.StartsWith("<")
                ? Strings.Get("voice.auto")
                : Strings.Get("voice.auto") + $" ({Shorten(actual, 26)})";
        }

        return Shorten(configured, 34);
    }

    private static string Shorten(string text, int length) =>
        text.Length <= length ? text : text.Substring(0, length - 1) + "…";

    /// <summary>
    /// Twelve cells, filled to the level; the top cell needs a genuinely loud signal.
    ///
    /// Plain bars rather than block glyphs: the menu font has no ▮ or ▯, and every character it
    /// lacks costs a warning in the log on every redraw. The empty cells are the same bar at a
    /// quarter of the alpha, which TMP does inline.
    /// </summary>
    private static string LevelBar(float level)
    {
        const int cells = 12;
        var filled = Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(Mathf.Clamp01(level)) * cells), 0, cells);
        return new string('|', filled) + "<alpha=#50>" + new string('|', cells - filled) + "<alpha=#FF>";
    }

    private static string Percent(float multiplier) => $"{Mathf.RoundToInt(multiplier * 100f)}%";

    /// <summary>The step after the current value, wrapping to the first; an unlisted value goes to the nearest.</summary>
    private static float NextStep(float[] steps, float current)
    {
        var index = 0;
        var best = float.MaxValue;
        for (var i = 0; i < steps.Length; i++)
        {
            var distance = Mathf.Abs(steps[i] - current);
            if (distance < best)
            {
                best = distance;
                index = i;
            }
        }

        return steps[(index + 1) % steps.Length];
    }

    /// <summary>
    /// Puts everything a friend needs on the clipboard in one go — address, port, and the fact
    /// that both protocols matter — so the host can paste it into a chat instead of reading
    /// numbers off the screen.
    /// </summary>
    private static void CopyShareText()
    {
        var address = NetworkAddresses.Public ?? NetworkAddresses.Local ?? "?";
        var port = App.ServerPort.Value;

        var text = $"{Strings.Get("host.text.title")}\n{Strings.Get("host.text.address")}: {address}\n{Strings.Get("host.text.port", port)}";

        var local = NetworkAddresses.Local;
        if (NetworkAddresses.Public != null && local != null)
            text += $"\n{Strings.Get("host.text.local")}: {local}";

        try
        {
            GUIUtility.systemCopyBuffer = text;
            _copiedUntil = Time.unscaledTime + 3f;
            App.Log.LogInfo("[MP screen] Share details copied to the clipboard.");
        }
        catch (Exception ex)
        {
            App.Log.LogWarning($"[MP screen] Clipboard copy failed: {ex.Message}");
        }

        Refresh();
    }

    private static string OnOff(bool value) => value ? Strings.On : Strings.Off;

    private static void Connect()
    {
        var address = _addressField != null && !string.IsNullOrWhiteSpace(_addressField.Text) ? _addressField.Text : App.ServerAddress.Value;
        var port = _portField != null && !string.IsNullOrWhiteSpace(_portField.Text) ? _portField.Text : App.ServerPort.Value;

        App.Log.LogInfo($"[MP screen] Connecting to {address.Trim()}:{port.Trim()}");
        Network.SwitchServer(address, port);
        ServerStatus.Reset();
        Refresh();
    }

    private static void ToggleHost()
    {
        if (HostControl.IsHosting)
        {
            HostControl.Stop();
        }
        else
        {
            HostControl.Start();
            NetworkAddresses.Refresh();

            // A host plays on their own machine.
            if (HostControl.IsHosting && App.ServerAddress.Value != "127.0.0.1")
            {
                Network.SwitchServer("127.0.0.1", App.ServerPort.Value);
                ServerStatus.Reset();
                if (_addressField != null) _addressField.Text = "127.0.0.1";
            }
        }

        Refresh();
    }

    #endregion
}
