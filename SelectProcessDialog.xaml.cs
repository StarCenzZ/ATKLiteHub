using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ATKLiteHub
{
    public class ProcessItem
    {
        public string ProcessName { get; set; } = "";
        public string ExeName { get; set; } = "";
        public string Title { get; set; } = "";
        public bool HasWindow { get; set; }
    }

    public partial class SelectProcessDialog : Window
    {
        private List<ProcessItem> _allItems = new();
        public string? SelectedExeName { get; private set; }

        public SelectProcessDialog()
        {
            InitializeComponent();
            LoadProcesses();
        }

        private void LoadProcesses()
        {
            var ignoredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "System", "Idle", "Registry", "smss", "csrss", "wininit", "services",
                "lsass", "svchost", "fontdrvhost", "dwm", "sihost", "taskhostw",
                "ctfmon", "SearchHost", "StartMenuExperienceHost", "ShellExperienceHost",
                "RuntimeBroker", "explorer", "conhost", "dllhost"
            };

            var items = new Dictionary<string, ProcessItem>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    string name = p.ProcessName;
                    if (ignoredNames.Contains(name)) continue;

                    string title = "";
                    bool hasWin = false;
                    try
                    {
                        title = p.MainWindowTitle;
                        hasWin = !string.IsNullOrWhiteSpace(title);
                    }
                    catch { }

                    string exe = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : (name + ".exe");

                    if (!items.ContainsKey(name))
                    {
                        items[name] = new ProcessItem
                        {
                            ProcessName = name,
                            ExeName = exe,
                            Title = hasWin ? title : "后台进程",
                            HasWindow = hasWin
                        };
                    }
                    else if (hasWin && !items[name].HasWindow)
                    {
                        items[name].Title = title;
                        items[name].HasWindow = true;
                    }
                }
                catch { }
            }

            _allItems = items.Values
                .OrderByDescending(x => x.HasWindow)
                .ThenBy(x => x.ProcessName)
                .ToList();

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string q = TxtSearch.Text?.Trim() ?? "";
            var filtered = string.IsNullOrEmpty(q)
                ? _allItems
                : _allItems.Where(x => x.ProcessName.Contains(q, StringComparison.OrdinalIgnoreCase)
                                    || x.Title.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

            ListProcesses.ItemsSource = filtered;
            TxtCount.Text = $"{filtered.Count} 个程序";
            if (filtered.Count > 0)
            {
                ListProcesses.SelectedIndex = 0;
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnSelect_Click(object sender, RoutedEventArgs e)
        {
            ConfirmSelection();
        }

        private void ListProcesses_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ConfirmSelection();
        }

        private void ConfirmSelection()
        {
            if (ListProcesses.SelectedItem is ProcessItem item)
            {
                SelectedExeName = item.ExeName;
                DialogResult = true;
                Close();
            }
        }
    }
}
