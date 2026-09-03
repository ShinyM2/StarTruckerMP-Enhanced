using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace StarTruckMP.Client.UI;

/// <summary>
/// Asks GitHub once, at startup, whether a newer release exists.
///
/// Players sit on old builds and report bugs that were fixed a version ago; one line in the
/// menu saying "1.3.1 is out" is the cheapest fix for that. Off with <c>CheckForUpdates</c>.
/// </summary>
internal static class UpdateCheck
{
    private const string Api = "https://api.github.com/repos/ShinyM2/StarTruckerMP-Enhanced/releases/latest";

    public const string ReleasesUrl = "https://github.com/ShinyM2/StarTruckerMP-Enhanced/releases";

    /// <summary>The newest version on GitHub when it is newer than this build, otherwise null.</summary>
    public static string Available { get; private set; }

    public static void Start()
    {
        if (App.CheckForUpdates?.Value == false) return;
        Plugin.StartAttachedThread(Run);
    }

    private static void Run()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("StarTruckMP", PluginInfo.PLUGIN_VERSION));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            var json = http.GetStringAsync(Api).Result;
            using var document = JsonDocument.Parse(json);

            var tag = document.RootElement.GetProperty("tag_name").GetString() ?? string.Empty;
            var latest = tag.TrimStart('v', 'V');

            if (!Version.TryParse(latest, out var remote) || !Version.TryParse(PluginInfo.PLUGIN_VERSION, out var local))
            {
                App.Log.LogInfo($"[Update] Could not read versions ('{tag}' vs '{PluginInfo.PLUGIN_VERSION}').");
                return;
            }

            if (remote > local)
            {
                Available = latest;
                App.Log.LogInfo($"[Update] Version {latest} is available (this is {PluginInfo.PLUGIN_VERSION}).");
            }
            else
            {
                App.Log.LogInfo($"[Update] Up to date ({PluginInfo.PLUGIN_VERSION}; latest release {latest}).");
            }
        }
        catch (Exception ex)
        {
            // No network, GitHub down, rate-limited: none of it is the player's problem.
            App.Log.LogInfo($"[Update] Check skipped: {(ex.InnerException ?? ex).Message}");
        }
    }
}
