using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Kiota.Abstractions.Authentication;
using OneNoteMdExport.Cli;

namespace OneNoteMdExport.Auth;

public sealed class GraphAuth
{
    // Delegated scopes required by OneNote API.
    // Notes.Read is sufficient for exporting the signed-in user's notebooks and
    // is supported for personal Microsoft accounts.
    private static readonly string[] Scopes =
        ["Notes.Read", "User.Read"];

    private readonly ExportOptions _opt;
    private readonly ILogger<GraphAuth> _logger;

    public GraphAuth(ExportOptions opt, ILogger<GraphAuth> logger)
    {
        _opt = opt;
        _logger = logger;
    }

    public async Task<GraphServiceClient> CreateGraphClientAsync()
    {
        if (string.IsNullOrWhiteSpace(_opt.ClientId))
            throw new InvalidOperationException(
                "ClientId is missing. Copy appsettings.example.json → appsettings.json " +
                "and fill in your Azure AD app registration details.");

        var app = PublicClientApplicationBuilder
            .Create(_opt.ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, _opt.TenantId)
            .WithRedirectUri(_opt.RedirectUri)
            .Build();

        var tokenProvider = new MsalTokenProvider(app, Scopes, _opt.UseDeviceCode, _logger);
        var authProvider = new BaseBearerTokenAuthenticationProvider(tokenProvider);
        return new GraphServiceClient(authProvider);
    }
}

/// <summary>Bridges MSAL to the Kiota authentication abstraction used by Graph SDK v5.</summary>
internal sealed class MsalTokenProvider : IAccessTokenProvider
{
    private readonly IPublicClientApplication _app;
    private readonly string[] _scopes;
    private readonly bool _useDeviceCode;
    private readonly ILogger _logger;

    public AllowedHostsValidator AllowedHostsValidator { get; } =
        new(["graph.microsoft.com"]);

    public MsalTokenProvider(
        IPublicClientApplication app,
        string[] scopes,
        bool useDeviceCode,
        ILogger logger)
    {
        _app = app;
        _scopes = scopes;
        _useDeviceCode = useDeviceCode;
        _logger = logger;
    }

    public async Task<string> GetAuthorizationTokenAsync(
        Uri uri,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        // Try silent (cached) token first
        var accounts = await _app.GetAccountsAsync();
        try
        {
            var silent = await _app
                .AcquireTokenSilent(_scopes, accounts.FirstOrDefault())
                .ExecuteAsync(cancellationToken);
            return silent.AccessToken;
        }
        catch (MsalUiRequiredException) { }

        // Interactive fallback
        if (_useDeviceCode)
        {
            _logger.LogInformation("Authenticating via device code flow…");
            var result = await _app
                .AcquireTokenWithDeviceCode(_scopes, dc =>
                {
                    Console.WriteLine(dc.Message);
                    return Task.CompletedTask;
                })
                .ExecuteAsync(cancellationToken);
            return result.AccessToken;
        }
        else
        {
            _logger.LogInformation("Authenticating via interactive browser…");
            var result = await _app
                .AcquireTokenInteractive(_scopes)
                .ExecuteAsync(cancellationToken);
            return result.AccessToken;
        }
    }
}
