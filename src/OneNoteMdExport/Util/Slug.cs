namespace OneNoteMdExport.Util;

public static class Slug
{
    private static readonly HashSet<Char> Invalid =
        [.. Path.GetInvalidFileNameChars(), '\\', '/'];

    /// <summary>Converts arbitrary text to a safe file/folder name.</summary>
    public static String FileName(String? input)
    {
        if (String.IsNullOrWhiteSpace(input))
        {
            return "untitled";
        }

        var sb = new System.Text.StringBuilder(input.Length);
        foreach (var c in input)
        {
            sb.Append(Invalid.Contains(c) ? '_' : c);
        }

        var result = sb.ToString().Trim('_', ' ', '.');
        return String.IsNullOrWhiteSpace(result) ? "untitled" : result;
    }

    public static String FolderName(String? input) => FileName(input);
}
