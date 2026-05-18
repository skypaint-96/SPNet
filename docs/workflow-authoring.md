# Workflow authoring guide

Related docs: [documentation index](index.md), [publishing and operations guide](publishing-and-operations.md), [action support matrix](action-support-matrix.md), [stage transitions](stage-transitions.md), and [deep DynamicValue validation](deep-dynamic-values.md).

This guide explains the practical `spnet.workflow/v1` YAML surface. Use it with the [action support matrix](action-support-matrix.md), which remains the detailed source of truth for action support classification, aliases, builder status, export status, samples, tests, validation, risk, and caveats.

## Authoring principles

- YAML is the workflow source of truth. Build output is generated XAML plus a generated `*.xaml.metadata.json` sidecar.
- The metadata JSON sidecar is the normal publish contract for workflow display name, technical name, description, target, start options, initiation settings, and form fields.
- Legacy `*.xaml.formfield.xml` may be produced or consumed for compatibility/inspection, but it is a deprecated fallback rather than the normal YAML publish input.
- Generated XAML must not emit raw WF language expression activities such as `VisualBasicValue`, `VisualBasicReference`, `CSharpValue`, or `CSharpReference`. Builders should emit structured SharePoint proxy activities or `Microsoft.Activities.Expressions` proxy expression activities.
- SharePoint Designer rendering and Workflow Manager validation are separate compatibility bars. A workflow can publish while still showing misleading red boxes, blank operands, or hidden activities in Designer.

## Top-level schema

Supported top-level fields are:

| Field | Purpose |
| --- | --- |
| `schemaVersion` | Must be `spnet.workflow/v1`. |
| `name` | Friendly workflow name. Legacy alias for `metadata.displayName`. |
| `technicalName` | Optional WF class name. Legacy alias for `metadata.technicalName`; defaults to `name + .MTW` when omitted. |
| `metadata` | Canonical publish metadata source. Supports `displayName`, `technicalName`, `description`, `target`, `start`, and `initiation.formFields`. |
| `start` | Legacy top-level start metadata with `manual`, `autoStartCreate`, and `autoStartChange`. Canonical metadata uses `metadata.start.manual`, `metadata.start.onCreated`, and `metadata.start.onUpdated`. |
| `target` | Legacy top-level publish target metadata with `type` and optional `listTitle`. Canonical metadata uses `metadata.target.type` and `metadata.target.listTitle`. |
| `parameters` | Legacy top-level initiation parameters. Canonical metadata uses `metadata.initiation.formFields`. |
| `variables` | Typed workflow variables and inferred variable hints. |
| `stages` | One or more stages containing supported actions and optional explicit stage transitions. |

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

The legacy `name`, `technicalName`, top-level `start`, top-level `target`, and `parameters` fields remain supported aliases. Prefer canonical `metadata` in new workflows when authoring publish metadata.

## Parameters and variables

Initiation form fields can be written as map-style YAML keyed by parameter name or list-style items with explicit `name`. Supported field types include `Text`, `Choice`, `Note`, `URL`, `UserMulti`, `Boolean`, `Number`, and `DateTime`. Supported metadata properties include `formType`, `displayName`, `description`, `direction`, `default`, `choices`, `format`, `baseType`, `maxLength`, `numLines`, `sortable`, `richTextMode`, `list`, `showField`, `mult`, `userSelectionMode`, and `userSelectionScope`.

Parameter names must not duplicate workflow variables. Expressions may read parameters by name, but assignment actions may not target parameters.

Workflow variables currently support `Double`/`Number`, `String`, `Boolean`/`Bool`, `Int32`/`Int`/`Integer`, `Guid`, `DateTime`/`Date`, `TimeSpan`, and `DynamicValue` where actions require it. Some action outputs infer variables when omitted from the `variables` list, but explicit declarations are clearer for production workflows.

Parameter sample: [`samples/workflow.parameters.yml`](../samples/workflow.parameters.yml).

## Stages and stage transitions

Every workflow has one or more stages. Without explicit transitions, stages run linearly in YAML order. With transitions, each stage can declare a stable `id` and a `transition` block.

Supported transition fields are:

- `stage.id`: optional stable transition target. Prefer unique IDs over names for authored non-linear workflows.
- `stage.transition.branches`: ordered conditional branches. Each branch has a structured Boolean `condition` and a `goto` target.
- `stage.transition.default.goto`: fallback target when no branch matches.
- `stage.transition.goto`: shorthand for a default-only transition; do not combine it with `transition.default.goto`.
- `goto: end`: reserved terminal target that serializes to the SharePoint Designer end sentinel `4294967294`.

Explicit stage transitions build WF `System.Activities.Statements.Flowchart` graphs with `FlowStep` stage nodes and chained `FlowDecision` branch nodes. See [stage transitions and round-trip guidance](stage-transitions.md) for examples, export requirements, and function-like stage-call guidance.

Stage samples: [`samples/workflow.stage-transitions.yml`](../samples/workflow.stage-transitions.yml), [`samples/workflow.stage-flow-concepts.yml`](../samples/workflow.stage-flow-concepts.yml), and [`samples/workflow.stage-functions.yml`](../samples/workflow.stage-functions.yml).

## Action support overview

Use stable actions as the production baseline. Preview, experimental, and dev-only actions require additional review and target-environment validation.

| Support | Typical actions |
| --- | --- |
| Stable | `calc`, `assign`/`setVariable`, `replaceString`, `substring`, `trimString`, `writeHistory`, `setStatus`, `comment`, nested `lookupWorkflowContext`, nested `getCurrentListId`, nested `getCurrentItemGuid` |
| Preview | `delayFor`, `delayUntil`, `while`, `if`, `setField`, `createListItem`, `updateListItem`, `deleteListItem`, nested `lookupListItemStringProperty`, `callHttpWebService`, `sendEmail`, `singleTask`, `lookupRestPropertyName` |
| Experimental | `getDynamicValueProperty`, `setDynamicValueProperty`, `buildDynamicValue` |
| Dev-only | Hidden Microsoft activity experiments such as `buildUri`, `getConfigurationValue`, `getInstanceAddress`, TimeSpan/date helpers, and nested string/DynamicValue expression helpers |
| Unsupported | Shapes explicitly rejected by the YAML model or documented as unavailable in the current proxy environment |

The full [action support matrix](action-support-matrix.md) contains the authoritative caveats. In particular:

- Top-level lookup actions are rejected because SharePoint Designer can render them as blank or crashing actions. Use lookup expressions nested inside visible actions.
- List mutations, email, task actions, timers, HTTP calls, and DynamicValue operations require target-site validation because they can have side effects or runtime/environment dependencies.
- Delete operations are destructive and should only be used against known safe test data unless separately approved.
- Hidden Microsoft activity shapes can execute while remaining invisible or misleading in SharePoint Designer.

## Expressions

Supported scalar expression shapes include:

```yaml
literal: 1
```

```yaml
variable: calc
```

```yaml
toString:
  variable: calc
```

```yaml
type: toString
value:
  variable: calc
```

Supported lookup expression shapes include:

```yaml
type: lookupWorkflowContext
propertyName: CurrentWebUrl
```

```yaml
type: getCurrentListId
```

```yaml
type: getCurrentItemGuid
```

Lookup expressions should stay nested inside actions such as `assign`, `writeHistory`, HTTP URL construction, or list-item operations. Direct stage-child lookup actions are not safe for SharePoint Designer.

Assignment and lookup sample:

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

## Control flow and conditions

`while` and `if` use structured Boolean condition trees, not raw expression text.

Supported condition families include:

- Logical nodes: `and`, `or`, and `not`. `and`/`or` use `leftCondition` and `rightCondition`; `not` uses `operand`.
- Numeric comparisons: `isLessThan` / `lessThan`, `equals`, `greaterThan`, `lessThanOrEqual`, and `greaterThanOrEqual`.
- Typed comparisons: `isEqual` for Boolean, DateTime, and DynamicValue operands; `isEqualString`, `containsString`, `startsWithString`, `endsWithString`, and DateTime comparisons such as `isGreaterThan`.
- Operand expressions: `literal`, `variable`, `parseDate`, `parseDynamicValue`, and supported nested expression nodes.
- `designerId`: optional exported metadata used to preserve original SharePoint Designer activity IDs when needed for RHS operand rendering.

Control-flow sample:

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

Designer compatibility caveats for conditions:

- Workflow Manager validation is not enough to prove Designer rendering.
- Boolean condition nodes and operand conversion expression nodes need Designer-compatible result state and custom attributes.
- `ParseDate.CultureName` should remain in the Designer-authored property-element shape that calls `GetConfigurationValue`.
- RHS operands may need `ArgumentValue.Result` / `OutArgument` wrappers and preserved `designerId` values for SharePoint Designer to render tokens correctly.

## List and list-item actions

Current-item and list-item actions include `setField`, `createListItem`, `updateListItem`, `deleteListItem`, and nested `lookupListItemStringProperty` expressions.

Current item field update sample:

```yaml
- type: setField
  fieldName: Title
  value:
    literal: SPNet YAML list-action smoke
```

List lifecycle sample:

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

Direct/top-level list item lookup actions are SPD-unsafe even when server publish validation succeeds. Keep list-item lookups nested inside an assignment or another visible action argument:

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

Designer can show misleading local validation state for lifecycle actions. Treat server validation and runtime execution in the target environment as the source of truth for tested create/update flows.

Samples: [`samples/workflow.list-actions.yml`](../samples/workflow.list-actions.yml), [`samples/workflow.list-lifecycle.yml`](../samples/workflow.list-lifecycle.yml), and [`samples/workflow.list-item-lookup.yml`](../samples/workflow.list-item-lookup.yml).

## HTTP, REST, email, and task actions

HTTP actions use `callHttpWebService` / `callHttp` / `http`. Literal methods accept `GET`, `POST`, `PUT`, `DELETE`, and the SharePoint Designer forms `HTTPGET`, `HTTPPOST`, `HTTPPUT`, and `HTTPDELETE`; short forms are normalized to Designer forms.

HTTP GET sample:

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

For POST/PUT, build request body and header dictionaries first with `buildDynamicValue`, declare them as `DynamicValue` variables, and reference those variables from `requestContent` and `requestHeaders`. Those fields are variable names, not inline objects. Keep request and response bodies small and predictable.

Email uses `sendEmail` / `email` with `to`, `cc`, `subject`, and `body`. Recipients are wrapped in the SharePoint Designer-compatible `ExpandInitFormUsers` + `BuildCollection` shape. Samples use placeholder recipients; review real recipients before live publish.

Task support is intentionally bounded to `singleTask` / `task`, with required `assignedTo` and `title` plus optional task body, due date, task ID output, and outcome output. Defaults intentionally avoid accidental assignment/cancelation emails in samples.

Samples: [`samples/workflow.http.yml`](../samples/workflow.http.yml), [`samples/workflow.http-post.yml`](../samples/workflow.http-post.yml), [`samples/workflow.email.yml`](../samples/workflow.email.yml), and [`samples/workflow.task.yml`](../samples/workflow.task.yml).

## DynamicValue patterns

`DynamicValue` support is powerful but runtime-sensitive. Use explicit typed reads and writes, keep payloads small, and validate against the real response or dictionary shape.

Core actions and expressions include `buildDynamicValue`, `getDynamicValueProperty`, `setDynamicValueProperty`, `containsDynamicValueProperty`, `isEmptyDynamicValue`, and focused count/predicate helpers documented in the support matrix.

Important rules:

- Build nested dictionaries from the inside out.
- When a dictionary value is another dictionary, set `valueType: DynamicValue`; otherwise it may serialize as a scalar/string.
- Validate keys before reads when the payload can omit properties.
- Use `valueType` consistently for `String`, `Int32`, `Double`, `Boolean`, `DateTime`, `Guid`, and `DynamicValue` values.
- Slash-path reads/writes and array-like paths are supported by serializing the path literally; the YAML serializer does not bounds-check array indexes or create intermediate elements.

Deep examples and validation notes are in [Deep DynamicValue build/write/read validation](deep-dynamic-values.md). Samples: [`samples/workflow.experimental-dynamic-values.yml`](../samples/workflow.experimental-dynamic-values.yml), [`samples/workflow.experimental-deep-dynamic-values.yml`](../samples/workflow.experimental-deep-dynamic-values.yml), and [`samples/workflow.deepworkflowexample-dynamic-values.yml`](../samples/workflow.deepworkflowexample-dynamic-values.yml).

## Export and round-trip expectations

Export is a diagnostic aid, not a guaranteed full XAML-to-YAML round trip. With WebsiteCache/config available, export first attempts WF object-model deserialization and can reconstruct stage ordering and non-linear transitions from object references. Without object-model export, structural XML export recognizes common generated/reference shapes but has lower transition and expression fidelity.

Export warnings should be treated as important. Expressions, recipients, list IDs, identities, and operands can be placeholdered unless represented as simple attributes, simple literals/variables, or narrowly recognized generated expression text.

## Known limitations

- YAML support is intentionally limited to documented actions and expressions.
- Explicit stage transitions support stage-to-stage branches, defaults, loops, and `goto: end`, but large transition graphs can become hard to read and may hit Workflow Manager/Designer limits.
- Top-level lookup actions are rejected because SharePoint Designer can render them as blank/crashing actions.
- The CSOM publisher update path is delete/recreate. It does not migrate running instances or preserve old subscriptions beyond recreating the requested publish subscription.
- Large workflows, broad loops, many variables/properties, large HTTP/DynamicValue payloads, and repeated whole-body string manipulation can hit validation, persistence, rendering, or runtime limits even when local build succeeds.
- Local WebsiteCache/proxy assemblies can differ from the target publish/runtime environment.

## Authoring checklist

Before publishing a workflow:

1. Confirm every action in the workflow is classified appropriately in the [action support matrix](action-support-matrix.md).
2. Keep production workflows mostly stable, small, observable, and orchestration-focused.
3. Validate local config and build to XAML plus metadata JSON.
4. Inspect generated XAML and run export when round-trip diagnostics matter.
5. Publish first with `--dry-run`, then to a non-production SharePoint site with a unique workflow name.
6. Open the workflow in SharePoint Designer, run `Check for Errors`, and verify visible stage/action rendering.
7. Run realistic start conditions, download the workflow, and compare behavior before promoting the shape.
