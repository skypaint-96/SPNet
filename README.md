# SPNet YAML-first SharePoint workflow authoring

SPNet now focuses on editing SharePoint 2013 / Workflow Manager workflows as controlled YAML, mapping that YAML to real Windows Workflow Foundation `ActivityBuilder` objects, serializing through the proven SharePoint Designer-compatible WF serializer path, and publishing the resulting XAML through a separate CSOM-only publisher.

This is intentionally not the old broad YAML conversion pipeline. The YAML schema is small, explicit, and only emits supported WF activity objects before the existing serializer normalizes SharePoint Designer metadata.

## Current architecture

- `src/SPNet.Workflow.WfSerializer`: legacy .NET Framework 4.8 serializer/converter. It owns YAML parsing, WF activity construction, XAML export/inspection, SharePoint Designer metadata normalization, and WebsiteCache proxy assembly loading.
- `src/SPNet.Workflow.Publisher.Csom`: standalone .NET Framework 4.8 WorkflowServices CSOM publisher. It treats generated XAML as opaque text and intentionally does not reference WF, SharePoint Designer, or serializer assemblies.
- `scripts/Invoke-SPNetYamlWorkflow.ps1`: orchestration wrapper for local build/export/inspect/golden validation and live publish/list/cleanup flows.
- `artifacts/`: ignored generated output and diagnostics workspace. Only `artifacts/.gitkeep` is intentional source control content.

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

6. Publish through the retained PowerShell boundary. By default publish builds from `-Workflow` to `-XamlPath` first; pass `-NoBuild` when publishing an existing XAML file directly. The current publisher path uses WorkflowServices CSOM and submits XAML as opaque text:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action Publish -Workflow samples\workflow.example.yml -XamlPath artifacts\YamlFirstSmoke.xaml -SiteUrl 'https://tenant.sharepoint.com/sites/site' -WorkflowName YamlFirstSmoke -TargetType Site -DryRun
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action Publish -NoBuild -XamlPath artifacts\YamlFirstSmoke.xaml -SiteUrl 'https://tenant.sharepoint.com/sites/site' -WorkflowName YamlFirstSmoke -TargetType Site -DryRun
```

Remove `-DryRun` only when SharePoint authentication/session state and WorkflowServices CSOM dependencies are available. For browser-authenticated SharePoint Online sessions, the wrapper can hand off WebLogin/WinINet cookies to the CSOM publisher; username/password/domain credentials and default Windows credentials remain available for environments that support them.

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
- Lookup activities are supported only as expression values inside another activity, normally `assign` / `setVariable`. Top-level `lookupWorkflowContext` / `lookupContextProperty`, `getCurrentListId`, and `getCurrentItemGuid` actions are deprecated because SharePoint Designer can render them as blank actions and crash when their properties are selected.
- `lookupWorkflowContext` / `lookupContextProperty` expressions emit SharePoint `LookupWorkflowContextProperty` inside an `InArgument`, with `propertyName`. Reference XAML confirms scalar context lookups such as `CurrentWebUrl` are nested expression activities rather than standalone stage actions.
- `getCurrentListId` and `getCurrentItemGuid` expressions emit nested SharePoint `GetCurrentListId` / `GetCurrentItemGuid` inside Guid `InArgument` values.
  - Note: Guid variables are safe as lookup assignment outputs, but generic `toString` expression conversion for Guid variables is deferred; write scalar string context values directly to history.
- `setField`: emits SharePoint `SetField` for the current item only, with `fieldName` and scalar/object `value`. This is a mutating list workflow action; use only on intentional test list items or controlled list workflow contexts.
- `callHttpWebService` / `callHttp` / `http`: emits SharePoint `CallHTTPWebService` with `address`, `requestType`, and any response targets: `responseStatusCodeTo`, `responseContentTo`, and `responseHeadersTo`. Literal methods accept `GET`, `POST`, `PUT`, `DELETE` and `HTTPGET`, `HTTPPOST`, `HTTPPUT`, `HTTPDELETE`; aliases are normalized to the `HTTP*` values SharePoint Designer expects.
- `lookupRestPropertyName` / `lookupSPListItemPropertyNameInREST`: emits SharePoint `LookupSPListItemPropertyNameInREST` with `listId`, `propertyName`, and `to`.
- `getDynamicValueProperty` / `getDictionaryItem` / `getDictionaryValue` / `getResponseProperty`: extracts a string property from a `DynamicValue` HTTP response variable, with `source`, `propertyName`, and `to`.

The external YAML shape is intentionally stable. Internally, action YAML is deserialized into a discriminated action hierarchy (`calc`, `writeHistory`, `setStatus`, and assignment actions) so action-specific validation and WF activity construction stay scoped to the supported action type instead of one broad property bag.

Supported expressions:

- literal values: `literal: 1` or `literal: "text"`.
- variable references: `variable: calc`.
- conversion to string: `toString: { variable: calc }`.
- conversion to string alternative form: `type: toString` with nested `value`, for example `value: { type: toString, value: { variable: calc } }`.
- lookup expressions for assignment values: `type: lookupWorkflowContext` / `lookupContextProperty` with `propertyName`, `type: getCurrentListId`, and `type: getCurrentItemGuid`.

Lookup example:

```yaml
- type: assign
  to: currentWebUrl
  value:
    type: lookupWorkflowContext
    propertyName: CurrentWebUrl
- type: assign
  to: currentListId
  value:
    type: getCurrentListId
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

HTTP/web service example:

```yaml
- type: callHttpWebService
  address:
    type: formatString
    literal: "{0}/_api/web/currentuser"
    value:
      type: lookupWorkflowContext
      propertyName: CurrentWebUrl
  requestType:
    literal: GET
  responseStatusCodeTo: httpStatusCode
  responseContentTo: httpResponseContent
  responseHeadersTo: httpResponseHeaders
```

HTTP response content and headers are emitted as SharePoint Designer proxy `DynamicValue` variables at build time; they do not need to be declared in YAML, and declaring arbitrary `DynamicValue` variables is intentionally rejected. `RequestContent` and `RequestHeaders` are initialized to empty `DynamicValue` references so Designer can render the HTTP action. This HTTP shape was publish-validated against PMteamblog and opens with the expected display in SharePoint Designer. Known caveat: using top-level lookup actions can still leave an invisible previous action in Designer; keep lookup activities nested inside expressions such as the `CurrentWebUrl` URL construction above.

DynamicValue extraction example:

```yaml
- type: getDynamicValueProperty
  source: httpResponseContent
  propertyName:
    literal: Title
  to: currentUserTitle
```

Reflection/reference inspection against the SharePoint Designer WebsiteCache proxy assembly and downloaded PMteamblog XAML confirmed many additional SharePoint activity types. The first expansion batch is intentionally limited to scalar/low-risk activities whose writable proxy properties map directly to typed WF arguments: `Comment.CommentText`, `DelayFor.Days`/`Hours`/`Minutes`, and `DelayUntil.Date`. The lookup expansion now emits current workflow/list/item lookup activities only as nested expression activities in `InArgument` values (`LookupWorkflowContextProperty.PropertyName`, nested `GetCurrentListId`, and nested `GetCurrentItemGuid`), matching the downloaded SharePoint Designer XAML pattern and avoiding known SPD-crashing blank top-level lookup actions. The third expansion batch adds only current-item `SetField` because downloaded reference XAML shows a clear safe current-item shape, for example `SetField FieldName="Title"` with current item `AppliesTo` metadata and an object `FieldValue` argument. The HTTP batch adds Designer-rendered `CallHTTPWebService`, `LookupSPListItemPropertyNameInREST`, request method normalization, and guarded `DynamicValue` response plumbing.

Deferred actions for future safe expansion batches: `updateListItem`, `createListItem`, `deleteListItem`, `copyItem`, `checkInItem`, `checkOutItem`, `undoCheckOutItem`, `setModerationStatus`, `waitForFieldChange`, `waitForItemEvent`, email, task/process actions, general dictionary/dynamic-value mutation actions, person/group and lookup field actions, workflow interop, arbitrary list item field lookups such as `LookupSPListItemStringProperty`, and principal lookups. These require more property/value-shape validation before being emitted from YAML.

## CSOM publishing, listing, and cleanup

The CSOM publisher supports site workflows and list workflows. List publishing resolves the target list by `-TargetListTitle` or `-TargetListId`, creates a WorkflowServices definition scoped to that list, publishes a subscription for that list, and applies manual/create/update start flags from YAML or explicit parameters. It expects standard `Workflow History` and `Workflow Tasks` lists to exist in the target web.

Publishing currently uses an intentionally conservative `--if-exists Fail` policy. If a workflow definition with the requested name already exists, local tooling reports the conflict instead of overwriting or deleting live SharePoint content.

Use read-only listing before any cleanup:

```powershell
.\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action List -SiteUrl https://tenant/sites/site -WorkflowNamePrefix SPNetYamlHttp -IncludeSubscriptions
```

Cleanup is guarded and refuses to run unless `-Force` is supplied. Prefix cleanup also requires a prefix of at least eight characters; exact-name cleanup can use `-WorkflowName`.

```powershell
.\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action Cleanup -SiteUrl https://tenant/sites/site -WorkflowNamePrefix SPNetYamlHttpSmoke -Force
```

Do not use cleanup against production names such as `New Leave Request`. If a publish fails after saving a definition but before subscription creation, the publish result reports `PartialDefinitionSaved` or `PartialDefinitionPublished` plus the definition ID when available; run `List` by the exact test name or prefix first, then clean only the confirmed test artifacts.

## Configuration and WebsiteCache requirement

- `config/spnet.defaults.yml` contains safe committed defaults and known SharePoint Designer metadata tokens.
- `config/spnet.local.yml` is ignored and should contain workstation-specific paths.
- `SPNET_SPD_CACHE` can provide the WebsiteCache folder without a local config file.

The serializer requires a SharePoint Designer WebsiteCache folder containing the SharePoint and Microsoft Activities proxy assemblies, including `Microsoft.SharePoint.WorkflowServices.Activities.Proxy.dll` and `Microsoft.Activities.Proxy.dll`. This requirement applies to local build, export, and inspection of WF activity trees. The CSOM publisher does not use WebsiteCache; it only needs built XAML plus SharePoint CSOM authentication.

External dependencies are not packaged: SharePoint Designer WebsiteCache DLLs, Office install paths, SharePoint auth/session state, and workflow IDs remain environment/config driven.

## Retained components

- `src/SPNet.Workflow.WfSerializer`: .NET Framework 4.8 serializer/converter. It loads WebsiteCache proxy DLLs, builds real WF activity trees, serializes XAML, and injects SPD stage metadata.
- `scripts/Invoke-SPNetWorkflow.ps1`: live SharePoint publish/download boundary.
- `scripts/Get-SPNetWorkflowDiagnostics.ps1`: read-only SharePoint workflow diagnostic/export helper.
- `scripts/Invoke-SPNetYamlWorkflow.ps1`: wrapper/combiner for YAML build/export/inspect/publish/download actions.
- `scripts/Test-SPNetYamlWorkflowGolden.ps1`: local golden validation harness for the YAML-first path.

## Validation

The YAML-first baseline has been validated end-to-end in SharePoint Designer with `YamlFirstSmoke`: the workflow opens in Designer, the stage/action structure is visible, and Designer `Check for Errors` reports no errors. HTTP actions, DynamicValue property extraction, current-item list field updates, local list smoke generation, read-only list workflow listing, and CSOM list publishing have also been validated. Generated artifacts are intentionally ignored by Git; keep only `artifacts/.gitkeep` committed in the workspace.

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

Live publish validation requires SharePoint auth/session support and pinned SharePoint Online CSOM dependencies used by the publisher project. Do not publish or clean workflows as part of local validation unless intentionally performing a live SharePoint smoke test. Manual validation guidance: publish a uniquely named test workflow, open it in SharePoint Designer, run `Check for Errors`, validate the target list subscription/start behavior, then list and clean only the uniquely named test artifacts.

## Known limitations

- YAML support is intentionally limited to logs/status/comments, assignment, scalar control flow, current item `setField`, HTTP calls, REST property-name lookup, workflow context/current list/current item expressions, and DynamicValue string property extraction.
- `DynamicValue` variables are generated only for HTTP response targets; arbitrary YAML-declared `DynamicValue` variables and general dictionary mutation actions are deferred.
- Top-level lookup actions are rejected because SharePoint Designer can render them as blank/crashing actions; use nested lookup expressions inside assignment or action arguments.
- The CSOM publisher does not overwrite, delete, or migrate existing live workflows; `if-exists` currently fails on name conflicts.
- Site/list publishing is validated for current SharePoint WorkflowServices scenarios, but broader task/process/email/list-item CRUD actions remain deferred until safe XAML shapes are captured and validated.
