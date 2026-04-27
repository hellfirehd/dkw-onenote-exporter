using OneNoteMdExport.Cli;
using OneNoteMdExport.Graph;
using OneNoteMdExport.Util;

namespace OneNoteMdExport.Output;

public sealed class PathLayout(ExportOptions opt)
{
    private readonly ExportOptions _opt = opt;

    public String Root => Path.GetFullPath(_opt.OutputDir);

    /// <summary>
    /// Returns the output path for a page:
    /// <c>{Root}/{Notebook}/{Section}/{Title}.md</c>
    /// </summary>
    public String PagePath(OneNotePageInfo p)
    {
        var notebook = Slug.FolderName(p.NotebookName);
        var section = Slug.FolderName(p.SectionName);
        var title = Slug.FileName(p.Title);

        return Path.Combine(Root, notebook, section, $"{title}.md");
    }

    public String AssetsDir(OneNotePageInfo p)
        => Path.Combine(Path.GetDirectoryName(PagePath(p))!, "assets");
}
