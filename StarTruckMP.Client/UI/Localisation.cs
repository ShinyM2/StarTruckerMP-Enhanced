using System;

namespace StarTruckMP.Client.UI;

/// <summary>
/// Which language the game is showing.
///
/// The authority is the game's own localisation system: <c>StringTable.language</c> holds the
/// code of the loaded string table ("ru", "en", "pt-br", ...), which is exactly what the player
/// is reading. Sniffing glyphs off a label was the previous answer and it was order-dependent —
/// the pause menu's labels have not been through <see cref="StringTableLookup"/> when its
/// <c>Awake</c> runs, so the sample came back empty and the pause menu demoted the language the
/// main menu had already got right. That is why one entry read "Multiplayer" in a Russian game.
///
/// Asking the font is no guide either: the menu face carries no Cyrillic at all and TMP draws
/// Russian through a fallback.
/// </summary>
internal static class Localisation
{
    private static bool _learnedRussian;
    private static string _logged;

    /// <summary>The game's language code, lower case: "en", "ru", "de", "pt-br", "zh-cn", ...</summary>
    public static string Code
    {
        get
        {
            var fromGame = FromGame();
            if (fromGame != null) return fromGame;
            return _learnedRussian ? "ru" : "en";
        }
    }

    public static bool IsRussian => Code.StartsWith("ru", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Records the language from a sample of the game's own UI text, for the moment before the
    /// string table has said. Only ever promotes to Russian: an inconclusive sample proves nothing
    /// and must never demote what a better sample established.
    /// </summary>
    public static void LearnFrom(string gameText)
    {
        if (_learnedRussian || string.IsNullOrWhiteSpace(gameText)) return;

        foreach (var c in gameText)
        {
            if (c >= 0x0400 && c <= 0x04FF)
            {
                _learnedRussian = true;
                return;
            }
        }
    }

    /// <summary>The loaded string table's language, or null while it is not yet available.</summary>
    private static string FromGame()
    {
        try
        {
            if (!StringTable.isReady) return null;

            var language = StringTable.language;
            if (string.IsNullOrWhiteSpace(language)) return null;

            language = language.Trim().ToLowerInvariant();

            if (_logged != language)
            {
                _logged = language;
                App.Log.LogInfo($"[Localisation] Game language: {language}");
            }

            return language;
        }
        catch
        {
            // The string table lives in the game's assembly; never let a missing member here
            // take a menu patch down with it.
            return null;
        }
    }
}
