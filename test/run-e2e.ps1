# 端到端测试：启动 QuickType（读 config.ini 里的测试文本）→ 启动测试宿主取得焦点
# → 模拟按下全局快捷键 Ctrl+Alt+Enter → 比对测试宿主输入框里收到的内容。
$dir = 'C:\Users\35261\DeepSeek\QuickType'
$out = "$dir\test\received.txt"

Remove-Item $out -ErrorAction SilentlyContinue
Get-Process QuickType, TestHost -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600

$expected = "第一行：中文与英文混排 QuickType 测试 123`n第二行：符号 !@#$%^&*()_+-=[]{}|;':"",.<>/?`n第三行：emoji 😀🚀🎉 与生僻字 龘靐齉`n第四行：结束"

# 写入本次测试用的配置（多行文本用 \n 转义，程序读入时会还原）
$cfgText = "# e2e-test`nmode=0`nstartdelay=300`nchardelay=8`nrestorefocus=1`nhideonclose=0`nlasttext=" + ($expected -replace "`n", '\n') + "`n"
[System.IO.File]::WriteAllText("$dir\config.ini", $cfgText, (New-Object System.Text.UTF8Encoding($true)))

Start-Process "$dir\QuickType.exe"
Start-Sleep -Seconds 3
$p = Get-Process QuickType -ErrorAction SilentlyContinue
if (-not $p) { 'RESULT: FAIL — QuickType 没有启动'; exit 1 }
"QuickType 已启动  PID=$($p.Id)"
"窗口标题 = [$($p.MainWindowTitle)]"
"托盘/句柄 = $($p.MainWindowHandle)"

Start-Process "$dir\test\TestHost.exe" -ArgumentList "`"$out`""
Start-Sleep -Seconds 3

Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.SendKeys]::SendWait('^%{ENTER}')
'已发送全局快捷键 Ctrl+Alt+Enter，等待逐字输入完成…'
Start-Sleep -Seconds 14

if (Test-Path $out) {
    $got = [System.IO.File]::ReadAllText($out, [System.Text.Encoding]::UTF8)
    $g = $got -replace "`r`n", "`n"
    $e = $expected -replace "`r`n", "`n"
    "收到字符数 = $($g.Length)   期望字符数 = $($e.Length)"
    if ($g -ceq $e) {
        'RESULT: PASS —— 全局快捷键触发成功，多行中文 / 英文 / 符号 / emoji / 生僻字全部正确输入'
    } else {
        'RESULT: MISMATCH'
        '--- 实际收到 ---'
        $g
        '--- 期望内容 ---'
        $e
    }
} else {
    'RESULT: FAIL —— 测试宿主没有写出结果文件（可能快捷键没触发或输入没落到输入框）'
}

Get-Process QuickType, TestHost -ErrorAction SilentlyContinue | Stop-Process -Force
