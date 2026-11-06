using System;
using System.IO;
using Microsoft.Win32;

namespace ATKLiteHub
{
    public static class StartupHelper
    {
        private const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string APP_NAME = "ATKLiteHub";

        public static bool IsAutoStartEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RUN_KEY, false);
                return key?.GetValue(APP_NAME) != null;
            }
            catch
            {
                return false;
            }
        }

        public static void SetAutoStart(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RUN_KEY, true);
                if (key == null) return;

                if (enable)
                {
                    string exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue(APP_NAME, $"\"{exePath}\" --minimized");
                    }
                }
                else
                {
                    key.DeleteValue(APP_NAME, false);
                }
            }
            catch { }
        }
    }
}
