# 用 UI Automation 真实点击“存为新片段”，截取弹出的命名对话框
$dir = 'C:\Users\35261\DeepSeek\QuickType'
Get-Process QuickType -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -Namespace W -Name U -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
public struct RECT { public int Left, Top, Right, Bottom; }
'@
[void][W.U]::SetProcessDPIAware()

# 写一份干净配置
$cfg = "# ui-dialog`nmode=0`nstartdelay=300`nchardelay=8`nrestorefocus=1`nhideonclose=0`nlasttext=待保存的示例文本内容`n"
[System.IO.File]::WriteAllText("$dir\config.ini", $cfg, (New-Object System.Text.UTF8Encoding($true)))

Start-Process "$dir\QuickType.exe"
Start-Sleep -Seconds 4

$auto = [System.Windows.Automation.AutomationElement]
$main = $auto::RootElement.FindFirst(
    [System.Windows.Automation.TreeScope]::Children,
    (New-Object System.Windows.Automation.PropertyCondition($auto::NameProperty, '速录助手 QuickType — 把文本直接“敲”到光标处')))
if (-not $main) { '找不到主窗口'; exit 1 }

$btn = $main.FindFirst(
    [System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition($auto::NameProperty, '存为新片段')))
if (-not $btn) { '找不到“存为新片段”按钮'; Get-Process QuickType | Stop-Process -Force; exit 1 }

$inv = $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
$inv.Invoke()
Start-Sleep -Seconds 2

$dlg = $auto::RootElement.FindFirst(
    [System.Windows.Automation.TreeScope]::Children,
    (New-Object System.Windows.Automation.PropertyCondition($auto::NameProperty, '存为新片段')))
if (-not $dlg) { '对话框没出现'; Get-Process QuickType | Stop-Process -Force; exit 1 }

$pid2 = $dlg.Current.ProcessId
$h = (Get-Process -Id $pid2).MainWindowHandle
[void][W.U]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 800

$r = New-Object W.U+RECT
[void][W.U]::GetWindowRect($h, [ref]$r)
$w = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
"对话框尺寸 = ${w}x${hh}"
$bmp = New-Object System.Drawing.Bitmap($w, $hh)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $hh)))
$g.Dispose()
$bmp.Save("$dir\test\ui-dialog.png", [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
'已截图 test\ui-dialog.png'
Get-Process QuickType -ErrorAction SilentlyContinue | Stop-Process -Force
