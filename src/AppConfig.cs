using System;
using Microsoft.Extensions.Configuration;

    public class AppConfig    
    {
        IConfiguration configuration;
        public AppConfig()
        {
             configuration = new ConfigurationBuilder().AddJsonFile("appsettings.json", false).Build();                       
        }

        public string GetClientId()
        {
            return configuration.GetSection("AppSettings")["ida:ClientId"];
        }

        public string GetRedirectUri()
        {              
              return configuration.GetSection("AppSettings")["ida:RedirectUri"];
        }

        public string GetIsMultiTenantApp()
        {
            return configuration.GetSection("AppSettings")["ida:IsMultiTenantApp"];
        }

        public string GetTenantId()
        {
            return configuration.GetSection("AppSettings")["ida:TenantGuid"];
        }

        public string GetAppName()
        {
             return configuration.GetSection("AppSettings")["app:Name"];
        }

        public string GetAppVersion()
        {
             return configuration.GetSection("AppSettings")["app:Version"]; 
        }
    }
