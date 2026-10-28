using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensibility;
using Moq;

namespace mipsdk.Tests;

public sealed class AuthenticationTests
{
    [Theory]
    [InlineData("common")]
    [InlineData("organizations")]
    [InlineData(TestConfig.Tenant)]
    public void SingleTenantChallengesStayPinned(string tenant)
    {
        using var config = TestConfig.Create();
        using var auth = new AuthDelegateImpl(config);
        Assert.Equal($"https://login.microsoftonline.com/{TestConfig.Tenant}",
            auth.ValidateAuthority($"https://login.microsoftonline.com/{tenant}/"));
    }

    [Theory]
    [InlineData("http://login.microsoftonline.com/common")]
    [InlineData("https://login.microsoftonline.com/consumers")]
    [InlineData("https://login.microsoftonline.com/")]
    [InlineData("https://login.microsoftonline.com/common/extra")]
    [InlineData("https://login.microsoftonline.com/common?secret=synthetic")]
    [InlineData("https://synthetic@login.microsoftonline.com/common")]
    [InlineData("https://login.microsoftonline.com:444/common")]
    [InlineData("relative")]
    [InlineData("https://login.microsoftonline.com/" + TestConfig.OtherTenant)]
    public void InvalidOrCrossTenantAuthoritiesAreRejected(string authority)
    {
        using var config = TestConfig.Create();
        using var auth = new AuthDelegateImpl(config);
        var error = Assert.Throws<InputValidationException>(() => auth.ValidateAuthority(authority));
        Assert.DoesNotContain("synthetic", error.Message);
    }

    [Theory]
    [InlineData("common")]
    [InlineData(TestConfig.Tenant)]
    public void SdkAuthorityAliasRetainsSingleTenantBinding(string tenant)
    {
        using var config = TestConfig.Create();
        using var auth = new AuthDelegateImpl(config);
        Assert.Equal($"https://login.windows.net/{TestConfig.Tenant}",
            auth.ValidateAuthority($"https://login.windows.net/{tenant}"));
    }

    [Fact]
    public void MultiTenantChallengesRetainOrganizationalTenant()
    {
        using var config = TestConfig.Create(("ida:IsMultiTenantApp", "true"));
        using var auth = new AuthDelegateImpl(config);
        Assert.Equal($"https://login.microsoftonline.com/{TestConfig.OtherTenant}",
            auth.ValidateAuthority($"https://login.microsoftonline.com/{TestConfig.OtherTenant}"));
    }

    [Theory]
    [InlineData("https://graph.microsoft.com", "https://graph.microsoft.com/.default")]
    [InlineData("https://graph.microsoft.com/", "https://graph.microsoft.com/.default")]
    [InlineData("https://GRAPH.microsoft.com/", "https://graph.microsoft.com/.default")]
    [InlineData("https://psor.o365syncservice.com", "https://psor.o365syncservice.com/.default")]
    public void ResourcesNormalizeToOneDefaultScope(string resource, string scope)
    {
        using var config = TestConfig.Create();
        using var auth = new AuthDelegateImpl(config);
        Assert.Equal(scope, auth.BuildDefaultScope(resource));
    }

    [Theory]
    [InlineData("http://graph.microsoft.com")]
    [InlineData("https://graph.microsoft.com/#fragment")]
    [InlineData("https://graph.microsoft.com?query=true")]
    [InlineData("https://graph.microsoft.com/.default")]
    [InlineData("relative")]
    public void InvalidResourcesAreRejected(string resource)
    {
        using var config = TestConfig.Create();
        using var auth = new AuthDelegateImpl(config);
        Assert.Throws<InputValidationException>(() => auth.BuildDefaultScope(resource));
    }

    [Fact]
    public void SdkHttpsAudienceDoesNotRequireSampleConfiguration()
    {
        using var config = TestConfig.Create();
        using var auth = new AuthDelegateImpl(config);
        Assert.Equal("https://example.invalid/resource/.default", auth.BuildDefaultScope("https://example.invalid/resource/"));
    }

    [Fact]
    public void CachedAccountSelectionNeverChoosesAnArbitraryUser()
    {
        var first = Account("first@example.invalid", "first.home");
        var second = Account("second@example.invalid", "second.home");
        Assert.Null(AuthDelegateImpl.SelectAccount([first, second], null, null));
        Assert.Same(second, AuthDelegateImpl.SelectAccount([first, second], null, "SECOND@example.invalid"));
        Assert.Same(first, AuthDelegateImpl.SelectAccount([first, second], "first.home", null));
        Assert.Null(AuthDelegateImpl.SelectAccount([first, second], "first.home", "second@example.invalid"));
        Assert.Null(AuthDelegateImpl.SelectAccount([first], "missing.home", null));
        Assert.Throws<InvalidOperationException>(() =>
            AuthDelegateImpl.SelectAccount([first, Account(first.Username, "another.home")], null, first.Username));
    }

    private static IAccount Account(string username, string id)
    {
        var account = new Mock<IAccount>();
        account.SetupGet(value => value.Username).Returns(username);
        account.SetupGet(value => value.HomeAccountId).Returns(new AccountId(id));
        return account.Object;
    }

    [Fact]
    public async Task RealMsalReusesItsClientAndCacheAndForwardsClaimsWithoutNetwork()
    {
        using var config = TestConfig.Create();
        using var service = new SyntheticIdentityService();
        var real = PublicClientApplicationBuilder.Create(config.ClientId)
            .WithAuthority(config.BootstrapAuthority, validateAuthority: true)
            .WithRedirectUri("http://localhost")
            .WithHttpClientFactory(service)
            .Build();
        var client = new Mock<IPublicClientApplication>();
        client.Setup(value => value.GetAccountsAsync()).Returns(() => real.GetAccountsAsync());
        client.Setup(value => value.AcquireTokenSilent(It.IsAny<IEnumerable<string>>(), It.IsAny<IAccount>()))
            .Returns((IEnumerable<string> scopes, IAccount account) => real.AcquireTokenSilent(scopes, account));
        client.Setup(value => value.AcquireTokenInteractive(It.IsAny<IEnumerable<string>>()))
            .Returns((IEnumerable<string> scopes) => real.AcquireTokenInteractive(scopes).WithCustomWebUi(service));
        using var auth = new AuthDelegateImpl(config, client.Object);
        var identity = await auth.GetUserIdentityAsync();
        Assert.Equal(SyntheticIdentityService.Username, identity.Email);
        Assert.Single(service.TokenRequests);
        var cached = await auth.AcquireTokenAsync(config.BootstrapAuthority, config.IdentityResource, null, identity.Email);
        Assert.Equal("synthetic-access-token", cached.AccessToken);
        Assert.Single(service.TokenRequests);
        const string claims = """{"access_token":{"synthetic_claim":{"essential":true}}}""";
        var rms = auth.AcquireToken(identity, "https://login.windows.net/common", "https://api.aadrm.com", claims);
        Assert.Equal("synthetic-access-token", rms);
        Assert.Equal(2, service.TokenRequests.Count);
        Assert.Contains(Uri.EscapeDataString(claims), service.TokenRequests[1].Body);
        Assert.All(service.TokenRequests, request => Assert.Contains(TestConfig.Tenant, request.Uri.AbsolutePath));
        Assert.All(service.TokenRequests, request => Assert.Equal("login.microsoftonline.com", request.Uri.Host));
        client.Verify(value => value.AcquireTokenInteractive(It.IsAny<IEnumerable<string>>()), Times.Once);
    }

    [Theory]
    [InlineData("different-user@example.invalid", TestConfig.Tenant)]
    [InlineData(SyntheticIdentityService.Username, TestConfig.OtherTenant)]
    public async Task TokenResultsMustMatchIntendedUserAndTenant(string username, string tenant)
    {
        using var config = TestConfig.Create();
        using var service = new SyntheticIdentityService { UsernameToReturn = username, TenantToReturn = tenant };
        var real = PublicClientApplicationBuilder.Create(config.ClientId).WithAuthority(config.BootstrapAuthority)
            .WithRedirectUri("http://localhost").WithHttpClientFactory(service).Build();
        var client = new Mock<IPublicClientApplication>();
        client.Setup(value => value.GetAccountsAsync()).Returns(() => real.GetAccountsAsync());
        client.Setup(value => value.AcquireTokenSilent(It.IsAny<IEnumerable<string>>(), It.IsAny<IAccount>()))
            .Returns((IEnumerable<string> scopes, IAccount account) => real.AcquireTokenSilent(scopes, account));
        client.Setup(value => value.AcquireTokenInteractive(It.IsAny<IEnumerable<string>>()))
            .Returns((IEnumerable<string> scopes) => real.AcquireTokenInteractive(scopes).WithCustomWebUi(service));
        using var auth = new AuthDelegateImpl(config, client.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            auth.AcquireTokenAsync(config.BootstrapAuthority, config.IdentityResource, null, SyntheticIdentityService.Username));
    }

    [Fact]
    public async Task DisposedAuthenticationRejectsTokenAcquisition()
    {
        using var config = TestConfig.Create();
        var auth = new AuthDelegateImpl(config);
        auth.Dispose();
        auth.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => auth.AcquireTokenAsync(
            config.BootstrapAuthority, config.IdentityResource, null));
    }
}

internal sealed class SyntheticIdentityService : HttpMessageHandler, IMsalHttpClientFactory, ICustomWebUi
{
    public const string Username = "synthetic-user@example.invalid";
    private readonly HttpClient client;
    public string UsernameToReturn { get; init; } = Username;
    public string TenantToReturn { get; init; } = TestConfig.Tenant;
    public List<(Uri Uri, string Body)> TokenRequests { get; } = [];

    public SyntheticIdentityService() => client = new HttpClient(this, disposeHandler: false);
    public HttpClient GetHttpClient() => client;

    public Task<Uri> AcquireAuthorizationCodeAsync(Uri authorizationUri, Uri redirectUri, CancellationToken cancellationToken)
    {
        var state = authorizationUri.Query.TrimStart('?').Split('&')
            .Select(value => value.Split('=', 2)).Single(pair => pair[0] == "state")[1];
        return Task.FromResult(new Uri($"{redirectUri}?code=synthetic-code&state={state}"));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Assert.Equal("login.microsoftonline.com", request.RequestUri!.Host);
        string json;
        if (request.RequestUri.AbsolutePath.Contains("discovery/instance"))
        {
            json = JsonSerializer.Serialize(new
            {
                tenant_discovery_endpoint = $"https://login.microsoftonline.com/{TestConfig.Tenant}/v2.0/.well-known/openid-configuration",
                metadata = new[] { new
                {
                    preferred_network = "login.microsoftonline.com",
                    preferred_cache = "login.microsoftonline.com",
                    aliases = new[] { "login.microsoftonline.com" }
                }}
            });
        }
        else if (request.RequestUri.AbsolutePath.EndsWith("/token"))
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            TokenRequests.Add((request.RequestUri, body));
            var payload = Encode(new
            {
                aud = TestConfig.Client, iss = $"https://login.microsoftonline.com/{TenantToReturn}/v2.0",
                tid = TenantToReturn, oid = TestConfig.Client, sub = "synthetic-subject",
                preferred_username = UsernameToReturn, name = "Synthetic user", ver = "2.0",
                iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
            });
            json = JsonSerializer.Serialize(new
            {
                token_type = "Bearer", access_token = "synthetic-access-token", expires_in = 3600,
                refresh_token = "synthetic-refresh-token",
                scope = body.Contains("api.aadrm.com") ? "https://api.aadrm.com/.default" : "https://graph.microsoft.com/.default",
                id_token = $"{Encode(new { alg = "none", typ = "JWT" })}.{payload}.synthetic",
                client_info = Encode(new { uid = TestConfig.Client, utid = TenantToReturn })
            });
        }
        else { throw new InvalidOperationException("Unexpected synthetic identity request."); }
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static string Encode(object value) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    protected override void Dispose(bool disposing)
    {
        if (disposing) { client.Dispose(); }
        base.Dispose(disposing);
    }
}
