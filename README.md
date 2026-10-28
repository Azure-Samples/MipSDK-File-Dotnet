---
page_type: sample
languages:
- csharp
products:
- m365
- office-365
description: "Use the Microsoft Information Protection File SDK with delegated browser sign-in to label files and verify label and protection."
urlFragment: MipSDK-File-Dotnet
---

# MIP File SDK .NET 10 sample

This interactive console sample lists active file labels, applies a selected label to a new output file, and verifies the label and required protection. It uses delegated Microsoft Entra ID authentication through MSAL and your browser. It does not use client secrets, certificates, managed identity, or service-principal permissions.

## Supported platforms and dependencies

Use a stable .NET 10 SDK and an x64 process. `global.json` selects the latest installed stable .NET 10 feature band, not previews.

| Host | MIP package | Native prerequisites |
| --- | --- | --- |
| Windows x64 | `Microsoft.InformationProtection.File` | Visual C++ Redistributable for Visual Studio 2022, MSVC 14.44 or newer, x64 |
| Ubuntu 22.04 x64 | `Microsoft.InformationProtection.File.Ubuntu2204` | `libgsf-1-114 libgmime-3.0-0 libsecret-1-0 libcurl4 libssl3 libuuid1 xdg-utils` |
| Ubuntu 24.04 x64 | `Microsoft.InformationProtection.File.Ubuntu2404` | `libgsf-1-114 libgmime-3.0-0t64 libsecret-1-0 libcurl4t64 libssl3t64 libuuid1 xdg-utils` |

Install the listed Ubuntu packages with `sudo apt-get update` and `sudo apt-get install`. Interactive authentication also needs a usable desktop browser. A headless CI runner can run the credential-free tests, but not this sample's browser sign-in. On Ubuntu, an encrypted SDK policy cache may also require a working desktop secret service/keyring.

Select the exact Ubuntu package on **every** restore/build/test/publish command:

```bash
dotnet restore tests/mipsdk.Tests/mipsdk.Tests.csproj -p:MipSdkPackage=Microsoft.InformationProtection.File.Ubuntu2204
```

For Ubuntu 24.04, use `Microsoft.InformationProtection.File.Ubuntu2404`. The sample does not infer an Ubuntu version from "not Windows." Windows selects its package automatically. macOS, Windows ARM64/x86, other Linux distributions, cross-OS publishing, single-file publishing, trimming, and Native AOT are not supported by this sample.

For `dotnet list ... package`, which does not accept MSBuild property arguments, supply the same selection through the environment, for example `export MipSdkPackage=Microsoft.InformationProtection.File.Ubuntu2204` in Bash.

The Ubuntu 1.18.148 packages put their .NET 6 managed wrapper in legacy `lib/dotnet` folders, rather than target-framework folders. `Directory.Build.targets` explicitly references that compatible wrapper and copies the matching native libraries into build and publish output. Only these packages' misleading `NU1701` layout warning is suppressed; dependency advisories and other warnings remain enforced. Contract tests verify the wrapper's actual target framework and native assets. The shared MIP version is pinned in `Directory.Build.props`.

Dependency baseline verified on **October 23, 2026**:

| Component | Before this modernization | Selected stable version |
| --- | --- | --- |
| Target framework | .NET 6 | .NET 10 LTS |
| MIP managed/native File SDK | 1.18.124 | 1.18.148 |
| MSAL | 4.86.0 | 4.91.1 |
| Configuration libraries | 6.0.x | 10.0.13 |
| Tests | None | xUnit v3 4.0.2, Coverlet MTP 10.1.0, Moq 4.21.0 |

Versions are pinned in the project files and `Directory.Build.props`. Configuration JSON/environment providers supply the shared Configuration dependencies transitively; unused dependency injection was removed. Test execution uses Microsoft.Testing.Platform, not a VSTest collector.

Sources: [MIP release notes](https://learn.microsoft.com/information-protection/develop/version-release-history), [MIP packages](https://www.nuget.org/packages/Microsoft.InformationProtection.File), [MSAL](https://www.nuget.org/packages/Microsoft.Identity.Client), [Configuration](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.Json), [.NET support policy](https://dotnet.microsoft.com/platform/support/policy/dotnet-core), [xUnit](https://www.nuget.org/packages/xunit.v3), [Coverlet MTP](https://www.nuget.org/packages/coverlet.MTP), and [Moq](https://www.nuget.org/packages/Moq).

## Entra registration and permissions

Register a **public client** application for the intended organizational tenant(s), enable the public-client flow, and register the mobile/desktop redirect URI `http://localhost`. Match `ida:IsMultiTenantApp` to the registration. Single-tenant mode validates the configured tenant GUID, pins shared authority challenges to it, and rejects another tenant.

Use delegated permissions appropriate to the operations:

- Microsoft Graph `User.Read` for the existing Graph-based identity bootstrap.
- Microsoft Information Protection Sync Service `UnifiedPolicy.User.Read` to retrieve the signed-in user's labeling policy.
- Azure Rights Management Services `user_impersonation` for delegated protection operations.

Have your administrator approve any required consent. The user must have the appropriate licensing, published labels, and rights to the input and output. Do not grant application permissions or super-user access to make this delegated sample work. The exact permission names and registration steps are documented in [MIP setup](https://learn.microsoft.com/information-protection/develop/setup-configure-mip).

## Configuration

Keep tracked `src/appsettings.json` free of deployment identifiers. Its client and tenant IDs are deliberately blank. Create ignored `src/appsettings.Local.json`:

```json
{
  "AppSettings": {
    "ida:ClientId": "YOUR-APPLICATION-REGISTRATION-GUID",
    "ida:TenantGuid": "YOUR-TENANT-GUID",
    "ida:IsMultiTenantApp": false
  }
}
```

Replace the placeholders locally. The tenant GUID is required in single-tenant mode. Multi-tenant mode still requires an organizational account.

Precedence, from lowest to highest:

1. `appsettings.json` beside the executable, required.
2. `appsettings.Local.json` beside the executable, optional.
3. Environment variables prefixed `MIPSDK_`, with `__` separating configuration sections.

For example, `MIPSDK_AppSettings__ida__ClientId` overrides `AppSettings:ida:ClientId`. `dotnet run` copies the ignored local file to build output, but **publish never copies it**. Provision the published application's local file privately or use environment variables. Existing files in a reused publish directory are not scrubbed; publish into a new directory when preparing public artifacts.

| Setting | Behavior |
| --- | --- |
| `ida:RedirectUri` | HTTP loopback redirect, default `http://localhost`; must match the public-client registration |
| `ida:AllowedAuthorityHosts` | Retained hostname boundary; HTTPS/URI validation and MSAL authority validation also remain enabled. Empty list retains the commercial-host fallback |
| `ida:AllowedResources` | Retained exact audience boundary, normalized for trailing `/`. Must include the cloud's Graph resource when nonempty. An empty list retains the prior unrestricted-HTTPS-audience behavior and is not recommended |
| `sdk:Cloud` | Explicit engine and diagnostic cloud: `Commercial`, `US_GCC`, `US_GCC_High`, `US_DoD`, or `China_01` |
| `sdk:ServiceConsent` | SDK service access decision: `AcceptAlways` (existing default), `Accept`, or `Reject`; not Entra/admin consent |
| `sdk:LogLevel` | SDK log threshold, default `Warning`, rather than the previous `Trace` |
| `sdk:EnableDiagnosticLogging` | Default `false`; enables diagnostic disk caching/tracing only when explicitly configured and confirmed in the CLI |
| `sdk:GenerateChangeAuditEvent` | Default `false`; successful-change notification only after output verification |
| `sdk:CachePath` | Default: per-user local application data under `MipSdkFileSample/<client>/<cloud>`; optional absolute/local override |

The two allowlists are **whole-list overrides**, unlike ordinary scalar settings. The highest-priority provider that defines a list supplies every entry; shorter local arrays or environment lists never inherit trailing entries from defaults. Omitting a list leaves the lower-priority list unchanged. A local JSON `[]` or `null` explicitly clears it: empty authority hosts retain the commercial-host fallback, while empty resources retain the prior unrestricted-HTTPS-audience behavior. **An empty resource list does not block all resources.**

Use indexed environment variables, such as `MIPSDK_AppSettings__ida__AllowedResources__0` and `MIPSDK_AppSettings__ida__AllowedResources__1`, and supply the complete reviewed list, including the cloud's Graph resource. Even one indexed environment entry replaces the entire JSON list. Nonempty scalar list values, named entries, and nested objects are rejected rather than silently falling back to defaults.

Cloud selection is not an endpoint firewall. Retain narrow, intentional authority/resource lists. For sovereign clouds, change both lists to the reviewed endpoints for your deployment; the sample does not silently extend them. Bootstrap mappings are:

| Cloud | Authority host | Graph resource |
| --- | --- | --- |
| Commercial / US_GCC | `login.microsoftonline.com` | `https://graph.microsoft.com` |
| US_GCC_High | `login.microsoftonline.us` | `https://graph.microsoft.us` |
| US_DoD | `login.microsoftonline.us` | `https://dod-graph.microsoft.us` |
| China_01 | `login.chinacloudapi.cn` | `https://microsoftgraph.chinacloudapi.cn` |

MSAL reuses one public client and its in-memory token cache for the session. Requests retain SDK claims and select the intended account, not the first cached account. Tokens are not persisted by this sample, so a new process may prompt again. The SDK uses an encrypted on-disk policy cache. Caches and logs can still contain identity, policy, and file metadata. Keep them private, outside source control, and delete them according to your organization's retention policy. Minimal SDK telemetry is enabled; this does not mean all SDK telemetry/auditing is disabled. No tokens, raw authentication exceptions, or raw SDK server responses are printed by the CLI.

## Build and run

From the repository root on Windows:

```powershell
dotnet restore .\tests\mipsdk.Tests\mipsdk.Tests.csproj
dotnet build .\tests\mipsdk.Tests\mipsdk.Tests.csproj --configuration Release --no-restore
dotnet run --project .\src\mipsdk.csproj --configuration Release --no-build -- --help
dotnet run --project .\src\mipsdk.csproj --configuration Release --no-build -- --check-config
dotnet run --project .\src\mipsdk.csproj --configuration Release --no-build
```

The interactive application signs in, lists applicable active leaf labels, and prompts for a label GUID, existing input file, and new output path. It preserves the existing explicit downgrade-justification prompt and asks for approved recipient addresses with **Viewer** rights if the SDK requires user-defined permissions. Blank/cancelled input aborts; no recipients or justification are fabricated.

Exit codes: `0` verified change or genuine no-op; `1` authentication/SDK/I/O/verification failure; `2` invalid configuration, arguments, or input; `3` user cancellation. `--help` and `--native-smoke` require no configured identity. `--check-config` validates locally without authentication.

### Output guarantees and SDK lifetime

The sample rejects in-place labeling, pre-existing destinations, and missing destination directories. Source content is opened read-only. A changed file is written to an exclusively created staging file beside the destination, retaining the SDK-required extension. The staged label and protection are verified before a no-overwrite move to the final path, then read back again. A competing destination is never overwritten.

If the SDK requires another extension, such as `.pfile`, select a new destination with that extension and rerun. There is no silent rename. No-op detection uses `IFileHandler.IsModified()` **after** policy evaluation, including same-ID requests. A verified no-op creates no output and sends no success audit.

Commit/staged-verification failures do not publish output; temporary cleanup is best effort if the OS denies deletion. Final verification failure retains the already-published output for private investigation and reports failure without success audit. A successful-change audit uses the actual output path, never the source path. SDK/service auditing may occur independently of this explicit notification.

Handlers, protection handlers, streams, engines, profiles, authentication state, and the MIP context have deterministic ownership, including partial initialization failures. One session is reused for write/read-back. File operations and disposal on a session must not run concurrently.

### Code-only downgrade example

Reusable `SetLabelAsync` never prompts or automatically retries. The CLI asks separately, then retries only with the user's explicit reason:

```csharp
try
{
    await action.SetLabelAsync(options);
}
catch (Microsoft.InformationProtection.Exceptions.JustificationRequiredException)
{
    // Set only after an authorized caller supplies a real reason.
    options.Justification = authorizedReason;
    await action.SetLabelAsync(options);
}
```

The implementation maps that reason to `LabelingOptions.IsDowngradeJustified` and `JustificationMessage`. An unjustified SDK exception propagates without commit, retry, input mutation, or success audit. Not every tenant policy requires justification.

## Credential-free validation

On Windows, run:

```powershell
dotnet restore .\tests\mipsdk.Tests\mipsdk.Tests.csproj -p:NuGetAudit=true -p:NuGetAuditMode=all -warnaserror
dotnet list .\src\mipsdk.csproj package --vulnerable --include-transitive --no-restore
dotnet list .\tests\mipsdk.Tests\mipsdk.Tests.csproj package --vulnerable --include-transitive --no-restore
dotnet build .\tests\mipsdk.Tests\mipsdk.Tests.csproj --configuration Release --no-restore -warnaserror
dotnet test --project .\tests\mipsdk.Tests\mipsdk.Tests.csproj --configuration Release --no-build --coverlet --coverlet-include "[mipsdk]*" --coverlet-output-format cobertura --coverlet-threshold 80 --coverlet-threshold-type branch --results-directory .\artifacts\coverage
dotnet publish .\src\mipsdk.csproj --configuration Release --runtime win-x64 --self-contained false --output .\artifacts\publish
dotnet .\artifacts\publish\mipsdk.dll --help
dotnet .\artifacts\publish\mipsdk.dll --native-smoke
dotnet .\artifacts\publish\mipsdk.dll --check-config
```

The last command is expected to return **2** with the public blank configuration and no private environment overrides. Publish to a fresh directory; local settings must be absent. On Ubuntu, add the matching `MipSdkPackage` to all project commands and publish with `--runtime linux-x64` on that host.

The **80% managed branch-coverage gate** includes the entire `mipsdk` assembly, with no hand-written source exclusions. Tests cover configuration precedence/validation, cloud/consent, actual MSAL flows against an in-memory identity service, claims/cache/account/tenant handling, label selection, no-op behavior, commit failures, output races, independent protection verification, exception propagation, disposal, CLI validation, and native loading. Coverage instrumentation must not run concurrently against the same binaries.

Native loading proves neither policy access nor protected publishing. A clean dependency advisory report is not a source-code security scan. CI runs the credential-free matrix on Windows and Ubuntu 22.04/24.04, coverage, advisory enforcement, and published-app smoke checks. A separate CodeQL workflow scans C# source. CI actions are pinned to verified release commits; Dependabot maintains package/action updates. These workflows require an actual hosted run before their results can be claimed. Only credential-free coverage is uploaded, not SDK caches, logs, or live content.

## Opt-in live validation

Live tests are **skipped by default**. Run them only in an approved test tenant, from an interactive desktop, with reviewed local configuration and approved active leaf labels:

```powershell
$env:MIPSDK_RUN_LIVE = "1"
$env:MIPSDK_LIVE_LABEL_ID = "YOUR-APPROVED-TEST-LABEL-GUID"
$env:MIPSDK_LIVE_PROTECTED_LABEL_ID = "YOUR-APPROVED-PROTECTED-TEST-LABEL-GUID"
try {
    dotnet test --project .\tests\mipsdk.Tests\mipsdk.Tests.csproj --configuration Release
} finally {
    Remove-Item Env:\MIPSDK_RUN_LIVE, Env:\MIPSDK_LIVE_LABEL_ID, Env:\MIPSDK_LIVE_PROTECTED_LABEL_ID
}
```

Each live case also requires its corresponding label variable. Tests generate disposable valid DOCX content, verify unchanged input, read back label/protection, and repeat labeling while accepting either the SDK's changed or unchanged result. Protected testing requires actual protection read-back, not just a matching label ID. No customer files are relabeled and no tenant settings are changed. Live PDF, sovereign-cloud, DKE, user-defined-permissions, and downgrade-policy scenarios require separate approved manual evidence; passing ordinary live labeling does not prove those modes.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Invalid configuration | Required non-placeholder client/tenant GUIDs, named enums, configuration precedence, selected cloud and allowlists |
| Sign-in failure | Public-client registration, loopback redirect, intended user/tenant, delegated permissions, consent, desktop browser |
| Native load failure | x64 process, matching OS package, copied native assets, VC++ Redistributable or Ubuntu libraries |
| No policy/applicable labels | User licensing, published policy, consent, active leaf labels with file scope |
| Output failure | New destination, existing writable parent directory, SDK-required extension, competing creation, read-back verification |
| `TemplateNotFoundException` | Preserve private diagnostics; investigate permissions, cloud, policy/template availability, cache, and publishing mode rather than assuming one cause |

Offline publishing remains at the target SDK's default. No workaround or privilege elevation is enabled. In MIP 1.18, the version-specific online-publishing alternative is `MipConfiguration.FeatureSettingsOverride = new() { [FlightingFeature.FileSDKOfflinePublishing] = false }`, set before context creation. This is **not enabled or live-tested here**, requires a separate approved change, and is not equivalent to `MipConfiguration.IsOfflineOnly`, which controls network availability.
