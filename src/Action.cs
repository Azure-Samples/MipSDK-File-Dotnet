/*
*
* Copyright (c) Microsoft Corporation.
* All rights reserved.
*
* This code is licensed under the MIT License.
*
* Permission is hereby granted, free of charge, to any person obtaining a copy
* of this software and associated documentation files(the "Software"), to deal
* in the Software without restriction, including without limitation the rights
* to use, copy, modify, merge, publish, distribute, sublicense, and / or sell
* copies of the Software, and to permit persons to whom the Software is
* furnished to do so, subject to the following conditions :
*
* The above copyright notice and this permission notice shall be included in
* all copies or substantial portions of the Software.
*
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
* IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
* FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
* AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
* LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
* OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
* THE SOFTWARE.
*
*/

using Microsoft.InformationProtection;
using Microsoft.InformationProtection.File;
using Microsoft.InformationProtection.Protection;

namespace mipsdk
{
    public sealed class Action : IDisposable
    {
        private readonly IFileEngine engine;
        private readonly IFileProfile? profile;
        private readonly IDisposable? mipContext;
        private readonly IDisposable? authentication;
        private bool disposed;

        public struct FileOptions
        {
            public string FileName;
            public string OutputName;
            public string LabelId;
            public DataState DataState;
            public AssignmentMethod AssignmentMethod;
            public bool IsAuditDiscoveryEnabled;
            public bool GenerateChangeAuditEvent;
            public string? Justification;
            public IReadOnlyCollection<string>? AdhocRecipients;
        }

        public struct ProtectionDetails
        {
            public List<UserRoles>? UserRoles;
            public List<UserRights>? UserRights;
            public string? TemplateId;
            public bool IsProtected;
        }

        public sealed record LabelingResult(bool Changed, string? OutputPath, string LabelId, bool IsProtected);

        internal Action(IFileEngine engine, IFileProfile? profile = null,
            IDisposable? mipContext = null, IDisposable? authentication = null)
        {
            this.engine = engine;
            this.profile = profile;
            this.mipContext = mipContext;
            this.authentication = authentication;
        }

        public static async Task<Action> CreateAsync(AppConfig config)
        {
            config.Validate();
            InitializeNative();
            var auth = new AuthDelegateImpl(config);
            MipContext? context = null;
            IFileProfile? profile = null;
            try
            {
                var identity = await auth.GetUserIdentityAsync().ConfigureAwait(false);
                context = MIP.CreateMipContext(CreateMipConfiguration(config));
                profile = await MIP.LoadFileProfileAsync(new FileProfileSettings(
                    context, CacheStorageType.OnDiskEncrypted, new ConsentDelegateImpl(config.ServiceConsent)))
                    .ConfigureAwait(false);
                var engine = await profile.AddEngineAsync(CreateEngineSettings(config, identity, auth))
                    .ConfigureAwait(false);
                return new Action(engine, profile, context, auth);
            }
            catch
            {
                CleanupFailedInitialization(profile, context, auth);
                throw;
            }
        }

        public static void InitializeNative() => MIP.Initialize(MipComponent.File);

        internal static MipConfiguration CreateMipConfiguration(AppConfig config) =>
            new(config.CreateApplicationInfo(), config.CachePath, config.LogLevel, false, CacheStorageType.OnDiskEncrypted)
            {
                DiagnosticOverride = new DiagnosticConfiguration
                {
                    Cloud = config.Cloud,
                    IsMinimalTelemetryEnabled = true,
                    IsLocalCachingEnabled = config.EnableDiagnosticLogging,
                    IsTraceLoggingEnabled = config.EnableDiagnosticLogging
                }
            };

        internal static FileEngineSettings CreateEngineSettings(AppConfig config, Identity identity, IAuthDelegate auth) =>
            new(identity.Email, auth, "", "en-US")
            {
                Identity = identity,
                Cloud = config.Cloud,
                ConfiguredFunctionality = new Dictionary<FunctionalityFilterType, bool>
                {
                    [FunctionalityFilterType.DoubleKeyProtection] = true
                }
            };

        public IEnumerable<Label> ListLabels()
        {
            ThrowIfDisposed();
            return Flatten(engine.SensitivityLabels).Where(IsApplicable).ToArray();
        }

        private static IEnumerable<Label> Flatten(IEnumerable<Label> labels)
        {
            foreach (var label in labels)
            {
                yield return label;
                foreach (var child in Flatten(label.Children ?? []))
                {
                    yield return child;
                }
            }
        }

        internal static bool IsApplicable(Label label) =>
            label.IsActive && (label.Children == null || label.Children.Count == 0)
            && (label.ContentFormats == null || label.ContentFormats.Length == 0
                || label.ContentFormats.Contains("file", StringComparer.OrdinalIgnoreCase));

        public bool SetLabel(FileOptions options) => SetLabelAsync(options).GetAwaiter().GetResult().Changed;

        public async Task<LabelingResult> SetLabelAsync(FileOptions options)
        {
            ThrowIfDisposed();
            var (input, output) = SafeOutput.ValidatePaths(options.FileName, options.OutputName);
            if (!Guid.TryParse(options.LabelId, out var id) || id == Guid.Empty)
            {
                throw new InputValidationException("Enter a valid label GUID.");
            }
            var label = engine.GetLabelById(options.LabelId);
            if (label == null || !IsApplicable(label))
            {
                throw new InputValidationException("Select an active leaf label applicable to files.");
            }
            if (options.Justification != null && string.IsNullOrWhiteSpace(options.Justification))
            {
                throw new InputValidationException("Downgrade justification cannot be blank.");
            }

            using var source = OpenRead(input);
            using var handler = await CreateHandlerAsync(source, input, options.IsAuditDiscoveryEnabled).ConfigureAwait(false);
            if (options.AdhocRecipients != null)
            {
                if (!label.HasAdhocProtection)
                {
                    throw new InputValidationException("Recipients can only be supplied for a user-defined-permissions label.");
                }
                var recipients = ValidateRecipients(options.AdhocRecipients);
                handler.SetProtection(new ProtectionDescriptor(
                    [new UserRoles(recipients, [Roles.Viewer])]), new ProtectionSettings());
            }

            handler.SetLabel(label, new LabelingOptions
            {
                AssignmentMethod = options.AssignmentMethod,
                IsDowngradeJustified = options.Justification != null,
                JustificationMessage = options.Justification
            }, new ProtectionSettings());

            if (!handler.IsModified())
            {
                return new LabelingResult(false, null, label.Id, VerifyHandler(handler, label));
            }

            var extension = Path.GetExtension(handler.OutputFileName);
            if (string.IsNullOrEmpty(extension))
            {
                extension = Path.GetExtension(input);
            }
            if (!Path.GetExtension(output).Equals(extension, StringComparison.OrdinalIgnoreCase))
            {
                throw new InputValidationException($"The SDK requires the output extension '{extension}'. Choose a new destination with that extension.");
            }

            using var staged = new SafeOutput(output, extension);
            if (!await handler.CommitAsync(staged.Stream).ConfigureAwait(false))
            {
                throw new IOException("The SDK did not commit the requested change.");
            }
            await staged.FinishWritingAsync().ConfigureAwait(false);
            await VerifyFileAsync(staged.StagePath, label).ConfigureAwait(false);
            staged.Publish();
            var isProtected = await VerifyFileAsync(output, label).ConfigureAwait(false);
            if (options.GenerateChangeAuditEvent)
            {
                handler.NotifyCommitSuccessful(output);
            }
            return new LabelingResult(true, output, label.Id, isProtected);
        }

        internal static List<string> ValidateRecipients(IEnumerable<string> recipients)
        {
            var result = recipients.Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (result.Count == 0 || result.Any(value =>
                !System.Net.Mail.MailAddress.TryCreate(value, out var address) || address.Address != value))
            {
                throw new InputValidationException("Supply at least one valid recipient email address, without display names.");
            }
            return result;
        }

        private Task<IFileHandler> CreateHandlerAsync(Stream stream, string path, bool audit = false) =>
            engine.CreateFileHandlerAsync(stream, path, audit);

        private static FileStream OpenRead(string path) =>
            new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        private async Task<bool> VerifyFileAsync(string path, Label expected)
        {
            using var stream = OpenRead(path);
            using var handler = await CreateHandlerAsync(stream, path).ConfigureAwait(false);
            return VerifyHandler(handler, expected);
        }

        internal static bool VerifyHandler(IFileHandler handler, Label expected)
        {
            var content = handler.Label;
            using var protection = handler.Protection;
            var requiresProtection = expected.HasRightsManagementPolicy || expected.HasAdhocProtection
                || expected.HasDoNotForwardProtection || expected.HasEncryptOnlyProtection
                || !string.IsNullOrEmpty(expected.DoubleKeyUrl);
            if (content?.Label == null
                || !expected.Id.Equals(content.Label.Id, StringComparison.OrdinalIgnoreCase)
                || (requiresProtection && (!content.IsProtectionAppliedFromLabel || protection == null)))
            {
                throw new InvalidOperationException("Read-back verification failed: the requested label or required protection is missing. No success audit was sent.");
            }
            return protection != null;
        }

        public ContentLabel GetLabel(FileOptions options)
        {
            ThrowIfDisposed();
            using var stream = OpenRead(options.FileName);
            using var handler = CreateHandlerAsync(stream, options.FileName, options.IsAuditDiscoveryEnabled)
                .GetAwaiter().GetResult();
            return handler.Label;
        }

        public ProtectionDetails GetProtectionDetails(FileOptions options)
        {
            ThrowIfDisposed();
            using var stream = OpenRead(options.FileName);
            using var handler = CreateHandlerAsync(stream, options.FileName).GetAwaiter().GetResult();
            using var protection = handler.Protection;
            var descriptor = protection?.ProtectionDescriptor;
            return new ProtectionDetails
            {
                UserRights = descriptor?.UserRights,
                UserRoles = descriptor?.UserRoles,
                IsProtected = protection != null,
                TemplateId = descriptor?.TemplateId ?? ""
            };
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

        internal static void CleanupFailedInitialization(params IDisposable?[] resources)
        {
            foreach (var resource in resources)
            {
                try { DisposeContext(resource); }
                // Cleanup must not replace the exception that caused initialization to fail.
                catch (Exception) { }
            }
        }

        private static void DisposeContext(IDisposable? context)
        {
            try { (context as MipContext)?.ShutDown(); }
            finally { context?.Dispose(); }
        }

        public void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            try { engine.Dispose(); }
            finally
            {
                try { profile?.Dispose(); }
                finally
                {
                    try { DisposeContext(mipContext); }
                    finally { authentication?.Dispose(); }
                }
            }
        }
    }
}