# MIP File SDK .NET sample

## Build and validation

Run commands from the repository root. This is a .NET 10 x64 console application with native MIP dependencies. `global.json` selects a stable .NET 10 SDK and Microsoft.Testing.Platform (MTP); the tests use xUnit v3, Moq, and Coverlet MTP.

Windows PowerShell:

```powershell
dotnet restore .\tests\mipsdk.Tests\mipsdk.Tests.csproj -p:NuGetAudit=true -p:NuGetAuditMode=all -warnaserror
dotnet build .\tests\mipsdk.Tests\mipsdk.Tests.csproj --configuration Release --no-restore -warnaserror

# Full credential-free suite and managed branch-coverage gate
$env:MIPSDK_RUN_LIVE = "0"
dotnet test --project .\tests\mipsdk.Tests\mipsdk.Tests.csproj --configuration Release --no-build --coverlet --coverlet-include "[mipsdk]*" --coverlet-output-format cobertura --coverlet-threshold 80 --coverlet-threshold-type branch --results-directory .\artifacts\coverage

# One test method, or one test class
dotnet test --project .\tests\mipsdk.Tests\mipsdk.Tests.csproj --configuration Release --no-build --filter-method mipsdk.Tests.AuthenticationTests.RealMsalReusesItsClientAndCacheAndForwardsClaimsWithoutNetwork
dotnet test --project .\tests\mipsdk.Tests\mipsdk.Tests.csproj --configuration Release --no-build --filter-class mipsdk.Tests.ConfigurationTests

# Advisory reports for both dependency graphs
dotnet list .\src\mipsdk.csproj package --vulnerable --include-transitive --no-restore
dotnet list .\tests\mipsdk.Tests\mipsdk.Tests.csproj package --vulnerable --include-transitive --no-restore
```

Use MTP's `--project` and xUnit's `--filter-method`/`--filter-class`, not VSTest collector/filter options. Do not mix simple filters with query or VSTest filters. Keep the 80% branch gate over the entire `mipsdk` assembly, without source exclusions. Do not run coverage instrumentation concurrently against shared binaries. Tests serialize execution because some change process environment or console state.

Run and publish:

```powershell
dotnet run --project .\src\mipsdk.csproj --configuration Release --no-build -- --help
dotnet run --project .\src\mipsdk.csproj --configuration Release --no-build -- --check-config
dotnet run --project .\src\mipsdk.csproj --configuration Release --no-build -- --native-smoke
dotnet run --project .\src\mipsdk.csproj --configuration Release --no-build
dotnet publish .\src\mipsdk.csproj --configuration Release --runtime win-x64 --self-contained false --output .\artifacts\publish
dotnet .\artifacts\publish\mipsdk.dll --native-smoke
dotnet .\artifacts\publish\mipsdk.dll --check-config
```

Use a fresh publish directory. Published output must not contain `appsettings.Local.json`; `--check-config` should exit 2 with blank public settings and no private environment overrides. Help/native smoke need no identity. Use `dotnet run` instead of guessing build-output paths; Windows app builds use an x64 platform directory.

Record elapsed validation times. Report managed tests, native loading, live policy access, and protected publishing as distinct evidence.

### Native platform integration

Windows requires the x64 Visual C++ Redistributable documented in README.md. Ubuntu 22.04/24.04 requires the matching native prerequisites and package. Add `-p:MipSdkPackage=Microsoft.InformationProtection.File.Ubuntu2204` or `.Ubuntu2404` to every restore/build/test/publish command; use `linux-x64` for publishing on Linux. For `dotnet list ... package`, supply `MipSdkPackage` through the environment instead of `-p:`.

`Directory.Build.props` pins the shared MIP version. Ubuntu packages have legacy `lib/dotnet` and native asset layouts, integrated explicitly in `Directory.Build.targets`; preserve managed/native version alignment and SDK contract tests. Only the known Ubuntu layout warning `NU1701` is suppressed. Cross-OS publishing, trimming, single-file publishing, Native AOT, and non-x64 targets are unsupported.

## Architecture and invariants

- `Program` owns the interactive CLI and maps outcomes to exit codes: 0 success/no-op, 1 operation failure, 2 invalid configuration/input, 3 cancellation. Reusable file operations do not prompt; only the CLI collects explicit downgrade justification or approved ad-hoc Viewer recipients.
- `AppConfig` loads executable-directory defaults, optional ignored local JSON, then `MIPSDK_` environment overrides. `MIPSDK_AppSettings__ida__ClientId` maps to `AppSettings:ida:ClientId`. Validate before starting authentication or SDK operations.
- `AuthDelegateImpl` owns one MSAL public client and an in-memory token cache per session, bound to the explicitly supplied organizational account UPN. SDK callbacks supply authority, audience, and claims and trigger policy/protection token acquisition directly. Use the intended account as the login hint and validate returned accounts even if a callback omits its identity. Preserve serialized acquisitions, SDK claims, tenant pinning, and selected-account binding.
- `Action.CreateAsync(config, userPrincipalName)` validates the intended identity before native initialization, then creates the context, profile, and file engine. Only `Program` prompts for the UPN. Entered identity is not authentication evidence; cached policy may avoid a new sign-in. Dispose partial initialization on failure. A session owns its engine/profile/context/authentication and reuses them for write/read-back; file operations and disposal must not overlap.
- `Action.SetLabelAsync` validates an active applicable leaf label, evaluates SDK policy, then checks `IsModified()`. Same-label requests must still evaluate policy. A genuine no-op verifies label/protection, creates no output, and sends no success notification.
- `SafeOutput` exclusively creates a sibling staging file with the SDK-required extension. Open source content read-only, refuse overwrite/in-place writes, verify staged label and required protection, publish without overwrite, and verify final output before explicit success audit. Final-verification failure retains published output for private investigation and reports failure.

## Authentication, cloud, and live testing

This is delegated system-browser authentication, not service-principal authentication. Use a public-client mobile/desktop registration with `http://localhost`, not HTTPS or a custom-scheme redirect. Broker/WAM adoption is a separate authentication change.

Set `FileEngineSettings.Cloud` explicitly from `sdk:Cloud`, whose public default is `Commercial`. The SDK discovers cloud services. Do not reintroduce sample authority/resource allowlists: they previously rejected the SDK's `login.windows.net` authority alias. Preserve HTTPS/URI checks, MSAL authority validation, and tenant/account binding. Token requests use the configured cloud's MSAL client authority with the validated SDK tenant and audience.

For live testing, first run configuration and native checks, then use an interactive terminal/browser for sign-in and label listing. The CLI rejects redirected input. If browser sign-in succeeds but engine creation raises `NoAuthTokenException`, inspect sanitized SDK error markers before repeating sign-in or proposing permission changes. Do not print raw tokens, claims, callback URLs, or server responses.

Live tests require explicit `MIPSDK_RUN_LIVE=1`, the intended account in `MIPSDK_LIVE_USER`, plus `MIPSDK_LIVE_LABEL_ID` or `MIPSDK_LIVE_PROTECTED_LABEL_ID`. Use approved active labels and generated throwaway content, never customer files. Ordinary labeling does not prove protected publishing, sovereign-cloud, DKE, ad-hoc permissions, or downgrade-policy behavior. Keep the SDK publishing-mode default and service-consent behavior unless a separate change is approved; SDK service consent is not Entra/admin permission consent.

## Public-sample and repository boundaries

Keep tracked client/tenant IDs blank and test identities synthetic. Store real settings only in ignored local configuration or process environment. Local JSON is copied into build/test output but excluded from publish; reused output directories can still retain old copies.

Before publication, inspect staged files and outgoing commits for real deployment identifiers, local configuration copies, logs/caches, private keys, test content, and generated binaries. Clean up the specific test application's cache/log directory and generated settings copies when requested, without clearing unrelated applications' caches.

README.md and CONTRIBUTING.md define local validation. Keep tests and coverage local; the repository retains CodeQL and Dependabot, not build/test/publish or E2E workflows. Retained automation requires no tenant credentials. Follow `.github/PULL_REQUEST_TEMPLATE.md` when updating a PR, with exact validation commands/results and explicit untested modes.
