using System.Text;
using OneNoteMdExport.Cli;
using OneNoteMdExport.Graph;
using OneNoteMdExport.Util;

namespace OneNoteMdExport.Output;

public sealed class MarkdownWriter
{
    private readonly PathLayout _layout;
    private readonly ExportOptions _opt;

    public MarkdownWriter(PathLayout layout, ExportOptions opt)
    {
        _layout = layout;
        _opt = opt;
    }

    public async Task WritePageAsync(
        OneNotePageInfo page,
        String markdown,
        CancellationToken ct = default)
    {
        var path = _layout.PagePath(page);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (_opt.EmitFrontMatter)
        {
            var sb = new StringBuilder();
            sb.AppendLine("---");
            sb.AppendLine($"onenote_id: \"{page.Id}\"");
            sb.AppendLine($"title: \"{EscapeYaml(page.Title)}\"");
            sb.AppendLine($"created: \"{page.CreatedTime:O}\"");
            sb.AppendLine($"modified: \"{page.LastModifiedTime:O}\"");
            sb.AppendLine($"notebook: \"{EscapeYaml(page.NotebookName)}\"");
            sb.AppendLine($"section: \"{EscapeYaml(page.SectionName)}\"");
            sb.AppendLine("---");
            sb.AppendLine();
            sb.Append(markdown);
            markdown = sb.ToString();
        }

        await File.WriteAllTextAsync(path, markdown, Encoding.UTF8, ct);
    }

    private static String EscapeYaml(String s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
