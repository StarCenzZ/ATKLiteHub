using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace ATKLiteHub
{
    public partial class App : Application
    {
        private static Mutex? _mutex;
        private NotifyIcon? _notifyIcon;
        private MainWindow? _mainWindow;
        private AppConfig? _config;
        private ProcessMonitorService? _monitorService;

        private ToolStripMenuItem? _menuStatus;
        private ToolStripMenuItem? _menuHeadsetStatus;
        private NotifyIcon? _headsetIcon;
        private ToolStripMenuItem? _menuProfile1;
        private ToolStripMenuItem? _menuProfile2;
        private ToolStripMenuItem? _menuAutoStart;

        protected override void OnStartup(StartupEventArgs e)
        {
            string logPath = @"C:\Users\Love2self\.gemini\antigravity\scratch\ATKLiteHub\startup.log";
            File.WriteAllText(logPath, $"OnStartup entered at {DateTime.Now}\n");

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                File.AppendAllText(logPath, $"AppDomain Unhandled: {args.ExceptionObject}\n");
            };
            DispatcherUnhandledException += (s, args) =>
            {
                File.AppendAllText(logPath, $"Dispatcher Unhandled: {args.Exception}\n");
            };

            try
            {
                const string appName = "Local\\ATKLiteHub_SingleInstance_Mutex";
                _mutex = new Mutex(true, appName, out bool isNewInstance);

                if (!isNewInstance)
                {
                    File.AppendAllText(logPath, "Mutex already exists, exiting.\n");
                    System.Windows.MessageBox.Show("ATK HUB LITE 已经在运行中（请查看右下角系统托盘图标）。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    Shutdown();
                    return;
                }

                base.OnStartup(e);
                File.AppendAllText(logPath, "Base OnStartup completed\n");

                _config = AppConfig.Load();
                File.AppendAllText(logPath, "Config loaded\n");

                _monitorService = new ProcessMonitorService(_config);
                File.AppendAllText(logPath, "MonitorService created\n");

                // Initialize NotifyIcon
                SetupTrayIcon();
                File.AppendAllText(logPath, "SetupTrayIcon completed\n");

                // Create MainWindow
                _mainWindow = new MainWindow(_config, _monitorService);
                File.AppendAllText(logPath, "MainWindow created\n");

                // Wire Service Events
                _monitorService.OnStateUpdated += MonitorService_OnStateUpdated;
                _monitorService.OnProfileAutoSwitched += (profile, triggerGame) =>
                {
                    string sub = !string.IsNullOrEmpty(triggerGame)
                        ? $"🎮 检测到 {triggerGame} 运行中 · 启用电竞配置"
                        : "💻 游戏已退出 · 恢复日常配置";
                    ProfileToastWindow.ShowToast(profile, sub);
                };
                _monitorService.OnNotificationRequested += (title, msg) =>
                {
                    _notifyIcon?.ShowBalloonTip(3000, title, msg, ToolTipIcon.Warning);
                };

                // Start Monitoring
                _monitorService.Start();
                File.AppendAllText(logPath, "MonitorService started\n");

                // Handle CLI flags
                bool startMinimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase)
                                                   || a.Equals("/minimized", StringComparison.OrdinalIgnoreCase));

                if (!startMinimized)
                {
                    _mainWindow.Show();
                    _mainWindow.Activate();
                    File.AppendAllText(logPath, "MainWindow shown\n");
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText(logPath, $"Exception in OnStartup: {ex}\n");
            }
        }

        private void SetupTrayIcon()
        {
            var menu = new ContextMenuStrip();

            _menuStatus = new ToolStripMenuItem("ATK HUB: 初始化中...") { Enabled = false };
            menu.Items.Add(_menuStatus);
            _menuHeadsetStatus = new ToolStripMenuItem("HyperX Cloud III: 检测中...") { Enabled = false };
            menu.Items.Add(_menuHeadsetStatus);
            menu.Items.Add(new ToolStripSeparator());

            _menuProfile1 = new ToolStripMenuItem("💻 切换至 板载配置 1 (日常模式)", null, (s, ev) =>
            {
                Task.Run(() => HardwareService.SetMouseProfile(1));
                ProfileToastWindow.ShowToast(1, "🖱️ 手动切换至日常模式");
                _monitorService?.TriggerImmediatePoll();
            });
            menu.Items.Add(_menuProfile1);

            _menuProfile2 = new ToolStripMenuItem("🎯 切换至 板载配置 2 (游戏电竞)", null, (s, ev) =>
            {
                Task.Run(() => HardwareService.SetMouseProfile(2));
                ProfileToastWindow.ShowToast(2, "🖱️ 手动切换至游戏电竞模式");
                _monitorService?.TriggerImmediatePoll();
            });
            menu.Items.Add(_menuProfile2);

            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add(new ToolStripMenuItem("⚙️ 打开控制面板", null, (s, ev) => ShowMainWindow()));

            _menuAutoStart = new ToolStripMenuItem("开机自动启动", null, (s, ev) =>
            {
                if (_menuAutoStart != null && _config != null)
                {
                    bool next = !_menuAutoStart.Checked;
                    _menuAutoStart.Checked = next;
                    _config.AutoStart = next;
                    _config.Save();
                    StartupHelper.SetAutoStart(next);
                }
            })
            {
                Checked = StartupHelper.IsAutoStartEnabled()
            };
            menu.Items.Add(_menuAutoStart);

            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add(new ToolStripMenuItem("退出", null, (s, ev) => ExitApplication()));

            _notifyIcon = new NotifyIcon
            {
                Text = "ATK HUB LITE",
                ContextMenuStrip = menu,
                Visible = true,
                Icon = TrayIconHelper.CreateCombinedIcon(null, null, null, false, 1)
            };

            _notifyIcon.MouseClick += (s, ev) =>
            {
                if (ev.Button == MouseButtons.Left)
                {
                    ToggleMainWindow();
                }
            };
        }

        [DllImport("psapi.dll")]
        private static extern int EmptyWorkingSet(IntPtr hwProc);

        public static void TrimWorkingSet()
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Aggressive);
                GC.WaitForPendingFinalizers();
                EmptyWorkingSet(Process.GetCurrentProcess().Handle);
            }
            catch { }
        }

        private void MonitorService_OnStateUpdated(SystemHardwareState state, bool gameRunning, string? activeGame)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    int? mouseBat = (state.Mouse.Connected && state.Mouse.Percent.HasValue) ? state.Mouse.Percent : null;
                    int? headsetBat = (state.Headset != null && state.Headset.Connected && state.Headset.Percent.HasValue) ? state.Headset.Percent : null;
                    int? lowestBat = state.LowestBatteryPercent;
                    bool charging = state.IsAnyCharging;
                    int profile = state.Mouse.CurrentProfile ?? 1;

                    // Unified single tray icon: Orange line (Mouse), Red line (Headset), Lowest number
                    var icon = TrayIconHelper.CreateCombinedIcon(mouseBat, headsetBat, lowestBat, charging, profile);

                    if (_notifyIcon != null)
                    {
                        var oldIcon = _notifyIcon.Icon;
                        _notifyIcon.Icon = icon;
                        oldIcon?.Dispose();

                        string mouseStr = mouseBat.HasValue ? $"{mouseBat.Value}%" : "--%";
                        if (state.Mouse.Charging) mouseStr += " ⚡";

                        string tip = $"鼠标: {mouseStr}";
                        if (headsetBat.HasValue)
                        {
                            string hsStr = $"{headsetBat.Value}%";
                            if (state.Headset != null && state.Headset.Charging) hsStr += " ⚡";
                            tip += $" | 耳机: {hsStr}";
                        }
                        tip += $" | 板载 {profile}";
                        if (gameRunning && !string.IsNullOrEmpty(activeGame))
                        {
                            tip += $" [{activeGame}]";
                        }
                        if (tip.Length > 63) tip = tip.Substring(0, 60) + "...";
                        _notifyIcon.Text = tip;
                    }

                    // Ensure separate headset tray icon is disabled (single icon mode)
                    if (_headsetIcon != null)
                    {
                        _headsetIcon.Visible = false;
                        _headsetIcon.Dispose();
                        _headsetIcon = null;
                    }

                    // Update context menu items
                    if (_menuStatus != null)
                    {
                        string statusInfo = state.Mouse.Connected
                            ? $"ATK A9: {(mouseBat.HasValue ? $"{mouseBat.Value}%" : "--%")} (板载配置 {profile})"
                            : "ATK 鼠标: 未连接";
                        _menuStatus.Text = statusInfo;
                    }
                    if (_menuHeadsetStatus != null)
                    {
                        var hs = state.Headset;
                        _menuHeadsetStatus.Text = (hs != null && hs.Connected && hs.Percent.HasValue)
                            ? $"HyperX Cloud III: {hs.Percent.Value}%{(hs.Charging ? " ⚡" : "")}"
                            : "HyperX Cloud III: 未连接";
                    }

                    if (_menuProfile1 != null) _menuProfile1.Checked = (profile == 1);
                    if (_menuProfile2 != null) _menuProfile2.Checked = (profile == 2);
                }
                catch { }
            });
        }

        private void ShowMainWindow()
        {
            if (_mainWindow == null) return;
            if (!_mainWindow.IsVisible) _mainWindow.Show();
            if (_mainWindow.WindowState == WindowState.Minimized) _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
            _mainWindow.Focus();
        }

        private void ToggleMainWindow()
        {
            if (_mainWindow == null) return;
            if (_mainWindow.IsVisible && _mainWindow.WindowState != WindowState.Minimized)
            {
                _mainWindow.Hide();
                TrimWorkingSet();
            }
            else
            {
                ShowMainWindow();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                File.AppendAllText(@"C:\Users\Love2self\.gemini\antigravity\scratch\ATKLiteHub\startup.log", $"App.OnExit called with code {e.ApplicationExitCode} at {DateTime.Now}\n");
            }
            catch { }
            base.OnExit(e);
        }

        private void ExitApplication()
        {
            try
            {
                File.AppendAllText(@"C:\Users\Love2self\.gemini\antigravity\scratch\ATKLiteHub\startup.log", $"ExitApplication called at {DateTime.Now}\n");
                _monitorService?.Stop();
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                }
                if (_headsetIcon != null)
                {
                    _headsetIcon.Visible = false;
                    _headsetIcon.Dispose();
                }
                _mainWindow?.PrepareExplicitExit();
                _mainWindow?.Close();
            }
            finally
            {
                _mutex?.ReleaseMutex();
                Shutdown();
            }
        }
    }
}
