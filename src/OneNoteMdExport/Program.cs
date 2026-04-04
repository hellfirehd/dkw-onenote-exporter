using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Logging;
using OneNoteMdExport.Auth;
using OneNoteMdExport.Cli;
using OneNoteMdExport.Graph;
using OneNoteMdExport.Logging;
using OneNoteMdExport.Output;
using OneNoteMdExport.Transform;
using OneNoteMdExport.Util;
using Microsoft.Kiota.Abstractions;

var options = CommandLine.Parse(args);
if (options is null) return 0;   // --help was shown

Retry.Configure(
    options.ThrottleRequestsPerMinute,
    options.ThrottleRequestsPerHour,
    options.ThrottleConcurrentRequests);

var logFilePath = Path.GetFullPath(Path.Combine(options.OutputDir, "export.log"));
Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole(options =>
    {
        options.FormatterName = MessageOnlyConsoleFormatter.FormatterName;
    });
    builder.AddConsoleFormatter<MessageOnlyConsoleFormatter, SimpleConsoleFormatterOptions>(o =>
    {
        o.SingleLine = true;
        o.TimestampFormat = "HH:mm:ss ";
    });
    builder.AddProvider(new FileLoggerProvider(logFilePath));
    builder.SetMinimumLevel(options.Verbose ? LogLevel.Debug : LogLevel.Information);
});

var logger = loggerFactory.CreateLogger("App");

try
{
    var auth       = new GraphAuth(options, loggerFactory.CreateLogger<GraphAuth>());
    var graphClient = await auth.CreateGraphClientAsync();

    var oneNote   = new OneNoteGraphClient(graphClient, loggerFactory.CreateLogger<OneNoteGraphClient>());
    var layout    = new PathLayout(options);
    var manifest  = new ManifestStore(layout);
    var assets    = new AssetDownloader(graphClient, layout, options, loggerFactory.CreateLogger<AssetDownloader>());
    var normalizer = new OneNoteHtmlNormalizer(options, loggerFactory.CreateLogger<OneNoteHtmlNormalizer>());
    var converter = new HtmlToMarkdown(options, loggerFactory.CreateLogger<HtmlToMarkdown>());
    var writer    = new MarkdownWriter(layout, options);

    int total = 0, skipped = 0, deferred = 0, written = 0, failed = 0;

    await foreach (var page in oneNote.EnumeratePagesAsync(options))
    {
        total++;

        var decision = manifest.GetDecision(page);

        if (decision is ManifestDecision.UpToDate)
        {
            skipped++;
            logger.LogDebug("Skip  [{Nb}/{Sec}] {Title}", page.NotebookName, page.SectionName, page.Title);
            continue;
        }

        if (decision is ManifestDecision.AlreadyAttemptedThisSweep)
        {
            deferred++;
            logger.LogDebug(
                "Defer [{Nb}/{Sec}] {Title} until the next sweep.",
                page.NotebookName,
                page.SectionName,
                page.Title);
            continue;
        }

        logger.LogInformation("Write [{Nb}/{Sec}] {Title}", page.NotebookName, page.SectionName, page.Title);

        try
        {
            var html           = await oneNote.GetPageHtmlAsync(page);
            var normalizedHtml = await normalizer.NormalizeAsync(page, html, assets);
            var markdown       = await converter.ConvertAsync(normalizedHtml);

            await writer.WritePageAsync(page, markdown);
            await manifest.MarkSuccessAsync(page);
            written++;
        }
        catch (ApiException ex)
        {
            failed++;
            await manifest.MarkFailureAsync(page, ex);
            logger.LogWarning(
                ex,
                "Skip  [{Nb}/{Sec}] {Title} ({Id}) because the page content could not be retrieved.",
                page.NotebookName,
                page.SectionName,
                page.Title,
                page.Id);
        }
    }

    await manifest.CompleteSweepAsync();

    logger.LogInformation(
        "Done — {Total} pages: {Written} written, {Skipped} unchanged, {Deferred} deferred until next sweep, {Failed} failed in this sweep.",
        total, written, skipped, deferred, failed);

    return 0;
}
catch (Exception ex)
{
    logger.LogError(ex, "Export failed: {Msg}", ex.Message);
    return 1;
}
