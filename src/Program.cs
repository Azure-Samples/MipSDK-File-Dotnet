using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CommandLine;
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

        public class Options
        {
            [Option('l', "list-labels", Required = false, HelpText = "List all labels.", Default = false)]
            public bool ListLabels { get; set; }

            [Option('r', "read-file-data", Required = false, HelpText = "Read label and protection information from file.", Default = false)]
            public bool ReadFileData { get; set; }

            [Option('w', "write-label", Required = false, HelpText = "Write label to file.", Default = false)]
            public bool WriteLabel { get; set; }

            [Option('i', "label-id", Required = false, HelpText = "Label ID to write to file.")]
            public string LabelId { get; set; }

            [Option('f', "input-file", Required = false, HelpText = "Input file path.")]
            public string InputFile { get; set; }

            [Option('o', "output-file", Required = false, HelpText = "Output file path.")]
            public string OutputFile { get; set; }

            [Option('d', "delegated-user", Required = false, HelpText = "User to read or write on behalf of.")]
            public string DelegatedUser { get; set; }

            [Option('c', "content-format", Required = false, HelpText = "Comma separated list of content formats to filter by.")]
            public string ContentFormats { get; set; }

            [Option('g', "get-rights-for-label-id", Required = false, HelpText = "Get rights for label id.")]
            public string GetRightsForLabelId { get; set; }

            [Option('v', "verbose", Required = false, HelpText = "Prints all messages to standard output.", Default = false)]
            public bool Verbose { get; set; }
        }

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
            Action action = null;

            _ = Parser.Default.ParseArguments<Options>(args)
                   .WithParsed<Options>(o =>
                   {
                       if (string.IsNullOrEmpty(o.DelegatedUser))
                       {
                           action = new Action(appInfo);
                       }
                       else
                       {
                           action = new Action(appInfo, o.DelegatedUser);
                       }

                       if (o.ListLabels)
                       {
                           if (!string.IsNullOrEmpty(o.ContentFormats))
                               ListLabels(action, o.ContentFormats);
                           else
                               ListLabels(action);
                       }

                       if (o.WriteLabel && string.IsNullOrEmpty(o.InputFile) == false && string.IsNullOrEmpty(o.OutputFile) == false && string.IsNullOrEmpty(o.LabelId) == false)
                       {
                           System.Console.WriteLine("Setting label.");
                           SetLabel(action, o.LabelId, o.InputFile, o.OutputFile);
                       }

                       if (o.ReadFileData && string.IsNullOrEmpty(o.InputFile) == false)
                       {
                           System.Console.WriteLine("Read props");
                           ReadMipProperties(action, o.InputFile);
                       }

                       if (string.IsNullOrEmpty(o.GetRightsForLabelId) == false)
                       {
                           System.Console.WriteLine("Get rights for label id");
                           var rights = action.GetRightsForLabelId(o.GetRightsForLabelId);
                           foreach (var right in rights)
                           {
                               System.Console.WriteLine(right);
                           }
                       }

                       else
                       {
                           Console.WriteLine("Quick Start Example!");
                       }
                   });

            return 0;
        }

        public static void ListLabels(Action action, string contentFormats = null)
        {

            List<string> contentFormatsList = new();
            if (string.IsNullOrEmpty(contentFormats) == false)
                contentFormatsList = contentFormats.Split(",").ToList();

            // List all labels available to the engine created in Action
            IEnumerable<Label> labels = action.ListLabels();

            foreach (var label in labels)
            {
                if (contentFormatsList.Count == 0 || label.ContentFormats.Intersect(contentFormatsList).Any())
                    Console.WriteLine(string.Format("{0} - {1}", label.Name, label.Id));

                if (label.Children.Count > 0)
                {
                    foreach (Label child in label.Children)
                    {
                        if (child.ContentFormats.Intersect(contentFormatsList).Any() || contentFormatsList.Count == 0)
                            Console.WriteLine(string.Format("\t{0} - {1}", child.Name, child.Id));
                    }
                }
            }
        }

        public static void ReadMipProperties(Action action, string inputPath, string delegatedUser = null)
        {
            Action.FileOptions options = new Action.FileOptions()
            {
                FileName = inputPath,
                DataState = DataState.Rest,
                AssignmentMethod = AssignmentMethod.Standard,
                IsAuditDiscoveryEnabled = false,
                GenerateChangeAuditEvent = false
            };

            System.Console.WriteLine("Reading label and protection from file.");

            ContentLabel contentLabel = action.GetLabel(options);
            Console.WriteLine(string.Format("File Label: {0} \r\nIsProtected: {1}", contentLabel.Label.Name, contentLabel.IsProtectionAppliedFromLabel.ToString()));


            Action.ProtectionDetails protectionDetails;

            try
            {
                protectionDetails = action.GetProtectionDetails(options);
                System.Console.WriteLine("Protection Details: {0}", protectionDetails.IsProtected.ToString());
                System.Console.WriteLine("Protection Owner: {0}", protectionDetails.TemplateId.ToString());
            }

            catch (Exception e)
            {
                System.Console.WriteLine(e.Message);
            }


        }

        public static void SetLabel(Action action, string labelId, string inputPath, string outputPath, string delegatedUser = null)
        {
            System.Console.WriteLine(labelId);
            System.Console.WriteLine(inputPath);
            System.Console.WriteLine(outputPath);

            if (!System.IO.File.Exists(inputPath))
            {
                Console.WriteLine("Invalid path.");
                throw new System.IO.FileNotFoundException();
            }

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

            // Write label.
            action.SetLabel(options);
            System.Console.WriteLine("Wrote label.");
        }
    }


}
