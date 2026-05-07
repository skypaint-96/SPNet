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
- `variables`: typed variables currently mapped to WF dynamic activity properties; `Double`/`Number`, `String`, `Boolean`/`Bool`, `Int32`/`Int`/`Integer`, `Guid`, and `DateTime`/`Date` are supported.
- `stages`: one or more stages, each with supported actions.

Supported actions:

- `calc`: emits SharePoint `Calc`, with `lValue`, `rValue`, `operator`, and `to`.
- `assign` / `setVariable`: emits WF `Assign<T>` against an existing YAML variable, with `to` and `value`.
- `writeHistory`: emits SharePoint `WriteToHistory`, with `message`.
- `setStatus`: emits SharePoint `SetWorkflowStatus`, with `status`.
- `comment`: emits SharePoint `Comment`, with `text`. This is a Designer annotation activity, not a runtime history-log action.
- `delayFor`: emits SharePoint `DelayFor`, with numeric `days`, `hours`, and `minutes` expressions.
- `delayUntil`: emits SharePoint `DelayUntil`, with a `date` expression. Prefer an explicit ISO-like date literal or a declared `DateTime` variable.
- `while` / `loop`: emits WF `While`, with a comparison `condition` and nested `actions` sequence.
- `if`: emits WF `If`, with a comparison `condition`, nested `then` sequence, and optional nested `else` sequence.
- `lookupWorkflowContext` / `lookupContextProperty`: emits SharePoint `LookupWorkflowContextProperty`, with `propertyName` and string `to` output. Reference XAML confirms scalar context properties such as `CurrentWebUrl` and `CurrentItemUrl`.
- `getCurrentListId`: emits SharePoint `GetCurrentListId`, with Guid `to` output.
- `getCurrentItemGuid`: emits SharePoint `GetCurrentItemGuid`, with Guid `to` output.
  - Note: Guid variables are safe as lookup outputs, but generic `toString` expression conversion for Guid variables is deferred; write scalar string context values directly to history.
- `setField`: emits SharePoint `SetField` for the current item only, with `fieldName` and scalar/object `value`. This is a mutating list workflow action; use only on intentional test list items or controlled list workflow contexts.

The external YAML shape is intentionally stable. Internally, action YAML is deserialized into a discriminated action hierarchy (`calc`, `writeHistory`, `setStatus`, and assignment actions) so action-specific validation and WF activity construction stay scoped to the supported action type instead of one broad property bag.

Supported expressions:

- literal values: `literal: 1` or `literal: "text"`.
- variable references: `variable: calc`.
- conversion to string: `toString: { variable: calc }`.
- conversion to string alternative form: `type: toString` with nested `value`, for example `value: { type: toString, value: { variable: calc } }`.

Lookup example:

```yaml
- type: lookupWorkflowContext
  propertyName: CurrentWebUrl
  to: currentWebUrl
- type: getCurrentListId
  to: currentListId
- type: writeHistory
  message:
    variable: currentWebUrl
```

Control-flow comparison expressions:

- comparison types: `isLessThan` / `lessThan`, `equals`, `greaterThan`, `lessThanOrEqual`, and `greaterThanOrEqual`.
- operands: numeric `literal` or `variable` expressions, currently emitted as `Double` WF expression operands.

Control-flow example:

```yaml
- type: while
  condition:
    type: isLessThan
    left:
      variable: counter
    right:
      literal: 3
  actions:
  - type: writeHistory
    message:
      type: toString
      value:
        variable: counter
- type: if
  condition:
    type: equals
    left:
      variable: counter
    right:
      literal: 3
  then:
  - type: setStatus
    status: Then branch
  else:
  - type: setStatus
    status: Else branch
```

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

List action example:

```yaml
- type: setField
  fieldName: Title
  value:
    literal: SPNet YAML list-action smoke
```

Reflection/reference inspection against the SharePoint Designer WebsiteCache proxy assembly and downloaded PMteamblog XAML confirmed many additional SharePoint activity types. The first expansion batch is intentionally limited to scalar/low-risk activities whose writable proxy properties map directly to typed WF arguments: `Comment.CommentText`, `DelayFor.Days`/`Hours`/`Minutes`, and `DelayUntil.Date`. The second expansion batch adds current workflow/list/item lookup activities with clear scalar output shapes: `LookupWorkflowContextProperty.PropertyName`/`Result`, `GetCurrentListId.Result`, and `GetCurrentItemGuid.Result`. The third expansion batch adds only current-item `SetField` because downloaded reference XAML shows a clear safe current-item shape, for example `SetField FieldName="Title"` with current item `AppliesTo` metadata and an object `FieldValue` argument.

Deferred actions for future safe expansion batches: `updateListItem`, `createListItem`, `deleteListItem`, `copyItem`, `checkInItem`, `checkOutItem`, `undoCheckOutItem`, `setModerationStatus`, `waitForFieldChange`, `waitForItemEvent`, email, task/process actions, dictionary/dynamic-value actions, HTTP/web service actions, person/group and lookup field actions, workflow interop, arbitrary list item field lookups such as `LookupSPListItemStringProperty`, and principal lookups. These require more property/value-shape validation before being emitted from YAML.

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
