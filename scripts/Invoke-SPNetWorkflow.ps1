<#
.SYNOPSIS
Publishes or downloads SharePoint 2013 Workflow Manager workflows for SPNet.

.DESCRIPTION
This is the intentionally explicit PowerShell boundary for live SharePoint operations. It publishes generated Windows Workflow Foundation XAML because many SharePoint 2013/Subscription Edition farms require legacy Microsoft.SharePoint.Client.WorkflowServices assemblies and legacy WebLogin authentication.

Packaged usage should prefer the primary `scripts\spnet-workflow.ps1 publish` command. This script remains as the compatibility wrapper and live SharePoint publishing boundary.

For YAML-authored workflows, the generated `*.xaml.metadata.json` sidecar is the normal publish contract. The metadata JSON contains display name, technical name, description, target, start options, initiation settings, and form fields. Publish uses it through `-MetadataJsonPath`, or discovers `-XamlPath + '.metadata.json'` when present. Download writes XAML plus `*.xaml.metadata.json` and may also preserve legacy `*.xaml.formfield.xml` for compatibility/inspection.

.PARAMETER MetadataJsonPath
Metadata JSON sidecar to use for Publish. This is the preferred/normal metadata input and is generated from effective YAML metadata/defaults by the YAML build path.

.PARAMETER FormFieldXmlPath
Deprecated Publish fallback. Used only when metadata JSON does not provide initiation form fields. Normal YAML publish must use metadata JSON instead. Download may still write FormField XML for compatibility/inspection.

.PARAMETER AuthMode
Authentication/bootstrap mode for Publish. WebLogin is the default and uses the wrapper to obtain PnP/WinINet cookies for the CSOM publisher. CookieHeader passes an explicit cookie header, WindowsDefault uses default Windows credentials, and Credentials passes username/password/domain. Direct CSOM publisher invocation does not perform WebLogin/cookie bootstrap and is advanced unless explicit cookies or credentials are supplied.

.PARAMETER IfExists
Publish conflict policy. Fail is the default create behavior and checks for an existing name before creating anything. Update is an explicit update path that replaces a single existing same-name workflow by delete/recreate. CreateNew intentionally publishes alongside an existing workflow by adding a unique suffix when needed. Newly-created definitions/subscriptions are best-effort rolled back if later publish/subscription steps fail.
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
    [string]$FormFieldXmlPath,
    [string]$MetadataJsonPath,
    [string]$OutputXamlPath,
    [ValidateSet('WebLogin','CookieHeader','WindowsDefault','Credentials')]
    [string]$AuthMode = 'WebLogin',
    [ValidateSet('Site', 'List', 'Auto')]
    [string]$TargetType = 'Auto',
    [string]$TargetListTitle,
    [object]$StartManual = $true,
    [object]$StartOnCreated = $false,
    [object]$StartOnUpdated = $false,
    [string]$StatusColumn,
    [ValidateSet('Update', 'CreateNew', 'Fail')]
    [string]$IfExists = 'Fail',
    [ValidateSet('Legacy', 'Csom')]
    [string]$PublisherMode = 'Csom',
    [string]$PublisherExePath,
    [string]$PublisherCookieHeader,
    [string]$PublisherUsername,
    [string]$PublisherPassword,
    [string]$PublisherDomain,
    [string]$ExpectedDefinitionId,
    [string]$BackupDirectory,
    [string]$WorkflowNamePrefix,
    [switch]$IncludeSubscriptions,
    [switch]$DryRun,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

function Throw-SpNetWorkflowWrapperError {
    param([string]$Code, [string]$Message, [string]$Hint = '', [string]$Path = '')
    $exception = New-Object System.InvalidOperationException($Message)
    $record = New-Object System.Management.Automation.ErrorRecord($exception, $Code, [System.Management.Automation.ErrorCategory]::InvalidOperation, $Path)
    if (-not [string]::IsNullOrWhiteSpace($Hint)) { $record.ErrorDetails = New-Object System.Management.Automation.ErrorDetails("$Message`nRemediation: $Hint") }
    throw $record
}

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

function Get-SPNetWinInetCookieHeader {
    param([Parameter(Mandatory = $true)][string]$Url)

    $typeName = 'SPNet.WinInetCookieReader'
    if (-not ($typeName -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SPNet
{
    public static class WinInetCookieReader
    {
        private const int InternetCookieHttponly = 0x00002000;

        [DllImport("wininet.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool InternetGetCookieEx(string url, string cookieName, StringBuilder cookieData, ref int size, int flags, IntPtr reserved);

        public static string GetCookieHeader(string url)
        {
            int size = 0;
            InternetGetCookieEx(url, null, null, ref size, InternetCookieHttponly, IntPtr.Zero);
            if (size <= 0) return null;

            var buffer = new StringBuilder(size);
            if (!InternetGetCookieEx(url, null, buffer, ref size, InternetCookieHttponly, IntPtr.Zero)) return null;
            return buffer.ToString();
        }
    }
}
'@
    }

    try {
        return [SPNet.WinInetCookieReader]::GetCookieHeader($Url)
    } catch {
        Write-Verbose ('Unable to read WinINet cookies for CSOM publisher bootstrap: ' + $_.Exception.Message)
        return $null
    }
}

function Get-SPNetCookieNamesForDiagnostics {
    param([string]$CookieHeader)
    if ([string]::IsNullOrWhiteSpace($CookieHeader)) { return @() }
    @($CookieHeader -split ';' | ForEach-Object {
        $part = ([string]$_).Trim()
        if ($part -match '^([^=]+)=') { $matches[1].Trim() }
    } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
}

function Get-SPNetPublisherToolInfo {
    param([string]$ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        return [pscustomobject]@{ Path = $ExplicitPath; Source = 'explicit'; PackagedDefault = $false; ProjectPath = $null }
    }

    $packagedPublisher = Join-Path $PSScriptRoot '..\tools\SPNet.Workflow.Publisher.Csom\SPNet.Workflow.Publisher.Csom.exe'
    if (Test-Path $packagedPublisher -PathType Leaf) { return [pscustomobject]@{ Path = [IO.Path]::GetFullPath($packagedPublisher); Source = 'packaged'; PackagedDefault = $true; ProjectPath = $null } }

    $sourcePublisher = Join-Path $PSScriptRoot '..\src\SPNet.Workflow.Publisher.Csom\bin\Release\net48\SPNet.Workflow.Publisher.Csom.exe'
    if (Test-Path $sourcePublisher -PathType Leaf) { return [pscustomobject]@{ Path = [IO.Path]::GetFullPath($sourcePublisher); Source = 'source-output-fallback'; PackagedDefault = $false; ProjectPath = $null } }

    $publisherProject = Join-Path $PSScriptRoot '..\src\SPNet.Workflow.Publisher.Csom\SPNet.Workflow.Publisher.Csom.csproj'
    return [pscustomobject]@{ Path = [IO.Path]::GetFullPath($sourcePublisher); Source = 'source-build-fallback'; PackagedDefault = $false; ProjectPath = [IO.Path]::GetFullPath($publisherProject) }
}

function Write-SPNetAuthDiagnostics {
    param([string]$Stage, [string]$Mode, [string]$Detail = '')

    $payload = @{ Stage = $Stage; AuthMode = $Mode; Detail = $Detail }
    if (-not [string]::IsNullOrWhiteSpace($PublisherUsername)) { $payload.HasPublisherUsername = $true }
    if (-not [string]::IsNullOrWhiteSpace($PublisherDomain)) { $payload.HasPublisherDomain = $true }
    if (-not [string]::IsNullOrWhiteSpace($PublisherCookieHeader)) { $payload.CookieNames = @(Get-SPNetCookieNamesForDiagnostics -CookieHeader $PublisherCookieHeader) }
    Write-Output ('SPNET_AUTH ' + ($payload | ConvertTo-Json -Compress -Depth 5))
}

function Invoke-SPNetCsomPublisher {
    if ($Action -ne 'Publish') { return $false }
    if ($PublisherMode -ne 'Csom') { return $false }
    if ([string]::IsNullOrWhiteSpace($XamlPath)) { throw '-XamlPath is required for Publish.' }
    if ([string]::IsNullOrWhiteSpace($WorkflowName)) { throw '-WorkflowName is required for Publish.' }
    if ($TargetType -eq 'Auto') { throw '-TargetType Site or -TargetType List is required when -PublisherMode Csom.' }

    $publisherInfo = Get-SPNetPublisherToolInfo -ExplicitPath $PublisherExePath
    $publisherProject = $publisherInfo.ProjectPath
    $publisherExe = $publisherInfo.Path
    Write-Output ('SPNET_PUBLISHER_TOOL ' + (@{ Path = $publisherExe; Source = $publisherInfo.Source; PackagedDefault = [bool]$publisherInfo.PackagedDefault; DirectInvocationWarning = 'Direct CSOM publisher invocation does not bootstrap WebLogin/cookies; prefer scripts\spnet-workflow.ps1 publish or Invoke-SPNetWorkflow.ps1 unless explicit auth is supplied.' } | ConvertTo-Json -Compress -Depth 4))
    if (-not (Test-Path $publisherExe)) {
        if ([string]::IsNullOrWhiteSpace($publisherProject) -or -not (Test-Path $publisherProject)) {
            Throw-SpNetWorkflowWrapperError -Code 'SPNET-PUBLISHER-TOOL-001' -Message 'CSOM publisher executable was not found and no source fallback project exists.' -Path $publisherExe -Hint 'Use a complete SPNet package, build/package from source, or pass -PublisherExePath to a valid SPNet.Workflow.Publisher.Csom.exe.'
        }
        Write-Warning 'Packaged publisher executable was not found; building source fallback. Packaged publish prefers tools\SPNet.Workflow.Publisher.Csom\SPNet.Workflow.Publisher.Csom.exe before source fallback.'
        dotnet build $publisherProject -v:minimal | Write-Output
        if ($LASTEXITCODE -ne 0) { Throw-SpNetWorkflowWrapperError -Code 'SPNET-PUBLISHER-TOOL-002' -Message "CSOM publisher build failed with exit code $LASTEXITCODE." -Path $publisherProject -Hint 'Fix the publisher project build or package with prebuilt tools.' }
    }
    if (-not (Test-Path $publisherExe)) { Throw-SpNetWorkflowWrapperError -Code 'SPNET-PUBLISHER-TOOL-003' -Message 'CSOM publisher executable was not found after build.' -Path $publisherExe -Hint 'Check build output and target framework net48, or pass -PublisherExePath.' }

    $publisherArgs = @(
        '--site-url', $SiteUrl,
        '--workflow-name', $WorkflowName,
        '--xaml', $XamlPath,
        '--target-type', $TargetType,
        '--start-manual', ([string][bool]$StartManual).ToLowerInvariant(),
        '--start-created', ([string][bool]$StartOnCreated).ToLowerInvariant(),
        '--start-updated', ([string][bool]$StartOnUpdated).ToLowerInvariant(),
        '--if-exists', $IfExists
    )
    if (-not [string]::IsNullOrWhiteSpace($MetadataJsonPath)) { $publisherArgs += @('--metadata-json', $MetadataJsonPath) }
    elseif (-not [string]::IsNullOrWhiteSpace($FormFieldXmlPath)) {
        Write-Warning '-FormFieldXmlPath is deprecated for publish. Use -MetadataJsonPath; FormField XML is passed only as explicit fallback.'
        $publisherArgs += @('--form-field-xml', $FormFieldXmlPath)
    }
    if ($TargetType -eq 'List') {
        if ([string]::IsNullOrWhiteSpace($TargetListTitle)) { throw '-TargetListTitle is required for list workflow publication.' }
        $listGuid = [Guid]::Empty
        if ([Guid]::TryParse($TargetListTitle, [ref]$listGuid)) { $publisherArgs += @('--target-list-id', $TargetListTitle) } else { $publisherArgs += @('--target-list-title', $TargetListTitle) }
    }
    if ($AuthMode -eq 'CookieHeader' -and [string]::IsNullOrWhiteSpace($PublisherCookieHeader)) { throw '-AuthMode CookieHeader requires -PublisherCookieHeader.' }
    if ($AuthMode -eq 'Credentials' -and [string]::IsNullOrWhiteSpace($PublisherUsername)) { throw '-AuthMode Credentials requires -PublisherUsername.' }
    if ($AuthMode -eq 'WebLogin' -and (-not [string]::IsNullOrWhiteSpace($PublisherCookieHeader) -or -not [string]::IsNullOrWhiteSpace($PublisherUsername))) { Write-Warning 'Explicit publisher auth input was supplied with -AuthMode WebLogin; explicit cookie/credentials will be passed through and WebLogin bootstrap may be skipped.' }

    if ($DryRun) {
        [Console]::Out.WriteLine('SPNET_RESULT ' + (@{ Action = 'Publish'; Status = 'DryRun'; WorkflowName = $WorkflowName; XamlPath = $XamlPath; MetadataJsonPath = $MetadataJsonPath; TargetType = $TargetType; StartManual = $StartManual; StartOnCreated = $StartOnCreated; StartOnUpdated = $StartOnUpdated; IfExists = $IfExists; PublisherMode = $PublisherMode; PublisherTool = $publisherExe; AuthMode = $AuthMode; LiveAuthenticationPerformed = $false; PublishAttempted = $false } | ConvertTo-Json -Compress -Depth 5))
        return $true
    }

    if ($AuthMode -eq 'WebLogin' -and [string]::IsNullOrWhiteSpace($PublisherCookieHeader) -and [string]::IsNullOrWhiteSpace($PublisherUsername)) {
        Write-SPNetAuthDiagnostics -Stage 'BeforeBootstrap' -Mode $AuthMode -Detail 'Using PnP WebLogin, then PnP CookieContainer or WinINet cookie handoff for CSOM publisher.'
        Connect-SPNetPnPOnline -Url $SiteUrl -Mode $AuthMode
        $pnpContext = Get-PnPContext
        if (-not $pnpContext) { throw 'PnP authenticated, but Get-PnPContext returned no client context for CSOM publisher bootstrap.' }
        $cookieContainer = $null
        if ($pnpContext.Credentials -and $pnpContext.Credentials.GetType().GetMethod('GetCookieContainer')) {
            $cookieContainer = $pnpContext.Credentials.GetCookieContainer()
        }
        if (-not $cookieContainer) {
            $credentialsProperty = $pnpContext.GetType().GetProperty('Credentials')
            if ($credentialsProperty) {
                $credentialsValue = $credentialsProperty.GetValue($pnpContext, $null)
                if ($credentialsValue -and $credentialsValue.GetType().GetMethod('GetCookieContainer')) { $cookieContainer = $credentialsValue.GetCookieContainer() }
            }
        }
        if ($cookieContainer) {
            $cookies = $cookieContainer.GetCookieHeader([Uri]$SiteUrl)
            if (-not [string]::IsNullOrWhiteSpace($cookies)) {
                $PublisherCookieHeader = $cookies
                Write-SPNetAuthDiagnostics -Stage 'CookieBootstrap' -Mode $AuthMode -Detail ('CSOM publisher bootstrap using PnP CookieContainer cookies: ' + ((Get-SPNetCookieNamesForDiagnostics -CookieHeader $PublisherCookieHeader) -join ', '))
            }
        }

        if ([string]::IsNullOrWhiteSpace($PublisherCookieHeader)) {
            $cookies = Get-SPNetWinInetCookieHeader -Url $SiteUrl
            if (-not [string]::IsNullOrWhiteSpace($cookies)) {
                $PublisherCookieHeader = $cookies
                Write-SPNetAuthDiagnostics -Stage 'CookieBootstrap' -Mode $AuthMode -Detail ('CSOM publisher bootstrap using WinINet cookies: ' + ((Get-SPNetCookieNamesForDiagnostics -CookieHeader $PublisherCookieHeader) -join ', '))
            }
        }

        if ([string]::IsNullOrWhiteSpace($PublisherCookieHeader)) { throw 'PnP WebLogin did not expose a CookieContainer and no WinINet cookies were available for the CSOM publisher bootstrap.' }
    }
    elseif ($AuthMode -eq 'CookieHeader') { Write-SPNetAuthDiagnostics -Stage 'Selected' -Mode $AuthMode -Detail 'Using explicit cookie header; no WebLogin bootstrap will be attempted.' }
    elseif ($AuthMode -eq 'Credentials') { Write-SPNetAuthDiagnostics -Stage 'Selected' -Mode $AuthMode -Detail 'Using explicit username/password/domain for CSOM publisher; no WebLogin bootstrap will be attempted.' }
    elseif ($AuthMode -eq 'WindowsDefault') { Write-SPNetAuthDiagnostics -Stage 'Selected' -Mode $AuthMode -Detail 'Using default Windows credentials in CSOM publisher; no WebLogin bootstrap will be attempted.' }
    else { Write-SPNetAuthDiagnostics -Stage 'Selected' -Mode $AuthMode -Detail 'Explicit cookie/credentials supplied; no additional WebLogin bootstrap required.' }
    if (-not [string]::IsNullOrWhiteSpace($PublisherCookieHeader)) { $publisherArgs += @('--cookie-header', $PublisherCookieHeader) }
    if (-not [string]::IsNullOrWhiteSpace($PublisherUsername)) { $publisherArgs += @('--username', $PublisherUsername) }
    if (-not [string]::IsNullOrWhiteSpace($PublisherPassword)) { $publisherArgs += @('--password', $PublisherPassword) }
    if (-not [string]::IsNullOrWhiteSpace($PublisherDomain)) { $publisherArgs += @('--domain', $PublisherDomain) }

    & $publisherExe @publisherArgs
    if ($LASTEXITCODE -ne 0) { throw "CSOM publisher failed with exit code $LASTEXITCODE." }
    return $true
}

if (Invoke-SPNetCsomPublisher) { return }

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

function New-SPNetUniqueWorkflowName {
    param([string]$RequestedName, [object[]]$Definitions)
    if ([string]::IsNullOrWhiteSpace($RequestedName)) { throw 'Workflow name is required.' }
    $existingNames = @($Definitions | Where-Object { $_ } | ForEach-Object { [string]$_.DisplayName })
    if ($existingNames -notcontains $RequestedName) { return $RequestedName }
    return ($RequestedName + ' ' + (Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
}

function Get-SPNetFormFieldXmlPath {
    param([string]$XamlFilePath, [string]$ExplicitFormFieldXmlPath)
    if (-not [string]::IsNullOrWhiteSpace($ExplicitFormFieldXmlPath)) {
        if (-not (Test-Path $ExplicitFormFieldXmlPath -PathType Leaf)) { throw "FormField XML file not found: $ExplicitFormFieldXmlPath" }
        return $ExplicitFormFieldXmlPath
    }

    if ([string]::IsNullOrWhiteSpace($XamlFilePath)) { return $null }
    $sidecarPath = $XamlFilePath + '.formfield.xml'
    if (Test-Path $sidecarPath -PathType Leaf) { return $sidecarPath }
    return $null
}

function Get-SPNetNormalizedFormFieldXml {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    [xml]$document = Get-Content -Path $Path -Raw
    if (-not $document.DocumentElement -or $document.DocumentElement.LocalName -ne 'Fields') { throw "FormField XML root element must be <Fields>: $Path" }
    return $document.OuterXml
}

function Get-SPNetWorkflowMetadataJsonPath {
    param([string]$XamlFilePath, [string]$ExplicitMetadataJsonPath)
    if (-not [string]::IsNullOrWhiteSpace($ExplicitMetadataJsonPath)) {
        if (-not (Test-Path $ExplicitMetadataJsonPath -PathType Leaf)) { throw "Metadata JSON file not found: $ExplicitMetadataJsonPath" }
        return $ExplicitMetadataJsonPath
    }

    if ([string]::IsNullOrWhiteSpace($XamlFilePath)) { return $null }
    $sidecarPath = $XamlFilePath + '.metadata.json'
    if (Test-Path $sidecarPath -PathType Leaf) { return $sidecarPath }
    return $null
}

function Read-SPNetWorkflowMetadataJson {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    return Get-Content -Path $Path -Raw | ConvertFrom-Json
}

function ConvertTo-SPNetFormFieldXmlFromMetadata {
    param($Metadata)
    if (-not $Metadata -or -not $Metadata.initiation -or -not $Metadata.initiation.formFields) { return $null }
    $document = New-Object System.Xml.XmlDocument
    $fields = $document.CreateElement('Fields')
    [void]$document.AppendChild($fields)
    foreach ($field in @($Metadata.initiation.formFields)) {
        $element = $document.CreateElement('Field')
        foreach ($property in @('name','formType','format','type','baseType','maxLength','numLines','sortable','richTextMode','list','showField','mult','userSelectionMode','userSelectionScope','displayName','description','direction')) {
            if ($null -eq $field.$property) { continue }
            $attributeName = switch ($property) {
                'name' { 'Name' }
                'formType' { 'FormType' }
                'baseType' { 'BaseType' }
                'maxLength' { 'MaxLength' }
                'numLines' { 'NumLines' }
                'richTextMode' { 'RichTextMode' }
                'showField' { 'ShowField' }
                'userSelectionMode' { 'UserSelectionMode' }
                'userSelectionScope' { 'UserSelectionScope' }
                'displayName' { 'DisplayName' }
                default { $property.Substring(0,1).ToUpperInvariant() + $property.Substring(1) }
            }
            $element.SetAttribute($attributeName, [string]$field.$property)
        }
        if ($null -ne $field.default) {
            $default = $document.CreateElement('Default')
            $default.InnerText = [string]$field.default
            [void]$element.AppendChild($default)
        }
        if ($field.choices) {
            $choices = $document.CreateElement('CHOICES')
            foreach ($choice in @($field.choices)) {
                $choiceElement = $document.CreateElement('CHOICE')
                if ($choice -is [string]) { $choiceElement.InnerText = [string]$choice }
                else {
                    if ($null -ne $choice.displayName) { $choiceElement.SetAttribute('DisplayName', [string]$choice.displayName) }
                    $choiceElement.InnerText = [string]$choice.value
                }
                [void]$choices.AppendChild($choiceElement)
            }
            [void]$element.AppendChild($choices)
        }
        [void]$fields.AppendChild($element)
    }
    return $document.OuterXml
}

function Get-SPNetWorkflowMetadataValue {
    param($Definition, [string]$Name)
    if (-not $Definition -or [string]::IsNullOrWhiteSpace($Name)) { return $null }

    $property = $Definition.GetType().GetProperty($Name)
    if ($property -and $property.CanRead) {
        try { return $property.GetValue($Definition, $null) } catch { Write-Verbose ("Unable to read WorkflowDefinition.${Name}: " + $_.Exception.Message) }
    }

    $getProperty = $Definition.GetType().GetMethod('GetProperty', [type[]]@([string]))
    if ($getProperty) {
        try { return $getProperty.Invoke($Definition, @($Name)) } catch { Write-Verbose ("Unable to invoke WorkflowDefinition.GetProperty('${Name}'): " + $_.Exception.Message) }
    }

    try { $propertyDefinitions = $Definition.PropertyDefinitions } catch { $propertyDefinitions = $null }
    if ($propertyDefinitions) {
        try { if ($propertyDefinitions.ContainsKey($Name)) { return $propertyDefinitions[$Name] } } catch { }
        try { return $propertyDefinitions[$Name] } catch { }
    }

    return $null
}

function Get-SPNetWorkflowInitiationUrl {
    param([Guid]$DefinitionId)
    return ('wfsvc/' + $DefinitionId.ToString('N') + '/WFInitForm.aspx')
}

function Set-SPNetWorkflowMetadataValue {
    param($Definition, [string]$Name, $Value)
    if (-not $Definition -or [string]::IsNullOrWhiteSpace($Name)) { return $false }

    $property = $Definition.GetType().GetProperty($Name)
    if ($property -and $property.CanWrite) {
        $targetType = $property.PropertyType
        if ($targetType -eq [bool]) { $property.SetValue($Definition, [bool]$Value, $null) }
        elseif ($targetType -eq [string]) { $property.SetValue($Definition, [string]$Value, $null) }
        else { $property.SetValue($Definition, $Value, $null) }
        return $true
    }

    $setProperty = $Definition.GetType().GetMethod('SetProperty', [type[]]@([string], [object]))
    if ($setProperty) { $setProperty.Invoke($Definition, @($Name, $Value)); return $true }
    $setProperty = $Definition.GetType().GetMethod('SetProperty', [type[]]@([string], [string]))
    if ($setProperty) { $setProperty.Invoke($Definition, @($Name, [string]$Value)); return $true }

    try { $propertyDefinitions = $Definition.PropertyDefinitions } catch { $propertyDefinitions = $null }
    if ($propertyDefinitions) {
        try { $propertyDefinitions[$Name] = [string]$Value; return $true } catch { }
        try { $propertyDefinitions.Add($Name, [string]$Value); return $true } catch { }
    }

    return $false
}

function Set-SPNetWorkflowDefinitionFormFieldMetadata {
    param($Definition, [string]$FormFieldXml, [string]$InitiationUrl)
    if ([string]::IsNullOrWhiteSpace($FormFieldXml)) { return }
    if (-not (Set-SPNetWorkflowMetadataValue -Definition $Definition -Name 'FormField' -Value $FormFieldXml)) { throw 'WorkflowDefinition does not expose writable FormField metadata.' }
    if (-not (Set-SPNetWorkflowMetadataValue -Definition $Definition -Name 'RequiresInitiationForm' -Value $true)) { throw 'WorkflowDefinition does not expose writable RequiresInitiationForm metadata.' }
    if (-not [string]::IsNullOrWhiteSpace($InitiationUrl)) { [void](Set-SPNetWorkflowMetadataValue -Definition $Definition -Name 'InitiationUrl' -Value $InitiationUrl) }
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

function Clear-SPNetPartialWorkflowPublication {
    param([Nullable[Guid]]$DefinitionId, [Nullable[Guid]]$SubscriptionId)
    if ($SubscriptionId.HasValue -and $SubscriptionId.Value -ne [Guid]::Empty) {
        try {
            $subscriptionService.DeleteSubscription($SubscriptionId.Value)
            $context.ExecuteQuery()
        } catch {
            Write-Warning ('Best-effort rollback cleanup could not delete subscription ' + $SubscriptionId.Value + ': ' + $_.Exception.Message)
        }
    }
    if ($DefinitionId.HasValue -and $DefinitionId.Value -ne [Guid]::Empty) {
        try {
            $definitionInfo = [pscustomobject]@{ Id = $DefinitionId.Value }
            [void](Remove-SPNetWorkflowDefinitionAndSubscriptions -DefinitionInfo $definitionInfo)
        } catch {
            Write-Warning ('Best-effort rollback cleanup could not delete definition ' + $DefinitionId.Value + ': ' + $_.Exception.Message)
        }
    }
}

function Get-SPNetTargetList {
    param([string]$ListIdentity)
    if ([string]::IsNullOrWhiteSpace($ListIdentity)) { throw '-TargetListTitle is required for list workflow publication.' }

    $listGuid = [Guid]::Empty
    if ([Guid]::TryParse($ListIdentity, [ref]$listGuid)) {
        return $web.Lists.GetById($listGuid)
    }

    return $web.Lists.GetByTitle($ListIdentity)
}

function Get-SPNetRequiredListByTitle {
    param([string]$Title)
    $list = $web.Lists.GetByTitle($Title)
    $context.Load($list)
    $context.ExecuteQuery()
    return $list
}

function Write-SPNetPreSaveDefinitionDiagnostics {
    param($Definition, [string]$TargetTypeName, $TargetListObject)
    $xaml = if ($Definition -and $Definition.Xaml) { [string]$Definition.Xaml } else { '' }
    $displayNameCount = [regex]::Matches($xaml, 'DisplayName\s*=').Count
    $rootHasDisplayName = $xaml -match '<Activity\b[^>]*\sDisplayName\s*='
    $flowchartHasDisplayName = $xaml -match '<Flowchart\b[^>]*\sDisplayName\s*='
    $objectDataProperties = @()
    try {
        $flags = [System.Reflection.BindingFlags]'Instance,NonPublic,Public'
        $objectDataProperty = $Definition.GetType().BaseType.GetProperty('ObjectData', $flags)
        if (-not $objectDataProperty) { $objectDataProperty = $Definition.GetType().GetProperty('ObjectData', $flags) }
        if ($objectDataProperty) {
            $objectData = $objectDataProperty.GetValue($Definition, $null)
            if ($objectData -and $objectData.Properties) { $objectDataProperties = @($objectData.Properties.Keys) }
        }
    } catch {
        $objectDataProperties = @('UnableToRead:' + $_.Exception.Message)
    }
    Write-Output ('SPNET_PRESAVE_DIAGNOSTICS ' + (@{
        WorkflowName = $Definition.DisplayName
        DefinitionDisplayNameIsNullOrWhiteSpace = [string]::IsNullOrWhiteSpace([string]$Definition.DisplayName)
        TargetType = $TargetTypeName
        RestrictToType = $Definition.RestrictToType
        RestrictToScope = $Definition.RestrictToScope
        TargetListId = if ($TargetListObject) { $TargetListObject.Id.ToString() } else { $null }
        TargetListTitle = if ($TargetListObject) { $TargetListObject.Title } else { $null }
        XamlLength = $xaml.Length
        XamlDisplayNameAttributeCount = $displayNameCount
        XamlRootActivityHasDisplayName = [bool]$rootHasDisplayName
        XamlFlowchartHasDisplayName = [bool]$flowchartHasDisplayName
        ObjectDataPropertyKeys = $objectDataProperties
    } | ConvertTo-Json -Compress -Depth 4))
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
    $formFieldXmlPath = $OutputXamlPath + '.formfield.xml'
    $formFieldXml = Get-SPNetWorkflowMetadataValue -Definition $definition -Name 'FormField'
    if ([string]::IsNullOrWhiteSpace([string]$formFieldXml)) { $formFieldXml = Get-SPNetWorkflowMetadataValue -Definition $definitionInfo -Name 'FormField' }
    if (-not [string]::IsNullOrWhiteSpace([string]$formFieldXml)) {
        [xml]$formFieldDocument = [string]$formFieldXml
        if ($formFieldDocument.DocumentElement -and $formFieldDocument.DocumentElement.LocalName -eq 'Fields') {
            Set-Content -Path $formFieldXmlPath -Value $formFieldDocument.OuterXml -Encoding UTF8
        } else {
            Write-Warning 'Workflow FormField metadata was present but did not contain a <Fields> root; no FormField sidecar was written.'
            $formFieldXmlPath = $null
        }
    } else {
        $formFieldXmlPath = $null
    }
    $requiresInitiationForm = Get-SPNetWorkflowMetadataValue -Definition $definition -Name 'RequiresInitiationForm'
    if ([string]::IsNullOrWhiteSpace([string]$requiresInitiationForm)) { $requiresInitiationForm = Get-SPNetWorkflowMetadataValue -Definition $definitionInfo -Name 'RequiresInitiationForm' }
    $initiationUrl = Get-SPNetWorkflowMetadataValue -Definition $definition -Name 'InitiationUrl'
    if ([string]::IsNullOrWhiteSpace([string]$initiationUrl)) { $initiationUrl = Get-SPNetWorkflowMetadataValue -Definition $definitionInfo -Name 'InitiationUrl' }
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
    $metadataJsonPath = $OutputXamlPath + '.metadata.json'
    $formFields = @()
    if (-not [string]::IsNullOrWhiteSpace($formFieldXmlPath)) {
        [xml]$formFieldSidecar = Get-Content -Path $formFieldXmlPath -Raw
        $formFields = @($formFieldSidecar.Fields.Field | ForEach-Object {
            $field = $_
            $choices = @($field.CHOICES.CHOICE | ForEach-Object { @{ value = [string]$_.'#text'; displayName = [string]$_.DisplayName } })
            $value = [ordered]@{
                name = [string]$field.Name
                formType = [string]$field.FormType
                type = [string]$field.Type
                displayName = [string]$field.DisplayName
                description = [string]$field.Description
                direction = [string]$field.Direction
            }
            if ($null -ne $field.Default) { $value.default = [string]$field.Default }
            if ($choices.Count -gt 0) { $value.choices = $choices }
            foreach ($attributeName in @('Format','BaseType','MaxLength','NumLines','Sortable','RichTextMode','List','ShowField','Mult','UserSelectionMode','UserSelectionScope')) {
                $attributeValue = [string]$field.$attributeName
                if (-not [string]::IsNullOrWhiteSpace($attributeValue)) { $value[$attributeName.Substring(0,1).ToLowerInvariant() + $attributeName.Substring(1)] = $attributeValue }
            }
            $value
        })
    }
    $metadata = [ordered]@{
        displayName = [string]$definition.DisplayName
        description = [string]$definition.Description
        target = [ordered]@{ type = $targetType; listTitle = $targetListName }
        start = [ordered]@{ manual = [bool]$manual; onCreated = [bool]$created; onUpdated = [bool]$updated }
        initiation = [ordered]@{ requiresForm = (-not [string]::IsNullOrWhiteSpace($formFieldXmlPath) -or ([string]$requiresInitiationForm).ToLowerInvariant() -eq 'true'); url = [string]$initiationUrl; formFields = $formFields }
    }
    $metadata | ConvertTo-Json -Depth 20 | Set-Content -Path $metadataJsonPath -Encoding UTF8
    Write-SPNetResult @{ Action = 'Download'; WorkflowName = $definition.DisplayName; DefinitionId = $definition.Id.ToString(); SubscriptionId = if ($subscription) { $subscription.Id.ToString() } else { $null }; XamlPath = $OutputXamlPath; FormFieldXmlPath = $formFieldXmlPath; MetadataJsonPath = $metadataJsonPath; HasFormField = -not [string]::IsNullOrWhiteSpace($formFieldXmlPath); HasMetadataJson = (Test-Path $metadataJsonPath); TargetType = $targetType; TargetListTitle = $targetListName; StartManual = [bool]$manual; StartOnCreated = [bool]$created; StartOnUpdated = [bool]$updated; StatusColumn = $status }
    return
}

if ([string]::IsNullOrWhiteSpace($XamlPath)) { throw '-XamlPath is required for Publish.' }
if ([string]::IsNullOrWhiteSpace($WorkflowName)) { throw '-WorkflowName is required for Publish.' }
$xamlContent = Get-Content -Path $XamlPath -Raw
$effectiveMetadataJsonPath = Get-SPNetWorkflowMetadataJsonPath -XamlFilePath $XamlPath -ExplicitMetadataJsonPath $MetadataJsonPath
$metadata = Read-SPNetWorkflowMetadataJson -Path $effectiveMetadataJsonPath
$effectiveFormFieldXmlPath = $null
$formFieldXml = ConvertTo-SPNetFormFieldXmlFromMetadata -Metadata $metadata
if ([string]::IsNullOrWhiteSpace($formFieldXml) -and -not [string]::IsNullOrWhiteSpace($FormFieldXmlPath)) {
    Write-Warning '-FormFieldXmlPath is deprecated for publish. Use -MetadataJsonPath; FormField XML is loaded only as explicit fallback.'
    $effectiveFormFieldXmlPath = Get-SPNetFormFieldXmlPath -XamlFilePath $null -ExplicitFormFieldXmlPath $FormFieldXmlPath
    $formFieldXml = Get-SPNetNormalizedFormFieldXml -Path $effectiveFormFieldXmlPath
}
$requestedWorkflowName = $WorkflowName
$allDefinitionsForCreateNew = @()
if ($IfExists -eq 'CreateNew') {
    $definitionCollectionForCreateNew = $deploymentService.EnumerateDefinitions($true)
    $context.Load($definitionCollectionForCreateNew)
    $context.ExecuteQuery()
    $allDefinitionsForCreateNew = @($definitionCollectionForCreateNew)
    $WorkflowName = New-SPNetUniqueWorkflowName -RequestedName $requestedWorkflowName -Definitions $allDefinitionsForCreateNew
}
$existingDefinitions = if ($IfExists -eq 'CreateNew') { @($allDefinitionsForCreateNew | Where-Object { $_ -and $_.DisplayName -eq $requestedWorkflowName }) } else { Get-SPNetWorkflowDefinitionsByName -Name $WorkflowName }
if ($existingDefinitions.Count -gt 0 -and $IfExists -eq 'Fail') {
    $workflowExistsMessage = "Workflow '$WorkflowName' already exists. Use the explicit update command/path to replace it, or delete the existing workflow and then create it again. Use -IfExists CreateNew only when you intentionally want a side-by-side workflow with a unique suffix."
    Write-SPNetResult @{ Action = 'Publish'; WorkflowName = $WorkflowName; ExistingDefinitionIds = @($existingDefinitions | ForEach-Object { $_.Id.ToString() }); Status = 'FailedBeforeCreate'; ErrorCode = 'WorkflowExists'; ErrorMessage = $workflowExistsMessage }
    throw $workflowExistsMessage
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
$workflowHistoryList = $null
$workflowTasksList = $null
if ($effectiveTargetType -eq 'List') {
    if ([string]::IsNullOrWhiteSpace($TargetListTitle)) { throw '-TargetListTitle is required for list workflow publication.' }
    $targetList = Get-SPNetTargetList -ListIdentity $TargetListTitle
    $context.Load($targetList)
    $context.ExecuteQuery()
    $workflowHistoryList = Get-SPNetRequiredListByTitle -Title 'Workflow History'
    $workflowTasksList = Get-SPNetRequiredListByTitle -Title 'Workflow Tasks'
    $definition.RestrictToType = 'List'
    $definition.RestrictToScope = $targetList.Id.ToString()
} else {
    $definition.RestrictToType = 'Site'
    $definition.RestrictToScope = $web.Id.ToString()
}
Set-SPNetWorkflowDefinitionFormFieldMetadata -Definition $definition -FormFieldXml $formFieldXml -InitiationUrl $null
$definitionId = $null
$subscriptionId = $null
try {
    Write-SPNetPreSaveDefinitionDiagnostics -Definition $definition -TargetTypeName $effectiveTargetType -TargetListObject $targetList
    $saveResult = $deploymentService.SaveDefinition($definition)
    $context.ExecuteQuery()
    $definitionId = $saveResult.Value
    if (-not [string]::IsNullOrWhiteSpace($formFieldXml)) {
        $savedDefinition = $deploymentService.GetDefinition($definitionId)
        $context.Load($savedDefinition)
        $context.ExecuteQuery()
        Set-SPNetWorkflowDefinitionFormFieldMetadata -Definition $savedDefinition -FormFieldXml $formFieldXml -InitiationUrl (Get-SPNetWorkflowInitiationUrl -DefinitionId $definitionId)
        [void]$deploymentService.SaveDefinition($savedDefinition)
        $context.ExecuteQuery()
    }
    $deploymentService.PublishDefinition($definitionId)
    $context.ExecuteQuery()
} catch {
    $partial = @{ Action = 'Publish'; WorkflowName = $WorkflowName; DefinitionId = if ($definitionId) { $definitionId.ToString() } else { $null }; TargetType = $effectiveTargetType; Status = if ($definitionId) { 'PartialDefinitionSaved' } else { 'FailedBeforeCreate' }; ErrorCode = $_.Exception.GetType().Name; ErrorMessage = $_.Exception.Message }
    if ($definitionId) { Clear-SPNetPartialWorkflowPublication -DefinitionId $definitionId -SubscriptionId $subscriptionId; $partial.RollbackAttempted = $true }
    Write-SPNetResult $partial
    throw
}
$subscription = New-Object Microsoft.SharePoint.Client.WorkflowServices.WorkflowSubscription($context)
$subscription.DefinitionId = $definitionId
$subscription.Name = $WorkflowName
$subscription.Enabled = $true
$subscription.EventTypes = New-Object 'System.Collections.Generic.List[string]'
$eventTypeTokens = @()
if ($StartManual) { [void]$subscription.EventTypes.Add('WorkflowStart'); $eventTypeTokens += 'WorkflowStart' }
if ($StartOnCreated) { [void]$subscription.EventTypes.Add('ItemAdded'); $eventTypeTokens += 'ItemAdded' }
if ($StartOnUpdated) { [void]$subscription.EventTypes.Add('ItemUpdated'); $eventTypeTokens += 'ItemUpdated' }
if ($eventTypeTokens.Count -eq 0) { [void]$subscription.EventTypes.Add('WorkflowStart'); $eventTypeTokens += 'WorkflowStart' }
if ($effectiveTargetType -eq 'List') {
    $subscription.EventSourceId = $targetList.Id
    $subscription.TaskListId = $workflowTasksList.Id
    $subscription.HistoryListId = $workflowHistoryList.Id
    $subscription.StatusFieldName = if ([string]::IsNullOrWhiteSpace($StatusColumn)) { $WorkflowName } else { $StatusColumn }
    $subscriptionResult = $subscriptionService.PublishSubscriptionForList($subscription, $targetList.Id)
} else {
    $subscription.EventSourceId = $web.Id
    $subscriptionResult = $subscriptionService.PublishSubscription($subscription)
}
try {
    $context.ExecuteQuery()
    $subscriptionId = $subscriptionResult.Value
} catch {
    Clear-SPNetPartialWorkflowPublication -DefinitionId $definitionId -SubscriptionId $subscriptionId
    Write-SPNetResult @{ Action = 'Publish'; WorkflowName = $WorkflowName; RequestedWorkflowName = $requestedWorkflowName; DefinitionId = $definitionId.ToString(); SubscriptionId = $null; TargetType = $effectiveTargetType; Status = 'PartialDefinitionPublishedRolledBack'; ErrorCode = $_.Exception.GetType().Name; ErrorMessage = $_.Exception.Message; RollbackAttempted = $true }
    throw
}
Write-SPNetResult @{ Action = 'Publish'; WorkflowName = $WorkflowName; RequestedWorkflowName = $requestedWorkflowName; IfExists = $IfExists; DefinitionId = $definitionId.ToString(); SubscriptionId = $subscriptionId.ToString(); OldDefinitionId = $script:spnetOldDefinitionId; OldSubscriptionId = $script:spnetOldSubscriptionId; BackupPath = $script:spnetBackupPath; TargetType = $effectiveTargetType; StartManual = $StartManual; StartOnCreated = $StartOnCreated; StartOnUpdated = $StartOnUpdated; StatusColumn = if ($effectiveTargetType -eq 'List') { $subscription.StatusFieldName } else { $null }; HasFormField = -not [string]::IsNullOrWhiteSpace($formFieldXml); FormFieldXmlPath = $effectiveFormFieldXmlPath; Status = if ($script:spnetOldDefinitionId) { 'UpdatedByBackupDeleteRecreate' } else { 'Published' } }
