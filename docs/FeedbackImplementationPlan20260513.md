Implementation planning is complete. The feedback should be implemented in two arcs: a stabilisation release first, then authoring-power releases.

## Stabilisation release

1. **Action classification and release framing**
   - Mark every action as stable, preview, experimental, dev-only, or unsupported in [`docs/action-support-matrix.md`](docs/action-support-matrix.md).
   - Add production guidance and known limitations to [`README.md`](README.md).
   - Status: implemented 2026-05-13.
   - Outcome: users can distinguish safe workflows from risky hidden/experimental SharePoint activity usage.

2. **One packaged CLI entry point**
   - Introduce one primary packaged command, for example `spnet-workflow.exe`, with subcommands: build, inspect, export, publish, list, download, validate, lint, report, dry-run, doctor, cleanup, and help.
   - Milestone 2 implemented 2026-05-13 as [`scripts/spnet-workflow.ps1`](../scripts/spnet-workflow.ps1), with the scoped subcommands `help`, `build`, `inspect`, `export`, and `publish`.
   - `build`, `inspect`, and `export` delegate to [`scripts/Invoke-SPNetYamlWorkflow.ps1`](../scripts/Invoke-SPNetYamlWorkflow.ps1) and the existing serializer path; `publish` delegates through the YAML wrapper to [`scripts/Invoke-SPNetWorkflow.ps1`](../scripts/Invoke-SPNetWorkflow.ps1).
   - Existing scripts and direct executables remain compatibility paths, but packaged docs now prefer `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 <command> ...`.
   - Later commands such as doctor, lint, report, true dry-run, list, download, cleanup, and auth preflight remain deferred to their own milestones.
   - Likely touched areas: [`src/SPNet.Workflow.WfSerializer/Program.cs`](src/SPNet.Workflow.WfSerializer/Program.cs), [`src/SPNet.Workflow.Publisher.Csom/Program.cs`](src/SPNet.Workflow.Publisher.Csom/Program.cs), [`scripts/Invoke-SPNetYamlWorkflow.ps1`](scripts/Invoke-SPNetYamlWorkflow.ps1), [`scripts/Invoke-SPNetWorkflow.ps1`](scripts/Invoke-SPNetWorkflow.ps1), [`scripts/Package-SPNetWorkflow.ps1`](scripts/Package-SPNetWorkflow.ps1).
   - Status: implemented 2026-05-13 for scoped milestone 2.
   - Outcome: packaged users have one obvious command path instead of wrapper/direct-executable confusion.

3. **Package integrity and doctor checks**
   - Add package manifest/startup validation so packaged commands never silently depend on missing source-tree paths.
   - Add `doctor` checks for package files, binaries, WebsiteCache/proxy metadata, CSOM assemblies, config, cache paths, artifacts paths, and optional SharePoint auth readiness.
   - Outcome: packaging defects are caught before build/publish.

4. **Help, errors, and path handling**
   - Add clean top-level and subcommand help with packaged and source-tree examples.
   - Standardise relative path resolution.
   - Add structured error codes and remediation hints for common failures: missing binaries, missing WebsiteCache, unsupported action, duplicate workflow, auth failure, local/publish mismatch.
   - Outcome: probing with help does not fail strangely, and users get actionable diagnostics.

5. **Authentication and publisher defaults**
   - Make auth mode explicit: browser-cookie/WebLogin, explicit cookie header, Windows/default credentials, or legacy username/password/domain if supported.
   - Add an auth preflight command that authenticates and reports cookie/source/CSOM info without publishing.
   - Ensure packaged publish defaults to the packaged publisher binary, not source output.
   - Outcome: direct publisher behavior stops being a trap, and publish failures happen before site mutation.

6. **Safe publish iteration**
   - Implement true `dry-run`: build, lint, validate metadata, resolve publisher, auth preflight, resolve target list, inspect existing workflow, and output the planned operation without touching SharePoint.
   - Add safe policies: fail, create-new, guarded replace, replace-test, and cleanup.
   - Guarded replace should require expected workflow id/name, backup, and explicit confirmation/force.
   - Outcome: users can iterate without endless uniquely named workflows or risky manual cleanup.

7. **YAML linter and validator**
   - Add offline linting for schema/model issues, unsupported actions, variable/type mismatches, DateTime/string mismatch risks, missing HTTP response variables, unsafe DynamicValue access, repeated full-body string replacement, and likely loop explosion.
   - Add publish-parity warnings for hidden/experimental/dev-only activities that may build locally but fail publish or inspect poorly.
   - Outcome: common runtime/publish failures are caught before SharePoint Workflow Manager sees the XAML.

8. **Variable/property and activity-count reporting**
   - Generate reports for declared variables, inferred variables, initiation parameters, generated workflow properties, total property count, action count, nested action count, loop risk, HTTP/list/email/task side effects, and hidden/experimental action count.
   - Include this report in `dry-run`.
   - Outcome: users can keep workflows below practical Workflow Manager/runtime risk thresholds.

9. **DynamicValue runtime safety**
   - Add docs and lint checks for REST response shapes, `value` arrays, primitive/null values, typed extraction, missing property handling, and unsafe nested paths.
   - Improve samples around safe DynamicValue dictionary/array access and mutation.
   - Outcome: DynamicValue remains powerful but becomes less trial-and-error.

10. **Docs, samples, and CI hardening**
   - Rewrite quickstart around the primary packaged CLI.
   - Split or clearly label stable vs experimental/dev-only samples in [`samples/`](samples/).
   - Add package smoke tests in CI: help, doctor local, lint stable samples, build stable samples, report, and dry-run offline.
   - Outcome: releases prove the packaged workflow, not just source builds.

## Later authoring-power releases

1. **JSON helper actions**
   - Add safer high-level helpers such as parse JSON, typed get string/number/boolean/date, has property, count, any, where/find where feasible, and distinct/grouping only if they can map safely to supported runtime activities.
   - Build on DynamicValue safety and linter infrastructure first.

2. **Date actions**
   - Add or promote parse date, add days, compare dates, format date, week start/end, and date range helpers.
   - Keep actions experimental until publish/runtime/export behavior is verified.

3. **HTML/email templating and escaping**
   - Add build-time template inclusion and named placeholder substitution.
   - Add escaping modes for HTML text, HTML attribute, URL, and raw-with-warning.
   - Add lint warnings for unescaped SharePoint text/DynamicValue insertion into HTML email.

## Recommended implementation order

1. Classify action support and update release docs.
2. Add the primary packaged CLI and compatibility shims.
3. Add package manifest validation and `doctor`.
4. Standardise help, errors, output, and path handling.
5. Implement explicit auth preflight and packaged publisher defaults.
6. Add `dry-run`, safe update, replace-test, and cleanup flows.
7. Add linter/validator and publish-parity warnings.
8. Add variable/property report and activity estimator.
9. Add DynamicValue safety guidance/checks.
10. Refresh samples/docs and enforce package smoke tests.
11. Defer JSON/date/template helpers until the stabilisation foundation is shipped.

This plan prioritises packaging reliability, command ergonomics, validation before publish, and safe iteration before adding larger workflow-authoring features.
