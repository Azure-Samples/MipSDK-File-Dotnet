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

            // List all labels available to the engine created in Action
            IEnumerable<Label> labels = action.ListLabels();          

            foreach (var label in labels)
            {
                Console.WriteLine(string.Format("{0} - {1}", label.Name, label.Id));

                if (label.Children.Count > 0)
                {
                    foreach (Label child in label.Children)
                    {
                        Console.WriteLine(string.Format("\t{0} - {1}", child.Name, child.Id));
                    }
                }
            }

            Console.Write("Enter a label ID: ");
            string labelId = Console.ReadLine();

            Console.Write("Enter an input file path: ");
            string inputPath = Console.ReadLine();
            if(!System.IO.File.Exists(inputPath))
            {
                Console.WriteLine("Invalid path.");
                return -1;
            }

            Console.Write("Enter an output file path: ");
            string outputPath = Console.ReadLine();

            Action.FileOptions options = new Action.FileOptions()
            {
                FileName = inputPath,
                OutputName = outputPath,
                LabelId = labelId,
                DataState = DataState.Rest,
                AssignmentMethod = AssignmentMethod.Standard,
                IsAuditDiscoveryEnabled = false,
                GenerateChangeAuditEvent = false                
            };
            
            System.Console.WriteLine("Writing label {0} to {1}.", labelId, inputPath);
            // Write label.
            action.SetLabel(options);      
            System.Console.WriteLine("Wrote label.");
            
            System.Console.WriteLine("Reading label and protection from file.");            
            options.FileName = options.OutputName;

            Action action2 = new Action(appInfo);
            ContentLabel contentLabel = action2.GetLabel(options);
            
            Console.WriteLine(string.Format("File Label: {0} \r\nIsProtected: {1}", contentLabel.Label.Name, contentLabel.IsProtectionAppliedFromLabel.ToString()));
            System.Console.WriteLine("Press a key to exit.");
            Console.ReadKey();
            return 0;                  
        }        
    }
}
