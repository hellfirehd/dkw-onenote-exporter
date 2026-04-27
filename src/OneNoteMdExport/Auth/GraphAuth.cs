using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Kiota.Abstractions.Authentication;
using OneNoteMdExport.Cli;

namespace OneNoteMdExport.Auth;

public sealed class GraphAuth(ExportOptions opt, ILogger<GraphAuth> logger)
{
    // Delegated scopes required by OneNote API.
    // Notes.Read is sufficient for exporting the signed-in user's notebooks and
    // is supported for personal Microsoft accounts.
    private static readonly String[] Scopes =
        ["Notes.Read", "User.Read"];

    private readonly ExportOptions _opt = opt;
    private readonly ILogger<GraphAuth> _logger = logger;

    public async Task<GraphServiceClient> CreateGraphClientAsync()
    {
        if (String.IsNullOrWhiteSpace(_opt.ClientId))
            throw new InvalidOperationException(
                "ClientId is missing. Copy appsettings.example.json → appsettings.json " +
                "and fill in your Azure AD app registration details.");

        var app = PublicClientApplicationBuilder
            .Create(_opt.ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, _opt.TenantId)
            .WithRedirectUri(_opt.RedirectUri)
            .Build();

        if (_opt.UsePersistentTokenCache)
            MsalTokenCachePersistence.Enable(app.UserTokenCache, GetCachePath(), _logger);

        var tokenProvider = new MsalTokenProvider(app, Scopes, _opt.UseDeviceCode, _logger);
        var authProvider = new BaseBearerTokenAuthenticationProvider(tokenProvider);
        return new GraphServiceClient(authProvider);
    }

    private String GetCachePath()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OneNoteMdExport");

        return Path.Combine(root, $"msal-{_opt.TenantId}-{_opt.ClientId}.bin");
    }
}

internal static class MsalTokenCachePersistence
{
    private static readonly Object Sync = new();

    public static void Enable(ITokenCache tokenCache, String cachePath, ILogger logger)
    {
        tokenCache.SetBeforeAccess(args =>
        {
            lock (Sync)
            {
                if (!File.Exists(cachePath))
                    return;

                var data = File.ReadAllBytes(cachePath);
                args.TokenCache.DeserializeMsalV3(data, shouldClearExistingCache: true);
            }
        });

        tokenCache.SetAfterAccess(args =>
        {
            if (!args.HasStateChanged)
                return;

            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                File.WriteAllBytes(cachePath, args.TokenCache.SerializeMsalV3());
            }
        });

        logger.LogInformation("Persistent token cache enabled at {Path}", cachePath);
    }
}

/// <summary>Bridges MSAL to the Kiota authentication abstraction used by Graph SDK v5.</summary>
internal sealed class MsalTokenProvider(
    IPublicClientApplication app,
    String[] scopes,
    Boolean useDeviceCode,
    ILogger logger) : IAccessTokenProvider
{
    private readonly IPublicClientApplication _app = app;
    private readonly String[] _scopes = scopes;
    private readonly Boolean _useDeviceCode = useDeviceCode;
    private readonly ILogger _logger = logger;

    public AllowedHostsValidator AllowedHostsValidator { get; } =
        new(["graph.microsoft.com"]);

    public async Task<String> GetAuthorizationTokenAsync(
        Uri uri,
        Dictionary<String, Object>? additionalAuthenticationContext = null,
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
