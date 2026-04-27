namespace OneNoteMdExport.Cli;

public sealed record ExportOptions
{
    // Azure AD
    public String TenantId { get; init; } = "common";
    public String ClientId { get; init; } = String.Empty;
    public String RedirectUri { get; init; } = "http://localhost";
    public Boolean UsePersistentTokenCache { get; init; } = false;

    // Output
    public String OutputDir { get; init; } = "export";
    public Boolean UsePandoc { get; init; } = false;
    public String PandocPath { get; init; } = "pandoc";
    public Boolean IncludeImages { get; init; } = true;
    public Boolean IncludeAttachments { get; init; } = true;
    public Boolean EmitFrontMatter { get; init; } = true;
    public Int32 ThrottleRequestsPerMinute { get; init; } = 100;
    public Int32 ThrottleRequestsPerHour { get; init; } = 350;
    public Int32 ThrottleConcurrentRequests { get; init; } = 5;

    // Auth
    public Boolean UseDeviceCode { get; init; } = false;

    // Filter
    public String? NotebookFilter { get; init; }

    // Diagnostics
    public Boolean Verbose { get; init; } = false;
}
