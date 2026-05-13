# SPNet YAML-first SharePoint workflow authoring

SPNet now focuses on editing SharePoint 2013 / Workflow Manager workflows as controlled YAML, mapping that YAML to real Windows Workflow Foundation `ActivityBuilder` objects, serializing through the proven SharePoint Designer-compatible WF serializer path, and publishing the resulting XAML through a separate CSOM-only publisher.

This is intentionally not the old broad YAML conversion pipeline. The YAML schema is small, explicit, and only emits supported WF activity objects before the existing serializer normalizes SharePoint Designer metadata.

Generated SharePoint workflow XAML must not contain raw WF language expression activities such as `VisualBasicValue`, `VisualBasicReference`, `CSharpValue`, or `CSharpReference`. Those activities require VB/C# expression compilation, and SharePoint Workflow Manager validation rejects them for published workflows (for example, raw `Microsoft.CSharp.Activities.CSharpValue<TResult>` fails as an invalid type). SPNet therefore emits structured SharePoint/Workflow Manager-safe activity nodes, primarily SharePoint proxy activities and `Microsoft.Activities.Expressions` proxy expression activities. Export/import compatibility may still recognize raw VB/C# expression text from legacy or downloaded XAML so it can produce diagnostic YAML, but that compatibility path is not an endorsed output format.

See the action support matrix for the current implementation, alias, validation, sample, test, export, and risk status: [docs/action-support-matrix.md](docs/action-support-matrix.md).

## Release framing and production guidance

This release is a stabilisation release for YAML-first workflow authoring. It is appropriate for controlled production use only when the workflow is built from documented **stable** actions, reviewed against the target SharePoint site/list, and validated through a test publish/download/runtime cycle before business use. Stable actions are not the same as **preview**, **experimental**, or **dev-only** actions:

- **Stable** actions use visible or well-understood Workflow Manager-safe activity shapes and are the default choice for production workflows.
- **Preview** actions build and have targeted validation, but may involve timers, external HTTP services, email/task side effects, list mutations, exact SharePoint Designer metadata, or partial export support. Use them only after validating against the target site and rollback plan.
- **Experimental** actions are for controlled trials. They commonly involve `DynamicValue` or hidden `Microsoft.Activities` shapes whose runtime behavior can depend on the real response payload, proxy assembly version, and SharePoint Workflow Manager behavior.
- **Dev-only** actions are for local diagnostics, sample builds, and developer experiments. A successful local build with WebsiteCache proxy assemblies does not prove that SharePoint publish, Designer rendering, or runtime execution will be acceptable.
- **Unsupported** shapes are documented limitations or rejected YAML surfaces and should not be published.

Production guidance:

- Keep production workflows small, observable, and mostly orchestration-focused: set state, call bounded services, update a small number of SharePoint fields/items, and send reviewed notifications. Do not use workflows for heavy matrix-style computation, bulk data shaping, or large in-workflow transformations.
- Treat Workflow Manager limits as practical design limits even when a generated XAML file builds locally: large workflows, deep nesting, high variable/property counts, long or unbounded loops, large `DynamicValue` payloads, and repeated large string operations can publish slowly, fail validation, render poorly in SharePoint Designer, or fail at runtime.
- Validate every production candidate in a non-production site/list first: build YAML, inspect the generated XAML/metadata JSON, publish with a unique test name, open in SharePoint Designer, run `Check for Errors`, run realistic start conditions, download the workflow, and compare behavior before promoting the same shape.
- Avoid hidden or experimental activities in production unless the owning team explicitly accepts the risk and has performed target-environment publish/runtime tests. Hidden activities may execute while remaining invisible or misleading in SharePoint Designer.
- Be careful with `DynamicValue`: REST payloads can contain missing properties, arrays where objects are expected, primitive/null values, unexpected types, or payloads larger than Workflow Manager can comfortably process. Prefer explicit typed extraction and small response bodies.
- Remember that local build success is not publish/runtime proof. The local serializer uses SharePoint Designer WebsiteCache proxy assemblies; SharePoint publishing and runtime use the target Workflow Manager environment. Version, metadata, auth, list schema, and service-response differences can create local-versus-live mismatches.

## CI, packaging, and releases

GitHub Actions are intentionally scoped to the protected `development` and `production` branches. Pull requests into `production` must come from `development`; `development` runs validation only. A push to `production` runs validation, builds a distributable package, uploads Actions artifacts, and creates a GitHub Release.

Repository versioning is controlled by Nerdbank.GitVersioning via `version.json`. The initial product version is `0.1`, matching the existing project metadata baseline. Builds from `development` are non-public prerelease/dev builds derived from Git history. Builds from `production` are public release builds, so CI uses the Nerdbank-derived stable package version for zip names, NuGet package versions, artifact names, GitHub Release tags, and release titles.

Inspect the local Nerdbank-derived version with:

```powershell
dotnet tool install --global nbgv
nbgv get-version
```

Create a local package with:

```powershell
$version = (nbgv get-version --format json | ConvertFrom-Json).NuGetPackageVersion
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package-SPNetWorkflow.ps1 -Configuration Release -Version $version -OutputDirectory artifacts\release -IncludeNuGetPackages
```

The package includes built command-line tools, runtime PowerShell scripts, safe config examples, docs, samples, and this README. It intentionally excludes local config, SharePoint secrets, SharePoint Designer WebsiteCache/proxy assemblies, generated diagnostics, and transient build artifacts.

Packaged workflow usage should start with the primary command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 help
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 build --workflow samples\workflow.example.yml --out artifacts\YamlFirstSmoke.xaml --config config\spnet.local.yml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 inspect --xaml artifacts\YamlFirstSmoke.xaml --config config\spnet.local.yml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 export --xaml artifacts\YamlFirstSmoke.xaml --out artifacts\YamlFirstSmoke.exported.yml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 publish --workflow samples\workflow.example.yml --xaml artifacts\YamlFirstSmoke.xaml --site-url 'https://tenant.sharepoint.com/sites/site' --workflow-name YamlFirstSmoke --target-type Site --dry-run
```

The retained wrappers and direct executables remain available for compatibility, but `scripts\spnet-workflow.ps1` is the intended packaged entry point.

## Current architecture

- `src/SPNet.Workflow.WfSerializer`: legacy .NET Framework 4.8 serializer/converter. It owns YAML parsing, WF activity construction, XAML export/inspection, SharePoint Designer metadata normalization, and WebsiteCache proxy assembly loading.
- `src/SPNet.Workflow.Publisher.Csom`: standalone .NET Framework 4.8 WorkflowServices CSOM publisher. It treats generated XAML as opaque text and intentionally does not reference WF, SharePoint Designer, or serializer assemblies.
- `tests/SPNet.Workflow.WfSerializer.Tests`: lightweight automated tests for YAML deserialization, action aliases, validation, and unsafe top-level lookup rejection. These tests do not require SharePoint or WebsiteCache proxy assemblies.
- `scripts/spnet-workflow.ps1`: primary packaged CLI entry point with `help`, `build`, `inspect`, `export`, and `publish` subcommands.
- `scripts/Invoke-SPNetYamlWorkflow.ps1`: compatibility orchestration wrapper for local build/export/inspect/golden validation and live publish/list/cleanup flows.
- `artifacts/`: ignored generated output and diagnostics workspace. Only `artifacts/.gitkeep` is intentional source control content.

If editor state shows `src/SPNet.Workflow.Core/`, `src/SPNet.Workflow.Cli/`, or `src/SPNet.Workflow.SharePoint/`, those projects are not present in this repository snapshot and are not included in `SPNet.slnx`.

## Primary workflow

1. Copy `config/spnet.local.example.yml` to `config/spnet.local.yml` and set the SharePoint Designer WebsiteCache folder, or set `SPNET_SPD_CACHE`.
2. Validate local configuration before building:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action ValidateConfig -Config config\spnet.local.yml
```

3. Edit `samples/workflow.example.yml` or your own `spnet.workflow/v1` YAML file. YAML is the authoring source of truth for workflow structure and publish metadata.
4. Build YAML to SPD-compatible XAML plus the generated publish metadata sidecar `*.xaml.metadata.json`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 build --workflow samples\workflow.example.yml --out artifacts\YamlFirstSmoke.xaml --config config\spnet.local.yml
```

Equivalent retained wrapper/direct serializer paths:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action Build -Workflow samples\workflow.example.yml -XamlPath artifacts\YamlFirstSmoke.xaml -Config config\spnet.local.yml
.\src\SPNet.Workflow.WfSerializer\bin\Release\net48\SPNet.Workflow.WfSerializer.exe build --workflow samples\workflow.example.yml --out artifacts\YamlFirstSmoke.xaml --config config\spnet.local.yml
```

5. Inspect or export generated/downloaded XAML:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 export --xaml artifacts\YamlFirstSmoke.xaml --out artifacts\YamlFirstSmoke.exported.yml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 inspect --xaml artifacts\YamlFirstSmoke.xaml --config config\spnet.local.yml
```

6. Publish through the retained PowerShell boundary. By default publish builds from `-Workflow` to `-XamlPath` first, emits `-XamlPath + '.metadata.json'`, and passes that metadata JSON to the publisher. Pass `-NoBuild` only when publishing an existing XAML file that already has a matching metadata JSON sidecar, or pass `-MetadataJsonPath` explicitly. The current publisher path uses WorkflowServices CSOM, submits XAML as opaque text, and uses metadata JSON as the normal publish contract:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 publish --workflow samples\workflow.example.yml --xaml artifacts\YamlFirstSmoke.xaml --site-url 'https://tenant.sharepoint.com/sites/site' --workflow-name YamlFirstSmoke --target-type Site --dry-run
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 publish --no-build --xaml artifacts\YamlFirstSmoke.xaml --site-url 'https://tenant.sharepoint.com/sites/site' --workflow-name YamlFirstSmoke --target-type Site --dry-run
```

Remove `-DryRun` only when SharePoint authentication/session state and WorkflowServices CSOM dependencies are available. For browser-authenticated SharePoint Online sessions, the wrapper can hand off WebLogin/WinINet cookies to the CSOM publisher; username/password/domain credentials and default Windows credentials remain available for environments that support them.

7. Download published workflows back to XAML plus metadata JSON. Download always writes the XAML and `*.xaml.metadata.json`; it may also preserve `*.xaml.formfield.xml` when SharePoint exposes legacy FormField metadata so older export/inspection paths can still inspect it:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action Download -SiteUrl 'https://tenant.sharepoint.com/sites/site' -WorkflowName YamlFirstSmoke -Out artifacts\YamlFirstSmoke.downloaded.xaml
```

## YAML schema `spnet.workflow/v1`

Supported top-level fields:

- `schemaVersion`: must be `spnet.workflow/v1`.
- `name`: friendly workflow name. This is the legacy alias for canonical `metadata.displayName`.
- `technicalName`: optional WF class name; defaults to `name + .MTW`. This is the legacy alias for canonical `metadata.technicalName`.
- `metadata`: canonical publish metadata source. Supported fields are `displayName`, `technicalName`, `description`, `target`, `start`, and `initiation.formFields`.
- `start`: legacy top-level `manual`, `autoStartCreate`, `autoStartChange` metadata for authoring/publish tooling. Canonical metadata uses `metadata.start.manual`, `metadata.start.onCreated`, and `metadata.start.onUpdated`.
- `target`: legacy top-level `type` and optional `listTitle` metadata for publish tooling. Canonical metadata uses `metadata.target.type` and `metadata.target.listTitle`.
- `parameters`: legacy top-level initiation form parameters. Canonical metadata uses `metadata.initiation.formFields`. Use either map-style YAML keyed by parameter name or list-style items with explicit `name`. Supported field types are `Text`, `Choice`, `Note`, `URL`, `UserMulti` (WF `String`), `Boolean` (WF `Boolean`), `Number` (WF `Double`), and `DateTime` (WF `DateTime`). Metadata properties include `formType`, `displayName`, `description`, `direction`, `default`, `choices`, `format`, `baseType`, `maxLength`, `numLines`, `sortable`, `richTextMode`, `list`, `showField`, `mult`, `userSelectionMode`, and `userSelectionScope`. Parameter names must not duplicate variables, expressions may read parameters by name, and assignment actions may not target parameters.
- `variables`: typed variables currently mapped to WF dynamic activity properties; `Double`/`Number`, `String`, `Boolean`/`Bool`, `Int32`/`Int`/`Integer`, `Guid`, and `DateTime`/`Date` are supported.
- `stages`: one or more stages, each with supported actions.

When YAML is built, SPNet emits public WF `InArgument<T>` declarations for effective initiation fields and writes a deterministic `*.xaml.metadata.json` sidecar. That JSON is generated from effective YAML metadata/defaults after applying the canonical `metadata` block and legacy aliases (`name`, `technicalName`, top-level `start`, top-level `target`, and `parameters`). It is the primary publish contract and contains display name, technical name, description, target, start options, initiation settings, and form fields. The normal round trip is YAML -> XAML + metadata JSON -> SharePoint publish with metadata JSON -> download XAML + metadata JSON. A legacy `*.xaml.formfield.xml` sidecar may still be generated for compatibility/inspection when form fields are present, and downloaded workflows may preserve it, but FormField XML is no longer the normal YAML publish input. Use `-FormFieldXmlPath` only as an explicit deprecated fallback when metadata JSON is unavailable.

Canonical metadata example:

```yaml
schemaVersion: spnet.workflow/v1
metadata:
  displayName: ParameterWorkflow
  technicalName: ParameterWorkflow.MTW
  description: Workflow with initiation fields published from metadata JSON.
  target:
    type: List
    listTitle: TestList
  start:
    manual: true
    onCreated: false
    onUpdated: false
  initiation:
    requiresForm: true
    formFields:
    - name: requestTitle
      type: Text
      displayName: Request title
      default: New request
stages:
- name: Stage 1
  actions:
  - type: writeHistory
    message:
      variable: requestTitle
```

Supported actions:

- `calc`: emits SharePoint `Calc`, with `lValue`, `rValue`, `operator`, and `to`.
- `assign` / `setVariable`: emits WF `Assign<T>` against an existing YAML variable, with `to` and `value`.
- `writeHistory`: emits SharePoint `WriteToHistory`, with `message`.
- `setStatus`: emits SharePoint `SetWorkflowStatus`, with `status`.
- `comment`: emits SharePoint `Comment`, with `text`. This is a Designer annotation activity, not a runtime history-log action.
- `delayFor`: emits SharePoint `DelayFor`, with numeric `days`, `hours`, and `minutes` expressions.
- `delayUntil`: emits SharePoint `DelayUntil`, with a `date` expression. Prefer an explicit ISO-like date literal or a declared `DateTime` variable.
- `while` / `loop`: emits WF `While`, with a structured Boolean `condition` and nested `actions` sequence.
- `if`: emits WF `If`, with a structured Boolean `condition`, nested `then` sequence, and optional nested `else` sequence.
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

These expressions are serialized as structured activity nodes and typed WF arguments. Do not add YAML features that emit raw WF language expression activities (`VisualBasicValue`, `VisualBasicReference`, `CSharpValue`, or `CSharpReference`); they require compilation and are rejected by SharePoint Workflow Manager publishing validation.

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

Control-flow condition expressions:

- logical condition nodes: `and`, `or`, and `not`. `and`/`or` use `leftCondition` and `rightCondition`; `not` uses `operand`.
- numeric comparison types: `isLessThan` / `lessThan`, `equals`, `greaterThan`, `lessThanOrEqual`, and `greaterThanOrEqual`.
- typed comparison types: `isEqual` for Boolean, DateTime, and DynamicValue operands; `isEqualString`, `containsString`, `startsWithString`, `endsWithString`, and DateTime comparisons such as `isGreaterThan`.
- operand expressions: `literal`, `variable`, `parseDate`, and `parseDynamicValue`. DateTime literals are normalized through SharePoint-local-to-UTC handling where the source XAML uses Designer-local date expressions.
- `designerId`: optional condition/expression metadata used by exported YAML to preserve original SharePoint Designer activity `Id` GUIDs when that identity is necessary for Designer to render nested RHS operand tokens.

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

Conditional round-trip example:

```yaml
- type: if
  condition:
    type: and
    leftCondition:
      type: or
      leftCondition:
        type: isEqualString
        valueType: String
        left:
          variable: requestTitle
        right:
          literal: Approved
      rightCondition:
        type: containsString
        valueType: String
        left:
          variable: requestTitle
        right:
          literal: Urgent
    rightCondition:
      type: not
      operand:
        type: isEqual
        valueType: Boolean
        left:
          variable: rejected
        right:
          literal: true
  then:
  - type: setStatus
    status: Condition matched
```

SharePoint Designer conditional compatibility caveats:

- Workflow Manager validation is not enough to prove Designer rendering. Conditions can validate and publish but appear as `(insert a condition)` in SharePoint Designer when the structured condition expression metadata is not the exact Designer-compatible shape.
- Boolean condition nodes and operand conversion expression nodes must retain Designer-compatible `Result="{x:Null}"` state and the expected Designer custom attributes; otherwise Designer may lose operand tokens even though the XAML is valid.
- `ParseDate.CultureName` must be emitted in the property-element shape that calls `GetConfigurationValue`, matching SharePoint Designer-authored XAML. Flattening it to a plain culture attribute can break round-trip Designer display.
- RHS `ArgumentValue` nodes inside conditions need `ArgumentValue.Result` / `OutArgument` wrappers so Designer can render the right-hand operand token instead of showing a blank value.
- Nested RHS conversions such as `parseDate` and `parseDynamicValue` may require preserving the original Designer `Id` GUIDs through YAML as `designerId`; without those IDs, SharePoint Designer can omit the RHS value after a build/publish/download round trip.
- SharePoint expression proxy namespaces should use the SharePoint Designer/publish-compatible non-assembly namespace form in generated XAML.

The reference round-trip for these rules is `artifacts/ExampleConditionals.xaml` exported to `artifacts/ExampleConditionals.roundtrip.yml`, rebuilt with preserved IDs as `artifacts/ExampleConditionalsRoundTripPreserveIds.yml`, and live-validated as `ExampleConditionalsRoundTripPreserveIds4`. That workflow opened in SharePoint Designer with the previously missing RHS values visible. Keep generated/downloaded XAML and site-specific details in ignored `artifacts/`; do not commit credentials, URLs, cookies, or local config.

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

The CSOM publisher supports site workflows and list workflows. List publishing resolves the target list by `-TargetListTitle` or `-TargetListId`, creates a WorkflowServices definition scoped to that list, publishes a subscription for that list, and applies manual/create/update start flags from YAML-generated metadata JSON or explicit parameters. It expects standard `Workflow History` and `Workflow Tasks` lists to exist in the target web.

For YAML-authored workflows, `*.xaml.metadata.json` is the expected metadata input. `Invoke-SPNetYamlWorkflow.ps1 -Action Publish` discovers `-XamlPath + '.metadata.json'` automatically after build or accepts `-MetadataJsonPath` for an explicit sidecar. Direct publishing with `Invoke-SPNetWorkflow.ps1` and the CSOM publisher follows the same contract through `-MetadataJsonPath` / `--metadata-json`. The publisher converts `metadata.initiation.formFields` to SharePoint Definition `FormField` metadata internally during publish; `*.xaml.formfield.xml` is only a deprecated fallback/compatibility input and is not used by the normal YAML publish path.

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
- `scripts/spnet-workflow.ps1`: primary packaged CLI entry point. It delegates YAML build/inspect/export to the serializer wrapper and publish to the publish wrapper.
- `scripts/Invoke-SPNetWorkflow.ps1`: live SharePoint publish/download boundary.
- `scripts/Get-SPNetWorkflowDiagnostics.ps1`: read-only SharePoint workflow diagnostic/export helper.
- `scripts/Invoke-SPNetYamlWorkflow.ps1`: wrapper/combiner for YAML build/export/inspect/publish/download actions.
- `scripts/Test-SPNetYamlWorkflowGolden.ps1`: local golden validation harness for the YAML-first path.

## Validation

The YAML-first baseline has been validated end-to-end in SharePoint Designer with `YamlFirstSmoke`: the workflow opens in Designer, the stage/action structure is visible, and Designer `Check for Errors` reports no errors. HTTP actions, DynamicValue property extraction, current-item list field updates, local list smoke generation, read-only list workflow listing, and CSOM list publishing have also been validated. Conditional workflow round-tripping was validated with `ExampleConditionals` artifacts and the live `ExampleConditionalsRoundTripPreserveIds4` workflow: nested `and`/`or`/`not`, string/Boolean/DateTime/DynamicValue comparisons, `parseDate`, `parseDynamicValue`, SharePoint-local-to-UTC date normalization, and Designer `Id` preservation opened in SharePoint Designer with RHS operand values visible. List lifecycle create/update has been runtime-validated with `SPNetYamlListLifecycleManual-20260509-1535` on `TestList`: Designer showed misleading red boxes and incomplete variable-picker/action-builder rendering for the created item ID, but `Check for Errors` passed and runtime execution created an item and updated that new item's title via the returned ID. Generated artifacts are intentionally ignored by Git; keep only `artifacts/.gitkeep` committed in the workspace.

Publishing validation has also confirmed that raw VB/C# WF language expression activities are not acceptable output: a probe containing raw `Microsoft.CSharp.Activities.CSharpValue<TResult>` failed Workflow Manager validation with an invalid-type error, and earlier raw `VisualBasicValue<T>` string-action builders were rejected until replaced with `Microsoft.Activities.Expressions` structured proxy expression nodes. Keep this as a hard guardrail for generated XAML.

```powershell
dotnet build .\SPNet.slnx
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action ValidateConfig -Config config\spnet.local.yml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action Build -Workflow samples\workflow.example.yml -XamlPath artifacts\YamlFirstSmoke.xaml -Config config\spnet.local.yml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action Export -XamlPath artifacts\YamlFirstSmoke.xaml -Out artifacts\YamlFirstSmoke.exported.yml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-SPNetYamlWorkflowGolden.ps1 -Workflow samples\workflow.example.yml -XamlPath artifacts\golden\YamlFirstSmoke.xaml -Config config\spnet.local.yml
```

Downloaded workflow XAML can be captured from SharePoint and will be accompanied by metadata JSON:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action Download -SiteUrl 'https://tenant.sharepoint.com/sites/site' -WorkflowName YamlFirstSmoke -Out artifacts\YamlFirstSmoke.downloaded.xaml
```

The downloaded metadata JSON is written beside the XAML as `artifacts\YamlFirstSmoke.downloaded.xaml.metadata.json`. If SharePoint exposes legacy FormField XML, the download path may also write `artifacts\YamlFirstSmoke.downloaded.xaml.formfield.xml` for compatibility and inspection.

Live publish validation requires SharePoint auth/session support and pinned SharePoint Online CSOM dependencies used by the publisher project. Do not publish or clean workflows as part of local validation unless intentionally performing a live SharePoint smoke test. Manual validation guidance: publish a uniquely named test workflow using YAML -> XAML + metadata JSON, open it in SharePoint Designer, run `Check for Errors`, validate the target list subscription/start behavior and initiation fields, download the workflow to confirm XAML + metadata JSON round trip, then list and clean only the uniquely named test artifacts.

## Known limitations

- YAML support is intentionally limited to logs/status/comments, assignment, structured conditional control flow, current item `setField`, HTTP calls, bounded `sendEmail`, bounded `singleTask`, REST property-name lookup, workflow context/current list/current item expressions, and DynamicValue string property extraction.
- `DynamicValue` variables are generated only for HTTP response targets; arbitrary YAML-declared `DynamicValue` variables and general dictionary mutation actions are deferred.
- Top-level lookup actions are rejected because SharePoint Designer can render them as blank/crashing actions; use nested lookup expressions inside assignment or action arguments. This includes list item property lookups such as `lookupListItemStringProperty`.
- The CSOM publisher does not overwrite, delete, or migrate existing live workflows; `if-exists` currently fails on name conflicts.
- Site/list publishing is validated for current SharePoint WorkflowServices scenarios, but Email/task workflows should remain local/golden validated only unless intentionally reviewed for safe recipients, assignees, and side effects. Broader task/process/list-item CRUD actions remain deferred until safe XAML shapes are captured and validated.
- The support surface is classified in [docs/action-support-matrix.md](docs/action-support-matrix.md). Stable actions are the production baseline; preview actions require target-site validation; experimental/dev-only actions are not production defaults.
- Large workflows, broad loops, many variables/properties, large HTTP/DynamicValue payloads, and repeated whole-body string manipulation can hit Workflow Manager validation, persistence, rendering, or runtime limits even when local YAML-to-XAML build succeeds.
- SharePoint Designer rendering and Workflow Manager validation are separate compatibility bars. Some generated shapes can publish but show misleading red boxes, blank operands, or hidden activities in Designer.
- Local WebsiteCache/proxy assemblies can differ from the target publish/runtime environment. Treat local build/export/inspect as necessary but not sufficient for live use.
