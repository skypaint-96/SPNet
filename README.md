# SPNet YAML-first SharePoint workflow authoring

SPNet now focuses on editing SharePoint 2013 / Workflow Manager workflows as controlled YAML, mapping that YAML to real Windows Workflow Foundation `ActivityBuilder` objects, and serializing through the proven SharePoint Designer-compatible WF serializer path.

This is intentionally not the old broad YAML conversion pipeline. The YAML schema is small, explicit, and only emits supported WF activity objects before the existing serializer normalizes SharePoint Designer metadata.

## Primary workflow

1. Copy `config/spnet.local.example.yml` to `config/spnet.local.yml` and set the SharePoint Designer WebsiteCache folder, or set `SPNET_SPD_CACHE`.
2. Validate local configuration before building:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action ValidateConfig -Config config\spnet.local.yml
```

3. Edit `samples/workflow.example.yml` or your own `spnet.workflow/v1` YAML file.
4. Build YAML to SPD-compatible XAML:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action Build -Workflow samples\workflow.example.yml -XamlPath artifacts\YamlFirstSmoke.xaml -Config config\spnet.local.yml
```

Equivalent direct CLI:

```powershell
.\src\SPNet.Workflow.WfSerializer\bin\Release\net48\SPNet.Workflow.WfSerializer.exe build --workflow samples\workflow.example.yml --out artifacts\YamlFirstSmoke.xaml --config config\spnet.local.yml
```

5. Inspect or export generated/downloaded XAML:

```powershell
.\src\SPNet.Workflow.WfSerializer\bin\Release\net48\SPNet.Workflow.WfSerializer.exe export --xaml artifacts\YamlFirstSmoke.xaml --out artifacts\YamlFirstSmoke.exported.yml
.\src\SPNet.Workflow.WfSerializer\bin\Release\net48\SPNet.Workflow.WfSerializer.exe inspect --xaml artifacts\YamlFirstSmoke.xaml --config config\spnet.local.yml
```

6. Publish through the retained PowerShell boundary. By default publish builds from `-Workflow` to `-XamlPath` first; pass `-NoBuild` when publishing an existing XAML file directly:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action Publish -Workflow samples\workflow.example.yml -XamlPath artifacts\YamlFirstSmoke.xaml -SiteUrl 'https://tenant.sharepoint.com/sites/site' -WorkflowName YamlFirstSmoke -TargetType Site -DryRun
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action Publish -NoBuild -XamlPath artifacts\YamlFirstSmoke.xaml -SiteUrl 'https://tenant.sharepoint.com/sites/site' -WorkflowName YamlFirstSmoke -TargetType Site -DryRun
```

Remove `-DryRun` when SharePoint authentication and WorkflowServices CSOM dependencies are available.

## YAML schema `spnet.workflow/v1`

Supported top-level fields:

- `schemaVersion`: must be `spnet.workflow/v1`.
- `name`: friendly workflow name.
- `technicalName`: optional WF class name; defaults to `name + .MTW`.
- `start`: `manual`, `autoStartCreate`, `autoStartChange` metadata for authoring/publish tooling.
- `target`: `type` and optional `listTitle` metadata for publish tooling.
- `variables`: typed variables currently mapped to WF dynamic activity properties; `Double`/`Number`, `String`, and `Boolean`/`Bool` are supported.
- `stages`: one or more stages, each with supported actions.

Supported actions:

- `calc`: emits SharePoint `Calc`, with `lValue`, `rValue`, `operator`, and `to`.
- `assign` / `setVariable`: emits WF `Assign<T>` against an existing YAML variable, with `to` and `value`.
- `writeHistory`: emits SharePoint `WriteToHistory`, with `message`.
- `setStatus`: emits SharePoint `SetWorkflowStatus`, with `status`.

The external YAML shape is intentionally stable. Internally, action YAML is deserialized into a discriminated action hierarchy (`calc`, `writeHistory`, `setStatus`, and assignment actions) so action-specific validation and WF activity construction stay scoped to the supported action type instead of one broad property bag.

Supported expressions:

- literal values: `literal: 1` or `literal: "text"`.
- variable references: `variable: calc`.
- conversion to string: `toString: { variable: calc }`.
- conversion to string alternative form: `type: toString` with nested `value`, for example `value: { type: toString, value: { variable: calc } }`.

Assignment example:

```yaml
- type: assign
  to: assignedNumber
  value:
    literal: 123
- type: setVariable
  to: assignedText
  value:
    type: toString
    value:
      variable: assignedNumber
```

Deferred actions for future safe expansion batches: list item mutation/query actions, email, task/process actions, dictionary actions, HTTP/web service actions, person/group and lookup field actions, conditional branching, and loops. These require SharePoint Designer reference XAML before being emitted from YAML.

## Configuration

- `config/spnet.defaults.yml` contains safe committed defaults and known SharePoint Designer metadata tokens.
- `config/spnet.local.yml` is ignored and should contain workstation-specific paths.
- `SPNET_SPD_CACHE` can provide the WebsiteCache folder without a local config file.

External dependencies are not packaged: SharePoint Designer WebsiteCache DLLs, Office install paths, SharePoint auth/session state, and workflow IDs remain environment/config driven.

## Retained components

- `src/SPNet.Workflow.WfSerializer`: .NET Framework 4.8 serializer/converter. It loads WebsiteCache proxy DLLs, builds real WF activity trees, serializes XAML, and injects SPD stage metadata.
- `scripts/Invoke-SPNetWorkflow.ps1`: live SharePoint publish/download boundary.
- `scripts/Get-SPNetWorkflowDiagnostics.ps1`: read-only SharePoint workflow diagnostic/export helper.
- `scripts/Invoke-SPNetYamlWorkflow.ps1`: wrapper/combiner for YAML build/export/inspect/publish/download actions.
- `scripts/Test-SPNetYamlWorkflowGolden.ps1`: local golden validation harness for the YAML-first path.

## Validation

The YAML-first baseline has been validated end-to-end in SharePoint Designer with `YamlFirstSmoke`: the workflow opens in Designer, the stage/action structure is visible, and Designer `Check for Errors` reports no errors. Generated artifacts are intentionally ignored by Git; keep only `artifacts/.gitkeep` committed in the workspace.

```powershell
dotnet build .\SPNet.slnx
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action ValidateConfig -Config config\spnet.local.yml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action Build -Workflow samples\workflow.example.yml -XamlPath artifacts\YamlFirstSmoke.xaml -Config config\spnet.local.yml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action Export -XamlPath artifacts\YamlFirstSmoke.xaml -Out artifacts\YamlFirstSmoke.exported.yml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-SPNetYamlWorkflowGolden.ps1 -Workflow samples\workflow.example.yml -XamlPath artifacts\golden\YamlFirstSmoke.xaml -Config config\spnet.local.yml
```

Downloaded workflow XAML can be captured without publishing:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action Download -SiteUrl 'https://tenant.sharepoint.com/sites/site' -WorkflowName YamlFirstSmoke -Out artifacts\YamlFirstSmoke.downloaded.xaml
```

Live publish validation requires SharePoint auth/session support and the legacy WorkflowServices/PnP dependencies used by `scripts/Invoke-SPNetWorkflow.ps1`.
