using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Button = System.Windows.Controls.Button;
using ProgressBar = System.Windows.Controls.ProgressBar;

namespace ATKLiteHub
{
    public class MonitoredProcessDisplayItem
    {
        public string Name { get; set; } = "";
        public string StatusText { get; set; } = "";
        public Brush StatusColor { get; set; } = MainWindow.Muted;
        public Brush IconColor { get; set; } = MainWindow.Dim;
    }

    public partial class MainWindow : Window
    {
        private readonly AppConfig _config;
        private readonly ProcessMonitorService _monitorService;
        private bool _isExplicitExit = false;

        public MainWindow(AppConfig config, ProcessMonitorService monitorService)
        {
            InitializeComponent();
            _config = config;
            _monitorService = monitorService;

            // Load saved settings to UI controls
            ChkAutoSwitch.IsChecked = _config.AutoSwitchEnabled;
            ChkAutoStart.IsChecked = StartupHelper.IsAutoStartEnabled();

            RefreshProcessList();

            // Subscribe to state updates
            _monitorService.OnStateUpdated += MonitorService_OnStateUpdated;

            // Trigger immediate poll
            _monitorService.TriggerImmediatePoll();
        }

        public void PrepareExplicitExit()
        {
            _isExplicitExit = true;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_isExplicitExit)
            {
                e.Cancel = true;
                Hide();
                App.TrimWorkingSet();
            }
            else
            {
                _monitorService.OnStateUpdated -= MonitorService_OnStateUpdated;
                base.OnClosing(e);
            }
        }

        private void MonitorService_OnStateUpdated(SystemHardwareState state, bool gameRunning, string? activeGame)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    UpdateUiFromHardwareState(state, gameRunning, activeGame);
                }
                catch (Exception ex)
                {
                    File.AppendAllText(@"C:\Users\Love2self\.gemini\antigravity\scratch\ATKLiteHub\startup.log", $"UpdateUi error: {ex}\n");
                }
            });
        }

        // Palette
        internal static readonly Brush Accent = Freeze(0xFF, 0x4E, 0x27);
        internal static readonly Brush AccentSoft = Freeze(0x2A, 0x17, 0x12);
        internal static readonly Brush Green = Freeze(0x22, 0xC5, 0x5E);
        internal static readonly Brush Amber = Freeze(0xF5, 0x9E, 0x0B);
        internal static readonly Brush Red = Freeze(0xEF, 0x44, 0x44);
        internal static readonly Brush Muted = Freeze(0x8B, 0x93, 0xA1);
        internal static readonly Brush Dim = Freeze(0x4B, 0x55, 0x63);
        internal static readonly Brush Text = Freeze(0xF3, 0xF4, 0xF6);
        internal static readonly Brush SegIdle = Freeze(0x9C, 0xA3, 0xAF);
        internal static readonly Brush SegActiveBg = Freeze(0x23, 0x26, 0x2E);

        private static Brush Freeze(byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromRgb(r, g, b));
            br.Freeze();
            return br;
        }

        private static void ApplyDevice(DeviceStatus? dev, string offlineText,
            System.Windows.Shapes.Ellipse dot, TextBlock status, TextBlock percent, TextBlock chargeIcon, ProgressBar bar)
        {
            bool online = dev != null && dev.Connected && dev.Percent.HasValue;
            if (!online)
            {
                dot.Fill = Dim;
                status.Text = offlineText;
                percent.Text = "--";
                percent.Foreground = Dim;
                bar.Value = 0;
                chargeIcon.Visibility = Visibility.Collapsed;
                return;
            }

            int pct = Math.Clamp(dev!.Percent!.Value, 0, 100);
            dot.Fill = Green;
            status.Text = dev.Full ? "已充满" : dev.Charging ? "充电中" : "已连接";
            percent.Text = pct.ToString();
            percent.Foreground = Text;
            bar.Value = pct;
            chargeIcon.Visibility = dev.Charging ? Visibility.Visible : Visibility.Collapsed;
            bar.Foreground = dev.Charging ? Amber : pct <= 20 ? Red : Green;
        }

        private void UpdateUiFromHardwareState(SystemHardwareState state, bool gameRunning, string? activeGame)
        {
            ApplyDevice(state.Mouse, "未连接", DotMouse, TxtMouseStatus, TxtMousePercent, IcoMouseCharging, PbMouse);
            ApplyDevice(state.Headset, state.Headset == null ? "未检测到接收器" : "未开机",
                DotHeadset, TxtHeadsetStatus, TxtHeadsetPercent, IcoHeadsetCharging, PbHeadset);

            int currentProf = state.Mouse.CurrentProfile ?? 1;
            bool known = state.Mouse.Connected && state.Mouse.CurrentProfile.HasValue;
            StyleSegment(BtnProfile1, known && currentProf == 1, false);
            StyleSegment(BtnProfile2, known && currentProf == 2, true);

            RefreshProcessList(activeGame);

            if (gameRunning && !string.IsNullOrEmpty(activeGame))
            {
                DotStatus.Fill = Accent;
                TxtStatusTip.Text = $"{activeGame} 运行中 · 已锁定配置 2";
            }
            else if (!_config.AutoSwitchEnabled)
            {
                DotStatus.Fill = Dim;
                TxtStatusTip.Text = "游戏守护已关闭";
            }
            else
            {
                DotStatus.Fill = Green;
                TxtStatusTip.Text = "守护中";
            }
        }

        private static void StyleSegment(Button btn, bool active, bool gaming)
        {
            if (active)
            {
                btn.Background = gaming ? AccentSoft : SegActiveBg;
                btn.BorderBrush = gaming ? Accent : Freeze(0x3A, 0x3F, 0x4A);
                btn.Foreground = gaming ? Accent : Text;
            }
            else
            {
                btn.Background = System.Windows.Media.Brushes.Transparent;
                btn.BorderBrush = System.Windows.Media.Brushes.Transparent;
                btn.Foreground = SegIdle;
            }
        }

        private void RefreshProcessList(string? activeGameName = null)
        {
            var list = new List<MonitoredProcessDisplayItem>();
            string currentActive = activeGameName ?? _monitorService.ActiveGameName ?? "";

            foreach (var p in _config.TargetProcesses)
            {
                string cleanTarget = Path.GetFileNameWithoutExtension(p.Trim());
                bool isActive = !string.IsNullOrEmpty(currentActive) &&
                                string.Equals(cleanTarget, currentActive, StringComparison.OrdinalIgnoreCase);

                list.Add(new MonitoredProcessDisplayItem
                {
                    Name = p,
                    StatusText = isActive ? "运行中" : "",
                    StatusColor = isActive ? Accent : Muted,
                    IconColor = isActive ? Accent : Dim
                });
            }

            // Preserve selection across refreshes
            string? selected = (ListProcesses.SelectedItem as MonitoredProcessDisplayItem)?.Name;
            ListProcesses.ItemsSource = list;
            if (selected != null)
                ListProcesses.SelectedItem = list.FirstOrDefault(x => x.Name == selected);
            TxtEmptyList.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            App.TrimWorkingSet();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            App.TrimWorkingSet();
        }

        private async void BtnSwitchProfile1_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(() => HardwareService.SetMouseProfile(1));
            ProfileToastWindow.ShowToast(1, "🖱️ 控制面板手动切换");
            _monitorService.TriggerImmediatePoll();
        }

        private async void BtnSwitchProfile2_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(() => HardwareService.SetMouseProfile(2));
            ProfileToastWindow.ShowToast(2, "🖱️ 控制面板手动切换");
            _monitorService.TriggerImmediatePoll();
        }

        private void ChkAutoSwitch_Changed(object sender, RoutedEventArgs e)
        {
            if (_config == null) return;
            _config.AutoSwitchEnabled = (ChkAutoSwitch.IsChecked == true);
            _config.Save();
            _monitorService.TriggerImmediatePoll();
        }

        private void ChkAutoStart_Changed(object sender, RoutedEventArgs e)
        {
            if (_config == null) return;
            bool enabled = (ChkAutoStart.IsChecked == true);
            _config.AutoStart = enabled;
            _config.Save();
            StartupHelper.SetAutoStart(enabled);
        }

        private void BtnAddProcessFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择要关联板载配置2的游戏或应用程序",
                Filter = "可执行程序 (*.exe)|*.exe|所有文件 (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                string exeName = Path.GetFileName(dlg.FileName);
                AddProcessToConfig(exeName);
            }
        }

        private void BtnAddRunningProcess_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SelectProcessDialog
            {
                Owner = this
            };

            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.SelectedExeName))
            {
                AddProcessToConfig(dlg.SelectedExeName);
            }
        }

        private void AddProcessToConfig(string exeName)
        {
            if (string.IsNullOrWhiteSpace(exeName)) return;

            string normalized = exeName.Trim();
            if (!_config.TargetProcesses.Any(x => x.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            {
                _config.TargetProcesses.Add(normalized);
                _config.Save();
                RefreshProcessList();
                _monitorService.TriggerImmediatePoll();
            }
        }

        private void BtnRemoveProcess_Click(object sender, RoutedEventArgs e)
        {
            if (ListProcesses.SelectedItem is MonitoredProcessDisplayItem item)
            {
                _config.TargetProcesses.RemoveAll(x => x.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
                _config.Save();
                RefreshProcessList();
                _monitorService.TriggerImmediatePoll();
            }
        }
    }
}
