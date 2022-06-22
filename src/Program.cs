using System;
using System.Collections.Generic;

using Microsoft.InformationProtection;
using Microsoft.InformationProtection.Exceptions;
using Microsoft.InformationProtection.File;
using Microsoft.InformationProtection.Policy;
using Microsoft.InformationProtection.Protection;

namespace mipsdk
{
    public class Program
    {
        private static string clientId;
        private static string appName;
        private static string appVersion;

        static int Main(string[] args)
        {
           AppConfig config = new AppConfig();
           clientId = config.GetClientId();
           appName = config.GetAppName();
           appVersion = config.GetAppVersion();

           ApplicationInfo appInfo = new ApplicationInfo()
            {
                // ApplicationId should ideally be set to the same ClientId found in the Azure AD App Registration.
                // This ensures that the clientID in AAD matches the AppId reported in AIP Analytics.
                ApplicationId = clientId,
                ApplicationName = appName,
                ApplicationVersion = appVersion
            };

            // Initialize Action class, passing in AppInfo.
            Action action = new Action(appInfo);
            Console.Write("Enter the path for a message.rpmsg file: ");

            string inputPath = Console.ReadLine();


            if(!System.IO.File.Exists(inputPath) || System.IO.Path.GetExtension(inputPath) != ".rpmsg")
            {
                Console.WriteLine("Invalid path.");
                return -1;
            }

            Action.FileOptions options = new Action.FileOptions()
            {
                FileName = inputPath,
                IsAuditDiscoveryEnabled = false,
                GenerateChangeAuditEvent = false                
            };
            
            
            System.Console.WriteLine("Getting decrypted message.rpmsg contents...");
            action.InspectRpmsg(options);

           
            System.Console.WriteLine("Press a key to exit.");
            Console.ReadKey();
            return 0;                  
        }      
    }
}
