param(
    [ValidateSet('Build','Export','Inspect','Publish','Download','ValidateConfig')]
    [string]$Action = 'Build',
    [string]$Workflow = 'samples\workflow.example.yml',
    [string]$XamlPath = 'artifacts\workflow.xaml',
    [string]$Out = '',
    [string]$Config = 'config\spnet.local.yml',
    [string]$CacheFolder = '',
    [string]$SiteUrl = '',
    [string]$WorkflowName = '',
    [string]$TargetType = 'Site',
    [switch]$NoBuild,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$tool = Join-Path $PSScriptRoot '..\src\SPNet.Workflow.WfSerializer\bin\Release\net48\SPNet.Workflow.WfSerializer.exe'
if (-not (Test-Path $tool)) {
    dotnet build (Join-Path $PSScriptRoot '..\src\SPNet.Workflow.WfSerializer\SPNet.Workflow.WfSerializer.csproj') -c Release
}

$common = @()
if ($Config) { $common += @('--config', $Config) }
if ($CacheFolder) { $common += @('--cache-folder', $CacheFolder) }

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
        if (-not $NoBuild -and -not $PSBoundParameters.ContainsKey('XamlPath')) {
            $XamlPath = Join-Path 'artifacts' (([IO.Path]::GetFileNameWithoutExtension($Workflow)) + '.xaml')
        }
        if (-not $NoBuild -and $Workflow) { & $tool build --workflow $Workflow --out $XamlPath @common }
        if (-not $WorkflowName) { $WorkflowName = [IO.Path]::GetFileNameWithoutExtension($XamlPath) }
        $publishArgs = @{ Action = 'Publish'; SiteUrl = $SiteUrl; WorkflowName = $WorkflowName; XamlPath = $XamlPath; TargetType = $TargetType }
        if ($DryRun) { $publishArgs.DryRun = $true }
        & (Join-Path $PSScriptRoot 'Invoke-SPNetWorkflow.ps1') @publishArgs
    }
    'Download' {
        if (-not $Out) { $Out = $XamlPath }
        & (Join-Path $PSScriptRoot 'Invoke-SPNetWorkflow.ps1') -Action Download -SiteUrl $SiteUrl -WorkflowName $WorkflowName -OutputXamlPath $Out
    }
}
