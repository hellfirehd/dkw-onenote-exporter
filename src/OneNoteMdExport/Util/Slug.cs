namespace OneNoteMdExport.Util;

public static class Slug
{
    private static readonly HashSet<char> Invalid =
        [.. Path.GetInvalidFileNameChars(), '\\', '/'];

    /// <summary>Converts arbitrary text to a safe file/folder name.</summary>
    public static string FileName(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "untitled";

        var sb = new System.Text.StringBuilder(input.Length);
        foreach (var c in input)
            sb.Append(Invalid.Contains(c) ? '_' : c);

        var result = sb.ToString().Trim('_', ' ', '.');
        return string.IsNullOrWhiteSpace(result) ? "untitled" : result;
    }

    public static string FolderName(string? input) => FileName(input);
}
