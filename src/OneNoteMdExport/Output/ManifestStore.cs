using System.Text.Json;
using OneNoteMdExport.Graph;

namespace OneNoteMdExport.Output;

/// <summary>
/// Persists a pageId → lastModifiedTime map to <c>.manifest.json</c> so that
/// re-runs skip pages that haven't changed.
/// </summary>
public sealed class ManifestStore
{
    private readonly string _path;
    private Dictionary<string, DateTimeOffset> _seen = [];

    private static readonly JsonSerializerOptions JsonOpts =
        new() { WriteIndented = true };

    public ManifestStore(PathLayout layout)
    {
        Directory.CreateDirectory(layout.Root);
        _path = Path.Combine(layout.Root, ".manifest.json");

        if (!File.Exists(_path)) return;
        try
        {
            var json = File.ReadAllText(_path);
            _seen = JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(json) ?? [];
        }
        catch { /* corrupt manifest — start fresh */ }
    }

    public bool IsUpToDate(OneNotePageInfo p) =>
        _seen.TryGetValue(p.Id, out var t) && t >= p.LastModifiedTime;

    public async Task MarkDoneAsync(OneNotePageInfo p, CancellationToken ct = default)
    {
        _seen[p.Id] = p.LastModifiedTime;
        var json = JsonSerializer.Serialize(_seen, JsonOpts);
        await File.WriteAllTextAsync(_path, json, ct);
    }
}
