using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;

namespace ATKLiteHub
{
    public partial class ProfileToastWindow : Window
    {
        private static ProfileToastWindow? _activeToast = null;
        private DispatcherTimer? _closeTimer;

        public ProfileToastWindow()
        {
            InitializeComponent();
        }

        public static void ShowToast(int profile, string? subtitle = null)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                try
                {
                    if (_activeToast != null)
                    {
                        try
                        {
                            _activeToast._closeTimer?.Stop();
                            _activeToast.Close();
                        }
                        catch { }
                        _activeToast = null;
                    }

                    var toast = new ProfileToastWindow();
                    _activeToast = toast;

                    bool isGaming = (profile == 2);
                    Color accentColor = isGaming
                        ? Color.FromRgb(0xFF, 0x4E, 0x27)  // Gaming flame orange
                        : Color.FromRgb(0x3B, 0x82, 0xF6); // Daily sleek blue

                    toast.CardBorderBrush.Color = accentColor;
                    toast.BadgeBorder.Background = new SolidColorBrush(Color.FromArgb(38, accentColor.R, accentColor.G, accentColor.B));
                    toast.TxtBadge.Text = isGaming ? "P2" : "P1";
                    toast.TxtBadge.Foreground = new SolidColorBrush(accentColor);

                    toast.TxtTitle.Text = isGaming
                        ? "已切换至 板载配置 2 (游戏电竞)"
                        : "已切换至 板载配置 1 (日常模式)";

                    toast.TxtSubtitle.Text = subtitle ?? (isGaming ? "电竞参数已就绪" : "日常节能模式");

                    // Position at bottom-right corner
                    var workArea = SystemParameters.WorkArea;
                    toast.Left = workArea.Right - toast.Width - 18;
                    toast.Top = workArea.Bottom - toast.Height - 18;

                    toast.Show();

                    var fadeIn = toast.Resources["FadeInStory"] as Storyboard;
                    fadeIn?.Begin(toast);

                    toast._closeTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(2.8)
                    };
                    toast._closeTimer.Tick += (s, e) =>
                    {
                        toast._closeTimer.Stop();
                        toast.Dismiss();
                    };
                    toast._closeTimer.Start();
                }
                catch { }
            });
        }

        private void Dismiss()
        {
            try
            {
                _closeTimer?.Stop();
                var fadeOut = Resources["FadeOutStory"] as Storyboard;
                if (fadeOut != null)
                {
                    fadeOut = fadeOut.Clone();
                    fadeOut.Completed += (s, e) =>
                    {
                        try { Close(); } catch { }
                        if (_activeToast == this) _activeToast = null;
                    };
                    fadeOut.Begin(this);
                }
                else
                {
                    Close();
                    if (_activeToast == this) _activeToast = null;
                }
            }
            catch
            {
                try { Close(); } catch { }
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Dismiss();
        }
    }
}
