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

    [Fact]
    public void MissingDefaultsAreNotSilentlyIgnored()
    {
        using var directory = new TestDirectory();
        Assert.Throws<FileNotFoundException>(() => new AppConfig(directory.Path));
    }

    [Fact]
    public void MissingRedirectDefaultsToHttpLoopback()
    {
        using var config = TestConfig.Create(("ida:RedirectUri", null));
        config.Validate();
        Assert.Equal("http://localhost", config.RedirectUri);
    }

    private static void WriteDefaults(TestDirectory directory)
    {
        var defaults = TestConfig.Values()
            .ToDictionary(pair => pair.Key["AppSettings:".Length..], pair => (object?)pair.Value);
        File.WriteAllText(directory.File("appsettings.json"), JsonSerializer.Serialize(new { AppSettings = defaults }));
    }
}
