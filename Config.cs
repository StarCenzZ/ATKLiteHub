using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATKLiteHub
{
    public class AppConfig
    {
        public List<string> TargetProcesses { get; set; } = new List<string> { "cs2.exe", "csgo.exe" };
        public bool AutoSwitchEnabled { get; set; } = true;
        public bool AutoStart { get; set; } = false;
        public int PollIntervalSeconds { get; set; } = 2;
        public int LowBatteryAlertThreshold { get; set; } = 15;

        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ATKLiteHub",
            "config.json"
        );

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                    if (cfg != null) return cfg;
                }
            }
            catch { }
            var def = new AppConfig();
            def.Save();
            return def;
        }

        public void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(ConfigPath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }
    }
}
