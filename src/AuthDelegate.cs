using System;
using Microsoft.InformationProtection;
using Microsoft.InformationProtection.Exceptions;
using Microsoft.Identity.Client;
using System.Linq;
using System.Threading.Tasks;

public class AuthDelegateImpl : IAuthDelegate
{
    AppConfig config = new AppConfig();

    private static bool isMultitenantApp;
    private static string tenant;
    private ApplicationInfo appInfo;

    // Define MSAL scopes.
    // As of the 1.7 release, the two services backing the MIP SDK, RMS and MIP Sync Service, provide resources instead of scopes.
    // The List<string> entities below will be used to map the resources to scopes and to pass those scopes to Azure AD via MSAL.

    public AuthDelegateImpl(ApplicationInfo appInfo)
    {
        //redirectUri = config.GetRedirectUri();        
        isMultitenantApp = Convert.ToBoolean(config.GetIsMultiTenantApp());
        tenant = config.GetTenantId();
        this.appInfo = appInfo;
    }

    /// <summary>
    /// AcquireToken is called by the SDK when auth is required for an operation. 
    /// Adding or loading an IFileEngine is typically where this will occur first.
    /// The SDK provides all three parameters below.Identity from the EngineSettings.
    /// Authority and resource are provided from the 401 challenge.
    /// The SDK cares only that an OAuth2 token is returned.How it's fetched isn't important.
    /// In this sample, we fetch the token using Active Directory Authentication Library(ADAL).
    /// </summary>
    /// <param name="identity"></param>
    /// <param name="authority"></param>
    /// <param name="resource"></param>
    /// <returns>The OAuth2 token for the user</returns>
    public string AcquireToken(Identity identity, string authority, string resource, string claims)
    {   
        if (config.GetIsConfidentialClient())
        {
            return AcquireAppTokenAsync(authority, resource, claims).Result.AccessToken;
        }
        else
        {
            return AcquireUserTokenAsync(authority, resource, claims).Result.AccessToken;
        }
    }

    /// <summary>
    /// Implements token acquisition logic via the Microsoft Authentication Library.
    /// 
    /// /// </summary>
    /// <param name="identity"></param>
    /// <param name="authority"></param>
    /// <param name="resource"></param>
    /// <param name="claims"></param>
    /// <returns></returns>
    public async Task<AuthenticationResult> AcquireUserTokenAsync(string authority, string resource, string claims, bool isMultiTenantApp = true)
    {
        AuthenticationResult result = null;
        IPublicClientApplication _app = null;

        // Create an auth context using the provided authority and token cache
        if (_app == null)
        {
            if (isMultitenantApp)
                _app = PublicClientApplicationBuilder.Create(appInfo.ApplicationId)
                    .WithAuthority(authority)
                    .WithDefaultRedirectUri()
                    .Build();
            else
            {
                if (authority.ToLower().Contains("common"))
                {
                    var authorityUri = new Uri(authority);
                    authority = String.Format("https://{0}/{1}", authorityUri.Host, tenant);
                }
                _app = PublicClientApplicationBuilder.Create(appInfo.ApplicationId)
                    .WithAuthority(authority)
                    .WithDefaultRedirectUri()
                    .Build();

            }
        }
        var accounts = await _app.GetAccountsAsync();//).GetAwaiter().GetResult();

        // Append .default to the resource passed in to AcquireToken().
        string[] scopes = new string[] { resource[resource.Length - 1].Equals('/') ? $"{resource}.default" : $"{resource}/.default" };

        try
        {
            result = await _app.AcquireTokenSilent(scopes, accounts.FirstOrDefault())
                .WithClaims(claims)
                .ExecuteAsync();
        }

        catch (MsalUiRequiredException ex)
        {
            System.Console.WriteLine(ex.Message);
            result = _app.AcquireTokenInteractive(scopes)
                .WithClaims(claims)
                .ExecuteAsync()
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
        }

        // Return the token. The token is sent to the resource.                           
        return result;
    }

    public async Task<AuthenticationResult> AcquireAppTokenAsync(string authority, string resource, string claims, bool isMultiTenantApp = true)
    {
        AuthenticationResult result = null;
        IConfidentialClientApplication _app = null;

        // Create an auth context using the provided authority and token cache
        if (_app == null)
        {
            if (authority.ToLower().Contains("common"))
            {
                var authorityUri = new Uri(authority);
                authority = String.Format("https://{0}/{1}", authorityUri.Host, tenant);
            }
            System.Console.WriteLine("Builder.");
            _app = ConfidentialClientApplicationBuilder.Create(appInfo.ApplicationId)
                .WithClientSecret(config.GetAppSecret())
                .WithRedirectUri(config.GetRedirectUri())
                .Build();
        }

        var accounts = await _app.GetAccountsAsync();//).GetAwaiter().GetResult();

        // Append .default to the resource passed in to AcquireToken().
        string[] scopes = new string[] { resource[resource.Length - 1].Equals('/') ? $"{resource}.default" : $"{resource}/.default" };

        try
        {
            System.Console.WriteLine("Getting token.");
            result = await _app.AcquireTokenForClient(scopes)
                .WithAuthority(authority)
                .WithClaims(claims)
                .ExecuteAsync();
        }

        catch (MsalClientException ex)
        {
            System.Console.WriteLine(ex.Message);
        }

        // Return the token. The token is sent to the resource.                           
        return result;
    }

    /// <summary>
    /// The GetUserIdentity() method is used to pre-identify the user and obtain the UPN. 
    /// The UPN is later passed set on FileEngineSettings for service location.
    /// </summary>
    /// <returns>Microsoft.InformationProtection.Identity</returns>
    public string GetUserIdentity()
    {
        AuthenticationResult result = AcquireUserTokenAsync("https://login.microsoftonline.com/common", "https://graph.microsoft.com", null).Result;
        return result.Account.Username;
    }
}
