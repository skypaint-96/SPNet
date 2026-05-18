<#
.SYNOPSIS
Primary packaged SPNet workflow command.

.DESCRIPTION
This is the preferred packaged entry point for YAML-first SharePoint workflow operations. It keeps build, inspect, export, publish, and local package diagnostics discoverable behind one command while delegating to the retained serializer and publisher wrapper scripts.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Command = 'help',

    [Alias('out')]
    [string]$CliOut,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments
)

$ErrorActionPreference = 'Stop'

function Write-SpNetError {
    param(
        [Parameter(Mandatory = $true)][string]$Code,
        [Parameter(Mandatory = $true)][string]$Message,
        [string]$Hint = '',
        [string]$Path = ''
    )

    [Console]::Error.WriteLine("SPNET_ERROR [$Code] $Message")
    if (-not [string]::IsNullOrWhiteSpace($Path)) { [Console]::Error.WriteLine("  path: $Path") }
    if (-not [string]::IsNullOrWhiteSpace($Hint)) { [Console]::Error.WriteLine("  remediation: $Hint") }
}

function Throw-SpNetCliError {
    param(
        [Parameter(Mandatory = $true)][string]$Code,
        [Parameter(Mandatory = $true)][string]$Message,
        [string]$Hint = '',
        [string]$Path = ''
    )

    $exception = New-Object System.InvalidOperationException($Message)
    $record = New-Object System.Management.Automation.ErrorRecord($exception, $Code, [System.Management.Automation.ErrorCategory]::InvalidOperation, $Path)
    if (-not [string]::IsNullOrWhiteSpace($Hint)) { $record.ErrorDetails = New-Object System.Management.Automation.ErrorDetails("$Message`nRemediation: $Hint") }
    throw $record
}

function Resolve-UserPath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) { return $Path }
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
}

function Test-SpNetHelpToken {
    param([string]$Value)
    return ($Value -in @('-h', '--help', '/?', '?', 'help'))
}

function Write-SPNetWorkflowHelp {
    @'
SPNet Workflow primary packaged CLI

Usage:
  .\scripts\spnet-workflow.ps1 <command> [options]
  powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 <command> [options]

Commands:
  help       Show top-level help or subcommand help.
  build      Build YAML to SharePoint Designer-compatible XAML and metadata JSON.
  inspect    Inspect generated or downloaded XAML.
  export     Export generated or downloaded XAML back to YAML.
  publish    Create a YAML-authored workflow through the publish wrapper; fails if the workflow already exists.
  update     Explicitly replace an existing same-name workflow through the publish wrapper.
  auth-test  Run local auth/publisher readiness checks without connecting to SharePoint.
  doctor     Run local source/package integrity checks without SharePoint connectivity.

Source-tree examples:
  .\scripts\spnet-workflow.ps1 build --workflow samples\workflow.example.yml --out artifacts\YamlFirstSmoke.xaml --config config\spnet.local.yml
  .\scripts\spnet-workflow.ps1 inspect --xaml artifacts\YamlFirstSmoke.xaml --config config\spnet.local.yml
  .\scripts\spnet-workflow.ps1 export --xaml artifacts\YamlFirstSmoke.xaml --out artifacts\YamlFirstSmoke.exported.yml
  .\scripts\spnet-workflow.ps1 publish --workflow samples\workflow.example.yml --xaml artifacts\YamlFirstSmoke.xaml --site-url https://tenant.sharepoint.com/sites/site --workflow-name YamlFirstSmoke --target-type Site --dry-run
  .\scripts\spnet-workflow.ps1 update --workflow samples\workflow.example.yml --xaml artifacts\YamlFirstSmoke.xaml --site-url https://tenant.sharepoint.com/sites/site --workflow-name YamlFirstSmoke --target-type Site --dry-run
  .\scripts\spnet-workflow.ps1 auth-test --site-url https://tenant.sharepoint.com/sites/site --auth-mode WebLogin
  .\scripts\spnet-workflow.ps1 doctor

Packaged examples:
  powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 help
  powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 doctor --json
  powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 build --workflow .\samples\workflow.example.yml --out .\artifacts\YamlFirstSmoke.xaml --config .\config\spnet.local.yml

Path handling:
  User-supplied relative paths are resolved from the caller's current directory.
  Script and tool discovery is resolved relative to this command/package.

Authentication and publisher defaults:
  Publish defaults to --auth-mode WebLogin, where the publish wrapper bootstraps browser/PnP WebLogin cookies for the CSOM publisher.
  Supported publish auth modes are WebLogin, CookieHeader, WindowsDefault, and Credentials.
  Packaged publish resolves tools\SPNet.Workflow.Publisher.Csom\SPNet.Workflow.Publisher.Csom.exe before source-build fallback. Direct CSOM publisher invocation is advanced because it does not perform WebLogin/cookie bootstrap.

Compatibility:
  Existing Invoke-SPNetYamlWorkflow.ps1, Invoke-SPNetWorkflow.ps1, serializer executable, and publisher executable remain available. Prefer this command for packaged usage.

Errors:
  Failures are reported as SPNET_ERROR [code] plus a remediation hint where possible.

Use '.\scripts\spnet-workflow.ps1 help <command>' for subcommand options.
'@
}

function Write-SPNetWorkflowSubcommandHelp {
    param([Parameter(Mandatory = $true)][string]$Name)

    switch ($Name.ToLowerInvariant()) {
        'build' {
            @'
Usage:
  .\scripts\spnet-workflow.ps1 build --workflow <workflow.yml> --out <workflow.xaml> [--config <spnet.local.yml>] [--cache-folder <WebsiteCache>]

Delegates to Invoke-SPNetYamlWorkflow.ps1 -Action Build.

Example:
  .\scripts\spnet-workflow.ps1 build --workflow samples\workflow.example.yml --out artifacts\YamlFirstSmoke.xaml --config config\spnet.local.yml

Packaged example:
  powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 build --workflow .\samples\workflow.example.yml --out .\artifacts\YamlFirstSmoke.xaml --config .\config\spnet.local.yml

Relative paths are resolved from the caller's current directory.
'@
        }
        'inspect' {
            @'
Usage:
  .\scripts\spnet-workflow.ps1 inspect --xaml <workflow.xaml> [--config <spnet.local.yml>] [--cache-folder <WebsiteCache>]

Delegates to Invoke-SPNetYamlWorkflow.ps1 -Action Inspect.

Example:
  .\scripts\spnet-workflow.ps1 inspect --xaml artifacts\YamlFirstSmoke.xaml --config config\spnet.local.yml

Packaged example:
  powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 inspect --xaml .\artifacts\YamlFirstSmoke.xaml --config .\config\spnet.local.yml

Relative paths are resolved from the caller's current directory.
'@
        }
        'export' {
            @'
Usage:
  .\scripts\spnet-workflow.ps1 export --xaml <workflow.xaml> --out <workflow.yml> [--form-field-xml <workflow.xaml.formfield.xml>]

Delegates to Invoke-SPNetYamlWorkflow.ps1 -Action Export.

Example:
  .\scripts\spnet-workflow.ps1 export --xaml artifacts\YamlFirstSmoke.xaml --out artifacts\YamlFirstSmoke.exported.yml

Packaged example:
  powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 export --xaml .\artifacts\YamlFirstSmoke.xaml --out .\artifacts\YamlFirstSmoke.exported.yml

Relative paths are resolved from the caller's current directory.
'@
        }
        'publish' {
            @'
Usage:
  .\scripts\spnet-workflow.ps1 publish --workflow <workflow.yml> --xaml <workflow.xaml> --site-url <url> --workflow-name <name> [options]
  .\scripts\spnet-workflow.ps1 publish --no-build --xaml <workflow.xaml> --site-url <url> --workflow-name <name> [options]

Creates a workflow through Invoke-SPNetYamlWorkflow.ps1 -Action Publish, which delegates live SharePoint publishing to Invoke-SPNetWorkflow.ps1. If a workflow with the effective name already exists, publish fails before creating anything and advises using the explicit update command/path or deleting before creating.

Common options:
  --config <path>                 Config file; default config\spnet.local.yml.
  --cache-folder <path>           SharePoint Designer WebsiteCache folder.
  --metadata-json <path>          Explicit metadata JSON sidecar.
  --target-type Site|List         Publish target type.
  --target-list-title <title>     Required for list workflows.
  --start-manual true|false       Manual start option.
  --start-created true|false      Item-created start option.
  --start-updated true|false      Item-updated start option.
  --if-exists CreateNew|Fail      Default: Fail. Fail checks before create; CreateNew uses a unique suffix when needed. Update remains available for compatibility but normal create/publish usage should use the explicit update command instead.
  --expected-definition-id <id>   Optional legacy fallback guarded update expected definition id.
  --backup-directory <path>       Optional legacy fallback guarded update backup directory.
  --dry-run                       Pass through existing dry-run behavior.
  --no-build                      Publish an existing XAML plus metadata JSON.
  --auth-mode WebLogin|CookieHeader|WindowsDefault|Credentials
                                  Default: WebLogin. WebLogin uses the wrapper to obtain PnP/WinINet cookies for the CSOM publisher.
  --publisher-cookie-header <v>   Explicit Cookie header; normally used with --auth-mode CookieHeader.
  --publisher-username <name>     Explicit username; normally used with --auth-mode Credentials.
  --publisher-password <secret>   Explicit password for --publisher-username.
  --publisher-domain <domain>     Optional Windows/domain credential domain.
  --publisher-exe <path>          Override publisher executable. Omit for packaged default.

Publisher discovery:
  Omitted --publisher-exe resolves package-relative tools\SPNet.Workflow.Publisher.Csom\SPNet.Workflow.Publisher.Csom.exe first, then source build output/project fallback.

Authentication modes:
  WebLogin       Default. Wrapper opens/uses PnP WebLogin and passes cookies to the CSOM publisher.
  CookieHeader   Wrapper passes --publisher-cookie-header directly; no WebLogin bootstrap.
  WindowsDefault Wrapper invokes the CSOM publisher with default Windows credentials; no WebLogin bootstrap.
  Credentials    Wrapper passes username/password/domain to the CSOM publisher; no WebLogin bootstrap.

Direct publisher warning:
  Direct SPNet.Workflow.Publisher.Csom.exe invocation does not bootstrap WebLogin/WinINet cookies. Use the primary CLI/wrappers unless all required cookies or credentials are supplied explicitly.

Publish safety:
  CSOM publish rolls back newly-created definitions/subscriptions if later publish or subscription creation fails, preventing orphan definitions from failed publishes.

Example:
  .\scripts\spnet-workflow.ps1 publish --workflow samples\workflow.example.yml --xaml artifacts\YamlFirstSmoke.xaml --site-url https://tenant.sharepoint.com/sites/site --workflow-name YamlFirstSmoke --target-type Site --dry-run

Packaged example:
  powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\spnet-workflow.ps1 publish --workflow .\samples\workflow.example.yml --xaml .\artifacts\YamlFirstSmoke.xaml --site-url https://tenant.sharepoint.com/sites/site --workflow-name YamlFirstSmoke --target-type Site --dry-run

Relative paths are resolved from the caller's current directory.
'@
        }
        'update' {
            @'
Usage:
  .\scripts\spnet-workflow.ps1 update --workflow <workflow.yml> --xaml <workflow.xaml> --site-url <url> --workflow-name <name> [options]
  .\scripts\spnet-workflow.ps1 update --no-build --xaml <workflow.xaml> --site-url <url> --workflow-name <name> [options]

Explicitly updates an existing same-name workflow by delegating to the publish path with Update conflict handling. Update replaces one existing same-name workflow by deleting its subscriptions/definition before creating the new definition/subscription. If no existing workflow is found, this path creates the workflow.

Use publish for normal create semantics. Use update only when replacing an existing workflow is intended.

Common options are the same as publish. CSOM publish rolls back newly-created definitions/subscriptions if later publish or subscription creation fails, preventing orphan definitions from failed updates.

Example:
  .\scripts\spnet-workflow.ps1 update --workflow samples\workflow.example.yml --xaml artifacts\YamlFirstSmoke.xaml --site-url https://tenant.sharepoint.com/sites/site --workflow-name YamlFirstSmoke --target-type Site --dry-run

Relative paths are resolved from the caller's current directory.
'@
        }
        'auth-test' {
            @'
Usage:
  .\scripts\spnet-workflow.ps1 auth-test --site-url <url> [--auth-mode <mode>] [--publisher-exe <path>] [auth options]

Runs a local, non-mutating readiness check. This validates URL shape, selected auth mode, required local auth inputs, wrapper presence, and publisher executable discovery. It does not connect to SharePoint, does not validate credentials, and does not publish.

Options:
  --site-url <url>                SharePoint site URL to validate locally.
  --auth-mode WebLogin|CookieHeader|WindowsDefault|Credentials
                                  Default: WebLogin.
  --publisher-cookie-header <v>   Required for CookieHeader mode.
  --publisher-username <name>     Required for Credentials mode.
  --publisher-password <secret>   Optional password for Credentials mode.
  --publisher-domain <domain>     Optional domain for Credentials mode.
  --publisher-exe <path>          Optional explicit publisher executable override.
  --json                          Emit machine-readable JSON.

Publisher discovery defaults to package-relative tools\SPNet.Workflow.Publisher.Csom\SPNet.Workflow.Publisher.Csom.exe before source fallback.

Example:
  .\scripts\spnet-workflow.ps1 auth-test --site-url https://tenant.sharepoint.com/sites/site --auth-mode WebLogin
'@
        }
        'doctor' {
            @'
Usage:
  .\scripts\spnet-workflow.ps1 doctor [--json]

Runs local source/package integrity checks without connecting to SharePoint.

Checks include primary/wrapper scripts, packaged tools or source fallback paths,
config files, artifacts writeability, package manifest readability, docs/samples,
and PowerShell runtime basics.

Example:
  .\scripts\spnet-workflow.ps1 doctor --json

Doctor uses package/script-relative discovery for SPNet scripts and tools.
'@
        }
        default { Throw-SpNetCliError -Code 'SPNET-CLI-HELP-001' -Message "Unknown help topic '$Name'." -Hint 'Run .\scripts\spnet-workflow.ps1 help for supported commands.' }
    }
}

function Resolve-SPNetPublisherToolPath {
    param([string]$ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        return [pscustomobject]@{ path = $ExplicitPath; source = 'explicit'; exists = (Test-Path $ExplicitPath -PathType Leaf); packagedDefault = $false }
    }

    $root = Get-SPNetWorkflowRoot
    $packagedPublisher = Join-Path $root 'tools\SPNet.Workflow.Publisher.Csom\SPNet.Workflow.Publisher.Csom.exe'
    if (Test-Path $packagedPublisher -PathType Leaf) { return [pscustomobject]@{ path = [IO.Path]::GetFullPath($packagedPublisher); source = 'packaged'; exists = $true; packagedDefault = $true } }

    $sourcePublisher = Join-Path $root 'src\SPNet.Workflow.Publisher.Csom\bin\Release\net48\SPNet.Workflow.Publisher.Csom.exe'
    if (Test-Path $sourcePublisher -PathType Leaf) { return [pscustomobject]@{ path = [IO.Path]::GetFullPath($sourcePublisher); source = 'source-output-fallback'; exists = $true; packagedDefault = $false } }

    $sourceProject = Join-Path $root 'src\SPNet.Workflow.Publisher.Csom\SPNet.Workflow.Publisher.Csom.csproj'
    if (Test-Path $sourceProject -PathType Leaf) { return [pscustomobject]@{ path = [IO.Path]::GetFullPath($sourcePublisher); source = 'source-project-build-fallback'; exists = $false; packagedDefault = $false; project = [IO.Path]::GetFullPath($sourceProject) } }

    return [pscustomobject]@{ path = [IO.Path]::GetFullPath($packagedPublisher); source = 'missing'; exists = $false; packagedDefault = $true }
}

function Invoke-SPNetAuthTest {
    param([hashtable]$Options)

    $jsonOutput = $Options.ContainsKey('json')
    $siteUrl = if ($Options.ContainsKey('site-url')) { [string]$Options['site-url'] } elseif ($Options.ContainsKey('siteurl')) { [string]$Options['siteurl'] } else { '' }
    $authMode = if ($Options.ContainsKey('auth-mode')) { [string]$Options['auth-mode'] } elseif ($Options.ContainsKey('authmode')) { [string]$Options['authmode'] } else { 'WebLogin' }
    $validAuthModes = @('WebLogin','CookieHeader','WindowsDefault','Credentials')
    $checks = New-Object 'System.Collections.Generic.List[object]'

    Add-SPNetDoctorCheck -Checks $checks -Status 'OK' -Name 'preflight scope' -Message 'Local readiness only; no SharePoint connection, credential validation, publish, update, or cleanup is performed.'

    if ([string]::IsNullOrWhiteSpace($siteUrl)) { Add-SPNetDoctorCheck -Checks $checks -Status 'FAIL' -Name 'site URL' -Message 'Missing --site-url.' -Remediation 'Pass --site-url https://tenant.sharepoint.com/sites/site.' }
    else {
        $parsedUri = $null
        if ([Uri]::TryCreate($siteUrl, [UriKind]::Absolute, [ref]$parsedUri) -and $parsedUri.Scheme -in @('http','https')) { Add-SPNetDoctorCheck -Checks $checks -Status 'OK' -Name 'site URL' -Message 'Site URL is an absolute HTTP/HTTPS URI.' -Path $siteUrl }
        else { Add-SPNetDoctorCheck -Checks $checks -Status 'FAIL' -Name 'site URL' -Message 'Site URL is not an absolute HTTP/HTTPS URI.' -Path $siteUrl -Remediation 'Use a full SharePoint site URL.' }
    }

    if ($authMode -notin $validAuthModes) { Add-SPNetDoctorCheck -Checks $checks -Status 'FAIL' -Name 'auth mode' -Message "Unsupported auth mode '$authMode'." -Remediation ('Use one of: ' + ($validAuthModes -join ', ') + '.') }
    else { Add-SPNetDoctorCheck -Checks $checks -Status 'OK' -Name 'auth mode' -Message "Selected $authMode." }

    $cookieHeader = if ($Options.ContainsKey('publisher-cookie-header')) { [string]$Options['publisher-cookie-header'] } elseif ($Options.ContainsKey('publishercookieheader')) { [string]$Options['publishercookieheader'] } else { '' }
    $username = if ($Options.ContainsKey('publisher-username')) { [string]$Options['publisher-username'] } elseif ($Options.ContainsKey('publisherusername')) { [string]$Options['publisherusername'] } else { '' }
    if ($authMode -eq 'CookieHeader') {
        if ([string]::IsNullOrWhiteSpace($cookieHeader)) { Add-SPNetDoctorCheck -Checks $checks -Status 'FAIL' -Name 'cookie auth input' -Message 'CookieHeader mode requires --publisher-cookie-header.' -Remediation 'Pass an explicit SharePoint Cookie header, or use --auth-mode WebLogin for wrapper bootstrap.' }
        else { Add-SPNetDoctorCheck -Checks $checks -Status 'OK' -Name 'cookie auth input' -Message 'Explicit cookie header was supplied.' }
    } elseif ($authMode -eq 'Credentials') {
        if ([string]::IsNullOrWhiteSpace($username)) { Add-SPNetDoctorCheck -Checks $checks -Status 'FAIL' -Name 'credential auth input' -Message 'Credentials mode requires --publisher-username.' -Remediation 'Pass --publisher-username and optional password/domain, or choose another auth mode.' }
        else { Add-SPNetDoctorCheck -Checks $checks -Status 'OK' -Name 'credential auth input' -Message 'Explicit username was supplied.' }
    } elseif ($authMode -eq 'WebLogin') {
        Add-SPNetDoctorCheck -Checks $checks -Status 'OK' -Name 'WebLogin bootstrap' -Message 'Publish wrapper will attempt PnP WebLogin and cookie handoff at live publish time.'
    } elseif ($authMode -eq 'WindowsDefault') {
        Add-SPNetDoctorCheck -Checks $checks -Status 'OK' -Name 'Windows default credentials' -Message 'Publisher will use default Windows credentials at live publish time.'
    }

    $wrapper = Get-SPNetWorkflowScriptPath -Name 'Invoke-SPNetWorkflow.ps1'
    Add-SPNetDoctorCheck -Checks $checks -Status 'OK' -Name 'publish wrapper' -Message 'Found publish wrapper that performs auth/bootstrap orchestration.' -Path $wrapper
    $publisherInfo = Resolve-SPNetPublisherToolPath -ExplicitPath $(if ($Options.ContainsKey('publisher-exe')) { [string]$Options['publisher-exe'] } elseif ($Options.ContainsKey('publisher-exe-path')) { [string]$Options['publisher-exe-path'] } elseif ($Options.ContainsKey('publisherexepath')) { [string]$Options['publisherexepath'] } else { '' })
    if ($publisherInfo.exists) { Add-SPNetDoctorCheck -Checks $checks -Status 'OK' -Name 'publisher executable' -Message "Resolved $($publisherInfo.source) publisher executable. Packaged default preferred: $($publisherInfo.packagedDefault)." -Path $publisherInfo.path }
    elseif ($publisherInfo.source -eq 'source-project-build-fallback') { Add-SPNetDoctorCheck -Checks $checks -Status 'WARN' -Name 'publisher executable' -Message 'Packaged publisher executable is missing; source project exists and live publish can build fallback.' -Path $publisherInfo.project -Remediation 'Prefer a complete package with tools\SPNet.Workflow.Publisher.Csom\SPNet.Workflow.Publisher.Csom.exe.' }
    else { Add-SPNetDoctorCheck -Checks $checks -Status 'FAIL' -Name 'publisher executable' -Message 'No packaged publisher executable or source fallback was found.' -Path $publisherInfo.path -Remediation 'Use a complete package, build/package from source, or pass --publisher-exe.' }

    Add-SPNetDoctorCheck -Checks $checks -Status 'OK' -Name 'direct publisher warning' -Message 'Direct CSOM publisher invocation is advanced and does not bootstrap WebLogin/cookies; use the wrapper unless explicit auth is supplied.'

    $failCount = @($checks | Where-Object { $_.status -eq 'FAIL' }).Count
    $warnCount = @($checks | Where-Object { $_.status -eq 'WARN' }).Count
    $okCount = @($checks | Where-Object { $_.status -eq 'OK' }).Count
    $summaryStatus = if ($failCount -gt 0) { 'FAIL' } elseif ($warnCount -gt 0) { 'WARN' } else { 'OK' }
    $result = [pscustomobject]@{ status = $summaryStatus; authMode = $authMode; siteUrl = $siteUrl; publisher = $publisherInfo; liveAuthenticationPerformed = $false; generatedUtc = (Get-Date).ToUniversalTime().ToString('o'); counts = [pscustomobject]@{ ok = $okCount; warn = $warnCount; fail = $failCount }; checks = $checks.ToArray() }

    if ($jsonOutput) { $result | ConvertTo-Json -Depth 6 }
    else {
        Write-Host "SPNet auth readiness: $summaryStatus (local only; no SharePoint connection)"
        foreach ($check in $checks) {
            $pathText = if ($check.path) { " [$($check.path)]" } else { '' }
            $line = "[$($check.status)] $($check.name): $($check.message)$pathText"
            if ($check.status -eq 'FAIL') { Write-Host $line -ForegroundColor Red }
            elseif ($check.status -eq 'WARN') { Write-Host $line -ForegroundColor Yellow }
            else { Write-Host $line -ForegroundColor Green }
            if ($check.remediation) { Write-Host "      remediation: $($check.remediation)" }
        }
        Write-Host "Summary: $okCount OK, $warnCount warning(s), $failCount failure(s)."
    }

    if ($failCount -gt 0) { exit 1 }
}

function Add-SPNetDoctorCheck {
    param(
        [System.Collections.Generic.List[object]]$Checks,
        [Parameter(Mandatory = $true)][ValidateSet('OK','WARN','FAIL')][string]$Status,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Message,
        [string]$Path = '',
        [string]$Remediation = ''
    )

    $Checks.Add([pscustomobject]@{ status = $Status; name = $Name; message = $Message; path = $Path; remediation = $Remediation }) | Out-Null
}

function Get-SPNetWorkflowRoot {
    return ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))).TrimEnd('\', '/')
}

function Test-SPNetReadableFile {
    param([Parameter(Mandatory = $true)][string]$Path)
    try {
        $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        $stream.Dispose()
        return $true
    } catch { return $false }
}

function Test-SPNetDoctorFile {
    param(
        [System.Collections.Generic.List[object]]$Checks,
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][string]$Name,
        [switch]$WarningOnly,
        [string]$Remediation = ''
    )

    $path = Join-Path $Root $RelativePath
    if ((Test-Path $path -PathType Leaf) -and (Test-SPNetReadableFile -Path $path)) {
        Add-SPNetDoctorCheck -Checks $Checks -Status 'OK' -Name $Name -Message 'Found and readable.' -Path $RelativePath
    } else {
        $status = if ($WarningOnly) { 'WARN' } else { 'FAIL' }
        $message = if (Test-Path $path -PathType Leaf) { 'Found but not readable.' } else { 'Missing.' }
        Add-SPNetDoctorCheck -Checks $Checks -Status $status -Name $Name -Message $message -Path $RelativePath -Remediation $Remediation
    }
}

function Test-SPNetDoctorDirectory {
    param(
        [System.Collections.Generic.List[object]]$Checks,
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][string]$Name,
        [switch]$WarningOnly,
        [string]$Remediation = ''
    )

    if (Test-Path (Join-Path $Root $RelativePath) -PathType Container) {
        Add-SPNetDoctorCheck -Checks $Checks -Status 'OK' -Name $Name -Message 'Found.' -Path $RelativePath
    } else {
        $status = if ($WarningOnly) { 'WARN' } else { 'FAIL' }
        Add-SPNetDoctorCheck -Checks $Checks -Status $status -Name $Name -Message 'Missing.' -Path $RelativePath -Remediation $Remediation
    }
}

function Test-SPNetDoctorArtifactsDirectory {
    param([System.Collections.Generic.List[object]]$Checks, [Parameter(Mandatory = $true)][string]$Root)

    $relative = 'artifacts'
    $path = Join-Path $Root $relative
    try {
        if (-not (Test-Path $path -PathType Container)) { New-Item -ItemType Directory -Path $path -Force | Out-Null }
        $probe = Join-Path $path ('.spnet-doctor-' + [guid]::NewGuid().ToString('N') + '.tmp')
        Set-Content -Path $probe -Value 'doctor' -Encoding ASCII
        Remove-Item -LiteralPath $probe -Force
        Add-SPNetDoctorCheck -Checks $Checks -Status 'OK' -Name 'artifacts directory' -Message 'Exists or can be created and written.' -Path $relative
    } catch {
        Add-SPNetDoctorCheck -Checks $Checks -Status 'FAIL' -Name 'artifacts directory' -Message ('Cannot create/write artifacts directory: ' + $_.Exception.Message) -Path $relative -Remediation 'Create artifacts manually or fix filesystem permissions.'
    }
}

function Invoke-SPNetDoctor {
    param([hashtable]$Options)

    $jsonOutput = $Options.ContainsKey('json')
    $root = Get-SPNetWorkflowRoot
    $checks = New-Object 'System.Collections.Generic.List[object]'
    $manifestPath = Join-Path $root 'package-manifest.json'
    $packageMode = Test-Path $manifestPath -PathType Leaf
    $sourceMode = Test-Path (Join-Path $root 'SPNet.slnx') -PathType Leaf
    $mode = if ($packageMode) { 'package' } elseif ($sourceMode) { 'source' } else { 'unknown' }

    if ($mode -eq 'unknown') { Add-SPNetDoctorCheck -Checks $checks -Status 'WARN' -Name 'root detection' -Message 'Could not confirm package or source root.' -Path $root -Remediation 'Run from an extracted package or repository checkout.' }
    else { Add-SPNetDoctorCheck -Checks $checks -Status 'OK' -Name 'root detection' -Message "Detected $mode root." -Path $root }

    Test-SPNetDoctorFile -Checks $checks -Root $root -RelativePath 'scripts\spnet-workflow.ps1' -Name 'primary CLI script' -Remediation 'Restore the primary CLI script.'
    Test-SPNetDoctorFile -Checks $checks -Root $root -RelativePath 'scripts\Invoke-SPNetYamlWorkflow.ps1' -Name 'YAML workflow wrapper' -Remediation 'Restore the YAML wrapper script.'
    Test-SPNetDoctorFile -Checks $checks -Root $root -RelativePath 'scripts\Invoke-SPNetWorkflow.ps1' -Name 'publish workflow wrapper' -Remediation 'Restore the publish wrapper script.'

    $serializerPackagePath = 'tools\SPNet.Workflow.WfSerializer\SPNet.Workflow.WfSerializer.exe'
    $serializerSourcePath = 'src\SPNet.Workflow.WfSerializer\bin\Release\net48\SPNet.Workflow.WfSerializer.exe'
    if (Test-Path (Join-Path $root $serializerPackagePath) -PathType Leaf) { Add-SPNetDoctorCheck -Checks $checks -Status 'OK' -Name 'serializer executable' -Message 'Found packaged serializer executable.' -Path $serializerPackagePath }
    elseif (Test-Path (Join-Path $root $serializerSourcePath) -PathType Leaf) { Add-SPNetDoctorCheck -Checks $checks -Status 'OK' -Name 'serializer executable' -Message 'Found source build fallback serializer executable.' -Path $serializerSourcePath }
    elseif (Test-Path (Join-Path $root 'src\SPNet.Workflow.WfSerializer\SPNet.Workflow.WfSerializer.csproj') -PathType Leaf) { Add-SPNetDoctorCheck -Checks $checks -Status 'WARN' -Name 'serializer executable' -Message 'Executable is missing, but source project exists and wrapper can build it on demand.' -Path 'src\SPNet.Workflow.WfSerializer\SPNet.Workflow.WfSerializer.csproj' -Remediation 'Run dotnet build for the serializer project or package without -SkipBuild.' }
    else { Add-SPNetDoctorCheck -Checks $checks -Status 'FAIL' -Name 'serializer executable' -Message 'No packaged executable or source fallback was found.' -Path $serializerPackagePath -Remediation 'Use a complete package or build/package from source.' }

    $publisherPackagePath = 'tools\SPNet.Workflow.Publisher.Csom\SPNet.Workflow.Publisher.Csom.exe'
    $publisherSourcePath = 'src\SPNet.Workflow.Publisher.Csom\bin\Release\net48\SPNet.Workflow.Publisher.Csom.exe'
    if (Test-Path (Join-Path $root $publisherPackagePath) -PathType Leaf) { Add-SPNetDoctorCheck -Checks $checks -Status 'OK' -Name 'publisher executable' -Message 'Found packaged publisher executable.' -Path $publisherPackagePath }
    elseif (Test-Path (Join-Path $root $publisherSourcePath) -PathType Leaf) { Add-SPNetDoctorCheck -Checks $checks -Status 'OK' -Name 'publisher executable' -Message 'Found source build fallback publisher executable.' -Path $publisherSourcePath }
    elseif (Test-Path (Join-Path $root 'src\SPNet.Workflow.Publisher.Csom\SPNet.Workflow.Publisher.Csom.csproj') -PathType Leaf) { Add-SPNetDoctorCheck -Checks $checks -Status 'WARN' -Name 'publisher executable' -Message 'Executable is missing, but source project exists.' -Path 'src\SPNet.Workflow.Publisher.Csom\SPNet.Workflow.Publisher.Csom.csproj' -Remediation 'Run dotnet build for the publisher project or package without -SkipBuild.' }
    else { Add-SPNetDoctorCheck -Checks $checks -Status 'FAIL' -Name 'publisher executable' -Message 'No packaged executable or source fallback was found.' -Path $publisherPackagePath -Remediation 'Use a complete package or build/package from source.' }

    Test-SPNetDoctorFile -Checks $checks -Root $root -RelativePath 'config\spnet.defaults.yml' -Name 'default config' -Remediation 'Restore config\spnet.defaults.yml.'
    Test-SPNetDoctorFile -Checks $checks -Root $root -RelativePath 'config\spnet.local.example.yml' -Name 'local config example' -WarningOnly -Remediation 'Restore config\spnet.local.example.yml.'
    Test-SPNetDoctorArtifactsDirectory -Checks $checks -Root $root

    if ($packageMode) {
        try {
            $manifest = Get-Content -Path $manifestPath -Raw | ConvertFrom-Json
            $version = if ($manifest.version) { [string]$manifest.version } else { 'unknown' }
            Add-SPNetDoctorCheck -Checks $checks -Status 'OK' -Name 'package manifest' -Message "Readable package manifest; version $version." -Path 'package-manifest.json'
        } catch {
            Add-SPNetDoctorCheck -Checks $checks -Status 'FAIL' -Name 'package manifest' -Message ('Manifest is missing, unreadable, or invalid JSON: ' + $_.Exception.Message) -Path 'package-manifest.json' -Remediation 'Recreate the package from source.'
        }
    } else {
        Add-SPNetDoctorCheck -Checks $checks -Status 'WARN' -Name 'package manifest' -Message 'package-manifest.json is absent; this is expected in source mode before packaging.' -Path 'package-manifest.json' -Remediation 'Run scripts\Package-SPNetWorkflow.ps1 to create a package.'
    }

    Test-SPNetDoctorFile -Checks $checks -Root $root -RelativePath 'README.md' -Name 'README documentation' -WarningOnly -Remediation 'Restore README.md.'
    Test-SPNetDoctorDirectory -Checks $checks -Root $root -RelativePath 'docs' -Name 'docs directory' -WarningOnly -Remediation 'Restore docs directory.'
    Test-SPNetDoctorDirectory -Checks $checks -Root $root -RelativePath 'samples' -Name 'samples directory' -WarningOnly -Remediation 'Restore samples directory.'
    Test-SPNetDoctorFile -Checks $checks -Root $root -RelativePath 'samples\workflow.example.yml' -Name 'example workflow sample' -WarningOnly -Remediation 'Restore samples\workflow.example.yml.'

    $powerShellEdition = if ($PSVersionTable.PSEdition) { [string]$PSVersionTable.PSEdition } else { 'Desktop' }
    Add-SPNetDoctorCheck -Checks $checks -Status 'OK' -Name 'PowerShell runtime' -Message "PowerShell $($PSVersionTable.PSVersion) ($powerShellEdition); CLR $($PSVersionTable.CLRVersion); OS $([Environment]::OSVersion.VersionString)."

    $failCount = @($checks | Where-Object { $_.status -eq 'FAIL' }).Count
    $warnCount = @($checks | Where-Object { $_.status -eq 'WARN' }).Count
    $okCount = @($checks | Where-Object { $_.status -eq 'OK' }).Count
    $summaryStatus = if ($failCount -gt 0) { 'FAIL' } elseif ($warnCount -gt 0) { 'WARN' } else { 'OK' }
    $result = [pscustomobject]@{ status = $summaryStatus; mode = $mode; root = $root; generatedUtc = (Get-Date).ToUniversalTime().ToString('o'); counts = [pscustomobject]@{ ok = $okCount; warn = $warnCount; fail = $failCount }; checks = $checks.ToArray() }

    if ($jsonOutput) { $result | ConvertTo-Json -Depth 6 }
    else {
        Write-Host "SPNet workflow doctor: $summaryStatus ($mode mode)"
        Write-Host "Root: $root"
        foreach ($check in $checks) {
            $pathText = if ($check.path) { " [$($check.path)]" } else { '' }
            $line = "[$($check.status)] $($check.name): $($check.message)$pathText"
            if ($check.status -eq 'FAIL') { Write-Host $line -ForegroundColor Red }
            elseif ($check.status -eq 'WARN') { Write-Host $line -ForegroundColor Yellow }
            else { Write-Host $line -ForegroundColor Green }
            if ($check.remediation) { Write-Host "      remediation: $($check.remediation)" }
        }
        Write-Host "Summary: $okCount OK, $warnCount warning(s), $failCount failure(s)."
    }

    if ($failCount -gt 0) { exit 1 }
}

function Get-SPNetWorkflowScriptPath {
    param([Parameter(Mandatory = $true)][string]$Name)

    $path = Join-Path $PSScriptRoot $Name
    if (Test-Path $path -PathType Leaf) { return $path }
    Throw-SpNetCliError -Code 'SPNET-CLI-SCRIPT-001' -Message "Required SPNet wrapper script was not found: $Name." -Path $path -Hint 'Use a complete SPNet package or restore the scripts directory from the repository/package.'
}

function ConvertFrom-SPNetCliArguments {
    param([string[]]$Values)

    $parsed = @{}
    $positionals = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $Values.Count; $i++) {
        $arg = [string]$Values[$i]
        if ($arg -eq '--') {
            for ($j = $i + 1; $j -lt $Values.Count; $j++) { $positionals.Add([string]$Values[$j]) }
            break
        }
        if ($arg.StartsWith('-')) {
            $name = ($arg -replace '^-+', '').ToLowerInvariant()
            if ($name -in @('help', 'h', '?', 'json', 'dry-run', 'dryrun', 'no-build', 'nobuild', 'force', 'include-subscriptions', 'includesubscriptions')) {
                $parsed[$name] = $true
                continue
            }
            if ($i + 1 -ge $Values.Count) { Throw-SpNetCliError -Code 'SPNET-CLI-ARGS-001' -Message "Missing value for option $arg." -Hint "Run .\scripts\spnet-workflow.ps1 help for usage." }
            $next = [string]$Values[$i + 1]
            if ($next.StartsWith('-')) { Throw-SpNetCliError -Code 'SPNET-CLI-ARGS-001' -Message "Missing value for option $arg." -Hint "Provide a value after $arg, or run .\scripts\spnet-workflow.ps1 help for usage." }
            $parsed[$name] = $next
            $i++
        } else {
            $positionals.Add($arg)
        }
    }
    $parsed['__positionals'] = @($positionals)
    return $parsed
}

function Add-SPNetArgumentValue {
    param([hashtable]$Target, [hashtable]$Source, [string[]]$Names, [string]$ParameterName)

    foreach ($name in $Names) {
        if ($Source.ContainsKey($name)) {
            $Target[$ParameterName] = $Source[$name]
            return
        }
    }
}

function Convert-SPNetUserPathOptions {
    param([hashtable]$Options, [string]$CommandName)

    $pathOptionNames = @(
        'workflow', 'workflow-yaml', 'xaml', 'xaml-path', 'out-xaml', 'out', 'output', 'output-yaml',
        'config', 'cache-folder', 'cache', 'metadata-json', 'metadatajson', 'metadata-json-path',
        'form-field-xml', 'formfieldxml', 'form-field-xml-path', 'backup-directory', 'backupdirectory',
        'publisher-exe', 'publisher-exe-path', 'publisherexepath'
    )

    foreach ($name in $pathOptionNames) {
        if ($Options.ContainsKey($name) -and $Options[$name] -is [string]) {
            $Options[$name] = Resolve-UserPath -Path ([string]$Options[$name])
        }
    }

    foreach ($name in @('workflow', 'workflow-yaml')) {
        if ($Options.ContainsKey($name) -and -not (Test-Path ([string]$Options[$name]) -PathType Leaf)) {
            Throw-SpNetCliError -Code 'SPNET-CLI-PATH-001' -Message 'Input workflow YAML file was not found.' -Path ([string]$Options[$name]) -Hint 'Check the --workflow path. Relative paths are resolved from the current directory where you invoked spnet-workflow.ps1.'
        }
    }

    foreach ($name in @('xaml', 'xaml-path', 'out-xaml')) {
        if ($Options.ContainsKey($name) -and $CommandName -in @('inspect', 'export') -and -not (Test-Path ([string]$Options[$name]) -PathType Leaf)) {
            Throw-SpNetCliError -Code 'SPNET-CLI-PATH-002' -Message 'Input XAML file was not found.' -Path ([string]$Options[$name]) -Hint 'Check the --xaml path or build the workflow first. Relative paths are resolved from the current directory.'
        }
    }

    foreach ($name in @('metadata-json', 'metadatajson', 'metadata-json-path')) {
        if ($Options.ContainsKey($name) -and -not (Test-Path ([string]$Options[$name]) -PathType Leaf)) {
            Throw-SpNetCliError -Code 'SPNET-CLI-PATH-003' -Message 'Metadata JSON file was not found.' -Path ([string]$Options[$name]) -Hint 'Build the workflow to generate the .metadata.json sidecar, or pass a valid --metadata-json path.'
        }
    }

    foreach ($name in @('form-field-xml', 'formfieldxml', 'form-field-xml-path')) {
        if ($Options.ContainsKey($name) -and -not (Test-Path ([string]$Options[$name]) -PathType Leaf)) {
            Throw-SpNetCliError -Code 'SPNET-CLI-PATH-004' -Message 'FormField XML file was not found.' -Path ([string]$Options[$name]) -Hint 'Use metadata JSON for normal YAML publish, or pass a valid legacy FormField XML path.'
        }
    }

    if ($Options.ContainsKey('publisher-exe') -and -not (Test-Path ([string]$Options['publisher-exe']) -PathType Leaf)) {
        Throw-SpNetCliError -Code 'SPNET-CLI-PATH-005' -Message 'Publisher executable was not found.' -Path ([string]$Options['publisher-exe']) -Hint 'Use the packaged publisher, build the publisher project, or pass a valid --publisher-exe path.'
    }

    return $Options
}

function Add-SPNetArgumentSwitch {
    param([hashtable]$Target, [hashtable]$Source, [string[]]$Names, [string]$ParameterName)

    foreach ($name in $Names) {
        if ($Source.ContainsKey($name)) {
            $Target[$ParameterName] = $true
            return
        }
    }
}

function Invoke-SPNetYamlWrapperAction {
    param([Parameter(Mandatory = $true)][string]$Action, [Parameter(Mandatory = $true)][hashtable]$Options)

    $wrapper = Get-SPNetWorkflowScriptPath -Name 'Invoke-SPNetYamlWorkflow.ps1'
    $parameters = @{ Action = $Action }
    Add-SPNetArgumentValue -Target $parameters -Source $Options -Names @('workflow', 'workflow-yaml') -ParameterName 'Workflow'
    Add-SPNetArgumentValue -Target $parameters -Source $Options -Names @('xaml', 'xaml-path', 'out-xaml') -ParameterName 'XamlPath'
    Add-SPNetArgumentValue -Target $parameters -Source $Options -Names @('out', 'output', 'output-yaml') -ParameterName 'Out'
    Add-SPNetArgumentValue -Target $parameters -Source $Options -Names @('config') -ParameterName 'Config'
    Add-SPNetArgumentValue -Target $parameters -Source $Options -Names @('cache-folder', 'cache') -ParameterName 'CacheFolder'
    Add-SPNetArgumentValue -Target $parameters -Source $Options -Names @('site-url', 'siteurl') -ParameterName 'SiteUrl'
    Add-SPNetArgumentValue -Target $parameters -Source $Options -Names @('workflow-name', 'workflowname', 'name') -ParameterName 'WorkflowName'
    Add-SPNetArgumentValue -Target $parameters -Source $Options -Names @('workflow-name-prefix', 'workflownameprefix') -ParameterName 'WorkflowNamePrefix'
    Add-SPNetArgumentValue -Target $parameters -Source $Options -Names @('target-type', 'targettype') -ParameterName 'TargetType'
    Add-SPNetArgumentValue -Target $parameters -Source $Options -Names @('target-list-title', 'targetlisttitle') -ParameterName 'TargetListTitle'
    Add-SPNetArgumentValue -Target $parameters -Source $Options -Names @('start-manual', 'startmanual') -ParameterName 'StartManual'
    Add-SPNetArgumentValue -Target $parameters -Source $Options -Names @('start-created', 'startoncreated', 'start-on-created') -ParameterName 'StartOnCreated'
    Add-SPNetArgumentValue -Target $parameters -Source $Options -Names @('start-updated', 'startonupdated', 'start-on-updated') -ParameterName 'StartOnUpdated'
    Add-SPNetArgumentValue -Target $parameters -Source $Options -Names @('status-column', 'statuscolumn') -ParameterName 'StatusColumn'
    Add-SPNetArgumentValue -Target $parameters -Source $Options -Names @('if-exists', 'ifexists') -ParameterName 'IfExists'
    Add-SPNetArgumentValue -Target $parameters -Source $Options -Names @('metadata-json', 'metadatajson', 'metadata-json-path') -ParameterName 'MetadataJsonPath'
    Add-SPNetArgumentValue -Target $parameters -Source $Options -Names @('form-field-xml', 'formfieldxml', 'form-field-xml-path') -ParameterName 'FormFieldXmlPath'
    Add-SPNetArgumentValue -Target $parameters -Source $Options -Names @('expected-definition-id', 'expecteddefinitionid') -ParameterName 'ExpectedDefinitionId'
    Add-SPNetArgumentValue -Target $parameters -Source $Options -Names @('backup-directory', 'backupdirectory') -ParameterName 'BackupDirectory'
    Add-SPNetArgumentValue -Target $parameters -Source $Options -Names @('publisher-exe', 'publisher-exe-path', 'publisherexepath') -ParameterName 'PublisherExePath'
    Add-SPNetArgumentValue -Target $parameters -Source $Options -Names @('auth-mode', 'authmode') -ParameterName 'AuthMode'
    Add-SPNetArgumentValue -Target $parameters -Source $Options -Names @('publisher-cookie-header', 'publishercookieheader') -ParameterName 'PublisherCookieHeader'
    Add-SPNetArgumentValue -Target $parameters -Source $Options -Names @('publisher-username', 'publisherusername') -ParameterName 'PublisherUsername'
    Add-SPNetArgumentValue -Target $parameters -Source $Options -Names @('publisher-password', 'publisherpassword') -ParameterName 'PublisherPassword'
    Add-SPNetArgumentValue -Target $parameters -Source $Options -Names @('publisher-domain', 'publisherdomain') -ParameterName 'PublisherDomain'
    Add-SPNetArgumentSwitch -Target $parameters -Source $Options -Names @('no-build', 'nobuild') -ParameterName 'NoBuild'
    Add-SPNetArgumentSwitch -Target $parameters -Source $Options -Names @('dry-run', 'dryrun') -ParameterName 'DryRun'
    Add-SPNetArgumentSwitch -Target $parameters -Source $Options -Names @('include-subscriptions', 'includesubscriptions') -ParameterName 'IncludeSubscriptions'
    Add-SPNetArgumentSwitch -Target $parameters -Source $Options -Names @('force') -ParameterName 'Force'

    & $wrapper @parameters
    if ($LASTEXITCODE -ne 0) { Throw-SpNetCliError -Code 'SPNET-CLI-DELEGATE-001' -Message "Delegated YAML workflow command failed with exit code $LASTEXITCODE." -Hint 'Review the preceding wrapper/tool output. Run doctor to check package integrity and help <command> to verify options.' }
}

try {
    if (-not [string]::IsNullOrWhiteSpace($CliOut)) {
        if ($null -eq $Arguments) { $Arguments = @() }
        $Arguments = @('--out', $CliOut) + @($Arguments)
    }

    $normalizedCommand = if ([string]::IsNullOrWhiteSpace($Command)) { 'help' } else { $Command.ToLowerInvariant() }
    if (Test-SpNetHelpToken -Value $normalizedCommand) { $normalizedCommand = 'help' }
    $options = ConvertFrom-SPNetCliArguments -Values @($Arguments)

    if ($normalizedCommand -eq 'help') {
        $topics = @($options['__positionals'] | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
        if ($topics.Count -gt 0) { Write-SPNetWorkflowSubcommandHelp -Name $topics[0] } else { Write-SPNetWorkflowHelp }
        return
    }

    if ($options.ContainsKey('help') -or $options.ContainsKey('h') -or $options.ContainsKey('?')) {
        Write-SPNetWorkflowSubcommandHelp -Name $normalizedCommand
        return
    }

    $validCommands = @('build', 'inspect', 'export', 'publish', 'update', 'auth-test', 'doctor')
    if ($normalizedCommand -notin $validCommands) {
        Throw-SpNetCliError -Code 'SPNET-CLI-COMMAND-001' -Message "Unknown command '$Command'." -Hint "Run .\scripts\spnet-workflow.ps1 help for supported commands: $($validCommands -join ', ')."
    }

    $options = Convert-SPNetUserPathOptions -Options $options -CommandName $normalizedCommand

    switch ($normalizedCommand) {
        'build' { Invoke-SPNetYamlWrapperAction -Action 'Build' -Options $options }
        'inspect' { Invoke-SPNetYamlWrapperAction -Action 'Inspect' -Options $options }
        'export' { Invoke-SPNetYamlWrapperAction -Action 'Export' -Options $options }
        'publish' { Invoke-SPNetYamlWrapperAction -Action 'Publish' -Options $options }
        'update' { $options['if-exists'] = 'Update'; Invoke-SPNetYamlWrapperAction -Action 'Publish' -Options $options }
        'auth-test' { Invoke-SPNetAuthTest -Options $options }
        'doctor' { Invoke-SPNetDoctor -Options $options }
    }
} catch {
    $code = if ($_.FullyQualifiedErrorId -and $_.FullyQualifiedErrorId -like 'SPNET-*') { $_.FullyQualifiedErrorId.Split(',')[0] } else { 'SPNET-CLI-UNHANDLED-001' }
    $hint = if ($_.ErrorDetails -and $_.ErrorDetails.Message -match 'Remediation:\s*(.+)$') { $matches[1] } else { 'Run .\scripts\spnet-workflow.ps1 help for usage or doctor for package integrity checks.' }
    $errorPath = if ($_.TargetObject) { [string]$_.TargetObject } else { '' }
    Write-SpNetError -Code $code -Message $_.Exception.Message -Hint $hint -Path $errorPath
    exit 1
}
