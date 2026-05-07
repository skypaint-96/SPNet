<#
.SYNOPSIS
Read-only SharePoint Workflow Manager diagnostics for SPNet investigations.

.DESCRIPTION
Connects with the same legacy PnP -UseWebLogin mechanism as Invoke-SPNetWorkflow.ps1,
enumerates workflow definitions/subscriptions, downloads matching XAML, and writes JSON
diagnostic dumps without publishing, deleting, or modifying SharePoint objects.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SiteUrl,
    [string]$WorkflowName,
    [string]$OutputDirectory = (Join-Path (Join-Path (Get-Location) 'artifacts') 'sharepoint-workflow-diagnostics')
)

$ErrorActionPreference = 'Stop'

function Connect-SPNetDiagnosticsPnPOnline {
    param([string]$Url)
    $command = Get-Command Connect-PnPOnline -ErrorAction SilentlyContinue
    if (-not $command) { throw 'Connect-PnPOnline is not available. Install/import a PnP PowerShell module compatible with this SharePoint farm.' }
    if ($command.Parameters.Keys -notcontains 'UseWebLogin') { throw 'Connect-PnPOnline -UseWebLogin is unavailable. Install/import a legacy PnP PowerShell module that supports WebLogin.' }
    Connect-PnPOnline -Url $Url -UseWebLogin
}

function Import-SPNetDiagnosticsWorkflowServicesCsom {
    $loadedType = [Type]::GetType('Microsoft.SharePoint.Client.WorkflowServices.WorkflowServicesManager, Microsoft.SharePoint.Client.WorkflowServices', $false)
    if ($loadedType) { return }
    $candidateRoots = @()
    $pnpCommand = Get-Command Connect-PnPOnline -ErrorAction SilentlyContinue
    if ($pnpCommand -and $pnpCommand.Module -and $pnpCommand.Module.ModuleBase) { $candidateRoots += $pnpCommand.Module.ModuleBase }
    $candidateRoots += @(
        (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.sharepointonline.csom'),
        'C:\Program Files\Common Files\microsoft shared\Web Server Extensions\15\ISAPI',
        'C:\Program Files (x86)\Common Files\microsoft shared\Web Server Extensions\15\ISAPI',
        'C:\Program Files\SharePoint Client Components',
        'C:\Program Files (x86)\SharePoint Client Components'
    )
    $workflowAssembly = $null
    foreach ($root in ($candidateRoots | Where-Object { $_ } | Select-Object -Unique)) {
        if (-not (Test-Path $root)) { continue }
        $workflowAssembly = Get-ChildItem -Path $root -Recurse -Filter 'Microsoft.SharePoint.Client.WorkflowServices.dll' -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($workflowAssembly) { break }
    }
    if (-not $workflowAssembly) { throw 'Authenticated with PnP, but Microsoft.SharePoint.Client.WorkflowServices.dll is not available locally.' }
    $assemblyDirectory = Split-Path -Parent $workflowAssembly.FullName
    foreach ($assemblyName in @('Microsoft.SharePoint.Client.Runtime.dll', 'Microsoft.SharePoint.Client.dll', 'Microsoft.SharePoint.Client.WorkflowServices.dll')) {
        $assemblyPath = Join-Path $assemblyDirectory $assemblyName
        if (Test-Path $assemblyPath) { Add-Type -Path $assemblyPath -ErrorAction SilentlyContinue }
    }
}

function ConvertTo-SPNetDiagnosticValue {
    param($Value)
    if ($null -eq $Value) { return $null }
    if ($Value -is [string] -or $Value.GetType().IsPrimitive -or $Value -is [decimal] -or $Value -is [Guid] -or $Value -is [DateTime]) { return [string]$Value }
    if ($Value -is [System.Collections.IEnumerable]) {
        $items = @()
        foreach ($item in $Value) { $items += (ConvertTo-SPNetDiagnosticValue -Value $item) }
        return $items
    }
    return [string]$Value
}

function ConvertTo-SPNetDiagnosticPropertyBag {
    param($Object)
    $properties = [ordered]@{}
    if (-not $Object) { return $properties }
    foreach ($property in ($Object.GetType().GetProperties() | Sort-Object Name)) {
        if ($property.GetIndexParameters().Length -ne 0) { continue }
        try {
            $properties[$property.Name] = ConvertTo-SPNetDiagnosticValue -Value $property.GetValue($Object, $null)
        } catch {
            $properties[$property.Name] = '<unreadable: ' + $_.Exception.Message + '>'
        }
    }
    return $properties
}

function ConvertTo-SPNetDiagnosticDictionary {
    param($Dictionary)
    $properties = [ordered]@{}
    if (-not $Dictionary) { return $properties }
    try {
        foreach ($entry in $Dictionary.GetEnumerator()) {
            $key = if ($entry.PSObject.Properties.Name -contains 'Key') { [string]$entry.Key } elseif ($entry.PSObject.Properties.Name -contains 'Name') { [string]$entry.Name } else { $null }
            if ([string]::IsNullOrWhiteSpace($key)) { continue }
            $properties[$key] = ConvertTo-SPNetDiagnosticValue -Value $entry.Value
        }
    } catch {
        $properties['<enumerationError>'] = $_.Exception.Message
    }
    return $properties
}

if (-not (Test-Path $OutputDirectory)) { New-Item -Path $OutputDirectory -ItemType Directory -Force | Out-Null }

Connect-SPNetDiagnosticsPnPOnline -Url $SiteUrl
Import-SPNetDiagnosticsWorkflowServicesCsom
$context = Get-PnPContext
if (-not $context) { throw 'PnP authenticated, but Get-PnPContext returned no client context.' }
$web = $context.Web
$context.Load($web)
$context.ExecuteQuery()
$manager = New-Object Microsoft.SharePoint.Client.WorkflowServices.WorkflowServicesManager($context, $web)
$deploymentService = $manager.GetWorkflowDeploymentService()
$subscriptionService = $manager.GetWorkflowSubscriptionService()

$definitionInfos = $deploymentService.EnumerateDefinitions($true)
$context.Load($definitionInfos)
$context.ExecuteQuery()
$matchingDefinitionInfos = @($definitionInfos | Where-Object { [string]::IsNullOrWhiteSpace($WorkflowName) -or $_.DisplayName -eq $WorkflowName -or $_.Id.ToString() -eq $WorkflowName })

$diagnostics = [ordered]@{
    SiteUrl = $SiteUrl
    WebId = $web.Id.ToString()
    WebTitle = $web.Title
    WorkflowFilter = $WorkflowName
    DefinitionCount = @($definitionInfos).Count
    MatchedDefinitionCount = @($matchingDefinitionInfos).Count
    Definitions = @()
}

foreach ($definitionInfo in $matchingDefinitionInfos) {
    $definition = $deploymentService.GetDefinition($definitionInfo.Id)
    $context.Load($definition)
    $context.ExecuteQuery()
    $safeName = [Regex]::Replace($definition.DisplayName, '[^A-Za-z0-9._-]+', '-')
    if ([string]::IsNullOrWhiteSpace($safeName)) { $safeName = $definition.Id.ToString() }
    $xamlPath = Join-Path $OutputDirectory ($safeName + '.' + $definition.Id.ToString() + '.xaml')
    Set-Content -Path $xamlPath -Value $definition.Xaml -Encoding UTF8

    $subscriptions = @()
    try {
        $subscriptionCollection = $subscriptionService.EnumerateSubscriptionsByDefinition($definition.Id)
        $context.Load($subscriptionCollection)
        $context.ExecuteQuery()
        $subscriptions = @($subscriptionCollection)
    } catch {
        $subscriptions = @(@{ Error = $_.Exception.Message })
    }

    $diagnostics.Definitions += [ordered]@{
        DefinitionInfo = ConvertTo-SPNetDiagnosticPropertyBag -Object $definitionInfo
        Definition = ConvertTo-SPNetDiagnosticPropertyBag -Object $definition
        XamlPath = $xamlPath
        XamlLength = if ($definition.Xaml) { $definition.Xaml.Length } else { 0 }
        Subscriptions = @($subscriptions | ForEach-Object {
            if ($_ -is [hashtable]) { return $_ }
            [ordered]@{
                Properties = ConvertTo-SPNetDiagnosticPropertyBag -Object $_
                PropertyDefinitions = ConvertTo-SPNetDiagnosticDictionary -Dictionary $_.PropertyDefinitions
                EventTypes = @($_.EventTypes)
            }
        })
    }
}

$jsonPath = Join-Path $OutputDirectory 'workflow-diagnostics.json'
$diagnostics | ConvertTo-Json -Depth 20 | Set-Content -Path $jsonPath -Encoding UTF8
Write-Output ('SPNET_DIAGNOSTICS ' + $jsonPath)
