# Changelog

## 2026-10-27

### Changed

- Target .NET 10 LTS with MIP File SDK 1.18.148, MSAL 4.91.1, and Configuration 10.0.13.
- Retain delegated browser authentication, SDK claims, and explicit interactive justification.
- Prompt for the intended organizational account before engine creation and let SDK callbacks acquire policy/protection tokens directly. Remove the Graph identity bootstrap and its permission requirement; retain account/tenant validation. Live tests now also require `MIPSDK_LIVE_USER`.
- Add explicit cloud selection, tenant/account binding, per-session MSAL cache reuse, and ignored local/environment configuration.
- Replace finalizer-based SDK cleanup with deterministic disposal and reuse one SDK session for write/read-back.
- Stage output without overwrite, verify label and required protection, and report no-op/failure/success accurately.
- Remove fabricated ad-hoc recipients; ask for approved Viewer recipients only when policy requires them.
- Default to Warning-level logs with explicit diagnostic-logging consent; preserve configurable service consent and publishing defaults.

### Added

- Credential-free configuration/authentication/file/CLI/native tests and an 80% managed branch-coverage gate.
- Explicitly gated live tests using generated DOCX files and approved test labels.
- Local Windows/Ubuntu build, advisory, coverage, and publish-smoke instructions; pinned CodeQL scanning and Dependabot without tenant credentials.
- Explicit Ubuntu managed/native asset integration and contract tests for the packages' legacy folder layout.
- Public-sample ignore rules and documentation for configuration, permissions, file guarantees, tests, and troubleshooting.
- Repository Copilot instructions covering local validation, native dependencies, architecture, and public-sample hygiene.

### Fixed

- Remove sample-specific authority/resource allowlists that rejected SDK authority aliases. Use explicit engine cloud selection and MSAL authority validation while retaining HTTPS validation and tenant/account binding.

### Compatibility

- .NET 6 and Ubuntu 18.04/20.04 guidance is replaced by .NET 10 and Windows x64/Ubuntu 22.04 or 24.04 x64.
- Ubuntu builds require explicit selection of the matching native package.
- Existing output files and in-place labeling are rejected. A genuine SDK no-op creates no output.
- Public deployment identifiers are blank. Published artifacts do not include ignored local configuration.
