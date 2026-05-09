# SPNet YAML-first SharePoint workflow authoring

SPNet now focuses on editing SharePoint 2013 / Workflow Manager workflows as controlled YAML, mapping that YAML to real Windows Workflow Foundation `ActivityBuilder` objects, serializing through the proven SharePoint Designer-compatible WF serializer path, and publishing the resulting XAML through a separate CSOM-only publisher.

This is intentionally not the old broad YAML conversion pipeline. The YAML schema is small, explicit, and only emits supported WF activity objects before the existing serializer normalizes SharePoint Designer metadata.

See the action support matrix for the current implementation, alias, validation, sample, test, export, and risk status: [docs/action-support-matrix.md](docs/action-support-matrix.md).

## Current architecture

- `src/SPNet.Workflow.WfSerializer`: legacy .NET Framework 4.8 serializer/converter. It owns YAML parsing, WF activity construction, XAML export/inspection, SharePoint Designer metadata normalization, and WebsiteCache proxy assembly loading.
- `src/SPNet.Workflow.Publisher.Csom`: standalone .NET Framework 4.8 WorkflowServices CSOM publisher. It treats generated XAML as opaque text and intentionally does not reference WF, SharePoint Designer, or serializer assemblies.
- `tests/SPNet.Workflow.WfSerializer.Tests`: lightweight automated tests for YAML deserialization, action aliases, validation, and unsafe top-level lookup rejection. These tests do not require SharePoint or WebsiteCache proxy assemblies.
- `scripts/Invoke-SPNetYamlWorkflow.ps1`: orchestration wrapper for local build/export/inspect/golden validation and live publish/list/cleanup flows.
- `artifacts/`: ignored generated output and diagnostics workspace. Only `artifacts/.gitkeep` is intentional source control content.

If editor state shows `src/SPNet.Workflow.Core/`, `src/SPNet.Workflow.Cli/`, or `src/SPNet.Workflow.SharePoint/`, those projects are not present in this repository snapshot and are not included in `SPNet.slnx`.

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
- `createListItem`: emits SharePoint `CreateListItem`, with `listId` (defaults to `type: getCurrentListId`), non-empty `fields`, and optional `itemIdTo` / `itemGuidTo` outputs. Target-by-title is publish metadata only; the proxy activity requires a Guid `ListId` argument.
- `updateListItem`: emits SharePoint `UpdateListItem`, with `listId`, exactly the item identity you provide via `itemId` and/or `itemGuid`, and non-empty `fields`.
- `deleteListItem`: emits SharePoint `DeleteListItem`, with `listId` plus `itemId` and/or `itemGuid`. Delete is supported by the proxy metadata but intentionally omitted from the safe list lifecycle sample.
- `lookupListItemStringProperty` / `lookupSPListItemStringProperty`: supported only as a nested string expression inside another visible action, normally `assign` / `setVariable`. It emits SharePoint `LookupSPListItemStringProperty`, with `listId` (defaults to `type: getCurrentListId`), `itemId` and/or `itemGuid`, and `fieldName` or `propertyName`. Top-level list item lookup actions are rejected because SharePoint Designer can render them as invisible actions and crash when properties are opened.
- `lookupListItemIntProperty` / `lookupSPListItemIntProperty`: YAML shape and Int32 `to` validation are present, but the tested PMteamblog WebsiteCache proxy assembly does not contain `LookupSPListItemIntProperty`; using it with that cache fails fast at build time and is documented as unsupported for that environment.
- `callHttpWebService` / `callHttp` / `http`: emits SharePoint `CallHTTPWebService` with `address`, `requestType`, and any response targets: `responseStatusCodeTo`, `responseContentTo`, and `responseHeadersTo`. Literal methods accept `GET`, `POST`, `PUT`, `DELETE` and `HTTPGET`, `HTTPPOST`, `HTTPPUT`, `HTTPDELETE`; aliases are normalized to the `HTTP*` values SharePoint Designer expects.
- `sendEmail` / `email`: emits SharePoint `Email` with `to`, `cc`, `subject`, and `body`. These fields accept scalar literal shorthand or full expression mappings. Recipients are always wrapped in the SPD-compatible `ExpandInitFormUsers` + `BuildCollection` shape; literal recipient lists are split on `;` and `,`, while variable and `formatString` recipients are emitted as a single dynamic `BuildCollection` item. `subject` and `body` support literals, variables, workflow-context/list-item lookups, `toString`, and `formatString`; `body` may be an HTML string composed with variables/lookups. Use safe placeholder recipients such as `spnet-workflow-test@example.invalid` in samples and validation. Attachments, from/reply-to, BCC, importance, and task-notification coupling are not implemented.
- `singleTask` / `task`: emits bounded SharePoint `SingleTask` only, based on the `ExampleWF2` reference. Minimal YAML is `assignedTo`, `title`, optional `taskBody`, optional `dueDate`, `taskIdTo`, and `outcomeTo`. Defaults intentionally waive assignment/cancelation emails in samples to avoid accidental real notifications; do not live-publish task workflows until assignees and notification settings are reviewed.
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

List item lifecycle example:

```yaml
- type: createListItem
  listId:
    type: getCurrentListId
  itemIdTo: createdItemId
  itemGuidTo: createdItemGuid
  fields:
    Title:
      literal: SPNet lifecycle create
- type: updateListItem
  listId:
    type: getCurrentListId
  itemId:
    variable: createdItemId
  fields:
    Title:
      literal: SPNet lifecycle updated
```

List item property lookup example:

```yaml
- type: assign
  to: readBackTitle
  value:
    type: lookupListItemStringProperty
    listId:
      type: getCurrentListId
    itemId:
      variable: createdItemId
    fieldName: Title
```

SharePoint Designer safety caveat: `LookupSPListItemStringProperty` passed server/publish validation as a direct stage child, but the published `SPNetYamlListItemLookupManual-20260509-1607` workflow opened with the lookup action invisible and Designer crashed when its properties were selected. Treat direct/top-level list item lookup actions as SPD-unsafe even when publish validation succeeds; keep the lookup nested inside an assignment or another visible action argument.

SharePoint Designer rendering caveat: lifecycle actions can show misleading local/designer UI state even when the generated workflow is valid. Manual inspection of `SPNetYamlListLifecycleManual-20260509-1535`, published to `TestList`, showed that Designer did not expose every generated part cleanly: the created item ID output was not visibly represented as an integer output in the action builder, some later action builders/lookups did not show the `itemIdTo` variable cleanly, and Designer drew red boxes around actions as if local designer validation had concerns. Despite those visual quirks, Designer `Check for Errors` reported no errors, publishing remained allowed, and runtime execution succeeded: the workflow created a list item and then updated that newly created item's title through the returned `createdItemId`. Treat Designer visual/local rendering for `createListItem` / `updateListItem` / `deleteListItem` as potentially misleading; server validation and runtime execution are the source of truth for the tested create/update flow. In particular, `itemIdTo` / created-item ID outputs can be functional even when SharePoint Designer does not expose the variable cleanly in builders or lookup pickers.

Delete shape, for workflows that intentionally delete a known safe item:

```yaml
- type: deleteListItem
  listId:
    type: getCurrentListId
  itemId:
    variable: createdItemId
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

Reflection/reference inspection against the SharePoint Designer WebsiteCache proxy assembly and downloaded PMteamblog XAML confirmed many additional SharePoint activity types. The first expansion batch is intentionally limited to scalar/low-risk activities whose writable proxy properties map directly to typed WF arguments: `Comment.CommentText`, `DelayFor.Days`/`Hours`/`Minutes`, and `DelayUntil.Date`. The lookup expansion now emits current workflow/list/item lookup activities only as nested expression activities in `InArgument` values (`LookupWorkflowContextProperty.PropertyName`, nested `GetCurrentListId`, and nested `GetCurrentItemGuid`), matching the downloaded SharePoint Designer XAML pattern and avoiding known SPD-crashing blank top-level lookup actions. The third expansion batch adds only current-item `SetField` because downloaded reference XAML shows a clear safe current-item shape, for example `SetField FieldName="Title"` with current item `AppliesTo` metadata and an object `FieldValue` argument. The HTTP batch adds Designer-rendered `CallHTTPWebService`, `LookupSPListItemPropertyNameInREST`, request method normalization, and guarded `DynamicValue` response plumbing. The list item lifecycle batch is based on reflected WebsiteCache proxy types `CreateListItem`, `UpdateListItem`, and `DeleteListItem`: all expose `ListId`, `ItemGuid`, `ItemId`, and dictionary `ListItemProperties` where applicable; `CreateListItem` is `Activity<Guid>` and additionally exposes `ItemGuid` as `InOutArgument<Guid>` plus `ItemId` as `OutArgument<int>`. Runtime validation for `SPNetYamlListLifecycleManual-20260509-1535` confirmed the create/update sequence against `TestList` even though SharePoint Designer rendered red local-validation boxes and did not show the returned item ID variable cleanly in all UI surfaces. The email/task batch adds bounded `sendEmail` only: PMteamblog WebsiteCache exposes `Microsoft.SharePoint.WorkflowServices.Activities.Email` with writable `Subject`, `To`, `CC`, `BCC`, `Body`, and `AdditionalHeaders`; no `SendEmail` type was present. Task metadata exposed `SingleTask`, `CompositeTask`, and `GetTaskListId`, but not simple `CreateTask`, `AssignTask`, `StartTaskProcess`, or `CollectDataFromUser` types; the task proxies include participant collections, outcome/completion criteria, reminder/cancelation email settings, related content links, and wait/preserve behavior, so task actions remain deferred.

Deferred actions for future safe expansion batches: `copyItem`, `checkInItem`, `checkOutItem`, `undoCheckOutItem`, `setModerationStatus`, `waitForFieldChange`, `waitForItemEvent`, composite task/process actions beyond the bounded `singleTask` wrapper, general dictionary/dynamic-value mutation actions, person/group and lookup field actions, workflow interop, arbitrary list item field lookups such as `LookupSPListItemStringProperty`, and principal lookups. These require more property/value-shape validation before being emitted from YAML.

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

The YAML-first baseline has been validated end-to-end in SharePoint Designer with `YamlFirstSmoke`: the workflow opens in Designer, the stage/action structure is visible, and Designer `Check for Errors` reports no errors. HTTP actions, DynamicValue property extraction, current-item list field updates, local list smoke generation, read-only list workflow listing, and CSOM list publishing have also been validated. List lifecycle create/update has been runtime-validated with `SPNetYamlListLifecycleManual-20260509-1535` on `TestList`: Designer showed misleading red boxes and incomplete variable-picker/action-builder rendering for the created item ID, but `Check for Errors` passed and runtime execution created an item and updated that new item's title via the returned ID. Generated artifacts are intentionally ignored by Git; keep only `artifacts/.gitkeep` committed in the workspace.

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

- YAML support is intentionally limited to logs/status/comments, assignment, scalar control flow, current item `setField`, HTTP calls, bounded `sendEmail`, bounded `singleTask`, REST property-name lookup, workflow context/current list/current item expressions, and DynamicValue string property extraction.
- `DynamicValue` variables are generated only for HTTP response targets; arbitrary YAML-declared `DynamicValue` variables and general dictionary mutation actions are deferred.
- Top-level lookup actions are rejected because SharePoint Designer can render them as blank/crashing actions; use nested lookup expressions inside assignment or action arguments. This includes list item property lookups such as `lookupListItemStringProperty`.
- The CSOM publisher does not overwrite, delete, or migrate existing live workflows; `if-exists` currently fails on name conflicts.
- Site/list publishing is validated for current SharePoint WorkflowServices scenarios, but Email/task workflows should remain local/golden validated only unless intentionally reviewed for safe recipients, assignees, and side effects. Broader task/process/list-item CRUD actions remain deferred until safe XAML shapes are captured and validated.
