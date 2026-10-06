using System.Text.Json;

namespace SteamPosters.Core.IO;

/// <summary>One file in a backup. <see cref="BackupFile"/> is null when the file did not exist yet.</summary>
public sealed record BackupEntry(string OriginalPath, string? BackupFile);

public sealed record BackupManifest(DateTime CreatedUtc, string? Description, IReadOnlyList<BackupEntry> Entries);

public sealed record BackupSet(string Folder, BackupManifest Manifest);

/// <summary>
/// Copies files that are about to be changed into backups\{yyyyMMdd-HHmmss}\ with a manifest,
/// and restores them. Files that did not exist are recorded so a restore removes them again.
/// </summary>
public sealed class BackupService
{
    public const string ManifestFileName = "manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Func<DateTime> _now;

    public BackupService(string? backupsRoot = null, Func<DateTime>? now = null)
    {
        BackupsRoot = backupsRoot ?? DefaultBackupsRoot;
        _now = now ?? (() => DateTime.Now);
    }

    public static string DefaultBackupsRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SteamPosters", "backups");

    public string BackupsRoot { get; }

    public BackupSet Create(IEnumerable<string> pathsAboutToChange, string? description = null)
    {
        var now = _now();
        var folder = Path.Combine(BackupsRoot, now.ToString("yyyyMMdd-HHmmss"));
        for (var n = 2; Directory.Exists(folder); n++)
            folder = Path.Combine(BackupsRoot, $"{now:yyyyMMdd-HHmmss}-{n}");
        Directory.CreateDirectory(folder);

        var entries = new List<BackupEntry>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in pathsAboutToChange.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path))
            {
                entries.Add(new BackupEntry(path, null));
                continue;
            }
            var name = Path.GetFileName(path);
            for (var n = 2; !usedNames.Add(name); n++)
                name = $"{Path.GetFileNameWithoutExtension(path)}-{n}{Path.GetExtension(path)}";
            File.Copy(path, Path.Combine(folder, name));
            entries.Add(new BackupEntry(path, name));
        }

        var manifest = new BackupManifest(now.ToUniversalTime(), description, entries);
        File.WriteAllText(Path.Combine(folder, ManifestFileName), JsonSerializer.Serialize(manifest, JsonOptions));
        return new BackupSet(folder, manifest);
    }

    /// <summary>Backups, newest first.</summary>
    public IReadOnlyList<BackupSet> List()
    {
        if (!Directory.Exists(BackupsRoot)) return [];
        return Directory.EnumerateDirectories(BackupsRoot)
            .Select(TryLoad)
            .OfType<BackupSet>()
            .OrderByDescending(b => b.Manifest.CreatedUtc)
            .ToList();
    }

    /// <summary>Puts every file back as it was when the backup was made.</summary>
    public void Restore(BackupSet backup)
    {
        foreach (var entry in backup.Manifest.Entries)
        {
            if (entry.BackupFile is null)
            {
                if (File.Exists(entry.OriginalPath)) File.Delete(entry.OriginalPath);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(entry.OriginalPath)!);
            SafeFileWriter.Write(entry.OriginalPath, File.ReadAllBytes(Path.Combine(backup.Folder, entry.BackupFile)));
        }
    }

    private static BackupSet? TryLoad(string folder)
    {
        var manifestPath = Path.Combine(folder, ManifestFileName);
        if (!File.Exists(manifestPath)) return null;
        try
        {
            var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath));
            return manifest is null ? null : new BackupSet(folder, manifest);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
