param(
    [ValidateSet('Build','Export','Inspect','Publish','Download','List','Cleanup','ValidateConfig')]
    [string]$Action = 'Build',
    [string]$Workflow = 'samples\workflow.example.yml',
    [string]$XamlPath = 'artifacts\workflow.xaml',
    [string]$Out = '',
    [string]$Config = 'config\spnet.local.yml',
    [string]$CacheFolder = '',
    [string]$SiteUrl = '',
    [string]$WorkflowName = '',
    [string]$WorkflowNamePrefix = '',
    [string]$TargetType = 'Site',
    [string]$TargetListTitle = '',
    [object]$StartManual = $null,
    [object]$StartOnCreated = $null,
    [object]$StartOnUpdated = $null,
    [string]$StatusColumn = '',
    [ValidateSet('Update', 'CreateNew', 'Fail')]
    [string]$IfExists = 'Update',
    [string]$ExpectedDefinitionId = '',
    [string]$BackupDirectory = '',
    [switch]$NoBuild,
    [switch]$DryRun,
    [switch]$IncludeSubscriptions,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$tool = Join-Path $PSScriptRoot '..\src\SPNet.Workflow.WfSerializer\bin\Release\net48\SPNet.Workflow.WfSerializer.exe'
if (-not (Test-Path $tool)) {
    dotnet build (Join-Path $PSScriptRoot '..\src\SPNet.Workflow.WfSerializer\SPNet.Workflow.WfSerializer.csproj') -c Release
}

$common = @()
if ($Config) { $common += @('--config', $Config) }
if ($CacheFolder) { $common += @('--cache-folder', $CacheFolder) }

function Get-SPNetYamlScalar {
    param([string[]]$Lines, [string]$Name)
    $match = $Lines | Select-String -Pattern ('^\s*' + [regex]::Escape($Name) + '\s*:\s*[''\"]?(.*?)[''\"]?\s*$') | Select-Object -First 1
    if ($match) { return $match.Matches[0].Groups[1].Value.Trim() }
    return ''
}

function ConvertTo-SPNetYamlBoolText {
    param([object]$Value, [string]$Default)
    if ($null -eq $Value) { return $Default }
    $text = ([string]$Value).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) { return $Default }
    if ($text -eq '1') { return 'true' }
    if ($text -eq '0') { return 'false' }
    $parsed = $false
    if ([bool]::TryParse($text, [ref]$parsed)) { return $parsed.ToString().ToLowerInvariant() }
    throw "Cannot convert '$Value' to Boolean. Use true, false, 1, or 0."
}

switch ($Action) {
    'Build' { & $tool build --workflow $Workflow --out $XamlPath @common }
    'ValidateConfig' {
        $candidateCacheFolder = $CacheFolder
        if (-not $candidateCacheFolder -and (Test-Path $Config)) {
            $match = Select-String -Path $Config -Pattern '^\s*spdCacheFolder\s*:\s*"?(.*?)"?\s*$' | Select-Object -First 1
            if ($match) { $candidateCacheFolder = $match.Matches[0].Groups[1].Value.Trim() }
        }
        if (-not $candidateCacheFolder) { $candidateCacheFolder = $env:SPNET_SPD_CACHE }
        if (-not $candidateCacheFolder) { throw 'Missing SharePoint Designer WebsiteCache folder. Set -CacheFolder, config spdCacheFolder, or SPNET_SPD_CACHE.' }
        if (-not (Test-Path $candidateCacheFolder -PathType Container)) { throw "SPD cache folder not found: $candidateCacheFolder" }
        foreach ($dll in @('Microsoft.SharePoint.WorkflowServices.Activities.Proxy.dll', 'Microsoft.Activities.Proxy.dll')) {
            $path = Join-Path $candidateCacheFolder $dll
            if (-not (Test-Path $path -PathType Leaf)) { throw "Required WebsiteCache proxy DLL not found: $path" }
        }
        Write-Host "SPNet YAML workflow config is valid. SPD cache: $candidateCacheFolder"
    }
    'Export' { $target = if ($Out) { $Out } else { $XamlPath -replace '\.xaml$', '.exported.yml' }; & $tool export --xaml $XamlPath --out $target }
    'Inspect' { & $tool inspect --xaml $XamlPath @common }
    'Publish' {
        $workflowLines = @()
        if ($Workflow -and (Test-Path $Workflow)) { $workflowLines = Get-Content -Path $Workflow }
        if (-not $NoBuild -and -not $PSBoundParameters.ContainsKey('XamlPath')) {
            $XamlPath = Join-Path 'artifacts' (([IO.Path]::GetFileNameWithoutExtension($Workflow)) + '.xaml')
        }
        if (-not $NoBuild -and $Workflow) { & $tool build --workflow $Workflow --out $XamlPath @common }
        if (-not $WorkflowName) { $WorkflowName = [IO.Path]::GetFileNameWithoutExtension($XamlPath) }
        if (-not $PSBoundParameters.ContainsKey('TargetType') -and $workflowLines.Count -gt 0) { $TargetType = Get-SPNetYamlScalar -Lines $workflowLines -Name 'type' }
        if ([string]::IsNullOrWhiteSpace($TargetType)) { $TargetType = 'Site' }
        if ([string]::IsNullOrWhiteSpace($TargetListTitle) -and $workflowLines.Count -gt 0) { $TargetListTitle = Get-SPNetYamlScalar -Lines $workflowLines -Name 'listTitle' }
        $defaultStartManual = 'true'
        $defaultStartCreated = 'false'
        $defaultStartUpdated = 'false'
        if ($workflowLines.Count -gt 0) {
            $defaultStartManual = Get-SPNetYamlScalar -Lines $workflowLines -Name 'manual'
            $defaultStartCreated = Get-SPNetYamlScalar -Lines $workflowLines -Name 'autoStartCreate'
            $defaultStartUpdated = Get-SPNetYamlScalar -Lines $workflowLines -Name 'autoStartChange'
        }
        $startManualText = ConvertTo-SPNetYamlBoolText -Value $StartManual -Default $defaultStartManual
        $startCreatedText = ConvertTo-SPNetYamlBoolText -Value $StartOnCreated -Default $defaultStartCreated
        $startUpdatedText = ConvertTo-SPNetYamlBoolText -Value $StartOnUpdated -Default $defaultStartUpdated
        if ([string]::IsNullOrWhiteSpace($startManualText)) { $startManualText = 'true' }
        if ([string]::IsNullOrWhiteSpace($startCreatedText)) { $startCreatedText = 'false' }
        if ([string]::IsNullOrWhiteSpace($startUpdatedText)) { $startUpdatedText = 'false' }
        $publishArgs = @{ Action = 'Publish'; SiteUrl = $SiteUrl; WorkflowName = $WorkflowName; XamlPath = $XamlPath; TargetType = $TargetType; StartManual = $startManualText; StartOnCreated = $startCreatedText; StartOnUpdated = $startUpdatedText; IfExists = $IfExists }
        if (-not [string]::IsNullOrWhiteSpace($TargetListTitle)) { $publishArgs.TargetListTitle = $TargetListTitle }
        if (-not [string]::IsNullOrWhiteSpace($StatusColumn)) { $publishArgs.StatusColumn = $StatusColumn }
        if (-not [string]::IsNullOrWhiteSpace($ExpectedDefinitionId)) { $publishArgs.ExpectedDefinitionId = $ExpectedDefinitionId }
        if (-not [string]::IsNullOrWhiteSpace($BackupDirectory)) { $publishArgs.BackupDirectory = $BackupDirectory }
        if ($DryRun) { $publishArgs.DryRun = $true }
        & (Join-Path $PSScriptRoot 'Invoke-SPNetWorkflow.ps1') @publishArgs
    }
    'Download' {
        if (-not $Out) { $Out = $XamlPath }
        & (Join-Path $PSScriptRoot 'Invoke-SPNetWorkflow.ps1') -Action Download -SiteUrl $SiteUrl -WorkflowName $WorkflowName -OutputXamlPath $Out
    }
    'List' {
        $listArgs = @{ Action = 'List'; SiteUrl = $SiteUrl }
        if (-not [string]::IsNullOrWhiteSpace($WorkflowName)) { $listArgs.WorkflowName = $WorkflowName }
        if (-not [string]::IsNullOrWhiteSpace($WorkflowNamePrefix)) { $listArgs.WorkflowNamePrefix = $WorkflowNamePrefix }
        if ($IncludeSubscriptions) { $listArgs.IncludeSubscriptions = $true }
        & (Join-Path $PSScriptRoot 'Invoke-SPNetWorkflow.ps1') @listArgs
    }
    'Cleanup' {
        $cleanupArgs = @{ Action = 'Cleanup'; SiteUrl = $SiteUrl }
        if (-not [string]::IsNullOrWhiteSpace($WorkflowName)) { $cleanupArgs.WorkflowName = $WorkflowName }
        if (-not [string]::IsNullOrWhiteSpace($WorkflowNamePrefix)) { $cleanupArgs.WorkflowNamePrefix = $WorkflowNamePrefix }
        if ($Force) { $cleanupArgs.Force = $true }
        & (Join-Path $PSScriptRoot 'Invoke-SPNetWorkflow.ps1') @cleanupArgs
    }
}
