using System;
using System.Collections.Generic;
using System.IO;

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

        public struct TestResult
        {
            public string Filename;
            public string Extension;
            public bool ResultLabelSet;
            public bool ResultLabelRead;
            public string ExceptionMessageLabelSet;
            public string ExceptionMessageLabelRead;
        }

        static int Main(string[] args)
        {
            AppConfig config = new AppConfig();
            clientId = config.GetClientId();
            appName = config.GetAppName();
            appVersion = config.GetAppVersion();

            // List to store results
            List<TestResult> testResults = new List<TestResult>();


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

            // List all labels available to the engine created in Action
  
            System.Console.WriteLine("Input file path: ");
            string inputPath = System.Console.ReadLine();
            
            System.Console.WriteLine("Output file path: ");
            string outputPath = Console.ReadLine();

            if(!System.IO.File.Exists(inputPath))
            {
                System.Console.WriteLine("Invalid path.");
                return -1;
            }

            Action.FileOptions options = new Action.FileOptions();
            options.FileName = inputPath;
            options.OutputName = outputPath;
            options.GenerateChangeAuditEvent = false;
            options.IsAuditDiscoveryEnabled = false;
            options.DataState = DataState.Rest;

            System.Console.WriteLine("Removing protection from {0}:", options.FileName);            
            action.RemoveProtection(options);

            System.Console.WriteLine("Press a key to quit.");
            System.Console.ReadKey();      
            return 0;                                             
        }
    }
}
