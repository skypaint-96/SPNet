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

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments
)

$ErrorActionPreference = 'Stop'

function Write-SPNetWorkflowHelp {
    @'
SPNet Workflow primary packaged CLI

Usage:
  .\scripts\spnet-workflow.ps1 <command> [options]

Commands:
  help       Show top-level help or subcommand help.
  build      Build YAML to SharePoint Designer-compatible XAML and metadata JSON.
  inspect    Inspect generated or downloaded XAML.
  export     Export generated or downloaded XAML back to YAML.
  publish    Build and/or publish a YAML-authored workflow through the publish wrapper.
  doctor     Run local source/package integrity checks without SharePoint connectivity.

Packaged examples:
  .\scripts\spnet-workflow.ps1 build --workflow samples\workflow.example.yml --out artifacts\YamlFirstSmoke.xaml --config config\spnet.local.yml
  .\scripts\spnet-workflow.ps1 inspect --xaml artifacts\YamlFirstSmoke.xaml --config config\spnet.local.yml
  .\scripts\spnet-workflow.ps1 export --xaml artifacts\YamlFirstSmoke.xaml --out artifacts\YamlFirstSmoke.exported.yml
  .\scripts\spnet-workflow.ps1 publish --workflow samples\workflow.example.yml --xaml artifacts\YamlFirstSmoke.xaml --site-url https://tenant.sharepoint.com/sites/site --workflow-name YamlFirstSmoke --target-type Site --dry-run
  .\scripts\spnet-workflow.ps1 doctor

Compatibility:
  Existing Invoke-SPNetYamlWorkflow.ps1, Invoke-SPNetWorkflow.ps1, serializer executable, and publisher executable remain available. Prefer this command for packaged usage.

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
'@
        }
        'inspect' {
            @'
Usage:
  .\scripts\spnet-workflow.ps1 inspect --xaml <workflow.xaml> [--config <spnet.local.yml>] [--cache-folder <WebsiteCache>]

Delegates to Invoke-SPNetYamlWorkflow.ps1 -Action Inspect.

Example:
  .\scripts\spnet-workflow.ps1 inspect --xaml artifacts\YamlFirstSmoke.xaml --config config\spnet.local.yml
'@
        }
        'export' {
            @'
Usage:
  .\scripts\spnet-workflow.ps1 export --xaml <workflow.xaml> --out <workflow.yml> [--form-field-xml <workflow.xaml.formfield.xml>]

Delegates to Invoke-SPNetYamlWorkflow.ps1 -Action Export.

Example:
  .\scripts\spnet-workflow.ps1 export --xaml artifacts\YamlFirstSmoke.xaml --out artifacts\YamlFirstSmoke.exported.yml
'@
        }
        'publish' {
            @'
Usage:
  .\scripts\spnet-workflow.ps1 publish --workflow <workflow.yml> --xaml <workflow.xaml> --site-url <url> --workflow-name <name> [options]
  .\scripts\spnet-workflow.ps1 publish --no-build --xaml <workflow.xaml> --site-url <url> --workflow-name <name> [options]

Delegates to Invoke-SPNetYamlWorkflow.ps1 -Action Publish, which delegates live SharePoint publishing to Invoke-SPNetWorkflow.ps1.

Common options:
  --config <path>                 Config file; default config\spnet.local.yml.
  --cache-folder <path>           SharePoint Designer WebsiteCache folder.
  --metadata-json <path>          Explicit metadata JSON sidecar.
  --target-type Site|List         Publish target type.
  --target-list-title <title>     Required for list workflows.
  --start-manual true|false       Manual start option.
  --start-created true|false      Item-created start option.
  --start-updated true|false      Item-updated start option.
  --if-exists Update|CreateNew|Fail
  --expected-definition-id <id>   Guarded update expected definition id.
  --backup-directory <path>       Guarded update backup directory.
  --dry-run                       Pass through existing dry-run behavior.
  --no-build                      Publish an existing XAML plus metadata JSON.

Example:
  .\scripts\spnet-workflow.ps1 publish --workflow samples\workflow.example.yml --xaml artifacts\YamlFirstSmoke.xaml --site-url https://tenant.sharepoint.com/sites/site --workflow-name YamlFirstSmoke --target-type Site --dry-run
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
'@
        }
        default { throw "Unknown help topic '$Name'. Supported topics: build, inspect, export, publish, doctor." }
    }
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
    throw "Required SPNet script not found beside primary CLI: $path"
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
            if ($i + 1 -ge $Values.Count) { throw "Missing value for option $arg." }
            $next = [string]$Values[$i + 1]
            if ($next.StartsWith('-')) { throw "Missing value for option $arg." }
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
    Add-SPNetArgumentSwitch -Target $parameters -Source $Options -Names @('no-build', 'nobuild') -ParameterName 'NoBuild'
    Add-SPNetArgumentSwitch -Target $parameters -Source $Options -Names @('dry-run', 'dryrun') -ParameterName 'DryRun'
    Add-SPNetArgumentSwitch -Target $parameters -Source $Options -Names @('include-subscriptions', 'includesubscriptions') -ParameterName 'IncludeSubscriptions'
    Add-SPNetArgumentSwitch -Target $parameters -Source $Options -Names @('force') -ParameterName 'Force'

    & $wrapper @parameters
}

$normalizedCommand = if ([string]::IsNullOrWhiteSpace($Command)) { 'help' } else { $Command.ToLowerInvariant() }
if ($normalizedCommand -in @('-h', '--help', '/?', '?')) { $normalizedCommand = 'help' }
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

switch ($normalizedCommand) {
    'build' { Invoke-SPNetYamlWrapperAction -Action 'Build' -Options $options }
    'inspect' { Invoke-SPNetYamlWrapperAction -Action 'Inspect' -Options $options }
    'export' { Invoke-SPNetYamlWrapperAction -Action 'Export' -Options $options }
    'publish' { Invoke-SPNetYamlWrapperAction -Action 'Publish' -Options $options }
    'doctor' { Invoke-SPNetDoctor -Options $options }
    default { throw "Unknown command '$Command'. Run '.\scripts\spnet-workflow.ps1 help' for supported commands." }
}
