<#
.SYNOPSIS
Builds, inspects, publishes, downloads, lists, and cleans up SPNet YAML-authored SharePoint workflows.

.DESCRIPTION
YAML is the authoring source of truth. The Build and Publish actions compile YAML to SharePoint Designer-compatible XAML and generate a `*.xaml.metadata.json` sidecar from effective YAML metadata/defaults. That metadata JSON is the normal publish contract for display name, technical name, description, target, start options, initiation settings, and form fields.

Packaged usage should prefer the primary `scripts\spnet-workflow.ps1` command. This script remains as a compatibility wrapper and implementation boundary for YAML workflow orchestration.

The normal YAML publish flow is YAML -> XAML + metadata JSON -> publish with metadata JSON -> download XAML + metadata JSON. Legacy `*.xaml.formfield.xml` may still be emitted or downloaded for compatibility/inspection, but it is not the normal publish input.

.PARAMETER MetadataJsonPath
Explicit metadata JSON sidecar to use for Publish. If omitted, Publish requires and auto-discovers `-XamlPath + '.metadata.json'` after Build or beside an existing XAML when `-NoBuild` is used.

.PARAMETER FormFieldXmlPath
Deprecated for Publish. Passed only as an explicit fallback when metadata JSON cannot supply initiation form fields. For Export, this can still point at a legacy/downloaded FormField XML sidecar for compatibility inspection.

.PARAMETER IfExists
Publish conflict policy passed through to the live publisher. Fail is the default create behavior and checks before creating anything, Update is an explicit update path, and CreateNew intentionally creates a unique same-base-name workflow when needed.
#>
param(
    [ValidateSet('Build','Export','Inspect','Publish','Download','List','Cleanup','ValidateConfig')]
    [string]$Action = 'Build',
    [string]$Workflow = 'samples\workflow.example.yml',
    [string]$XamlPath = 'artifacts\workflow.xaml',
    [string]$FormFieldXmlPath = '',
    [string]$MetadataJsonPath = '',
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
    [string]$IfExists = 'Fail',
    [ValidateSet('WebLogin','CookieHeader','WindowsDefault','Credentials')]
    [string]$AuthMode = 'WebLogin',
    [string]$PublisherExePath = '',
    [string]$PublisherCookieHeader = '',
    [string]$PublisherUsername = '',
    [string]$PublisherPassword = '',
    [string]$PublisherDomain = '',
    [string]$ExpectedDefinitionId = '',
    [string]$BackupDirectory = '',
    [switch]$NoBuild,
    [switch]$DryRun,
    [switch]$IncludeSubscriptions,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

function Write-SpNetWrapperError {
    param([string]$Code, [string]$Message, [string]$Hint = '', [string]$Path = '')
    Write-Error "SPNET_ERROR [$Code] $Message" -ErrorAction Continue
    if (-not [string]::IsNullOrWhiteSpace($Path)) { Write-Error "  path: $Path" -ErrorAction Continue }
    if (-not [string]::IsNullOrWhiteSpace($Hint)) { Write-Error "  remediation: $Hint" -ErrorAction Continue }
}

function Throw-SpNetWrapperError {
    param([string]$Code, [string]$Message, [string]$Hint = '', [string]$Path = '')
    $exception = New-Object System.InvalidOperationException($Message)
    $record = New-Object System.Management.Automation.ErrorRecord($exception, $Code, [System.Management.Automation.ErrorCategory]::InvalidOperation, $Path)
    if (-not [string]::IsNullOrWhiteSpace($Hint)) { $record.ErrorDetails = New-Object System.Management.Automation.ErrorDetails("$Message`nRemediation: $Hint") }
    throw $record
}

function Resolve-SPNetSerializerToolPath {
    $candidates = @(
        (Join-Path $PSScriptRoot '..\tools\SPNet.Workflow.WfSerializer\SPNet.Workflow.WfSerializer.exe'),
        (Join-Path $PSScriptRoot 'tools\SPNet.Workflow.WfSerializer\SPNet.Workflow.WfSerializer.exe'),
        (Join-Path $PSScriptRoot '..\src\SPNet.Workflow.WfSerializer\bin\Release\net48\SPNet.Workflow.WfSerializer.exe')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate -PathType Leaf) { return [IO.Path]::GetFullPath($candidate) }
    }
    return $null
}

$tool = Resolve-SPNetSerializerToolPath
if ([string]::IsNullOrWhiteSpace($tool) -or -not (Test-Path $tool)) {
    $serializerProject = Join-Path $PSScriptRoot '..\src\SPNet.Workflow.WfSerializer\SPNet.Workflow.WfSerializer.csproj'
    if (-not (Test-Path $serializerProject -PathType Leaf)) {
        Throw-SpNetWrapperError -Code 'SPNET-YAML-SERIALIZER-001' -Message 'Serializer executable was not found and no source fallback project exists.' -Path $tool -Hint 'Use a complete SPNet package, or run from a repository checkout containing src\SPNet.Workflow.WfSerializer.'
    }
    dotnet build $serializerProject -c Release
    if ($LASTEXITCODE -ne 0) { Throw-SpNetWrapperError -Code 'SPNET-YAML-SERIALIZER-002' -Message "Serializer build failed with exit code $LASTEXITCODE." -Path $serializerProject -Hint 'Fix the serializer project build or package with prebuilt tools.' }
    $tool = Join-Path $PSScriptRoot '..\src\SPNet.Workflow.WfSerializer\bin\Release\net48\SPNet.Workflow.WfSerializer.exe'
    if (-not (Test-Path $tool -PathType Leaf)) { Throw-SpNetWrapperError -Code 'SPNET-YAML-SERIALIZER-003' -Message 'Serializer build completed but executable was not found.' -Path $tool -Hint 'Check build output and target framework net48.' }
}

function Get-SPNetYamlScalar {
    param([string[]]$Lines, [string]$Name)
    $match = $Lines | Select-String -Pattern ('^' + [regex]::Escape($Name) + '\s*:\s*[''\"]?(.*?)[''\"]?\s*$') | Select-Object -First 1
    if ($match) { return $match.Matches[0].Groups[1].Value.Trim() }
    return ''
}

function Get-SPNetConfigScalar {
    param([string]$Path, [string]$Name)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path $Path -PathType Leaf)) { return '' }
    $lines = Get-Content -Path $Path
    return Get-SPNetYamlScalar -Lines $lines -Name $Name
}

function Get-SPNetWorkflowMetadataJsonPath {
    param([string]$XamlFilePath, [string]$ExplicitMetadataJsonPath)
    if (-not [string]::IsNullOrWhiteSpace($ExplicitMetadataJsonPath)) {
        if (-not (Test-Path $ExplicitMetadataJsonPath -PathType Leaf)) { throw "Metadata JSON file not found: $ExplicitMetadataJsonPath" }
        return $ExplicitMetadataJsonPath
    }
    if ([string]::IsNullOrWhiteSpace($XamlFilePath)) { return '' }
    $sidecarPath = $XamlFilePath + '.metadata.json'
    if (-not (Test-Path $sidecarPath -PathType Leaf)) { throw "Metadata JSON sidecar not found: $sidecarPath. Build the YAML workflow before publishing." }
    return $sidecarPath
}

function Read-SPNetWorkflowMetadataJson {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    return Get-Content -Path $Path -Raw | ConvertFrom-Json
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

if ([string]::IsNullOrWhiteSpace($SiteUrl)) { $SiteUrl = Get-SPNetConfigScalar -Path $Config -Name 'siteUrl' }
if ([string]::IsNullOrWhiteSpace($CacheFolder)) { $CacheFolder = Get-SPNetConfigScalar -Path $Config -Name 'spdCacheFolder' }

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
    'Export' {
        $target = if ($Out) { $Out } else { $XamlPath -replace '\.xaml$', '.exported.yml' }
        $exportArgs = @('export', '--xaml', $XamlPath, '--out', $target)
        if (-not [string]::IsNullOrWhiteSpace($FormFieldXmlPath)) {
            $exportArgs += @('--form-field-xml', $FormFieldXmlPath)
        } else {
            $sidecarPath = $XamlPath + '.formfield.xml'
            if (Test-Path $sidecarPath -PathType Leaf) { $exportArgs += @('--form-field-xml', $sidecarPath) }
        }
        & $tool @exportArgs
    }
    'Inspect' { & $tool inspect --xaml $XamlPath @common }
    'Publish' {
        if (-not $NoBuild -and -not $PSBoundParameters.ContainsKey('XamlPath')) {
            $XamlPath = Join-Path 'artifacts' (([IO.Path]::GetFileNameWithoutExtension($Workflow)) + '.xaml')
        }
        if (-not $NoBuild -and $Workflow) { & $tool build --workflow $Workflow --out $XamlPath @common }
        $effectiveMetadataJsonPath = Get-SPNetWorkflowMetadataJsonPath -XamlFilePath $XamlPath -ExplicitMetadataJsonPath $MetadataJsonPath
        $metadata = Read-SPNetWorkflowMetadataJson -Path $effectiveMetadataJsonPath
        if (-not $WorkflowName) { $WorkflowName = [IO.Path]::GetFileNameWithoutExtension($XamlPath) }
        if (-not $PSBoundParameters.ContainsKey('TargetType') -and $metadata -and $metadata.target -and $metadata.target.type) { $TargetType = [string]$metadata.target.type }
        if ([string]::IsNullOrWhiteSpace($TargetType)) { $TargetType = 'Site' }
        if ([string]::IsNullOrWhiteSpace($TargetListTitle) -and $metadata -and $metadata.target -and $metadata.target.listTitle) { $TargetListTitle = [string]$metadata.target.listTitle }
        $defaultStartManual = 'true'
        $defaultStartCreated = 'false'
        $defaultStartUpdated = 'false'
        if ($metadata -and $metadata.start) {
            if ($null -ne $metadata.start.manual) { $defaultStartManual = [string]$metadata.start.manual }
            if ($null -ne $metadata.start.onCreated) { $defaultStartCreated = [string]$metadata.start.onCreated }
            if ($null -ne $metadata.start.onUpdated) { $defaultStartUpdated = [string]$metadata.start.onUpdated }
        }
        $startManualText = ConvertTo-SPNetYamlBoolText -Value $StartManual -Default $defaultStartManual
        $startCreatedText = ConvertTo-SPNetYamlBoolText -Value $StartOnCreated -Default $defaultStartCreated
        $startUpdatedText = ConvertTo-SPNetYamlBoolText -Value $StartOnUpdated -Default $defaultStartUpdated
        if ([string]::IsNullOrWhiteSpace($startManualText)) { $startManualText = 'true' }
        if ([string]::IsNullOrWhiteSpace($startCreatedText)) { $startCreatedText = 'false' }
        if ([string]::IsNullOrWhiteSpace($startUpdatedText)) { $startUpdatedText = 'false' }
        $publishArgs = @{ Action = 'Publish'; SiteUrl = $SiteUrl; WorkflowName = $WorkflowName; XamlPath = $XamlPath; TargetType = $TargetType; StartManual = $startManualText; StartOnCreated = $startCreatedText; StartOnUpdated = $startUpdatedText; IfExists = $IfExists }
        $publishArgs.AuthMode = $AuthMode
        if (-not [string]::IsNullOrWhiteSpace($TargetListTitle)) { $publishArgs.TargetListTitle = $TargetListTitle }
        if (-not [string]::IsNullOrWhiteSpace($StatusColumn)) { $publishArgs.StatusColumn = $StatusColumn }
        if (-not [string]::IsNullOrWhiteSpace($FormFieldXmlPath)) {
            Write-Warning '-FormFieldXmlPath is deprecated for YAML publish. Metadata JSON is the normal publish contract; FormField XML is passed only as an explicit fallback.'
            $publishArgs.FormFieldXmlPath = $FormFieldXmlPath
        }
        $publishArgs.MetadataJsonPath = $effectiveMetadataJsonPath
        if (-not [string]::IsNullOrWhiteSpace($ExpectedDefinitionId)) { $publishArgs.ExpectedDefinitionId = $ExpectedDefinitionId }
        if (-not [string]::IsNullOrWhiteSpace($BackupDirectory)) { $publishArgs.BackupDirectory = $BackupDirectory }
        if (-not [string]::IsNullOrWhiteSpace($PublisherExePath)) { $publishArgs.PublisherExePath = $PublisherExePath }
        if (-not [string]::IsNullOrWhiteSpace($PublisherCookieHeader)) { $publishArgs.PublisherCookieHeader = $PublisherCookieHeader }
        if (-not [string]::IsNullOrWhiteSpace($PublisherUsername)) { $publishArgs.PublisherUsername = $PublisherUsername }
        if (-not [string]::IsNullOrWhiteSpace($PublisherPassword)) { $publishArgs.PublisherPassword = $PublisherPassword }
        if (-not [string]::IsNullOrWhiteSpace($PublisherDomain)) { $publishArgs.PublisherDomain = $PublisherDomain }
        if ($DryRun) { $publishArgs.DryRun = $true }
        $publishWrapper = Join-Path $PSScriptRoot 'Invoke-SPNetWorkflow.ps1'
        if (-not (Test-Path $publishWrapper -PathType Leaf)) { Throw-SpNetWrapperError -Code 'SPNET-YAML-WRAPPER-001' -Message 'Publish wrapper script was not found.' -Path $publishWrapper -Hint 'Use a complete SPNet package or restore scripts\Invoke-SPNetWorkflow.ps1.' }
        & $publishWrapper @publishArgs
    }
    'Download' {
        if (-not $Out) { $Out = $XamlPath }
        $publishWrapper = Join-Path $PSScriptRoot 'Invoke-SPNetWorkflow.ps1'
        if (-not (Test-Path $publishWrapper -PathType Leaf)) { Throw-SpNetWrapperError -Code 'SPNET-YAML-WRAPPER-001' -Message 'Publish wrapper script was not found.' -Path $publishWrapper -Hint 'Use a complete SPNet package or restore scripts\Invoke-SPNetWorkflow.ps1.' }
        & $publishWrapper -Action Download -SiteUrl $SiteUrl -WorkflowName $WorkflowName -OutputXamlPath $Out
    }
    'List' {
        $listArgs = @{ Action = 'List'; SiteUrl = $SiteUrl }
        if (-not [string]::IsNullOrWhiteSpace($WorkflowName)) { $listArgs.WorkflowName = $WorkflowName }
        if (-not [string]::IsNullOrWhiteSpace($WorkflowNamePrefix)) { $listArgs.WorkflowNamePrefix = $WorkflowNamePrefix }
        if ($IncludeSubscriptions) { $listArgs.IncludeSubscriptions = $true }
        $publishWrapper = Join-Path $PSScriptRoot 'Invoke-SPNetWorkflow.ps1'
        if (-not (Test-Path $publishWrapper -PathType Leaf)) { Throw-SpNetWrapperError -Code 'SPNET-YAML-WRAPPER-001' -Message 'Publish wrapper script was not found.' -Path $publishWrapper -Hint 'Use a complete SPNet package or restore scripts\Invoke-SPNetWorkflow.ps1.' }
        & $publishWrapper @listArgs
    }
    'Cleanup' {
        $cleanupArgs = @{ Action = 'Cleanup'; SiteUrl = $SiteUrl }
        if (-not [string]::IsNullOrWhiteSpace($WorkflowName)) { $cleanupArgs.WorkflowName = $WorkflowName }
        if (-not [string]::IsNullOrWhiteSpace($WorkflowNamePrefix)) { $cleanupArgs.WorkflowNamePrefix = $WorkflowNamePrefix }
        if ($Force) { $cleanupArgs.Force = $true }
        $publishWrapper = Join-Path $PSScriptRoot 'Invoke-SPNetWorkflow.ps1'
        if (-not (Test-Path $publishWrapper -PathType Leaf)) { Throw-SpNetWrapperError -Code 'SPNET-YAML-WRAPPER-001' -Message 'Publish wrapper script was not found.' -Path $publishWrapper -Hint 'Use a complete SPNet package or restore scripts\Invoke-SPNetWorkflow.ps1.' }
        & $publishWrapper @cleanupArgs
    }
}
