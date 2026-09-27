# 以 DPI 感知方式截取窗口，避免坐标虚拟化
$dir = 'C:\Users\35261\DeepSeek\QuickType'
Get-Process QuickType, TestHost -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Add-Type -Namespace W -Name U -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
[DllImport("user32.dll")] public static extern int GetDpiForWindow(IntPtr h);
public struct RECT { public int Left, Top, Right, Bottom; }
'@
[void][W.U]::SetProcessDPIAware()

Start-Process "$dir\QuickType.exe"
Start-Sleep -Seconds 4
$p = Get-Process QuickType
$h = $p.MainWindowHandle
"DPI = $([W.U]::GetDpiForWindow($h))  (96=100%, 120=125%, 144=150%)"
"屏幕分辨率 = $([W.U]::GetSystemMetrics(0)) x $([W.U]::GetSystemMetrics(1))"
[void][W.U]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 1200

$r = New-Object W.U+RECT
[void][W.U]::GetWindowRect($h, [ref]$r)
$w = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
"窗口物理尺寸 = ${w} x ${hh}  位置 = ($($r.Left),$($r.Top))"

Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap($w, $hh)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $hh)))
$bmp.Save("$dir\test\ui2.png", [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
'截图: test\ui2.png'
Get-Process QuickType -ErrorAction SilentlyContinue | Stop-Process -Force
