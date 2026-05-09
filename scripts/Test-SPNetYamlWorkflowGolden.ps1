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
    'InitBlock-7751C281-B0D1-4336-87B4-83F2198EDE6D',
    'StageContainer-8EDBFE6D-DA0D-42F6-A806-F5807380DA4D',
    'StageHeader-7FE15537-DFDB-4198-ABFA-8AF8B9D669AE',
    'StageFooter-3A59FA7C-C493-47A1-8F8B-1F481143EB08',
    'clr-namespace:Microsoft.SharePoint.WorkflowServices.Activities',
    'WriteToHistory',
    'SetWorkflowStatus'
)

if ($Workflow -notlike '*workflow.list-actions.yml') {
    $requiredMarkers += 'x:Members'
}

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

if ($Workflow -like '*workflow.http.yml') {
    $httpRequiredMarkers = @(
        'CallHTTPWebService',
        'LookupSPListItemPropertyNameInREST',
        'DynamicValue',
        'RequestContent',
        'RequestHeaders',
        'ResponseStatusCode',
        'HTTPGET',
        'GetDynamicValueProperty',
        'PropertyName="Title"',
        'currentUserTitle',
        'LookupWorkflowContextProperty',
        'PropertyName="CurrentWebUrl"',
        '{0}/_api/web/currentuser'
    )

    foreach ($marker in $httpRequiredMarkers) {
        if ($xaml -notlike "*$marker*") { throw "HTTP golden regression marker missing from ${XamlPath}: $marker" }
    }

    if ($xaml -like '*literal: GET*') { throw "HTTP request method was not normalized in ${XamlPath}." }
}

if ($Workflow -like '*workflow.list-actions.yml') {
    $listRequiredMarkers = @(
        'YamlListActionsSmoke.MTW',
        'SetField',
        'FieldName',
        'Title',
        'SPNet YAML list-action smoke',
        'WriteToHistory',
        'SPNet YAML TestList smoke workflow updated the current item Title.'
    )

    foreach ($marker in $listRequiredMarkers) {
        if ($xaml -notlike "*$marker*") { throw "List golden regression marker missing from ${XamlPath}: $marker" }
    }

    $listForbiddenMarkers = @(
        'GetCurrentListId',
        'GetCurrentItemGuid',
        'LookupSPListItemPropertyNameInREST'
    )

    foreach ($marker in $listForbiddenMarkers) {
        if ($xaml -like "*$marker*") { throw "List golden regression marker must not appear in ${XamlPath}: $marker" }
    }
}

if ($Workflow -like '*workflow.list-lifecycle.yml') {
    $lifecycleRequiredMarkers = @(
        'SPNetYamlListLifecycleManual.MTW',
        'CreateListItem',
        'UpdateListItem',
        'ListItemProperties',
        'GetCurrentListId',
        'createdItemId',
        'createdItemGuid',
        'SPNet lifecycle create',
        'SPNet lifecycle updated'
    )

    foreach ($marker in $lifecycleRequiredMarkers) {
        if ($xaml -notlike "*$marker*") { throw "List lifecycle golden regression marker missing from ${XamlPath}: $marker" }
    }

    if ($xaml -like '*Literal x:TypeArguments="x:Object"*' -or $xaml -like '*Literal&lt;x:Object&gt;*') { throw "List lifecycle XAML emitted invalid object literal in ${XamlPath}." }
}

if ($Workflow -like '*workflow.list-item-lookup.yml') {
    $lookupRequiredMarkers = @(
        'SPNetYamlListItemLookupManual.MTW',
        'CreateListItem',
        'UpdateListItem',
        'Assign',
        'LookupSPListItemStringProperty',
        'PropertyName',
        'Title',
        'readBackTitle',
        'SPNet lookup readback updated',
        'Read back TestList Title'
    )

    foreach ($marker in $lookupRequiredMarkers) {
        if ($xaml -notlike "*$marker*") { throw "List item lookup golden regression marker missing from ${XamlPath}: $marker" }
    }

    if ($xaml -like '*LookupSPListItemStringProperty.Result*') { throw "List item lookup sample must not emit top-level LookupSPListItemStringProperty Result output in ${XamlPath}. Keep the lookup nested inside assign/setVariable." }
}

Write-Host "Golden YAML-first workflow regression passed: $XamlPath"
