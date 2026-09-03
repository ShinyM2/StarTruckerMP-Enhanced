using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace StarTruckMP.Client.UI;

/// <summary>
/// The addresses a host has to hand out. Friends on the internet need the public one, friends on
/// the same network the local one, so both are shown and the player picks whichever applies
/// rather than being told to go and look them up.
/// </summary>
internal static class NetworkAddresses
{
    private static string _public;
    private static bool _lookupRunning;

    private static string _local;
    private static float _localReadAt = float.NegativeInfinity;

    /// <summary>
    /// How long the local address is trusted before the adapters are asked again. Enumerating
    /// them costs tens of milliseconds, and the share row used to do it on every redraw — at
    /// frame rate, in the first build, which is what made hosting from the menu stutter.
    /// </summary>
    private const float LocalCacheSeconds = 30f;

    /// <summary>The address the outside world sees, or null until the lookup lands.</summary>
    public static string Public => _public;

    /// <summary>What to read out to friends. Falls back to the local address until the lookup lands.</summary>
    public static string Share
    {
        get
        {
            var local = Local;

            if (_public == null) return local ?? "…";
            if (local == null) return _public;

            return $"{_public}  ·  {Strings.Get("host.text.local")}: {local}";
        }
    }

    /// <summary>This machine's address on the local network, or null when there is no usable one. Cached.</summary>
    public static string Local
    {
        get
        {
            var now = UnityEngine.Time.unscaledTime;
            if (now - _localReadAt < LocalCacheSeconds) return _local;

            _localReadAt = now;
            _local = ReadLocal();
            return _local;
        }
    }

    private static string ReadLocal()
    {
        {
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    foreach (var address in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;

                        var text = address.Address.ToString();
                        if (text.StartsWith("169.254")) continue; // self-assigned, of no use to anyone

                        return text;
                    }
                }
            }
            catch (Exception ex)
            {
                App.Log.LogWarning($"[Addresses] Could not read the local address: {ex.Message}");
            }

            return null;
        }
    }

    /// <summary>
    /// Looks the public address up once, in the background. Only a remote service can report it:
    /// behind NAT the machine itself has no idea what the world sees.
    /// </summary>
    public static void Refresh()
    {
        if (_public != null || _lookupRunning) return;
        _lookupRunning = true;

        Plugin.StartAttachedThread(() =>
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                var value = http.GetStringAsync("https://api.ipify.org").Result?.Trim();

                if (!string.IsNullOrWhiteSpace(value) && IPAddress.TryParse(value, out _))
                {
                    _public = value;
                    App.Log.LogInfo($"[Addresses] Public address: {_public}");
                }
            }
            catch (Exception ex)
            {
                App.Log.LogWarning($"[Addresses] Public address lookup failed: {ex.Message}");
            }
            finally
            {
                _lookupRunning = false;
            }
        });
    }
}
