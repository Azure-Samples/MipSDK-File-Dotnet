using Microsoft.InformationProtection;
using Microsoft.InformationProtection.Exceptions;
using Microsoft.Identity.Client;

namespace mipsdk;

public static class Program
{
    public static Task<int> Main(string[] args) => RunAsync(args, () => new AppConfig());

    internal static async Task<int> RunAsync(string[] args, Func<AppConfig> loadConfiguration)
    {
        if (args is ["--help"] or ["-h"])
        {
            Console.WriteLine("MIP File SDK interactive sample\nUsage: mipsdk [--help | --check-config | --native-smoke]\nConfigure appsettings.Local.json or MIPSDK_ environment variables before labeling.\nExit codes: 0 success/no-op, 1 operation failure, 2 invalid configuration/input, 3 cancelled.");
            return 0;
        }
        if (args.Length > 1 || (args.Length == 1 && args[0] is not ("--check-config" or "--native-smoke")))
        {
            Console.Error.WriteLine("Unknown arguments. Use --help.");
            return 2;
        }

        try
        {
            if (args is ["--native-smoke"])
            {
                Action.InitializeNative();
                Console.WriteLine("Native MIP File SDK loaded. No authentication or policy access was attempted.");
                return 0;
            }

            using var config = LoadConfiguration(loadConfiguration);
            config.Validate();
            if (args is ["--check-config"])
            {
                Console.WriteLine("Configuration is valid. No authentication or policy access was attempted.");
                return 0;
            }

            if (Console.IsInputRedirected)
            {
                Console.Error.WriteLine("Labeling requires an interactive terminal and browser. Use --check-config for credential-free validation.");
                return 2;
            }

            Console.WriteLine($"Delegated browser sign-in; cloud: {config.Cloud}; service consent: {config.ServiceConsent}.\nSDK logs may contain identity and file metadata. Do not share caches or logs.");
            if (config.EnableDiagnosticLogging)
            {
                Console.Write("Diagnostic logging is enabled. Consent to diagnostic data collection? [y/N]: ");
                if (!string.Equals(Console.ReadLine(), "y", StringComparison.OrdinalIgnoreCase)) { return 3; }
            }

            var userPrincipalName = ReadRequired("Enter your organizational account UPN/email: ");
            using var action = await Action.CreateAsync(config, userPrincipalName);
            foreach (var label in action.ListLabels())
            {
                Console.WriteLine($"{label.Name} - {label.Id}");
            }

            var options = new Action.FileOptions
            {
                LabelId = ReadRequired("Enter an active file label ID: "),
                FileName = ReadRequired("Enter an input file path: "),
                OutputName = ReadRequired("Enter a new output file path: "),
                DataState = DataState.Rest,
                AssignmentMethod = AssignmentMethod.Standard,
                GenerateChangeAuditEvent = config.GenerateChangeAuditEvent
            };
            var result = await LabelInteractivelyAsync(action.SetLabelAsync, options, Console.In, Console.Out);
            Console.WriteLine(result.Changed
                ? $"Wrote and verified output. Protected: {result.IsProtected}."
                : $"No SDK change was needed; the input label/protection was verified. No output was created. Protected: {result.IsProtected}.");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Operation cancelled. No success was reported.");
            return 3;
        }
        catch (InputValidationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
        catch (ArgumentException)
        {
            Console.Error.WriteLine("Invalid input or SDK argument. Check configuration, label policy, recipient addresses, and output paths.");
            return 2;
        }
        catch (MsalException)
        {
            Console.Error.WriteLine("Sign-in failed. Check the application registration, redirect URI, cloud, tenant, and delegated permissions.");
            return 1;
        }
        catch (DllNotFoundException)
        {
            Console.Error.WriteLine("Native MIP libraries could not load. Check the x64 package, published assets, and platform prerequisites.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Operation failed ({ex.GetType().Name}). No success was reported. Check permissions, label policy, output paths, and native prerequisites. If an output exists after final verification failed, retain it for private investigation.");
            return 1;
        }
    }

    private static AppConfig LoadConfiguration(Func<AppConfig> load)
    {
        try { return load(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or FormatException)
        {
            throw new InputValidationException("Configuration could not be read. Check appsettings.json and appsettings.Local.json for valid JSON and readable permissions.");
        }
    }

    private static string ReadRequired(string prompt)
    {
        Console.Write(prompt);
        var value = Console.ReadLine();
        return string.IsNullOrWhiteSpace(value) ? throw new OperationCanceledException() : value.Trim();
    }

    internal static async Task<Action.LabelingResult> LabelInteractivelyAsync(
        Func<Action.FileOptions, Task<Action.LabelingResult>> label,
        Action.FileOptions options, TextReader input, TextWriter output)
    {
        while (true)
        {
            try { return await label(options); }
            catch (JustificationRequiredException) when (options.Justification == null)
            {
                output.Write("Policy requires downgrade justification. Enter an authorized reason (blank cancels): ");
                var reason = input.ReadLine();
                if (string.IsNullOrWhiteSpace(reason)) { throw new OperationCanceledException(); }
                options.Justification = reason.Trim();
            }
            catch (AdhocProtectionRequiredException) when (options.AdhocRecipients == null)
            {
                output.Write("Policy requires user-defined permissions. Enter approved recipient emails separated by commas (Viewer rights; blank cancels): ");
                var recipients = input.ReadLine();
                if (string.IsNullOrWhiteSpace(recipients)) { throw new OperationCanceledException(); }
                options.AdhocRecipients = Action.ValidateRecipients(recipients.Split(','));
            }
        }
    }
}
