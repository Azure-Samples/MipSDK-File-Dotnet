using Microsoft.Extensions.Configuration;
using Microsoft.InformationProtection;

internal sealed class InputValidationException(string message) : ArgumentException(message);

public sealed class AppConfig : IDisposable
{
    private readonly IConfigurationRoot configuration;

    public AppConfig(string? directory = null)
        : this(new ConfigurationBuilder()
            .SetBasePath(directory ?? AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables("MIPSDK_")
            .Build())
    {
    }

    internal AppConfig(IConfigurationRoot configuration)
    {
        this.configuration = configuration;
    }

    public string ClientId => Get("ida:ClientId");
    public string TenantId => Get("ida:TenantGuid");
    public string AppName => Get("app:Name");
    public string AppVersion => Get("app:Version");
    public bool IsMultiTenant => Boolean("ida:IsMultiTenantApp");
    public Cloud Cloud => NamedEnum<Cloud>("sdk:Cloud");
    public LogLevel LogLevel => NamedEnum<LogLevel>("sdk:LogLevel");
    public Consent ServiceConsent => NamedEnum<Consent>("sdk:ServiceConsent");
    public bool EnableDiagnosticLogging => Boolean("sdk:EnableDiagnosticLogging");
    public bool GenerateChangeAuditEvent => Boolean("sdk:GenerateChangeAuditEvent");
    public string RedirectUri => Get("ida:RedirectUri", "http://localhost");
    public string AuthorityHost => Cloud switch
    {
        Cloud.Commercial or Cloud.US_GCC => "login.microsoftonline.com",
        Cloud.US_GCC_High or Cloud.US_DoD => "login.microsoftonline.us",
        Cloud.China_01 => "login.chinacloudapi.cn",
        _ => throw new InputValidationException("sdk:Cloud must be Commercial, US_GCC, US_GCC_High, US_DoD, or China_01.")
    };

    public string IdentityResource => Cloud switch
    {
        Cloud.US_GCC_High => "https://graph.microsoft.us",
        Cloud.US_DoD => "https://dod-graph.microsoft.us",
        Cloud.China_01 => "https://microsoftgraph.chinacloudapi.cn",
        _ => "https://graph.microsoft.com"
    };

    public string BootstrapAuthority => $"https://{AuthorityHost}/{(IsMultiTenant ? "organizations" : TenantId)}";

    public string CachePath
    {
        get
        {
            var configured = Get("sdk:CachePath");
            return string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MipSdkFileSample", ClientId, Cloud.ToString())
                : Path.GetFullPath(configured);
        }
    }

    public IReadOnlyCollection<string> AllowedAuthorityHosts
    {
        get
        {
            var hosts = Values("ida:AllowedAuthorityHosts");
            return hosts.Length == 0 ? ["login.microsoftonline.com"] : hosts;
        }
    }

    public IReadOnlyCollection<string> AllowedResources =>
        Values("ida:AllowedResources").Select(value => value.TrimEnd('/')).ToArray();

    public ApplicationInfo CreateApplicationInfo() => new()
    {
        ApplicationId = ClientId,
        ApplicationName = AppName,
        ApplicationVersion = AppVersion
    };

    public void Validate()
    {
        RequireGuid(ClientId, "ida:ClientId");
        if (!IsMultiTenant)
        {
            RequireGuid(TenantId, "ida:TenantGuid");
        }

        if (string.IsNullOrWhiteSpace(AppName) || string.IsNullOrWhiteSpace(AppVersion))
        {
            throw new InputValidationException("app:Name and app:Version are required.");
        }

        _ = AuthorityHost;
        _ = LogLevel;
        _ = ServiceConsent;
        _ = EnableDiagnosticLogging;
        _ = GenerateChangeAuditEvent;
        _ = CachePath;
        if (!Uri.TryCreate(RedirectUri, UriKind.Absolute, out var redirect)
            || !redirect.IsLoopback || redirect.Scheme != Uri.UriSchemeHttp
            || !string.IsNullOrEmpty(redirect.UserInfo) || !string.IsNullOrEmpty(redirect.Fragment)
            || !string.IsNullOrEmpty(redirect.Query))
        {
            throw new InputValidationException("ida:RedirectUri must be an HTTP loopback URI registered for this public client.");
        }

        if (AllowedAuthorityHosts.Any(host => Uri.CheckHostName(host) != UriHostNameType.Dns)
            || !AllowedAuthorityHosts.Contains(AuthorityHost, StringComparer.OrdinalIgnoreCase))
        {
            throw new InputValidationException("ida:AllowedAuthorityHosts must contain valid DNS hosts, including the selected cloud's authority host.");
        }

        foreach (var resource in AllowedResources)
        {
            AuthDelegateImpl.ValidateHttpsUri(resource, "ida:AllowedResources");
        }

        if (AllowedResources.Count > 0
            && !AllowedResources.Contains(IdentityResource, StringComparer.OrdinalIgnoreCase))
        {
            throw new InputValidationException("ida:AllowedResources must include the selected cloud's Graph resource for delegated sign-in.");
        }
    }

    private string Get(string key, string fallback = "") =>
        configuration[$"AppSettings:{key}"]?.Trim() ?? fallback;

    private string[] Values(string key)
    {
        var path = $"AppSettings:{key}";
        // Read a whole list from one provider; merged children retain lower-priority entries.
        foreach (var provider in configuration.Providers.Reverse())
        {
            var defined = provider.TryGet(path, out var value);
            var children = provider.GetChildKeys([], path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (!defined && children.Length == 0) { continue; }
            if (!string.IsNullOrWhiteSpace(value))
            {
                throw new InputValidationException($"{key} must be an array of string values with numeric indexes.");
            }
            return children.Select(child =>
            {
                if (!int.TryParse(child, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out _)
                    || !provider.TryGet($"{path}:{child}", out var item))
                {
                    throw new InputValidationException($"{key} must be an array of string values with numeric indexes.");
                }
                return item?.Trim();
            }).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().ToArray();
        }
        return [];
    }

    private bool Boolean(string key) => bool.TryParse(Get(key), out var value)
        ? value : throw new InputValidationException($"{key} must be true or false.");

    private T NamedEnum<T>(string key) where T : struct, Enum =>
        Enum.GetNames<T>().Contains(Get(key), StringComparer.OrdinalIgnoreCase)
        && Enum.TryParse<T>(Get(key), true, out var value)
            ? value : throw new InputValidationException($"{key} must be a supported {typeof(T).Name} name.");

    private static void RequireGuid(string value, string key)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new InputValidationException($"{key} must be a non-empty application registration GUID, not a placeholder.");
        }
    }

    public void Dispose() => (configuration as IDisposable)?.Dispose();
}
