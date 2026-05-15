# Deep DynamicValue build/write/read validation

This note documents the validated pattern for authoring nested `DynamicValue` payloads in SPNet YAML, serializing them to SharePoint-compatible XAML, and reading/writing deeply nested values back with either step-by-step operations or a single slash-path operation.

The small reference sample is [`samples/workflow.experimental-deep-dynamic-values.yml`](../samples/workflow.experimental-deep-dynamic-values.yml). The complex `deepworkflowexample` sample is [`samples/workflow.deepworkflowexample-dynamic-values.yml`](../samples/workflow.deepworkflowexample-dynamic-values.yml). The regression coverage is in [`tests/SPNet.Workflow.WfSerializer.Tests/YamlWorkflowModelTests.cs`](../tests/SPNet.Workflow.WfSerializer.Tests/YamlWorkflowModelTests.cs).

## Recommended authoring pattern

Author deep `DynamicValue` payloads from the inside out:

1. Build the innermost dictionary with `buildDynamicValue`.
2. Put that dictionary into its parent dictionary with `valueType: DynamicValue`.
3. Repeat until the outer dictionary is complete.
4. When changing a nested object, write the changed inner value first, then write each updated child back into its parent.

The important rule is that nested dynamic values must be declared as `DynamicValue`, not strings. For example:

```yaml
- type: buildDynamicValue
  to: layer3
  entries:
    - key: Title
      value: Before deep SetDynamicValueProperty
    - key: Enabled
      value: true
      valueType: Boolean
- type: buildDynamicValue
  to: layer2
  entries:
    - key: Layer3
      value:
        variable: layer3
      valueType: DynamicValue
- type: buildDynamicValue
  to: layer1
  entries:
    - key: Layer2
      value:
        variable: layer2
      valueType: DynamicValue
```

To update the deepest value while preserving the nested structure:

```yaml
- type: setDynamicValueProperty
  source: layer3
  propertyName: Title
  value: Set three layers deep
  valueType: String
  to: updatedLayer3
- type: setDynamicValueProperty
  source: layer2
  propertyName: Layer3
  value:
    variable: updatedLayer3
  valueType: DynamicValue
  to: updatedLayer2
- type: setDynamicValueProperty
  source: layer1
  propertyName: Layer2
  value:
    variable: updatedLayer2
  valueType: DynamicValue
  to: updatedLayer1
```

The serializer must preserve `valueType: DynamicValue` for nested writes. Support for this shape is implemented in [`src/SPNet.Workflow.WfSerializer/Actions/HttpEmailTaskActionBuilders.cs`](../src/SPNet.Workflow.WfSerializer/Actions/HttpEmailTaskActionBuilders.cs) so nested values serialize as `p:DynamicValue` arguments instead of `x:String` values.

## Complex `deepworkflowexample` workflow design

[`samples/workflow.deepworkflowexample-dynamic-values.yml`](../samples/workflow.deepworkflowexample-dynamic-values.yml) is a safe site workflow sample whose `metadata.displayName` is `deepworkflowexample`, matching the published workflow name supplied for validation. It writes workflow history only; it does not mutate SharePoint list data, send email, create tasks, or call external HTTP endpoints.

The sample builds this nested payload shape:

```text
rootValue
  Branch: DynamicValue
    Leaf: DynamicValue
      Title: String
      Count: Int32
      Amount: Double
      Enabled: Boolean
      DueDate: DateTime
      Url: String URL text
      Lookup: DynamicValue
        LookupId: Int32
        LookupTitle: String
        LookupUrl: String URL text
    Items: DynamicValue parsed from JSON array text
      0: DynamicValue/object-like item
        Title: String
        Status: String
      1: DynamicValue/object-like item
        Title: String
        Status: String
  Audit: DynamicValue
    User: String
    WhenUtc: DateTime
```

It then demonstrates all of these dynamic operations:

- `buildDynamicValue` with explicit `valueType` casts for `String`, `Int32`, `Double`, `Boolean`, `DateTime`, and nested `DynamicValue` entries.
- `setDynamicValueProperty` against a deep leaf dictionary for string, integer, number, boolean, date/time, and URL-as-string values.
- `setDynamicValueProperty` that writes modified child dictionaries back into their parent dictionaries using `valueType: DynamicValue`.
- Slash-path `setDynamicValueProperty` writes from the root object for nested string, integer, and boolean values: `Branch/Leaf/Title`, `Branch/Leaf/Count`, and `Branch/Leaf/Enabled`.
- Slash-path array-index writes through a parsed JSON array-like `DynamicValue`, for example `Branch/Items(1)/Status`.
- `getDynamicValueProperty` step-by-step reads for nested dictionaries and typed scalar values.
- Slash-path reads from the root object with `propertyName: Branch/Leaf/Title`, `Branch/Leaf/Count`, and `Branch/Leaf/Enabled`.
- Slash-path array-index reads from the root object with `propertyName: Branch/Items(0)/Title` and `Branch/Items(1)/Status`.
- Inline slash-path `getDynamicValueProperty` usage inside the final `writeHistory` `concatString` expression, inside a conditional predicate, inside another `setDynamicValueProperty` value assignment, and inside an intermediate history-summary assignment, so retrieved values can be consumed directly without first assigning each one to a workflow variable.
- Dictionary-like lookup/user payloads represented as nested `DynamicValue` dictionaries because the current serializer has explicit `DynamicValue` support but no dedicated SharePoint lookup/person object value type for this action family.
- Dynamic predicates through `containsDynamicValueProperty` and `isEmptyDynamicValue` assignment expressions.
- `countDynamicValueItems` against the materialized leaf dictionary.

### Value types and conversion patterns

Supported `buildDynamicValue`, `setDynamicValueProperty`, and `getDynamicValueProperty` `valueType` values include:

| YAML `valueType` | Runtime argument/result shape | Pattern in the complex sample |
| --- | --- | --- |
| `String` | `String` value; `buildDynamicValue` casts to object when needed | `Title`, `Url`, `LookupTitle`, `LookupUrl`, `Audit/User` |
| `Int32` / `Int` / `Integer` | `Int32` value; `buildDynamicValue` casts to object | `Count`, `LookupId` |
| `Double` / `Number` | `Double` value; `buildDynamicValue` casts to object | `Amount` |
| `Boolean` / `Bool` | `Boolean` value; `buildDynamicValue` casts to object | `Enabled` |
| `DateTime` / `Date` | `DateTime`; use a nested `parseDate` expression for string literals | `DueDate`, `Audit/WhenUtc` |
| `Guid` | `Guid`; supported by serializer mapping but not required in this sample | Use `valueType: Guid` with a GUID literal or `parseGuid` expression if needed |
| `DynamicValue` / `Dictionary` | Microsoft `DynamicValue` argument/reference | `Lookup`, `Leaf`, `Branch`, `Audit` |

URL values are represented as `String` because the dynamic property serializer maps URL-like values to supported scalar types rather than a dedicated URL object. Lookup-like or dictionary-like values should be represented as nested `DynamicValue` dictionaries with explicit typed fields such as `LookupId`, `LookupTitle`, and `LookupUrl`.

Date/time string conversion should be explicit:

```yaml
- key: DueDate
  value:
    type: parseDate
    value: '2026-05-20T09:30:00Z'
  valueType: DateTime
```

Do not write nested dictionaries as strings. This is the critical cast pattern for deep updates:

```yaml
- type: setDynamicValueProperty
  source: branchValue
  propertyName: Leaf
  value:
    variable: updatedLeafValue
  valueType: DynamicValue
  to: updatedBranchValue
```

When the target runtime supports slash-path property names, a nested scalar can also be updated directly from the outer object. The complex sample keeps these operations history-only and demonstrates string, number, and boolean slash-path writes:

```yaml
- type: setDynamicValueProperty
  source: updatedRootWithAudit
  propertyName: Branch/Leaf/Title
  value: Updated through slash path
  valueType: String
  to: updatedRootWithSlashString
- type: setDynamicValueProperty
  source: updatedRootWithSlashString
  propertyName: Branch/Leaf/Count
  value: 11
  valueType: Int32
  to: updatedRootWithSlashNumber
- type: setDynamicValueProperty
  source: updatedRootWithSlashNumber
  propertyName: Branch/Leaf/Enabled
  value: true
  valueType: Boolean
  to: updatedRootWithSlashBoolean
```

The write chain uses a new target variable for each update so the sample clearly shows the mutated outer `DynamicValue` flowing into the next slash-path operation.

### Array-like DynamicValue paths

The serializer does not introduce a separate YAML array action for `DynamicValue`. To keep the example aligned with the runtime actions that already exist, array-like content is authored by parsing JSON text into a `DynamicValue` and then addressing zero-based indexes with parenthesized path syntax in the same slash-path string used for dictionary keys:

```yaml
- type: assign
  to: itemsValue
  value:
    type: parseDynamicValue
    value: '[{"Title":"First array item","Status":"Ready"},{"Title":"Second array item","Status":"Pending"}]'
- type: setDynamicValueProperty
  source: updatedRootWithSlashBoolean
  propertyName: Branch/Items
  value:
    variable: itemsValue
  valueType: DynamicValue
  to: updatedRootWithItems
```

After the parsed array is attached to the parent `DynamicValue`, the exact supported path syntax is slash-separated with zero-based parenthesized array indexes:

```yaml
- type: getDynamicValueProperty
  source: updatedRootWithCopiedArrayValue
  propertyName: Branch/Items(0)/Title
  to: readFirstItemTitle
  valueType: String
- type: setDynamicValueProperty
  source: updatedRootWithItems
  propertyName: Branch/Items(1)/Status
  value: Reviewed through parenthesized array index
  valueType: String
  to: updatedRootWithArraySlashWrite
```

Limitations and exact syntax:

- Parenthesized indexes such as `(0)` and `(1)` are passed through to Microsoft `GetDynamicValueProperty`/`SetDynamicValueProperty` unchanged. They are not parsed or bounds-checked by the YAML serializer.
- Array indexing is zero-based when the underlying Microsoft `DynamicValue` runtime interprets the slash path.
- Use parenthesized array index syntax, not a literal numeric slash segment. Correct examples are `Branch/Items(0)/Title`, `(0)`, `(0)/Title`, and nested mixed syntax such as `Branch/Items(0)/Tags/(1)`. Avoid the incorrect numeric-segment style `Branch/Items/0/Title` and avoid bracket syntax (`Branch/Items[0]/Title`), because bracket indexing is not implemented by the YAML serializer.
- Array-like payloads should come from `parseDynamicValue` JSON or from an existing SharePoint/HTTP `DynamicValue` response. `buildDynamicValue` remains dictionary/key oriented.
- Deep array-index writes are feasible when the local Microsoft Activities runtime supports slash-path mutation for the supplied path. The YAML serializer serializes the path literally and does not create intermediate array elements.

## Reading deep values

Both read styles are valid.

Step-by-step reads materialize each intermediate object:

```yaml
- type: getDynamicValueProperty
  source: updatedLayer1
  propertyName: Layer2
  to: readLayer2
  valueType: DynamicValue
- type: getDynamicValueProperty
  source: readLayer2
  propertyName: Layer3
  to: readLayer3
  valueType: DynamicValue
- type: getDynamicValueProperty
  source: readLayer3
  propertyName: Title
  to: readBackDeepTitle
  valueType: String
```

A single slash-path read can address the same deep property from the outer object. The complex sample includes string, integer, and boolean reads:

```yaml
- type: getDynamicValueProperty
  source: updatedRootWithSlashBoolean
  propertyName: Branch/Leaf/Title
  to: readSlashTitle
  valueType: String
- type: getDynamicValueProperty
  source: updatedRootWithSlashBoolean
  propertyName: Branch/Leaf/Count
  to: readSlashCount
  valueType: Int32
- type: getDynamicValueProperty
  source: updatedRootWithSlashBoolean
  propertyName: Branch/Leaf/Enabled
  to: readSlashEnabled
  valueType: Boolean
```

Validation proved that the YAML model, generated XAML, and SharePoint publish validation accept the slash path. Generated XAML is expected to contain a `GetDynamicValueProperty` node with `PropertyName="Layer2/Layer3/Title"`, and the source argument should remain the outer `p:DynamicValue` variable.

### Inline slash-path reads

For scalar values, `getDynamicValueProperty` can also be used inline as a nested expression wherever the surrounding action accepts a typed expression. This is useful when the value only needs to feed another action and does not need its own workflow variable. The complex sample uses the pattern inside the final history message:

```yaml
- type: writeHistory
  message:
    type: concatString
    values:
      - 'deepworkflowexample validated: '
      -
        type: getDynamicValueProperty
        source:
          variable: updatedRootWithSlashBoolean
        propertyName: Branch/Leaf/Title
        valueType: String
      - ' count='
      -
        type: toString
        value:
          variable: readSlashCount
```

The inline expression reads `Branch/Leaf/Title` directly from `updatedRootWithSlashBoolean` as a `String`. Because it is nested inside `concatString`, it is consumed immediately by the history output instead of being assigned to an intermediate variable first.

Inline `getDynamicValueProperty` can also be used in these additional contexts demonstrated by the complex sample:

1. As the `value` of another `setDynamicValueProperty`, copying an array-index read into a different deep property:

```yaml
- type: setDynamicValueProperty
  source: updatedRootWithArraySlashWrite
  propertyName: Branch/Leaf/ArrayFirstTitleCopy
  value:
    type: getDynamicValueProperty
    source:
      variable: updatedRootWithArraySlashWrite
    propertyName: Branch/Items(0)/Title
    valueType: String
  valueType: String
  to: updatedRootWithCopiedArrayValue
```

2. As a conditional predicate operand:

```yaml
- type: if
  condition:
    type: equals
    valueType: String
    left:
      type: getDynamicValueProperty
      source:
        variable: updatedRootWithCopiedArrayValue
      propertyName: Branch/Items(0)/Status
      valueType: String
    right: Ready
  then:
    - type: writeHistory
      message: First item was ready
```

3. Inside an intermediate `assign` expression that is later used by a history-only branch:

```yaml
- type: assign
  to: firstItemHistorySummary
  value:
    type: concatString
    values:
      - 'First item via inline array path: '
      -
        type: getDynamicValueProperty
        source:
          variable: itemsValue
        propertyName: (0)/Title
        valueType: String
```

When inline `getDynamicValueProperty` feeds a typed location, keep `valueType` aligned with the target argument type. For example, use `valueType: String` for string comparisons, history concatenation, and string-valued `setDynamicValueProperty` assignments.

## Function-like stage call dictionaries

DynamicValue dictionaries can also be used with explicit stage transitions to model function-like stage calls. The validated sample is [`samples/workflow.stage-functions.yml`](../samples/workflow.stage-functions.yml), which publishes as `SPNet Stage Function Concepts Test` in non-production validation.

The pattern uses shared dictionaries instead of per-function arguments:

- `functionparams` (`DynamicValue`) contains the active function call's input parameters.
- `funcitonreturnvalue` (`DynamicValue`) contains the active function call's return payload. The sample intentionally preserves this spelling from the original concept.
- `returnStage` (`String`) identifies which static continuation branch a function stage should take after setting its return dictionary.

Caller stages build `functionparams`, set `returnStage`, and transition to a function stage by stable `stage.id`. Function stages validate keys with `containsDynamicValueProperty`, read them with `getDynamicValueProperty`, build `funcitonreturnvalue`, and transition back to one of the allowed continuation stages. See [`docs/stage-transitions.md`](stage-transitions.md) for the stage-flow side of this pattern.

Important authoring rules from validation:

- Validate keys before reads. A missing `DynamicValue` property can fail at runtime if read directly.
- Use typed `valueType` fields consistently. `calc` emits `Double` arguments, so use `Double` variables for numeric calculations and convert to `String` with `toString` when returning formatted scalar values.
- Avoid dictionary keys named `Result` in function return payloads. SharePoint Workflow Manager validation can report duplicate environment names because Microsoft dynamic-value activities also expose runtime arguments named `Result`. Use keys such as `ReturnValue`, `Ok`, `Error`, and `Function` instead.
- This is static dispatch, not a true call stack. Only one active `functionparams` / `funcitonreturnvalue` pair is available unless the workflow explicitly saves and restores additional dictionaries.
- Keep stage IDs unique and branch back to explicit continuation stages; dynamic stage names are not resolved at runtime.

Local and publish validation should include build, object-model inspect, publish, download, and cache-enabled export:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action Build -Workflow .\samples\workflow.stage-functions.yml -XamlPath .\artifacts\workflow.stage-functions.xaml -Config .\config\spnet.local.yml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action Inspect -XamlPath .\artifacts\workflow.stage-functions.xaml -Config .\config\spnet.local.yml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action Publish -Workflow .\samples\workflow.stage-functions.yml -XamlPath .\artifacts\workflow.stage-functions.xaml -WorkflowName "SPNet Stage Function Concepts Test" -TargetType Site -IfExists Fail -AuthMode WebLogin
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-SPNetYamlWorkflow.ps1 -Action Download -WorkflowName "SPNet Stage Function Concepts Test" -Out .\artifacts\SPNetStageFunctionConceptsTest.downloaded.xaml -Config .\config\spnet.local.yml
dotnet run --project .\src\SPNet.Workflow.WfSerializer\SPNet.Workflow.WfSerializer.csproj -- export --xaml .\artifacts\SPNetStageFunctionConceptsTest.downloaded.xaml --out .\artifacts\SPNetStageFunctionConceptsTest.downloaded.objectmodel.exported.yml --config .\config\spnet.local.yml
```

## Validation commands

Use the focused regression tests for this sample:

```powershell
dotnet test .\tests\SPNet.Workflow.WfSerializer.Tests\SPNet.Workflow.WfSerializer.Tests.csproj --filter "DeepDynamicValues"
```

Use the focused regression tests for the complex `deepworkflowexample` sample:

```powershell
dotnet test .\tests\SPNet.Workflow.WfSerializer.Tests\SPNet.Workflow.WfSerializer.Tests.csproj --filter "DeepWorkflowExampleDynamicValues|DeepDynamicValues"
```

Run the full serializer test suite:

```powershell
dotnet test .\tests\SPNet.Workflow.WfSerializer.Tests\SPNet.Workflow.WfSerializer.Tests.csproj
```

Run all tests in the solution:

```powershell
dotnet test .\SPNet.slnx
```

Build the sample and inspect the generated XAML locally:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 build --workflow .\samples\workflow.experimental-deep-dynamic-values.yml --out .\artifacts\ExperimentalDeepDynamicValues.xaml --config .\config\spnet.local.yml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 inspect --xaml .\artifacts\ExperimentalDeepDynamicValues.xaml --config .\config\spnet.local.yml
```

Build and inspect the complex `deepworkflowexample` sample locally after configuring a SharePoint Designer WebsiteCache path in [`config/spnet.local.yml`](../config/spnet.local.example.yml), passing `--cache-folder`, or setting `SPNET_SPD_CACHE`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 build --workflow .\samples\workflow.deepworkflowexample-dynamic-values.yml --out .\artifacts\deepworkflowexample.dynamic-values.xaml --config .\config\spnet.local.yml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 inspect --xaml .\artifacts\deepworkflowexample.dynamic-values.xaml --config .\config\spnet.local.yml
```

Local build cannot run without the SharePoint Designer proxy assemblies from WebsiteCache. If the cache is not configured, the expected failure is:

```text
Missing SharePoint Designer WebsiteCache folder. Set --cache-folder, config spdCacheFolder, or SPNET_SPD_CACHE.
```

Use the non-mutating doctor command to verify local package/script/tool readiness:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 doctor --json
```

Publish validation should be performed only against an appropriate non-production SharePoint site, with placeholder values replaced locally:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 auth-test --site-url 'https://tenant.sharepoint.com/sites/site' --auth-mode WebLogin
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 publish --workflow .\samples\workflow.experimental-deep-dynamic-values.yml --xaml .\artifacts\ExperimentalDeepDynamicValues.xaml --site-url 'https://tenant.sharepoint.com/sites/site' --workflow-name ExperimentalDeepDynamicValues --target-type Site --dry-run
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 publish --workflow .\samples\workflow.experimental-deep-dynamic-values.yml --xaml .\artifacts\ExperimentalDeepDynamicValues.xaml --site-url 'https://tenant.sharepoint.com/sites/site' --workflow-name ExperimentalDeepDynamicValues --target-type Site
```

## Published `deepworkflowexample` validation process

The repository has read-only diagnostics that can verify whether the published definition/subscription exists and can download its XAML/metadata, but there is no current safe CLI action that manually starts a site workflow instance and waits for its workflow history. Therefore live validation should be split into non-mutating definition checks, optional guarded publish/update checks, and manual runtime invocation in SharePoint.

### Non-mutating published workflow checks

Run these commands from the repository root after replacing the site URL locally. Do not commit tenant URLs or authentication material.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 auth-test --site-url 'https://tenant.sharepoint.com/sites/site' --auth-mode WebLogin
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Get-SPNetWorkflowDiagnostics.ps1 -SiteUrl 'https://tenant.sharepoint.com/sites/site' -WorkflowName 'deepworkflowexample' -OutputDirectory .\artifacts\sharepoint-workflow-diagnostics\deepworkflowexample
```

Inspect the generated diagnostics JSON for `MatchedDefinitionCount`, definition `DisplayName`, `Published`, target/scope metadata, subscriptions, start event types, and downloaded XAML shape. This verifies the published workflow metadata, not runtime execution.

### Build/publish validation for the complex sample

After local build succeeds with WebsiteCache configured, perform a dry-run publish first:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 publish --workflow .\samples\workflow.deepworkflowexample-dynamic-values.yml --xaml .\artifacts\deepworkflowexample.dynamic-values.xaml --site-url 'https://tenant.sharepoint.com/sites/site' --workflow-name deepworkflowexample --target-type Site --dry-run
```

For a real publish/update, first obtain the existing definition ID from diagnostics. Then use guarded update semantics so the command refuses to replace an unexpected workflow:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 publish --workflow .\samples\workflow.deepworkflowexample-dynamic-values.yml --xaml .\artifacts\deepworkflowexample.dynamic-values.xaml --site-url 'https://tenant.sharepoint.com/sites/site' --workflow-name deepworkflowexample --target-type Site --if-exists Update --expected-definition-id '<existing-definition-id>' --backup-directory .\artifacts\workflow-backups
```

### Manual runtime validation

Because the current scripts do not expose a safe site-workflow start operation, runtime validation must be performed in SharePoint/Workflow Manager UI or by an operator-approved external script. Start the `deepworkflowexample` site workflow manually, then confirm workflow history contains the final message beginning with:

```text
deepworkflowexample validated:
```

If the workflow was published from [`samples/workflow.deepworkflowexample-dynamic-values.yml`](../samples/workflow.deepworkflowexample-dynamic-values.yml), that history message confirms the workflow reached the final action after the typed deep writes, slash-path string/number/boolean writes, array-index slash-path read/write operations, inline `getDynamicValueProperty` inside set values and conditional predicates, step-by-step reads, slash-path string/number/boolean reads, inline slash-path history read, dynamic predicates, and item count operation.

Do not put private cookie headers, passwords, tokens, or tenant-specific secrets in committed commands, docs, or artifacts.

## Validation outcome and caveats

Validated outcome:

- The YAML model loads three nested `DynamicValue` layers and preserves the expected read/write action sequence.
- The generated XAML contains `p:GetDynamicValueProperty PropertyName="Layer2/Layer3/Title"`.
- Nested `setDynamicValueProperty` writes serialize nested values as `p:DynamicValue`, not `x:String`.
- SharePoint accepted and published the site workflow with display name `Experimental Deep DynamicValues`.

Caveats:

- Runtime manual invocation was not performed because the current tooling does not provide a safe site-workflow manual-start action.
- This remains an experimental `DynamicValue` shape. Validate in the target Workflow Manager environment before production use.
- Publish metadata can override the CLI workflow name. In this sample, `metadata.displayName` is `Experimental Deep DynamicValues`; publishing with a different `--workflow-name` may still create/update using the metadata display name. If publish is run with fail semantics instead of update semantics, this can leave multiple definitions with the same display name. Keep YAML metadata and CLI names aligned when validating repeated publishes.
