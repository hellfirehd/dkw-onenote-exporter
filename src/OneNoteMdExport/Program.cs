using Microsoft.Extensions.Logging;
using OneNoteMdExport.Auth;
using OneNoteMdExport.Cli;
using OneNoteMdExport.Graph;
using OneNoteMdExport.Output;
using OneNoteMdExport.Transform;

var options = CommandLine.Parse(args);
if (options is null) return 0;   // --help was shown

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddSimpleConsole(o =>
    {
        o.SingleLine = true;
        o.TimestampFormat = "HH:mm:ss ";
    });
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

    int total = 0, skipped = 0, written = 0;

    await foreach (var page in oneNote.EnumeratePagesAsync(options))
    {
        total++;

        if (manifest.IsUpToDate(page))
        {
            skipped++;
            logger.LogDebug("Skip  [{Nb}/{Sec}] {Title}", page.NotebookName, page.SectionName, page.Title);
            continue;
        }

        logger.LogInformation("Write [{Nb}/{Sec}] {Title}", page.NotebookName, page.SectionName, page.Title);

        var html           = await oneNote.GetPageHtmlAsync(page.Id);
        var normalizedHtml = await normalizer.NormalizeAsync(page, html, assets);
        var markdown       = await converter.ConvertAsync(normalizedHtml);

        await writer.WritePageAsync(page, markdown);
        await manifest.MarkDoneAsync(page);
        written++;
    }

    logger.LogInformation(
        "Done — {Total} pages: {Written} written, {Skipped} unchanged.",
        total, written, skipped);

    return 0;
}
catch (Exception ex)
{
    logger.LogError(ex, "Export failed: {Msg}", ex.Message);
    return 1;
}
