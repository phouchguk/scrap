using System.Security.Cryptography;

namespace Scrapscript.Core.Scrapyard;

public class LocalYard
{
    public string Root { get; }

    public LocalYard(string? root = null)
    {
        Root = root
            ?? Environment.GetEnvironmentVariable("SCRAP_YARD")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".scrap", "yard");
    }

    public void Init()
    {
        Directory.CreateDirectory(Root);
    }

    public string Push(byte[] flat)
    {
        var hash = SHA1.HashData(flat);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        var hashRef = $"sha1~~{hex}";

        var dir = Path.Combine(Root, "sha1", hex[..2]);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, hex[2..]);
        File.WriteAllBytes(file, flat);

        return hashRef;
    }

    public byte[]? Pull(string hashRef)
    {
        var file = HashRefToPath(hashRef);
        return file is not null && File.Exists(file) ? File.ReadAllBytes(file) : null;
    }

    public bool Contains(string hashRef)
    {
        var file = HashRefToPath(hashRef);
        return file is not null && File.Exists(file);
    }

    private string? HashRefToPath(string hashRef)
    {
        if (!hashRef.StartsWith("sha1~~")) return null;
        var hex = hashRef["sha1~~".Length..];
        if (hex.Length < 3) return null;
        return Path.Combine(Root, "sha1", hex[..2], hex[2..]);
    }
}
