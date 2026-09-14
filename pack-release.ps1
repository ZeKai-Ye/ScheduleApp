#Requires -Version 5.1
<#
.SYNOPSIS
    Package a release build of ScheduleApp into the release folder.

.DESCRIPTION
    1. publishes the WPF app (framework-dependent by default; -SelfContained for a single file)
    2. copies docs and the CSV samples
    3. renders the Chinese release notes from packaging\release-notes.txt
       (fills in version / date / size / SHA256)
    4. verifies the packaged exe: --info plus --ui-smoke in dark and light theme

.NOTES
    IMPORTANT: this file must stay ASCII-only (comments included).
    Windows PowerShell 5.1 reads .ps1 as ANSI unless the file has a UTF-8 BOM,
    so any non-ASCII literal here turns into mojibake and breaks parsing.
    All Chinese text lives in packaging\release-notes.txt and is copied as UTF-8.
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory = 'release',
    [string]$Configuration = 'Release',
    [switch]$SkipVerify,
    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root

function Step([string]$text) {
    Write-Host ''
    Write-Host ('=== ' + $text + ' ===') -ForegroundColor Cyan
}

# The release notes file name is built from code points to keep this script ASCII-only.
function NotesFileName {
    $chars = @(0x8FD0, 0x884C, 0x8BF4, 0x660E) | ForEach-Object { [char]$_ }
    return [string]::Join('', $chars) + '.txt'
}

try {
    $out = Join-Path $root $OutputDirectory
    # Keep the build tree out of bin\: a running instance may hold those files.
    $buildRoot = Join-Path $root '.verify-fix\release-build'
    $baseOutput = '-p:BaseOutputPath=' + $buildRoot + '\bin\'

    if (Test-Path $out) { Remove-Item -Recurse -Force $out }
    New-Item -ItemType Directory -Force -Path $out | Out-Null

    Step 'publish'
    $project = 'src/Schedule.App/Schedule.App.csproj'

    if ($SelfContained) {
        # Self-contained single file (~150 MB, target machine needs no runtime).
        # Requires downloading the runtime packs from nuget.org, so it fails offline.
        $publishArgs = @(
            'publish', $project, '-c', $Configuration, '-r', 'win-x64', '--self-contained', 'true',
            '-p:PublishSingleFile=true', '-p:EnableCompressionInSingleFile=true', '-p:DebugType=none',
            $baseOutput, '-o', $out, '--nologo'
        )
        & dotnet @publishArgs
        if ($LASTEXITCODE -ne 0) { throw 'publish failed: self-contained needs the runtime packs from nuget.org' }
    }
    else {
        # Framework-dependent (default): small, needs the .NET 9 Desktop Runtime on the target machine.
        # Output is a folder rather than a single file: PublishSingleFile requires a RID,
        # and resolving a RID requires the runtime packs (network).
        $publishArgs = @(
            'publish', $project, '-c', $Configuration,
            '-p:DebugType=none', $baseOutput, '-o', $out, '--nologo', '--no-restore'
        )
        & dotnet @publishArgs
        if ($LASTEXITCODE -ne 0) { throw 'publish failed' }
    }

    Step 'docs'
    Copy-Item (Join-Path $root 'README.md') $out -Force
    Copy-Item (Join-Path $root 'CHANGELOG.md') $out -Force

    $docsOut = Join-Path $out 'docs'
    New-Item -ItemType Directory -Force -Path $docsOut | Out-Null
    Get-ChildItem (Join-Path $root 'docs') -Filter *.md | ForEach-Object { Copy-Item $_.FullName $docsOut -Force }

    $samplesSource = Join-Path $root 'samples'
    if (Test-Path $samplesSource) {
        $samplesOut = Join-Path $out ('{' + [char]0x6837 + [char]0x4F8B + '}')
        $samplesOut = $samplesOut.Replace('{', '').Replace('}', '')
        New-Item -ItemType Directory -Force -Path $samplesOut | Out-Null
        Get-ChildItem $samplesSource -File | ForEach-Object { Copy-Item $_.FullName $samplesOut -Force }
    }

    Step 'cleanup'
    Get-ChildItem $out -Filter *.pdb | Remove-Item -Force
    Get-ChildItem $out -Filter *.xml | Remove-Item -Force

    $exe = Get-ChildItem $out -Filter *.exe | Select-Object -First 1
    if (-not $exe) { throw 'no exe produced' }

    # The exe of a framework-dependent folder build is only the launcher stub (never changes);
    # the actual program is ScheduleApp.dll, so hash that one (or the single file when self-contained).
    $mainArtifact = Get-ChildItem $out -Filter 'ScheduleApp.dll' | Select-Object -First 1
    $mainLabel = 'ScheduleApp.dll'
    if (-not $mainArtifact) {
        $mainArtifact = $exe
        $mainLabel = 'ScheduleApp.exe'
    }

    $totalBytes = (Get-ChildItem $out -Recurse -File | Measure-Object -Property Length -Sum).Sum
    $sizeMb = [math]::Round($totalBytes / 1MB, 2)
    $sha = (Get-FileHash $mainArtifact.FullName -Algorithm SHA256).Hash
    $version = (Get-Item $mainArtifact.FullName).VersionInfo.ProductVersion
    if (-not $version) { $version = (Get-Item $exe.FullName).VersionInfo.ProductVersion }
    if (-not $version) { $version = 'unknown' }
    $version = $version.Split('+')[0]

    Step 'checksums'
    $sumsFile = Join-Path $out 'SHA256SUMS.txt'
    $sumLines = Get-ChildItem $out -Recurse -File |
        Where-Object { $_.Name -ne 'SHA256SUMS.txt' } |
        Sort-Object FullName |
        ForEach-Object {
            $rel = $_.FullName.Substring($out.Length + 1)
            (Get-FileHash $_.FullName -Algorithm SHA256).Hash + '  ' + $rel
        }

    [System.IO.File]::WriteAllLines($sumsFile, $sumLines, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host ('wrote SHA256SUMS.txt (' + $sumLines.Count + ' files)')

    Step 'release notes'
    $templatePath = Join-Path $root 'packaging\release-notes.txt'
    $template = [System.IO.File]::ReadAllText($templatePath, [System.Text.Encoding]::UTF8)

    $rendered = $template.Replace('{{MAIN}}', $mainLabel)
    $rendered = $rendered.Replace('{{VERSION}}', 'v' + $version)
    $rendered = $rendered.Replace('{{DATE}}', (Get-Date).ToString('yyyy-MM-dd HH:mm'))
    $rendered = $rendered.Replace('{{SIZE}}', ($sizeMb.ToString() + ' MB'))
    $rendered = $rendered.Replace('{{SHA256}}', $sha)

    [System.IO.File]::WriteAllText((Join-Path $out (NotesFileName)), $rendered, (New-Object System.Text.UTF8Encoding($true)))

    if (-not $SkipVerify) {
        Step 'verify'
        $dataDir = Join-Path $buildRoot 'verify-data'
        if (Test-Path $dataDir) { Remove-Item -Recurse -Force $dataDir }

        & $exe.FullName --info --data $dataDir --out (Join-Path $buildRoot 'info.txt') | Out-Null
        $infoExit = $LASTEXITCODE
        Write-Host ('--info exit=' + $infoExit)

        & $exe.FullName --ui-smoke --theme dark --data $dataDir | Out-Null
        $darkExit = $LASTEXITCODE
        Write-Host ('--ui-smoke(dark) exit=' + $darkExit)

        & $exe.FullName --ui-smoke --theme light --data $dataDir | Out-Null
        $lightExit = $LASTEXITCODE
        Write-Host ('--ui-smoke(light) exit=' + $lightExit)

        $log = Join-Path $dataDir 'logs\ui-smoke.log'
        if (Test-Path $log) {
            $fails = Get-Content $log -Encoding UTF8 | Select-String -Pattern 'FAIL'
            if ($fails) { $fails | ForEach-Object { Write-Host ('  ' + $_.Line) -ForegroundColor Red } }
            else { Write-Host '  ui-smoke.log: no FAIL lines' }
        }

        if ($infoExit -ne 0 -or $darkExit -ne 0 -or $lightExit -ne 0) { throw 'verification failed on the packaged exe' }
    }

    Step 'summary'
    Write-Host ('output  : ' + $out)
    Write-Host ('exe     : ' + $exe.Name)
    Write-Host ('main    : ' + $mainArtifact.Name + '  (' + $mainLabel + ')')
    Write-Host ('version : ' + $version)
    Write-Host ('size    : ' + $sizeMb + ' MB')
    Write-Host ('sha256  : ' + $sha)
    Write-Host ('files   : ' + (Get-ChildItem $out -Recurse -File).Count)
    Write-Host 'done' -ForegroundColor Green
}
finally {
    Pop-Location
}
