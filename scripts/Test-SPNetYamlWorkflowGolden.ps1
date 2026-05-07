param(
    [string]$Workflow = 'samples\workflow.example.yml',
    [string]$XamlPath = 'artifacts\golden\YamlFirstSmoke.xaml',
    [string]$Config = 'config\spnet.local.yml',
    [string]$CacheFolder = '',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$invoke = Join-Path $PSScriptRoot 'Invoke-SPNetYamlWorkflow.ps1'
$resolvedXaml = Join-Path $root $XamlPath

if (-not $NoBuild) {
    $buildArgs = @{ Action = 'Build'; Workflow = $Workflow; XamlPath = $XamlPath; Config = $Config }
    if ($CacheFolder) { $buildArgs.CacheFolder = $CacheFolder }
    & $invoke @buildArgs
}

if (-not (Test-Path $resolvedXaml)) { throw "Golden XAML not found: $XamlPath" }

$xaml = Get-Content -Raw -Path $resolvedXaml
$requiredMarkers = @(
    '.MTW',
    'x:Members',
    'InitBlock-7751C281-B0D1-4336-87B4-83F2198EDE6D',
    'StageContainer-8EDBFE6D-DA0D-42F6-A806-F5807380DA4D',
    'StageHeader-7FE15537-DFDB-4198-ABFA-8AF8B9D669AE',
    'StageFooter-3A59FA7C-C493-47A1-8F8B-1F481143EB08',
    'clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities',
    'WriteToHistory',
    'SetWorkflowStatus'
)

foreach ($marker in $requiredMarkers) {
    if ($xaml -notlike "*$marker*") { throw "Golden regression marker missing from ${XamlPath}: $marker" }
}

$forbiddenMarkers = @(
    'xmlns:local="clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities;assembly=Microsoft.SharePoint.WorkflowServices.Activities.Proxy"',
    'Microsoft.SharePoint.WorkflowServices.Activities;assembly=Microsoft.SharePoint.WorkflowServices.Activities.Proxy'
)

foreach ($marker in $forbiddenMarkers) {
    if ($xaml -like "*$marker*") { throw "Golden regression marker must not appear in ${XamlPath}: $marker" }
}

Write-Host "Golden YAML-first workflow regression passed: $XamlPath"
