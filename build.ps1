#Requires -Version 5.1
<#
.SYNOPSIS
    日程助手 ScheduleApp 构建脚本：清理 → 构建 → 自测 →（可选）运行 / 发布。
.NOTES
    使用 -m:1 单节点构建，兼容命名管道受限的沙箱环境。
#>
[CmdletBinding()]
param(
    [switch]$Run,
    [switch]$Publish,
    [switch]$SelfContained,
    [switch]$NoClean,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root

function Step([string]$text) {
    Write-Host ''
    Write-Host ('=== ' + $text + ' ===') -ForegroundColor Cyan
}

try {
    if (-not $NoClean) {
        Step '清理上次输出'
        foreach ($project in @('src/Schedule.Core', 'src/Schedule.App', 'tests/Schedule.Core.Tests')) {
            $dir = Join-Path $root ($project + '/bin')
            if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
        }
    }

    Step ('构建解决方案（' + $Configuration + '，单节点）')
    dotnet build ScheduleApp.sln -c $Configuration --nologo -m:1
    if ($LASTEXITCODE -ne 0) { throw '构建失败' }

    Step '运行核心自测'
    dotnet run --project tests/Schedule.Core.Tests -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw '自测未通过' }

    $exe = Join-Path $root ('src/Schedule.App/bin/' + $Configuration + '/net9.0-windows/ScheduleApp.exe')

    if ($Publish) {
        Step '发布单文件'
        $publishDir = Join-Path $root 'publish'
        $selfContained = if ($SelfContained) { 'true' } else { 'false' }

        dotnet publish src/Schedule.App/Schedule.App.csproj -c $Configuration -r win-x64 --self-contained $selfContained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o $publishDir
        if ($LASTEXITCODE -ne 0) { throw '发布失败' }

        Get-ChildItem $publishDir -File | Where-Object { $_.Extension -eq '.exe' } | ForEach-Object {
            Write-Host ('  产物：' + $_.FullName + '  ' + [math]::Round($_.Length / 1MB, 2) + ' MB') -ForegroundColor Green
        }
    }

    if ($Run) {
        Step '启动程序'
        Start-Process -FilePath $exe
    }

    Write-Host ''
    Write-Host '全部完成。' -ForegroundColor Green
}
finally {
    Pop-Location
}
