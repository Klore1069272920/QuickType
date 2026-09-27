# 截图 + 配置保存回路验证
$dir = 'C:\Users\35261\DeepSeek\QuickType'
Get-Process QuickType, TestHost -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600

$cfg = @'
# ui-check
mode=0
startdelay=300
chardelay=8
restorefocus=1
hideonclose=0
lasttext=这是一段示例文本，按 Ctrl+Alt+Enter 就会自动“敲”到光标所在的位置。
[snippet]
title=常用邮箱
hotkey=0
text=yourname@example.com
[snippet]
title=常用地址
hotkey=1
text=XX省XX市XX区XX路XX号
[snippet]
title=客服话术
hotkey=2
text=您好，很高兴为您服务！
'@
[System.IO.File]::WriteAllText("$dir\config.ini", $cfg, (New-Object System.Text.UTF8Encoding($true)))
$before = (Get-Item "$dir\config.ini").LastWriteTime

Add-Type -AssemblyName System.Drawing
Add-Type -Namespace W -Name U -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
public struct RECT { public int Left, Top, Right, Bottom; }
'@

Start-Process "$dir\QuickType.exe"
Start-Sleep -Seconds 4
$p = Get-Process QuickType -ErrorAction SilentlyContinue
$h = $p.MainWindowHandle
"窗口句柄 = $h"
[void][W.U]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 1200

$r = New-Object W.U+RECT
[void][W.U]::GetWindowRect($h, [ref]$r)
$w = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
"窗口区域 = ($($r.Left),$($r.Top)) ${w}x${hh}"
$bmp = New-Object System.Drawing.Bitmap($w, $hh)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $hh)))
$png = "$dir\test\ui.png"
$bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
"截图已保存：$png"

# 关闭窗口 -> 应触发保存
[void][W.U]::PostMessage($h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
Start-Sleep -Seconds 3
$after = (Get-Item "$dir\config.ini").LastWriteTime
"配置写入时间：$before -> $after  (是否被重写: $($before -ne $after))"
"进程是否已退出: $(-not [bool](Get-Process QuickType -ErrorAction SilentlyContinue))"
'--- 保存后的 config.ini ---'
Get-Content "$dir\config.ini" -Encoding UTF8
Get-Process QuickType, TestHost -ErrorAction SilentlyContinue | Stop-Process -Force
