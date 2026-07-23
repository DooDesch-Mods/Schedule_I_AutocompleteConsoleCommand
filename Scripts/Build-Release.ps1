<#
.SYNOPSIS
  Build Console Autocomplete Release (IL2CPP + Mono + Loader) into releases\v<version>.

.DESCRIPTION
  Reads ModVersion from src\Constants\ModInfo.cs.
  Builds:
    Autocomplete.Il2Cpp.csproj              -> Mods\ConsoleAutocomplete.IL2CPP.dll
    Autocomplete.csproj                     -> Mods\ConsoleAutocomplete.dll
    loader\ConsoleAutocomplete.Loader.csproj -> Plugins\ConsoleAutocomplete.Loader.dll

  Packages:
    releases\v<version>\ConsoleAutocomplete - v<version>.zip     (Nexus / manual install)
    releases\v<version>\thunderstore\...zip                       (Thunderstore layout)

  Fails if that version folder already contains build artifacts (DLLs or zip)
  so docs-only folders (CHANGELOG / README / NEXUS_DESCRIPTION) are allowed.

.PARAMETER Configuration
  MSBuild configuration. Default: Release

.PARAMETER GameCorePath
  Optional override for Schedule I install (same as Directory.Build.props).

.PARAMETER SkipMono
  Skip Mono build when Managed assemblies are missing (IL2CPP-only package).

.PARAMETER CreateGitHubRelease
  If set, runs `gh release upload` for tag v<version> (release must already exist).
#>
[CmdletBinding()]
param(
    [ValidateSet("Release", "Debug")]
    [string] $Configuration = "Release",

    [string] $GameCorePath = "",

    [switch] $SkipMono,

    [switch] $CreateGitHubRelease
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $RepoRoot

function Get-ModVersion {
    $modInfo = Join-Path $RepoRoot "src\Constants\ModInfo.cs"
    if (-not (Test-Path $modInfo)) {
        throw "ModInfo not found: $modInfo"
    }
    $text = Get-Content -Raw $modInfo
    $m = [regex]::Match($text, 'ModVersion\s*=\s*"([^"]+)"')
    if (-not $m.Success) {
        throw "Could not parse ModVersion from $modInfo"
    }
    return $m.Groups[1].Value
}

function Assert-ReleaseFolderFresh([string] $dir) {
    if (-not (Test-Path $dir)) { return }

    $artifactHints = @(
        (Join-Path $dir "Mods\ConsoleAutocomplete.IL2CPP.dll"),
        (Join-Path $dir "Mods\ConsoleAutocomplete.dll"),
        (Join-Path $dir "Plugins\ConsoleAutocomplete.Loader.dll")
    )
    $zipHits = @(Get-ChildItem -Path $dir -Filter "*.zip" -File -ErrorAction SilentlyContinue)
    $hit = @($artifactHints | Where-Object { Test-Path $_ }) + $zipHits
    if ($hit.Count -gt 0) {
        $msg = @(
            "Release folder already exists with build artifacts:"
            "  $dir"
            ""
            "Delete the folder (or just Mods/, Plugins/, and *.zip) before rebuilding."
            "Docs-only files (README / CHANGELOG / NEXUS_DESCRIPTION) may stay."
        ) -join [Environment]::NewLine
        throw $msg
    }
}

function Invoke-DotNetBuild([string] $project, [string] $config, [hashtable] $extraProps) {
    $args = @(
        "build", $project,
        "-c", $config,
        "--nologo",
        "-v", "minimal"
    )
    foreach ($k in $extraProps.Keys) {
        $args += "-p:$k=$($extraProps[$k])"
    }
    Write-Host ">> dotnet $($args -join ' ')" -ForegroundColor Cyan
    & dotnet @args
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed: $project (exit $LASTEXITCODE)"
    }
}

function Find-BuiltDll([string] $projectDir, [string] $assemblyFileName, [string] $config) {
    $candidates = @(
        Join-Path $projectDir "bin\$config\net6.0\$assemblyFileName"
        Join-Path $projectDir "bin\$config\netstandard2.1\$assemblyFileName"
        Join-Path $projectDir "bin\x64\$config\net6.0\$assemblyFileName"
        Join-Path $projectDir "bin\AnyCPU\$config\netstandard2.1\$assemblyFileName"
        Join-Path $projectDir "loader\bin\$config\netstandard2.1\$assemblyFileName"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { return (Resolve-Path $c).Path }
    }
    $found = Get-ChildItem -Path (Join-Path $projectDir "bin") -Recurse -Filter $assemblyFileName -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match [regex]::Escape($config) } |
        Select-Object -First 1
    if ($found) { return $found.FullName }
    throw "Built DLL not found for $assemblyFileName under $projectDir\bin (config=$config)"
}

function Get-ChangelogSection([string] $version) {
    $changelog = Join-Path $RepoRoot "CHANGELOG.md"
    if (-not (Test-Path $changelog)) { return $null }
    $text = Get-Content -Raw $changelog
    # ## [0.1.0] - date   or   ## 0.1.0
    $pattern = "(?ms)^##\s*\[?" + [regex]::Escape($version) + "\]?[^\n]*\n(.*?)(?=^##\s|\z)"
    $m = [regex]::Match($text, $pattern)
    if (-not $m.Success) { return $null }
    return $m.Groups[1].Value.Trim()
}

function Write-ManifestJson([string] $path, [string] $version, [string] $description) {
    $manifest = [ordered]@{
        name            = "ConsoleAutocomplete"
        version_number  = $version
        website_url     = "https://github.com/shreyas1996/Schedule_I_AutocompleteConsoleCommand"
        description     = $description
        dependencies    = @(
            "LavaGang-MelonLoader-0.7.3"
        )
    }
    $json = $manifest | ConvertTo-Json -Depth 5
    # Thunderstore expects UTF-8 without BOM ideally
    [System.IO.File]::WriteAllText($path, $json + "`n", (New-Object System.Text.UTF8Encoding $false))
}

$version = Get-ModVersion
$releaseDir = Join-Path $RepoRoot "releases\v$version"
$zipPath = Join-Path $releaseDir "ConsoleAutocomplete - v$version.zip"
$tsZipPath = Join-Path $releaseDir "Hiemdallh-ConsoleAutocomplete-$version.zip"

Write-Host "Console Autocomplete release build" -ForegroundColor Green
Write-Host "  Version : $version (from ModInfo)"
Write-Host "  Output  : $releaseDir"
Write-Host "  Config  : $Configuration"

Assert-ReleaseFolderFresh $releaseDir
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $releaseDir "Mods") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $releaseDir "Plugins") | Out-Null

$msbuildProps = @{}
if ($GameCorePath) {
    $msbuildProps["GameCorePath"] = $GameCorePath
}

# IL2CPP (required for main branch)
Invoke-DotNetBuild (Join-Path $RepoRoot "Autocomplete.Il2Cpp.csproj") $Configuration $msbuildProps
$il2cppDll = Find-BuiltDll $RepoRoot "ConsoleAutocomplete.IL2CPP.dll" $Configuration
Copy-Item -Force $il2cppDll (Join-Path $releaseDir "Mods\ConsoleAutocomplete.IL2CPP.dll")

# Loader (always)
Invoke-DotNetBuild (Join-Path $RepoRoot "loader\ConsoleAutocomplete.Loader.csproj") $Configuration $msbuildProps
$loaderDll = Find-BuiltDll (Join-Path $RepoRoot "loader") "ConsoleAutocomplete.Loader.dll" $Configuration
Copy-Item -Force $loaderDll (Join-Path $releaseDir "Plugins\ConsoleAutocomplete.Loader.dll")

# Mono (optional if Managed missing)
$builtMono = $false
if (-not $SkipMono) {
    $managedHint = if ($GameCorePath) {
        Join-Path $GameCorePath "Schedule I_Data\Managed\Assembly-CSharp.dll"
    } else {
        "D:\SteamLibrary\steamapps\common\Schedule I\Schedule I_Data\Managed\Assembly-CSharp.dll"
    }
    if (Test-Path $managedHint) {
        Invoke-DotNetBuild (Join-Path $RepoRoot "Autocomplete.csproj") $Configuration $msbuildProps
        $monoDll = Find-BuiltDll $RepoRoot "ConsoleAutocomplete.dll" $Configuration
        Copy-Item -Force $monoDll (Join-Path $releaseDir "Mods\ConsoleAutocomplete.dll")
        $builtMono = $true
    } else {
        Write-Host "Mono Managed assemblies not found - packaging IL2CPP + Loader only." -ForegroundColor Yellow
        Write-Host "  Re-run without -SkipMono after switching to the Mono game branch once." -ForegroundColor Yellow
    }
} else {
    Write-Host "Skipping Mono build (-SkipMono)." -ForegroundColor Yellow
}

# Player README for this version
$readmeSrc = Join-Path $releaseDir "README.txt"
$readmeFallback = Join-Path $RepoRoot "releases\README.txt"
if (-not (Test-Path $readmeSrc)) {
    if (Test-Path $readmeFallback) {
        $tpl = Get-Content -Raw $readmeFallback
        $tpl = $tpl -replace '\{\{VERSION\}\}', $version
        Set-Content -Encoding utf8 -Path $readmeSrc -Value $tpl
    } else {
        $fallbackReadme = @(
            "Console Autocomplete $version"
            "============================="
            "See docs/RELEASING.md and the GitHub release notes."
        ) -join [Environment]::NewLine
        Set-Content -Encoding utf8 -Path $readmeSrc -Value $fallbackReadme
    }
}

# CHANGELOG excerpt for the zip
$section = Get-ChangelogSection $version
$changelogOut = Join-Path $releaseDir "CHANGELOG.txt"
if ($section) {
    $changelogBody = @(
        "Console Autocomplete $version"
        "============================="
        ""
        $section
    ) -join [Environment]::NewLine
    Set-Content -Encoding utf8 -Path $changelogOut -Value $changelogBody
} elseif (Test-Path (Join-Path $RepoRoot "CHANGELOG.md")) {
    Copy-Item -Force (Join-Path $RepoRoot "CHANGELOG.md") (Join-Path $releaseDir "CHANGELOG.md")
}

# Nexus description helper (copy template if missing)
$nexusDesc = Join-Path $releaseDir "NEXUS_DESCRIPTION.md"
if (-not (Test-Path $nexusDesc) -and (Test-Path (Join-Path $RepoRoot "packaging\nexus\DESCRIPTION.md"))) {
    Copy-Item -Force (Join-Path $RepoRoot "packaging\nexus\DESCRIPTION.md") $nexusDesc
}

# --- Manual / Nexus zip (game-folder layout) ---
$staging = Join-Path $env:TEMP ("ConsoleAutocomplete-release-" + [guid]::NewGuid().ToString("N"))
try {
    New-Item -ItemType Directory -Force -Path $staging | Out-Null
    Copy-Item -Recurse (Join-Path $releaseDir "Mods") (Join-Path $staging "Mods")
    Copy-Item -Recurse (Join-Path $releaseDir "Plugins") (Join-Path $staging "Plugins")
    Copy-Item (Join-Path $releaseDir "README.txt") (Join-Path $staging "README.txt")
    if (Test-Path $changelogOut) {
        Copy-Item $changelogOut (Join-Path $staging "CHANGELOG.txt")
    }

    if (Test-Path $zipPath) { throw "Zip already exists: $zipPath" }
    Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $zipPath -CompressionLevel Optimal
}
finally {
    if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
}

# --- Thunderstore zip ---
$tsStaging = Join-Path $env:TEMP ("ConsoleAutocomplete-ts-" + [guid]::NewGuid().ToString("N"))
try {
    New-Item -ItemType Directory -Force -Path $tsStaging | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $tsStaging "Mods") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $tsStaging "Plugins") | Out-Null

    Copy-Item (Join-Path $releaseDir "Mods\*") (Join-Path $tsStaging "Mods\") -Force
    Copy-Item (Join-Path $releaseDir "Plugins\*") (Join-Path $tsStaging "Plugins\") -Force

    $tsReadmeSrc = Join-Path $RepoRoot "packaging\thunderstore\README.md"
    if (Test-Path $tsReadmeSrc) {
        Copy-Item -Force $tsReadmeSrc (Join-Path $tsStaging "README.md")
    } else {
        Copy-Item -Force (Join-Path $releaseDir "README.txt") (Join-Path $tsStaging "README.md")
    }

    $iconSrc = Join-Path $RepoRoot "packaging\thunderstore\icon.png"
    if (-not (Test-Path $iconSrc)) { throw "Missing Thunderstore icon: $iconSrc" }
    Copy-Item -Force $iconSrc (Join-Path $tsStaging "icon.png")

    $shortDesc = "Live console autocomplete for Schedule I - suggestions, Tab complete, structure helper, mod source labels."
    if ($shortDesc.Length -gt 250) { $shortDesc = $shortDesc.Substring(0, 250) }
    Write-ManifestJson (Join-Path $tsStaging "manifest.json") $version $shortDesc

    if (Test-Path $tsZipPath) { throw "Thunderstore zip already exists: $tsZipPath" }
    Compress-Archive -Path (Join-Path $tsStaging "*") -DestinationPath $tsZipPath -CompressionLevel Optimal
}
finally {
    if (Test-Path $tsStaging) { Remove-Item -Recurse -Force $tsStaging }
}

Write-Host ""
Write-Host "Release ready:" -ForegroundColor Green
Write-Host "  $releaseDir"
Get-ChildItem -Recurse $releaseDir | Where-Object { -not $_.PSIsContainer } | ForEach-Object {
    $rel = $_.FullName.Substring($releaseDir.Length + 1)
    Write-Host ("  {0,-56} {1,10:N0} bytes" -f $rel, $_.Length)
}

Write-Host ""
Write-Host "Upload tips:"
Write-Host "  Nexus / manual : ConsoleAutocomplete - v$version.zip"
Write-Host "  Thunderstore   : Hiemdallh-ConsoleAutocomplete-$version.zip"
Write-Host "  Changelog      : CHANGELOG.md (section $version) or releases\v$version\CHANGELOG.txt"
Write-Host "  Nexus blurb    : packaging\nexus\DESCRIPTION.md"
Write-Host "  GitHub release : git tag v$version ; git push origin v$version"
Write-Host "                 then attach the two zips (or use docs/RELEASING.md)."

if ($CreateGitHubRelease) {
    $tag = "v$version"
    Write-Host ""
    Write-Host "Uploading assets to GitHub release $tag ..." -ForegroundColor Cyan
    & gh release upload $tag $zipPath $tsZipPath --clobber
    if ($LASTEXITCODE -ne 0) {
        throw "gh release upload failed. Create the release first (push tag or use the Release workflow)."
    }
}

if (-not $builtMono) {
    Write-Host ""
    Write-Host "NOTE: Package has no ConsoleAutocomplete.dll (Mono). Fine for IL2CPP-only; rebuild with Mono Managed present for dual-branch zips." -ForegroundColor Yellow
}
