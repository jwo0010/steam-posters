using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using SteamPosters.Core.IO;

namespace SteamPosters.Artwork;

/// <summary>Where the SteamGridDB API key is kept between runs.</summary>
public interface IApiKeyStore
{
    /// <summary>The saved key, or null if there is none or it can't be read.</summary>
    string? Load();

    void Save(string apiKey);

    void Delete();
}

/// <summary>
/// Stores the user's SteamGridDB API key encrypted with Windows DPAPI (current user only),
/// in %APPDATA%\SteamPosters\settings. The key is never written in plain text.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ApiKeyStore : IApiKeyStore
{
    public const string FileName = "steamgriddb.key";

    private static readonly byte[] Entropy = "SteamPosters.SteamGridDb.v1"u8.ToArray();

    public ApiKeyStore(string? settingsFolder = null) => SettingsFolder = settingsFolder ?? DefaultSettingsFolder;

    public static string DefaultSettingsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SteamPosters", "settings");

    public string SettingsFolder { get; }

    private string KeyPath => Path.Combine(SettingsFolder, FileName);

    public void Save(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("API key is empty.", nameof(apiKey));
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey.Trim()), Entropy, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(SettingsFolder);
        SafeFileWriter.Write(KeyPath, encrypted);
    }

    /// <summary>The saved key, or null if none is saved or it can't be decrypted (e.g. another Windows user).</summary>
    public string? Load()
    {
        if (!File.Exists(KeyPath)) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(KeyPath), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public void Delete()
    {
        if (File.Exists(KeyPath)) File.Delete(KeyPath);
    }
}
