using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OneNoteMdExport.Cli;
using ReverseMarkdown;

namespace OneNoteMdExport.Transform;

public sealed class HtmlToMarkdown
{
    private readonly ExportOptions _opt;
    private readonly ILogger<HtmlToMarkdown> _logger;

    public HtmlToMarkdown(ExportOptions opt, ILogger<HtmlToMarkdown> logger)
    {
        _opt = opt;
        _logger = logger;
    }

    public async Task<String> ConvertAsync(String html, CancellationToken ct = default)
    {
        if (String.IsNullOrWhiteSpace(html)) return String.Empty;

        return _opt.UsePandoc
            ? await ConvertWithPandocAsync(html, ct)
            : ConvertWithReverseMarkdown(html);
    }

    // ── ReverseMarkdown (in-process, default) ────────────────────────────────

    private static String ConvertWithReverseMarkdown(String html)
    {
        var config = new Config
        {
            GithubFlavored = true,
            RemoveComments = true,
            SmartHrefHandling = true,
            UnknownTags = Config.UnknownTagsOption.PassThrough,
        };

        return new Converter(config).Convert(html);
    }

    // ── Pandoc (external binary, optional) ──────────────────────────────────

    private async Task<String> ConvertWithPandocAsync(String html, CancellationToken ct)
    {
        var tempIn = Path.GetTempFileName();
        var tempOut = Path.ChangeExtension(Path.GetTempFileName(), ".md");
        try
        {
            await File.WriteAllTextAsync(tempIn, html, System.Text.Encoding.UTF8, ct);

            var psi = new ProcessStartInfo
            {
                FileName = _opt.PandocPath,
                // --wrap=none avoids hard line-wrapping inside paragraphs
                Arguments = $"-f html -t gfm --wrap=none -o \"{tempOut}\" \"{tempIn}\"",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException(
                       $"Failed to start pandoc at '{_opt.PandocPath}'. " +
                       "Ensure pandoc is installed and on PATH, or set PandocPath in appsettings.json.");

            var stderr = await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            if (proc.ExitCode != 0)
                _logger.LogWarning("pandoc exited {Code}: {Err}", proc.ExitCode, stderr);

            return await File.ReadAllTextAsync(tempOut, System.Text.Encoding.UTF8, ct);
        }
        finally
        {
            if (File.Exists(tempIn)) File.Delete(tempIn);
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }
}
