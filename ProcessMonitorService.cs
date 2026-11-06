using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ATKLiteHub
{
    public class ProcessMonitorService
    {
        private readonly AppConfig _config;
        private System.Threading.Timer? _timer;
        private bool _isPolling = false;
        private bool _lastGameRunning = false;
        private string? _activeGameName = null;
        private bool _lowBatteryAlertSent = false;

        public event Action<SystemHardwareState, bool, string?>? OnStateUpdated;
        public event Action<int, string?>? OnProfileAutoSwitched; // profile, triggerProcess
        public event Action<string, string>? OnNotificationRequested; // title, message

        public SystemHardwareState CurrentState { get; private set; } = new SystemHardwareState();
        public bool IsGameRunning => _lastGameRunning;
        public string? ActiveGameName => _activeGameName;

        public ProcessMonitorService(AppConfig config)
        {
            _config = config;
        }

        public void Start()
        {
            _timer?.Dispose();
            int intervalMs = Math.Max(1000, _config.PollIntervalSeconds * 1000);
            _timer = new System.Threading.Timer(async _ => await PollCycleAsync(), null, 200, intervalMs);
        }

        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
        }

        public void TriggerImmediatePoll()
        {
            Task.Run(async () => await PollCycleAsync());
        }

        private async Task PollCycleAsync()
        {
            if (_isPolling) return;
            _isPolling = true;

            try
            {
                // 1. Check Running Target Processes
                bool gameRunning = false;
                string? matchedName = null;

                var runningProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        runningProcessNames.Add(p.ProcessName);
                    }
                    catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(@"C:\Users\Love2self\.gemini\antigravity\scratch\ATKLiteHub\startup.log", $"PollCycleAsync error: {ex}\n");
                }
                catch { }
            }
                }

                foreach (var target in _config.TargetProcesses)
                {
                    if (string.IsNullOrWhiteSpace(target)) continue;
                    string nameOnly = Path.GetFileNameWithoutExtension(target.Trim());
                    if (runningProcessNames.Contains(nameOnly))
                    {
                        gameRunning = true;
                        matchedName = nameOnly;
                        break;
                    }
                }

                // 2. Hardware Query
                var state = HardwareService.QueryAll();
                CurrentState = state;

                // 3. Process Lifetime Auto-Switch Logic
                if (_config.AutoSwitchEnabled && state.Mouse.Connected)
                {
                    int desiredProfile = gameRunning ? 2 : 1;
                    int currentProfile = state.Mouse.CurrentProfile ?? 1;

                    if (currentProfile != desiredProfile)
                    {
                        bool success = HardwareService.SetMouseProfile(desiredProfile);
                        if (success)
                        {
                            state.Mouse.CurrentProfile = desiredProfile;
                            OnProfileAutoSwitched?.Invoke(desiredProfile, gameRunning ? matchedName : null);
                        }
                    }
                }

                _lastGameRunning = gameRunning;
                _activeGameName = matchedName;

                // 4. Low battery alert check
                int? bat = state.LowestBatteryPercent;
                if (bat.HasValue && !state.IsAnyCharging)
                {
                    if (bat.Value <= _config.LowBatteryAlertThreshold && !_lowBatteryAlertSent)
                    {
                        _lowBatteryAlertSent = true;
                        string who = (state.Headset != null && state.Headset.Connected && state.Headset.Percent == bat.Value
                                      && !(state.Mouse.Connected && state.Mouse.Percent == bat.Value))
                            ? "HyperX 耳机" : "ATK 鼠标";
                        OnNotificationRequested?.Invoke("电量过低", $"{who}电量剩余 {bat.Value}%，请及时充电");
                    }
                    else if (bat.Value > _config.LowBatteryAlertThreshold + 5)
                    {
                        _lowBatteryAlertSent = false;
                    }
                }
                else if (state.IsAnyCharging)
                {
                    _lowBatteryAlertSent = false;
                }

                // 5. Fire state update event
                OnStateUpdated?.Invoke(state, gameRunning, matchedName);
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(@"C:\Users\Love2self\.gemini\antigravity\scratch\ATKLiteHub\startup.log", $"PollCycleAsync error: {ex}\n");
                }
                catch { }
            }
            finally
            {
                _isPolling = false;
            }
        }
    }
}
