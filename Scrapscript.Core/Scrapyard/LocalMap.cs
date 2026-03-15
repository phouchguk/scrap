using System.Text.Json;

namespace Scrapscript.Core.Scrapyard;

public record MapEntry(int Version, DateTimeOffset Timestamp, string HashRef);

public class LocalMap
{
    public string Root { get; }

    public LocalMap(string? root = null)
    {
        Root = root
            ?? Environment.GetEnvironmentVariable("SCRAP_MAP")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".scrap", "map");
    }

    public void Init()
    {
        Directory.CreateDirectory(Root);
    }

    public string Commit(string name, string hashRef)
    {
        var entries = History(name);
        int version = entries.Count == 0 ? 0 : entries[^1].Version + 1;
        var entry = new MapEntry(version, DateTimeOffset.UtcNow, hashRef);
        entries.Add(entry);

        var path = NameToPath(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(entries, JsonOptions));

        return $"{name}@{version}";
    }

    public string? Resolve(string name, int? version = null, DateTimeOffset? asOf = null)
    {
        var entries = History(name);
        if (entries.Count == 0) return null;

        if (version.HasValue)
            return entries.FirstOrDefault(e => e.Version == version.Value)?.HashRef;

        if (asOf.HasValue)
            return entries.Where(e => e.Timestamp <= asOf.Value).MaxBy(e => e.Version)?.HashRef;

        return entries.MaxBy(e => e.Version)?.HashRef;
    }

    public List<MapEntry> History(string name)
    {
        var path = NameToPath(name);
        if (!File.Exists(path)) return new List<MapEntry>();
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<MapEntry>>(json, JsonOptions) ?? new List<MapEntry>();
    }

    private string NameToPath(string name)
    {
        // "connie2036/fib"  -> Root/connie2036/fib.json
        // "fib"             -> Root/fib.json
        var parts = name.Split('/');
        var dirs = parts[..^1];
        var file = parts[^1] + ".json";
        var segments = new[] { Root }.Concat(dirs).Append(file).ToArray();
        return Path.Combine(segments);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
