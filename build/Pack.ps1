<#
.SYNOPSIS
    Builds the Steam Posters installer (Setup.exe) and update packages with Velopack.

.DESCRIPTION
    1. Publishes the app self-contained for win-x64 (no .NET install needed on the user's PC).
    2. Optionally downloads the latest GitHub release so Velopack can build a small delta update.
    3. Packs Setup.exe, the full and delta .nupkg update packages and releases.win.json into
       artifacts\releases.
    4. Optionally uploads them as a GitHub release (used by the release workflow).

    Works on Windows PowerShell 5.1 and PowerShell 7. Run from anywhere in the repo.

.EXAMPLE
    ./build/Pack.ps1                       # local build of the version in steamposters.csproj
.EXAMPLE
    ./build/Pack.ps1 -Version 0.2.0 -GitHubToken $env:GITHUB_TOKEN -Upload
#>
[CmdletBinding()]
param(
    # Defaults to <Version> in steamposters.csproj.
    [string]$Version,
    # Needed for -Upload; also lets the delta download avoid GitHub's anonymous rate limit.
    [string]$GitHubToken,
    [switch]$Upload
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'steamposters\steamposters.csproj'
$publishDir = Join-Path $repo 'artifacts\publish'
$releaseDir = Join-Path $repo 'artifacts\releases'
$repoUrl = 'https://github.com/jwo0010/steam-posters'

if (-not $Version) { $Version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }
if (-not $Version) { throw "No -Version given and none found in $project" }
Write-Host "Packing Steam Posters $Version"

function Invoke-Checked([string]$what, [scriptblock]$command) {
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)" }
}

Push-Location $repo
try {
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    Invoke-Checked 'dotnet publish' {
        dotnet publish $project -c Release -r win-x64 --self-contained -o $publishDir "-p:Version=$Version"
    }
    Invoke-Checked 'dotnet tool restore' { dotnet tool restore }

    New-Item -ItemType Directory -Force $releaseDir | Out-Null
    if ($GitHubToken -or $Upload) {
        # The previous release lets vpk build a delta package (small download for existing users).
        $tokenArgs = @(); if ($GitHubToken) { $tokenArgs = @('--token', $GitHubToken) }
        dotnet vpk download github --repoUrl $repoUrl --outputDir $releaseDir @tokenArgs
        if ($LASTEXITCODE -ne 0) { Write-Warning 'No previous release downloaded (first release?); packing without a delta.' }
    }

    Invoke-Checked 'vpk pack' {
        dotnet vpk pack --packId SteamPosters --packVersion $Version --packDir $publishDir `
            --mainExe SteamPosters.exe --packTitle 'Steam Posters' --packAuthors 'Steam Posters' `
            --icon (Join-Path $repo 'steamposters\Assets\app.ico') --outputDir $releaseDir
    }

    if ($Upload) {
        if (-not $GitHubToken) { throw '-Upload needs -GitHubToken' }
        Invoke-Checked 'vpk upload' {
            dotnet vpk upload github --repoUrl $repoUrl --token $GitHubToken --outputDir $releaseDir `
                --publish --releaseName "Steam Posters $Version" --tag "v$Version"
        }
    }

    Write-Host "Done. Installer: $(Join-Path $releaseDir 'SteamPosters-win-Setup.exe')"
}
finally {
    Pop-Location
}
