using System.Text.Json;
using Microsoft.InformationProtection;
using Microsoft.InformationProtection.File;
using Moq;
using SdkAction = mipsdk.Action;

namespace mipsdk.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void ValidConfigurationHasExplicitCloudAndPrivateCache()
    {
        using var config = TestConfig.Create();
        config.Validate();
        Assert.Equal(Cloud.Commercial, config.Cloud);
        Assert.Equal($"https://login.microsoftonline.com/{TestConfig.Tenant}", config.BootstrapAuthority);
        Assert.Contains(TestConfig.Client, config.CachePath);
        Assert.True(Path.IsPathFullyQualified(config.CachePath));
        Assert.False(config.EnableDiagnosticLogging);
        Assert.False(config.GenerateChangeAuditEvent);
        Assert.Equal(TestConfig.Client, config.CreateApplicationInfo().ApplicationId);
        Assert.Equal("http://localhost", config.RedirectUri);
    }

    [Theory]
    [InlineData("ida:ClientId", "")]
    [InlineData("ida:ClientId", "YOUR CLIENT ID")]
    [InlineData("ida:ClientId", "00000000-0000-0000-0000-000000000000")]
    [InlineData("ida:TenantGuid", "YOUR TENANT ID")]
    [InlineData("ida:IsMultiTenantApp", "maybe")]
    [InlineData("app:Name", "")]
    [InlineData("app:Version", "")]
    [InlineData("sdk:Cloud", "Unknown")]
    [InlineData("sdk:Cloud", "Custom")]
    [InlineData("sdk:Cloud", "123")]
    [InlineData("sdk:LogLevel", "Verbose")]
    [InlineData("sdk:ServiceConsent", "Yes")]
    [InlineData("sdk:EnableDiagnosticLogging", "yes")]
    [InlineData("sdk:GenerateChangeAuditEvent", "yes")]
    [InlineData("ida:RedirectUri", "https://example.invalid")]
    [InlineData("ida:RedirectUri", "http://localhost/#fragment")]
    [InlineData("ida:RedirectUri", "http://localhost/?query=true")]
    [InlineData("ida:AllowedAuthorityHosts:0", "https://login.microsoftonline.com")]
    [InlineData("ida:AllowedAuthorityHosts:0", "example.invalid")]
    [InlineData("ida:AllowedResources:0", "http://graph.microsoft.com")]
    [InlineData("ida:AllowedResources:0", "https://example.invalid")]
    public void InvalidConfigurationFailsBeforeSdkOrBrowser(string key, string value)
    {
        using var config = TestConfig.Create((key, value));
        var exception = Assert.Throws<InputValidationException>(config.Validate);
        Assert.DoesNotContain(TestConfig.Client, exception.Message);
        Assert.DoesNotContain(TestConfig.Tenant, exception.Message);
    }

    [Theory]
    [InlineData("Commercial", "login.microsoftonline.com", "https://graph.microsoft.com")]
    [InlineData("US_GCC", "login.microsoftonline.com", "https://graph.microsoft.com")]
    [InlineData("US_GCC_High", "login.microsoftonline.us", "https://graph.microsoft.us")]
    [InlineData("US_DoD", "login.microsoftonline.us", "https://dod-graph.microsoft.us")]
    [InlineData("China_01", "login.chinacloudapi.cn", "https://microsoftgraph.chinacloudapi.cn")]
    public void CloudControlsBootstrapAndEngine(string cloud, string host, string resource)
    {
        using var config = TestConfig.Create(("sdk:Cloud", cloud),
            ("ida:AllowedAuthorityHosts:0", host), ("ida:AllowedResources:0", resource),
            ("ida:IsMultiTenantApp", "true"), ("ida:TenantGuid", ""));
        config.Validate();
        Assert.Equal(host, config.AuthorityHost);
        Assert.Equal(resource, config.IdentityResource);
        Assert.EndsWith("/organizations", config.BootstrapAuthority);
        var identity = new Identity("synthetic-user@example.invalid");
        var engine = SdkAction.CreateEngineSettings(config, identity, Mock.Of<IAuthDelegate>());
        Assert.Equal(config.Cloud, engine.Cloud);
        Assert.Same(identity, engine.Identity);
        Assert.True(engine.ConfiguredFunctionality[FunctionalityFilterType.DoubleKeyProtection]);
    }

    [Theory]
    [InlineData(Consent.Accept)]
    [InlineData(Consent.AcceptAlways)]
    [InlineData(Consent.Reject)]
    public void ServiceConsentIsExplicit(Consent consent)
    {
        using var config = TestConfig.Create(("sdk:ServiceConsent", consent.ToString()));
        config.Validate();
        Assert.Equal(consent, new ConsentDelegateImpl(config.ServiceConsent).GetUserConsent("https://example.invalid"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiagnosticSettingsDoNotChangePublishingMode(bool logging)
    {
        using var config = TestConfig.Create(("sdk:EnableDiagnosticLogging", logging.ToString()),
            ("sdk:CachePath", Path.GetTempPath()));
        var settings = SdkAction.CreateMipConfiguration(config);
        Assert.False(settings.IsOfflineOnly);
        Assert.Null(settings.FeatureSettingsOverride);
        Assert.Equal(CacheStorageType.OnDiskEncrypted, settings.CacheStorageType);
        Assert.True(settings.DiagnosticOverride.IsMinimalTelemetryEnabled);
        Assert.Equal(logging, settings.DiagnosticOverride.IsTraceLoggingEnabled);
        Assert.Equal(logging, settings.DiagnosticOverride.IsLocalCachingEnabled);
        Assert.Equal(config.Cloud, settings.DiagnosticOverride.Cloud);
    }

    [Fact]
    public void ConfigurationPrecedenceIsDefaultsThenLocalThenEnvironment()
    {
        using var directory = new TestDirectory();
        WriteDefaults(directory);
        File.WriteAllText(directory.File("appsettings.Local.json"), """{"AppSettings":{"app:Name":"Local test name"}}""");
        using (var local = new AppConfig(directory.Path)) { Assert.Equal("Local test name", local.AppName); }
        const string key = "MIPSDK_AppSettings__app__Name";
        var previous = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, "Environment test name");
            using var config = new AppConfig(directory.Path);
            config.Validate();
            Assert.Equal("Environment test name", config.AppName);
        }
        finally { Environment.SetEnvironmentVariable(key, previous); }
    }

    [Theory]
    [InlineData("ida:AllowedResources", "empty")]
    [InlineData("ida:AllowedResources", "short")]
    [InlineData("ida:AllowedResources", "null")]
    [InlineData("ida:AllowedResources", "blank")]
    [InlineData("ida:AllowedAuthorityHosts", "empty")]
    [InlineData("ida:AllowedAuthorityHosts", "short")]
    [InlineData("ida:AllowedAuthorityHosts", "null")]
    [InlineData("ida:AllowedAuthorityHosts", "blank")]
    public void LocalAllowlistsReplaceWholeDefaultList(string key, string kind)
    {
        using var directory = new TestDirectory();
        WriteDefaults(directory);
        var required = key == "ida:AllowedResources" ? "https://graph.microsoft.com" : "login.microsoftonline.com";
        string[]? values = kind switch
        {
            "empty" => [],
            "short" => [required],
            "blank" => [" "],
            _ => null
        };
        WriteLocal(directory, key, values);
        using var config = new AppConfig(directory.Path);
        config.Validate();
        string[] expected = kind == "short" || key == "ida:AllowedAuthorityHosts" ? [required] : [];
        Assert.Equal(expected, GetAllowlist(config, key));
    }

    [Theory]
    [InlineData("ida:AllowedResources", false)]
    [InlineData("ida:AllowedResources", true)]
    [InlineData("ida:AllowedAuthorityHosts", false)]
    [InlineData("ida:AllowedAuthorityHosts", true)]
    public void EnvironmentAllowlistsReplaceWholeLocalList(string key, bool omitRequiredEndpoint)
    {
        using var directory = new TestDirectory();
        WriteDefaults(directory);
        var resources = key == "ida:AllowedResources";
        var required = resources ? "https://graph.microsoft.com" : "login.microsoftonline.com";
        var other = resources ? "https://example.invalid" : "example.invalid";
        WriteLocal(directory, key, new[] { required, other });
        var variable = $"MIPSDK_AppSettings__{key.Replace(":", "__")}__0";
        var previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            var value = omitRequiredEndpoint ? other : required;
            Environment.SetEnvironmentVariable(variable, value);
            using var config = new AppConfig(directory.Path);
            Assert.Equal([value], GetAllowlist(config, key));
            if (omitRequiredEndpoint) { Assert.Throws<InputValidationException>(config.Validate); }
            else { config.Validate(); }
        }
        finally { Environment.SetEnvironmentVariable(variable, previous); }
    }

    [Theory]
    [InlineData("ida:AllowedResources", "scalar")]
    [InlineData("ida:AllowedResources", "nested")]
    [InlineData("ida:AllowedResources", "named")]
    [InlineData("ida:AllowedAuthorityHosts", "scalar")]
    [InlineData("ida:AllowedAuthorityHosts", "nested")]
    [InlineData("ida:AllowedAuthorityHosts", "named")]
    public void MalformedLocalAllowlistsDoNotFallBackToDefaults(string key, string kind)
    {
        using var directory = new TestDirectory();
        WriteDefaults(directory);
        var validValue = key == "ida:AllowedResources" ? "https://graph.microsoft.com" : "login.microsoftonline.com";
        object value = kind switch
        {
            "scalar" => "synthetic-invalid-list",
            "named" => new Dictionary<string, object> { ["entry"] = validValue },
            _ => new Dictionary<string, object> { ["0"] = new { nested = validValue } }
        };
        WriteLocal(directory, key, value);
        using var config = new AppConfig(directory.Path);
        Assert.Throws<InputValidationException>(config.Validate);
    }

    [Theory]
    [InlineData("ida:AllowedResources")]
    [InlineData("ida:AllowedAuthorityHosts")]
    public void MalformedEnvironmentAllowlistsDoNotFallBackToJson(string key)
    {
        using var directory = new TestDirectory();
        WriteDefaults(directory);
        var variable = $"MIPSDK_AppSettings__{key.Replace(":", "__")}";
        var previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, "synthetic-invalid-list");
            using var config = new AppConfig(directory.Path);
            Assert.Throws<InputValidationException>(config.Validate);
        }
        finally { Environment.SetEnvironmentVariable(variable, previous); }
    }

    [Fact]
    public void MissingAllowlistsKeepTheirExistingFallbacks()
    {
        using var directory = new TestDirectory();
        WriteDefaults(directory, includeAllowlists: false);
        using var config = new AppConfig(directory.Path);
        config.Validate();
        Assert.Equal(["login.microsoftonline.com"], config.AllowedAuthorityHosts);
        Assert.Empty(config.AllowedResources);
    }

    [Fact]
    public void MissingDefaultsAreNotSilentlyIgnored()
    {
        using var directory = new TestDirectory();
        Assert.Throws<FileNotFoundException>(() => new AppConfig(directory.Path));
    }

    [Fact]
    public void EmptyAllowlistsPreserveExistingFallbackSemantics()
    {
        using var config = TestConfig.Create(("ida:AllowedAuthorityHosts:0", ""),
            ("ida:AllowedResources:0", ""), ("ida:AllowedResources:1", ""),
            ("ida:RedirectUri", null));
        config.Validate();
        Assert.Equal(["login.microsoftonline.com"], config.AllowedAuthorityHosts);
        Assert.Empty(config.AllowedResources);
        Assert.Equal("http://localhost", config.RedirectUri);
    }

    private static IReadOnlyCollection<string> GetAllowlist(AppConfig config, string key) =>
        key == "ida:AllowedResources" ? config.AllowedResources : config.AllowedAuthorityHosts;

    private static void WriteDefaults(TestDirectory directory, bool includeAllowlists = true)
    {
        var defaults = TestConfig.Values()
            .Where(pair => !pair.Key.StartsWith("AppSettings:ida:Allowed", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key["AppSettings:".Length..], pair => (object?)pair.Value);
        if (includeAllowlists)
        {
            defaults["ida:AllowedResources"] = new[] { "https://graph.microsoft.com", "https://api.aadrm.com", "https://syncservice.o365syncservice.com" };
            defaults["ida:AllowedAuthorityHosts"] = new[] { "login.microsoftonline.com", "example.invalid" };
        }
        File.WriteAllText(directory.File("appsettings.json"), JsonSerializer.Serialize(new { AppSettings = defaults }));
    }

    private static void WriteLocal(TestDirectory directory, string key, object? value) =>
        File.WriteAllText(directory.File("appsettings.Local.json"),
            JsonSerializer.Serialize(new { AppSettings = new Dictionary<string, object?> { [key] = value } }));
}
