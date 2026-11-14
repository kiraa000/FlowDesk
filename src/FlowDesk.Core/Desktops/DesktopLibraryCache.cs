using System.Security.Cryptography;

namespace FlowDesk.Core.Desktops;

/// <summary>
/// Load an immutable content-addressed copy, not the user-selected file that Settings must be able
/// to replace. Different bytes get a different module path/STA owner; equal bytes share one owner.
/// </summary>
internal static class DesktopLibraryCache
{
    internal static string Prepare(string source, string dataDirectory)
    {
        string hash = Hash(source);
        string directory = Path.Combine(dataDirectory, "native", "cache", hash);
        string destination = Path.Combine(directory, VirtualDesktopAccessor.DllName);
        Directory.CreateDirectory(directory);
        if (File.Exists(destination))
        {
            if (Hash(destination) != hash) throw new InvalidDataException("The cached desktop library is damaged.");
            return destination;
        }
        string temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, temp);
            if (Hash(temp) != hash) throw new IOException("The desktop library changed while being prepared.");
            try { File.Move(temp, destination); }
            catch (IOException) when (File.Exists(destination) && Hash(destination) == hash)
            { /* another process prepared the same immutable bytes */ }
            return destination;
        }
        finally
        {
            try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
