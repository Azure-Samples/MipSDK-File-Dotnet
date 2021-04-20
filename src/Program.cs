using System;
using Microsoft.InformationProtection.File;
using Microsoft.InformationProtection;
using Microsoft.InformationProtection.Policy;
using System.Collections.Generic;

namespace mipsdk
{
    class Program
    {
        
        private static string clientId;
        private static string appName;
        private static string appVersion;
        private IFileEngine engine;
        private IFileHandler handler;

        static void Main(string[] args)
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

        }

        public bool SetLabel(string labelId)
        {
            LabelingOptions options = new LabelingOptions();
            options.AssignmentMethod = AssignmentMethod.Standard;
                                    
            // First Attempt to label
            try 
            {
                handler.SetLabel(engine.GetLabelById(labelId), options, new ProtectionSettings());
            }

            // Catch the adhoc proteciton required, generate protection descriptor, and try again.
            catch (Microsoft.InformationProtection.Exceptions.AdhocProtectionRequiredException ex)
            {                
                // Generate a list of all users who should have the set of roles. 
                List<string> userList = new List<string>()
                {
                    "alice@milt0r.com","bob@milt0r.com"
                };

                // Roles for the users
                List<string> rolelist = new List<string>()
                {
                    "OWNER"
                };
                
                List<UserRoles> userRolesList = new List<UserRoles>()
                {
                    new UserRoles(userList, rolelist)
                };
                
                // Use userroles to generate descriptor. 
                ProtectionDescriptor descriptor = new ProtectionDescriptor(userRolesList);                
                // Set protection using descriptor
                handler.SetProtection(descriptor, new ProtectionSettings());

                //try to set label
                handler.SetLabel(engine.GetLabelById(labelId), options, new ProtectionSettings());
                return false;
            }

            catch (Microsoft.InformationProtection.Exceptions.JustificationRequiredException ex)
            {
                return false;
            }
        return true;
        }
    }
}
