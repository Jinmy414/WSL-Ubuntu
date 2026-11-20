// UbuntuDS-Console — Windows 上的 Ubuntu 工作站控制台 (WPF)
// v2: 菜单栏、工具栏、实时资源状态、重启和 SSH 快捷操作
// Windows checks the GNOME RDP handshake through the IPv4 relay before starting mstsc.
// 编译: setup\build-console.cmd
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using IoPath = System.IO.Path;
using SPath = System.Windows.Shapes.Path;

namespace UbuntuDSConsole
{
    public static class Program
    {
        public static bool ArgFullScreen = false;
        static bool takeover;

        [STAThread]
        public static void Main(string[] args)
        {
            foreach (string a in args ?? new string[0])
            {
                if (a == "--takeover") takeover = true;
                if (a == "--fullscreen") ArgFullScreen = true;
                if (a == "--prepare-desktop")
                {
                    Environment.ExitCode = MainWindow.PrepareDesktop(delegate { return false; }, null) ? 0 : 1;
                    return;
                }
            }
            bool createdNew;
            var mutex = new Mutex(true, "UbuntuDS-Console-SingleInstance", out createdNew);
            if (!createdNew)
            {
                if (takeover)
                {
                    // 看门狗自愈: 击杀僵死旧实例后接管
                    foreach (var p in Process.GetProcessesByName("UbuntuDS-Console"))
                    { if (p.Id != Process.GetCurrentProcess().Id) { try { p.Kill(); } catch { } } }
                    Thread.Sleep(2500);
                    mutex = new Mutex(true, "UbuntuDS-Console-SingleInstance", out createdNew);
                    if (!createdNew) return;
                }
                else
                {
                    // 连续双击时，前一个实例可能还在创建主窗口。
                    // 等它创建完成，正常启动路径不强制结束已有实例。
                    ActivateExistingConsole();
                    return;
                }
            }
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.Run(new MainWindow());
            GC.KeepAlive(mutex);
        }

        static void ActivateExistingConsole()
        {
            using (var current = Process.GetCurrentProcess())
            {
                var timer = Stopwatch.StartNew();
                while (timer.ElapsedMilliseconds < 2500)
                {
                    foreach (var candidate in Process.GetProcessesByName(current.ProcessName))
                    using (candidate)
                    {
                        try
                        {
                            if (candidate.Id == current.Id || candidate.SessionId != current.SessionId) continue;
                            IntPtr handle = candidate.MainWindowHandle;
                            if (handle == IntPtr.Zero) continue;
                            Native.SetForegroundWindow(handle);
                            return;
                        }
                        catch (InvalidOperationException) { }
                        catch (System.ComponentModel.Win32Exception) { }
                    }
                    Thread.Sleep(100);
                }
            }
        }
    }

    public class MainWindow : Window
    {
        // Windows connects through the guest's IPv4 RDP relay.
        const string RdpHost = "127.0.0.1";
        const int RdpPort = 3393;
        static readonly string BinDir = AppDomain.CurrentDomain.BaseDirectory;
        static readonly string RdpFile = IoPath.Combine(BinDir, "ds-console-embed.rdp");
        public static readonly string ConnectionLogPath = IoPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UbuntuDS", "connection.log");
        static readonly object connectionLogLock = new object();

        const double MenuHeight = 29, ToolbarHeight = 66, SessionBarHeight = 37, StatusBarHeight = 36;
        static readonly Brush Chrome = MakeBrush(0xFAF8FB);
        static readonly Brush TextInk = MakeBrush(0x282230);
        static readonly Brush SecondaryInk = MakeBrush(0x625B6C);
        static readonly Brush Divider = MakeBrush(0xE4DFE8);
        static readonly Brush Accent = MakeBrush(0xB83F20);
        static readonly Brush Stage = MakeBrush(0x261F30);
        static readonly Brush StageText = MakeBrush(0xF9F4FB);
        static readonly Brush StageMuted = MakeBrush(0xC3B8CC);
        static readonly Brush StageDivider = MakeBrush(0x514659);
        static readonly Brush StatusOk = MakeBrush(0x17664D);
        static readonly Brush StatusWorking = MakeBrush(0x8F4B13);
        static readonly Brush StatusError = MakeBrush(0xA5313B);

        enum Phase { Idle, Booting, Connecting, Embedded }
        enum PowerAction { None, Restart, Stop, Backup }

        TextBlock statusText, txtMem, txtCpu, txtUp, txtRdp;
        Ellipse sessionDot;
        TextBlock sessionState, connectionDetail, connectionTitle, connectionLogLink;
        Border sessionBadge;
        FrameworkElement waitingView;
        Brush lastStatusBrush;
        Rect windowedBounds;
        WindowState windowedState;
        MstscHost mstscHost;
        Thread worker;
        volatile bool running = true;
        volatile bool wantConnect = false;
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        Phase phase = Phase.Idle;
        readonly Stopwatch connectionClock = Stopwatch.StartNew();
        long phaseAt, nextConnectAt;
        int connectAttempts;
        const int MaxConnectAttempts = 3;
        int pendingPowerAction;
        Process mstsc;
        IntPtr embeddedHwnd = IntPtr.Zero;
        bool isFullScreen = false;
        bool suppressPowerOffOnClose = false;
        bool startFullScreen = false;
        RowDefinition rowMenu, rowToolbar, rowTabs, rowStatus;
        IntPtr exitBtnHwnd = IntPtr.Zero;
        double lastCpuBusy = -1, lastCpuTotal = -1;

        Thread statsWorker;
        long lastWorkerBeatTicks = DateTime.Now.Ticks;   // 看门狗心跳 (Interlocked)

        public MainWindow()
        {
            Title = "Ubuntu-DS 控制台";
            Width = 1440; Height = 880; MinWidth = 900; MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Chrome;
            FontFamily = new FontFamily("Segoe UI Variable Text, Microsoft YaHei UI, Segoe UI");
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            var root = new Grid();
            root.RowDefinitions.Add(rowMenu = new RowDefinition { Height = new GridLength(MenuHeight) });
            root.RowDefinitions.Add(rowToolbar = new RowDefinition { Height = new GridLength(ToolbarHeight) });
            root.RowDefinitions.Add(rowTabs = new RowDefinition { Height = new GridLength(SessionBarHeight) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(rowStatus = new RowDefinition { Height = new GridLength(StatusBarHeight) });

            var menu = BuildMenu();
            Grid.SetRow(menu, 0); root.Children.Add(menu);

            var toolbar = BuildToolbar();
            Grid.SetRow(toolbar, 1); root.Children.Add(toolbar);

            var sessionBar = BuildSessionBar();
            Grid.SetRow(sessionBar, 2); root.Children.Add(sessionBar);

            var workspace = new Grid { Background = Stage, ClipToBounds = true };
            waitingView = BuildWaitingView();
            workspace.Children.Add(waitingView);
            // HwndHost 使用独立的原生子窗口，等待界面显示时需要把它隐藏。
            mstscHost = new MstscHost { Visibility = Visibility.Hidden };
            mstscHost.SizeChanged += delegate { Dispatcher.BeginInvoke((Action)delegate { FitShell(); }); };
            workspace.Children.Add(mstscHost);
            Grid.SetRow(workspace, 3); root.Children.Add(workspace);

            var statusBar = BuildStatusBar();
            Grid.SetRow(statusBar, 4); root.Children.Add(statusBar);

            Content = root;

            PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                Key key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key == Key.Enter && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == (ModifierKeys.Control | ModifierKeys.Alt))
                {
                    if (!e.IsRepeat) ToggleFullScreen();
                    e.Handled = true;
                }
                else if (key == Key.Escape && isFullScreen)
                { ToggleFullScreen(); e.Handled = true; }
            };
            MstscHost.ExitClicked = delegate { Dispatcher.BeginInvoke((Action)delegate { if (isFullScreen) ToggleFullScreen(); }); };
            if (Program.ArgFullScreen) startFullScreen = true;
            SourceInitialized += delegate
            {
                // 主窗口 HWND hook: 接收退出按钮的 WM_COMMAND (按钮挂在主窗口)
                var src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                if (src != null) src.AddHook(WndProcHook);
                InstallEscHook();
            };
            Closed += delegate { Cleanup(); try { if (escHookHandle != IntPtr.Zero) Native.UnhookWindowsHookEx(escHookHandle); } catch { } };
            SizeChanged += delegate { if (isFullScreen) ShowExitButton(true); };

            // 看门狗: 状态机 60 秒无心跳 = 卡死(如 9p/进程挂起), 整个控制台自愈重启
            var wd = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(10) };
            wd.Tick += delegate
            {
                if (!running || !wantConnect) return;
                if ((DateTime.Now.Ticks - System.Threading.Interlocked.Read(ref lastWorkerBeatTicks)) > 600000000L)  // 60s (1e7 ticks/s)
                {
                    UiSet("状态机无响应，自动重启控制台…", Brushes.Red);
                    suppressPowerOffOnClose = true;   // 自愈重启≠用户关机, 虚拟机必须保持运行
                    try { Process.Start(Process.GetCurrentProcess().MainModule.FileName, isFullScreen ? "--takeover --fullscreen" : "--takeover"); } catch { }
                    Close();
                }
            };
            wd.Start();

            worker = new Thread(WorkerLoop) { IsBackground = true, Name = "ds-console-worker" };
            worker.Start();
            Loaded += delegate
            {
                RequestConnection();
                if (startFullScreen) Dispatcher.BeginInvoke((Action)delegate { ToggleFullScreen(); });
            };
        }

        static SolidColorBrush MakeBrush(int rgb)
        {
            var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            brush.Freeze();
            return brush;
        }

        TextBlock Label(string text, Brush ink, double size, bool strong = false)
        {
            return new TextBlock
            {
                Text = text, Foreground = ink, FontSize = size,
                FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        Menu BuildMenu()
        {
            var menu = new Menu { Background = Chrome, Foreground = TextInk, FontSize = 12, Padding = new Thickness(10, 1, 10, 1) };

            var mFile = new MenuItem { Header = "文件(_F)" };
            var miExit = new MenuItem { Header = "退出(_X)" };
            miExit.Click += delegate { Close(); };
            mFile.Items.Add(miExit);

            var mVm = new MenuItem { Header = "虚拟机(_M)" };
            var miPower = new MenuItem { Header = "连接桌面(_C)" };
            miPower.Click += delegate { RequestConnection(); };
            var miStop = new MenuItem { Header = "关机(_S)" };
            miStop.Click += delegate { DoStop(); };
            var miRestart = new MenuItem { Header = "重启(_R)" };
            miRestart.Click += delegate { DoRestart(); };
            var miBackup = new MenuItem { Header = "备份(_B)…" };
            miBackup.Click += delegate { DoBackup(); };
            var miTerminal = new MenuItem { Header = "打开终端(_H)" };
            miTerminal.Click += delegate { OpenTerminal(); };
            mVm.Items.Add(miPower); mVm.Items.Add(miStop); mVm.Items.Add(miRestart);
            mVm.Items.Add(new Separator()); mVm.Items.Add(miBackup);
            mVm.Items.Add(new Separator()); mVm.Items.Add(miTerminal);

            var mView = new MenuItem { Header = "查看(_V)" };
            var miFull = new MenuItem { Header = "全屏(_F)", InputGestureText = "Ctrl+Alt+Enter" };
            miFull.Click += delegate { ToggleFullScreen(); };
            mView.Items.Add(miFull);

            var mHelp = new MenuItem { Header = "帮助(_H)" };
            var miLog = new MenuItem { Header = "查看连接日志(_L)" };
            miLog.Click += delegate { OpenConnectionLog(); };
            mHelp.Items.Add(miLog);
            var miAbout = new MenuItem { Header = "关于 Ubuntu-DS 控制台(_A)" };
            miAbout.Click += delegate
            {
                MessageBox.Show(this, "Ubuntu / WSL2\nGNOME 远程桌面：" + RdpHost + ":" + RdpPort
                    + "\n\n支持 RDP 剪贴板和桌面自动重连。关闭控制台会关闭 Ubuntu-DS。",
                    "关于 Ubuntu-DS 控制台", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            mHelp.Items.Add(miAbout);

            menu.Items.Add(mFile); menu.Items.Add(mVm); menu.Items.Add(mView); menu.Items.Add(mHelp);
            return menu;
        }

        void OpenConnectionLog()
        {
            try
            {
                if (File.Exists(ConnectionLogPath))
                {
                    Process.Start(new ProcessStartInfo("notepad.exe", "\"" + ConnectionLogPath + "\"") { UseShellExecute = true });
                    return;
                }
                MessageBox.Show(this, "发起一次桌面连接后，控制台会生成日志。", "连接日志",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "无法打开连接日志", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        FrameworkElement BuildToolbar()
        {
            var toolbar = new DockPanel { LastChildFill = true, Margin = new Thickness(20, 0, 20, 0) };
            var identity = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            identity.Children.Add(new Border
            {
                Width = 38, Height = 38, CornerRadius = new CornerRadius(8), Background = Accent,
                Margin = new Thickness(0, 0, 11, 0),
                Child = Glyph("M3,4 L15,4 L15,12 L3,12 Z M6,15 L12,15 M9,12 L9,15", Brushes.White)
            });
            var identityText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            identityText.Children.Add(Label("Ubuntu-DS", TextInk, 21, true));
            identityText.Children.Add(Label("本地 WSL2 工作站", SecondaryInk, 12));
            identity.Children.Add(identityText);
            DockPanel.SetDock(identity, Dock.Left);
            toolbar.Children.Add(identity);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(MakeToolBtn("连接桌面", "M9,2 L9,8 M5,4 A6,6 0 1,0 13,4", "启动 Ubuntu-DS 并连接 GNOME 桌面",
                delegate { RequestConnection(); }, true));
            actions.Children.Add(MakeToolBtn("终端", "M3,4 L15,4 L15,14 L3,14 Z M6,7 L8,9 L6,11 M10,11 L12,11",
                "打开 Ubuntu 终端", OpenTerminal));
            actions.Children.Add(ToolSeparator());
            actions.Children.Add(MakeToolBtn("重启", "M14,6 A5.5,5.5 0 1,0 14,12 M14,3 L14,6 L11,6", "重新启动 Ubuntu-DS", DoRestart));
            actions.Children.Add(MakeToolBtn("备份", "M3,6 L15,6 L15,14 L3,14 Z M2.5,3 L15.5,3 L15.5,6 L2.5,6 Z M7,9 L11,9",
                "关闭 Ubuntu-DS 并导出系统备份（VHDX）", DoBackup));
            actions.Children.Add(MakeToolBtn("全屏", "M3,7 L3,3 L7,3 M11,3 L15,3 L15,7 M15,11 L15,15 L11,15 M7,15 L3,15 L3,11",
                "全屏显示（Ctrl+Alt+Enter）", ToggleFullScreen));
            actions.Children.Add(ToolSeparator());
            actions.Children.Add(MakeToolBtn("关机", "M5,5 L13,5 L13,13 L5,13 Z", "安全关闭 Ubuntu-DS", DoStop, false, StatusError));
            DockPanel.SetDock(actions, Dock.Right);
            toolbar.Children.Add(actions);
            toolbar.Children.Add(new Border());
            return new Border { Background = Chrome, Child = toolbar };
        }

        Button MakeToolBtn(string text, string icon, string tip, Action onClick, bool primary = false, Brush ink = null)
        {
            var contentInk = primary ? Brushes.White : (ink ?? TextInk);
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var glyph = Glyph(icon, contentInk);
            glyph.Margin = new Thickness(0, 0, 6, 0);
            panel.Children.Add(glyph);
            panel.Children.Add(Label(text, contentInk, 13, primary));

            var b = new Button
            {
                Content = panel, ToolTip = tip, Cursor = Cursors.Hand, FocusVisualStyle = null,
                Height = 36, Margin = new Thickness(2, 0, 2, 0)
            };
            System.Windows.Automation.AutomationProperties.SetName(b, text);
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, primary ? Accent : Brushes.Transparent);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetValue(Border.PaddingProperty, new Thickness(11, 0, 11, 0));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(cp);
            template.VisualTree = border;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, MakeBrush(primary ? 0x9C3218 : 0xECE6EF)));
            template.Triggers.Add(hover);
            var press = new Trigger { Property = Button.IsPressedProperty, Value = true };
            press.Setters.Add(new Setter(Border.BackgroundProperty, MakeBrush(primary ? 0x842A16 : 0xDED6E4)));
            template.Triggers.Add(press);
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, primary ? Brushes.White : Accent));
            template.Triggers.Add(focus);
            b.Template = template;
            b.Click += delegate { onClick(); };
            return b;
        }

        FrameworkElement ToolSeparator()
        {
            return new Border
            {
                Width = 1, Height = 24, Margin = new Thickness(8, 0, 8, 0),
                Background = Divider, VerticalAlignment = VerticalAlignment.Center
            };
        }

        FrameworkElement Glyph(string geometry, Brush ink)
        {
            var canvas = new Canvas
            {
                Width = 18, Height = 18,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            canvas.Children.Add(new SPath
            {
                Data = Geometry.Parse(geometry), Stroke = ink, StrokeThickness = 1.55,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
            });
            return canvas;
        }

        FrameworkElement BuildSessionBar()
        {
            var row = new DockPanel { LastChildFill = false, Margin = new Thickness(20, 0, 20, 0) };
            var label = Label("桌面会话", TextInk, 13, true);
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);

            var status = new StackPanel { Orientation = Orientation.Horizontal };
            sessionDot = new Ellipse
            {
                Width = 7, Height = 7, Fill = SecondaryInk, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 7, 0)
            };
            sessionState = Label("未连接", SecondaryInk, 12, true);
            status.Children.Add(sessionDot);
            status.Children.Add(sessionState);
            sessionBadge = new Border
            {
                Background = MakeBrush(0xEAE5EE), CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 3, 10, 3), VerticalAlignment = VerticalAlignment.Center, Child = status
            };
            DockPanel.SetDock(sessionBadge, Dock.Right);
            row.Children.Add(sessionBadge);
            return new Border
            {
                Background = MakeBrush(0xF3EFF5), BorderBrush = Divider,
                BorderThickness = new Thickness(0, 1, 0, 0), Child = row
            };
        }

        FrameworkElement BuildWaitingView()
        {
            var layout = new Grid
            {
                MaxWidth = 820, Margin = new Thickness(52, 24, 52, 24), VerticalAlignment = VerticalAlignment.Center
            };
            layout.ColumnDefinitions.Add(new ColumnDefinition());
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(246) });

            var overview = new StackPanel { Margin = new Thickness(0, 0, 35, 0), VerticalAlignment = VerticalAlignment.Center };
            var title = Label("GNOME 桌面", StageText, 42, true);
            title.Margin = new Thickness(0, 0, 0, 22);
            overview.Children.Add(title);

            var activity = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
            connectionTitle = Label("等待连接", StageText, 17, true);
            connectionDetail = Label("选择「连接桌面」启动 Ubuntu-DS。", StageMuted, 14);
            connectionDetail.Margin = new Thickness(0, 5, 0, 0);
            connectionDetail.TextWrapping = TextWrapping.Wrap;
            activity.Children.Add(connectionTitle);
            activity.Children.Add(connectionDetail);
            connectionLogLink = Label("", StageText, 13, true);
            connectionLogLink.Margin = new Thickness(0, 11, 0, 0);
            connectionLogLink.Visibility = Visibility.Collapsed;
            var logLink = new Hyperlink(new Run("查看连接日志")) { Foreground = MakeBrush(0xFFC3AD) };
            logLink.Click += delegate { OpenConnectionLog(); };
            connectionLogLink.Inlines.Add(logLink);
            activity.Children.Add(connectionLogLink);
            overview.Children.Add(new Border
            {
                BorderThickness = new Thickness(3, 0, 0, 0), BorderBrush = MakeBrush(0xED7855), Child = activity
            });
            layout.Children.Add(overview);

            var details = new StackPanel();
            AddSessionDetail(details, "系统", "Ubuntu 26.04 LTS");
            AddSessionDetail(details, "运行平台", "Windows / WSL2");
            AddSessionDetail(details, "本机远程桌面", RdpHost + ":" + RdpPort);
            var detailsFrame = new Border
            {
                BorderBrush = StageDivider, BorderThickness = new Thickness(1, 0, 0, 0),
                Padding = new Thickness(27, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = details
            };
            Grid.SetColumn(detailsFrame, 1);
            layout.Children.Add(detailsFrame);
            return layout;
        }

        void AddSessionDetail(StackPanel parent, string text, string value)
        {
            if (parent.Children.Count > 0)
                parent.Children.Add(new Border { Height = 1, Background = StageDivider, Margin = new Thickness(0, 11, 0, 11) });
            var line = new DockPanel { LastChildFill = false };
            var name = Label(text, StageMuted, 12);
            var data = Label(value, StageText, 13, true);
            DockPanel.SetDock(name, Dock.Left);
            DockPanel.SetDock(data, Dock.Right);
            line.Children.Add(name);
            line.Children.Add(data);
            parent.Children.Add(line);
        }

        FrameworkElement BuildStatusBar()
        {
            var bar = new DockPanel { LastChildFill = true, Margin = new Thickness(20, 0, 20, 0) };
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            txtRdp = StatusMetric(right, "RDP —", "本机桌面端口 " + RdpPort, 103);
            txtMem = StatusMetric(right, "内存 —", "WSL2 内存占用", 118);
            txtCpu = StatusMetric(right, "CPU —", "WSL2 CPU 占用", 78);
            txtUp = StatusMetric(right, "运行 —", "Ubuntu-DS 运行时长", 102);
            DockPanel.SetDock(right, Dock.Right);
            bar.Children.Add(right);
            statusText = Label("就绪", SecondaryInk, 12);
            statusText.Margin = new Thickness(0, 0, 18, 0);
            statusText.TextTrimming = TextTrimming.CharacterEllipsis;
            bar.Children.Add(statusText);
            return new Border { Background = Chrome, BorderBrush = Divider, BorderThickness = new Thickness(0, 1, 0, 0), Child = bar };
        }

        TextBlock StatusMetric(StackPanel bar, string text, string tip, double width)
        {
            var label = Label(text, SecondaryInk, 12);
            bar.Children.Add(new Border
            {
                Child = label, Width = width, ToolTip = tip, BorderBrush = Divider,
                BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(12, 0, 5, 0)
            });
            return label;
        }

        // ===================== UI 更新 (线程安全) =====================
        void Ui(string t, Brush c)
        {
            Dispatcher.BeginInvoke((Action)delegate { UiSet(t, c); });
        }

        void UiStats(string mem, string cpu, string up, string rdp)
        {
            Dispatcher.BeginInvoke((Action)delegate
            {
                if (mem != null) txtMem.Text = mem;
                if (cpu != null) txtCpu.Text = cpu;
                if (up != null) txtUp.Text = up;
                if (rdp != null) txtRdp.Text = rdp;
            });
        }

        // ===================== 资源监控 (worker 线程, UNC 读 /proc, 轻量) =====================
        void PollStats()
        {
            string mem = "内存 —", cpu = "CPU —", up = "运行 —";
            try
            {
                string mi = File.ReadAllText(@"\\wsl.localhost\Ubuntu-DS\proc\meminfo");
                double totalK = 0, availK = 0;
                foreach (string line in mi.Split('\n'))
                {
                    if (line.StartsWith("MemTotal:")) totalK = ParseKb(line);
                    else if (line.StartsWith("MemAvailable:")) availK = ParseKb(line);
                    if (totalK > 0 && availK > 0) break;
                }
                mem = totalK > 0
                    ? string.Format(CultureInfo.InvariantCulture, "内存 {0:F1}/{1:F0} GB", (totalK - availK) / 1048576.0, totalK / 1048576.0)
                    : "内存 —";
            }
            catch { mem = "内存 —"; }
            try
            {
                // CPU: /proc/stat 两次采样差值
                string st = File.ReadAllText(@"\\wsl.localhost\Ubuntu-DS\proc\stat");
                foreach (string line in st.Split('\n'))
                {
                    if (line.StartsWith("cpu "))
                    {
                        string[] f = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        double idle = double.Parse(f[4], CultureInfo.InvariantCulture) + (f.Length > 5 ? double.Parse(f[5], CultureInfo.InvariantCulture) : 0);
                        double total = 0;
                        for (int i = 1; i < f.Length; i++) total += double.Parse(f[i], CultureInfo.InvariantCulture);
                        if (lastCpuTotal >= 0)
                        {
                            double dTotal = total - lastCpuTotal, dBusy = (total - idle) - lastCpuBusy;
                            if (dTotal > 0) cpu = "CPU " + Math.Round(100.0 * dBusy / dTotal) + "%";
                            else cpu = "CPU —";
                        }
                        else cpu = "CPU —";
                        lastCpuBusy = total - idle; lastCpuTotal = total;
                        break;
                    }
                }
            }
            catch { cpu = "CPU —"; lastCpuTotal = -1; }
            try
            {
                string up_s = File.ReadAllText(@"\\wsl.localhost\Ubuntu-DS\proc\uptime");
                double secs = double.Parse(up_s.Split(' ')[0], CultureInfo.InvariantCulture);
                up = "运行 " + FormatUp(secs);
            }
            catch { up = "运行 —"; }
            UiStats(mem, cpu, up, null);
        }

        static double ParseKb(string line)
        {
            string[] f = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            double v; return f.Length > 1 && double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        static string FormatUp(double secs)
        {
            if (secs < 90) return Math.Round(secs) + "s";
            if (secs < 5400) return Math.Round(secs / 60) + "m";
            double h = Math.Floor(secs / 3600);
            return h + "h" + Math.Round((secs - h * 3600) / 60) + "m";
        }

        // ===================== 工具(阻塞, 仅 worker 线程调用) =====================
        static bool ProbeDesktop()
        {
            // socat can accept locally before the GNOME server can answer an X.224 request.
            byte[] request = { 3, 0, 0, 19, 14, 0xE0, 0, 0, 0, 0, 0, 1, 0, 8, 0, 3, 0, 0, 0 };
            try
            {
                using (var client = new TcpClient(AddressFamily.InterNetwork))
                {
                    if (!client.ConnectAsync(IPAddress.Parse(RdpHost), RdpPort).Wait(900)) return false;
                    using (var stream = client.GetStream())
                    {
                        stream.WriteTimeout = 1200;
                        stream.Write(request, 0, request.Length);
                        byte[] response = new byte[19];
                        var stopwatch = Stopwatch.StartNew();
                        int read = 0, expected = 4;
                        while (read < expected)
                        {
                            int remaining = 1500 - (int)stopwatch.ElapsedMilliseconds;
                            if (remaining <= 0) return false;
                            stream.ReadTimeout = remaining;
                            int count = stream.Read(response, read, expected - read);
                            if (count == 0) return false;
                            read += count;
                            if (read == 4)
                            {
                                expected = (response[2] << 8) | response[3];
                                if (response[0] != 3 || response[1] != 0 || (expected != 11 && expected != 19)) return false;
                            }
                        }
                        return response[4] == expected - 5 && response[5] == 0xD0 &&
                            (expected == 11 || (response[11] == 2 && response[13] == 8 && response[14] == 0));
                    }
                }
            }
            catch { return false; }
        }

        static string RunCmd(string exe, string args, int timeoutMs)
        {
            return RunCmd(exe, args, timeoutMs, null);
        }

        static string RunCmd(string exe, string args, int timeoutMs, Encoding outEnc)
        {
            try
            {
                var si = new ProcessStartInfo
                {
                    FileName = exe, Arguments = args, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                if (outEnc != null) si.StandardOutputEncoding = outEnc;
                var p = Process.Start(si);
                string s = null;
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return null; }
                try { s = p.StandardOutput.ReadToEnd(); } catch { }
                return s ?? "";
            }
            catch { return null; }
        }

        // ===================== 剪贴板双向同步 (v3 事件驱动) =====================
        // grd+FreeRDP 的 skip-channeljoin 与 mstsc 不兼容(通道握手后 mstsc 不响应),
        // RDP 通道剪贴板不可用。v3 双向事件驱动, 零轮询:
        //   Linux→Windows: 常驻 wl-paste -w cat watcher (stdout 防抖聚合)
        //   Windows→Linux: 消息窗口 AddClipboardFormatListener (WM_CLIPBOARDUPDATE)
        // 注意: 必须显式 WAYLAND_DISPLAY=wayland-1 (headless 会话);
        // 落在 WSLg 的 wayland-0 上会与 Windows 剪贴板桥接形成反馈风暴, 占死剪贴板
        void StartStatsLoop()
        {
            if (statsWorker != null) return;
            statsWorker = new Thread(delegate()
            {
                while (running)
                {
                    if (wantConnect && embeddedHwnd != IntPtr.Zero) PollStats();
                    Thread.Sleep(2200);
                }
            }) { IsBackground = true, Name = "ds-resource-monitor" };
            statsWorker.Start();
        }

        static void LogConnection(string message)
        {
            try
            {
                lock (connectionLogLock)
                {
                    Directory.CreateDirectory(IoPath.GetDirectoryName(ConnectionLogPath));
                    File.AppendAllText(ConnectionLogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                        + " " + message + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }

        static int? RunConnectionCommand(string name, string exe, string arguments, int timeoutMs, Func<bool> cancelled)
        {
            if (timeoutMs <= 0 || cancelled()) return null;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using (var p = Process.Start(new ProcessStartInfo(exe, arguments)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                }))
                {
                    p.OutputDataReceived += delegate { };
                    p.ErrorDataReceived += delegate { };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    while (!cancelled())
                    {
                        int remaining = timeoutMs - (int)stopwatch.ElapsedMilliseconds;
                        if (remaining <= 0) break;
                        if (!p.WaitForExit(Math.Min(200, remaining))) continue;
                        LogConnection(name + " exit=" + p.ExitCode + " elapsedMs=" + stopwatch.ElapsedMilliseconds);
                        return p.ExitCode;
                    }
                    try { if (!p.HasExited) p.Kill(); } catch { }
                    LogConnection(name + (cancelled() ? " cancelled" : " timed out") + " elapsedMs=" + stopwatch.ElapsedMilliseconds);
                }
            }
            catch (Exception ex) { LogConnection(name + " failed: " + ex.GetType().Name); }
            return null;
        }

        public static bool PrepareDesktop(Func<bool> cancelled, Action<string> report)
        {
            bool keepAliveRequested = false;
            return PrepareDesktop(cancelled, report, ref keepAliveRequested);
        }

        static bool PrepareDesktop(Func<bool> cancelled, Action<string> report, ref bool keepAliveRequested)
        {
            const int readinessBudgetMs = 105000;
            const string startServices = "-d Ubuntu-DS --exec systemctl --user --no-block start " +
                "ds-shell-headless.service gnome-remote-desktop-headless.service";
            var stopwatch = Stopwatch.StartNew();
            int serviceAttempts = 0;
            long nextServiceTry = 0;
            while (!cancelled() && stopwatch.ElapsedMilliseconds < readinessBudgetMs)
            {
                if (ProbeDesktop() && !cancelled())
                {
                    if (!keepAliveRequested) keepAliveRequested = RequestKeepAlive(cancelled);
                    if (cancelled()) return false;
                    LogConnection("RDP confirmed at " + RdpHost + ":" + RdpPort + " elapsedMs=" + stopwatch.ElapsedMilliseconds);
                    return true;
                }
                if (serviceAttempts < 3 && stopwatch.ElapsedMilliseconds >= nextServiceTry)
                {
                    if (report != null) report(serviceAttempts == 0 ? "正在启动 Ubuntu-DS…" : "正在重试启动桌面服务…");
                    int? code = RunConnectionCommand("Desktop service request #" + ++serviceAttempts, "wsl.exe", startServices,
                        Math.Min(42000, readinessBudgetMs - (int)stopwatch.ElapsedMilliseconds), cancelled);
                    nextServiceTry = stopwatch.ElapsedMilliseconds + (code == 0 ? 24000 : 3000);
                    if (!keepAliveRequested && code == 0)
                        keepAliveRequested = RequestKeepAlive(cancelled);
                }
                if (report != null && !cancelled())
                    report("正在等待 GNOME 桌面…（" + stopwatch.ElapsedMilliseconds / 1000 + " 秒）");
                if (!cancelled()) Thread.Sleep(1200);
            }
            LogConnection("Desktop readiness " + (cancelled() ? "cancelled" : "timed out") + " elapsedMs=" + stopwatch.ElapsedMilliseconds);
            return false;
        }

        static bool RequestKeepAlive(Func<bool> cancelled)
        {
            RunConnectionCommand("Enable keep-alive", "schtasks.exe", "/Change /TN UbuntuDS-KeepAlive /ENABLE", 5000, cancelled);
            RunConnectionCommand("Request keep-alive", "schtasks.exe", "/Run /TN UbuntuDS-KeepAlive", 5000, cancelled);
            return !cancelled();
        }

        bool keepAliveRequested;
        void Beat() { Interlocked.Exchange(ref lastWorkerBeatTicks, DateTime.Now.Ticks); }
        bool ConnectionCancelled() { Beat(); return !running || !wantConnect || Thread.VolatileRead(ref pendingPowerAction) != 0; }

        void RequestConnection()
        {
            if (Thread.VolatileRead(ref pendingPowerAction) != 0) return;
            wantConnect = true;
            wake.Set();
        }

        void KillMstsc()
        {
            var client = Interlocked.Exchange(ref mstsc, null);
            embeddedHwnd = IntPtr.Zero;
            if (client == null) return;
            try { if (!client.HasExited) client.Kill(); } catch { }
            client.Dispose();
        }

        // ===================== 后台状态机 =====================
        void WorkerLoop()
        {
            while (running)
            {
                Beat();
                try
                {
                    var action = (PowerAction)Interlocked.CompareExchange(ref pendingPowerAction, 0, 0);
                    if (action != PowerAction.None) RunPowerAction(action);
                    else if (wantConnect) AdvanceConnection();
                }
                catch (Exception ex)
                {
                    if (wantConnect)
                    {
                        KillMstsc();
                        FailConnection("连接意外中断。请重试或查看连接日志。", "Worker exception: " + ex.GetType().Name);
                    }
                }
                Beat();
                wake.WaitOne(500);
            }
        }

        void AdvanceConnection()
        {
            long now = connectionClock.ElapsedMilliseconds;
            if (mstsc != null)
            {
                if (mstsc.HasExited)
                {
                    RetryConnection("mstsc exited with code " + mstsc.ExitCode);
                    return;
                }
                IntPtr shell = FindShellHwnd();
                if (shell != IntPtr.Zero)
                {
                    if (embeddedHwnd != shell)
                    {
                        embeddedHwnd = shell;
                        phase = Phase.Embedded;
                        phaseAt = now;
                        Dispatcher.BeginInvoke((Action)delegate
                        {
                            if (embeddedHwnd == shell && Native.IsWindow(shell)) EmbedShell(shell);
                        });
                        LogConnection("mstsc desktop window found");
                        Ui("GNOME 桌面已连接", Brushes.LimeGreen);
                        UiStats(null, null, null, "RDP " + RdpPort + " ●");

                        StartStatsLoop();
                    }
                    if (now - phaseAt >= 15000) connectAttempts = 0;
                    return;
                }
                if (phase == Phase.Embedded || now - phaseAt >= 120000)
                    RetryConnection("mstsc desktop window unavailable");
                return;
            }

            if (connectAttempts >= MaxConnectAttempts)
            {
                FailConnection("远程桌面连接失败。请重试或查看连接日志。", "Client retries exhausted");
                return;
            }
            if (now < nextConnectAt) return;
            phase = Phase.Booting;
            if (!PrepareDesktop(ConnectionCancelled, delegate(string status) { Ui(status, Brushes.Orange); }, ref keepAliveRequested))
            {
                if (!ConnectionCancelled())
                    FailConnection("桌面服务未就绪。请重试或查看连接日志。", "Desktop service readiness failed");
                return;
            }
            if (ConnectionCancelled()) return;
            connectAttempts++;
            Ui("请在远程桌面窗口完成登录（" + connectAttempts + "/" + MaxConnectAttempts + "）", Brushes.Orange);
            DoConnect();
        }

        void RetryConnection(string reason)
        {
            KillMstsc();
            phase = Phase.Idle;
            nextConnectAt = connectionClock.ElapsedMilliseconds + 1500 * Math.Max(1, connectAttempts);
            LogConnection(reason + " attempt=" + connectAttempts);
            Ui("桌面连接中断，正在重试…", Brushes.Orange);
            UiStats(null, null, null, "RDP —");
        }

        void FailConnection(string status, string reason)
        {
            wantConnect = false;
            ResetConnection();
            LogConnection(reason);
            Ui(status, Brushes.Red);
        }

        void ResetConnection()
        {
            phase = Phase.Idle;
            connectAttempts = 0;
            nextConnectAt = 0;
            keepAliveRequested = false;
        }

        void DoConnect()
        {
            try
            {
                mstsc = Process.Start(new ProcessStartInfo { FileName = "mstsc.exe", Arguments = "\"" + RdpFile + "\"", UseShellExecute = true });

                phase = Phase.Connecting;
                phaseAt = connectionClock.ElapsedMilliseconds;
                LogConnection("mstsc started attempt=" + connectAttempts);
            }
            catch (Exception ex) { RetryConnection("mstsc launch failed: " + ex.GetType().Name); }
        }

        // ===================== 动作 =====================
        void DoRestart()
        {
            if (MessageBox.Show("重启 Ubuntu-DS？运行中的程序会被终止（数据保留）。", "重启",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            QueuePowerAction(PowerAction.Restart, "正在重启 Ubuntu-DS…");
        }

        void OpenTerminal()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "wsl.exe",
                    Arguments = "-d Ubuntu-DS",
                    UseShellExecute = true
                });
            }
            catch (Exception ex) { MessageBox.Show("启动 Ubuntu 终端失败: " + ex.Message); }
        }

        // ===================== mstsc 窗口定位 =====================
        IntPtr FindShellHwnd()
        {
            if (mstsc == null || mstsc.HasExited) return IntPtr.Zero;
            uint pid = (uint)mstsc.Id;
            IntPtr shell = IntPtr.Zero;
            Native.EnumWindows(delegate(IntPtr top, IntPtr lp)
            {
                uint wpid; Native.GetWindowThreadProcessId(top, out wpid);
                if (wpid != pid || !Native.IsWindowVisible(top)) return true;
                var cls = new StringBuilder(256); Native.GetClassName(top, cls, 256);
                string c = cls.ToString();
                if (c == "TscShellContainerClass") { shell = top; return true; }
                return true;
            }, IntPtr.Zero);
            if (shell == IntPtr.Zero && mstscHost.HostHwnd != IntPtr.Zero)
            {
                Native.EnumChildWindows(mstscHost.HostHwnd, delegate(IntPtr ch, IntPtr lp)
                {
                    var cls = new StringBuilder(256); Native.GetClassName(ch, cls, 256);
                    uint wpid; Native.GetWindowThreadProcessId(ch, out wpid);
                    if (wpid == pid && cls.ToString() == "TscShellContainerClass") { shell = ch; return false; }
                    return true;
                }, IntPtr.Zero);
            }
            return shell;
        }

        // ===================== 嵌入与贴合 (UI 线程) =====================
        static int DpiOf(IntPtr h)
        {
            try { uint d = Native.GetDpiForWindow(h); if (d >= 96 && d <= 480) return (int)d; } catch { }
            return 96;
        }

        void EmbedShell(IntPtr shell)
        {
            Native.SetWindowLong(shell, -16, Native.GetWindowLong(shell, -16) & ~0x00C40000); // 去标题栏/边框
            Native.SetParent(shell, mstscHost.HostHwnd);
            FitShell();
        }

        void FitShell()
        {
            if (embeddedHwnd == IntPtr.Zero || mstscHost.HostHwnd == IntPtr.Zero) return;
            Native.RECT r; Native.GetClientRect(mstscHost.HostHwnd, out r);
            if (r.Right <= 0 || r.Bottom <= 0) return;
            Native.SetWindowPos(embeddedHwnd, IntPtr.Zero, 0, 0, r.Right, r.Bottom,
                0x0004 | 0x0010 | 0x0200);  // SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER
        }

        void ToggleFullScreen()
        {
            if (!isFullScreen)
            {
                windowedState = WindowState;
                windowedBounds = RestoreBounds;
                // 折叠全部装饰行: 显示区占满整窗, 无白边
                rowMenu.Height = new GridLength(0); rowToolbar.Height = new GridLength(0);
                rowTabs.Height = new GridLength(0); rowStatus.Height = new GridLength(0);
                Background = Brushes.Black;
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Normal;
                WindowState = WindowState.Maximized;
                isFullScreen = true;
                Dispatcher.BeginInvoke((Action)delegate
                {
                    FitShell();
                    ShowExitButton(true);
                });
            }
            else
            {
                isFullScreen = false;
                ShowExitButton(false);
                rowMenu.Height = new GridLength(MenuHeight);
                rowToolbar.Height = new GridLength(ToolbarHeight);
                rowTabs.Height = new GridLength(SessionBarHeight);
                rowStatus.Height = new GridLength(StatusBarHeight);
                Background = Chrome;
                WindowState = WindowState.Normal;
                WindowStyle = WindowStyle.SingleBorderWindow;
                if (!windowedBounds.IsEmpty && windowedBounds.Width > 0 && windowedBounds.Height > 0)
                {
                    Left = windowedBounds.Left;
                    Top = windowedBounds.Top;
                    Width = windowedBounds.Width;
                    Height = windowedBounds.Height;
                }
                if (windowedState == WindowState.Maximized) WindowState = WindowState.Maximized;
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, (Action)delegate
                {
                    FitShell();
                });
            }
        }

        // 退出全屏按钮: 原生子窗口, 挂在主窗口 (与 HwndHost 平级, mstsc 遮不到)
        void ShowExitButton(bool show)
        {
            IntPtr winHwnd = new WindowInteropHelper(this).Handle;
            if (show)
            {
                if (exitBtnHwnd == IntPtr.Zero && winHwnd != IntPtr.Zero)
                {
                    // WS_CHILD | WS_VISIBLE | BS_FLAT, id=1, 挂主窗口
                    exitBtnHwnd = Native.CreateWindowEx(0, "BUTTON", "✕  退出全屏 (Esc)",
                        0x40000000 | 0x10000000 | 0x00008000,
                        0, 0, 150, 30, winHwnd, (IntPtr)1, IntPtr.Zero, IntPtr.Zero);
                    if (exitBtnHwnd != IntPtr.Zero)
                        Native.SendMessage(exitBtnHwnd, 0x0030 /*WM_SETFONT*/, Native.GetStockObject(17), (IntPtr)1);
                }
                if (exitBtnHwnd != IntPtr.Zero)
                {
                    Native.RECT cr; Native.GetClientRect(winHwnd, out cr);
                    // PMv2: cr is physical; scale the button with the window DPI so it keeps its visual size
                    int d = DpiOf(winHwnd);
                    int bw = 150 * d / 96, bh = 30 * d / 96, m = 10 * d / 96;
                    Native.MoveWindow(exitBtnHwnd, cr.Right - bw - m, m, bw, bh, true);
                    Native.ShowWindow(exitBtnHwnd, 5 /*SW_SHOW*/);
                    // 抬到 HwndHost(Static) 之上, 否则被宿主窗口遮住
                    Native.SetWindowPos(exitBtnHwnd, (IntPtr)0, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0040);
                }
            }
            else if (exitBtnHwnd != IntPtr.Zero)
            {
                Native.ShowWindow(exitBtnHwnd, 0 /*SW_HIDE*/);
            }
        }

        // ===================== Esc 全局钩子 (键盘焦点在 mstsc 里, WPF 收不到) =====================
        static Native.KbdHookProc escHookProc;
        static IntPtr escHookHandle = IntPtr.Zero;

        IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0111 && isFullScreen)   // WM_COMMAND <- 退出全屏按钮 (id=1)
            {
                Dispatcher.BeginInvoke((Action)delegate { if (isFullScreen) ToggleFullScreen(); });
                handled = true;
            }
            return IntPtr.Zero;
        }

        void InstallEscHook()
        {
            if (escHookHandle != IntPtr.Zero) return;
            escHookProc = EscHookCallback;
            escHookHandle = Native.SetWindowsHookEx(13 /*WH_KEYBOARD_LL*/, escHookProc, IntPtr.Zero, 0);
        }

        IntPtr EscHookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && isFullScreen &&
                (wParam == (IntPtr)0x0100 /*WM_KEYDOWN*/ || wParam == (IntPtr)0x0104 /*WM_SYSKEYDOWN*/))
            {
                int vk = Marshal.ReadInt32(lParam) & 0xFF;
                if (vk == 0x1B)   // VK_ESCAPE
                {
                    Dispatcher.BeginInvoke((Action)delegate { if (isFullScreen) ToggleFullScreen(); });
                    return (IntPtr)1;   // 吞掉, 不进 mstsc
                }
            }
            return Native.CallNextHookEx(escHookHandle, code, wParam, lParam);
        }

        void DoBackup()
        {
            if (MessageBox.Show("备份会先关机再导出整个系统 (vhdx)，需要几分钟。现在开始？", "备份 Ubuntu-DS",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            QueuePowerAction(PowerAction.Backup, "正在准备系统备份…");
        }

        void DoStop()
        {
            if (MessageBox.Show("确定关机？数据不丢，点「开机/连接」可随时恢复现场。", "关机 Ubuntu-DS",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            QueuePowerAction(PowerAction.Stop, "正在关闭 Ubuntu-DS…");
        }

        void QueuePowerAction(PowerAction action, string status)
        {
            if (Interlocked.CompareExchange(ref pendingPowerAction, (int)action, 0) != 0) return;
            wantConnect = false;
            UiSet(status, Brushes.Orange);
            wake.Set();
        }

        void RunPowerAction(PowerAction action)
        {
            KillMstsc();
            ResetConnection();
            UiStats("内存 —", "CPU —", "运行 —", "RDP —");
            try
            {
                if (action == PowerAction.Backup)
                {
                    using (Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = "/c \"\"" + IoPath.Combine(BinDir, "ds-backup.cmd") + "\"\"", UseShellExecute = true })) { }
                    Ui("备份已启动，请查看备份窗口。", Brushes.Orange);
                    return;
                }
                Func<bool> cancelled = delegate { Beat(); return !running; };
                if (action == PowerAction.Stop)
                    RunConnectionCommand("Disable keep-alive", "schtasks.exe", "/Change /TN UbuntuDS-KeepAlive /DISABLE", 5000, cancelled);
                int? result = RunConnectionCommand("Terminate Ubuntu-DS", "wsl.exe", "--terminate Ubuntu-DS", 30000, cancelled);
                if (!running) return;
                if (result != 0)
                {
                    Ui("Ubuntu-DS 未能关闭。请重试或查看连接日志。", Brushes.Red);
                    return;
                }
                if (action == PowerAction.Restart) wantConnect = true;
                else Ui("Ubuntu-DS 已关闭", Brushes.Gray);
            }
            catch (Exception ex)
            {
                LogConnection("Power action " + action + " failed: " + ex.GetType().Name);
                Ui("操作失败。请重试或查看连接日志。", Brushes.Red);
            }
            finally
            {
                Interlocked.Exchange(ref pendingPowerAction, 0);
                if (wantConnect) wake.Set();
            }
        }

        void UiSet(string t, Brush c)
        {
            if (statusText.Text == t && lastStatusBrush == c) return;

            statusText.Text = t;
            statusText.ToolTip = t;
            connectionDetail.Text = c == Brushes.Gray && t == "已断开"
                ? "选择「连接桌面」启动或重新连接 Ubuntu-DS。" : t;
            if (lastStatusBrush == c) return;
            lastStatusBrush = c;

            bool connected = c == Brushes.LimeGreen;
            bool failed = c == Brushes.Red;
            bool working = c == Brushes.Orange;
            Brush color = connected ? StatusOk : failed ? StatusError : working ? StatusWorking : SecondaryInk;
            sessionBadge.Background = MakeBrush(connected ? 0xDFEEE7 : failed ? 0xF9E3E7 : working ? 0xF7E9D9 : 0xEAE5EE);
            sessionDot.Fill = color;
            statusText.Foreground = color;
            sessionState.Foreground = color;
            sessionState.Text = connected ? "已连接" : failed ? "连接失败" : working ? "处理中" : "未连接";
            connectionTitle.Text = failed ? "无法连接桌面" : working ? "正在准备桌面" : "等待连接";
            connectionLogLink.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
            waitingView.Visibility = connected ? Visibility.Hidden : Visibility.Visible;
            mstscHost.Visibility = connected ? Visibility.Visible : Visibility.Hidden;
            if (connected)
                Dispatcher.BeginInvoke((Action)FitShell, DispatcherPriority.Loaded);
        }

        void Cleanup()
        {
            running = false; wake.Set();
            wantConnect = false;

            if (worker != null) worker.Join(3000);
            KillMstsc();
            // 看门狗重启控制台时保留 Ubuntu-DS 会话。
            if (!suppressPowerOffOnClose)
            {
                LogConnection("Console closed; stopping Ubuntu-DS");
                RunCmd("schtasks.exe", "/Change /TN \"UbuntuDS-KeepAlive\" /DISABLE", 15000);
                RunCmd("wsl.exe", "--terminate Ubuntu-DS", 30000);
            }
        }
    }

    // 黑底宿主: static 子窗口 + 子类化擦除背景为纯黑 (避免白/灰闪底)
    public class MstscHost : HwndHost
    {
        public IntPtr HostHwnd = IntPtr.Zero;
        static WndProcDelegate procDel;
        static IntPtr origProc;

        delegate IntPtr WndProcDelegate(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr SetWindowLongPtr(IntPtr h, int idx, IntPtr val);
        [DllImport("user32.dll")] static extern IntPtr CallWindowProc(IntPtr proc, IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out Native.RECT r);
        [DllImport("user32.dll")] static extern int FillRect(IntPtr hdc, ref Native.RECT r, IntPtr brush);
        [DllImport("gdi32.dll")] static extern IntPtr GetStockObject(int i);

        public static Action ExitClicked;   // 退出全屏按钮 (WM_COMMAND) 回调

        static IntPtr WndProc(IntPtr h, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == 0x0014 && wParam != IntPtr.Zero)   // WM_ERASEBKGND
            {
                Native.RECT r; GetClientRect(h, out r);
                FillRect(wParam, ref r, GetStockObject(4));   // BLACK_BRUSH
                return (IntPtr)1;
            }
            if (msg == 0x0111 && ExitClicked != null)     // WM_COMMAND <- 退出全屏按钮
            {
                ExitClicked();
            }
            return CallWindowProc(origProc, h, msg, wParam, lParam);
        }

        protected override HandleRef BuildWindowCore(HandleRef parent)
        {
            HostHwnd = Native.CreateWindowEx(0, "static", "",
                0x40000000 | 0x10000000,   // WS_CHILD | WS_VISIBLE
                0, 0, 0, 0, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (HostHwnd != IntPtr.Zero)
            {
                procDel = WndProc;   // 静态字段锚定, 防止委托被 GC
                origProc = SetWindowLongPtr(HostHwnd, -4 /*GWLP_WNDPROC*/, Marshal.GetFunctionPointerForDelegate(procDel));
            }
            return new HandleRef(this, HostHwnd);
        }

        protected override void DestroyWindowCore(HandleRef hwnd) { Native.DestroyWindow(hwnd.Handle); }
    }

    public static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }
        public delegate bool EnumProc(IntPtr h, IntPtr lp);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lp);
        [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr lp);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int idx, int val);
        [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool repaint);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint flags);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("gdi32.dll")] public static extern IntPtr GetStockObject(int i);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
        public delegate IntPtr KbdHookProc(int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int id, KbdHookProc proc, IntPtr mod, uint tid);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
    }
}
