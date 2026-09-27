# 诊断2：剪贴板粘贴模式究竟卡在哪一步
$dir = 'C:\Users\35261\DeepSeek\QuickType'
$out = "$dir\test\received4.txt"

Remove-Item $out -ErrorAction SilentlyContinue
Get-Process QuickType, TestHost -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600

$cfg = @'
# diag2
mode=1
startdelay=200
chardelay=8
restorefocus=1
hideonclose=0
lasttext=PASTE-CURRENT-TEXT
[snippet]
title=片段一
hotkey=0
text=PASTE-SNIPPET-TEXT
'@
[System.IO.File]::WriteAllText("$dir\config.ini", $cfg, (New-Object System.Text.UTF8Encoding($true)))

Add-Type -AssemblyName System.Windows.Forms
Set-Clipboard -Value 'CLIPBOARD-ORIGINAL'
"启动前剪贴板 = [$(Get-Clipboard)]"

Start-Process "$dir\QuickType.exe"
Start-Sleep -Seconds 3
Start-Process "$dir\test\TestHost.exe" -ArgumentList "`"$out`""
Start-Sleep -Seconds 3

[System.Windows.Forms.SendKeys]::SendWait('^%1')
'已发送 Ctrl+Alt+1（剪贴板模式）'
Start-Sleep -Seconds 12

"剪贴板现在 = [$(Get-Clipboard)]"
if (Test-Path $out) { "TestHost 收到 = [$(  ([System.IO.File]::ReadAllText($out,[System.Text.Encoding]::UTF8)) -replace "`r`n"," / "  )]" }
else { 'TestHost 没写出文件' }

Get-Process QuickType, TestHost -ErrorAction SilentlyContinue | Stop-Process -Force
