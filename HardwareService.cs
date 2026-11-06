using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace ATKLiteHub
{
    public class DeviceStatus
    {
        public string Name { get; set; } = "Unknown";
        public bool Connected { get; set; }
        public int? Percent { get; set; }
        public bool Charging { get; set; }
        public bool Full { get; set; }
        public int? Millivolts { get; set; }
        public int? CurrentProfile { get; set; } // 1 or 2
    }

    public class SystemHardwareState
    {
        public DeviceStatus Mouse { get; set; } = new DeviceStatus { Name = "ATK A9 Ultimate" };
        public DeviceStatus? Headset { get; set; }
        public int? LowestBatteryPercent
        {
            get
            {
                int? m = (Mouse.Connected && Mouse.Percent.HasValue) ? Mouse.Percent : null;
                int? h = (Headset != null && Headset.Connected && Headset.Percent.HasValue) ? Headset.Percent : null;
                if (m.HasValue && h.HasValue) return Math.Min(m.Value, h.Value);
                return m ?? h;
            }
        }
        public bool IsAnyCharging => Mouse.Charging || (Headset != null && Headset.Charging);
    }

    public static class HardwareService
    {
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        private const uint DIGCF_PRESENT = 0x00000002;
        private const uint DIGCF_DEVICEINTERFACE = 0x00000010;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public uint cbSize;
            public Guid InterfaceClassGuid;
            public uint Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDD_ATTRIBUTES
        {
            public uint Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct OVERLAPPED
        {
            public IntPtr Internal;
            public IntPtr InternalHigh;
            public uint Offset;
            public uint OffsetHigh;
            public IntPtr hEvent;
        }

        [DllImport("hid.dll", SetLastError = true)]
        private static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetAttributes(IntPtr HidDeviceObject, ref HIDD_ATTRIBUTES Attributes);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetPreparsedData(IntPtr HidDeviceObject, out IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetCaps(IntPtr PreparsedData, out HIDP_CAPS Capabilities);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_FreePreparsedData(IntPtr PreparsedData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevsW(ref Guid ClassGuid, string? Enumerator, IntPtr hwndParent, uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, uint MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, uint DeviceInterfaceDetailDataSize, out uint RequiredSize, IntPtr DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, ref OVERLAPPED lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, ref OVERLAPPED lpOverlapped);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateEventW(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelIo(IntPtr hFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetOverlappedResult(IntPtr hFile, ref OVERLAPPED lpOverlapped, out uint lpNumberOfBytesTransferred, bool bWait);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private class HidDeviceInfo
        {
            public string Path = "";
            public ushort Vid;
            public ushort Pid;
            public ushort UsagePage;
            public ushort Usage;
        }

        private static List<HidDeviceInfo> EnumHidDevices()
        {
            var list = new List<HidDeviceInfo>();
            HidD_GetHidGuid(out Guid hidGuid);
            IntPtr hDevInfo = SetupDiGetClassDevsW(ref hidGuid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (hDevInfo == new IntPtr(-1)) return list;

            try
            {
                uint index = 0;
                while (true)
                {
                    var devInterfaceData = new SP_DEVICE_INTERFACE_DATA
                    {
                        cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>()
                    };
                    if (!SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero, ref hidGuid, index++, ref devInterfaceData))
                        break;

                    SetupDiGetDeviceInterfaceDetailW(hDevInfo, ref devInterfaceData, IntPtr.Zero, 0, out uint reqSize, IntPtr.Zero);
                    if (reqSize > 0)
                    {
                        IntPtr detailBuf = Marshal.AllocHGlobal((int)reqSize);
                        try
                        {
                            Marshal.WriteInt32(detailBuf, IntPtr.Size == 8 ? 8 : 6);
                            if (SetupDiGetDeviceInterfaceDetailW(hDevInfo, ref devInterfaceData, detailBuf, reqSize, out _, IntPtr.Zero))
                            {
                                string devicePath = Marshal.PtrToStringUni(detailBuf + 4)!;
                                IntPtr hFile = CreateFileW(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                                if (hFile != new IntPtr(-1))
                                {
                                    try
                                    {
                                        var attr = new HIDD_ATTRIBUTES { Size = (uint)Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                                        if (HidD_GetAttributes(hFile, ref attr))
                                        {
                                            if (HidD_GetPreparsedData(hFile, out IntPtr preparsed))
                                            {
                                                HidP_GetCaps(preparsed, out HIDP_CAPS caps);
                                                HidD_FreePreparsedData(preparsed);
                                                list.Add(new HidDeviceInfo
                                                {
                                                    Path = devicePath,
                                                    Vid = attr.VendorID,
                                                    Pid = attr.ProductID,
                                                    UsagePage = caps.UsagePage,
                                                    Usage = caps.Usage
                                                });
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        CloseHandle(hFile);
                                    }
                                }
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(detailBuf);
                        }
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(hDevInfo);
            }
            return list;
        }

        private static byte[]? TransactHid(string path, byte[] outBytes, int inLen, uint timeoutMs = 400)
        {
            IntPtr hFile = CreateFileW(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (hFile == new IntPtr(-1)) return null;

            IntPtr hEventW = CreateEventW(IntPtr.Zero, true, false, null);
            IntPtr hEventR = CreateEventW(IntPtr.Zero, true, false, null);
            try
            {
                var ovW = new OVERLAPPED { hEvent = hEventW };
                if (!WriteFile(hFile, outBytes, (uint)outBytes.Length, out _, ref ovW))
                {
                    if (WaitForSingleObject(hEventW, timeoutMs) != 0)
                    {
                        CancelIo(hFile);
                        return null;
                    }
                }

                Thread.Sleep(40);

                var ovR = new OVERLAPPED { hEvent = hEventR };
                byte[] inBuf = new byte[inLen];
                if (!ReadFile(hFile, inBuf, (uint)inLen, out uint readBytes, ref ovR))
                {
                    if (WaitForSingleObject(hEventR, timeoutMs) != 0)
                    {
                        CancelIo(hFile);
                        return null;
                    }
                    GetOverlappedResult(hFile, ref ovR, out readBytes, false);
                }

                return inBuf;
            }
            finally
            {
                CloseHandle(hEventW);
                CloseHandle(hEventR);
                CloseHandle(hFile);
            }
        }

        private static bool SendWriteOnly(string path, byte[] outBytes, uint timeoutMs = 400)
        {
            IntPtr hFile = CreateFileW(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (hFile == new IntPtr(-1)) return false;

            IntPtr hEventW = CreateEventW(IntPtr.Zero, true, false, null);
            try
            {
                var ovW = new OVERLAPPED { hEvent = hEventW };
                if (!WriteFile(hFile, outBytes, (uint)outBytes.Length, out _, ref ovW))
                {
                    if (WaitForSingleObject(hEventW, timeoutMs) != 0)
                    {
                        CancelIo(hFile);
                        return false;
                    }
                }
                return true;
            }
            finally
            {
                CloseHandle(hEventW);
                CloseHandle(hFile);
            }
        }

        private static string? _cachedMousePath = null;
        private static string? _cachedHeadsetPath = null;
        private static DateTime _lastEnumTime = DateTime.MinValue;
        private static readonly object _syncLock = new object();

        private static void RefreshDevicesIfNeeded(bool force = false)
        {
            lock (_syncLock)
            {
                if (!force && _cachedMousePath != null && (DateTime.Now - _lastEnumTime).TotalSeconds < 30)
                {
                    return;
                }

                _lastEnumTime = DateTime.Now;
                _cachedMousePath = null;
                _cachedHeadsetPath = null;

                foreach (var d in EnumHidDevices())
                {
                    if (_cachedMousePath == null && d.Vid == 0x373B && d.UsagePage == 0xFF02 && d.Usage == 0x0002)
                    {
                        _cachedMousePath = d.Path;
                    }
                    else if (_cachedHeadsetPath == null && d.Vid == 0x03F0 && (d.Pid == 0x05B7 || d.Pid == 0x0C9D) && d.UsagePage == 0xFF13 && d.Usage == 0x0001)
                    {
                        _cachedHeadsetPath = d.Path;
                    }
                }
            }
        }

        public static string? GetMousePath()
        {
            lock (_syncLock)
            {
                if (!string.IsNullOrEmpty(_cachedMousePath))
                {
                    IntPtr h = CreateFileW(_cachedMousePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                    if (h != new IntPtr(-1))
                    {
                        CloseHandle(h);
                        return _cachedMousePath;
                    }
                    _cachedMousePath = null;
                }

                RefreshDevicesIfNeeded(force: true);
                return _cachedMousePath;
            }
        }

        public static string? GetHeadsetPath()
        {
            lock (_syncLock)
            {
                if (!string.IsNullOrEmpty(_cachedHeadsetPath))
                {
                    IntPtr h = CreateFileW(_cachedHeadsetPath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                    if (h != new IntPtr(-1))
                    {
                        CloseHandle(h);
                        return _cachedHeadsetPath;
                    }
                    _cachedHeadsetPath = null;
                }

                RefreshDevicesIfNeeded(force: true);
                return _cachedHeadsetPath;
            }
        }

        private static int? _lastKnownMousePercent = null;
        private static int? _lastKnownMouseProfile = null;
        private static int? _lastKnownMouseMv = null;
        private static bool _lastKnownMouseCharging = false;
        private static bool _lastKnownMouseFull = false;

        /// <summary>
        /// Query ATK Mouse Current Onboard Profile (Returns 1 for Profile 1, 2 for Profile 2, null if failed)
        /// </summary>
        public static int? GetMouseCurrentProfile()
        {
            string? path = GetMousePath();
            if (path == null) return null;

            byte[] qPkt = new byte[17];
            qPkt[0] = 8;
            qPkt[1] = 14; // GetCurrentConfig
            qPkt[16] = 63; // 85 - (8 + 14) = 63

            byte[]? rep = TransactHid(path, qPkt, 17);
            if (rep != null && rep.Length >= 7 && rep[0] == 8 && rep[1] == 14)
            {
                int rawIndex = rep[6]; // 0 for Profile 1, 1 for Profile 2
                return rawIndex + 1;
            }
            return null;
        }

        /// <summary>
        /// Set ATK Mouse Onboard Profile (targetProfile: 1 for Profile 1, 2 for Profile 2)
        /// </summary>
        public static bool SetMouseProfile(int targetProfile)
        {
            string? path = GetMousePath();
            if (path == null) return false;

            int rawIndex = (targetProfile <= 1) ? 0 : 1;

            byte[] setPkt = new byte[17];
            setPkt[0] = 8;
            setPkt[1] = 15; // SetCurrentConfig
            setPkt[5] = 1;  // dataValidLen = 1
            setPkt[6] = (byte)rawIndex;

            int sum = 0;
            for (int i = 0; i < 16; i++) sum += setPkt[i];
            setPkt[16] = (byte)(85 - (sum & 0xFF));

            bool ok = SendWriteOnly(path, setPkt);
            if (ok)
            {
                _lastKnownMouseProfile = targetProfile;
                Thread.Sleep(200); // Wait for EEPROM commit
            }
            return ok;
        }

        /// <summary>
        /// Query full hardware state: Mouse battery, mV, charging, current profile, plus optional headset
        /// </summary>
        public static SystemHardwareState QueryAll()
        {
            var state = new SystemHardwareState();

            string? mousePath = GetMousePath();
            if (mousePath != null)
            {
                // Query Battery (Cmd 4)
                byte[] batPkt = new byte[17];
                batPkt[0] = 8;
                batPkt[1] = 4; // GetBatteryLevel
                batPkt[16] = 73;

                byte[]? batRep = TransactHid(mousePath, batPkt, 17);
                if (batRep != null && batRep.Length >= 10 && batRep[0] == 8 && batRep[1] == 4)
                {
                    int pct = batRep[6];
                    bool wired = batRep[7] != 0;
                    int mv = (batRep[8] << 8) | batRep[9];

                    if (pct > 0 || mv > 0)
                    {
                        _lastKnownMousePercent = pct;
                        _lastKnownMouseCharging = wired && pct < 100;
                        _lastKnownMouseFull = wired && pct >= 100;
                        _lastKnownMouseMv = mv;
                    }

                    state.Mouse.Connected = true;
                    state.Mouse.Percent = _lastKnownMousePercent ?? pct;
                    state.Mouse.Charging = _lastKnownMouseCharging;
                    state.Mouse.Full = _lastKnownMouseFull;
                    state.Mouse.Millivolts = _lastKnownMouseMv ?? mv;
                }
                else
                {
                    // Receiver is still plugged in, but mouse RF is in sleep mode (~15s idle).
                    // Keep the last known battery level so tray and UI do not vanish to '--'.
                    if (_lastKnownMousePercent.HasValue)
                    {
                        state.Mouse.Connected = true;
                        state.Mouse.Percent = _lastKnownMousePercent;
                        state.Mouse.Charging = _lastKnownMouseCharging;
                        state.Mouse.Full = _lastKnownMouseFull;
                        state.Mouse.Millivolts = _lastKnownMouseMv;
                    }
                    else
                    {
                        state.Mouse.Connected = false;
                    }
                }

                Thread.Sleep(30);

                // Query Profile (Cmd 14)
                byte[] profPkt = new byte[17];
                profPkt[0] = 8;
                profPkt[1] = 14;
                profPkt[16] = 63;

                byte[]? profRep = TransactHid(mousePath, profPkt, 17);
                if (profRep != null && profRep.Length >= 7 && profRep[0] == 8 && profRep[1] == 14)
                {
                    int prof = profRep[6] + 1;
                    _lastKnownMouseProfile = prof;
                    state.Mouse.CurrentProfile = prof;
                }
                else if (_lastKnownMouseProfile.HasValue)
                {
                    state.Mouse.CurrentProfile = _lastKnownMouseProfile;
                }
            }
            else
            {
                // Receiver physically disconnected
                _lastKnownMousePercent = null;
                _lastKnownMouseProfile = null;
                state.Mouse.Connected = false;
                state.Mouse.Percent = null;
            }

            // Also check HyperX headset if present
            string? headsetPath = GetHeadsetPath();
            if (headsetPath != null)
            {
                byte[] pktChg = new byte[62];
                pktChg[0] = 102;
                pktChg[1] = 138;
                byte[]? repChg = TransactHid(headsetPath, pktChg, 62);

                byte[] pktBat = new byte[62];
                pktBat[0] = 102;
                pktBat[1] = 137;
                byte[]? repBat = TransactHid(headsetPath, pktBat, 62);

                if (repBat != null && repBat.Length >= 5 && repBat[0] == 102)
                {
                    int s1 = repBat[2];
                    int s2 = repBat[3];
                    int level = repBat[4];
                    int chgCode = (repChg != null && repChg.Length >= 3) ? repChg[2] : 0;
                    int mv = (s1 << 8) | s2;
                    bool connected = (s1 != 0 || s2 != 0);
                    state.Headset = new DeviceStatus
                    {
                        Name = "HyperX Cloud III Wireless",
                        Connected = connected,
                        Percent = connected ? level : null,
                        Charging = chgCode == 1,
                        Full = chgCode == 2,
                        Millivolts = connected ? mv : null
                    };
                }
            }

            return state;
        }
    }
}
