# Deep DynamicValue build/write/read validation

This note documents the validated pattern for authoring nested `DynamicValue` payloads in SPNet YAML, serializing them to SharePoint-compatible XAML, and reading deeply nested values back with either step-by-step lookups or a single slash-path lookup.

The reference sample is [`samples/workflow.experimental-deep-dynamic-values.yml`](../samples/workflow.experimental-deep-dynamic-values.yml). The regression coverage is in [`tests/SPNet.Workflow.WfSerializer.Tests/YamlWorkflowModelTests.cs`](../tests/SPNet.Workflow.WfSerializer.Tests/YamlWorkflowModelTests.cs).

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

A single slash-path read can address the same deep property from the outer object:

```yaml
- type: getDynamicValueProperty
  source: updatedLayer1
  propertyName: Layer2/Layer3/Title
  to: readBackSlashPathTitle
  valueType: String
```

Validation proved that the YAML model, generated XAML, and SharePoint publish validation accept the slash path. Generated XAML is expected to contain a `GetDynamicValueProperty` node with `PropertyName="Layer2/Layer3/Title"`, and the source argument should remain the outer `p:DynamicValue` variable.

## Validation commands

Use the focused regression tests for this sample:

```powershell
dotnet test .\tests\SPNet.Workflow.WfSerializer.Tests\SPNet.Workflow.WfSerializer.Tests.csproj --filter "DeepDynamicValues"
```

Build the sample and inspect the generated XAML locally:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 build --workflow .\samples\workflow.experimental-deep-dynamic-values.yml --out .\artifacts\ExperimentalDeepDynamicValues.xaml --config .\config\spnet.local.yml
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 inspect --xaml .\artifacts\ExperimentalDeepDynamicValues.xaml --config .\config\spnet.local.yml
```

Publish validation should be performed only against an appropriate non-production SharePoint site, with placeholder values replaced locally:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 auth-test --site-url 'https://tenant.sharepoint.com/sites/site' --auth-mode WebLogin
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 publish --workflow .\samples\workflow.experimental-deep-dynamic-values.yml --xaml .\artifacts\ExperimentalDeepDynamicValues.xaml --site-url 'https://tenant.sharepoint.com/sites/site' --workflow-name ExperimentalDeepDynamicValues --target-type Site --dry-run
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 publish --workflow .\samples\workflow.experimental-deep-dynamic-values.yml --xaml .\artifacts\ExperimentalDeepDynamicValues.xaml --site-url 'https://tenant.sharepoint.com/sites/site' --workflow-name ExperimentalDeepDynamicValues --target-type Site
```

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
