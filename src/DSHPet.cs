// DSH 桌宠 v1.2 —— 鲸鱼娘桌面宠物
// 功能:启动/停止/监测 DSH 服务,双击唤起 GUI,右键菜单,托盘图标,状态悬浮卡片
// 技术:WinForms + UpdateLayeredWindow 逐像素透明;单实例;资源防泄漏
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DSHWhalePet
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            try { SetProcessDPIAware(); } catch { }

            // 崩溃日志:任何未捕获异常都写入 exe 同目录的 crash 日志
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dsh-whale-pet-crash.log");
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                try { File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " Unhandled:\n" + e.ExceptionObject + "\n\n"); } catch { }
            };
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                try { File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ThreadException:\n" + e.Exception + "\n\n"); } catch { }
            };

            bool createdNew;
            using (var mutex = new Mutex(true, "DSH_Whale_Pet_Mutex", out createdNew))
            {
                if (!createdNew)
                {
                    // 已有实例:通知它唤起并退出本实例
                    try
                    {
                        using (var evt = EventWaitHandle.OpenExisting("DSH_Whale_Pet_Event"))
                        {
                            evt.Set();
                        }
                    }
                    catch { }
                    return;
                }

                using (var wake = new EventWaitHandle(false, EventResetMode.AutoReset, "DSH_Whale_Pet_Event"))
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new PetForm(wake));
                }
            }
        }
    }

    class PetForm : Form
    {
        // ── 可配置项(配置文件 dsh-whale-pet.conf 可覆盖;留空 = 自动检测) ──
        string cfgWorkspace = "";        // DSH 工作区目录(默认 = exe 所在目录)
        string cfgNodePath = "";         // node.exe 路径
        string cfgDshBin = "";           // @deepseek-ai/dsh 的 lib/bin.js 路径
        string cfgPwaShortcut = "";      // Chrome PWA 快捷方式路径(空 = 自动查找,找不到则回落到浏览器)
        string cfgPwaWindowTitle = "DeepSeek Harness";  // PWA 窗口标题前缀(用于关窗)
        int cfgPort = 3080;              // DSH 服务端口
        int cfgLastX = -1, cfgLastY = -1; // 上次位置

        string WorkSpace { get { return cfgWorkspace.Length > 0 ? cfgWorkspace : AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'); } }
        string DshUrl { get { return "http://127.0.0.1:" + cfgPort; } }
        string NodePath { get { return cfgNodePath; } }
        string DshBin { get { return cfgDshBin; } }
        string PwaShortcut { get { return cfgPwaShortcut; } }
        string PwaWindowTitle { get { return cfgPwaWindowTitle; } }

        const string VERSION = "v1.6";
        const int ONLINE_MS = 5000;   // 在线检测间隔
        const int OFFLINE_MS = 2000;  // 离线检测间隔
        const string RES_NAME = "DSHWhalePet.pet.png";
        const string MUTEX_NAME = "DSH_Whale_Pet_Mutex";

        // ── 状态 ──
        Image petImage;
        Bitmap layerBitmap;
        IntPtr trayIconHandle = IntPtr.Zero;
        Icon trayIcon;
        NotifyIcon tray;
        ContextMenuStrip menu;
        System.Windows.Forms.Timer statusTimer;
        bool online = false;
        bool checking = false;
        bool waitingForReady = false;
        bool startedService = false;
        DateTime startTime = DateTime.Now;
        EventWaitHandle wakeEvent;
        Thread wakeThread;
        StatusCard card;
        bool exitingAll = false;

        // ── 拖动/双击 ──
        bool dragging = false;
        bool moved = false;
        Point dragStartScreen;
        Point winStart;
        DateTime lastClickTime = DateTime.MinValue;

        // ── P/Invoke:分层窗口 ──
        const int ULW_ALPHA = 2;
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int Width, Height; }
        [StructLayout(LayoutKind.Sequential)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hgo);
        [DllImport("user32.dll")] static extern int GetGuiResources(IntPtr hProcess, int uiFlags);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        const uint WM_CLOSE = 0x0010;

        public PetForm(EventWaitHandle wake)
        {
            wakeEvent = wake;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;

            LoadPetImage();
            Size = new Size(petImage.Width, petImage.Height);
            ClientSize = new Size(petImage.Width, petImage.Height);

            BuildMenu();
            BuildTray();
            LoadConfig();

            statusTimer = new System.Windows.Forms.Timer();
            statusTimer.Tick += OnStatusTick;
            statusTimer.Interval = OFFLINE_MS;
            statusTimer.Start();

            // 首次启动行为:服务在 → 直接开 GUI;不在 → 拉起服务,就绪后开 GUI
            if (IsPortOpen(700))
            {
                OpenGui();
            }
            else
            {
                StartService();
                waitingForReady = true;
            }

            CheckStatusAsync();
            StartWakeThread();
        }

        // ── 资源加载 ──
        void LoadPetImage()
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream(RES_NAME))
            {
                if (s == null) throw new Exception("图标资源缺失: " + RES_NAME);
                petImage = new Bitmap(s);
            }
        }

        // ── 分层窗口绘制(鲸鱼娘 + 状态点) ──
        void ApplyLayer()
        {
            if (Handle == IntPtr.Zero) return;
            if (layerBitmap != null) { layerBitmap.Dispose(); layerBitmap = null; }

            int w = petImage.Width, h = petImage.Height;
            layerBitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(layerBitmap))
            {
                g.Clear(Color.Transparent);
                g.DrawImage(petImage, 0, 0, w, h);
                // 右下角状态点:绿=在线 红=离线
                int d = 16, margin = 5;
                int dx = w - d - margin, dy = h - d - margin;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush b = new SolidBrush(online ? Color.FromArgb(230, 46, 204, 113) : Color.FromArgb(230, 231, 76, 60)))
                using (Pen p = new Pen(Color.FromArgb(235, 255, 255, 255), 2))
                {
                    g.FillEllipse(b, dx, dy, d, d);
                    g.DrawEllipse(p, dx, dy, d, d);
                }
            }

            IntPtr hdcScreen = GetDC(IntPtr.Zero);
            IntPtr hdcMem = CreateCompatibleDC(hdcScreen);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr old = IntPtr.Zero;
            try
            {
                hBitmap = layerBitmap.GetHbitmap(Color.FromArgb(0));
                old = SelectObject(hdcMem, hBitmap);
                POINT ptDst = new POINT { X = Left, Y = Top };
                POINT ptSrc = new POINT { X = 0, Y = 0 };
                SIZE size = new SIZE { Width = w, Height = h };
                BLENDFUNCTION blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                UpdateLayeredWindow(Handle, hdcScreen, ref ptDst, ref size, hdcMem, ref ptSrc, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                if (old != IntPtr.Zero) SelectObject(hdcMem, old);
                if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                if (hdcMem != IntPtr.Zero) DeleteDC(hdcMem);
                if (hdcScreen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, hdcScreen);
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplyLayer();
        }

        // 关键:必须加 WS_EX_LAYERED,UpdateLayeredWindow 才会生效,否则透明区域渲染成黑色
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00080000; // WS_EX_LAYERED
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 分层窗口,不做常规背景绘制
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyLayer();
        }

        // ── 拖动与双击 ──
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                dragging = true;
                moved = false;
                dragStartScreen = Cursor.Position;
                winStart = Location;
            }
            else if (e.Button == MouseButtons.Right)
            {
                menu.Show(Cursor.Position);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            Point now = Cursor.Position;
            if (!moved && (Math.Abs(now.X - dragStartScreen.X) + Math.Abs(now.Y - dragStartScreen.Y) > 4))
                moved = true;
            if (moved)
                Location = new Point(winStart.X + now.X - dragStartScreen.X, winStart.Y + now.Y - dragStartScreen.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && dragging)
            {
                dragging = false;
                if (!moved)
                {
                    // 单击 → 双击判定
                    DateTime now = DateTime.Now;
                    if ((now - lastClickTime).TotalMilliseconds < SystemInformation.DoubleClickTime)
                    {
                        lastClickTime = DateTime.MinValue;
                        OpenGui();
                    }
                    else
                    {
                        lastClickTime = now;
                    }
                }
                else
                {
                    SaveConfig();
                }
            }
        }

        // ── 右键菜单 ──
        void BuildMenu()
        {
            menu = new ContextMenuStrip();
            menu.Items.Add("🖥️ 打开程序", null, delegate { OpenProgram(); });
            menu.Items.Add("⏹ 关闭程序", null, delegate { StopService(); });
            menu.Items.Add("📊 查看状态", null, delegate { ShowStatusCard(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("🚪 退出", null, delegate { ExitAll(); });
        }

        void OpenProgram()
        {
            if (IsPortOpen(600)) { OpenGui(); }
            else { StartService(); waitingForReady = true; }
        }

        void OpenGui()
        {
            try
            {
                // 优先走 Chrome PWA 快捷方式:独立窗口 + 重复启动复用同一窗口,不会在浏览器里堆标签页
                if (PwaShortcut.Length > 0 && File.Exists(PwaShortcut))
                {
                    Process.Start(new ProcessStartInfo(PwaShortcut) { UseShellExecute = true });
                }
                else
                {
                    Process.Start(new ProcessStartInfo(DshUrl) { UseShellExecute = true });
                }
            }
            catch { }
        }

        // ── 托盘 ──
        void BuildTray()
        {
            Bitmap bmp = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Transparent);
                g.DrawImage(petImage, 0, 0, 32, 32);
            }
            trayIconHandle = bmp.GetHicon();
            trayIcon = Icon.FromHandle(trayIconHandle);
            bmp.Dispose();

            tray = new NotifyIcon();
            tray.Icon = trayIcon;
            tray.Text = "DSH 桌宠 - " + (online ? "在线" : "离线");
            tray.Visible = true;
            tray.ContextMenuStrip = menu;
            tray.MouseUp += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) OpenProgram();
            };
        }

        // ── 状态监测(绿/红两态,自适应频率) ──
        void OnStatusTick(object sender, EventArgs e)
        {
            CheckStatusAsync();
        }

        public void CheckStatusAsync()
        {
            if (checking) return;
            checking = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = IsPortOpen(1500);
                try { BeginInvoke(new Action<bool>(ApplyStatus), ok); }
                catch { }
            });
        }

        void ApplyStatus(bool ok)
        {
            checking = false;
            online = ok;
            statusTimer.Interval = ok ? ONLINE_MS : OFFLINE_MS;
            ApplyLayer();
            if (tray != null) tray.Text = "DSH 桌宠 - " + (ok ? "在线" : "离线");
            if (ok && waitingForReady)
            {
                waitingForReady = false;
                OpenGui();
            }
        }

        bool IsPortOpen(int timeoutMs)
        {
            try
            {
                using (TcpClient c = new TcpClient())
                {
                    IAsyncResult ar = c.BeginConnect("127.0.0.1", cfgPort, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(timeoutMs, false)) return false;
                    c.EndConnect(ar);
                    return c.Connected;
                }
            }
            catch { return false; }
        }

        // ── 服务管理 ──
        void StartService()
        {
            if (IsPortOpen(500)) return;
            // 未检测到 node/dsh 时给出明确提示,而不是静默失败
            if (NodePath.Length == 0 || DshBin.Length == 0)
            {
                string msg = "未找到 Node.js 或 DeepSeek Harness 安装:\n"
                    + "  nodePath: " + (NodePath.Length > 0 ? NodePath : "(未找到)") + "\n"
                    + "  dshBin:   " + (DshBin.Length > 0 ? DshBin : "(未找到)") + "\n\n"
                    + "请确认已正确安装 DeepSeek Harness(Node.js + dsh CLI),\n"
                    + "或在配置文件 " + Path.GetFileName(ConfigPath) + " 中手动填写。";
                MessageBox.Show(msg, "DSH 桌宠", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            startedService = true;
            try
            {
                // 直接 node <bin.js> web,等价于 npx @deepseek-ai/dsh web,但无 npx 解析/下载/弹窗
                ProcessStartInfo psi = new ProcessStartInfo(NodePath, "\"" + DshBin + "\" web");
                psi.WorkingDirectory = WorkSpace;
                psi.WindowStyle = ProcessWindowStyle.Minimized;
                psi.CreateNoWindow = false;
                Process.Start(psi);
            }
            catch { }
        }

        int FindPid()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("netstat", "-ano")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (Process p = Process.Start(psi))
                {
                    string outp = p.StandardOutput.ReadToEnd();
                    foreach (string line in outp.Split('\n'))
                    {
                        if (line.IndexOf(":3080") >= 0 && line.IndexOf("LISTENING") >= 0)
                        {
                            string[] parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 5)
                            {
                                int pid;
                                if (int.TryParse(parts[parts.Length - 1], out pid)) return pid;
                            }
                        }
                    }
                }
            }
            catch { }
            return -1;
        }

        void StopService()
        {
            int pid = FindPid();
            if (pid > 0)
            {
                try { Process.GetProcessById(pid).Kill(); } catch { }
            }
            // 顺带关闭 DeepSeek Harness PWA 窗口(按窗口标题匹配,只关该窗口,不影响其他 Chrome 页面)
            ClosePwaWindow();
        }

        void ClosePwaWindow()
        {
            try
            {
                EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
                {
                    if (IsWindowVisible(hWnd))
                    {
                        StringBuilder sb = new StringBuilder(512);
                        GetWindowText(hWnd, sb, 512);
                        string title = sb.ToString();
                        // PWA 窗口标题以应用名开头(如 "DeepSeek Harness - ..."),普通浏览器标签页不会
                        if (title.StartsWith(PwaWindowTitle, StringComparison.Ordinal))
                        {
                            PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                        }
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
        }

        // ── 退出 ──
        void ExitAll()
        {
            if (exitingAll) return;
            DialogResult r = MessageBox.Show(
                "退出桌宠将同时关闭 DSH 服务,确定退出吗?",
                "DSH 桌宠",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question);
            if (r != DialogResult.OK) return;
            exitingAll = true;
            StopService();
            SaveConfig();
            Application.Exit();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exitingAll)
            {
                // 非菜单退出(理论上不会发生,兜底):直接退,不动服务?按规格退出=连服务,这里也停
                StopService();
            }
            SaveConfig();
            if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
            if (trayIcon != null) { trayIcon.Dispose(); trayIcon = null; }
            if (trayIconHandle != IntPtr.Zero) { DestroyIcon(trayIconHandle); trayIconHandle = IntPtr.Zero; }
            if (statusTimer != null) { statusTimer.Stop(); statusTimer.Dispose(); statusTimer = null; }
            base.OnFormClosing(e);
        }

        // ── 配置(INI 文件:设置 + 位置记忆) ──
        string ConfigPath
        {
            get
            {
                string dir = Path.GetDirectoryName(Application.ExecutablePath);
                return Path.Combine(dir, "dsh-whale-pet.conf");
            }
        }

        void SaveConfig()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# DSH 桌宠配置(留空 = 自动检测)");
                sb.AppendLine("workspace=" + cfgWorkspace);
                sb.AppendLine("nodePath=" + cfgNodePath);
                sb.AppendLine("dshBin=" + cfgDshBin);
                sb.AppendLine("pwaShortcut=" + cfgPwaShortcut);
                sb.AppendLine("pwaWindowTitle=" + cfgPwaWindowTitle);
                sb.AppendLine("port=" + cfgPort);
                sb.AppendLine("lastX=" + Location.X);
                sb.AppendLine("lastY=" + Location.Y);
                File.WriteAllText(ConfigPath, sb.ToString());
            }
            catch { }
        }

        void LoadConfig()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int x = wa.Right - Width - 20;
            int y = wa.Bottom - Height - 20;
            try
            {
                if (File.Exists(ConfigPath))
                {
                    foreach (string line in File.ReadAllLines(ConfigPath))
                    {
                        string t = line.Trim();
                        if (t.Length == 0 || t.StartsWith("#")) continue;
                        int eq = t.IndexOf('=');
                        if (eq <= 0) continue;
                        string key = t.Substring(0, eq).Trim();
                        string val = t.Substring(eq + 1).Trim();
                        switch (key)
                        {
                            case "workspace": cfgWorkspace = val; break;
                            case "nodePath": cfgNodePath = val; break;
                            case "dshBin": cfgDshBin = val; break;
                            case "pwaShortcut": cfgPwaShortcut = val; break;
                            case "pwaWindowTitle": if (val.Length > 0) cfgPwaWindowTitle = val; break;
                            case "port": int p; if (int.TryParse(val, out p) && p > 0) cfgPort = p; break;
                            case "lastX": int lx; if (int.TryParse(val, out lx)) cfgLastX = lx; break;
                            case "lastY": int ly; if (int.TryParse(val, out ly)) cfgLastY = ly; break;
                        }
                    }
                }
                // 首次运行:自动检测并把结果写回配置,方便用户查看/修改
                bool firstRun = !File.Exists(ConfigPath);
                if (cfgNodePath.Length == 0) cfgNodePath = DetectNodePath();
                if (cfgDshBin.Length == 0) cfgDshBin = DetectDshBin(cfgNodePath);
                if (cfgPwaShortcut.Length == 0) cfgPwaShortcut = DetectPwaShortcut(WorkSpace);
                if (cfgLastX >= 0 && cfgLastY >= 0)
                {
                    x = Math.Max(wa.Left, Math.Min(cfgLastX, wa.Right - Width));
                    y = Math.Max(wa.Top, Math.Min(cfgLastY, wa.Bottom - Height));
                }
            }
            catch { }
            Location = new Point(x, y);
            // 首次运行:位置设定后再保存,避免写入 (0,0)
            if (!File.Exists(ConfigPath)) { try { SaveConfig(); } catch { } }
        }

        // ── 自动检测 ──
        string DetectNodePath()
        {
            try
            {
                // 1) PATH 里的 node
                string where = RunCapture("where", "node");
                if (where != null)
                {
                    foreach (string line in where.Split('\n'))
                    {
                        string p = line.Trim();
                        if (p.Length > 0 && File.Exists(p)) return p;
                    }
                }
            }
            catch { }
            // 2) 常见安装位置
            string[] cands = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "node.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "nodejs", "node.exe")
            };
            foreach (string c in cands) { if (File.Exists(c)) return c; }
            return "";
        }

        string DetectDshBin(string nodePath)
        {
            string nodeDir = nodePath.Length > 0 ? Path.GetDirectoryName(nodePath) : "";
            var cands = new System.Collections.Generic.List<string>();
            if (nodeDir.Length > 0)
            {
                cands.Add(Path.Combine(nodeDir, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js"));
                cands.Add(Path.Combine(Path.GetDirectoryName(nodeDir), "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js"));
            }
            cands.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js"));
            foreach (string c in cands) { if (File.Exists(c)) return c; }
            return "";
        }

        string DetectPwaShortcut(string workspace)
        {
            var dirs = new System.Collections.Generic.List<string>();
            dirs.Add(AppDomain.CurrentDomain.BaseDirectory);
            dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            if (workspace.Length > 0) dirs.Add(workspace);
            foreach (string d in dirs)
            {
                string p = Path.Combine(d, "DeepSeek Harness.lnk");
                if (File.Exists(p)) return p;
            }
            return "";
        }

        string RunCapture(string exe, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (Process p = Process.Start(psi))
                {
                    if (!p.WaitForExit(5000)) { try { p.Kill(); } catch { } return null; }
                    return p.StandardOutput.ReadToEnd();
                }
            }
            catch { return null; }
        }

        // ── 状态悬浮卡片 ──
        void ShowStatusCard()
        {
            if (card != null && !card.IsDisposed) { card.Close(); card = null; }
            card = new StatusCard(this);
            card.Show();
            // 放在鲸鱼娘右侧
            Point p = new Point(Location.X + Width + 6, Location.Y);
            if (p.X + card.Width > Screen.PrimaryScreen.WorkingArea.Right)
                p.X = Location.X - card.Width - 6;
            if (p.Y + card.Height > Screen.PrimaryScreen.WorkingArea.Bottom)
                p.Y = Screen.PrimaryScreen.WorkingArea.Bottom - card.Height;
            card.Location = p;
        }

        public string StatusServiceInfo()
        {
            int pid = FindPid();
            string state = online ? "🟢 在线" : "🔴 离线";
            string pidStr = pid > 0 ? pid.ToString() : "-";
            string up = "-";
            if (pid > 0)
            {
                try
                {
                    TimeSpan ts = DateTime.Now - Process.GetProcessById(pid).StartTime;
                    up = string.Format("{0}小时{1}分", ts.Hours, ts.Minutes);
                }
                catch { }
            }
            return "服务状态: " + state + "\n"
                 + "服务地址: " + DshUrl + "\n"
                 + "工作区: " + WorkSpace + "\n"
                 + "进程 PID: " + pidStr + "\n"
                 + "运行时长: " + up;
        }

        public string StatusPetInfo()
        {
            long memMB = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
            int gdi = 0;
            try { gdi = GetGuiResources(Process.GetCurrentProcess().Handle, 0); } catch { }
            TimeSpan up = DateTime.Now - startTime;
            return "桌宠版本: " + VERSION + "\n"
                 + "内存占用: " + memMB + " MB\n"
                 + "GDI 句柄: " + gdi + "\n"
                 + "运行时长: " + string.Format("{0}小时{1}分", up.Hours, up.Minutes) + "\n"
                 + "检测频率: " + (online ? "5 秒(在线)" : "2 秒(离线)");
        }

        public void RefreshStatusCard()
        {
            if (card != null && !card.IsDisposed) card.RefreshInfo();
        }

        // ── 第二实例唤醒 ──
        void StartWakeThread()
        {
            wakeThread = new Thread(delegate()
            {
                while (true)
                {
                    if (wakeEvent.WaitOne(500, false))
                    {
                        try
                        {
                            BeginInvoke(new Action(delegate() { OpenProgram(); }));
                        }
                        catch { }
                    }
                }
            });
            wakeThread.IsBackground = true;
            wakeThread.Start();
        }
    }

    // 状态悬浮卡片:点击别处自动收起
    class StatusCard : Form
    {
        PetForm owner;
        Label lbl;

        public StatusCard(PetForm ownerForm)
        {
            owner = ownerForm;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(250, 250, 252);
            Size = new Size(260, 260);

            lbl = new Label();
            lbl.AutoSize = false;
            lbl.Dock = DockStyle.Top;
            lbl.Height = 150;
            lbl.Padding = new Padding(10);
            lbl.Font = new Font("Microsoft YaHei", 9.5f);
            lbl.Text = "";

            Button btnCheck = new Button();
            btnCheck.Text = "立即检测一次";
            btnCheck.FlatStyle = FlatStyle.Flat;
            btnCheck.BackColor = Color.FromArgb(46, 204, 113);
            btnCheck.ForeColor = Color.White;
            btnCheck.Height = 30;
            btnCheck.Dock = DockStyle.Top;
            btnCheck.Click += delegate { owner.CheckStatusAsync(); RefreshInfo(); };

            Button btnRefresh = new Button();
            btnRefresh.Text = "刷新";
            btnRefresh.FlatStyle = FlatStyle.Flat;
            btnRefresh.BackColor = Color.FromArgb(90, 140, 200);
            btnRefresh.ForeColor = Color.White;
            btnRefresh.Height = 30;
            btnRefresh.Dock = DockStyle.Top;
            btnRefresh.Click += delegate { RefreshInfo(); };

            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 66;
            top.Controls.Add(btnRefresh);
            btnCheck.Dock = DockStyle.Top;
            top.Controls.Add(btnCheck);
            // 顺序:btnRefresh 在上,btnCheck 在下

            Panel content = new Panel();
            content.Dock = DockStyle.Fill;
            content.Controls.Add(lbl);

            Controls.Add(content);
            Controls.Add(top);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Close();
        }

        public void RefreshInfo()
        {
            lbl.Text = owner.StatusServiceInfo() + "\n\n" + owner.StatusPetInfo();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RefreshInfo();
        }
    }
}
