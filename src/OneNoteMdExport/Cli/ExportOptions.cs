namespace OneNoteMdExport.Cli;

public sealed record ExportOptions
{
    // Azure AD
    public string TenantId { get; init; } = "common";
    public string ClientId { get; init; } = string.Empty;
    public string RedirectUri { get; init; } = "http://localhost";

    // Output
    public string OutputDir { get; init; } = "export";
    public bool UsePandoc { get; init; } = false;
    public string PandocPath { get; init; } = "pandoc";
    public bool IncludeImages { get; init; } = true;
    public bool IncludeAttachments { get; init; } = true;
    public bool EmitFrontMatter { get; init; } = true;

    // Auth
    public bool UseDeviceCode { get; init; } = false;

    // Filter
    public string? NotebookFilter { get; init; }

    // Diagnostics
    public bool Verbose { get; init; } = false;
}
