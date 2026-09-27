// =====================================================================
//  速录助手 QuickType
//  把指定文本“敲”到光标所在位置，解决网页 / 软件禁止复制粘贴的问题。
//  原理：调用 Win32 SendInput(KEYEVENTF_UNICODE) 发送真实键盘事件，
//        对目标程序而言与人工逐字打字完全一致，因此不限输入框类型。
//  编译：见同目录 build.ps1（使用系统自带 csc.exe，无第三方依赖）
// =====================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using WinTimer = System.Windows.Forms.Timer;

namespace QuickType
{
    // =================================================================
    // Win32 互操作
    // =================================================================
    internal static class Win32
    {
        public const int WM_HOTKEY = 0x0312;

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        public const uint INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint KEYEVENTF_UNICODE = 0x0004;

        public const ushort VK_TAB = 0x09;
        public const ushort VK_RETURN = 0x0D;
        public const ushort VK_CONTROL = 0x11;
        public const ushort VK_V = 0x56;

        public const int SW_RESTORE = 9;

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        public static readonly int InputSize = Marshal.SizeOf(typeof(INPUT));

        /// <summary>把目标窗口切到前台（带 AttachThreadInput 兜底，成功率比 SetForegroundWindow 高）。</summary>
        public static void ForceForeground(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || !IsWindow(hWnd)) return;
            if (IsIconic(hWnd)) ShowWindow(hWnd, SW_RESTORE);
            if (GetForegroundWindow() == hWnd) return;

            uint pid;
            uint cur = GetCurrentThreadId();
            uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            uint tgThread = GetWindowThreadProcessId(hWnd, out pid);

            bool a1 = false, a2 = false;
            if (fgThread != 0 && fgThread != cur) a1 = AttachThreadInput(cur, fgThread, true);
            if (tgThread != 0 && tgThread != cur && tgThread != fgThread) a2 = AttachThreadInput(cur, tgThread, true);
            try
            {
                SetForegroundWindow(hWnd);
            }
            finally
            {
                if (a2) AttachThreadInput(cur, tgThread, false);
                if (a1) AttachThreadInput(cur, fgThread, false);
            }
        }
    }

    // =================================================================
    // 输入引擎
    // =================================================================
    internal delegate void TypeProgress(int done, int total);

    internal static class Keyboard
    {
        private static void Send(Win32.INPUT[] inputs)
        {
            Win32.SendInput((uint)inputs.Length, inputs, Win32.InputSize);
        }

        private static Win32.INPUT Key(ushort vk, uint flags)
        {
            Win32.INPUT i = new Win32.INPUT();
            i.type = Win32.INPUT_KEYBOARD;
            i.u.ki.wVk = vk;
            i.u.ki.wScan = 0;
            i.u.ki.dwFlags = flags;
            return i;
        }

        public static void SendVk(ushort vk)
        {
            Send(new Win32.INPUT[] { Key(vk, 0), Key(vk, Win32.KEYEVENTF_KEYUP) });
        }

        private static void SendUnicodeChar(char ch)
        {
            Win32.INPUT down = new Win32.INPUT();
            down.type = Win32.INPUT_KEYBOARD;
            down.u.ki.wVk = 0;
            down.u.ki.wScan = ch;
            down.u.ki.dwFlags = Win32.KEYEVENTF_UNICODE;

            Win32.INPUT up = new Win32.INPUT();
            up.type = Win32.INPUT_KEYBOARD;
            up.u.ki.wVk = 0;
            up.u.ki.wScan = ch;
            up.u.ki.dwFlags = Win32.KEYEVENTF_UNICODE | Win32.KEYEVENTF_KEYUP;

            Send(new Win32.INPUT[] { down, up });
        }

        /// <summary>Ctrl+V</summary>
        public static void SendCtrlV()
        {
            Send(new Win32.INPUT[]
            {
                Key(Win32.VK_CONTROL, 0),
                Key(Win32.VK_V, 0),
                Key(Win32.VK_V, Win32.KEYEVENTF_KEYUP),
                Key(Win32.VK_CONTROL, Win32.KEYEVENTF_KEYUP)
            });
        }

        /// <summary>
        /// 逐字符模拟键盘输入。中文、emoji、生僻字都能正确送达（UTF-16 逐码元发送）。
        /// </summary>
        public static bool TypeText(string text, int delayMs, Func<bool> isCanceled, TypeProgress progress)
        {
            // 统一换行，避免 CRLF 变成两次回车
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            int total = text.Length;

            for (int i = 0; i < total; i++)
            {
                if (isCanceled != null && isCanceled()) return false;

                char ch = text[i];
                if (ch == '\n') SendVk(Win32.VK_RETURN);
                else if (ch == '\t') SendVk(Win32.VK_TAB);
                else SendUnicodeChar(ch);

                if (delayMs > 0) Thread.Sleep(delayMs);
                if (progress != null && (i % 8 == 0 || i == total - 1)) progress(i + 1, total);
            }
            return true;
        }
    }

    // =================================================================
    // 数据模型
    // =================================================================
    internal class Snippet
    {
        public string Title = "";
        public string Text = "";
        public int Hotkey = -1;          // -1 表示无快捷键；0..9 对应 Ctrl+Alt+1..0

        public Snippet() { }

        public Snippet(string title, string text, int hotkey)
        {
            Title = title;
            Text = text;
            Hotkey = hotkey;
        }

        public static string HotkeyLabel(int index)
        {
            return index == 9 ? "0" : (index + 1).ToString();
        }

        public override string ToString()
        {
            string name = Title == null ? "" : Title.Trim();
            if (name.Length == 0)
            {
                name = Text == null ? "" : Text.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
                if (name.Length > 18) name = name.Substring(0, 18) + "…";
                if (name.Length == 0) name = "(空内容)";
            }
            string key = Hotkey >= 0 ? "Ctrl+Alt+" + HotkeyLabel(Hotkey) : "无快捷键";
            return "[" + key + "]  " + name;
        }
    }

    internal class Config
    {
        public List<Snippet> Snippets = new List<Snippet>();
        public int Mode = 0;                 // 0 = 逐字键入, 1 = 剪贴板粘贴
        public int StartDelayMs = 300;       // 开始前的等待时间
        public int CharDelayMs = 8;          // 每个字符之间的间隔
        public bool RestoreFocus = true;     // 点面板按钮时自动切回上一个窗口
        public bool HideOnClose = true;      // 关闭窗口时缩到托盘
        public string LastText = "";

        private static string _folder;

        /// <summary>配置目录：优先放 exe 旁边（便携，整个文件夹可拷走）；不可写时退回 %APPDATA%。</summary>
        public static string FolderPath
        {
            get
            {
                if (_folder != null) return _folder;
                string exeDir = "";
                try { exeDir = Path.GetDirectoryName(Application.ExecutablePath); }
                catch (Exception) { }
                try
                {
                    string probe = Path.Combine(exeDir, "quicktype.writecheck.tmp");
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);
                    _folder = exeDir;
                }
                catch (Exception)
                {
                    _folder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "QuickType");
                }
                return _folder;
            }
        }

        public static string FilePath
        {
            get { return Path.Combine(FolderPath, "config.ini"); }
        }

        public static Config CreateDefault()
        {
            Config c = new Config();
            c.Snippets.Add(new Snippet("示例：常用邮箱", "yourname@example.com", 0));
            c.Snippets.Add(new Snippet("示例：常用地址", "XX省XX市XX区XX路XX号", 1));
            c.LastText = "这是一段示例文本，按 Ctrl+Alt+Enter 就会自动“敲”到光标所在的位置。";
            return c;
        }

        // ---------- 转义（保证多行文本能存在单行里） ----------
        private static string Esc(string s)
        {
            if (s == null) return "";
            StringBuilder sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '\\') sb.Append("\\\\");
                else if (ch == '\r') sb.Append("\\r");
                else if (ch == '\n') sb.Append("\\n");
                else if (ch == '\t') sb.Append("\\t");
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        private static string Unesc(string s)
        {
            if (s == null) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '\\' && i + 1 < s.Length)
                {
                    i++;
                    char n = s[i];
                    if (n == 'n') sb.Append('\n');
                    else if (n == 'r') sb.Append('\r');
                    else if (n == 't') sb.Append('\t');
                    else if (n == '\\') sb.Append('\\');
                    else { sb.Append('\\'); sb.Append(n); }
                }
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        private static int ParseInt(string s, int fallback)
        {
            int v;
            if (int.TryParse(s, out v)) return v;
            return fallback;
        }

        public static Config Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return CreateDefault();

                Config c = new Config();
                string[] lines = File.ReadAllLines(FilePath, Encoding.UTF8);
                Snippet pending = null;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;

                    if (line == "[snippet]")
                    {
                        if (pending != null) c.Snippets.Add(pending);
                        pending = new Snippet();
                        continue;
                    }

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1);

                    if (pending != null && (key == "title" || key == "hotkey" || key == "text"))
                    {
                        if (key == "title") pending.Title = Unesc(val);
                        else if (key == "hotkey") pending.Hotkey = ParseInt(val, -1);
                        else pending.Text = Unesc(val);
                        continue;
                    }

                    if (key == "mode") c.Mode = ParseInt(val, 0);
                    else if (key == "startdelay") c.StartDelayMs = ParseInt(val, 300);
                    else if (key == "chardelay") c.CharDelayMs = ParseInt(val, 8);
                    else if (key == "restorefocus") c.RestoreFocus = ParseInt(val, 1) != 0;
                    else if (key == "hideonclose") c.HideOnClose = ParseInt(val, 1) != 0;
                    else if (key == "lasttext") c.LastText = Unesc(val);
                }
                if (pending != null) c.Snippets.Add(pending);

                // 注意：配置文件存在时，即使片段列表为空也照原样使用，
                // 只有配置文件不存在才给一份带示例的默认配置。
                if (c.Mode < 0 || c.Mode > 1) c.Mode = 0;
                if (c.StartDelayMs < 0) c.StartDelayMs = 0;
                if (c.CharDelayMs < 0) c.CharDelayMs = 0;
                return c;
            }
            catch (Exception)
            {
                return CreateDefault();
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(FolderPath);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# 速录助手 QuickType 配置文件（UTF-8）");
                sb.AppendLine("# 可以直接用记事本编辑；改完在“文件 → 重新载入配置”里生效。");
                sb.AppendLine("mode=" + Mode);
                sb.AppendLine("startdelay=" + StartDelayMs);
                sb.AppendLine("chardelay=" + CharDelayMs);
                sb.AppendLine("restorefocus=" + (RestoreFocus ? 1 : 0));
                sb.AppendLine("hideonclose=" + (HideOnClose ? 1 : 0));
                sb.AppendLine("lasttext=" + Esc(LastText));
                sb.AppendLine();

                for (int i = 0; i < Snippets.Count; i++)
                {
                    Snippet s = Snippets[i];
                    sb.AppendLine("[snippet]");
                    sb.AppendLine("title=" + Esc(s.Title));
                    sb.AppendLine("hotkey=" + s.Hotkey);
                    sb.AppendLine("text=" + Esc(s.Text));
                    sb.AppendLine();
                }
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(true));
            }
            catch (Exception)
            {
                // 配置写不进去不影响主功能，静默忽略
            }
        }
    }

    // =================================================================
    // 片段命名 / 重命名 用的小弹窗
    // =================================================================
    internal class PromptForm : Form
    {
        private TextBox _box;
        public string Value = "";

        /// <summary>与主窗口共用同一个缩放系数</summary>
        private static int S(int v)
        {
            return (int)Math.Round(v * MainForm.CurrentScale);
        }

        private PromptForm(string title, string label, string initial)
        {
            Text = title;
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(S(420), S(150));

            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.ColumnCount = 1;
            t.RowCount = 3;
            t.Padding = new Padding(14, 12, 14, 12);
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label lb = new Label();
            lb.Text = label;
            lb.AutoSize = true;

            _box = new TextBox();
            _box.Text = initial;
            _box.Dock = DockStyle.Fill;
            _box.Margin = new Padding(0, 8, 0, 12);

            FlowLayoutPanel p = new FlowLayoutPanel();
            p.Dock = DockStyle.Fill;
            p.AutoSize = true;
            p.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            p.FlowDirection = FlowDirection.RightToLeft;
            p.WrapContents = false;

            Button ok = new Button();
            ok.Text = "确定";
            ok.DialogResult = DialogResult.OK;
            ok.AutoSize = true;
            ok.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ok.Padding = new Padding(12, 6, 12, 6);
            ok.Margin = new Padding(8, 0, 0, 0);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.AutoSize = true;
            cancel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            cancel.Padding = new Padding(12, 6, 12, 6);

            p.Controls.Add(ok);
            p.Controls.Add(cancel);

            t.Controls.Add(lb, 0, 0);
            t.Controls.Add(_box, 0, 1);
            t.Controls.Add(p, 0, 2);
            Controls.Add(t);

            AcceptButton = ok;
            CancelButton = cancel;
            Shown += delegate { _box.Focus(); _box.SelectAll(); };
        }

        public static string Ask(IWin32Window owner, string title, string label, string initial)
        {
            using (PromptForm f = new PromptForm(title, label, initial))
            {
                if (f.ShowDialog(owner) == DialogResult.OK)
                {
                    f.Value = f._box.Text.Trim();
                    return f.Value;
                }
            }
            return null;
        }
    }

    // =================================================================
    // 主窗口
    // =================================================================
    internal class MainForm : Form
    {
        private const int HK_SNIPPET_BASE = 1;      // 1..10 -> 片段快捷键
        private const int HK_TYPE_CURRENT = 100;    // Ctrl+Alt+Enter
        private const int HK_TOGGLE_WINDOW = 101;   // Ctrl+Alt+Q

        private Config _cfg = new Config();
        private NotifyIcon _tray;
        private WinTimer _fgTimer;
        private Icon _appIcon;

        private TextBox txtScratch;
        private Button btnType;
        private Button btnFromClip;
        private ComboBox cboMode;
        private NumericUpDown numStartDelay;
        private NumericUpDown numCharDelay;
        private CheckBox chkRestore;
        private CheckBox chkHideOnClose;
        private ListBox lstSnippets;
        private ComboBox cboHotkey;
        private ToolStripStatusLabel lblStatus;
        private Label lblTipList;
        private Label lblTipHot;
        private Label lblTipOpt;

        private volatile bool _typing;
        private volatile bool _cancel;
        private IntPtr _lastForeground = IntPtr.Zero;
        private bool _balloonShown;
        private bool _loadingUi;
        private bool _reallyExit;

        /// <summary>当前屏幕缩放系数（1.0 = 100%，1.5 = 150%），供别处复用</summary>
        public static float CurrentScale = 1f;

        private float _scale = 1f;

        /// <summary>把 96 DPI 下的设计尺寸换算成当前 DPI 的实际像素</summary>
        private int S(int v)
        {
            return (int)Math.Round(v * _scale);
        }

        public MainForm()
        {
            _cfg = Config.Load();
            BuildUi();
            LoadConfigIntoUi();
        }

        // ============================================================
        // 界面构建
        //
        // 排版原则：
        //  · 一律用 TableLayoutPanel / FlowLayoutPanel / Dock / Percent / AutoSize，
        //    不用“绝对像素坐标 + Anchor”；
        //  · 显示的字体用磅值，会自动随 DPI 放大；
        //  · 少数必须写死的像素尺寸（窗口大小、按钮内边距、数字框宽度）统一经过 S() 换算。
        // 特别注意：不要用 AutoScaleMode.Dpi —— 在多层嵌套容器里它会把
        // Margin / MinimumSize / Width 反复套用缩放系数，实测放大了 16 倍，
        // 结果就是行距巨大、控件被挤成一条线。
        // ============================================================
        private void BuildUi()
        {
            _appIcon = CreateAppIcon();
            try
            {
                using (Graphics g = CreateGraphics()) _scale = g.DpiX / 96f;
            }
            catch (Exception) { _scale = 1f; }
            if (_scale < 1f) _scale = 1f;
            CurrentScale = _scale;

            SuspendLayout();
            Text = "速录助手 QuickType — 把文本直接“敲”到光标处";
            Icon = _appIcon;
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(S(900), S(660));
            MinimumSize = new Size(S(600), S(440));
            StartPosition = FormStartPosition.CenterScreen;

            // ---------- 菜单 ----------
            MenuStrip menu = new MenuStrip();
            menu.Font = Font;

            ToolStripMenuItem mFile = new ToolStripMenuItem("文件(&F)");
            mFile.DropDownItems.Add(new ToolStripMenuItem("打开配置文件夹", null, delegate { OpenConfigFolder(); }));
            mFile.DropDownItems.Add(new ToolStripMenuItem("重新载入配置", null, delegate { ReloadConfig(); }));
            mFile.DropDownItems.Add(new ToolStripMenuItem("保存配置", null, delegate { SaveConfig(); SetStatus("配置已保存"); }));
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add(new ToolStripMenuItem("退出", null, delegate { ExitApp(); }));

            ToolStripMenuItem mHelp = new ToolStripMenuItem("帮助(&H)");
            mHelp.DropDownItems.Add(new ToolStripMenuItem("使用说明", null, delegate { ShowHelp(); }));
            mHelp.DropDownItems.Add(new ToolStripMenuItem("关于", null, delegate { ShowAbout(); }));

            menu.Items.Add(mFile);
            menu.Items.Add(mHelp);
            MainMenuStrip = menu;

            // ---------- 底部状态栏 ----------
            StatusStrip status = new StatusStrip();
            status.Font = Font;
            lblStatus = new ToolStripStatusLabel("就绪");
            lblStatus.Spring = true;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            status.Items.Add(lblStatus);

            // ---------- 根布局：左右两栏 ----------
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 2;
            root.RowCount = 1;
            root.Padding = new Padding(8, 6, 8, 6);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.Controls.Add(BuildLeftPanel(), 0, 0);
            root.Controls.Add(BuildRightPanel(), 1, 0);

            Controls.Add(root);
            Controls.Add(status);
            Controls.Add(menu);

            // ---------- 托盘图标 ----------
            ContextMenuStrip trayMenu = new ContextMenuStrip();
            trayMenu.Font = Font;
            trayMenu.Items.Add("显示 / 隐藏窗口 (Ctrl+Alt+Q)", null, delegate { ToggleWindow(); });
            trayMenu.Items.Add("输入当前文本 (Ctrl+Alt+Enter)", null, delegate { TypeCurrent(false); });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("使用说明", null, delegate { ShowHelp(); });
            trayMenu.Items.Add("退出", null, delegate { ExitApp(); });

            _tray = new NotifyIcon();
            _tray.Icon = _appIcon;
            _tray.Text = "速录助手 QuickType";
            _tray.ContextMenuStrip = trayMenu;
            _tray.Visible = true;
            _tray.DoubleClick += delegate { ToggleWindow(); };

            // ---------- 记录“上一个前台窗口”，供点击面板按钮时切回焦点 ----------
            _fgTimer = new WinTimer();
            _fgTimer.Interval = 400;
            _fgTimer.Tick += delegate { RememberForeground(); };
            _fgTimer.Start();
            Deactivate += delegate { RememberForeground(); };

            ResumeLayout(false);
            PerformLayout();
        }

        /// <summary>左栏：要输入的文本 + 输入设置</summary>
        private Control BuildLeftPanel()
        {
            GroupBox box = new GroupBox();
            box.Text = "要输入的文本（中文 / emoji / 换行都能输入）";
            box.Dock = DockStyle.Fill;
            box.Padding = new Padding(10, 4, 10, 10);
            box.Margin = new Padding(0, 0, 6, 0);

            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.ColumnCount = 1;
            t.RowCount = 3;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));   // 文本编辑区
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // 按钮
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // 设置
            t.Margin = new Padding(0);

            txtScratch = new TextBox();
            txtScratch.Multiline = true;
            txtScratch.ScrollBars = ScrollBars.Vertical;
            txtScratch.AcceptsReturn = true;
            txtScratch.WordWrap = true;
            txtScratch.Dock = DockStyle.Fill;
            txtScratch.Margin = new Padding(0, 6, 0, 0);

            FlowLayoutPanel pBtn = new FlowLayoutPanel();
            pBtn.Dock = DockStyle.Fill;
            pBtn.AutoSize = true;
            pBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            pBtn.WrapContents = false;
            pBtn.Margin = new Padding(0, 8, 0, 0);

            btnType = new Button();
            btnType.Text = "▶  输入到光标处";
            btnType.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            btnType.AutoSize = true;
            btnType.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btnType.Padding = new Padding(S(14), S(8), S(14), S(8));
            btnType.Margin = new Padding(0, 0, 8, 0);
            btnType.Click += delegate { TypeCurrent(true); };

            btnFromClip = new Button();
            btnFromClip.Text = "读入剪贴板";
            btnFromClip.AutoSize = true;
            btnFromClip.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btnFromClip.Padding = new Padding(S(12), S(8), S(12), S(8));
            btnFromClip.Click += delegate { LoadFromClipboard(); };

            pBtn.Controls.Add(btnType);
            pBtn.Controls.Add(btnFromClip);

            t.Controls.Add(txtScratch, 0, 0);
            t.Controls.Add(pBtn, 0, 1);
            t.Controls.Add(BuildOptionsSection(), 0, 2);
            box.Controls.Add(t);
            return box;
        }

        /// <summary>
        /// 左下：输入设置。
        /// 关键点：
        ///  1) 末行放一个 Percent 100 的空行当“吸收器”，父容器给的多余高度全被它吃掉，
        ///     上面的内容行就不会被拉伸（纯 AutoSize 行会被平均撑开，行距变得很大）；
        ///  2) NumericUpDown 必须 AutoSize=false，否则宽度会塌成一条线。
        /// </summary>
        private Control BuildOptionsSection()
        {
            GroupBox box = new GroupBox();
            box.Text = "输入设置";
            box.Dock = DockStyle.Fill;
            box.AutoSize = true;
            box.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            box.Padding = new Padding(10, 4, 10, 10);
            box.Margin = new Padding(0, 14, 0, 0);

            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.ColumnCount = 3;
            t.RowCount = 7;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(96)));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 6; i++) t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));   // 吸收多余空间
            t.Margin = new Padding(0);

            cboMode = new ComboBox();
            cboMode.DropDownStyle = ComboBoxStyle.DropDownList;
            cboMode.Dock = DockStyle.Fill;
            cboMode.Margin = new Padding(0, 4, 0, 4);
            cboMode.Items.Add("逐字模拟输入（推荐）");
            cboMode.Items.Add("剪贴板粘贴 Ctrl+V");
            cboMode.SelectedIndexChanged += delegate { if (!_loadingUi) SaveConfig(); };

            numStartDelay = MakeSpin(96);
            numStartDelay.DecimalPlaces = 1;
            numStartDelay.Increment = 0.5m;
            numStartDelay.Minimum = 0m;
            numStartDelay.Maximum = 20m;
            numStartDelay.ValueChanged += delegate { if (!_loadingUi) SaveConfig(); };

            numCharDelay = MakeSpin(96);
            numCharDelay.Minimum = 0m;
            numCharDelay.Maximum = 300m;
            numCharDelay.Increment = 5m;
            numCharDelay.ValueChanged += delegate { if (!_loadingUi) SaveConfig(); };

            chkRestore = new CheckBox();
            chkRestore.Text = "点本窗口按钮时，自动把焦点切回上一个窗口";
            chkRestore.AutoSize = true;
            chkRestore.Margin = new Padding(0, 8, 0, 0);
            chkRestore.CheckedChanged += delegate { if (!_loadingUi) SaveConfig(); };

            chkHideOnClose = new CheckBox();
            chkHideOnClose.Text = "关闭窗口时最小化到托盘，后台继续待命";
            chkHideOnClose.AutoSize = true;
            chkHideOnClose.Margin = new Padding(0, 4, 0, 0);
            chkHideOnClose.CheckedChanged += delegate { if (!_loadingUi) SaveConfig(); };

            lblTipOpt = new Label();
            lblTipOpt.Text = "开始前等待：留出把光标点到目标输入框的时间。字符间隔：个别输入框漏字就调大。"
                           + "输入过程中再按一次 Ctrl+Alt+Enter 可立即中止。";
            lblTipOpt.ForeColor = Color.FromArgb(110, 110, 110);
            lblTipOpt.AutoSize = true;
            lblTipOpt.Margin = new Padding(0, 10, 0, 0);

            t.Controls.Add(MakeFieldLabel("输入方式："), 0, 0);
            t.Controls.Add(cboMode, 1, 0);
            t.SetColumnSpan(cboMode, 2);
            t.Controls.Add(MakeFieldLabel("开始前等待："), 0, 1);
            t.Controls.Add(numStartDelay, 1, 1);
            t.Controls.Add(MakeUnitLabel("秒"), 2, 1);
            t.Controls.Add(MakeFieldLabel("字符间隔："), 0, 2);
            t.Controls.Add(numCharDelay, 1, 2);
            t.Controls.Add(MakeUnitLabel("毫秒"), 2, 2);
            t.Controls.Add(chkRestore, 0, 3);
            t.SetColumnSpan(chkRestore, 3);
            t.Controls.Add(chkHideOnClose, 0, 4);
            t.SetColumnSpan(chkHideOnClose, 3);
            t.Controls.Add(lblTipOpt, 0, 5);
            t.SetColumnSpan(lblTipOpt, 3);

            box.Controls.Add(t);
            return box;
        }

        /// <summary>字段名标签：统一最小宽度，保证各行对齐</summary>
        private static Label MakeFieldLabel(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.MinimumSize = new Size(104, 0);
            l.Margin = new Padding(0, 8, 6, 0);
            return l;
        }

        private static Label MakeUnitLabel(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Margin = new Padding(0, 8, 0, 0);
            return l;
        }

        /// <summary>
        /// 数字输入框。NumericUpDown 默认 AutoSize=true，放进表格里宽度会塌成一条线；
        /// 这里关掉 AutoSize 并用 Minimum/MaximumSize 把尺寸钉死，双保险。
        /// </summary>
        private NumericUpDown MakeSpin(int width)
        {
            NumericUpDown n = new NumericUpDown();
            n.AutoSize = false;
            int w = S(width);
            int h = n.Font.Height + S(8);
            n.MinimumSize = new Size(w, h);
            n.MaximumSize = new Size(w, h);
            n.Size = new Size(w, h);
            n.Margin = new Padding(0, 3, 8, 0);
            return n;
        }

        /// <summary>右栏：常用片段</summary>
        private Control BuildRightPanel()
        {
            GroupBox box = new GroupBox();
            box.Text = "常用文本片段（Ctrl+Alt+数字 一键输入）";
            box.Dock = DockStyle.Fill;
            box.Padding = new Padding(10, 4, 10, 10);
            box.Margin = new Padding(6, 0, 0, 0);

            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.ColumnCount = 1;
            t.RowCount = 5;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // 提示
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));   // 列表
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // 按钮
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // 快捷键
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // 说明
            t.Margin = new Padding(0);

            lblTipList = new Label();
            lblTipList.Text = "双击列表项可载入到左侧编辑框；右键可直接输入 / 重命名 / 删除。";
            lblTipList.ForeColor = Color.FromArgb(110, 110, 110);
            lblTipList.AutoSize = true;
            lblTipList.Margin = new Padding(0, 6, 0, 6);

            lstSnippets = new ListBox();
            lstSnippets.Dock = DockStyle.Fill;
            lstSnippets.IntegralHeight = false;
            lstSnippets.HorizontalScrollbar = true;
            lstSnippets.Margin = new Padding(0, 0, 0, 8);
            lstSnippets.SelectedIndexChanged += delegate { OnSnippetSelected(); };
            lstSnippets.DoubleClick += delegate { LoadSelectedSnippet(); };
            lstSnippets.MouseDown += OnListMouseDown;

            ContextMenuStrip listMenu = new ContextMenuStrip();
            listMenu.Font = Font;
            listMenu.Items.Add("▶ 立即输入到光标处", null, delegate { TypeSelectedSnippet(true); });
            listMenu.Items.Add("载入到编辑框", null, delegate { LoadSelectedSnippet(); });
            listMenu.Items.Add("重命名", null, delegate { RenameSelectedSnippet(); });
            listMenu.Items.Add(new ToolStripSeparator());
            listMenu.Items.Add("删除", null, delegate { DeleteSelectedSnippet(); });
            lstSnippets.ContextMenuStrip = listMenu;

            FlowLayoutPanel pBtns = new FlowLayoutPanel();
            pBtns.Dock = DockStyle.Fill;
            pBtns.AutoSize = true;
            pBtns.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            pBtns.WrapContents = true;
            pBtns.Margin = new Padding(0, 8, 0, 0);

            pBtns.Controls.Add(MakeButton("存为新片段", delegate { AddSnippet(); }));
            pBtns.Controls.Add(MakeButton("更新选中", delegate { UpdateSelectedSnippet(); }));
            pBtns.Controls.Add(MakeButton("▶ 输入", delegate { TypeSelectedSnippet(true); }));
            pBtns.Controls.Add(MakeButton("删除", delegate { DeleteSelectedSnippet(); }));
            pBtns.Controls.Add(MakeButton("▲", delegate { MoveSnippet(-1); }));
            pBtns.Controls.Add(MakeButton("▼", delegate { MoveSnippet(1); }));

            FlowLayoutPanel pHot = new FlowLayoutPanel();
            pHot.Dock = DockStyle.Fill;
            pHot.AutoSize = true;
            pHot.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            pHot.WrapContents = false;
            pHot.Margin = new Padding(0, 10, 0, 0);

            Label lHot = new Label();
            lHot.Text = "该片段快捷键：";
            lHot.AutoSize = true;
            lHot.Margin = new Padding(0, 8, 6, 0);

            cboHotkey = new ComboBox();
            cboHotkey.DropDownStyle = ComboBoxStyle.DropDownList;
            cboHotkey.Width = S(170);
            cboHotkey.Margin = new Padding(0, 3, 0, 0);
            cboHotkey.Items.Add("无快捷键");
            for (int i = 0; i < 10; i++) cboHotkey.Items.Add("Ctrl+Alt+" + Snippet.HotkeyLabel(i));
            cboHotkey.SelectedIndexChanged += delegate { if (!_loadingUi) ApplyHotkeyToSelected(); };

            pHot.Controls.Add(lHot);
            pHot.Controls.Add(cboHotkey);

            lblTipHot = new Label();
            lblTipHot.Text = "快捷键是全局的：在任意网页 / 软件的输入框里直接按就能输入，不必先打开本窗口；"
                           + "若组合被其它程序占用，会在窗口底部状态栏提示。";
            lblTipHot.ForeColor = Color.FromArgb(110, 110, 110);
            lblTipHot.AutoSize = true;
            lblTipHot.Margin = new Padding(0, 8, 0, 0);

            t.Controls.Add(lblTipList, 0, 0);
            t.Controls.Add(lstSnippets, 0, 1);
            t.Controls.Add(pBtns, 0, 2);
            t.Controls.Add(pHot, 0, 3);
            t.Controls.Add(lblTipHot, 0, 4);
            box.Controls.Add(t);
            return box;
        }

        private static Button MakeButton(string text, EventHandler onClick)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Margin = new Padding(0, 0, 8, 0);
            b.Click += onClick;
            return b;
        }

        private Icon CreateAppIcon()
        {
            Bitmap bmp = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(37, 99, 235)))
                    g.FillEllipse(b, 1, 1, 30, 30);
                using (Font f = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush w = new SolidBrush(Color.White))
                {
                    StringFormat sf = new StringFormat();
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString("输", f, w, new RectangleF(0, 1, 32, 31), sf);
                }
            }
            IntPtr h = bmp.GetHicon();
            Icon ic = (Icon)Icon.FromHandle(h).Clone();
            Win32.DestroyIcon(h);
            bmp.Dispose();
            return ic;
        }

        // ------------------------------------------------------------
        // 生命周期
        // ------------------------------------------------------------
        // 全局快捷键绑定在窗口句柄上，所以句柄一建立就立刻注册
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterHotkeys(false);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RegisterHotkeys(true);
            SetStatus("就绪　|　Ctrl+Alt+Enter 输入当前文本　|　Ctrl+Alt+1~0 输入片段　|　Ctrl+Alt+Q 显示/隐藏");
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            FitTipWidths();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            FitTipWidths();
        }

        /// <summary>让长提示文字按列宽自动换行，避免撑宽窗口或被截断。</summary>
        private void FitTipWidths()
        {
            Fit(lblTipList);
            Fit(lblTipHot);
            Fit(lblTipOpt);
        }

        private static void Fit(Label l)
        {
            if (l == null || l.Parent == null) return;
            int w = l.Parent.ClientSize.Width - l.Margin.Horizontal - 4;
            if (w < 80) w = 80;
            if (l.MaximumSize.Width != w) l.MaximumSize = new Size(w, 0);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_reallyExit && e.CloseReason == CloseReason.UserClosing && chkHideOnClose.Checked)
            {
                e.Cancel = true;
                Hide();
                if (!_balloonShown)
                {
                    _balloonShown = true;
                    _tray.ShowBalloonTip(4000, "速录助手还在后台运行",
                        "双击托盘图标（或按 Ctrl+Alt+Q）可以重新打开窗口；右键托盘图标可退出。",
                        ToolTipIcon.Info);
                }
                return;
            }
            SaveConfig();
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { UnregisterAll(); } catch (Exception) { }
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            base.OnFormClosed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Win32.WM_HOTKEY)
            {
                HandleHotkey(m.WParam.ToInt32());
            }
            base.WndProc(ref m);
        }

        private void HandleHotkey(int id)
        {
            if (id == HK_TYPE_CURRENT)
            {
                if (_typing) { _cancel = true; SetStatus("正在停止…"); }
                else TypeCurrent(false);
                return;
            }
            if (id == HK_TOGGLE_WINDOW) { ToggleWindow(); return; }

            int slot = id - HK_SNIPPET_BASE;
            if (slot < 0 || slot > 9) return;
            for (int i = 0; i < _cfg.Snippets.Count; i++)
            {
                if (_cfg.Snippets[i].Hotkey == slot)
                {
                    TypeNow(_cfg.Snippets[i].Text, false, "片段：" + _cfg.Snippets[i].ToString());
                    return;
                }
            }
        }

        // ------------------------------------------------------------
        // 快捷键注册
        // ------------------------------------------------------------
        private static uint VkForSlot(int slot)
        {
            return slot == 9 ? (uint)0x30 : (uint)(0x31 + slot);
        }

        private void UnregisterAll()
        {
            for (int i = 0; i < 10; i++) Win32.UnregisterHotKey(Handle, HK_SNIPPET_BASE + i);
            Win32.UnregisterHotKey(Handle, HK_TYPE_CURRENT);
            Win32.UnregisterHotKey(Handle, HK_TOGGLE_WINDOW);
        }

        private void RegisterHotkeys(bool report)
        {
            UnregisterAll();
            uint mods = Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_NOREPEAT;
            List<string> fails = new List<string>();

            for (int i = 0; i < _cfg.Snippets.Count; i++)
            {
                Snippet s = _cfg.Snippets[i];
                if (s.Hotkey < 0 || s.Hotkey > 9) continue;
                if (!Win32.RegisterHotKey(Handle, HK_SNIPPET_BASE + s.Hotkey, mods, VkForSlot(s.Hotkey)))
                    fails.Add("Ctrl+Alt+" + Snippet.HotkeyLabel(s.Hotkey));
            }
            if (!Win32.RegisterHotKey(Handle, HK_TYPE_CURRENT, mods, Win32.VK_RETURN))
                fails.Add("Ctrl+Alt+Enter");
            if (!Win32.RegisterHotKey(Handle, HK_TOGGLE_WINDOW, mods, 0x51))
                fails.Add("Ctrl+Alt+Q");

            if (!report) return;
            if (fails.Count == 0)
            {
                SetStatus("快捷键已就绪　|　Ctrl+Alt+Enter 输入当前文本　|　Ctrl+Alt+1~0 输入片段　|　Ctrl+Alt+Q 显示/隐藏");
            }
            else
            {
                SetStatus("以下快捷键被其它程序占用，注册失败：" + string.Join("、", fails.ToArray()));
            }
        }

        // ------------------------------------------------------------
        // 配置 <-> 界面
        // ------------------------------------------------------------
        private void LoadConfigIntoUi()
        {
            _loadingUi = true;
            try
            {
                cboMode.SelectedIndex = _cfg.Mode == 1 ? 1 : 0;
                decimal sd = (decimal)_cfg.StartDelayMs / 1000m;
                if (sd < numStartDelay.Minimum) sd = numStartDelay.Minimum;
                if (sd > numStartDelay.Maximum) sd = numStartDelay.Maximum;
                numStartDelay.Value = sd;
                decimal cd = _cfg.CharDelayMs;
                if (cd < numCharDelay.Minimum) cd = numCharDelay.Minimum;
                if (cd > numCharDelay.Maximum) cd = numCharDelay.Maximum;
                numCharDelay.Value = cd;
                chkRestore.Checked = _cfg.RestoreFocus;
                chkHideOnClose.Checked = _cfg.HideOnClose;
                txtScratch.Text = _cfg.LastText == null ? "" : _cfg.LastText;
                RebuildList(0);
            }
            finally
            {
                _loadingUi = false;
            }
        }

        private void CollectUi()
        {
            _cfg.Mode = cboMode.SelectedIndex == 1 ? 1 : 0;
            _cfg.StartDelayMs = (int)Math.Round(numStartDelay.Value * 1000m);
            _cfg.CharDelayMs = (int)numCharDelay.Value;
            _cfg.RestoreFocus = chkRestore.Checked;
            _cfg.HideOnClose = chkHideOnClose.Checked;
            if (txtScratch != null) _cfg.LastText = txtScratch.Text;
        }

        private void SaveConfig()
        {
            CollectUi();
            _cfg.Save();
        }

        private void ReloadConfig()
        {
            _cfg = Config.Load();
            LoadConfigIntoUi();
            RegisterHotkeys(true);
            SetStatus("已重新载入配置");
        }

        private void OpenConfigFolder()
        {
            try
            {
                SaveConfig();
                Directory.CreateDirectory(Config.FolderPath);
                Process.Start("explorer.exe", Config.FolderPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "打不开配置文件夹：" + ex.Message, "速录助手",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ------------------------------------------------------------
        // 片段列表
        // ------------------------------------------------------------
        private void RebuildList(int selectIndex)
        {
            int keep = selectIndex;
            lstSnippets.BeginUpdate();
            lstSnippets.Items.Clear();
            for (int i = 0; i < _cfg.Snippets.Count; i++) lstSnippets.Items.Add(_cfg.Snippets[i]);
            lstSnippets.EndUpdate();

            if (lstSnippets.Items.Count == 0)
            {
                cboHotkey.SelectedIndex = 0;
                cboHotkey.Enabled = false;
                return;
            }
            cboHotkey.Enabled = true;
            if (keep < 0) keep = 0;
            if (keep >= lstSnippets.Items.Count) keep = lstSnippets.Items.Count - 1;
            lstSnippets.SelectedIndex = keep;
        }

        private Snippet SelectedSnippet()
        {
            int i = lstSnippets.SelectedIndex;
            if (i < 0 || i >= _cfg.Snippets.Count) return null;
            return _cfg.Snippets[i];
        }

        private void OnSnippetSelected()
        {
            if (_loadingUi) return;
            Snippet s = SelectedSnippet();
            _loadingUi = true;
            try
            {
                cboHotkey.SelectedIndex = (s == null || s.Hotkey < 0) ? 0 : s.Hotkey + 1;
            }
            finally
            {
                _loadingUi = false;
            }
        }

        private void OnListMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            int idx = lstSnippets.IndexFromPoint(e.Location);
            if (idx >= 0) lstSnippets.SelectedIndex = idx;
        }

        private static string AutoTitle(string text)
        {
            string t = text == null ? "" : text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (t.Length > 12) t = t.Substring(0, 12);
            if (t.Length == 0) t = "未命名片段";
            return t;
        }

        private int NextFreeHotkey()
        {
            for (int slot = 0; slot < 10; slot++)
            {
                bool used = false;
                for (int i = 0; i < _cfg.Snippets.Count; i++)
                    if (_cfg.Snippets[i].Hotkey == slot) { used = true; break; }
                if (!used) return slot;
            }
            return -1;
        }

        private void AddSnippet()
        {
            string text = txtScratch.Text;
            if (text == null || text.Trim().Length == 0)
            {
                SetStatus("左侧文本是空的，先写点内容再存为片段");
                return;
            }
            string title = PromptForm.Ask(this, "存为新片段", "给这个片段起个名字（只用于显示）：", AutoTitle(text));
            if (title == null) return;
            if (title.Length == 0) title = AutoTitle(text);

            Snippet s = new Snippet(title, text, NextFreeHotkey());
            _cfg.Snippets.Add(s);
            SaveConfig();
            RegisterHotkeys(false);
            RebuildList(_cfg.Snippets.Count - 1);
            if (s.Hotkey >= 0)
                SetStatus("已保存片段「" + title + "」，快捷键 Ctrl+Alt+" + Snippet.HotkeyLabel(s.Hotkey));
            else
                SetStatus("已保存片段「" + title + "」（快捷键位已用完，可在下方手动指定）");
        }

        private void UpdateSelectedSnippet()
        {
            Snippet s = SelectedSnippet();
            if (s == null) { SetStatus("请先在右侧列表里选中一个片段"); return; }
            s.Text = txtScratch.Text;
            SaveConfig();
            RebuildList(lstSnippets.SelectedIndex);
            SetStatus("已用左侧文本更新片段「" + s.Title + "」");
        }

        private void RenameSelectedSnippet()
        {
            Snippet s = SelectedSnippet();
            if (s == null) return;
            string title = PromptForm.Ask(this, "重命名片段", "片段名称：", s.Title);
            if (title == null || title.Length == 0) return;
            s.Title = title;
            SaveConfig();
            RebuildList(lstSnippets.SelectedIndex);
        }

        private void DeleteSelectedSnippet()
        {
            Snippet s = SelectedSnippet();
            if (s == null) { SetStatus("请先选中一个片段"); return; }
            if (MessageBox.Show(this, "确定删除片段「" + s.Title + "」？", "速录助手",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            int idx = lstSnippets.SelectedIndex;
            _cfg.Snippets.RemoveAt(idx);
            SaveConfig();
            RegisterHotkeys(false);
            RebuildList(idx >= _cfg.Snippets.Count ? _cfg.Snippets.Count - 1 : idx);
            SetStatus("已删除片段");
        }

        private void MoveSnippet(int delta)
        {
            int i = lstSnippets.SelectedIndex;
            int j = i + delta;
            if (i < 0 || i >= _cfg.Snippets.Count) return;
            if (j < 0 || j >= _cfg.Snippets.Count) return;
            Snippet tmp = _cfg.Snippets[i];
            _cfg.Snippets[i] = _cfg.Snippets[j];
            _cfg.Snippets[j] = tmp;
            SaveConfig();
            RebuildList(j);
        }

        private void ApplyHotkeyToSelected()
        {
            Snippet s = SelectedSnippet();
            if (s == null) return;
            s.Hotkey = cboHotkey.SelectedIndex <= 0 ? -1 : cboHotkey.SelectedIndex - 1;
            SaveConfig();
            RegisterHotkeys(false);
            int idx = lstSnippets.SelectedIndex;
            RebuildList(idx);
            SetStatus(s.Hotkey < 0
                ? "已取消该片段的快捷键"
                : "该片段已绑定 Ctrl+Alt+" + Snippet.HotkeyLabel(s.Hotkey));
        }

        private void LoadSelectedSnippet()
        {
            Snippet s = SelectedSnippet();
            if (s == null) return;
            txtScratch.Text = s.Text;
            txtScratch.Focus();
            SetStatus("已载入片段「" + s.Title + "」到编辑框，可修改后点“更新选中”");
        }

        private void TypeSelectedSnippet(bool fromButton)
        {
            Snippet s = SelectedSnippet();
            if (s == null) { SetStatus("请先选中一个片段"); return; }
            TypeNow(s.Text, fromButton, "片段：" + s.Title);
        }

        private void LoadFromClipboard()
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    txtScratch.Text = Clipboard.GetText();
                    SetStatus("已把剪贴板内容读入编辑框");
                }
                else SetStatus("剪贴板里没有文本");
            }
            catch (Exception)
            {
                SetStatus("读取剪贴板失败，请稍后再试");
            }
        }

        // ------------------------------------------------------------
        // 输入动作
        // ------------------------------------------------------------
        private void TypeCurrent(bool fromButton)
        {
            TypeNow(txtScratch.Text, fromButton, "当前文本");
        }

        private void TypeNow(string text, bool fromButton, string what)
        {
            if (_typing) { _cancel = true; SetStatus("正在停止…"); return; }
            if (text == null || text.Length == 0)
            {
                SetStatus("没有可输入的内容");
                return;
            }

            CollectUi();
            int mode = _cfg.Mode;
            int startDelay = _cfg.StartDelayMs;
            int charDelay = _cfg.CharDelayMs;
            IntPtr target = (fromButton && _cfg.RestoreFocus) ? _lastForeground : IntPtr.Zero;

            if (mode == 1 && !SetClipboardText(text))
            {
                // 剪贴板常被别的程序（输入法、剪贴板管理器、Office）占用，
                // 写不进去就自动降级成逐字输入，保证文字一定送达。
                mode = 0;
                SetStatus("剪贴板被其它程序占用，本次自动改用逐字输入…");
            }

            _typing = true;
            _cancel = false;
            btnType.Text = "■  停止输入";

            Thread t = new Thread(delegate() { Worker(text, mode, startDelay, charDelay, target, what); });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        private void Worker(string text, int mode, int startDelayMs, int charDelayMs, IntPtr target, string what)
        {
            try
            {
                if (target != IntPtr.Zero)
                {
                    Win32.ForceForeground(target);
                    Thread.Sleep(150);
                }

                int remain = startDelayMs;
                while (remain > 0 && !_cancel)
                {
                    int step = remain > 100 ? 100 : remain;
                    int shown = remain;
                    Ui(delegate { SetStatus("准备输入「" + what + "」… " + ((shown + 999) / 1000) + " 秒后开始，请把光标点到目标位置"); });
                    Thread.Sleep(step);
                    remain -= step;
                }

                if (_cancel) { Ui(delegate { SetStatus("已取消输入"); }); return; }

                if (mode == 1)
                {
                    Ui(delegate { SetStatus("正在粘贴「" + what + "」…"); });
                    Keyboard.SendCtrlV();
                }
                else
                {
                    Ui(delegate { SetStatus("正在输入「" + what + "」…"); });
                    bool ok = Keyboard.TypeText(text, charDelayMs,
                        delegate { return _cancel; },
                        delegate(int done, int total)
                        {
                            if (done == total || done % 24 == 0)
                                Ui(delegate { SetStatus("正在输入「" + what + "」… " + done + "/" + total); });
                        });
                    if (!ok) { Ui(delegate { SetStatus("已中止输入"); }); return; }
                }

                Ui(delegate { SetStatus("✔ 已输入 " + text.Length + " 个字符到光标位置"); });
            }
            catch (Exception ex)
            {
                string msg = ex.Message;
                Ui(delegate { SetStatus("输入出错：" + msg); });
            }
            finally
            {
                _typing = false;
                _cancel = false;
                Ui(delegate { btnType.Text = "▶  输入到光标处"; });
            }
        }

        private bool SetClipboardText(string text)
        {
            // 剪贴板是全局共享资源，可能被其它程序短暂锁住，必须多重试几次
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    DataObject data = new DataObject();
                    data.SetData(DataFormats.UnicodeText, text);
                    Clipboard.SetDataObject(data, true, 10, 80);
                    return true;
                }
                catch (Exception)
                {
                    try
                    {
                        Clipboard.SetText(text);
                        return true;
                    }
                    catch (Exception) { }
                    Thread.Sleep(80);
                }
            }
            return false;
        }

        // ------------------------------------------------------------
        // 杂项
        // ------------------------------------------------------------
        private void Ui(MethodInvoker action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void SetStatus(string text)
        {
            if (lblStatus != null) lblStatus.Text = text;
        }

        private void RememberForeground()
        {
            try
            {
                IntPtr h = Win32.GetForegroundWindow();
                if (h == IntPtr.Zero) return;
                uint pid;
                Win32.GetWindowThreadProcessId(h, out pid);
                if (pid == (uint)Process.GetCurrentProcess().Id) return;
                _lastForeground = h;
            }
            catch (Exception) { }
        }

        // 注意：不要修改 ShowInTaskbar —— 它会重建窗口句柄，导致已注册的全局快捷键失效。
        private void ToggleWindow()
        {
            if (Visible && WindowState != FormWindowState.Minimized)
            {
                Hide();
            }
            else
            {
                Show();
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
                BringToFront();
            }
        }

        private void ExitApp()
        {
            _reallyExit = true;
            Close();
        }

        private void ShowHelp()
        {
            string msg =
                "【怎么用】\r\n" +
                "1. 在左侧写下要输入的文本（中文、emoji、多行都行），或从右侧列表选一个常用片段。\r\n" +
                "2. 把光标点进目标输入框（网页、微信、软件登录框……）。\r\n" +
                "3. 按 Ctrl+Alt+Enter，文字就会被逐字“敲”进去，和人工打字完全一样。\r\n\r\n" +
                "【快捷键】\r\n" +
                "Ctrl+Alt+Enter  输入左侧编辑框里的当前文本（再按一次可中止）\r\n" +
                "Ctrl+Alt+1 ~ 0  直接输入对应编号的常用片段，无需打开本窗口\r\n" +
                "Ctrl+Alt+Q      显示 / 隐藏本窗口\r\n\r\n" +
                "【为什么能绕过“禁止复制粘贴”】\r\n" +
                "“逐字输入”模式调用的是 Windows 标准键盘事件接口（SendInput），\r\n" +
                "对目标网页来说等同于你本人一个键一个键地敲，没有任何粘贴动作，因此不会被拦截。\r\n" +
                "如果某个输入框反应太快漏字，把“字符间隔”调大一点（例如 20~50 毫秒）即可。\r\n\r\n" +
                "【小技巧】\r\n" +
                "· 点本窗口按钮前，程序会记住你上一个使用的窗口，输入前自动切回去，不用手动点。\r\n" +
                "· “开始前等待”用来给自己留出点击目标输入框的时间；用快捷键触发时可以设为 0。\r\n" +
                "· 常用片段保存在配置文件里，可用“文件 → 打开配置文件夹”备份或直接编辑。";
            MessageBox.Show(this, msg, "速录助手 使用说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowAbout()
        {
            MessageBox.Show(this,
                "速录助手 QuickType v1.0\r\n\r\n" +
                "把指定文本直接“敲”到光标所在位置，\r\n" +
                "用于解决网页 / 软件不允许复制粘贴的问题。\r\n\r\n" +
                "纯 Win32 + .NET Framework 实现，单文件 exe，免安装。\r\n" +
                "配置文件：" + Config.FilePath,
                "关于", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // =================================================================
    // 入口
    // =================================================================
    internal static class Program
    {
        private static Mutex _single;

        [STAThread]
        private static void Main()
        {
            bool createdNew;
            _single = new Mutex(true, @"Local\QuickType_SingleInstance_2024", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("速录助手已经在运行了。\r\n请在屏幕右下角的托盘区找它（蓝色圆形“输”图标），双击即可打开窗口。",
                    "速录助手", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show("程序发生异常：\r\n" + ex.ToString(), "速录助手",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            GC.KeepAlive(_single);
        }
    }
}
