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


            // Get a label ID and put it here. 
            string testLabel = config.GetLabelId();

            // Set your working directory here
            string directory = config.GetWorkingPath();

            // Clean up previous runs
            foreach (var modifiedFile in Directory.EnumerateFiles(directory, "*modified*.*"))
            {
                File.Delete(modifiedFile);
            }

            // Get all files in the working directory, add to new list.
            var files = System.IO.Directory.EnumerateFiles(directory);
            List<string> fileNames = new List<string>();

            foreach (string f in files)
            {
                fileNames.Add(f);
            }

            System.Console.WriteLine("Files: {0}", fileNames.Count);

            // Iterate through all files and try to set and read label
            foreach (var file in fileNames)
            {
                string filePart = Path.GetFileNameWithoutExtension(file);
                string extension = Path.GetExtension(file);
                string outputName = String.Concat(filePart, "_modified", extension);

                System.Console.WriteLine("OutputName: {0}", outputName);

                TestResult testResult = new TestResult();

                testResult.Filename = file;
                testResult.Extension = extension;

                bool result = false;

                // Set Label on File
                System.Console.WriteLine("Attempting to write label {0} to file: {1}", testLabel, file);

                Action.FileOptions options = new Action.FileOptions()
                {
                    ActionSource = ActionSource.Manual,
                    DataState = DataState.Rest,
                    FileName = file,
                    OutputName = Path.Combine(directory, outputName),
                    GenerateChangeAuditEvent = false,
                    LabelId = testLabel,
                    AssignmentMethod = AssignmentMethod.Standard,
                    IsAuditDiscoveryEnabled = false
                };

                try
                {
                    result = action.SetLabel(options);
                    testResult.ResultLabelSet = true;
                }

                catch (Exception ex)
                {
                    System.Console.WriteLine("Failed to set label with exception: {0}", ex.Message);
                    System.Console.WriteLine(ex.StackTrace);

                    testResult.ResultLabelSet = false;
                    testResult.ExceptionMessageLabelSet = ex.Message;
                }

                options.FileName = options.OutputName;

                if (result)
                {
                    try
                    {
                        action.GetLabel(options);
                        testResult.ResultLabelRead = true;
                    }

                    catch (Exception ex)
                    {
                        System.Console.WriteLine("Failed to set label with exception: {0}", ex.Message);
                        System.Console.WriteLine(ex.StackTrace);

                        testResult.ResultLabelRead = false;
                        testResult.ExceptionMessageLabelRead = ex.Message;
                    }
                }

                testResults.Add(testResult);
            }


            OutputResults(testResults);

            return 0;
        }

        public static void OutputResults(List<TestResult> testResults)
        {
            System.Console.WriteLine("************************************");
            System.Console.Write("| {0}", "File Name");
            PadLine(50 - "File Name".Length);
            System.Console.Write("{0}", "Extension");
            PadLine(10 - "Extension".Length);
            System.Console.Write("{0}", "Set Result");
            PadLine(10 - "Set Result".Length);
            System.Console.Write("{0}", "ReadResult");
            PadLine(10 - "ReadResult".Length);
            System.Console.WriteLine("");

            foreach (var result in testResults)
            {
                System.Console.Write("| {0}", result.Filename);
                PadLine(50 - result.Filename.Length);
                System.Console.Write("{0}", result.Extension);
                PadLine(10 - result.Extension.Length);
                System.Console.Write("{0}", result.ResultLabelSet.ToString());
                PadLine(10 - result.ResultLabelSet.ToString().Length);
                System.Console.Write("{0}", result.ResultLabelRead.ToString());
                PadLine(10 - result.ResultLabelRead.ToString().Length);
                System.Console.WriteLine("");
            }
        }

        public static void PadLine(int pad)
        {
            for (int i = 0; i < pad; i++)
            {
                System.Console.Write(" ");
            }
            System.Console.Write("| ");
        }
    }
}
