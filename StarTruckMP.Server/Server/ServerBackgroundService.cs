using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StarTruckMP.Server.Server.Services;

namespace StarTruckMP.Server.Server;

public class ServerBackgroundService(ServerManager serverManager) : IHostedService
{
    /// <summary>
    /// The longest the relay loop sleeps when nothing arrives: the pace of the ping table, the
    /// kick list and the library's own housekeeping. A packet wakes it at once.
    /// </summary>
    private const int IdleTickMs = 15;

    private Thread? _relayThread;
    private readonly CancellationTokenSource _stopping = new();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        serverManager.Start();

        // Its own thread rather than a Task: the loop blocks on a wait handle, and a thread-pool
        // thread held for the life of the process is a thread the pool is missing.
        _relayThread = new Thread(() =>
        {
            while (!_stopping.IsCancellationRequested)
            {
                serverManager.Polling();
                serverManager.WaitForWork(IdleTickMs);
            }
        })
        {
            Name = "StarTruckMP relay",
            IsBackground = true
        };
        _relayThread.Start();

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping.Cancel();
        _relayThread?.Join(TimeSpan.FromSeconds(2));
        serverManager.Stop();
        return Task.CompletedTask;
    }
}