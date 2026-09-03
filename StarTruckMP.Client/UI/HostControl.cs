using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace StarTruckMP.Client.UI;

/// <summary>
/// Runs the dedicated server that ships next to the plugin, so hosting is a button rather than a
/// separate download and a second window. Shared by the in-game page and the overlay menu.
/// </summary>
internal static class HostControl
{
    private static Process _process;

    public static bool IsHosting => _process is { HasExited: false };

    /// <summary>What last happened, in words the player can act on. Null when nothing to report.</summary>
    public static string LastMessage { get; private set; }

    /// <summary>
    /// Notices a server that died on its own. Without this the row simply flipped back to
    /// "start" a few seconds after being pressed, with no hint that anything had gone wrong.
    /// </summary>
    public static void Poll()
    {
        if (_process == null || !_process.HasExited) return;

        var code = _process.ExitCode;
        _process = null;

        LastMessage = code == 0
            ? "Сервер завершился."
            : $"Сервер остановился сразу после запуска (код {code}). Скорее всего, порт занят.";

        App.Log.LogWarning($"[Host] Server exited with code {code}");
    }

    /// <summary>
    /// True when something already listens on the port. Almost always a server that is already
    /// running — a second one cannot bind and would exit a moment after being started.
    /// </summary>
    private static bool PortInUse(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            listener.Stop();
            return false;
        }
        catch (SocketException)
        {
            return true;
        }
        catch (Exception ex)
        {
            App.Log.LogWarning($"[Host] Could not test port {port}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Null when the server was not shipped alongside the plugin.</summary>
    public static string ServerExe
    {
        get
        {
            var pluginDir = Path.GetDirectoryName(typeof(HostControl).Assembly.Location);
            if (pluginDir == null) return null;

            foreach (var candidate in new[]
                     {
                         Path.Combine(pluginDir, "server", "StarTruckMP.Server.exe"),
                         Path.Combine(pluginDir, "StarTruckMP.Server.exe")
                     })
            {
                if (File.Exists(candidate)) return candidate;
            }

            return null;
        }
    }

    /// <summary>Starts the server and returns what to tell the player.</summary>
    public static string Start()
    {
        LastMessage = null;

        if (IsHosting) return "Сервер уже запущен.";

        var exe = ServerExe;
        if (exe == null) return "Рядом с плагином нет StarTruckMP.Server.exe.";

        if (int.TryParse(App.ServerPort.Value, out var port) && PortInUse(port))
        {
            LastMessage = $"Порт {port} уже занят — сервер, похоже, уже работает. Подключайтесь как игрок.";
            return LastMessage;
        }

        try
        {
            _process = Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(exe)!
            });

            if (_process == null) return "Windows отказался запускать сервер.";

            _process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) App.Log.LogInfo($"[Server] {e.Data}");
            };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) App.Log.LogWarning($"[Server] {e.Data}");
            };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            App.Log.LogInfo($"[Host] Server started (pid={_process.Id})");
            LastMessage = null;
            return "Сервер запущен.";
        }
        catch (Exception ex)
        {
            App.Log.LogError($"[Host] Failed to start server: {ex.Message}");
            return $"Не удалось запустить сервер: {ex.Message}";
        }
    }

    public static string Stop()
    {
        try
        {
            if (IsHosting)
            {
                _process.Kill();
                App.Log.LogInfo("[Host] Server stopped");
            }
        }
        catch (Exception ex)
        {
            App.Log.LogWarning($"[Host] Failed to stop server: {ex.Message}");
        }
        finally
        {
            _process = null;
        }

        return "Сервер остановлен.";
    }
}
