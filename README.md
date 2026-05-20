# SPNet YAML-first SharePoint workflow authoring

SPNet authors SharePoint 2013 / Workflow Manager workflows as controlled YAML. The YAML model is converted into real Windows Workflow Foundation activity objects, serialized through a SharePoint Designer-compatible serializer path, and published through a separate CSOM publisher.

SPNet is intentionally not a broad YAML-to-XAML converter. The supported `spnet.workflow/v1` schema is small, explicit, and limited to activity shapes that the project can build, validate, and document. Generated SharePoint workflow XAML must not contain raw WF language expression activities such as `VisualBasicValue`, `VisualBasicReference`, `CSharpValue`, or `CSharpReference`; those require VB/C# expression compilation and are rejected by SharePoint Workflow Manager publishing validation. SPNet emits structured SharePoint/Workflow Manager-safe activity nodes instead.

## Documentation map

Start here, then follow the deeper reference that matches the task you are doing.

| Task | Read |
| --- | --- |
| Find the right document quickly | [Documentation index](docs/index.md) |
| Configure, build, inspect, export, publish, download, list, clean up, package, or validate workflows | [Publishing and operations guide](docs/publishing-and-operations.md) |
| Author workflow YAML, metadata, variables, parameters, actions, expressions, and conditions | [Workflow authoring guide](docs/workflow-authoring.md) |
| Check whether an action is stable, preview, experimental, dev-only, or unsupported | [Workflow action support matrix](docs/action-support-matrix.md) |
| Author non-linear stage flow and stage-to-stage branches | [Stage transitions and round-trip guidance](docs/stage-transitions.md) |
| Work with nested `DynamicValue` dictionaries, slash paths, and function-like stage calls | [Deep DynamicValue build/write/read validation](docs/deep-dynamic-values.md) |
| Review the stabilisation and future authoring-power plan | [Feedback implementation plan](docs/FeedbackImplementationPlan20260513.md) |

## Quickstart

### 1. Configure the SharePoint Designer WebsiteCache path

Local build, cache-enabled export, and XAML inspection require SharePoint Designer WebsiteCache proxy assemblies, including `Microsoft.SharePoint.WorkflowServices.Activities.Proxy.dll` and `Microsoft.Activities.Proxy.dll`.

Copy the example local config, then edit the cache path for your workstation:

```powershell
copy config\spnet.local.example.yml config\spnet.local.yml
notepad config\spnet.local.yml
```

You can also set `SPNET_SPD_CACHE` or pass `--cache-folder` directly. Local config and generated artifacts are intentionally ignored by Git.

### 2. Check local tool/package readiness

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 help
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 doctor
```

`doctor --json` emits machine-readable local readiness output. The check is offline; it does not connect to SharePoint.

### 3. Build the sample workflow

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 build --workflow samples\workflow.example.yml --out artifacts\YamlFirstSmoke.xaml --config config\spnet.local.yml
```

Build output includes the generated workflow XAML and a deterministic `*.xaml.metadata.json` sidecar. The metadata JSON is the normal publish contract for display name, technical name, description, target, start options, initiation settings, and form fields.

### 4. Inspect or export generated XAML

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 inspect --xaml artifacts\YamlFirstSmoke.xaml --config config\spnet.local.yml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 export --xaml artifacts\YamlFirstSmoke.xaml --out artifacts\YamlFirstSmoke.exported.yml --config config\spnet.local.yml
```

Cache-enabled export is preferred for workflows with explicit stage transitions because it can deserialize `Flowchart`, `FlowStep`, and `FlowDecision` object references.

### 5. Create or preflight a workflow

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 auth-test --site-url 'https://tenant.sharepoint.com/sites/site' --auth-mode WebLogin
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 create samples\workflow.example.yml config\spnet.local.yml --site-url 'https://tenant.sharepoint.com/sites/site' --dry-run
```

`auth-test` is local and non-mutating. The `create` command builds from YAML, infers normal publish inputs, emits an `SPNET_PLAN` JSON line, and delegates to the existing YAML publish path with `--if-exists Fail`. Remove `--dry-run` only after testing in a non-production SharePoint site.

`create <workflow.yml> <config.yml>` defaults the XAML path to `artifacts/<workflow-file-stem>.xaml`. The workflow name defaults to YAML `metadata.displayName`, then YAML `name`, then the workflow filename stem; override it with `--workflow-name` or `--name`. The target type defaults to YAML `metadata.target.type`, then YAML `target.type`, then `Site`; override it with `--target-type Site|List`. List workflows require `--target-list-title` or YAML `metadata.target.listTitle` / `target.listTitle`.

`--preflight` is an alias for script-layer dry-run behavior. In this first command-ergonomics slice, dry-run/preflight reports `LiveDiagnosticsImplemented=false`; advanced live SharePoint validation and YAML-level Workflow Manager type diagnostics are planned but not fully implemented yet.

## Common command paths

Use `scripts\spnet-workflow.ps1` as the primary packaged command for normal source-tree or packaged usage:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 help
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 help create
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 help build
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 create samples\workflow.example.yml config\spnet.local.yml --site-url 'https://tenant.sharepoint.com/sites/site' --dry-run
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 build --workflow samples\workflow.example.yml --out artifacts\YamlFirstSmoke.xaml --config config\spnet.local.yml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 inspect --xaml artifacts\YamlFirstSmoke.xaml --config config\spnet.local.yml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 export --xaml artifacts\YamlFirstSmoke.xaml --out artifacts\YamlFirstSmoke.exported.yml --config config\spnet.local.yml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 publish --workflow samples\workflow.example.yml --xaml artifacts\YamlFirstSmoke.xaml --site-url 'https://tenant.sharepoint.com/sites/site' --workflow-name YamlFirstSmoke --target-type Site --dry-run
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 update --workflow samples\workflow.example.yml --xaml artifacts\YamlFirstSmoke.xaml --site-url 'https://tenant.sharepoint.com/sites/site' --workflow-name YamlFirstSmoke --target-type Site --dry-run
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 auth-test --site-url 'https://tenant.sharepoint.com/sites/site' --auth-mode WebLogin
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 doctor --json
```

Retained wrappers remain available for compatibility and for operations not exposed by the primary command, such as download, list, cleanup, and config validation:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action ValidateConfig -Config config\spnet.local.yml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action Download -SiteUrl 'https://tenant.sharepoint.com/sites/site' -WorkflowName YamlFirstSmoke -Out artifacts\YamlFirstSmoke.downloaded.xaml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action List -SiteUrl 'https://tenant.sharepoint.com/sites/site' -WorkflowNamePrefix YamlFirstSmoke -IncludeSubscriptions
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action Cleanup -SiteUrl 'https://tenant.sharepoint.com/sites/site' -WorkflowNamePrefix YamlFirstSmoke -Force
```

For command details, authentication modes, conflict policies, metadata sidecars, packaging, release flow, and validation practices, see the [Publishing and operations guide](docs/publishing-and-operations.md).

## Authoring workflow YAML

YAML is the authoring source of truth. A minimal workflow looks like this:

```yaml
schemaVersion: spnet.workflow/v1
name: YamlFirstSmoke
technicalName: YamlFirstSmoke.MTW
start:
  manual: true
target:
  type: Site
variables:
  - name: calc
    type: Double
stages:
  - name: Stage 1
    actions:
      - type: calc
        lValue:
          literal: 1
        rValue:
          literal: 1
        operator: Add
        to: calc
      - type: writeHistory
        message:
          toString:
            variable: calc
```

Authoring guidance is split by depth:

- The [Workflow authoring guide](docs/workflow-authoring.md) explains the top-level schema, canonical metadata block, legacy aliases, initiation form fields, variables, stages, actions, expressions, control-flow conditions, list actions, HTTP actions, email/task actions, and known Designer caveats.
- The [Workflow action support matrix](docs/action-support-matrix.md) is the source of truth for each action's support classification, aliases, builder/export status, sample coverage, validation status, risk, and caveats.
- The [Stage transitions guide](docs/stage-transitions.md) covers explicit stage IDs, conditional branches, defaults, loops, `goto: end`, cache-enabled export, and function-like stage calls.
- The [Deep DynamicValue guide](docs/deep-dynamic-values.md) covers nested dictionaries, `valueType: DynamicValue`, slash-path reads/writes, array-like paths, and function parameter/return dictionaries.

Reference samples live under `samples/`. Start with `samples/workflow.example.yml`, then choose focused samples such as `samples/workflow.parameters.yml`, `samples/workflow.control-flow.yml`, `samples/workflow.http.yml`, `samples/workflow.list-lifecycle.yml`, `samples/workflow.stage-transitions.yml`, and `samples/workflow.deepworkflowexample-dynamic-values.yml`.

## Production guidance

This release is a stabilisation release for YAML-first workflow authoring. It is appropriate for controlled production use only when a workflow is built from documented stable actions, reviewed against the target SharePoint site/list, and validated through a test publish/download/runtime cycle before business use.

Support classification summary:

- **Stable** actions use visible or well-understood Workflow Manager-safe activity shapes and are the default production baseline.
- **Preview** actions build and have targeted validation, but may involve timers, external HTTP services, email/task side effects, list mutations, exact SharePoint Designer metadata, or partial export support.
- **Experimental** actions are for controlled trials, commonly involving `DynamicValue` or hidden Microsoft activity shapes.
- **Dev-only** actions are for local diagnostics, sample builds, and developer experiments.
- **Unsupported** shapes are documented limitations or rejected YAML surfaces and should not be published.

Keep production workflows small, observable, and orchestration-focused. Avoid heavy in-workflow computation, broad data shaping, large loops, large `DynamicValue` payloads, repeated large string operations, and hidden/experimental activities unless the owning team explicitly accepts the risk after target-environment validation. Local build success is necessary but not publish/runtime proof because local WebsiteCache proxy assemblies can differ from the target Workflow Manager environment.

## Repository layout

| Path | Purpose |
| --- | --- |
| `src/SPNet.Workflow.WfSerializer/` | .NET Framework 4.8 serializer/converter. It owns YAML parsing, WF activity construction, XAML export/inspection, SharePoint Designer metadata normalization, and WebsiteCache proxy assembly loading. |
| `src/SPNet.Workflow.Publisher.Csom/` | Standalone .NET Framework 4.8 WorkflowServices CSOM publisher. It treats generated XAML as opaque text and does not reference WF, SharePoint Designer, or serializer assemblies. |
| `tests/SPNet.Workflow.WfSerializer.Tests/` | Automated tests for YAML deserialization, aliases, validation, export recognition, metadata, and focused regression coverage that does not require SharePoint. |
| `scripts/spnet-workflow.ps1` | Primary packaged CLI entry point for help, build, inspect, export, publish, update, auth-test, and doctor. |
| `scripts/Invoke-SPNetYamlWorkflow.ps1` | Compatibility orchestration wrapper for build/export/inspect/publish/download/list/cleanup/ValidateConfig flows. |
| `scripts/Invoke-SPNetWorkflow.ps1` | Live SharePoint publishing/download/list/cleanup boundary. |
| `samples/` | Focused YAML examples for supported, preview, experimental, and dev-only authoring patterns. |
| `docs/` | Task-oriented documentation and reference material. |
| `artifacts/` | Ignored generated output and diagnostics workspace. Only `artifacts/.gitkeep` is committed. |

If editor state shows `src/SPNet.Workflow.Core/`, `src/SPNet.Workflow.Cli/`, or `src/SPNet.Workflow.SharePoint/`, those projects are not present in this repository snapshot and are not included in `SPNet.slnx`.

## Local validation

```powershell
dotnet build .\SPNet.slnx
dotnet test .\tests\SPNet.Workflow.WfSerializer.Tests\SPNet.Workflow.WfSerializer.Tests.csproj
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action ValidateConfig -Config config\spnet.local.yml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 build --workflow samples\workflow.example.yml --out artifacts\YamlFirstSmoke.xaml --config config\spnet.local.yml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 inspect --xaml artifacts\YamlFirstSmoke.xaml --config config\spnet.local.yml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 export --xaml artifacts\YamlFirstSmoke.xaml --out artifacts\YamlFirstSmoke.exported.yml --config config\spnet.local.yml
```

Do not publish or clean workflows as part of routine local validation unless intentionally performing a live SharePoint smoke test against a non-production site with unique workflow names. Generated XAML, diagnostics, tenant URLs, cookies, passwords, and local config belong in ignored local files or `artifacts/`, not source control.
