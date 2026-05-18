# SPNet documentation index

This index is the quickest way to choose the right SPNet document. The root [README](../README.md) stays focused on overview and quickstart; detailed reference material lives in this directory.

## Choose by task

| I want to... | Go to |
| --- | --- |
| Understand what SPNet does and run the first local build | [README](../README.md) |
| Configure WebsiteCache, run commands, publish safely, download/list/cleanup workflows, or package a release | [Publishing and operations guide](publishing-and-operations.md) |
| Write or review `spnet.workflow/v1` YAML | [Workflow authoring guide](workflow-authoring.md) |
| Check action support, aliases, export status, sample coverage, tests, validation, risk, or caveats | [Workflow action support matrix](action-support-matrix.md) |
| Add stage-to-stage branches, loops, terminal transitions, or function-like stage calls | [Stage transitions and round-trip guidance](stage-transitions.md) |
| Build, write, read, or validate nested `DynamicValue` dictionaries and slash paths | [Deep DynamicValue build/write/read validation](deep-dynamic-values.md) |
| Review the stabilisation-release feedback implementation plan | [Feedback implementation plan 2026-05-13](FeedbackImplementationPlan20260513.md) |

## Command entry points

| Entry point | Use for |
| --- | --- |
| [`scripts/spnet-workflow.ps1`](../scripts/spnet-workflow.ps1) | Primary packaged command for `help`, `build`, `inspect`, `export`, `publish`, `update`, `auth-test`, and `doctor`. |
| [`scripts/Invoke-SPNetYamlWorkflow.ps1`](../scripts/Invoke-SPNetYamlWorkflow.ps1) | Compatibility wrapper and operations not exposed by the primary command, including `ValidateConfig`, `Download`, `List`, and `Cleanup`. |
| [`scripts/Invoke-SPNetWorkflow.ps1`](../scripts/Invoke-SPNetWorkflow.ps1) | Live SharePoint publish/download/list/cleanup boundary used by the YAML wrapper. |
| [`scripts/Get-SPNetWorkflowDiagnostics.ps1`](../scripts/Get-SPNetWorkflowDiagnostics.ps1) | Read-only SharePoint workflow diagnostics and download helper. |
| [`scripts/Package-SPNetWorkflow.ps1`](../scripts/Package-SPNetWorkflow.ps1) | Local release package generation. |

## Sample guide

Start with the smallest stable sample, then move to focused samples only when you need the feature they demonstrate.

| Feature area | Samples |
| --- | --- |
| Baseline smoke workflow | [`samples/workflow.example.yml`](../samples/workflow.example.yml) |
| Variables and assignment | [`samples/workflow.assign.yml`](../samples/workflow.assign.yml), [`samples/workflow.string-actions.yml`](../samples/workflow.string-actions.yml) |
| Initiation parameters and metadata | [`samples/workflow.parameters.yml`](../samples/workflow.parameters.yml) |
| Conditions and loops | [`samples/workflow.control-flow.yml`](../samples/workflow.control-flow.yml), [`samples/workflow.conditionals-test.yml`](../samples/workflow.conditionals-test.yml) |
| Workflow context and list/item lookup expressions | [`samples/workflow.lookup.yml`](../samples/workflow.lookup.yml), [`samples/workflow.list-item-lookup.yml`](../samples/workflow.list-item-lookup.yml) |
| Current-item and list lifecycle actions | [`samples/workflow.list-actions.yml`](../samples/workflow.list-actions.yml), [`samples/workflow.list-lifecycle.yml`](../samples/workflow.list-lifecycle.yml) |
| HTTP, REST, email, and task actions | [`samples/workflow.http.yml`](../samples/workflow.http.yml), [`samples/workflow.http-post.yml`](../samples/workflow.http-post.yml), [`samples/workflow.email.yml`](../samples/workflow.email.yml), [`samples/workflow.task.yml`](../samples/workflow.task.yml) |
| Stage transitions and stage functions | [`samples/workflow.stage-transitions.yml`](../samples/workflow.stage-transitions.yml), [`samples/workflow.stage-flow-concepts.yml`](../samples/workflow.stage-flow-concepts.yml), [`samples/workflow.stage-functions.yml`](../samples/workflow.stage-functions.yml) |
| Experimental and deep `DynamicValue` patterns | [`samples/workflow.experimental-dynamic-values.yml`](../samples/workflow.experimental-dynamic-values.yml), [`samples/workflow.experimental-deep-dynamic-values.yml`](../samples/workflow.experimental-deep-dynamic-values.yml), [`samples/workflow.deepworkflowexample-dynamic-values.yml`](../samples/workflow.deepworkflowexample-dynamic-values.yml) |
| Dev-only Microsoft activity experiments | [`samples/workflow.devonly-microsoft-activities-expressions.yml`](../samples/workflow.devonly-microsoft-activities-expressions.yml) |

## Source layout reference

| Path | Notes |
| --- | --- |
| [`src/SPNet.Workflow.WfSerializer/`](../src/SPNet.Workflow.WfSerializer/) | YAML parser, activity builder, serializer, XAML exporter/inspector, metadata normalization, and WebsiteCache proxy assembly loading. |
| [`src/SPNet.Workflow.Publisher.Csom/`](../src/SPNet.Workflow.Publisher.Csom/) | Standalone WorkflowServices CSOM publisher. |
| [`tests/SPNet.Workflow.WfSerializer.Tests/`](../tests/SPNet.Workflow.WfSerializer.Tests/) | Serializer and YAML model tests. |
| [`config/spnet.defaults.yml`](../config/spnet.defaults.yml) | Safe committed defaults. |
| [`config/spnet.local.example.yml`](../config/spnet.local.example.yml) | Example local WebsiteCache config; copy to ignored `config/spnet.local.yml`. |
| [`artifacts/`](../artifacts/) | Ignored local build/diagnostic workspace. |

Generated artifacts, tenant URLs, cookies, passwords, secrets, and workstation-specific config should remain outside source control.
