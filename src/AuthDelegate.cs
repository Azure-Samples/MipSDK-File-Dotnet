using Microsoft.InformationProtection;
using Microsoft.Identity.Client;

public sealed class AuthDelegateImpl : IAuthDelegate, IDisposable
{
    private readonly AppConfig config;
    private readonly IPublicClientApplication application;
    private readonly SemaphoreSlim acquisition = new(1, 1);
    private string? accountId;
    private bool disposed;

    public AuthDelegateImpl(AppConfig config) : this(config, CreateApplication(config))
    {
    }

    internal AuthDelegateImpl(AppConfig config, IPublicClientApplication application)
    {
        config.Validate();
        this.config = config;
        this.application = application;
    }

    private static IPublicClientApplication CreateApplication(AppConfig config)
    {
        config.Validate();
        return PublicClientApplicationBuilder.Create(config.ClientId)
            .WithAuthority(config.BootstrapAuthority, validateAuthority: true)
            .WithRedirectUri(config.RedirectUri)
            .WithLogging((_, _, _) => { }, LogLevelForMsal, enablePiiLogging: false)
            .Build();
    }

    private const Microsoft.Identity.Client.LogLevel LogLevelForMsal = Microsoft.Identity.Client.LogLevel.Error;

    public string AcquireToken(Identity identity, string authority, string resource, string claims) =>
        AcquireTokenAsync(authority, resource, claims, identity?.Email).GetAwaiter().GetResult().AccessToken;

    public async Task<AuthenticationResult> AcquireTokenAsync(
        string authority, string resource, string? claims, string? expectedUser = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var validatedAuthority = ValidateAuthority(authority);
        var tenant = new Uri(validatedAuthority).AbsolutePath.Trim('/');
        var scopes = new[] { BuildDefaultScope(resource) };
        await acquisition.WaitAsync().ConfigureAwait(false);
        try
        {
            var account = SelectAccount(await application.GetAccountsAsync().ConfigureAwait(false),
                accountId, expectedUser);
            AuthenticationResult result;
            try
            {
                result = await application.AcquireTokenSilent(scopes, account)
                    .WithTenantId(tenant)
                    .WithClaims(claims)
                    .ExecuteAsync().ConfigureAwait(false);
            }
            catch (MsalUiRequiredException)
            {
                result = await application.AcquireTokenInteractive(scopes)
                    .WithTenantId(tenant)
                    .WithLoginHint(expectedUser ?? account?.Username)
                    .WithClaims(claims)
                    .ExecuteAsync().ConfigureAwait(false);
            }

            ValidateResult(result, expectedUser);
            accountId = result.Account.HomeAccountId.Identifier;
            return result;
        }
        finally
        {
            acquisition.Release();
        }
    }

    internal string ValidateAuthority(string authority)
    {
        var uri = ValidateHttpsUri(authority, "Authority");
        if (!config.AllowedAuthorityHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            throw new InputValidationException("Authority host is not in ida:AllowedAuthorityHosts.");
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 1)
        {
            throw new InputValidationException("Authority must identify exactly one tenant.");
        }

        var tenant = segments[0];
        var shared = tenant.Equals("common", StringComparison.OrdinalIgnoreCase)
            || tenant.Equals("organizations", StringComparison.OrdinalIgnoreCase);
        if (!shared && (!Guid.TryParse(tenant, out var tenantGuid) || tenantGuid == Guid.Empty))
        {
            throw new InputValidationException("Authority must identify an organizational tenant.");
        }

        if (!config.IsMultiTenant)
        {
            if (!shared && !tenant.Equals(config.TenantId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InputValidationException("Authority does not match the configured tenant.");
            }
            tenant = config.TenantId;
        }

        return $"https://{uri.Host}/{tenant}";
    }

    internal string BuildDefaultScope(string resource)
    {
        var uri = ValidateHttpsUri(resource, "Resource");
        var normalized = uri.AbsoluteUri.TrimEnd('/');
        if (normalized.EndsWith("/.default", StringComparison.OrdinalIgnoreCase))
        {
            throw new InputValidationException("The SDK resource must be an audience, not an OAuth scope.");
        }

        if (config.AllowedResources.Count > 0
            && !config.AllowedResources.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            throw new InputValidationException("Resource is not in ida:AllowedResources.");
        }
        return $"{normalized}/.default";
    }

    internal static Uri ValidateHttpsUri(string value, string name)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InputValidationException($"{name} must be an absolute HTTPS URI without credentials, a custom port, query, or fragment.");
        }
        return uri;
    }

    internal static IAccount? SelectAccount(IEnumerable<IAccount> accounts, string? selectedId, string? expectedUser)
    {
        if (selectedId != null)
        {
            return accounts.SingleOrDefault(account => account.HomeAccountId.Identifier == selectedId
                && (string.IsNullOrEmpty(expectedUser)
                    || account.Username.Equals(expectedUser, StringComparison.OrdinalIgnoreCase)));
        }

        return string.IsNullOrEmpty(expectedUser) ? null : accounts.SingleOrDefault(
            account => account.Username.Equals(expectedUser, StringComparison.OrdinalIgnoreCase));
    }

    private void ValidateResult(AuthenticationResult result, string? expectedUser)
    {
        if (result.Account == null || string.IsNullOrWhiteSpace(result.Account.Username)
            || result.Account.HomeAccountId == null
            || (accountId != null && accountId != result.Account.HomeAccountId.Identifier)
            || (!string.IsNullOrEmpty(expectedUser)
                && !expectedUser.Equals(result.Account.Username, StringComparison.OrdinalIgnoreCase))
            || (!config.IsMultiTenant
                && !config.TenantId.Equals(result.TenantId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Sign-in returned a different user or tenant. Sign in with the intended organizational account.");
        }
    }

    public async Task<Identity> GetUserIdentityAsync()
    {
        var result = await AcquireTokenAsync(config.BootstrapAuthority, config.IdentityResource, null)
            .ConfigureAwait(false);
        return new Identity(result.Account.Username);
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        acquisition.Dispose();
    }
}
