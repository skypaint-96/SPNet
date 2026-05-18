# Publishing and operations guide

Related docs: [documentation index](index.md), [workflow authoring guide](workflow-authoring.md), [action support matrix](action-support-matrix.md), [stage transitions](stage-transitions.md), and [deep DynamicValue validation](deep-dynamic-values.md).

This guide covers local configuration, command usage, publish safety, download/list/cleanup operations, packaging, releases, and validation for SPNet YAML-authored SharePoint workflows.

## Command model

Prefer the primary packaged command for normal usage:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 help
```

Primary command subcommands are `help`, `build`, `inspect`, `export`, `publish`, `update`, `auth-test`, and `doctor`.

Retained wrappers remain available for compatibility and for operations not currently exposed by the primary command:

- `scripts/Invoke-SPNetYamlWorkflow.ps1` handles `Build`, `Export`, `Inspect`, `Publish`, `Download`, `List`, `Cleanup`, and `ValidateConfig`.
- `scripts/Invoke-SPNetWorkflow.ps1` is the live SharePoint publishing/download/list/cleanup boundary.
- Direct serializer and publisher executable calls are advanced compatibility paths. Packaged usage should prefer `scripts/spnet-workflow.ps1` or the wrappers.

Relative path handling is standardized around caller intent: user-supplied relative paths are resolved from the caller's current directory, while script and packaged tool discovery remains relative to the command/package.

## Configuration and WebsiteCache

Local build, cache-enabled export, and XAML inspection require a SharePoint Designer WebsiteCache folder containing SharePoint and Microsoft Activities proxy assemblies. The important proxy assemblies include `Microsoft.SharePoint.WorkflowServices.Activities.Proxy.dll` and `Microsoft.Activities.Proxy.dll`.

Configuration sources are applied in this order:

1. Committed defaults from `config/spnet.defaults.yml`.
2. `config/spnet.local.yml` when present and `--config` is omitted.
3. An explicit `--config` path when supplied.

Cache lookup order is:

1. Explicit `--cache-folder` / `--cache`.
2. `spdCacheFolder` from merged config.
3. `SPNET_SPD_CACHE`.

Set up a local config file:

```powershell
copy config\spnet.local.example.yml config\spnet.local.yml
notepad config\spnet.local.yml
```

Validate the config through the retained wrapper:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action ValidateConfig -Config config\spnet.local.yml
```

The CSOM publisher does not use WebsiteCache; it only needs built XAML plus publish metadata and SharePoint authentication/bootstrap inputs.

## Offline readiness checks

Use `doctor` before build or publish:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 doctor
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 doctor --json
```

`doctor` performs offline source/package integrity checks: root detection, primary and wrapper scripts, packaged tools or source fallback paths, safe config examples, artifacts writeability, package manifest readability in package mode, docs/samples presence, and PowerShell runtime basics.

Use `auth-test` before live publish:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 auth-test --site-url 'https://tenant.sharepoint.com/sites/site' --auth-mode WebLogin
```

`auth-test` is local and non-mutating. It validates site URL shape, selected auth mode inputs, wrapper availability, and publisher discovery. It intentionally does not connect to SharePoint, validate credentials, or publish.

## Build, inspect, and export

Build YAML to SharePoint Designer-compatible XAML plus metadata JSON:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 build --workflow samples\workflow.example.yml --out artifacts\YamlFirstSmoke.xaml --config config\spnet.local.yml
```

Equivalent retained wrapper/direct serializer paths remain available:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action Build -Workflow samples\workflow.example.yml -XamlPath artifacts\YamlFirstSmoke.xaml -Config config\spnet.local.yml
.\src\SPNet.Workflow.WfSerializer\bin\Release\net48\SPNet.Workflow.WfSerializer.exe build --workflow samples\workflow.example.yml --out artifacts\YamlFirstSmoke.xaml --config config\spnet.local.yml
```

Inspect or export generated/downloaded XAML:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 inspect --xaml artifacts\YamlFirstSmoke.xaml --config config\spnet.local.yml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 export --xaml artifacts\YamlFirstSmoke.xaml --out artifacts\YamlFirstSmoke.exported.yml --config config\spnet.local.yml
```

Export is diagnostic rather than a guaranteed full round trip. Cache-enabled export is preferred for non-linear stage workflows because it can deserialize the WF object model and reconstruct `Flowchart`, `FlowStep`, and `FlowDecision` references.

## Metadata sidecars

When YAML is built, SPNet emits public WF `InArgument<T>` declarations for effective initiation fields and writes a deterministic `*.xaml.metadata.json` sidecar. That JSON is generated from effective YAML metadata/defaults after applying canonical `metadata` and legacy aliases.

The normal round trip is:

```text
YAML -> XAML + metadata JSON -> SharePoint publish with metadata JSON -> download XAML + metadata JSON
```

Legacy `*.xaml.formfield.xml` sidecars may still be generated or downloaded for compatibility/inspection when form fields are present, but FormField XML is no longer the normal YAML publish input. Use explicit FormField XML only as a deprecated fallback when metadata JSON is unavailable.

## Publishing

Packaged publishing should use the primary command or retained wrappers rather than direct executable calls. The wrapper is the authentication/bootstrap boundary.

Dry-run publish first:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 publish --workflow samples\workflow.example.yml --xaml artifacts\YamlFirstSmoke.xaml --site-url 'https://tenant.sharepoint.com/sites/site' --workflow-name YamlFirstSmoke --target-type Site --dry-run
```

Publish an existing XAML file only when a matching metadata JSON sidecar already exists or when `--metadata-json` points to the sidecar explicitly:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 publish --no-build --xaml artifacts\YamlFirstSmoke.xaml --site-url 'https://tenant.sharepoint.com/sites/site' --workflow-name YamlFirstSmoke --target-type Site --dry-run
```

Remove `--dry-run` only when SharePoint authentication/session state, target site/list validation, and WorkflowServices CSOM dependencies are ready.

### Authentication modes

Publish defaults to `--auth-mode WebLogin`. Supported auth modes are:

| Mode | Behavior |
| --- | --- |
| `WebLogin` | Default. The wrapper attempts PnP WebLogin and passes PnP/WinINet cookies to the CSOM publisher. |
| `CookieHeader` | Use with `--publisher-cookie-header` when you already have an explicit SharePoint Cookie header. |
| `WindowsDefault` | Use default Windows credentials in the CSOM publisher. |
| `Credentials` | Pass `--publisher-username`, optional `--publisher-password`, and optional `--publisher-domain` where legacy credentials are accepted. |

Omit `--publisher-exe` for packaged usage. The wrapper resolves `tools\SPNet.Workflow.Publisher.Csom\SPNet.Workflow.Publisher.Csom.exe` before source output/project fallback and reports the selected path in publish diagnostics. Direct `SPNet.Workflow.Publisher.Csom.exe` invocation does not bootstrap WebLogin/WinINet cookies and is advanced/unsupported unless all required cookies or credentials are supplied explicitly.

### Create, update, and conflict behavior

Publishing defaults to create semantics. `scripts/spnet-workflow.ps1 publish` and the wrappers fail before creating anything when a same-name workflow already exists, with an error advising explicit update or delete-then-create.

Use the explicit update command only when replacing an existing same-name workflow is intended:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 update --workflow samples\workflow.example.yml --xaml artifacts\YamlFirstSmoke.xaml --site-url 'https://tenant.sharepoint.com/sites/site' --workflow-name YamlFirstSmoke --target-type Site --dry-run
```

The update path replaces a single existing same-name workflow by deleting its subscriptions/definition before creating the new definition/subscription. It does not migrate running instances. `CreateNew` intentionally publishes alongside an existing workflow by adding a unique suffix when the requested name already exists.

If a newly created definition/subscription fails during later publish or subscription creation, the CSOM publisher and fallback path attempt best-effort rollback cleanup to avoid orphan definitions.

## Download, list, and cleanup

Download published workflows through the retained wrapper. Download writes the XAML and `*.xaml.metadata.json`; it may also preserve `*.xaml.formfield.xml` when SharePoint exposes legacy FormField metadata:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action Download -SiteUrl 'https://tenant.sharepoint.com/sites/site' -WorkflowName YamlFirstSmoke -Out artifacts\YamlFirstSmoke.downloaded.xaml
```

Use read-only listing before cleanup:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action List -SiteUrl 'https://tenant.sharepoint.com/sites/site' -WorkflowNamePrefix YamlFirstSmoke -IncludeSubscriptions
```

Cleanup is guarded and refuses to run unless `-Force` is supplied. Prefix cleanup requires a prefix of at least eight characters; exact-name cleanup can use `-WorkflowName`.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action Cleanup -SiteUrl 'https://tenant.sharepoint.com/sites/site' -WorkflowNamePrefix YamlFirstSmoke -Force
```

Do not use cleanup against production names such as `New Leave Request`. If a publish failure reports rollback warnings or a partial status, list by exact test name or prefix first, then clean only confirmed test artifacts.

## Diagnostics

For read-only SharePoint workflow diagnostics and downloads, use:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Get-SPNetWorkflowDiagnostics.ps1 -SiteUrl 'https://tenant.sharepoint.com/sites/site' -WorkflowName 'YamlFirstSmoke' -OutputDirectory .\artifacts\sharepoint-workflow-diagnostics\YamlFirstSmoke
```

Diagnostics are intended to verify definitions, subscriptions, metadata, and downloaded XAML shape without publishing, deleting, or modifying SharePoint objects.

## Validation practices

Routine local validation:

```powershell
dotnet build .\SPNet.slnx
dotnet test .\tests\SPNet.Workflow.WfSerializer.Tests\SPNet.Workflow.WfSerializer.Tests.csproj
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action ValidateConfig -Config config\spnet.local.yml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 build --workflow samples\workflow.example.yml --out artifacts\YamlFirstSmoke.xaml --config config\spnet.local.yml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 inspect --xaml artifacts\YamlFirstSmoke.xaml --config config\spnet.local.yml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 export --xaml artifacts\YamlFirstSmoke.xaml --out artifacts\YamlFirstSmoke.exported.yml --config config\spnet.local.yml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-SPNetYamlWorkflowGolden.ps1 -Workflow samples\workflow.example.yml -XamlPath artifacts\golden\YamlFirstSmoke.xaml -Config config\spnet.local.yml
```

Live validation should be explicit and non-production-first:

1. Build YAML to XAML plus metadata JSON.
2. Inspect generated XAML and sidecar metadata.
3. Run `auth-test` and publish with `--dry-run`.
4. Publish with a unique test name only after reviewing target site/list and auth settings.
5. Open in SharePoint Designer and run `Check for Errors`.
6. Run realistic start conditions and verify workflow history/side effects.
7. Download the workflow to confirm XAML plus metadata JSON round trip.
8. List and clean only uniquely named test artifacts.

Do not publish or clean workflows as part of local validation unless intentionally performing a live SharePoint smoke test.

## Packaging and releases

GitHub Actions are scoped in `.github/workflows/ci-release.yml` to the repository's protected development/production branch flow. Pull requests into production must come from development. Development runs validation. A production push runs validation, builds a distributable package, uploads Actions artifacts, and creates a GitHub Release.

Repository versioning is controlled by Nerdbank.GitVersioning through `version.json`. Inspect the local derived version with:

```powershell
dotnet tool install --global nbgv
nbgv get-version
```

Create a local package with:

```powershell
$version = (nbgv get-version --format json | ConvertFrom-Json).NuGetPackageVersion
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package-SPNetWorkflow.ps1 -Configuration Release -Version $version -OutputDirectory artifacts\release -IncludeNuGetPackages
```

The package includes built command-line tools, runtime PowerShell scripts, safe config examples, docs, samples, and the root README. It intentionally excludes local config, SharePoint secrets, SharePoint Designer WebsiteCache/proxy assemblies, generated diagnostics, and transient build artifacts.

## Architecture summary

| Component | Responsibility |
| --- | --- |
| `src/SPNet.Workflow.WfSerializer` | .NET Framework 4.8 serializer/converter. Loads WebsiteCache proxy DLLs, builds real WF activity trees, serializes XAML, injects SPD stage metadata, and exports/inspects XAML. |
| `src/SPNet.Workflow.Publisher.Csom` | Standalone .NET Framework 4.8 WorkflowServices CSOM publisher. Treats XAML as opaque text and uses metadata JSON as the publish contract. |
| `tests/SPNet.Workflow.WfSerializer.Tests` | Lightweight tests for YAML deserialization, action aliases, validation, metadata, export recognition, and focused regression coverage. |
| `scripts/spnet-workflow.ps1` | Primary packaged CLI entry point. |
| `scripts/Invoke-SPNetYamlWorkflow.ps1` | YAML build/export/inspect/publish/download/list/cleanup wrapper. |
| `scripts/Invoke-SPNetWorkflow.ps1` | Live SharePoint boundary and authentication/bootstrap wrapper. |

The solution file `SPNet.slnx` includes only the serializer project, CSOM publisher project, and test project present in this repository snapshot. Stale editor references to `src/SPNet.Workflow.Core/`, `src/SPNet.Workflow.Cli/`, or `src/SPNet.Workflow.SharePoint/` are not part of this workspace.

## Operational guardrails

- Keep generated XAML, diagnostics, downloaded artifacts, tenant URLs, cookies, credentials, and local config out of source control.
- Treat local build/export/inspect as necessary but not sufficient for live use.
- Stable actions are the production baseline. Preview actions require target-site validation. Experimental and dev-only actions should not be production defaults.
- Large workflows, deep nesting, many variables/properties, long loops, large HTTP/DynamicValue payloads, and repeated large string operations can publish slowly, fail validation, render poorly in SharePoint Designer, or fail at runtime.
- Use read-only list/diagnostic commands before cleanup and only clean confirmed test artifacts.
