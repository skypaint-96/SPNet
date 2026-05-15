# Stage transitions and round-trip guidance

SPNet supports explicit SharePoint Designer-style stage flow for `spnet.workflow/v1` YAML. Stage transitions are built as real Windows Workflow Foundation `System.Activities.Statements` object-model graphs: stages become `FlowStep` nodes, conditional transitions become chained `FlowDecision` nodes, and terminal branches become the Designer-compatible end sentinel during XAML serialization.

Use this feature when a workflow needs to branch between named stages, loop back to an earlier stage, skip forward, or end from a branch without relying on implicit linear stage order.

## YAML schema

Each stage can declare an optional stable `id` and a `transition` block. Transition targets can reference a unique stage `id`, a unique stage `name`, or the reserved terminal target `end`. Prefer `id` values for authored workflows because exported stage names may be normalized for uniqueness.

```yaml
schemaVersion: spnet.workflow/v1
name: stage flow example
variables:
- name: outcome
  type: String
stages:
- id: first
  name: First stage
  actions:
  - type: writeHistory
    message: In the first stage
  transition:
    branches:
    - condition:
        type: isEqualString
        valueType: String
        left:
          variable: outcome
        right:
          literal: approve
      goto: second
    - condition:
        type: isEqualString
        valueType: String
        left:
          variable: outcome
        right:
          literal: reject
      goto: end
    default:
      goto: third
- id: second
  name: Second stage
  actions:
  - type: writeHistory
    message: In the second stage
  transition:
    default:
      goto: first
- id: third
  name: Third stage
  actions:
  - type: writeHistory
    message: In the third stage
  transition:
    default:
      goto: end
```

Supported transition fields:

- `stage.id`: optional stable authoring/export target. It must be unique when used as a transition target.
- `stage.transition.branches`: ordered conditional branches. Each branch has `condition` and `goto`.
- `stage.transition.default.goto`: fallback target used when no branch condition is true.
- `stage.transition.goto`: shorthand for a default-only transition. Do not combine it with `transition.default.goto`.
- `goto: end`: reserved terminal target. It is serialized to SharePoint Designer-visible metadata using the SPD end sentinel `4294967294`.

If no explicit transitions are specified anywhere, stages remain linear in YAML order. Once explicit transitions are used, add a default terminal transition to intended final stages so export and Designer metadata stay unambiguous.

Reference samples:

- `samples/workflow.stage-transitions.yml`: compact branch, loop, and terminal-stage sample.
- `samples/workflow.stage-flow-concepts.yml`: publish-oriented site workflow sample with manual start metadata.

## Build, publish, and validation workflow

Build with the local SharePoint Designer WebsiteCache so the serializer can load SharePoint and Microsoft Activities proxy assemblies and emit Designer-compatible XAML plus sidecars:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 build --workflow samples\workflow.stage-flow-concepts.yml --out artifacts\SPNetStageFlowConceptsTest.xaml --config config\spnet.local.yml
```

Publish only after reviewing the generated XAML and metadata JSON. The generated `*.xaml.metadata.json` sidecar is the normal publish contract for display name, technical name, target, start options, initiation settings, and form fields.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 publish --workflow samples\workflow.stage-flow-concepts.yml --xaml artifacts\SPNetStageFlowConceptsTest.xaml --site-url 'https://tenant.sharepoint.com/sites/site' --workflow-name SPNetStageFlowConceptsTest --target-type Site --dry-run
```

For live validation, remove `--dry-run` only in a non-production site, open the published workflow in SharePoint Designer, run `Check for Errors`, verify the visible stage transition arrows/end states, run realistic start conditions, then download the workflow for round-trip inspection.

Validated ignored artifact names from the current stage-flow work include `artifacts/SPNetStageFlowConceptsTest.xaml`, `artifacts/SPNetStageFlowConceptsTest.exported.yml`, `artifacts/SPNetStageFlowConceptsTest.downloaded.xaml`, and `artifacts/SPNetStageFlowConceptsTest.downloaded.exported.yml`. Keep generated artifacts, site URLs, cookies, and local config out of source control.

## Export and round-trip requirements

Stage-flow export has two paths:

- With WebsiteCache/config available, export attempts WF object-model deserialization and reconstructs transitions from `Flowchart`, `FlowStep.Next`, and `FlowDecision.True` / `FlowDecision.False` object references. This is the preferred path for non-linear stage graphs.
- Without object-model deserialization, export falls back to structural XML inspection. It can still produce diagnostic YAML for recognizable `FlowStep` / `FlowDecision` markup, but transition and expression fidelity is lower.

Use the cache-enabled path for published/downloaded workflows:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 export --xaml artifacts\SPNetStageFlowConceptsTest.downloaded.xaml --out artifacts\SPNetStageFlowConceptsTest.downloaded.exported.yml --config config\spnet.local.yml
```

The serializer auto-loads committed defaults from `config/spnet.defaults.yml`, auto-loads `config/spnet.local.yml` when present and `--config` is omitted, and then applies an explicit `--config` file when supplied. Cache lookup order is explicit `--cache-folder` / `--cache`, then `spdCacheFolder` from merged config, then `SPNET_SPD_CACHE`.

## Current limitations

- Stage transition conditions use the existing structured Boolean condition support. Unsupported or partially recognized condition expressions may export as buildable placeholders.
- Exported stage target names can be normalized for uniqueness. Authored workflows should use unique `stage.id` values for stable targets.
- Branch chains are represented as ordered `FlowDecision` nodes. Very large or deeply nested transition graphs can become hard to read in SharePoint Designer and may hit practical Workflow Manager/Designer limits.
- Local build/export success is required but not sufficient for production. Validate against the target SharePoint site, list metadata, Workflow Manager version, and Designer rendering.
- The CSOM publisher does not need WebsiteCache, but build/export/inspect do. WebsiteCache assemblies are workstation-specific and are intentionally not packaged.
