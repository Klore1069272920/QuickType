# 第二轮测试：片段快捷键 Ctrl+Alt+1 + 剪贴板粘贴模式 + Ctrl+Alt+Q 显示/隐藏窗口
$dir = 'C:\Users\35261\DeepSeek\QuickType'
$out = "$dir\test\received2.txt"

Remove-Item $out -ErrorAction SilentlyContinue
Get-Process QuickType, TestHost -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600

# --- 写入测试配置（片段模式 + 剪贴板粘贴）---
$cfg = @'
# test
mode=1
startdelay=200
chardelay=8
restorefocus=1
hideonclose=0
lasttext=当前文本内容
[snippet]
title=片段一
hotkey=0
text=片段内容：通过 Ctrl+Alt+1 输入 emoji 😀 结束
[snippet]
title=片段二
hotkey=1
text=这是第二个片段
'@
[System.IO.File]::WriteAllText("$dir\config.ini", $cfg, (New-Object System.Text.UTF8Encoding($true)))
'测试配置已写入'

Add-Type -AssemblyName System.Windows.Forms
Add-Type -Namespace W -Name U -MemberDefinition '[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);'

Start-Process "$dir\QuickType.exe"
Start-Sleep -Seconds 3
$p = Get-Process QuickType -ErrorAction SilentlyContinue
if (-not $p) { 'RESULT: FAIL — QuickType 没启动'; exit 1 }
"QuickType 已启动 PID=$($p.Id) 主窗口句柄=$($p.MainWindowHandle)"

Start-Process "$dir\test\TestHost.exe" -ArgumentList "`"$out`""
Start-Sleep -Seconds 3

# --- 测试 1：片段快捷键 + 剪贴板粘贴 ---
[System.Windows.Forms.SendKeys]::SendWait('^%1')
'已发送 Ctrl+Alt+1（片段一，剪贴板粘贴模式）…'
Start-Sleep -Seconds 12

$expected1 = '片段内容：通过 Ctrl+Alt+1 输入 emoji 😀 结束'
$ok1 = $false
if (Test-Path $out) {
    $got = [System.IO.File]::ReadAllText($out, [System.Text.Encoding]::UTF8)
    $g = $got -replace "`r`n", "`n"
    if ($g -ceq $expected1) { $ok1 = $true; "测试1 片段快捷键 + 剪贴板粘贴：PASS（收到 $($g.Length) 字）" }
    else { "测试1：MISMATCH`n  实际=[$g]`n  期望=[$expected1]" }
} else { '测试1：FAIL — 没有结果文件' }

# --- 测试 2：Ctrl+Alt+Q 显示 / 隐藏窗口 ---
Get-Process TestHost -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
$h1 = (Get-Process QuickType).MainWindowHandle
[System.Windows.Forms.SendKeys]::SendWait('^%q')
Start-Sleep -Seconds 1
$h2 = (Get-Process QuickType).MainWindowHandle
[System.Windows.Forms.SendKeys]::SendWait('^%q')
Start-Sleep -Seconds 1
$h3 = (Get-Process QuickType).MainWindowHandle
"测试2 Ctrl+Alt+Q：初始可见=$($h1 -ne 0) → 按一次后可见=$($h2 -ne 0) → 再按一次后可见=$($h3 -ne 0)"
if ($h1 -ne 0 -and $h2 -eq 0 -and $h3 -ne 0) { '测试2：PASS' } else { '测试2：PARTIAL/FAIL' }

# --- 测试 3：进程存活（全局快捷键句柄没被销毁）---
$alive = Get-Process QuickType -ErrorAction SilentlyContinue
"测试3 进程存活：$([bool]$alive)"
if ($ok1 -and $alive) { 'RESULT: 第二轮 PASS' } else { 'RESULT: 第二轮 有问题，见上' }

Get-Process QuickType, TestHost -ErrorAction SilentlyContinue | Stop-Process -Force
