using System.Diagnostics;

namespace SteamPosters.Core.Steam;

/// <summary>
/// Detects, closes and starts the Steam client. Steam keeps shortcuts.vdf in memory and rewrites
/// it on exit, so it must be fully closed before the file is edited.
/// </summary>
public interface ISteamProcess
{
    bool IsRunning();

    /// <summary>Asks Steam to exit cleanly and waits for it. Returns false if it is still running after <paramref name="timeout"/>.</summary>
    Task<bool> ShutdownAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>Waits (indefinitely) for the user to close Steam themselves.</summary>
    Task WaitForExitAsync(CancellationToken cancellationToken = default);

    void Start();
}

/// <summary>The real Steam client, controlled through steam.exe.</summary>
public sealed class SteamProcess(string steamPath) : ISteamProcess
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>Extra wait after the process ends, so Steam's last file writes are flushed.</summary>
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(2);

    private string SteamExe => Path.Combine(steamPath, "steam.exe");

    public bool IsRunning()
    {
        var processes = Process.GetProcessesByName("steam");
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }

    public async Task<bool> ShutdownAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!IsRunning()) return true;
        // "-shutdown" asks the running client to exit the same way File > Exit does.
        using (Process.Start(new ProcessStartInfo(SteamExe, "-shutdown") { UseShellExecute = false })) { }

        var deadline = DateTime.UtcNow + timeout;
        while (IsRunning())
        {
            if (DateTime.UtcNow > deadline) return false;
            await Task.Delay(PollInterval, cancellationToken);
        }
        await Task.Delay(SettleTime, cancellationToken);
        return true;
    }

    public async Task WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning()) return;
        while (IsRunning()) await Task.Delay(PollInterval, cancellationToken);
        await Task.Delay(SettleTime, cancellationToken);
    }

    public void Start()
    {
        using (Process.Start(new ProcessStartInfo(SteamExe) { UseShellExecute = true })) { }
    }
}
