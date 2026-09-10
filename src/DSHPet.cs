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
        string cfgChromeProfile = "Default";  // Chrome 配置目录(与 PWA 快捷方式一致;空 = 从快捷方式参数读取)
        string cfgOpenMode = "auto";          // 开窗方式: auto = 优先令牌地址(自愈 cookie) | pwa = 只用快捷方式 | token = 只用令牌地址

        string WorkSpace { get { return cfgWorkspace.Length > 0 ? cfgWorkspace : AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'); } }
        string DshUrl { get { return "http://127.0.0.1:" + cfgPort; } }
        string NodePath { get { return cfgNodePath; } }
        string DshBin { get { return cfgDshBin; } }
        string PwaShortcut { get { return cfgPwaShortcut; } }
        string PwaWindowTitle { get { return cfgPwaWindowTitle; } }
        string ChromeProfile { get { return cfgChromeProfile; } }
        string OpenMode { get { return cfgOpenMode; } }

        const string VERSION = "v1.14";
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
        Process serverProc = null;
        volatile string serviceUrl = "";   // 服务启动时输出的带令牌地址(用于开窗时换取/续期浏览器 cookie)
        bool serviceUrlUsed = false;       // 该令牌地址是否已用于开窗(用过则回落 PWA 快捷方式,保持单窗口)
        bool openPending = false;          // 是否已有一个"等待服务就绪后开窗"的任务在排队
        DateTime waitStart = DateTime.MinValue;
        bool slowNotified = false;
        DateTime startTime = DateTime.Now;
        EventWaitHandle wakeEvent;
        Thread wakeThread;
        StatusCard card;
        bool exitingAll = false;
        bool minimizedToTray = false;   // 是否已最小化至托盘(隐藏鲸鱼娘,仅留托盘图标)

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
            menu.Items.Add("🗕 最小化至托盘", null, delegate { MinimizeToTray(); });
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

        // ── 开窗 ──
        // 新版 dsh(0.1.x)对裸地址 http://127.0.0.1:<port>/ 要求浏览器持有 dsh-auth-* cookie
        // (默认 30 天),cookie 缺失/过期时裸地址只返回 401。服务每次启动都会打印一行带进程
        // 令牌的地址(dsh web: http://...?token=...),浏览器访问一次即换取新 cookie 并 303 跳回
        // 干净地址。
        //
        // 抢跑保护:端口 listen 明显早于 Web 路由就绪 —— 服务刚起来就访问会先拿到 404
        // (找不到网页),稍后拿到 401(authentication required)。令牌行是"真正就绪"的可靠
        // 信号,所以桌宠在读到令牌行之前不急着开窗,而是短暂等待(最多 10 秒)后回落到原有路径。
        // 桌宠自己启动的服务进程是否仍在运行(只有它能提供有效的进程令牌)
        bool OurServiceRunning()
        {
            try { return serverProc != null && !serverProc.HasExited; }
            catch { return false; }
        }

        void OpenGui()
        {
            if (OpenMode != "pwa" && OurServiceRunning() && serviceUrl.Length == 0)
            {
                if (openPending) return;      // 已有一个等待中的开窗任务,避免重复排队
                openPending = true;
                LogLaunch("服务已启动但尚未打印访问地址,等待就绪后再开窗…");
                ThreadPool.QueueUserWorkItem(delegate(object state)
                {
                    for (int i = 0; i < 100 && serviceUrl.Length == 0 && OurServiceRunning(); i++) Thread.Sleep(100);
                    try { BeginInvoke(new Action(OpenGuiNow)); }
                    catch { openPending = false; }
                });
                return;
            }
            OpenGuiNow();
        }

        void OpenGuiNow()
        {
            openPending = false;
            try
            {
                // 令牌只在"桌宠自己启动的服务仍存活"时有效;外部启动或已退出的服务一律走原有路径
                string tokenUrl = OurServiceRunning() ? serviceUrl : "";
                bool wantToken = (OpenMode == "token") || (OpenMode == "auto" && !serviceUrlUsed);

                if (tokenUrl.Length > 0 && wantToken)
                {
                    serviceUrlUsed = true;
                    LogLaunch("开窗方式: 令牌地址(换 cookie 后自动跳回干净地址)");
                    if (OpenViaChromeApp(tokenUrl)) return;
                    // 未找到 Chrome:交给默认浏览器完成同样的令牌交换
                    Process.Start(new ProcessStartInfo(tokenUrl) { UseShellExecute = true });
                    return;
                }

                if (OpenMode == "token" && tokenUrl.Length == 0)
                {
                    // 明确要求令牌地址但没捕获到(服务由外部启动) → 退化为裸地址
                    LogLaunch("开窗方式: 未捕获到令牌地址,退化为裸地址");
                    Process.Start(new ProcessStartInfo(DshUrl) { UseShellExecute = true });
                    return;
                }

                // 原有路径: PWA 快捷方式(独立窗口 + 重复启动复用同一窗口)
                LogLaunch("开窗方式: PWA 快捷方式");
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

        void LogLaunch(string message)
        {
            try { File.AppendAllText(LaunchLogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + "\n"); } catch { }
        }

        // ── 令牌地址捕获 ──
        // 从服务输出里抓取形如 "dsh web: http://127.0.0.1:3080/?token=xxx" 的一行。
        void CaptureServiceUrl(string line)
        {
            try
            {
                if (line == null || line.Length == 0) return;
                int at = line.IndexOf("dsh web:", StringComparison.OrdinalIgnoreCase);
                string candidate = at >= 0 ? line.Substring(at + 8) : line;
                candidate = candidate.Trim();
                int space = candidate.IndexOf(' ');
                if (space > 0) candidate = candidate.Substring(0, space);
                if (candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    && candidate.IndexOf("token=", StringComparison.OrdinalIgnoreCase) > 0)
                {
                    serviceUrl = candidate;
                    serviceUrlUsed = false;
                    LogLaunch("已捕获令牌地址(用于开窗时自愈 cookie)");
                }
            }
            catch { }
        }

        // ── Chrome ──
        string FindChrome()
        {
            string[] cands = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe")
            };
            foreach (string c in cands) { if (File.Exists(c)) return c; }
            return "";
        }

        // 用 Chrome 的应用窗口模式打开指定地址(profile 与 PWA 快捷方式保持一致,才能共用 cookie)
        bool OpenViaChromeApp(string url)
        {
            string chrome = FindChrome();
            if (chrome.Length == 0) return false;
            string profile = ChromeProfile;
            if (profile.Length == 0) profile = ProfileFromShortcut();
            if (profile.Length == 0) profile = "Default";
            ProcessStartInfo psi = new ProcessStartInfo(chrome,
                "--profile-directory=\"" + profile + "\" --app=\"" + url + "\"");
            psi.UseShellExecute = false;
            Process.Start(psi);
            return true;
        }

        // 从 PWA 快捷方式参数里读取 --profile-directory(读不到则返回空)
        string ProfileFromShortcut()
        {
            try
            {
                if (PwaShortcut.Length == 0 || !File.Exists(PwaShortcut)) return "";
                string args = ReadShortcutArguments(PwaShortcut);
                int at = args.IndexOf("--profile-directory=", StringComparison.OrdinalIgnoreCase);
                if (at < 0) return "";
                string rest = args.Substring(at + "--profile-directory=".Length).Trim();
                if (rest.StartsWith("\"")) rest = rest.Substring(1);
                int end = rest.IndexOfAny(new char[] { '"', ' ' });
                return end > 0 ? rest.Substring(0, end) : rest;
            }
            catch { return ""; }
        }

        string ReadShortcutArguments(string lnkPath)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return "";
                object shell = Activator.CreateInstance(shellType);
                object shortcut = shellType.InvokeMember("CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                if (shortcut == null) return "";
                object args = shortcut.GetType().InvokeMember("Arguments",
                    System.Reflection.BindingFlags.GetProperty, null, shortcut, null);
                return args as string ?? "";
            }
            catch { return ""; }
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
            tray.Text = TrayText();
            tray.Visible = true;
            tray.ContextMenuStrip = menu;
            tray.MouseUp += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                // 已最小化 → 单击唤起鲸鱼娘;未最小化 → 沿用原有行为(打开 GUI)
                if (minimizedToTray) RestoreFromTray();
                else OpenProgram();
            };
        }

        // ── 最小化至托盘 / 还原 ──
        string TrayText()
        {
            string state = minimizedToTray ? "已最小化(单击图标显示)" : (online ? "在线" : "离线");
            return "DSH 桌宠 - " + state;
        }

        void MinimizeToTray()
        {
            if (minimizedToTray) return;
            try
            {
                SaveConfig();      // 记住当前位置,还原时仍在原处
                minimizedToTray = true;
                Hide();            // 只隐藏鲸鱼娘;托盘图标常驻,可随时单击唤起
                if (tray != null)
                {
                    tray.Text = TrayText();
                    tray.ShowBalloonTip(3000, "DSH 桌宠", "已最小化至托盘,单击托盘图标可重新显示。", ToolTipIcon.Info);
                }
                LogLaunch("已最小化至托盘");
            }
            catch { }
        }

        void RestoreFromTray()
        {
            if (!minimizedToTray) return;
            try
            {
                minimizedToTray = false;
                Show();
                BringToFront();
                ApplyLayer();      // 分层窗口需要重绘一次
                if (tray != null) tray.Text = TrayText();
                LogLaunch("已从托盘还原");
            }
            catch { }
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
            if (!minimizedToTray) ApplyLayer();   // 已隐藏时不必重绘(还原时会重绘)
            if (tray != null) tray.Text = TrayText();
            if (ok && waitingForReady)
            {
                waitingForReady = false;
                OpenGui();
                return;
            }
            if (!ok && waitingForReady)
            {
                // 服务进程已退出但服务没起来 → 明确报错
                if (serverProc != null)
                {
                    try
                    {
                        if (serverProc.HasExited)
                        {
                            waitingForReady = false;
                            MessageBox.Show(
                                "DSH 服务启动失败(进程已退出,退出码 " + serverProc.ExitCode + ")。\n"
                                + "请查看服务日志: " + ServerLogPath,
                                "DSH 桌宠", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            serverProc = null;
                            return;
                        }
                    }
                    catch { }
                }
                // 进程还活着但迟迟不就绪 → 一次性提示"启动较慢"(如首次启动被安全软件扫描)
                if (!slowNotified && (DateTime.Now - waitStart).TotalSeconds > 30)
                {
                    slowNotified = true;
                    try
                    {
                        tray.ShowBalloonTip(6000, "DSH 桌宠",
                            "DSH 服务启动较慢(可能是开机首次启动被安全软件扫描),正在等待…",
                            ToolTipIcon.Info);
                    }
                    catch { }
                }
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
            waitStart = DateTime.Now;
            slowNotified = false;
            // 新一轮服务:清掉上一轮的令牌状态,避免拿旧令牌开窗(旧进程的令牌必然失效)
            serviceUrl = "";
            serviceUrlUsed = false;
            openPending = false;
            try
            {
                // 直接 node <bin.js> web --no-open,等价于 npx @deepseek-ai/dsh web
                // --no-open:新版 dsh 启动会自动打开浏览器,由桌宠自己控制开窗(PWA),避免双窗口
                ProcessStartInfo psi = new ProcessStartInfo(NodePath, "\"" + DshBin + "\" web --no-open");
                psi.WorkingDirectory = WorkSpace;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.CreateNoWindow = true;   // 输出已重定向到日志,不再弹出控制台窗口
                // 输出重定向到日志文件:即使无窗口,服务日志也能看到卡在哪
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.UseShellExecute = false;
                serverProc = Process.Start(psi);
                // 启动日志
                try
                {
                    File.AppendAllText(LaunchLogPath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " 启动: " + NodePath + " \"" + DshBin + "\" web (cwd=" + WorkSpace + ")\n");
                }
                catch { }
                // 异步把服务输出写入日志,避免管道缓冲堵塞服务
                serverProc.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                    {
                        try { File.AppendAllText(ServerLogPath, e.Data + "\n"); } catch { }
                        CaptureServiceUrl(e.Data);
                    }
                };
                serverProc.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                    {
                        try { File.AppendAllText(ServerLogPath, e.Data + "\n"); } catch { }
                        CaptureServiceUrl(e.Data);
                    }
                };
                serverProc.BeginOutputReadLine();
                serverProc.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(LaunchLogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " 启动异常: " + ex + "\n"); } catch { }
            }
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

        // ── 关闭 GUI 窗口 ──
        // 窗口标题随 dsh 版本/页面状态变化,实测至少两种格式:
        //   应用窗口(旧格式/错误页): "DeepSeek Harness - 127.0.0.1"    ← 应用名在前
        //   应用窗口(新版 GUI):      "<会话标题> — DeepSeek Harness"    ← 应用名在后(0.1.5 起)
        // 只判断"以应用名开头"会漏掉新版格式(表现为:服务已停但窗口不关,页面显示"正在自动重连")。
        // 因此改为"包含应用名"匹配,并排除标题里带浏览器名的普通浏览器窗口,
        // 避免把用户正在浏览的窗口整窗关掉(原有设计意图:只关 GUI 窗口,不影响其他页面)。
        static readonly string[] BrowserTitleMarkers = {
            "Google Chrome", "Microsoft Edge", "Mozilla Firefox", "Brave", "Opera", "Vivaldi", "Safari", "360"
        };

        bool IsGuiWindowTitle(string title)
        {
            if (title.Length == 0 || PwaWindowTitle.Length == 0) return false;
            if (title.IndexOf(PwaWindowTitle, StringComparison.OrdinalIgnoreCase) < 0) return false;
            foreach (string marker in BrowserTitleMarkers)
            {
                if (title.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) return false;
            }
            return true;
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
                        if (IsGuiWindowTitle(sb.ToString()))
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

        // ── 诊断日志(与 exe 同目录) ──
        string LaunchLogPath
        {
            get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "dsh-launch.log"); }
        }
        string ServerLogPath
        {
            get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "dsh-server.log"); }
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
                sb.AppendLine("openMode=" + cfgOpenMode);
                sb.AppendLine("chromeProfile=" + cfgChromeProfile);
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
                            case "openMode": if (val == "auto" || val == "pwa" || val == "token") cfgOpenMode = val; break;
                            case "chromeProfile": cfgChromeProfile = val; break;
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
                            BeginInvoke(new Action(delegate()
                            {
                                // 再次运行 exe:若鲸鱼娘被最小化到托盘,先把她喊回来;否则照旧打开 GUI
                                if (minimizedToTray) RestoreFromTray();
                                else OpenProgram();
                            }));
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
