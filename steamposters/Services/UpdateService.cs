using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace steamposters.Services;

/// <summary>Checks for and installs new versions of the app.</summary>
public interface IUpdateService
{
    /// <summary>The version of a downloaded update ready to install, or null if there is none.</summary>
    Task<string?> CheckAndDownloadAsync(CancellationToken cancellationToken = default);

    /// <summary>Closes the app, installs the downloaded update and starts the new version.</summary>
    void ApplyAndRestart();
}

/// <summary>
/// Updates from the project's GitHub Releases through Velopack. Does nothing when the app runs
/// from a dev build (not installed with Setup.exe).
/// </summary>
public sealed class GitHubUpdateService : IUpdateService
{
    public const string RepositoryUrl = "https://github.com/jwo0010/steam-posters";

    private readonly UpdateManager _manager = new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
    private UpdateInfo? _pending;

    public async Task<string?> CheckAndDownloadAsync(CancellationToken cancellationToken = default)
    {
        if (!_manager.IsInstalled) return null;
        var update = await _manager.CheckForUpdatesAsync();
        if (update is null) return null;
        await _manager.DownloadUpdatesAsync(update, null, cancellationToken);
        _pending = update;
        return update.TargetFullRelease.Version.ToString();
    }

    public void ApplyAndRestart()
    {
        if (_pending is not null) _manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
    }
}
