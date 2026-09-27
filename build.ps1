# =====================================================================
#  速录助手 QuickType —— 一键编译脚本
#  使用 Windows 自带的 .NET Framework 编译器，无需安装 VS / .NET SDK。
#  用法：右键本文件 -> 使用 PowerShell 运行；或在 PowerShell 里执行 .\build.ps1
# =====================================================================

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$src  = Join-Path $root 'src\QuickType.cs'
$manifest = Join-Path $root 'src\app.manifest'
$out  = Join-Path $root 'QuickType.exe'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) { throw '找不到 .NET Framework 4.x 的 csc.exe，请先安装 .NET Framework 4.8。' }

$fw = Split-Path -Parent $csc

Write-Host '正在编译 QuickType.exe ...' -ForegroundColor Cyan
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /debug- /utf8output `
    /win32manifest:"$manifest" `
    /out:"$out" `
    /r:"$fw\System.dll" `
    /r:"$fw\System.Core.dll" `
    /r:"$fw\System.Drawing.dll" `
    /r:"$fw\System.Windows.Forms.dll" `
    "$src"

if ($LASTEXITCODE -ne 0) { throw "编译失败（退出码 $LASTEXITCODE）" }

$size = [math]::Round((Get-Item $out).Length / 1KB, 0)
Write-Host "编译成功：$out  (${size} KB)" -ForegroundColor Green
