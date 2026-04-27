using System.Text.Json;
using OneNoteMdExport.Graph;
using Microsoft.Kiota.Abstractions;

namespace OneNoteMdExport.Output;

/// <summary>
/// Persists page export state to <c>.manifest.json</c> so that re-runs can skip
/// unchanged pages and defer failed pages until the current sweep completes.
/// </summary>
public sealed class ManifestStore
{
    private readonly String _path;
    private ManifestDocument _manifest = new();

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
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("Pages", out _))
            {
                _manifest = JsonSerializer.Deserialize<ManifestDocument>(json) ?? new ManifestDocument();
            }
            else
            {
                var legacy = JsonSerializer.Deserialize<Dictionary<String, DateTimeOffset>>(json) ?? [];
                _manifest = UpgradeLegacy(legacy);
            }

            if (_manifest.ActiveSweep < 1)
            {
                _manifest.ActiveSweep = 1;
            }
        }
        catch { /* corrupt manifest — start fresh */ }
    }

    public ManifestDecision GetDecision(OneNotePageInfo page)
    {
        if (!_manifest.Pages.TryGetValue(page.Id, out var state))
        {
            return ManifestDecision.Process;
        }

        if (state.LastSucceededModifiedTime is { } succeeded &&
            succeeded >= page.LastModifiedTime)
        {
            return ManifestDecision.UpToDate;
        }

        if (state.LastAttemptSweep == _manifest.ActiveSweep &&
            state.LastAttemptedModifiedTime is { } attempted &&
            attempted >= page.LastModifiedTime)
        {
            return ManifestDecision.AlreadyAttemptedThisSweep;
        }

        return ManifestDecision.Process;
    }

    public async Task MarkSuccessAsync(OneNotePageInfo page, CancellationToken ct = default)
    {
        var state = GetOrCreateState(page.Id);
        state.LastSucceededModifiedTime = page.LastModifiedTime;
        state.LastAttemptedModifiedTime = page.LastModifiedTime;
        state.LastAttemptSweep = _manifest.ActiveSweep;
        state.LastOutcome = "Succeeded";
        state.LastErrorStatus = null;
        state.LastErrorMessage = null;

        await SaveAsync(ct);
    }

    public async Task MarkFailureAsync(OneNotePageInfo page, Exception ex, CancellationToken ct = default)
    {
        var state = GetOrCreateState(page.Id);
        state.LastAttemptedModifiedTime = page.LastModifiedTime;
        state.LastAttemptSweep = _manifest.ActiveSweep;
        state.LastOutcome = "Failed";
        state.LastErrorStatus = ex is ApiException apiEx ? apiEx.ResponseStatusCode : null;
        state.LastErrorMessage = ex.Message;

        await SaveAsync(ct);
    }

    public async Task CompleteSweepAsync(CancellationToken ct = default)
    {
        _manifest.ActiveSweep++;
        await SaveAsync(ct);
    }

    private ManifestPageState GetOrCreateState(String pageId)
    {
        if (_manifest.Pages.TryGetValue(pageId, out var state))
        {
            return state;
        }

        state = new ManifestPageState();
        _manifest.Pages[pageId] = state;
        return state;
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(_manifest, JsonOpts);
        await File.WriteAllTextAsync(_path, json, ct);
    }

    private static ManifestDocument UpgradeLegacy(Dictionary<String, DateTimeOffset> legacy)
    {
        var manifest = new ManifestDocument();

        foreach (var (pageId, modifiedTime) in legacy)
        {
            manifest.Pages[pageId] = new ManifestPageState
            {
                LastSucceededModifiedTime = modifiedTime,
                LastAttemptedModifiedTime = modifiedTime,
                LastAttemptSweep = 0,
                LastOutcome = "Succeeded",
            };
        }

        return manifest;
    }
}

public enum ManifestDecision
{
    Process,
    UpToDate,
    AlreadyAttemptedThisSweep,
}

public sealed class ManifestDocument
{
    public Int32 SchemaVersion { get; set; } = 2;
    public Int32 ActiveSweep { get; set; } = 1;
    public Dictionary<String, ManifestPageState> Pages { get; set; } = [];
}

public sealed class ManifestPageState
{
    public DateTimeOffset? LastSucceededModifiedTime { get; set; }
    public DateTimeOffset? LastAttemptedModifiedTime { get; set; }
    public Int32? LastAttemptSweep { get; set; }
    public String? LastOutcome { get; set; }
    public Int32? LastErrorStatus { get; set; }
    public String? LastErrorMessage { get; set; }
}
