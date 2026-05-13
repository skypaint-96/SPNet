<#
.SYNOPSIS
Primary packaged SPNet workflow command.

.DESCRIPTION
This is the preferred packaged entry point for YAML-first SharePoint workflow operations. It keeps build, inspect, export, and publish discoverable behind one command while delegating to the retained serializer and publisher wrapper scripts.
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

Packaged examples:
  .\scripts\spnet-workflow.ps1 build --workflow samples\workflow.example.yml --out artifacts\YamlFirstSmoke.xaml --config config\spnet.local.yml
  .\scripts\spnet-workflow.ps1 inspect --xaml artifacts\YamlFirstSmoke.xaml --config config\spnet.local.yml
  .\scripts\spnet-workflow.ps1 export --xaml artifacts\YamlFirstSmoke.xaml --out artifacts\YamlFirstSmoke.exported.yml
  .\scripts\spnet-workflow.ps1 publish --workflow samples\workflow.example.yml --xaml artifacts\YamlFirstSmoke.xaml --site-url https://tenant.sharepoint.com/sites/site --workflow-name YamlFirstSmoke --target-type Site --dry-run

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
        default { throw "Unknown help topic '$Name'. Supported topics: build, inspect, export, publish." }
    }
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
            if ($name -in @('help', 'h', '?', 'dry-run', 'dryrun', 'no-build', 'nobuild', 'force', 'include-subscriptions', 'includesubscriptions')) {
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
    default { throw "Unknown command '$Command'. Run '.\scripts\spnet-workflow.ps1 help' for supported commands." }
}
