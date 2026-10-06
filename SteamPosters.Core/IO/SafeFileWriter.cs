namespace SteamPosters.Core.IO;

/// <summary>
/// Writes a file without ever leaving a half-written target: write a temp file in the same
/// folder, optionally verify what landed on disk, then swap it in.
/// </summary>
public static class SafeFileWriter
{
    /// <param name="verify">Called with the bytes read back from the temp file; return false to abort.</param>
    public static void Write(string path, byte[] bytes, Func<byte[], bool>? verify = null)
    {
        var fullPath = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(fullPath)!;
        var temp = Path.Combine(folder, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (verify is not null)
            {
                bool ok;
                try
                {
                    ok = verify(File.ReadAllBytes(temp));
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException($"Verification of the new '{Path.GetFileName(path)}' failed.", ex);
                }
                if (!ok) throw new InvalidDataException($"Verification of the new '{Path.GetFileName(path)}' failed.");
            }

            if (File.Exists(fullPath)) File.Replace(temp, fullPath, destinationBackupFileName: null);
            else File.Move(temp, fullPath);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
