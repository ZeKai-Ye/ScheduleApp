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
        # 打包逻辑集中在 pack-release.ps1：发布 + 拷贝文档/样例 + 生成运行说明 + 验证打包后的 exe
        Step '打包发布版本到 release 目录'
        $packArgs = @()
        if ($SelfContained) { $packArgs += '-SelfContained' }

        & (Join-Path $root 'pack-release.ps1') @packArgs
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
