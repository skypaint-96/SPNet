<#
.SYNOPSIS
Publishes or downloads SharePoint 2013 Workflow Manager workflows for SPNet.

.DESCRIPTION
This is the intentionally explicit PowerShell boundary for live SharePoint operations. It publishes C# authored, Windows Workflow Foundation generated XAML because many SharePoint 2013/Subscription Edition farms require legacy Microsoft.SharePoint.Client.WorkflowServices assemblies and legacy WebLogin authentication.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Publish', 'Download', 'List', 'Cleanup')]
    [string]$Action,
    [Parameter(Mandatory = $true)]
    [string]$SiteUrl,
    [string]$WorkflowName,
    [string]$XamlPath,
    [string]$OutputXamlPath,
    [ValidateSet('WebLogin')]
    [string]$AuthMode = 'WebLogin',
    [ValidateSet('Site', 'List', 'Auto')]
    [string]$TargetType = 'Auto',
    [string]$TargetListTitle,
    [object]$StartManual = $true,
    [object]$StartOnCreated = $false,
    [object]$StartOnUpdated = $false,
    [string]$StatusColumn,
    [ValidateSet('Update', 'CreateNew', 'Fail')]
    [string]$IfExists = 'Update',
    [string]$ExpectedDefinitionId,
    [string]$BackupDirectory,
    [string]$WorkflowNamePrefix,
    [switch]$IncludeSubscriptions,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

function ConvertTo-SPNetBool {
    param(
        [object]$Value,
        [bool]$Default
    )
    if ($null -eq $Value) { return $Default }
    if ($Value -is [bool]) { return [bool]$Value }
    $text = ([string]$Value).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) { return $Default }
    if ($text -eq '1') { return $true }
    if ($text -eq '0') { return $false }
    $parsed = $false
    if ([bool]::TryParse($text, [ref]$parsed)) { return $parsed }
    throw "Cannot convert '$Value' to Boolean. Use true, false, 1, or 0."
}

$StartManual = ConvertTo-SPNetBool -Value $StartManual -Default $true
$StartOnCreated = ConvertTo-SPNetBool -Value $StartOnCreated -Default $false
$StartOnUpdated = ConvertTo-SPNetBool -Value $StartOnUpdated -Default $false

function Write-SPNetResult {
    param([hashtable]$Value)
    Write-Output ('SPNET_RESULT ' + ($Value | ConvertTo-Json -Compress -Depth 5))
}

function Get-PnPConnectParameters {
    $command = Get-Command Connect-PnPOnline -ErrorAction SilentlyContinue
    if (-not $command) { return $null }
    return $command.Parameters.Keys
}

function Connect-SPNetPnPOnline {
    param([string]$Url, [string]$Mode)
    $parameters = Get-PnPConnectParameters
    if (-not $parameters) { throw 'Connect-PnPOnline is not available. Install/import a PnP PowerShell module compatible with this SharePoint farm.' }
    $supportsUseWebLogin = $parameters -contains 'UseWebLogin'
    if (-not $supportsUseWebLogin) { throw 'Connect-PnPOnline -UseWebLogin is unavailable. Install/import a legacy PnP PowerShell module that supports WebLogin.' }
    Connect-PnPOnline -Url $Url -UseWebLogin
}

function Import-SPNetWorkflowServicesCsom {
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

Connect-SPNetPnPOnline -Url $SiteUrl -Mode $AuthMode
Import-SPNetWorkflowServicesCsom
$context = Get-PnPContext
if (-not $context) { throw 'PnP authenticated, but Get-PnPContext returned no client context.' }
$web = $context.Web
$context.Load($web)
$context.ExecuteQuery()
$manager = New-Object Microsoft.SharePoint.Client.WorkflowServices.WorkflowServicesManager($context, $web)
$deploymentService = $manager.GetWorkflowDeploymentService()
$subscriptionService = $manager.GetWorkflowSubscriptionService()

function Get-SPNetWorkflowDefinitionsByName {
    param([string]$Name)
    try {
        $definitions = $deploymentService.EnumerateDefinitions($true)
        $context.Load($definitions)
        $context.ExecuteQuery()
        return @($definitions | Where-Object { $_ -and $_.DisplayName -eq $Name })
    } catch {
        if ($_.Exception.Message -notlike '*DisplayName*') { throw }
        Write-Verbose ('Unable to enumerate existing workflow definitions by DisplayName; continuing as no match for new workflow name: ' + $_.Exception.Message)
        return @()
    }
}

function Get-SPNetWorkflowDefinitionsByFilter {
    param([string]$Name, [string]$Prefix)
    if ([string]::IsNullOrWhiteSpace($Name) -and [string]::IsNullOrWhiteSpace($Prefix)) { throw '-WorkflowName or -WorkflowNamePrefix is required for List/Cleanup.' }
    $definitions = $deploymentService.EnumerateDefinitions($true)
    $context.Load($definitions)
    $context.ExecuteQuery()
    @($definitions | Where-Object {
        $_ -and (
            (-not [string]::IsNullOrWhiteSpace($Name) -and $_.DisplayName -eq $Name) -or
            (-not [string]::IsNullOrWhiteSpace($Prefix) -and $_.DisplayName -like ($Prefix + '*'))
        )
    })
}

function Get-SPNetWorkflowSubscriptionsForDefinition {
    param($DefinitionInfo)
    try {
        $subscriptionCollection = $subscriptionService.EnumerateSubscriptionsByDefinition($DefinitionInfo.Id)
        $context.Load($subscriptionCollection)
        $context.ExecuteQuery()
        return @($subscriptionCollection)
    } catch {
        Write-Verbose ('Unable to enumerate subscriptions for workflow definition ' + $DefinitionInfo.Id + ': ' + $_.Exception.Message)
        return @()
    }
}

function Convert-SPNetWorkflowDefinitionToResult {
    param($DefinitionInfo, [switch]$WithSubscriptions)
    $subscriptions = if ($WithSubscriptions) { Get-SPNetWorkflowSubscriptionsForDefinition -DefinitionInfo $DefinitionInfo } else { @() }
    @{
        Name = $DefinitionInfo.DisplayName
        DefinitionId = $DefinitionInfo.Id.ToString()
        Published = $DefinitionInfo.Published
        RestrictToType = $DefinitionInfo.RestrictToType
        RestrictToScope = $DefinitionInfo.RestrictToScope
        SubscriptionIds = @($subscriptions | ForEach-Object { $_.Id.ToString() })
        SubscriptionCount = $subscriptions.Count
    }
}

function New-SPNetSafeBackupPath {
    param([string]$Name, [string]$Directory)
    $safeName = [Regex]::Replace($Name, '[^A-Za-z0-9._-]+', '-')
    if ([string]::IsNullOrWhiteSpace($safeName)) { $safeName = 'workflow' }
    $root = if ([string]::IsNullOrWhiteSpace($Directory)) { Join-Path (Join-Path (Get-Location) 'artifacts') 'backups' } else { $Directory }
    if (-not (Test-Path $root)) { New-Item -Path $root -ItemType Directory -Force | Out-Null }
    Join-Path $root ($safeName + '-backup-' + (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N') + '.xaml')
}

function Convert-SPNetSubscriptionPropertyDefinitions {
    param($Subscription)
    $properties = @{}
    if (-not $Subscription) { return $properties }

    # WorkflowSubscription.GetProperty() is not present in some legacy CSOM assemblies.
    # Read the subscription property bag through PropertyDefinitions when available and
    # keep this helper tolerant of missing/unloaded members across farm-compatible builds.
    $propertyDefinitions = $null
    try {
        $propertyDefinitions = $Subscription.PropertyDefinitions
    } catch {
        Write-Verbose ('Unable to read WorkflowSubscription.PropertyDefinitions: ' + $_.Exception.Message)
        return $properties
    }
    if (-not $propertyDefinitions) { return $properties }

    try {
        foreach ($entry in $propertyDefinitions.GetEnumerator()) {
            $key = $null
            $value = $null
            if ($entry.PSObject.Properties.Name -contains 'Key') {
                $key = [string]$entry.Key
                $value = $entry.Value
            } elseif ($entry.PSObject.Properties.Name -contains 'Name') {
                $key = [string]$entry.Name
                $value = $entry.Value
            }
            if (-not [string]::IsNullOrWhiteSpace($key) -and -not $properties.ContainsKey($key)) {
                $properties[$key] = $value
            }
        }
    } catch {
        Write-Verbose ('Unable to enumerate WorkflowSubscription.PropertyDefinitions: ' + $_.Exception.Message)
    }

    if ($properties.Count -gt 0) {
        Write-Verbose ('WorkflowSubscription property definitions: ' + (($properties.Keys | Sort-Object) -join ', '))
    }
    return $properties
}

function Get-SPNetSubscriptionMetadataValue {
    param($Subscription, [hashtable]$PropertyDefinitions, [string]$Name)
    if ([string]::IsNullOrWhiteSpace($Name)) { return $null }
    if ($PropertyDefinitions -and $PropertyDefinitions.ContainsKey($Name)) { return $PropertyDefinitions[$Name] }

    if ($Subscription) {
        $realProperty = $Subscription.GetType().GetProperty($Name)
        if ($realProperty) {
            try { return $realProperty.GetValue($Subscription, $null) } catch { Write-Verbose ("Unable to read WorkflowSubscription.${Name}: " + $_.Exception.Message) }
        }
    }
    return $null
}

function Remove-SPNetWorkflowDefinitionAndSubscriptions {
    param($DefinitionInfo)
    $subscriptions = Get-SPNetWorkflowSubscriptionsForDefinition -DefinitionInfo $DefinitionInfo
    foreach ($existingSubscription in $subscriptions) {
        $subscriptionService.DeleteSubscription($existingSubscription.Id)
        $context.ExecuteQuery()
    }
    $deploymentService.DeleteDefinition($DefinitionInfo.Id)
    $context.ExecuteQuery()
    $subscriptions
}

if ($Action -eq 'List') {
    $matches = Get-SPNetWorkflowDefinitionsByFilter -Name $WorkflowName -Prefix $WorkflowNamePrefix
    Write-SPNetResult @{ Action = 'List'; WorkflowName = $WorkflowName; WorkflowNamePrefix = $WorkflowNamePrefix; Count = $matches.Count; Workflows = @($matches | ForEach-Object { Convert-SPNetWorkflowDefinitionToResult -DefinitionInfo $_ -WithSubscriptions:$IncludeSubscriptions }) }
    return
}

if ($Action -eq 'Cleanup') {
    if (-not $Force) { throw 'Cleanup is guarded. Re-run with -Force after first running -Action List with the same exact name/prefix filter.' }
    if ([string]::IsNullOrWhiteSpace($WorkflowName) -and ([string]::IsNullOrWhiteSpace($WorkflowNamePrefix) -or $WorkflowNamePrefix.Length -lt 8)) { throw 'Cleanup by prefix requires -WorkflowNamePrefix of at least 8 characters, or use an exact -WorkflowName.' }
    $matches = Get-SPNetWorkflowDefinitionsByFilter -Name $WorkflowName -Prefix $WorkflowNamePrefix
    if ($matches.Count -eq 0) { Write-SPNetResult @{ Action = 'Cleanup'; WorkflowName = $WorkflowName; WorkflowNamePrefix = $WorkflowNamePrefix; Count = 0; Deleted = @(); Status = 'NoMatches' }; return }
    $deleted = @()
    foreach ($match in $matches) {
        $subscriptions = Remove-SPNetWorkflowDefinitionAndSubscriptions -DefinitionInfo $match
        $deleted += @{ Name = $match.DisplayName; DefinitionId = $match.Id.ToString(); SubscriptionIds = @($subscriptions | ForEach-Object { $_.Id.ToString() }) }
    }
    Write-SPNetResult @{ Action = 'Cleanup'; WorkflowName = $WorkflowName; WorkflowNamePrefix = $WorkflowNamePrefix; Count = $deleted.Count; Deleted = $deleted; Status = 'Deleted' }
    return
}

if ($Action -eq 'Download') {
    if ([string]::IsNullOrWhiteSpace($WorkflowName)) { throw '-WorkflowName is required for Download.' }
    if ([string]::IsNullOrWhiteSpace($OutputXamlPath)) { throw '-OutputXamlPath is required for Download.' }
    $definitions = $deploymentService.EnumerateDefinitions($true)
    $context.Load($definitions)
    $context.ExecuteQuery()
    $definitionInfo = $definitions | Where-Object { $_.DisplayName -eq $WorkflowName -or $_.Id.ToString() -eq $WorkflowName } | Select-Object -First 1
    if (-not $definitionInfo) { throw "Workflow definition '$WorkflowName' was not found." }
    $definition = $deploymentService.GetDefinition($definitionInfo.Id)
    $context.Load($definition)
    $context.ExecuteQuery()
    Set-Content -Path $OutputXamlPath -Value $definition.Xaml -Encoding UTF8
    $subscription = $null
    try {
        $subscriptions = $subscriptionService.EnumerateSubscriptionsByDefinition($definition.Id)
        $context.Load($subscriptions)
        $context.ExecuteQuery()
        $subscription = $subscriptions | Select-Object -First 1
    } catch { $subscription = $null }
    $subscriptionProperties = Convert-SPNetSubscriptionPropertyDefinitions -Subscription $subscription
    $eventTypeProperty = Get-SPNetSubscriptionMetadataValue -Subscription $subscription -PropertyDefinitions $subscriptionProperties -Name 'WSEventType'
    if ([string]::IsNullOrWhiteSpace([string]$eventTypeProperty)) { $eventTypeProperty = Get-SPNetSubscriptionMetadataValue -Subscription $subscription -PropertyDefinitions $subscriptionProperties -Name 'SharePointWorkflowContext.Subscription.EventType' }
    $eventTypes = if ($subscription -and $subscription.EventTypes) { @($subscription.EventTypes) } else { @() }
    $manual = ($eventTypes -contains 'WorkflowStart') -or ($eventTypeProperty -like '*WorkflowStart*')
    $created = ($eventTypes -contains 'ItemAdded') -or ($eventTypes -contains 'WorkflowStartOnCreate') -or ($eventTypeProperty -like '*ItemAdded*') -or ($eventTypeProperty -like '*WorkflowStartOnCreate*')
    $updated = ($eventTypes -contains 'ItemUpdated') -or ($eventTypes -contains 'WorkflowStartOnChange') -or ($eventTypeProperty -like '*ItemUpdated*') -or ($eventTypeProperty -like '*WorkflowStartOnChange*')
    $targetType = if ($definition.RestrictToType) { $definition.RestrictToType } else { 'Site' }
    $targetListName = $null
    $status = $null
    if ($subscription) {
        $status = Get-SPNetSubscriptionMetadataValue -Subscription $subscription -PropertyDefinitions $subscriptionProperties -Name 'StatusFieldName'
        $targetListName = Get-SPNetSubscriptionMetadataValue -Subscription $subscription -PropertyDefinitions $subscriptionProperties -Name 'Microsoft.SharePoint.ActivationProperties.ListName'
    }
    Write-SPNetResult @{ Action = 'Download'; WorkflowName = $definition.DisplayName; DefinitionId = $definition.Id.ToString(); SubscriptionId = if ($subscription) { $subscription.Id.ToString() } else { $null }; XamlPath = $OutputXamlPath; TargetType = $targetType; TargetListTitle = $targetListName; StartManual = [bool]$manual; StartOnCreated = [bool]$created; StartOnUpdated = [bool]$updated; StatusColumn = $status }
    return
}

if ([string]::IsNullOrWhiteSpace($XamlPath)) { throw '-XamlPath is required for Publish.' }
if ([string]::IsNullOrWhiteSpace($WorkflowName)) { throw '-WorkflowName is required for Publish.' }
$xamlContent = Get-Content -Path $XamlPath -Raw
$existingDefinitions = Get-SPNetWorkflowDefinitionsByName -Name $WorkflowName
if ($existingDefinitions.Count -gt 0 -and $IfExists -eq 'Fail') {
    Write-SPNetResult @{ Action = 'Publish'; WorkflowName = $WorkflowName; ExistingDefinitionIds = @($existingDefinitions | ForEach-Object { $_.Id.ToString() }); Status = 'FailedBeforeCreate'; ErrorCode = 'WorkflowExists'; ErrorMessage = "Workflow '$WorkflowName' already exists." }
    throw "Workflow '$WorkflowName' already exists. Use -IfExists Update or -IfExists CreateNew."
}
if ($existingDefinitions.Count -gt 0 -and $IfExists -eq 'Update') {
    if ($existingDefinitions.Count -gt 1) {
        Write-SPNetResult @{ Action = 'Publish'; WorkflowName = $WorkflowName; ExistingDefinitionIds = @($existingDefinitions | ForEach-Object { $_.Id.ToString() }); Status = 'FailedBeforeCreate'; ErrorCode = 'AmbiguousWorkflowName'; ErrorMessage = "Multiple workflows named '$WorkflowName' exist. Refusing guarded update." }
        throw "Multiple workflows named '$WorkflowName' exist. Refusing guarded update."
    }
    if ([string]::IsNullOrWhiteSpace($ExpectedDefinitionId)) {
        Write-SPNetResult @{ Action = 'Publish'; WorkflowName = $WorkflowName; ExistingDefinitionIds = @($existingDefinitions | ForEach-Object { $_.Id.ToString() }); Status = 'FailedBeforeCreate'; ErrorCode = 'MissingDefinitionId'; ErrorMessage = "Workflow '$WorkflowName' already exists. -ExpectedDefinitionId is required for guarded update." }
        throw "Workflow '$WorkflowName' already exists. -ExpectedDefinitionId is required for guarded update."
    }
    $existingDefinition = $existingDefinitions | Select-Object -First 1
    $existingDefinitionId = $existingDefinition.Id.ToString()
    if ($existingDefinitionId -ne $ExpectedDefinitionId) {
        Write-SPNetResult @{ Action = 'Publish'; WorkflowName = $WorkflowName; ExistingDefinitionIds = @($existingDefinitionId); ExpectedDefinitionId = $ExpectedDefinitionId; Status = 'FailedBeforeCreate'; ErrorCode = 'DefinitionIdMismatch'; ErrorMessage = "Workflow '$WorkflowName' online definition ID '$existingDefinitionId' does not match expected definition ID '$ExpectedDefinitionId'." }
        throw "Workflow '$WorkflowName' online definition ID '$existingDefinitionId' does not match expected definition ID '$ExpectedDefinitionId'."
    }
    $fullDefinition = $deploymentService.GetDefinition($existingDefinition.Id)
    $context.Load($fullDefinition)
    $context.ExecuteQuery()
    $backupPath = New-SPNetSafeBackupPath -Name $WorkflowName -Directory $BackupDirectory
    Set-Content -Path $backupPath -Value $fullDefinition.Xaml -Encoding UTF8
    if (-not (Test-Path $backupPath)) { throw "Backup was not created at '$backupPath'. Refusing delete/recreate update." }
    $oldSubscriptions = Remove-SPNetWorkflowDefinitionAndSubscriptions -DefinitionInfo $existingDefinition
    $script:spnetOldDefinitionId = $existingDefinitionId
    $script:spnetOldSubscriptionId = if ($oldSubscriptions.Count -gt 0) { $oldSubscriptions[0].Id.ToString() } else { $null }
    $script:spnetBackupPath = $backupPath
}
$definition = New-Object Microsoft.SharePoint.Client.WorkflowServices.WorkflowDefinition($context)
$definition.DisplayName = $WorkflowName
$definition.Description = 'SPNet C# authored workflow.'
$definition.Xaml = $xamlContent
$effectiveTargetType = if ($TargetType -eq 'Auto') { 'Site' } else { $TargetType }
$targetList = $null
if ($effectiveTargetType -eq 'List') {
    if ([string]::IsNullOrWhiteSpace($TargetListTitle)) { throw '-TargetListTitle is required for list workflow publication.' }
    $targetList = $web.Lists.GetByTitle($TargetListTitle)
    $context.Load($targetList)
    $context.ExecuteQuery()
    $definition.RestrictToType = 'List'
    $definition.RestrictToScope = $targetList.Id.ToString()
} else {
    $definition.RestrictToType = 'Site'
    $definition.RestrictToScope = $web.Id.ToString()
}
$definition.RequiresAssociationForm = $false
$definition.RequiresInitiationForm = $false
$definitionId = $null
$subscriptionId = $null
try {
    $saveResult = $deploymentService.SaveDefinition($definition)
    $context.ExecuteQuery()
    $definitionId = $saveResult.Value
    $deploymentService.PublishDefinition($definitionId)
    $context.ExecuteQuery()
} catch {
    $partial = @{ Action = 'Publish'; WorkflowName = $WorkflowName; DefinitionId = if ($definitionId) { $definitionId.ToString() } else { $null }; TargetType = $effectiveTargetType; Status = if ($definitionId) { 'PartialDefinitionSaved' } else { 'FailedBeforeCreate' }; ErrorCode = $_.Exception.GetType().Name; ErrorMessage = $_.Exception.Message }
    Write-SPNetResult $partial
    throw
}
$subscription = New-Object Microsoft.SharePoint.Client.WorkflowServices.WorkflowSubscription($context)
$subscription.Name = $WorkflowName
$subscription.DefinitionId = $definitionId
$subscription.Enabled = $true
$subscription.ManualStartBypassesActivationLimit = $true
$subscription.EventTypes = New-Object 'System.Collections.Generic.List[string]'
$eventTypeTokens = @()
if ($StartManual) { [void]$subscription.EventTypes.Add('WorkflowStart'); $eventTypeTokens += 'WorkflowStart' }
if ($StartOnCreated) { [void]$subscription.EventTypes.Add('ItemAdded'); $eventTypeTokens += 'ItemAdded' }
if ($StartOnUpdated) { [void]$subscription.EventTypes.Add('ItemUpdated'); $eventTypeTokens += 'ItemUpdated' }
if ($eventTypeTokens.Count -eq 0) { [void]$subscription.EventTypes.Add('WorkflowStart'); $eventTypeTokens += 'WorkflowStart' }
$eventTypeValue = (($eventTypeTokens | ForEach-Object { $_ + '#;' }) -join '')
$subscription.SetProperty('WSEventType', $eventTypeValue)
$subscription.SetProperty('SharePointWorkflowContext.Subscription.EventType', $eventTypeValue)
$subscription.SetProperty('WSDisplayName', $WorkflowName)
$subscription.SetProperty('WSEnabled', 'true')
$subscription.SetProperty('WSPublishState', '3')
$subscription.SetProperty('CreatedBySPD', '1')
$subscription.SetProperty('CurrentWebUri', $SiteUrl)
if ($effectiveTargetType -eq 'List') {
    $subscription.EventSourceId = $targetList.Id
    $subscription.StatusFieldName = if ([string]::IsNullOrWhiteSpace($StatusColumn)) { $WorkflowName } else { $StatusColumn }
    $subscription.SetProperty('Microsoft.SharePoint.ActivationProperties.ListId', $targetList.Id.ToString())
    $subscription.SetProperty('Microsoft.SharePoint.ActivationProperties.ListName', $targetList.Title)
    $subscriptionResult = $subscriptionService.PublishSubscriptionForList($subscription, $targetList.Id)
} else {
    $subscription.EventSourceId = $web.Id
    $subscriptionResult = $subscriptionService.PublishSubscription($subscription)
}
try {
    $context.ExecuteQuery()
    $subscriptionId = $subscriptionResult.Value
} catch {
    Write-SPNetResult @{ Action = 'Publish'; WorkflowName = $WorkflowName; DefinitionId = $definitionId.ToString(); SubscriptionId = $null; TargetType = $effectiveTargetType; Status = 'PartialDefinitionPublished'; ErrorCode = $_.Exception.GetType().Name; ErrorMessage = $_.Exception.Message }
    throw
}
Write-SPNetResult @{ Action = 'Publish'; WorkflowName = $WorkflowName; DefinitionId = $definitionId.ToString(); SubscriptionId = $subscriptionId.ToString(); OldDefinitionId = $script:spnetOldDefinitionId; OldSubscriptionId = $script:spnetOldSubscriptionId; BackupPath = $script:spnetBackupPath; TargetType = $effectiveTargetType; StartManual = $StartManual; StartOnCreated = $StartOnCreated; StartOnUpdated = $StartOnUpdated; StatusColumn = if ($effectiveTargetType -eq 'List') { $subscription.StatusFieldName } else { $null }; Status = if ($script:spnetOldDefinitionId) { 'UpdatedByBackupDeleteRecreate' } else { 'Published' } }
