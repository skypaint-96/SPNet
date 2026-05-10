param(
    [string]$Configuration = 'Release',
    [string]$Version = '0.0.0-local',
    [string]$OutputDirectory = 'artifacts\package',
    [switch]$SkipBuild,
    [switch]$IncludeNuGetPackages
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$outputRoot = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $repoRoot $OutputDirectory }
$outputRoot = [IO.Path]::GetFullPath($outputRoot)
$safeVersion = $Version -replace '[^0-9A-Za-z\.\-_\+]', '-'
$packageName = "SPNet.Workflow-$safeVersion"
$stageRoot = Join-Path $outputRoot 'stage'
$packageRoot = Join-Path $stageRoot $packageName
$zipPath = Join-Path $outputRoot "$packageName.zip"
$nugetOutput = Join-Path $outputRoot 'nuget'

$projects = @(
    @{ Name = 'SPNet.Workflow.WfSerializer'; Path = 'src\SPNet.Workflow.WfSerializer\SPNet.Workflow.WfSerializer.csproj' },
    @{ Name = 'SPNet.Workflow.Publisher.Csom'; Path = 'src\SPNet.Workflow.Publisher.Csom\SPNet.Workflow.Publisher.Csom.csproj' }
)

function New-CleanDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (Test-Path $Path) { Remove-Item -LiteralPath $Path -Recurse -Force }
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Test-SPNetPackageExcludedPath {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $normalized = $RelativePath -replace '/', '\'
    $fileName = [IO.Path]::GetFileName($normalized)

    if ($normalized -match '(^|\\)(bin|obj|WebsiteCache|wf-serializer-cache|TestResults|coverage)(\\|$)') { return $true }
    if ($normalized -match '(^|\\)artifacts(\\|$)') { return $true }
    if ($normalized -match 'sharepoint-workflow-diagnostics') { return $true }
    if ($normalized -match 'workflow-diagnostics\.json$') { return $true }
    if ($normalized -match '\.downloaded\.xaml$|\.exported\.yml$|\.inspect\.txt$') { return $true }
    if ($normalized -match '\.(secret|secrets)\.ya?ml$|\.local\.ya?ml$|\.local\.ps1$') { return $true }
    if ($normalized -match 'spnet\.local\.yml$') { return $true }
    if ($normalized -match '\.(bak|backup|tmp|temp|log)$') { return $true }
    if ($normalized -match '\.(zip|nupkg|snupkg)$') { return $true }
    if ($fileName -like '*.Proxy.dll') { return $true }
    if ($fileName -eq 'Microsoft.Web.Design.Client.dll') { return $true }
    if ($fileName -like 'Microsoft.Web.Authoring*.dll') { return $true }

    return $false
}

function Copy-SPNetPackageFile {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination,
        [string]$RelativePath = ''
    )

    if ($RelativePath -and (Test-SPNetPackageExcludedPath -RelativePath $RelativePath)) { return }
    $destinationDirectory = Split-Path -Parent $Destination
    if ($destinationDirectory) { New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null }
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function Copy-SPNetPackageTree {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDirectory,
        [Parameter(Mandatory = $true)][string]$DestinationDirectory
    )

    if (-not (Test-Path $SourceDirectory -PathType Container)) { return }

    $sourceRoot = (Resolve-Path $SourceDirectory).Path.TrimEnd('\', '/')
    Get-ChildItem -LiteralPath $SourceDirectory -File -Recurse | ForEach-Object {
        $relative = $_.FullName.Substring($sourceRoot.Length).TrimStart('\', '/')
        if (-not (Test-SPNetPackageExcludedPath -RelativePath $relative)) {
            Copy-SPNetPackageFile -Source $_.FullName -Destination (Join-Path $DestinationDirectory $relative) -RelativePath $relative
        }
    }
}

Write-Host "Packaging SPNet Workflow version $safeVersion to $outputRoot"

New-CleanDirectory -Path $outputRoot
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

Push-Location $repoRoot
try {
    if (-not $SkipBuild) {
        dotnet restore .\SPNet.slnx
        dotnet build .\SPNet.slnx --configuration $Configuration --no-restore
    }

    foreach ($project in $projects) {
        $projectPath = Join-Path $repoRoot $project.Path
        $projectOutput = Join-Path (Split-Path -Parent $projectPath) "bin\$Configuration\net48"
        if (-not (Test-Path $projectOutput -PathType Container)) {
            throw "Build output not found for $($project.Name): $projectOutput. Run without -SkipBuild or build first."
        }

        Copy-SPNetPackageTree -SourceDirectory $projectOutput -DestinationDirectory (Join-Path $packageRoot "tools\$($project.Name)")
    }

    Copy-SPNetPackageFile -Source (Join-Path $repoRoot 'README.md') -Destination (Join-Path $packageRoot 'README.md') -RelativePath 'README.md'
    Copy-SPNetPackageTree -SourceDirectory (Join-Path $repoRoot 'docs') -DestinationDirectory (Join-Path $packageRoot 'docs')
    Copy-SPNetPackageTree -SourceDirectory (Join-Path $repoRoot 'samples') -DestinationDirectory (Join-Path $packageRoot 'samples')

    New-Item -ItemType Directory -Path (Join-Path $packageRoot 'config') -Force | Out-Null
    foreach ($configFile in @('config\spnet.defaults.yml', 'config\spnet.local.example.yml')) {
        $sourceConfig = Join-Path $repoRoot $configFile
        if (Test-Path $sourceConfig -PathType Leaf) {
            Copy-SPNetPackageFile -Source $sourceConfig -Destination (Join-Path $packageRoot $configFile) -RelativePath $configFile
        }
    }

    New-Item -ItemType Directory -Path (Join-Path $packageRoot 'scripts') -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'scripts') -Filter '*.ps1' -File | ForEach-Object {
        $relative = Join-Path 'scripts' $_.Name
        Copy-SPNetPackageFile -Source $_.FullName -Destination (Join-Path $packageRoot $relative) -RelativePath $relative
    }

    $manifest = [ordered]@{
        name = 'SPNet.Workflow'
        version = $safeVersion
        createdUtc = (Get-Date).ToUniversalTime().ToString('o')
        configuration = $Configuration
        tools = $projects.Name
        notes = 'Package intentionally excludes local config, SharePoint secrets, WebsiteCache/proxy assemblies, generated diagnostics, and transient build artifacts.'
    }
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $packageRoot 'package-manifest.json') -Encoding UTF8

    if (Test-Path $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $zipPath -Force
    Write-Host "Created package: $zipPath"

    if ($IncludeNuGetPackages) {
        New-Item -ItemType Directory -Path $nugetOutput -Force | Out-Null
        foreach ($project in $projects) {
            dotnet pack (Join-Path $repoRoot $project.Path) --configuration $Configuration --no-build --output $nugetOutput /p:PackageVersion=$safeVersion
        }
        Write-Host "Created NuGet packages in: $nugetOutput"
    }
}
finally {
    Pop-Location
}

Write-Host 'Package outputs:'
Get-ChildItem -LiteralPath $outputRoot -File -Recurse -Include '*.zip','*.nupkg','*.snupkg' | ForEach-Object { Write-Host " - $($_.FullName)" }
