// 测试宿主：弹出一个带多行输入框的窗口，接收被“敲”进来的文本，
// 若干秒后把输入框内容写进文件，供外部比对。
using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

internal class TestHost : Form
{
    private TextBox _box;
    private Timer _timer;
    private int _ticks;
    private string _outPath;

    public TestHost(string outPath)
    {
        _outPath = outPath;
        Text = "QuickType 测试宿主（请勿关闭，等待自动退出）";
        ClientSize = new Size(760, 420);
        StartPosition = FormStartPosition.CenterScreen;

        _box = new TextBox();
        _box.Multiline = true;
        _box.ScrollBars = ScrollBars.Vertical;
        _box.Dock = DockStyle.Fill;
        _box.Font = new Font("Microsoft YaHei UI", 12F);
        Controls.Add(_box);

        _timer = new Timer();
        _timer.Interval = 1000;
        _timer.Tick += OnTick;
        _timer.Start();
        Shown += delegate { Activate(); _box.Focus(); };
    }

    private void OnTick(object sender, EventArgs e)
    {
        _ticks++;
        Text = "QuickType 测试宿主 - 已接收 " + _box.Text.Length + " 字 - " + _ticks + "s";
        if (_ticks >= 11)
        {
            _timer.Stop();
            try { File.WriteAllText(_outPath, _box.Text, new UTF8Encoding(false)); }
            catch (Exception) { }
            Close();
        }
    }

    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TestHost(args.Length > 0 ? args[0] : "received.txt"));
    }
}
