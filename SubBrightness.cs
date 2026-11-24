// SubBrightness - 副屏亮度快捷调节
// 通过 DDC/CI 直接控制外接/便携显示器的硬件背光。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SubBrightness
{
    #region Win32

    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PHYSICAL_MONITOR
        {
            public IntPtr hPhysicalMonitor;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szPhysicalMonitorDescription;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);
        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, ref uint count);
        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] arr);
        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool DestroyPhysicalMonitors(uint count, [In] PHYSICAL_MONITOR[] arr);
        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool GetMonitorBrightness(IntPtr h, ref uint min, ref uint cur, ref uint max);
        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool SetMonitorBrightness(IntPtr h, uint val);
        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool GetMonitorContrast(IntPtr h, ref uint min, ref uint cur, ref uint max);
        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool SetMonitorContrast(IntPtr h, uint val);
        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool GetCapabilitiesStringLength(IntPtr h, ref uint len);
        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr h, StringBuilder buf, uint len);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // ---- 软件调光：给不支持 DDC/CI 的屏兜底，改的是显卡输出的 gamma 曲线，不是背光 ----
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateDC(string driver, string device, string port, IntPtr dm);
        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")]
        public static extern bool SetDeviceGammaRamp(IntPtr hdc, ushort[] ramp);
        [DllImport("gdi32.dll")]
        public static extern bool GetDeviceGammaRamp(IntPtr hdc, ushort[] ramp);

        // ---- 接口类型查询：用来区分「笔记本内屏」和「外接但没有 DDC 的屏」----
        [StructLayout(LayoutKind.Sequential)]
        public struct LUID { public uint Low; public int High; }

        [StructLayout(LayoutKind.Sequential)]
        public struct PATH_SOURCE { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

        // 注意 refreshRate 在 C 里是两个 UINT32，写成 ulong 会强制 8 字节对齐把后面字段全顶偏
        [StructLayout(LayoutKind.Sequential)]
        public struct PATH_TARGET
        {
            public LUID adapterId; public uint id; public uint modeInfoIdx;
            public uint outputTechnology; public uint rotation; public uint scaling;
            public uint refreshNum; public uint refreshDen; public uint scanLineOrdering;
            public int targetAvailable; public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PATH_INFO { public PATH_SOURCE sourceInfo; public PATH_TARGET targetInfo; public uint flags; }

        [StructLayout(LayoutKind.Sequential, Size = 64)]
        public struct MODE_INFO { public uint dummy; }

        [StructLayout(LayoutKind.Sequential)]
        public struct DEVICE_INFO_HEADER { public uint type; public uint size; public LUID adapterId; public uint id; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SOURCE_NAME
        {
            public DEVICE_INFO_HEADER header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct TARGET_NAME
        {
            public DEVICE_INFO_HEADER header;
            public uint flags;
            public uint outputTechnology;
            public ushort edidManufactureId;
            public ushort edidProductCodeId;
            public uint connectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
        }

        [DllImport("user32.dll")]
        public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPath, out uint numMode);
        [DllImport("user32.dll")]
        public static extern int QueryDisplayConfig(uint flags, ref uint numPath, [Out] PATH_INFO[] paths,
            ref uint numMode, [Out] MODE_INFO[] modes, IntPtr current);
        [DllImport("user32.dll")]
        public static extern int DisplayConfigGetDeviceInfo(ref SOURCE_NAME req);
        [DllImport("user32.dll")]
        public static extern int DisplayConfigGetDeviceInfo(ref TARGET_NAME req);

        [StructLayout(LayoutKind.Sequential)]
        public struct ADVANCED_COLOR_INFO
        {
            public DEVICE_INFO_HEADER header;
            public uint value;              // bit0 = 支持 HDR，bit1 = HDR 已开启
            public uint colorEncoding;
            public uint bitsPerColorChannel;
        }
        [DllImport("user32.dll")]
        public static extern int DisplayConfigGetDeviceInfo(ref ADVANCED_COLOR_INFO req);
        public const uint DEVINFO_GET_ADVANCED_COLOR_INFO = 9;

        public const uint QDC_ONLY_ACTIVE_PATHS = 2;
        public const uint DEVINFO_GET_SOURCE_NAME = 1, DEVINFO_GET_TARGET_NAME = 2;

        // DISPLAYCONFIG_OUTPUT_TECHNOLOGY：这几个算「内嵌屏」
        public const uint TECH_LVDS = 6, TECH_DP_EMBEDDED = 11, TECH_UDI_EMBEDDED = 13, TECH_INTERNAL = 0x80000000;

        public static bool IsInternalPanel(uint tech)
        {
            return tech == TECH_LVDS || tech == TECH_DP_EMBEDDED
                || tech == TECH_UDI_EMBEDDED || tech == TECH_INTERNAL;
        }

        // ---- 屏幕旋转 ----
        [StructLayout(LayoutKind.Sequential)]
        public struct POINTL { public int x; public int y; }

        // DEVMODE 里那段联合体有两种解释：打印机用 dmOrientation/dmPaperSize... 一串 short，
        // 显示器用 dmPosition + dmDisplayOrientation + dmDisplayFixedOutput。两者都是 16 字节，
        // 这里只跟显示器打交道，所以直接按显示器那套铺开。字段顺序错一位整个结构就废了。
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public ushort dmSpecVersion;
            public ushort dmDriverVersion;
            public ushort dmSize;
            public ushort dmDriverExtra;
            public uint dmFields;

            public POINTL dmPosition;
            public uint dmDisplayOrientation;
            public uint dmDisplayFixedOutput;

            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public ushort dmLogPixels;
            public uint dmBitsPerPel;
            public uint dmPelsWidth;
            public uint dmPelsHeight;
            public uint dmDisplayFlags;
            public uint dmDisplayFrequency;
            public uint dmICMMethod;
            public uint dmICMIntent;
            public uint dmMediaType;
            public uint dmDitherType;
            public uint dmReserved1;
            public uint dmReserved2;
            public uint dmPanningWidth;
            public uint dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE dm);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int ChangeDisplaySettingsEx(string deviceName, ref DEVMODE dm,
            IntPtr hwnd, uint flags, IntPtr param);

        public const int ENUM_CURRENT_SETTINGS = -1;

        public const uint DM_POSITION = 0x00000020;
        public const uint DM_DISPLAYORIENTATION = 0x00000080;
        public const uint DM_PELSWIDTH = 0x00080000;
        public const uint DM_PELSHEIGHT = 0x00100000;

        public const uint CDS_UPDATEREGISTRY = 0x00000001;
        public const uint CDS_TEST = 0x00000002;

        public const uint DMDO_DEFAULT = 0, DMDO_90 = 1, DMDO_180 = 2, DMDO_270 = 3;

        public const int DISP_CHANGE_SUCCESSFUL = 0;

        public static string DispChangeName(int code)
        {
            switch (code)
            {
                case 0: return Ui.L("成功", "OK");
                case 1: return Ui.L("需要重启才能生效", "restart required");
                case -1: return Ui.L("驱动拒绝了这个模式", "driver rejected this mode");
                case -2: return Ui.L("显示器不支持这个模式", "mode not supported by the display");
                case -3: return Ui.L("无法写入注册表", "could not write registry");
                case -4: return Ui.L("参数标志有误", "bad flags");
                case -5: return Ui.L("参数有误", "bad parameter");
                case -6: return Ui.L("双屏模式不支持", "not supported in dual-view mode");
                default: return Ui.L("未知错误 ", "unknown error ") + code;
            }
        }

        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4;
        public const uint VK_PRIOR = 0x21, VK_NEXT = 0x22, VK_B = 0x42, VK_R = 0x52;
        public const uint VK_O = 0x4F, VK_END = 0x23;
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    }

    /// <summary>
    /// 本进程是「系统 DPI 感知」的，坐标单位 = 物理像素。
    /// 字体按磅走会自己跟着 DPI 放大，但硬编码的像素尺寸不会，
    /// 所以所有布局常量都必须过一遍 S()，否则在 175% 的屏上只有一半大。
    /// </summary>
    internal static class Ui
    {
        public static float Scale = 1f;

        public static void Init()
        {
            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                    Scale = g.DpiX / 96f;
            }
            catch { Scale = 1f; }
            if (Scale < 1f) Scale = 1f;
        }

        public static int S(int v) { return (int)Math.Round(v * Scale); }

        /// <summary>系统界面语言是中文就用中文，否则英文。</summary>
        public static readonly bool Zh =
            System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";
        public static string L(string zh, string en) { return Zh ? zh : en; }
        public static float F(float v) { return v * Scale; }
        public static Padding P(int l, int t, int r, int b) { return new Padding(S(l), S(t), S(r), S(b)); }
    }

    #endregion

    #region 显示器模型

    /// <summary>
    /// 调光方式。Ddc = 改硬件背光；Gamma = 改显卡输出曲线（画面变暗，背光没动）；
    /// DdcContrast = 显示器丢弃亮度指令但认对比度指令，借对比度压暗，压到底再叠软件曲线。
    /// </summary>
    internal enum DimMode { Ddc, Gamma, DdcContrast }

    internal class Screenlet
    {
        public IntPtr Physical;
        public string Name;
        public string Key;               // 配置文件里的稳定标识
        public Rectangle Bounds;
        public bool Primary;
        public uint BMin, BMax;
        public bool HasContrast;
        public uint CMin, CMax;

        public DimMode Mode = DimMode.Ddc;
        public string Device;            // \\.\DISPLAY5，软件调光和旋转都要用它
        public int Orientation;          // 0/1/2/3 = 0°/90°/180°/270°，来自 DEVMODE
        // 面板上显示的分辨率要用这个。GetMonitorInfo 在「系统 DPI 感知」进程里返回的是
        // 逻辑分辨率×系统DPI 的产物（会算出 1400×2240 这种面板上根本不存在的数），
        // DEVMODE 里的 dmPels* 才是真实像素。
        public int PelsW, PelsH;

        /// <summary>
        /// 软件调光的下限。Windows 不接受偏离单位曲线太远的 gamma ramp，
        /// 实测系数低于 0.5 就会被直接拒绝（SetDeviceGammaRamp 返回 false，曲线原地不动），
        /// 所以滑块的 0% 映射到 0.5，100% 映射到 1.0。
        /// </summary>
        public const double GammaFloor = 0.5;

        public int Brightness;           // UI 侧的乐观值 (0-100 百分比)
        public int Contrast;

        internal int PendingB = -1;      // 由 worker 消费
        internal int PendingC = -1;
        internal int AppliedB = -1;
        internal int AppliedC = -1;

        /// <summary>0 = 还没验过，1 = 写入确实生效，-1 = 显示器收下指令却不执行。</summary>
        internal int WriteVerified;
        internal int CWriteVerified;

        /// <summary>对比度是否走软件（改 gamma 曲线斜率）而不是显示器固件。</summary>
        public bool ContrastSoft;

        /// <summary>这块屏开着 HDR：软件曲线对它无效。</summary>
        public bool Hdr;

        /// <summary>显示器的 DDC 对比度读得到（HasContrast 可能来自软件，不代表这个）。</summary>
        public bool DdcContrast;

        /// <summary>DdcContrast 模式：对比度原始值的「正常亮度」点和压暗下限。</summary>
        public int HwBase, HwFloor;
        internal int AppliedHw = -1;

        public int ToPercent(uint raw, uint min, uint max)
        {
            if (max <= min) return (int)raw;
            return (int)Math.Round((raw - min) * 100.0 / (max - min));
        }
        public uint FromPercent(int pct, uint min, uint max)
        {
            if (max <= min) return (uint)pct;
            return (uint)Math.Round(min + pct / 100.0 * (max - min));
        }
    }

    /// <summary>
    /// 枚举显示器 + 串行执行 DDC 写入。
    /// DDC/CI 走 I2C，一次读写要几十到几百毫秒，绝不能放在 UI 线程上，
    /// 也不能按住热键就狂发 —— 所以这里用「只保留最新目标值」的合并策略。
    /// </summary>
    internal class MonitorManager : IDisposable
    {
        private readonly object _gate = new object();
        /// <summary>本程序用软件曲线接管了这块屏的哪些项。-1 表示该项仍走显示器固件。</summary>
        internal struct SoftState { public int B; public int C; }

        // 键是 GDI 设备名（\\.\DISPLAY5），不是 Screenlet.Key。
        // 型号名会变（同一块屏 EDID 叫 3208W、DDC 能力串里叫 Q2778VF），设备名才稳。
        private readonly Dictionary<string, SoftState> _gammaWanted = new Dictionary<string, SoftState>(StringComparer.OrdinalIgnoreCase);
        private volatile bool _gammaDirty;
        private readonly Config _cfg;
        private List<Screenlet> _list = new List<Screenlet>();
        private Native.PHYSICAL_MONITOR[] _raw = new Native.PHYSICAL_MONITOR[0];
        private readonly AutoResetEvent _signal = new AutoResetEvent(false);
        private readonly Thread _worker;
        private volatile bool _stop;

        public event Action Changed;     // 在 worker 线程上触发

        public MonitorManager(Config cfg)
        {
            _cfg = cfg;
            LoadGammaState();
            _worker = new Thread(WorkerLoop);
            _worker.IsBackground = true;
            _worker.Name = "ddc-worker";
            _worker.Start();
        }

        public List<Screenlet> Snapshot()
        {
            lock (_gate) return new List<Screenlet>(_list);
        }

        /// <summary>只返回支持 DDC 调光的屏幕（笔记本内屏通常不在其中）。</summary>
        public List<Screenlet> Controllable()
        {
            return Snapshot();
        }

        public void Refresh()
        {
            var found = new List<Screenlet>();
            var raws = new List<Native.PHYSICAL_MONITOR>();
            var hmons = new List<IntPtr>();
            var conn = BuildConnectorMap();
            // 两台同型号显示器的型号名一样，键会撞，各自记住的亮度会互相覆盖。撞了就带上设备名。
            var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Native.MonitorEnumProc cb = delegate (IntPtr h, IntPtr hdc, ref Native.RECT r, IntPtr d)
            {
                hmons.Add(h);
                return true;
            };
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);

            foreach (IntPtr hMon in hmons)
            {
                var mi = new Native.MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFOEX));
                if (!Native.GetMonitorInfo(hMon, ref mi)) continue;

                var bounds = Rectangle.FromLTRB(mi.rcMonitor.left, mi.rcMonitor.top,
                                                mi.rcMonitor.right, mi.rcMonitor.bottom);
                bool primary = (mi.dwFlags & 1) != 0;
                bool gotDdc = false;

                uint n = 0;
                if (Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, ref n) && n > 0)
                {
                    var pm = new Native.PHYSICAL_MONITOR[n];
                    if (Native.GetPhysicalMonitorsFromHMONITOR(hMon, n, pm))
                    {
                        for (int i = 0; i < n; i++)
                        {
                            raws.Add(pm[i]);

                            uint bmin = 0, bcur = 0, bmax = 0;
                            if (!Native.GetMonitorBrightness(pm[i].hPhysicalMonitor, ref bmin, ref bcur, ref bmax))
                                continue;   // 这块屏不认 DDC，下面走软件调光

                            var s = new Screenlet();
                            s.Mode = DimMode.Ddc;
                            s.Physical = pm[i].hPhysicalMonitor;
                            s.Device = mi.szDevice;
                            s.Bounds = bounds;
                            s.Primary = primary;
                            s.BMin = bmin; s.BMax = bmax;
                            s.Brightness = s.ToPercent(bcur, bmin, bmax);
                            s.AppliedB = s.Brightness;

                            uint cmin = 0, ccur = 0, cmax = 0;
                            if (Native.GetMonitorContrast(pm[i].hPhysicalMonitor, ref cmin, ref ccur, ref cmax))
                            {
                                s.HasContrast = true;
                                s.DdcContrast = true;
                                s.CMin = cmin; s.CMax = cmax;
                                s.Contrast = s.ToPercent(ccur, cmin, cmax);
                                s.AppliedC = s.Contrast;
                            }

                            string model = ReadModel(pm[i].hPhysicalMonitor);
                            s.Name = !string.IsNullOrEmpty(model) ? model
                                   : (pm[i].szPhysicalMonitorDescription ?? Ui.L("显示器", "Display"));
                            if (s.Primary) s.Name += Ui.L("（主屏）", " (primary)");
                            // 键里不能带分辨率：一旋转屏幕宽高就互换，键跟着变，
                            // 每屏记住的亮度和「热键钉死某屏」的设置就全对不上了。
                            s.Key = model ?? mi.szDevice;
                            if (!usedKeys.Add(s.Key)) { s.Key += "@" + mi.szDevice; usedKeys.Add(s.Key); }
                            ConnectorInfo dci;
                            s.Hdr = conn.TryGetValue(mi.szDevice, out dci) && dci.Hdr;
                            s.Orientation = ReadMode(mi.szDevice, out s.PelsW, out s.PelsH);

                            Classify(s, bcur, ccur);
                            RestoreSoftState(s);
                            found.Add(s);
                            gotDdc = true;
                        }
                    }
                }

                if (gotDdc) continue;

                // 没有 DDC 通道。内屏跳过（它有真正的背光控制，走 Fn 键/ Windows 滑条），
                // 外接屏则退化成软件调光，至少让同一个工具能管住它。
                uint tech = 0xFFFFFFFF;
                string friendly = null;
                ConnectorInfo ci;
                if (conn.TryGetValue(mi.szDevice, out ci)) { tech = ci.Tech; friendly = ci.Name; }
                if (Native.IsInternalPanel(tech)) continue;

                var gs = new Screenlet();
                gs.Mode = DimMode.Gamma;
                gs.Device = mi.szDevice;
                gs.Bounds = bounds;
                gs.Primary = primary;
                gs.BMin = 0; gs.BMax = 100;
                gs.Name = !string.IsNullOrEmpty(friendly) ? friendly : Ui.L("外接显示器", "External display");
                if (gs.Primary) gs.Name += Ui.L("（主屏）", " (primary)");
                gs.Key = friendly ?? mi.szDevice;
                if (!usedKeys.Add(gs.Key)) { gs.Key += "@" + mi.szDevice; usedKeys.Add(gs.Key); }
                gs.Hdr = ci.Hdr;
                gs.Orientation = ReadMode(mi.szDevice, out gs.PelsW, out gs.PelsH);
                gs.Brightness = ReadGammaPercent(mi.szDevice);

                // 曲线已经归本程序管了，对比度也一并用曲线斜率来做，不然这块屏就没有对比度可调
                gs.ContrastSoft = true;
                gs.HasContrast = true;
                gs.Contrast = 50;          // 50 = 斜率 1.0，中性
                gs.AppliedC = gs.Contrast;

                RestoreSoftState(gs);
                found.Add(gs);
            }

            Native.PHYSICAL_MONITOR[] oldRaw;
            lock (_gate)
            {
                oldRaw = _raw;
                _raw = raws.ToArray();
                _list = found;
            }
            if (oldRaw.Length > 0)
                Native.DestroyPhysicalMonitors((uint)oldRaw.Length, oldRaw);

            CleanupStaleGamma(found);
        }

        /// <summary>
        /// 还原那些「本程序压暗过、但现在已经不归软件调光管」的屏幕。
        /// 典型场景：用户在显示器 OSD 里把 DDC/CI 打开了，这块屏转到硬件背光路径，
        /// 之前那条 gamma 曲线就没人认领了 —— 面板显示 100%，屏幕却实际暗着。
        /// 只动本程序记过账的设备，绝不碰别人（f.lux 之类）设的曲线。
        /// </summary>
        private void CleanupStaleGamma(List<Screenlet> found)
        {
            var stillGamma = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in found)
                if ((s.Mode != DimMode.Ddc || s.ContrastSoft) && !string.IsNullOrEmpty(s.Device))
                    stillGamma.Add(s.Device);

            var stale = new List<string>();
            lock (_gate)
            {
                foreach (var k in _gammaWanted.Keys)
                    if (!stillGamma.Contains(k)) stale.Add(k);
                foreach (var k in stale) _gammaWanted.Remove(k);
            }

            if (stale.Count == 0) return;
            foreach (string dev in stale) ResetRamp(dev);
            SaveGammaState();
        }

        // 格式：设备名:亮度:对比度，多台用 | 分隔，-1 表示该项走硬件。
        // 设备名里有 \ 和 . 但没有 : 和 |，所以直接切就行。
        private void LoadGammaState()
        {
            string s = _cfg.GetStr("GammaState", "");
            if (string.IsNullOrEmpty(s)) return;
            foreach (string part in s.Split('|'))
            {
                if (part.Length == 0) continue;
                string[] f = part.Split(':');
                if (f.Length < 2) continue;

                int b, c = -1;
                if (!int.TryParse(f[f.Length - (f.Length >= 3 ? 2 : 1)], out b)) continue;
                if (f.Length >= 3 && !int.TryParse(f[f.Length - 1], out c)) c = -1;

                string dev = string.Join(":", f, 0, f.Length - (f.Length >= 3 ? 2 : 1));
                if (dev.Length == 0) continue;

                var st = new SoftState();
                st.B = (b < 0) ? -1 : Clamp(b);
                st.C = (c < 0) ? -1 : Clamp(c);
                _gammaWanted[dev] = st;
            }
        }

        private void SaveGammaState()
        {
            var sb = new StringBuilder();
            lock (_gate)
            {
                foreach (var p in _gammaWanted)
                {
                    if (sb.Length > 0) sb.Append('|');
                    sb.Append(p.Key).Append(':').Append(p.Value.B).Append(':').Append(p.Value.C);
                }
            }
            _cfg.Set("GammaState", sb.ToString());
            _cfg.Save();
        }

        /// <summary>读当前显示模式，返回旋转方向，顺便带出真实像素分辨率。</summary>
        public static int ReadMode(string device, out int pelsW, out int pelsH)
        {
            pelsW = 0; pelsH = 0;
            var dm = new Native.DEVMODE();
            dm.dmSize = (ushort)Marshal.SizeOf(typeof(Native.DEVMODE));
            if (!Native.EnumDisplaySettings(device, Native.ENUM_CURRENT_SETTINGS, ref dm)) return 0;
            pelsW = (int)dm.dmPelsWidth;
            pelsH = (int)dm.dmPelsHeight;
            return (int)(dm.dmDisplayOrientation & 3);
        }

        public static string OrientationName(int o)
        {
            switch (o & 3)
            {
                case 1: return "90°";
                case 2: return "180°";
                case 3: return "270°";
                default: return "0°";
            }
        }

        /// <summary>
        /// 旋转一块屏。返回 null 表示成功，否则是给用户看的失败原因。
        /// 关键点：横竖切换时必须把 dmPelsWidth / dmPelsHeight 也对调，
        /// 只改 dmDisplayOrientation 的话驱动会以「显示器不支持这个模式」拒掉。
        /// </summary>
        public static string SetRotation(string device, int orientation)
        {
            orientation &= 3;

            var dm = new Native.DEVMODE();
            dm.dmSize = (ushort)Marshal.SizeOf(typeof(Native.DEVMODE));
            if (!Native.EnumDisplaySettings(device, Native.ENUM_CURRENT_SETTINGS, ref dm))
                return Ui.L("读不到 ", "Cannot read ") + device + Ui.L(" 的当前显示模式", " current display mode");

            int cur = (int)(dm.dmDisplayOrientation & 3);
            if (cur == orientation) return null;

            // 0°/180° 是横，90°/270° 是竖。奇偶变了就说明横竖换了，宽高要跟着换。
            if (((cur ^ orientation) & 1) != 0)
            {
                uint t = dm.dmPelsWidth;
                dm.dmPelsWidth = dm.dmPelsHeight;
                dm.dmPelsHeight = t;
            }

            dm.dmDisplayOrientation = (uint)orientation;
            dm.dmFields = Native.DM_DISPLAYORIENTATION | Native.DM_PELSWIDTH | Native.DM_PELSHEIGHT;

            // 先干跑一次，别把桌面搞成不可用状态再来后悔
            int test = Native.ChangeDisplaySettingsEx(device, ref dm, IntPtr.Zero, Native.CDS_TEST, IntPtr.Zero);
            if (test != Native.DISP_CHANGE_SUCCESSFUL)
                return Ui.L("驱动不接受这个方向：", "Driver refused this orientation: ") + Native.DispChangeName(test);

            int r = Native.ChangeDisplaySettingsEx(device, ref dm, IntPtr.Zero, Native.CDS_UPDATEREGISTRY, IntPtr.Zero);
            if (r != Native.DISP_CHANGE_SUCCESSFUL)
                return Ui.L("旋转失败：", "Rotation failed: ") + Native.DispChangeName(r);

            return null;
        }

        /// <summary>
        /// 锁屏、切分辨率、独占全屏退出之后系统会把 gamma 打回单位曲线，
        /// 这时候读回来的是「没被动过」，不是用户要的值。本程序记过账的以记的为准，重新下发。
        /// </summary>
        private void RestoreSoftState(Screenlet s)
        {
            SoftState st;
            bool have;
            lock (_gate) have = _gammaWanted.TryGetValue(s.Device, out st);

            // 走哪条通路由 Classify 当场探测决定，这里只恢复用户设过的数值
            if (have && s.Mode != DimMode.Ddc && st.B >= 0) s.Brightness = st.B;
            if (have && s.ContrastSoft && st.C >= 0) s.Contrast = st.C;

            if (s.Mode == DimMode.Ddc && !s.ContrastSoft)
            {
                s.AppliedB = s.Brightness;
                s.AppliedC = s.Contrast;
                return;
            }

            RememberSoft(s);
            // 让 worker 重新下发一次（曲线 / 借来的对比度）
            s.AppliedB = -1;
            s.AppliedC = -1;
            Interlocked.Exchange(ref s.PendingB, s.Brightness);
            Interlocked.Exchange(ref s.PendingC, s.Contrast);
        }

        // ---- 硬件通路探测 ----
        // 每次启动对每块屏各探一次（结果只记在内存里），不把「这块屏不行」永久写进配置：
        // 换线、转屏、改 OSD 之后显示器可能又认了，下次启动就自动回到硬件通路。
        private readonly Dictionary<string, int> _probe = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public void ForgetProbes() { lock (_gate) _probe.Clear(); }

        /// <summary>写一个相邻值再读回：变了 = 显示器真执行了。随即写回原值，肉眼看不出。</summary>
        private static bool ProbeWrite(IntPtr h, bool contrast, uint cur, uint min, uint max)
        {
            if (max <= min) return true;
            uint target = cur < max ? cur + 1 : cur - 1;
            bool ok = contrast ? Native.SetMonitorContrast(h, target) : Native.SetMonitorBrightness(h, target);
            if (!ok) return false;
            Thread.Sleep(250);   // 给固件一点时间，读太快会拿到旧值
            uint mn = 0, rd = 0, mx = 0;
            bool rok = contrast ? Native.GetMonitorContrast(h, ref mn, ref rd, ref mx)
                                : Native.GetMonitorBrightness(h, ref mn, ref rd, ref mx);
            if (contrast) Native.SetMonitorContrast(h, cur); else Native.SetMonitorBrightness(h, cur);
            if (!rok) return true;   // 读不回来就不瞎猜，当它生效
            return rd != cur;
        }

        /// <summary>
        /// 定这块屏的亮度、对比度各走哪条路：
        /// 亮度认 → 硬件背光（对比度认就走硬件，不认走曲线）；
        /// 亮度不认、对比度认 → 借对比度压暗（实测 HIVIEW 便携屏就是这样，它的对比度效果就是整体明暗），
        ///                      对比度滑块改走曲线；
        /// 都不认 → 纯软件曲线。
        /// </summary>
        private void Classify(Screenlet s, uint bcur, uint ccur)
        {
            string k = s.Key + "@" + s.Device;
            int r;
            bool have;
            lock (_gate) have = _probe.TryGetValue(k, out r);
            if (!have)
            {
                r = 0;
                if (ProbeWrite(s.Physical, false, bcur, s.BMin, s.BMax)) r |= 1;
                if (s.DdcContrast && ProbeWrite(s.Physical, true, ccur, s.CMin, s.CMax)) r |= 2;
                lock (_gate) _probe[k] = r;
            }
            bool bOk = (r & 1) != 0, cOk = (r & 2) != 0;
            s.WriteVerified = bOk ? 1 : -1;
            s.CWriteVerified = cOk ? 1 : -1;

            if (bOk)
            {
                s.Mode = DimMode.Ddc;
                if (s.DdcContrast && !cOk) TakeContrastSoft(s);
                return;
            }

            if (cOk && _cfg.GetBool("HwDim", false))
            {
                s.Mode = DimMode.DdcContrast;
                // 「正常亮度」点只在第一次记一下并存起来，之后不再读：
                // 万一程序被强杀，显示器停在压暗状态，再读就会把暗的当成正常。
                s.HwBase = _cfg.GetInt("HwBase_" + s.Key, -1);
                if (s.HwBase <= 0)
                {
                    s.HwBase = ccur >= 20 ? (int)ccur : 50;
                    _cfg.Set("HwBase_" + s.Key, s.HwBase);
                    _cfg.Save();
                }
                int floorPct = Math.Max(5, Math.Min(90, _cfg.GetInt("HwDimFloor", 25)));
                s.HwFloor = Math.Max((int)s.CMin, (int)Math.Round(s.HwBase * floorPct / 100.0));
                s.AppliedHw = (int)ccur;
                s.Brightness = HwToPercent(s, (int)ccur);
            }
            else
            {
                s.Mode = DimMode.Gamma;
                s.Brightness = ReadGammaPercent(s.Device);
            }
            TakeContrastSoft(s);
        }

        /// <summary>借对比度压暗时，滑块 0~HwSplit 这一段改由软件曲线接着压。</summary>
        public const int HwSplit = 30;

        public static int PercentToHw(Screenlet s, int p)
        {
            if (p <= HwSplit) return s.HwFloor;
            return (int)Math.Round(s.HwFloor + (s.HwBase - s.HwFloor) * (p - HwSplit) / (double)(100 - HwSplit));
        }

        private static int HwToPercent(Screenlet s, int hw)
        {
            if (s.HwBase <= s.HwFloor) return 100;
            if (hw <= s.HwFloor) return HwSplit;
            return Clamp((int)Math.Round(HwSplit + (hw - s.HwFloor) * (100 - HwSplit) / (double)(s.HwBase - s.HwFloor)));
        }

        /// <summary>曲线负责的那部分亮度（百分比）；-1 = 曲线不管亮度。</summary>
        private static int GammaPart(Screenlet s)
        {
            if (s.Mode == DimMode.Gamma) return s.Brightness;
            if (s.Mode == DimMode.DdcContrast)
                return s.Brightness >= HwSplit ? 100 : (int)Math.Round(s.Brightness * 100.0 / HwSplit);
            return -1;
        }

        internal struct ConnectorInfo { public uint Tech; public string Name; public bool Hdr; }

        /// <summary>GDI 设备名（\\.\DISPLAY1）→ 接口类型 + EDID 友好名。</summary>
        private static Dictionary<string, ConnectorInfo> BuildConnectorMap()
        {
            var map = new Dictionary<string, ConnectorInfo>(StringComparer.OrdinalIgnoreCase);
            try
            {
                uint np = 0, nm = 0;
                if (Native.GetDisplayConfigBufferSizes(Native.QDC_ONLY_ACTIVE_PATHS, out np, out nm) != 0) return map;
                var paths = new Native.PATH_INFO[np];
                var modes = new Native.MODE_INFO[nm];
                if (Native.QueryDisplayConfig(Native.QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) != 0)
                    return map;

                for (int i = 0; i < np; i++)
                {
                    var src = new Native.SOURCE_NAME();
                    src.header.type = Native.DEVINFO_GET_SOURCE_NAME;
                    src.header.size = (uint)Marshal.SizeOf(typeof(Native.SOURCE_NAME));
                    src.header.adapterId = paths[i].sourceInfo.adapterId;
                    src.header.id = paths[i].sourceInfo.id;
                    if (Native.DisplayConfigGetDeviceInfo(ref src) != 0) continue;
                    if (string.IsNullOrEmpty(src.viewGdiDeviceName)) continue;

                    var tgt = new Native.TARGET_NAME();
                    tgt.header.type = Native.DEVINFO_GET_TARGET_NAME;
                    tgt.header.size = (uint)Marshal.SizeOf(typeof(Native.TARGET_NAME));
                    tgt.header.adapterId = paths[i].targetInfo.adapterId;
                    tgt.header.id = paths[i].targetInfo.id;

                    var ci = new ConnectorInfo();
                    ci.Tech = paths[i].targetInfo.outputTechnology;
                    if (Native.DisplayConfigGetDeviceInfo(ref tgt) == 0)
                        ci.Name = tgt.monitorFriendlyDeviceName;

                    // HDR 开着的屏，Windows 不再用 gamma ramp，软件调光会悄无声息地失效
                    var adv = new Native.ADVANCED_COLOR_INFO();
                    adv.header.type = Native.DEVINFO_GET_ADVANCED_COLOR_INFO;
                    adv.header.size = (uint)Marshal.SizeOf(typeof(Native.ADVANCED_COLOR_INFO));
                    adv.header.adapterId = paths[i].targetInfo.adapterId;
                    adv.header.id = paths[i].targetInfo.id;
                    if (Native.DisplayConfigGetDeviceInfo(ref adv) == 0)
                        ci.Hdr = (adv.value & 2) != 0;

                    map[src.viewGdiDeviceName] = ci;
                }
            }
            catch { }
            return map;
        }

        /// <summary>亮度滑块 0-100 → 曲线整体缩放系数（GammaFloor..1.0）。</summary>
        public static double BrightFactor(int pct)
        {
            return Screenlet.GammaFloor + (1.0 - Screenlet.GammaFloor) * (Clamp(pct) / 100.0);
        }

        /// <summary>对比度滑块 0-100 → 曲线斜率。50 = 1.0 中性，跟硬件对比度的默认值对齐。</summary>
        public static double ContrastK(int pct)
        {
            pct = Clamp(pct);
            return (pct <= 50) ? 0.4 + pct / 50.0 * 0.6
                               : 1.0 + (pct - 50) / 50.0 * 1.0;
        }

        /// <summary>
        /// 把亮度和对比度合成一条曲线：out = clamp((n - 0.5) * k + 0.5) * b
        ///
        /// Windows 的硬约束是**曲线白点不能低于约一半**（ramp[255] &lt; 32768 就整条拒收，
        /// 曲线原地不动）。降低对比度本身会压低白点，所以对比度越低、能压的亮度就越少 ——
        /// 实测 b=0.5&amp;k=2.0 能过，b=0.5&amp;k=0.5 被拒。这里按当前 k 反推亮度下限并夹住，
        /// 否则用户拖到某个组合就会整条曲线不生效（表现为「滑块动了但屏幕没变」）。
        /// </summary>
        private const double RampTopFloor = 0.5;

        // ---- 原始曲线 ----
        // 做过颜色校准的屏，Windows 会在登录时往显卡里装一条校准曲线。直接按单位曲线重写会把校准冲掉，
        // 所以第一次接管某块屏时先把它当时的曲线存下来（内存 + %APPDATA% 里一份，防强杀），
        // 压暗/改对比度都叠在它上面，还原时也还原成它。
        private static readonly Dictionary<string, ushort[]> _baseRamps = new Dictionary<string, ushort[]>(StringComparer.OrdinalIgnoreCase);

        private static string BaseRampPath(string device)
        {
            return Path.Combine(Config.Dir, "ramp_" + device.Replace("\\", "").Replace(".", "") + ".bin");
        }

        private static ushort[] IdentityRamp()
        {
            var r = new ushort[768];
            for (int i = 0; i < 256; i++) r[i] = r[i + 256] = r[i + 512] = (ushort)(i * 257);
            return r;
        }

        private static ushort[] BaseRamp(string device, IntPtr hdc)
        {
            lock (_baseRamps)
            {
                ushort[] r;
                if (_baseRamps.TryGetValue(device, out r)) return r;

                string path = BaseRampPath(device);
                try
                {
                    if (File.Exists(path))
                    {
                        byte[] bytes = File.ReadAllBytes(path);
                        if (bytes.Length == 1536) { r = new ushort[768]; Buffer.BlockCopy(bytes, 0, r, 0, 1536); }
                    }
                }
                catch { r = null; }

                if (r == null)
                {
                    r = new ushort[768];
                    // 白点明显低于满值的，多半是旧版本留下的压暗而不是校准，当它不存在
                    if (!Native.GetDeviceGammaRamp(hdc, r) || r[255] < 58000 || r[511] < 58000 || r[767] < 58000)
                        r = IdentityRamp();
                    try
                    {
                        var bytes = new byte[1536];
                        Buffer.BlockCopy(r, 0, bytes, 0, 1536);
                        File.WriteAllBytes(path, bytes);
                    }
                    catch { }
                }
                _baseRamps[device] = r;
                return r;
            }
        }

        private static void ForgetBaseRamp(string device)
        {
            lock (_baseRamps) _baseRamps.Remove(device);
            try { File.Delete(BaseRampPath(device)); } catch { }
        }

        private static ushort[] BuildRamp(int brightPct, int contrastPct, ushort[] baseRamp)
        {
            double k = (contrastPct < 0) ? 1.0 : ContrastK(contrastPct);
            double b = (brightPct < 0) ? 1.0 : BrightFactor(brightPct);

            double top = Math.Min(1.0, 0.5 * k + 0.5);
            double minB = RampTopFloor / top;
            if (b < minB) b = minB;

            var ramp = new ushort[768];
            for (int i = 0; i < 256; i++)
            {
                double n = i / 255.0;
                double v = (n - 0.5) * k + 0.5;
                if (v < 0) v = 0; else if (v > 1) v = 1;
                v *= b;

                // 过一遍原始曲线（每个通道各自查表、线性插值）
                double x = v * 255.0;
                int i0 = (int)Math.Floor(x);
                if (i0 > 254) i0 = 254;
                double f = x - i0;
                for (int ch = 0; ch < 3; ch++)
                {
                    int o = ch * 256;
                    double u = baseRamp[o + i0] * (1 - f) + baseRamp[o + i0 + 1] * f;
                    ramp[o + i] = (ushort)Math.Max(0, Math.Min(65535, (int)Math.Round(u)));
                }
            }
            return ramp;
        }

        /// <summary>brightPct / contrastPct 传 -1 表示该项走显示器固件，不由曲线负责。</summary>
        private static bool ApplyRamp(string device, int brightPct, int contrastPct)
        {
            if (string.IsNullOrEmpty(device)) return false;
            IntPtr hdc = Native.CreateDC("DISPLAY", device, null, IntPtr.Zero);
            if (hdc == IntPtr.Zero) return false;
            try
            {
                ushort[] b = BaseRamp(device, hdc);
                if (brightPct < 0 && contrastPct < 0) return Native.SetDeviceGammaRamp(hdc, b);
                return Native.SetDeviceGammaRamp(hdc, BuildRamp(brightPct, contrastPct, b));
            }
            finally { Native.DeleteDC(hdc); }
        }

        /// <summary>整块屏交还：曲线恢复成接管之前的那条（有校准就是校准曲线）。</summary>
        private static bool ResetRamp(string device)
        {
            bool ok = ApplyRamp(device, -1, -1);
            if (ok) ForgetBaseRamp(device);
            return ok;
        }

        /// <summary>按 Screenlet 当前状态刷一次曲线（哪一项走软件就带哪一项）。</summary>
        private static bool ApplyRampFor(Screenlet s)
        {
            return ApplyRamp(s.Device, GammaPart(s), s.ContrastSoft ? s.Contrast : -1);
        }

        private static int ReadGammaPercent(string device)
        {
            IntPtr hdc = Native.CreateDC("DISPLAY", device, null, IntPtr.Zero);
            if (hdc == IntPtr.Zero) return 100;
            try
            {
                var r = new ushort[768];
                if (!Native.GetDeviceGammaRamp(hdc, r)) return 100;
                ushort[] b = BaseRamp(device, hdc);
                double factor = r[255] / (double)Math.Max((ushort)1, b[255]);
                return Clamp((int)Math.Round((factor - Screenlet.GammaFloor) / (1.0 - Screenlet.GammaFloor) * 100.0));
            }
            catch { return 100; }
            finally { Native.DeleteDC(hdc); }
        }

        /// <summary>把所有软件调光的屏恢复成原始曲线。退出时必须调，否则屏幕会一直暗着没法救。</summary>
        public void ResetAllGamma()
        {
            List<string> devs;
            lock (_gate)
            {
                devs = new List<string>(_gammaWanted.Keys);
                _gammaWanted.Clear();
            }
            foreach (string d in devs) ResetRamp(d);
            _gammaDirty = false;
            SaveGammaState();
        }

        /// <summary>全部交还：借来压暗的硬件对比度写回原值，曲线还原。退出和重新检测都走这里。</summary>
        public void ResetAll()
        {
            foreach (var s in Snapshot())
                if (s.Mode == DimMode.DdcContrast && s.HwBase > 0)
                    try { Native.SetMonitorContrast(s.Physical, (uint)s.HwBase); } catch { }
            ResetAllGamma();
        }

        /// <summary>重新把当前值刷回去。锁屏、切分辨率、独占全屏之后 gamma 会被系统重置。</summary>
        public void ReapplyGamma()
        {
            foreach (var s in Snapshot())
                if (s.Mode != DimMode.Ddc || s.ContrastSoft) ApplyRampFor(s);
        }

        /// <summary>从 VCP 能力串里抠出 model(XXX)，比 "Generic PnP Monitor" 有用得多。</summary>
        private static string ReadModel(IntPtr h)
        {
            try
            {
                uint len = 0;
                if (!Native.GetCapabilitiesStringLength(h, ref len) || len == 0 || len > 16384) return null;
                var sb = new StringBuilder((int)len + 1);
                if (!Native.CapabilitiesRequestAndCapabilitiesReply(h, sb, len)) return null;
                string caps = sb.ToString();
                int i = caps.IndexOf("model(", StringComparison.OrdinalIgnoreCase);
                if (i < 0) return null;
                int j = caps.IndexOf(')', i);
                if (j < 0) return null;
                string m = caps.Substring(i + 6, j - i - 6).Trim();
                return m.Length > 0 ? m : null;
            }
            catch { return null; }
        }

        public void QueueBrightness(Screenlet s, int pct)
        {
            pct = Clamp(pct);
            s.Brightness = pct;
            RememberSoft(s);
            Interlocked.Exchange(ref s.PendingB, pct);
            _signal.Set();
        }

        public void QueueContrast(Screenlet s, int pct)
        {
            pct = Clamp(pct);
            s.Contrast = pct;
            RememberSoft(s);
            Interlocked.Exchange(ref s.PendingC, pct);
            _signal.Set();
        }

        /// <summary>记账：这块屏有哪几项是本程序用软件曲线接管的。</summary>
        private void RememberSoft(Screenlet s)
        {
            if (s.Mode == DimMode.Ddc && !s.ContrastSoft) return;
            if (string.IsNullOrEmpty(s.Device)) return;

            var st = new SoftState();
            st.B = (s.Mode != DimMode.Ddc) ? s.Brightness : -1;
            st.C = s.ContrastSoft ? s.Contrast : -1;

            lock (_gate) _gammaWanted[s.Device] = st;
            _gammaDirty = true;   // 由 worker 落盘，避免拖滑块时每帧写一次文件
        }

        public static int Clamp(int v) { return v < 0 ? 0 : (v > 100 ? 100 : v); }

        private void WorkerLoop()
        {
            try { Refresh(); } catch { }
            var h = Changed; if (h != null) h();

            while (!_stop)
            {
                _signal.WaitOne(500);
                if (_stop) break;

                bool needRefresh = false;
                foreach (var s in Snapshot())
                {
                    int b = Interlocked.Exchange(ref s.PendingB, -1);
                    if (b >= 0 && b != s.AppliedB)
                    {
                        if (s.Mode == DimMode.DdcContrast)
                        {
                            int hw = PercentToHw(s, b);
                            bool ok = true;
                            if (hw != s.AppliedHw)
                            {
                                ok = Native.SetMonitorContrast(s.Physical, (uint)hw);
                                if (ok) s.AppliedHw = hw; else needRefresh = true;
                            }
                            if (ok && ApplyRampFor(s)) s.AppliedB = b;
                        }
                        else if (s.Mode == DimMode.Gamma)
                        {
                            // 曲线失败不触发重新枚举：句柄没坏，是系统拒了这条曲线，重枚举也救不回来
                            if (ApplyRampFor(s)) s.AppliedB = b;
                        }
                        else
                        {
                            uint wantRaw = s.FromPercent(b, s.BMin, s.BMax);
                            uint beforeRaw = s.AppliedB >= 0
                                ? s.FromPercent(s.AppliedB, s.BMin, s.BMax)
                                : wantRaw + 1;

                            if (!Native.SetMonitorBrightness(s.Physical, wantRaw))
                            {
                                needRefresh = true;
                            }
                            else
                            {
                                s.AppliedB = b;
                                if (s.WriteVerified == 0 && !VerifyDdcWrite(s, wantRaw, beforeRaw))
                                {
                                    DemoteToGamma(s, b);
                                    _gammaDirty = true;
                                }
                            }
                        }
                    }

                    int c = Interlocked.Exchange(ref s.PendingC, -1);
                    if (c >= 0 && c != s.AppliedC)
                    {
                        if (s.ContrastSoft)
                        {
                            if (ApplyRampFor(s)) s.AppliedC = c;
                        }
                        else if (s.HasContrast)
                        {
                            uint wantRaw = s.FromPercent(c, s.CMin, s.CMax);
                            uint beforeRaw = s.AppliedC >= 0
                                ? s.FromPercent(s.AppliedC, s.CMin, s.CMax)
                                : wantRaw + 1;

                            if (!Native.SetMonitorContrast(s.Physical, wantRaw))
                            {
                                needRefresh = true;
                            }
                            else
                            {
                                s.AppliedC = c;
                                // 跟亮度同一个坑：显示器对写入回 ok 却不执行。
                                // 以前这里只看返回值，所以对比度滑块拖了完全没反应还不报错。
                                if (s.CWriteVerified == 0 && !VerifyDdcContrast(s, wantRaw, beforeRaw))
                                {
                                    DemoteContrastToSoft(s, c);
                                    _gammaDirty = true;
                                }
                            }
                        }
                    }
                }

                if (_gammaDirty) { _gammaDirty = false; SaveGammaState(); }

                if (needRefresh)
                {
                    // 睡眠唤醒 / 拔插之后句柄会失效，重新枚举一次
                    Thread.Sleep(400);
                    try { Refresh(); } catch { }
                    var hh = Changed; if (hh != null) hh();
                }
            }
        }

        /// <summary>
        /// 有些显示器（实测 AOC Q2778VF）对 DDC 写入一律回 ok=True 却根本不执行：
        /// VCP 0x02「new control value」始终停在 1，说明固件压根没记录这次改动。
        /// 光看返回值会以为调好了，所以每块屏第一次写入之后必须读回来对一次。
        /// </summary>
        private static bool VerifyDdcWrite(Screenlet s, uint wantRaw, uint beforeRaw)
        {
            Thread.Sleep(250);   // 给固件一点时间，读太快会拿到旧值

            uint mn = 0, cur = 0, mx = 0;
            if (!Native.GetMonitorBrightness(s.Physical, ref mn, ref cur, ref mx))
            {
                s.WriteVerified = 1;   // 读不回来就不瞎猜，当它生效
                return true;
            }

            long diff = (long)cur - wantRaw;
            if (diff < 0) diff = -diff;
            if (diff <= 3) { s.WriteVerified = 1; return true; }

            // 纹丝不动 = 指令被丢了。若是变成了别的值，那是显示器自己夹了范围，仍算生效。
            if (cur == beforeRaw) { s.WriteVerified = -1; return false; }

            s.WriteVerified = 1;
            return true;
        }

        /// <summary>对比度的写入校验，跟亮度那套一样。</summary>
        private static bool VerifyDdcContrast(Screenlet s, uint wantRaw, uint beforeRaw)
        {
            Thread.Sleep(250);

            uint mn = 0, cur = 0, mx = 0;
            if (!Native.GetMonitorContrast(s.Physical, ref mn, ref cur, ref mx))
            {
                s.CWriteVerified = 1;
                return true;
            }

            long diff = (long)cur - wantRaw;
            if (diff < 0) diff = -diff;
            if (diff <= 3) { s.CWriteVerified = 1; return true; }

            if (cur == beforeRaw) { s.CWriteVerified = -1; return false; }

            s.CWriteVerified = 1;
            return true;
        }

        /// <summary>DDC 亮度写入是摆设的屏，亮度降级成软件曲线。对比度不动，它自己单独验。</summary>
        private void DemoteToGamma(Screenlet s, int pct)
        {
            s.Mode = DimMode.Gamma;
            s.Brightness = pct;
            if (!s.ContrastSoft) TakeContrastSoft(s);
            RememberSoft(s);
            ApplyRampFor(s);
            s.AppliedB = pct;

            var h = Changed;
            if (h != null) h();
        }

        /// <summary>
        /// 亮度一旦走了软件曲线，对比度也必须跟着走曲线，不能再发 DDC 0x12。
        /// 实测 HIVIEW 便携屏（2026-11）：0x10 写入被丢弃、0x12 却照收 —— 而它固件里的「对比度」
        /// 效果就是整体压暗/提亮。结果亮度滑块是软件曲线，对比度滑块反倒成了真正的「亮度」，
        /// 用户看到的就是「拖对比度，实际在调亮度」。统一交给曲线后两个滑块各司其职。
        /// 显示器里的硬件对比度保持现状不再去动；软件对比度从 50（斜率 1.0，中性）开始。
        /// </summary>
        private static void TakeContrastSoft(Screenlet s)
        {
            s.ContrastSoft = true;
            s.HasContrast = true;
            s.Contrast = 50;
            s.AppliedC = -1;
        }

        /// <summary>DDC 对比度写入是摆设的屏，对比度改用曲线斜率来做。</summary>
        private void DemoteContrastToSoft(Screenlet s, int pct)
        {
            s.ContrastSoft = true;
            s.Contrast = pct;
            RememberSoft(s);
            ApplyRampFor(s);
            s.AppliedC = pct;

            var h = Changed;
            if (h != null) h();
        }

        public void RequestRefresh() { RequestRefresh(0); }

        /// <summary>
        /// 显示模式刚变过的那一两秒里显示器还在重新初始化，这时候枚举经常拿不到 DDC 句柄，
        /// 会把列表刷成空的 —— 然后就一直空着，直到下一次显示事件。所以要延迟＋空结果重试。
        /// </summary>
        public void RequestRefresh(int delayMs)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                if (delayMs > 0) Thread.Sleep(delayMs);

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try { Refresh(); } catch { }
                    if (Snapshot().Count > 0) break;
                    if (attempt < 2) Thread.Sleep(900);
                }

                var h = Changed; if (h != null) h();
            });
        }

        public void Dispose()
        {
            _stop = true;
            _signal.Set();
            lock (_gate)
            {
                if (_raw.Length > 0) Native.DestroyPhysicalMonitors((uint)_raw.Length, _raw);
                _raw = new Native.PHYSICAL_MONITOR[0];
            }
        }
    }

    #endregion

    #region 配置

    internal class Config
    {
        private readonly Dictionary<string, string> _kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly string _path;

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SubBrightness"); }
        }

        public Config()
        {
            string dir = Dir;
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "config.ini");
            Load();
        }

        private void Load()
        {
            if (!File.Exists(_path)) return;
            foreach (string line in File.ReadAllLines(_path, Encoding.UTF8))
            {
                int i = line.IndexOf('=');
                if (i <= 0) continue;
                _kv[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
            }
        }

        public void Save()
        {
            var sb = new StringBuilder();
            foreach (var p in _kv) sb.AppendLine(p.Key + "=" + p.Value);
            try { File.WriteAllText(_path, sb.ToString(), Encoding.UTF8); } catch { }
        }

        public int GetInt(string k, int def)
        {
            string v; int r;
            return _kv.TryGetValue(k, out v) && int.TryParse(v, out r) ? r : def;
        }
        public void Set(string k, object v) { _kv[k] = Convert.ToString(v); }
        public bool GetBool(string k, bool def) { return GetInt(k, def ? 1 : 0) != 0; }
        public string GetStr(string k, string def)
        {
            string v;
            return _kv.TryGetValue(k, out v) ? v : def;
        }

        /// <summary>取第一个以 prefix 开头的键的值。用来兼容旧版带分辨率后缀的键名。</summary>
        public string FindValueByPrefix(string prefix)
        {
            foreach (var p in _kv)
                if (p.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return p.Value;
            return null;
        }
    }

    #endregion

    #region 滑块控件

    internal class Slider : Control
    {
        private int _value = 50;
        private bool _drag;

        public event EventHandler ValueChanged;

        public Slider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Ui.S(28);
        }

        public Color Accent = Color.FromArgb(255, 196, 61);

        public int Value
        {
            get { return _value; }
            set
            {
                int v = MonitorManager.Clamp(value);
                if (v == _value) return;
                _value = v;
                Invalidate();
                var h = ValueChanged; if (h != null) h(this, EventArgs.Empty);
            }
        }

        private int TrackLeft { get { return Ui.S(10); } }
        private int TrackWidth { get { return Math.Max(1, Width - Ui.S(20)); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int cy = Height / 2;
            int th = Ui.S(8), half = th / 2, kr = Ui.S(7);
            var track = new Rectangle(TrackLeft, cy - half, TrackWidth, th);
            using (var b = new SolidBrush(Color.FromArgb(64, 64, 70)))
                FillRound(g, b, track, half);

            int w = (int)Math.Round(TrackWidth * _value / 100.0);
            if (w > 0)
                using (var b = new SolidBrush(Accent))
                    FillRound(g, b, new Rectangle(TrackLeft, cy - half, w, th), half);

            int kx = TrackLeft + w;
            using (var b = new SolidBrush(Color.White))
                g.FillEllipse(b, kx - kr, cy - kr, kr * 2, kr * 2);
            using (var p = new Pen(Color.FromArgb(40, 0, 0, 0)))
                g.DrawEllipse(p, kx - kr, cy - kr, kr * 2, kr * 2);
        }

        private static void FillRound(Graphics g, Brush b, Rectangle r, int rad)
        {
            using (var gp = new GraphicsPath())
            {
                int d = rad * 2;
                if (r.Width <= d) { g.FillRectangle(b, r); return; }
                gp.AddArc(r.X, r.Y, d, d, 90, 180);
                gp.AddArc(r.Right - d, r.Y, d, d, 270, 180);
                gp.CloseFigure();
                g.FillPath(b, gp);
            }
        }

        private void SetFromMouse(int x)
        {
            double p = (x - TrackLeft) * 100.0 / TrackWidth;
            Value = (int)Math.Round(p);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _drag = true; SetFromMouse(e.X); }
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_drag) SetFromMouse(e.X);
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { _drag = false; base.OnMouseUp(e); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Value = _value + (e.Delta > 0 ? 5 : -5);
            base.OnMouseWheel(e);
        }
    }

    #endregion

    #region OSD

    internal class Osd : Form
    {
        private readonly System.Windows.Forms.Timer _hide = new System.Windows.Forms.Timer();
        private string _text = "";
        private int _pct;

        public Osd()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(Ui.S(260), Ui.S(92));
            BackColor = Color.FromArgb(24, 24, 28);
            DoubleBuffered = true;
            _hide.Interval = 1400;
            _hide.Tick += delegate { _hide.Stop(); Hide(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 /*NOACTIVATE*/ | 0x00000020 /*TRANSPARENT*/ | 0x00000080 /*TOOLWINDOW*/;
                return cp;
            }
        }

        public void Flash(string title, int pct, Rectangle onScreen)
        {
            _text = title; _pct = pct;
            int x = onScreen.Left + (onScreen.Width - Width) / 2;
            int y = onScreen.Bottom - Height - Math.Max(Ui.S(60), onScreen.Height / 8);
            Location = new Point(x, y);
            Invalidate();
            if (!Visible) Show();
            TopMost = true;
            _hide.Stop(); _hide.Start();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int pref = 2;
            try { Native.DwmSetWindowAttribute(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, 4); } catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var f = new Font("Microsoft YaHei UI", 10f))
            using (var b = new SolidBrush(Color.FromArgb(215, 215, 225)))
                g.DrawString(_text, f, b, Ui.S(20), Ui.S(16));

            using (var f = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold))
            using (var b = new SolidBrush(Color.White))
            {
                string s = _pct + "%";
                var sz = g.MeasureString(s, f);
                g.DrawString(s, f, b, Width - Ui.S(20) - sz.Width, Ui.S(10));
            }

            var track = new Rectangle(Ui.S(20), Ui.S(58), Width - Ui.S(40), Ui.S(8));
            using (var b = new SolidBrush(Color.FromArgb(70, 70, 78)))
                g.FillRectangle(b, track);
            using (var b = new SolidBrush(Color.FromArgb(255, 196, 61)))
                g.FillRectangle(b, new Rectangle(track.X, track.Y, (int)(track.Width * _pct / 100.0), track.Height));
        }
    }

    #endregion

    #region 面板

    internal class Panel_ : Form
    {
        private readonly MonitorManager _mm;
        private readonly Config _cfg;
        private readonly FlowLayoutPanel _flow;
        private readonly System.Windows.Forms.Timer _watch = new System.Windows.Forms.Timer();
        private int _grace;

        /// <summary>点方向按钮时回调出去，实际旋转由 TrayApp 放到后台线程做。</summary>
        public Action<Screenlet, int> RotateRequested;

        /// <summary>「同步主屏亮度」开关的状态读取和切换，由 TrayApp 接。</summary>
        public Func<bool> SyncState;
        public bool SyncAvailable;   // 台式机没有内屏，读不到「主屏亮度」，按钮就不出现
        public Action<bool> SyncToggled;

        public Panel_(MonitorManager mm, Config cfg)
        {
            _mm = mm; _cfg = cfg;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(32, 32, 36);
            Width = Ui.S(340);
            Padding = new Padding(Ui.S(6));

            _flow = new FlowLayoutPanel();
            _flow.Dock = DockStyle.Fill;
            _flow.FlowDirection = FlowDirection.TopDown;
            _flow.WrapContents = false;
            _flow.AutoSize = true;
            _flow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Controls.Add(_flow);

            // 前台被全屏程序占着时 Activate() 会失败，失败就永远收不到 Deactivate，
            // 面板会一直挂在屏幕上。这个看门狗负责兜底。
            _watch.Interval = 400;
            _watch.Tick += delegate
            {
                if (!Visible) { _watch.Stop(); return; }
                if (_grace > 0) { _grace--; return; }
                if (Native.GetForegroundWindow() != Handle) Hide();
            };
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Hide(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int pref = 2;
            try { Native.DwmSetWindowAttribute(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, 4); } catch { }
        }

        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Hide(); }

        public void Build()
        {
            _flow.Controls.Clear();
            var list = _mm.Controllable();

            if (list.Count == 0)
            {
                var l = new Label();
                l.Text = Ui.L("没检测到可调节的外接显示器。\n笔记本内屏不走这个通道，\n请用键盘的亮度键调。", "No adjustable external display found.\nThe laptop's built-in screen isn't handled here,\nuse the keyboard brightness keys.");
                l.ForeColor = Color.Gainsboro;
                l.Font = new Font("Microsoft YaHei UI", 9f);
                l.AutoSize = true;
                l.Margin = Ui.P(12, 12, 12, 12);
                _flow.Controls.Add(l);
            }

            foreach (var s in list)
            {
                var sl = s;

                var title = new Label();
                string res = (sl.PelsW > 0 && sl.PelsH > 0)
                    ? sl.PelsW + "×" + sl.PelsH
                    : sl.Bounds.Width + "×" + sl.Bounds.Height;
                title.Text = sl.Name + "   " + res + "   · " + ModeName(sl.Mode);
                title.ForeColor = Color.FromArgb(170, 170, 180);
                title.Font = new Font("Microsoft YaHei UI", 8.5f);
                title.AutoSize = true;
                title.Margin = Ui.P(12, 10, 12, 2);
                _flow.Controls.Add(title);

                if (sl.Hdr && (sl.Mode != DimMode.Ddc || sl.ContrastSoft))
                {
                    var hdr = new Label();
                    hdr.Text = Ui.L("HDR 已开启，软件调光对这块屏不起作用", "HDR is on — software dimming has no effect here");
                    hdr.ForeColor = Color.FromArgb(230, 170, 90);
                    hdr.Font = new Font("Microsoft YaHei UI", 8f);
                    hdr.AutoSize = true;
                    hdr.Margin = Ui.P(12, 0, 12, 2);
                    _flow.Controls.Add(hdr);
                }

                var val = new Label();
                val.ForeColor = Color.White;
                val.Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold);
                val.AutoSize = true;
                val.Margin = Ui.P(12, 0, 12, 0);
                val.Text = Ui.L("亮度 ", "Brightness ") + sl.Brightness + "%";
                _flow.Controls.Add(val);

                var slider = new Slider();
                if (sl.Mode == DimMode.Gamma) slider.Accent = Color.FromArgb(150, 152, 168);
                slider.Width = Width - Ui.S(28);
                slider.Value = sl.Brightness;
                slider.Margin = Ui.P(8, 2, 8, 4);
                slider.ValueChanged += delegate
                {
                    val.Text = Ui.L("亮度 ", "Brightness ") + slider.Value + "%";
                    _mm.QueueBrightness(sl, slider.Value);
                    _cfg.Set("Last_" + sl.Key, slider.Value);
                };
                _flow.Controls.Add(slider);

                var presets = new FlowLayoutPanel();
                presets.FlowDirection = FlowDirection.LeftToRight;
                presets.AutoSize = true;
                presets.WrapContents = false;
                presets.Margin = Ui.P(10, 0, 10, 6);
                // 纯软件调光的 0% 只是暗一半，标数字会让人以为能调黑，改标实际效果
                bool soft = sl.Mode == DimMode.Gamma;
                int[] pvals = soft ? new[] { 0, 50, 100 } : new[] { 0, 20, 40, 60, 80, 100 };
                string[] plabels = soft ? new[] { Ui.L("暗", "Dim"), Ui.L("中", "Mid"), Ui.L("亮", "Bright") } : null;
                for (int pi = 0; pi < pvals.Length; pi++)
                {
                    int pv = pvals[pi];
                    var btn = new Button();
                    btn.Text = soft ? plabels[pi] : pv.ToString();
                    btn.Width = Ui.S(soft ? 96 : 46); btn.Height = Ui.S(26);
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 78);
                    btn.ForeColor = Color.Gainsboro;
                    btn.BackColor = Color.FromArgb(44, 44, 50);
                    btn.Font = new Font("Microsoft YaHei UI", 8.5f);
                    btn.Margin = new Padding(Ui.S(2));
                    btn.TabStop = false;
                    btn.Click += delegate { slider.Value = pv; };
                    presets.Controls.Add(btn);
                }
                _flow.Controls.Add(presets);

                var rotRow = new FlowLayoutPanel();
                rotRow.FlowDirection = FlowDirection.LeftToRight;
                rotRow.AutoSize = true;
                rotRow.WrapContents = false;
                rotRow.Margin = Ui.P(10, 0, 10, 6);

                var rotLbl = new Label();
                rotLbl.Text = Ui.L("方向", "Rotate");
                rotLbl.ForeColor = Color.FromArgb(170, 170, 180);
                rotLbl.Font = new Font("Microsoft YaHei UI", 8.5f);
                rotLbl.AutoSize = true;
                rotLbl.Margin = Ui.P(2, 7, 4, 0);
                rotRow.Controls.Add(rotLbl);

                foreach (int o in new[] { 0, 1, 2, 3 })
                {
                    int ov = o;
                    bool active = (sl.Orientation == ov);
                    var rb = new Button();
                    rb.Text = MonitorManager.OrientationName(ov);
                    rb.Width = Ui.S(50); rb.Height = Ui.S(26);
                    rb.FlatStyle = FlatStyle.Flat;
                    rb.FlatAppearance.BorderColor = active ? Color.FromArgb(255, 196, 61) : Color.FromArgb(70, 70, 78);
                    rb.ForeColor = active ? Color.FromArgb(255, 205, 110) : Color.Gainsboro;
                    rb.BackColor = active ? Color.FromArgb(58, 50, 34) : Color.FromArgb(44, 44, 50);
                    rb.Font = new Font("Microsoft YaHei UI", 8.5f);
                    rb.Margin = new Padding(Ui.S(2));
                    rb.TabStop = false;
                    rb.Click += delegate
                    {
                        if (active) return;          // 已经是这个方向，别白折腾一次模式切换
                        Hide();                      // 屏幕马上要转，面板先收起来
                        var h = RotateRequested;
                        if (h != null) h(sl, ov);
                    };
                    rotRow.Controls.Add(rb);
                }
                _flow.Controls.Add(rotRow);

                if (sl.HasContrast)
                {
                    var cval = new Label();
                    cval.ForeColor = Color.FromArgb(170, 170, 180);
                    cval.Font = new Font("Microsoft YaHei UI", 8.5f);
                    cval.AutoSize = true;
                    cval.Margin = Ui.P(12, 2, 12, 0);
                    cval.Text = Ui.L("对比度 ", "Contrast ") + sl.Contrast + "%" + (sl.ContrastSoft ? Ui.L("   · 软件", "   · software") : "");
                    _flow.Controls.Add(cval);

                    var cs = new Slider();
                    cs.Accent = sl.ContrastSoft
                        ? Color.FromArgb(120, 128, 150)
                        : Color.FromArgb(120, 170, 255);
                    cs.Width = Width - Ui.S(28);
                    cs.Height = Ui.S(22);
                    cs.Value = sl.Contrast;
                    cs.Margin = Ui.P(8, 0, 8, 8);
                    cs.ValueChanged += delegate
                    {
                        cval.Text = Ui.L("对比度 ", "Contrast ") + cs.Value + "%" + (sl.ContrastSoft ? Ui.L("   · 软件", "   · software") : "");
                        _mm.QueueContrast(sl, cs.Value);
                    };
                    _flow.Controls.Add(cs);
                }

                var sep = new Label();
                sep.Height = Math.Max(1, Ui.S(1)); sep.Width = Width - Ui.S(24);
                sep.BackColor = Color.FromArgb(52, 52, 58);
                sep.Margin = Ui.P(12, 2, 12, 2);
                _flow.Controls.Add(sep);
            }

            if (list.Count > 0 && SyncAvailable)
            {
                bool on = SyncState != null && SyncState();
                var sync = new Button();
                sync.Text = Ui.L("同步主屏亮度", "Follow built-in screen brightness") + (on ? Ui.L("   · 开", "   · on") : Ui.L("   · 关", "   · off"));
                sync.Width = Width - Ui.S(28); sync.Height = Ui.S(30);
                sync.FlatStyle = FlatStyle.Flat;
                sync.FlatAppearance.BorderColor = on ? Color.FromArgb(255, 196, 61) : Color.FromArgb(70, 70, 78);
                sync.ForeColor = on ? Color.FromArgb(255, 205, 110) : Color.Gainsboro;
                sync.BackColor = on ? Color.FromArgb(58, 50, 34) : Color.FromArgb(44, 44, 50);
                sync.Font = new Font("Microsoft YaHei UI", 8.5f);
                sync.Margin = Ui.P(12, 6, 12, 10);
                sync.TabStop = false;
                sync.Click += delegate
                {
                    var h = SyncToggled;
                    if (h != null) h(!on);
                    BeginInvoke((Action)Build);   // 别在按钮自己的 Click 里把它销毁
                };
                _flow.Controls.Add(sync);
            }

            // 先把布局跑完再量，否则 AutoSize 的多行标签量出来会少一行
            _flow.PerformLayout();
            Height = _flow.PreferredSize.Height + Ui.S(12);
        }

        public static string ModeName(DimMode m)
        {
            switch (m)
            {
                case DimMode.Gamma: return Ui.L("软件调光", "software dimming");
                case DimMode.DdcContrast: return Ui.L("显示器调光", "monitor dimming");
                default: return Ui.L("硬件背光", "hardware backlight");
            }
        }

        public void ShowNearTray()
        {
            Build();
            // 贴着托盘那个角：任务栏默认在底部（托盘在右下），挪到顶部/左侧时跟着换角
            var scr = Screen.PrimaryScreen;
            var wa = scr.WorkingArea;
            int m = Ui.S(12);
            int x = wa.Right - Width - m, y = wa.Bottom - Height - m;
            if (wa.Top > scr.Bounds.Top) y = wa.Top + m;
            if (wa.Left > scr.Bounds.Left) x = wa.Left + m;
            Location = new Point(x, y);
            Show();
            Activate();
            _grace = 4;
            _watch.Start();
        }
    }

    #endregion

    #region 主程序

    internal class TrayApp : ApplicationContext
    {
        /// <summary>出错时留一行到 %TEMP%\subbrightness.log，正常情况下不写任何东西。</summary>
        internal static void Log(string s)
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "subbrightness.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + s + Environment.NewLine);
            }
            catch { }
        }

        private readonly NotifyIcon _tray = new NotifyIcon();
        private readonly Config _cfg = new Config();
        private readonly MonitorManager _mm;   // 依赖 _cfg，必须在构造函数里建，字段初始化顺序靠不住
        private readonly Dictionary<string, Osd> _osds = new Dictionary<string, Osd>();
        private Panel_ _panel;
        private HotkeyWindow _hk;
        private int _step;
        private TrayWheel _wheel;

        private const int HK_UP = 1, HK_DOWN = 2, HK_PANEL = 3, HK_ROT = 4;

        public TrayApp()
        {
            _step = _cfg.GetInt("Step", 10);
            _mm = new MonitorManager(_cfg);

            _tray.Icon = BuildIcon();
            _tray.Text = Ui.L("副屏亮度", "SubBrightness");
            _tray.Visible = true;
            _tray.MouseClick += delegate (object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) TogglePanel();
            };

            _panel = new Panel_(_mm, _cfg);
            _panel.RotateRequested = RotateAsync;
            _panel.SyncState = delegate { return _cfg.GetBool("SyncMain", false); };
            _panel.SyncToggled = SetSync;
            _hasMain = ReadMainBrightness() >= 0;
            _panel.SyncAvailable = _hasMain;
            // 提前把句柄建出来（不会显示窗口），否则 worker 线程没法 BeginInvoke 回 UI 线程
            { var force = _panel.Handle; }

            _mm.Changed += OnMonitorsChanged;
            // MonitorManager 的 worker 一构造就开始枚举，可能赶在上面订阅之前就把 Changed 抛完了。
            // 这里再要一次，保证订阅之后一定还会收到一次。
            _mm.RequestRefresh();

            _hk = new HotkeyWindow();
            _hk.Pressed += OnHotkey;
            RegisterHotkeys();
            BuildMenu();   // 菜单里要写实际注册上的热键，得等注册完

            SystemEvents_Hook();

            if (_hasMain && _cfg.GetBool("SyncMain", false)) StartSync();

            try
            {
                _wheel = new TrayWheel(_tray, _panel, delegate (int dir) { AdjustBy(dir * 5); });
            }
            catch (Exception ex) { Log("tray wheel hook: " + ex.Message); }

            // 保险：不管走哪条正常退出路径，都把 gamma 还原回去。
            // （被任务管理器强杀救不回来，那种情况只能靠重开本程序改回来）
            AppDomain.CurrentDomain.ProcessExit += delegate { try { _mm.ResetAll(); } catch { } };
            Microsoft.Win32.SystemEvents.SessionEnding += delegate { try { _mm.ResetAll(); } catch { } };
        }

        private void SystemEvents_Hook()
        {
            // 事件可能在拓扑还没稳定时就到了，等一下再枚举
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += delegate { _mm.RequestRefresh(800); };
            // 锁屏解锁后系统会把 gamma 打回单位曲线，这里补一刀
            Microsoft.Win32.SystemEvents.SessionSwitch += delegate (object s, Microsoft.Win32.SessionSwitchEventArgs e)
            {
                if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock ||
                    e.Reason == Microsoft.Win32.SessionSwitchReason.ConsoleConnect)
                {
                    var t = new System.Windows.Forms.Timer(); t.Interval = 1500;
                    t.Tick += delegate { t.Stop(); t.Dispose(); _mm.ReapplyGamma(); };
                    t.Start();
                }
            };
            Microsoft.Win32.SystemEvents.PowerModeChanged += delegate (object s, Microsoft.Win32.PowerModeChangedEventArgs e)
            {
                if (e.Mode == Microsoft.Win32.PowerModes.Resume)
                {
                    var t = new System.Windows.Forms.Timer(); t.Interval = 3000;
                    t.Tick += delegate { t.Stop(); t.Dispose(); _mm.RequestRefresh(); };
                    t.Start();
                }
            };
        }

        private void OnMonitorsChanged()
        {
            // 这个事件是在 worker 线程上抛的，下面全是 UI 侧的东西，必须切回去。
            // _panel 的句柄在构造函数里已经强制建好了，所以 BeginInvoke 一定可用。
            if (!_panel.IsHandleCreated) return;
            _panel.BeginInvoke((Action)delegate
            {
                ApplyRestore();
                if (_syncWatcher != null && _lastMain >= 0) SyncTo(_lastMain);   // 新插上的屏也跟上
                UpdateTrayText();
                if (_panel.Visible) _panel.Build();   // 面板开着的时候换屏，内容跟着变
            });
        }

        // 按屏记「这块已经恢复过了」，而不是整个程序一个标志位。
        // 用户会中途换屏（小的便携屏 / 大的外接屏轮着插），换上来的那块也得恢复它自己的亮度。
        private readonly HashSet<string> _restoredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void ApplyRestore()
        {
            var list = _mm.Controllable();

            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in list) present.Add(s.Key);
            _restoredKeys.IntersectWith(present);   // 拔掉的忘掉，插回来时重新恢复一次

            if (!_cfg.GetBool("RestoreOnStart", false))
            {
                foreach (var s in list) _restoredKeys.Add(s.Key);
                return;
            }

            foreach (var s in list)
            {
                if (!_restoredKeys.Add(s.Key)) continue;   // Add 返回 false = 这轮已经恢复过
                int last = LastBrightnessOf(s);
                if (last >= 0) _mm.QueueBrightness(s, last);
            }
        }

        /// <summary>
        /// 取这块屏记住的亮度。旧版本的键名带分辨率后缀（Last_Q2778VF|3360x1890），
        /// 现在键里去掉了分辨率（旋转会让宽高互换），这里顺手把旧记录迁过来。
        /// </summary>
        private int LastBrightnessOf(Screenlet s)
        {
            int v = _cfg.GetInt("Last_" + s.Key, -1);
            if (v >= 0) return MonitorManager.Clamp(v);

            string old = _cfg.FindValueByPrefix("Last_" + s.Key + "|");
            int parsed;
            if (old != null && int.TryParse(old, out parsed))
            {
                _cfg.Set("Last_" + s.Key, parsed);
                _cfg.Save();
                return MonitorManager.Clamp(parsed);
            }
            return -1;
        }

        /// <summary>
        /// 旋转放到后台线程：ChangeDisplaySettingsEx 会阻塞一两秒，
        /// 卡在 UI 线程上整个托盘都会僵住。
        /// </summary>
        private void RotateAsync(Screenlet s, int orientation)
        {
            string dev = s.Device;
            string name = s.Name;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err;
                try { err = MonitorManager.SetRotation(dev, orientation); }
                catch (Exception ex) { err = Ui.L("异常：", "Error: ") + ex.Message; }

                // 只记失败：驱动拒绝旋转时气泡可能被通知设置吞掉，留个凭据好排查
                if (err != null) Log("rotate dev=[" + dev + "] target=" + orientation + " -> " + err);

                if (!_panel.IsHandleCreated) return;
                _panel.BeginInvoke((Action)delegate
                {
                    if (err != null)
                        _tray.ShowBalloonTip(5000, Ui.L("副屏亮度", "SubBrightness"), name + Ui.L("：", ": ") + err, ToolTipIcon.Warning);
                    // 成功的话 DisplaySettingsChanged 一般会自己触发重新枚举，
                    // 但不能指望所有驱动都规矩，这里补一刀（等屏幕转完再枚举）
                    _mm.RequestRefresh(1500);
                });
            });
        }

        /// <summary>托盘提示里直接写清楚当前认到了哪块屏、各自多少。</summary>
        private void UpdateTrayText()
        {
            var list = _mm.Controllable();
            string t;
            if (list.Count == 0)
            {
                t = Ui.L("副屏亮度 — 没有可调的外接屏", "SubBrightness — no adjustable external display");
            }
            else
            {
                var sb = new StringBuilder(Ui.L("副屏亮度", "SubBrightness"));
                foreach (var s in list)
                {
                    sb.Append(" · ").Append(s.Name).Append(' ').Append(s.Brightness).Append('%');
                    if (s.Mode == DimMode.Gamma) sb.Append(Ui.L("(软)", "(soft)"));
                }
                t = sb.ToString();
            }
            if (t.Length > 62) t = t.Substring(0, 62);   // NotifyIcon.Text 上限 63
            _tray.Text = t;
        }

        private struct HkCand { public uint Mods; public uint Vk; public string Label; }

        private static HkCand C(uint mods, uint vk, string label)
        {
            var c = new HkCand(); c.Mods = mods; c.Vk = vk; c.Label = label; return c;
        }

        private readonly Dictionary<int, string> _hkLabels = new Dictionary<int, string>();

        /// <summary>
        /// 每个功能给一串候选组合，用第一个能注册上的。
        /// 硬编码单一组合在别的机器上照样会撞车（Ctrl+Alt+R 就常被别的程序占着），
        /// 而且 RegisterHotKey 失败是静默的 —— 只发托盘气泡的话会被通知设置吞掉，
        /// 用户只会觉得「这个键没反应」。所以实际生效的组合要写在面板上。
        /// </summary>
        private void RegisterHotkeys()
        {
            uint CA = Native.MOD_CONTROL | Native.MOD_ALT;
            uint CSA = Native.MOD_CONTROL | Native.MOD_SHIFT | Native.MOD_ALT;

            var failed = new List<string>();

            TryBind(HK_UP, failed, Ui.L("调亮", "brighter"),
                C(CA, Native.VK_PRIOR, "Ctrl+Alt+PgUp"),
                C(CSA, Native.VK_PRIOR, "Ctrl+Shift+Alt+PgUp"));

            TryBind(HK_DOWN, failed, Ui.L("调暗", "dimmer"),
                C(CA, Native.VK_NEXT, "Ctrl+Alt+PgDn"),
                C(CSA, Native.VK_NEXT, "Ctrl+Shift+Alt+PgDn"));

            TryBind(HK_PANEL, failed, Ui.L("开关面板", "toggle panel"),
                C(CA, Native.VK_B, "Ctrl+Alt+B"),
                C(CSA, Native.VK_B, "Ctrl+Shift+Alt+B"));

            TryBind(HK_ROT, failed, Ui.L("转屏", "rotate"),
                C(CA, Native.VK_R, "Ctrl+Alt+R"),
                C(CSA, Native.VK_R, "Ctrl+Shift+Alt+R"),
                C(CA, Native.VK_O, "Ctrl+Alt+O"),
                C(CA, Native.VK_END, "Ctrl+Alt+End"));

            if (failed.Count > 0)
                _tray.ShowBalloonTip(6000, Ui.L("副屏亮度", "SubBrightness"),
                    Ui.L("这些功能的热键候选全被占用了：", "All hotkey candidates are taken for: ") + string.Join(Ui.L("、", ", "), failed.ToArray())
                    + Ui.L("。点托盘图标打开面板一样能操作。", ". You can still use the panel from the tray icon."), ToolTipIcon.Warning);
        }

        private void TryBind(int id, List<string> failed, string what, params HkCand[] cands)
        {
            foreach (var c in cands)
            {
                if (Native.RegisterHotKey(_hk.Handle, id, c.Mods, c.Vk))
                {
                    _hkLabels[id] = c.Label;
                    return;
                }
            }
            failed.Add(what);
        }

        private string BuildHotkeyTip()
        {
            string up = null, down = null, panel = null, rot = null;
            _hkLabels.TryGetValue(HK_UP, out up);
            _hkLabels.TryGetValue(HK_DOWN, out down);
            _hkLabels.TryGetValue(HK_PANEL, out panel);
            _hkLabels.TryGetValue(HK_ROT, out rot);

            string l1 = (up != null && down != null)
                ? up + " / " + down + Ui.L(" 调亮度", " brightness")
                : Ui.L("亮度热键没注册上", "Brightness hotkeys unavailable");

            string l2 = (rot != null ? rot + Ui.L(" 转屏", " rotate") : Ui.L("转屏热键没注册上", "Rotate hotkey unavailable"))
                      + " · "
                      + (panel != null ? panel + Ui.L(" 开关面板", " toggle panel") : Ui.L("面板热键被占，点托盘图标", "Panel hotkey taken, click the tray icon"));

            return l1 + "\n" + l2;
        }

        private void OnHotkey(int id)
        {
            if (id == HK_PANEL) { TogglePanel(); return; }

            if (id == HK_ROT)
            {
                var rots = HotkeyTargets();
                if (rots.Count == 0)
                {
                    _tray.ShowBalloonTip(3000, Ui.L("副屏亮度", "SubBrightness"), Ui.L("没找到可旋转的外接屏。", "No external display to rotate."), ToolTipIcon.Info);
                    return;
                }
                foreach (var t in rots) RotateAsync(t, (t.Orientation + 1) & 3);
                return;
            }

            AdjustBy((id == HK_UP) ? _step : -_step);
        }

        private void AdjustBy(int delta)
        {
            var targets = HotkeyTargets();
            if (targets.Count == 0)
            {
                _tray.ShowBalloonTip(3000, Ui.L("副屏亮度", "SubBrightness"), Ui.L("没找到可调节的显示器。", "No adjustable display found."), ToolTipIcon.Info);
                return;
            }

            // 每块屏各按自己的当前值加减，OSD 也各弹各的，多屏时一眼看清谁变了多少
            foreach (var t in targets)
            {
                int v = MonitorManager.Clamp(t.Brightness + delta);
                _mm.QueueBrightness(t, v);
                _cfg.Set("Last_" + t.Key, v);
                OsdFor(t).Flash(t.Name, v, t.Bounds);
            }
            UpdateTrayText();
            if (_panel.Visible) _panel.Build();
        }

        // ---- 同步主屏亮度：内屏（Fn 键 / Windows 滑条）一变，外接屏跟着设成同样的百分比 ----
        private ManagementEventWatcher _syncWatcher;
        private bool _hasMain;
        private int _lastMain = -1;

        private void SetSync(bool on)
        {
            _cfg.Set("SyncMain", on ? 1 : 0);
            _cfg.Save();
            if (on) StartSync(); else StopSync();
        }

        private void StartSync()
        {
            if (_syncWatcher != null) return;
            try
            {
                _syncWatcher = new ManagementEventWatcher(new ManagementScope(@"root\WMI"),
                    new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
                _syncWatcher.EventArrived += delegate (object o, EventArrivedEventArgs e)
                {
                    int v;
                    try { v = Convert.ToInt32(e.NewEvent.Properties["Brightness"].Value); }
                    catch { return; }
                    if (!_panel.IsHandleCreated) return;
                    _panel.BeginInvoke((Action)delegate { SyncTo(v); });
                };
                _syncWatcher.Start();
            }
            catch (Exception ex)
            {
                Log("sync start: " + ex.Message);
                _syncWatcher = null;
                _cfg.Set("SyncMain", 0); _cfg.Save();
                _tray.ShowBalloonTip(5000, Ui.L("副屏亮度", "SubBrightness"), Ui.L("读不到笔记本内屏亮度，没法同步。", "Cannot read the built-in screen brightness, sync is unavailable."), ToolTipIcon.Warning);
                return;
            }
            int cur = ReadMainBrightness();
            if (cur >= 0) SyncTo(cur);
        }

        private void StopSync()
        {
            if (_syncWatcher == null) return;
            try { _syncWatcher.Stop(); _syncWatcher.Dispose(); } catch { }
            _syncWatcher = null;
        }

        private static int ReadMainBrightness()
        {
            try
            {
                using (var q = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness"))
                    foreach (ManagementObject o in q.Get())
                        return Convert.ToInt32(o["CurrentBrightness"]);
            }
            catch { }
            return -1;
        }

        private void SyncTo(int v)
        {
            v = MonitorManager.Clamp(v);
            _lastMain = v;
            bool any = false;
            foreach (var t in _mm.Controllable())
            {
                if (t.Brightness == v) continue;
                _mm.QueueBrightness(t, v);
                _cfg.Set("Last_" + t.Key, v);
                any = true;
            }
            if (!any) return;
            UpdateTrayText();
            if (_panel.Visible) _panel.Build();
        }

        private Osd OsdFor(Screenlet s)
        {
            Osd o;
            if (!_osds.TryGetValue(s.Key, out o))
            {
                o = new Osd();
                _osds[s.Key] = o;
            }
            return o;
        }

        /// <summary>热键作用范围：菜单里钉了某块屏就只调它，否则调全部外接屏。</summary>
        private List<Screenlet> HotkeyTargets()
        {
            var all = _mm.Controllable();
            var outp = new List<Screenlet>();

            string pin = _cfg.GetStr("HotkeyTarget", "");
            if (!string.IsNullOrEmpty(pin))
            {
                foreach (var s in all) if (s.Key == pin) outp.Add(s);
                if (outp.Count > 0) return outp;   // 钉的那块拔掉了就退回全部，别让热键变哑巴
            }

            outp.AddRange(all);   // 内屏不在列表里，所以「全部」就是全部外接屏
            return outp;
        }

        private void TogglePanel()
        {
            if (_panel.Visible) _panel.Hide();
            else _panel.ShowNearTray();
        }

        private void BuildMenu()
        {
            var m = new ContextMenuStrip();
            m.Font = new Font("Microsoft YaHei UI", 9f);

            string panelKey;
            _hkLabels.TryGetValue(HK_PANEL, out panelKey);
            m.Items.Add(Ui.L("亮度面板", "Brightness panel") + (panelKey != null ? "  (" + panelKey + ")" : ""), null, delegate { TogglePanel(); });
            m.Items.Add(new ToolStripSeparator());

            var stepMenu = new ToolStripMenuItem(Ui.L("每次调节步进", "Step size"));
            foreach (int st in new[] { 5, 10, 20 })
            {
                int sv = st;
                var it = new ToolStripMenuItem(sv + "%");
                it.Checked = (_step == sv);
                it.Click += delegate
                {
                    _step = sv; _cfg.Set("Step", sv); _cfg.Save();
                    foreach (ToolStripMenuItem x in stepMenu.DropDownItems)
                        x.Checked = x.Text == sv + "%";
                };
                stepMenu.DropDownItems.Add(it);
            }
            m.Items.Add(stepMenu);

            var targetMenu = new ToolStripMenuItem(Ui.L("热键作用于", "Hotkeys apply to"));
            m.Items.Add(targetMenu);
            // 显示器会插拔，每次弹菜单再重建，不能在启动时定死
            m.Opening += delegate { RebuildTargetMenu(targetMenu); };

            var restore = new ToolStripMenuItem(Ui.L("启动时恢复上次亮度", "Restore last brightness on start"));
            restore.Checked = _cfg.GetBool("RestoreOnStart", false);
            restore.Click += delegate
            {
                restore.Checked = !restore.Checked;
                _cfg.Set("RestoreOnStart", restore.Checked ? 1 : 0);
                _cfg.Save();
            };
            m.Items.Add(restore);

            var autorun = new ToolStripMenuItem(Ui.L("开机自动启动", "Start with Windows"));
            autorun.Checked = AutoRun.Enabled;
            autorun.Click += delegate
            {
                try
                {
                    AutoRun.Set(!autorun.Checked);
                    autorun.Checked = AutoRun.Enabled;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(Ui.L("设置开机自启失败：", "Failed to set autostart: ") + ex.Message, Ui.L("副屏亮度", "SubBrightness"));
                }
            };
            m.Items.Add(autorun);

            var sync = new ToolStripMenuItem(Ui.L("同步主屏亮度", "Follow built-in screen brightness"));
            sync.Visible = _hasMain;
            m.Opening += delegate { sync.Checked = _syncWatcher != null; };
            sync.Click += delegate { SetSync(_syncWatcher == null); };
            m.Items.Add(sync);

            var hwdim = new ToolStripMenuItem(Ui.L("亮度指令无效时借对比度压暗", "Dim via monitor contrast when brightness is ignored"));
            hwdim.Checked = _cfg.GetBool("HwDim", false);
            hwdim.Click += delegate
            {
                hwdim.Checked = !hwdim.Checked;
                _cfg.Set("HwDim", hwdim.Checked ? 1 : 0);
                _cfg.Save();
                _mm.ResetAll();
                _mm.RequestRefresh(300);
            };
            m.Items.Add(hwdim);

            // 实际注册上的热键（会自动避让被占用的组合），放这里查，不占面板地方
            var keys = new ToolStripMenuItem(Ui.L("快捷键", "Hotkeys"));
            foreach (string line in BuildHotkeyTip().Replace(" · ", "\n").Split('\n'))
            {
                var it = new ToolStripMenuItem(line);
                it.Enabled = false;
                keys.DropDownItems.Add(it);
            }
            var wheelTip = new ToolStripMenuItem(Ui.L("托盘图标上滚滚轮 调亮度", "Scroll wheel on tray icon: brightness"));
            wheelTip.Enabled = false;
            keys.DropDownItems.Add(wheelTip);
            m.Items.Add(keys);

            m.Items.Add(new ToolStripSeparator());
            // 顺带忘掉「这块屏 DDC 写入是摆设」的结论并还原曲线，
            // 好让改过显示器设置的人有办法让工具重新走硬件通路
            m.Items.Add(Ui.L("重新检测显示器", "Re-detect displays"), null, delegate
            {
                _mm.ResetAll();
                _mm.ForgetProbes();
                _mm.RequestRefresh(300);
            });
            m.Items.Add(Ui.L("退出", "Exit"), null, delegate { ExitApp(); });

            _tray.ContextMenuStrip = m;
        }

        private void RebuildTargetMenu(ToolStripMenuItem parent)
        {
            parent.DropDownItems.Clear();
            string pin = _cfg.GetStr("HotkeyTarget", "");

            var all = new ToolStripMenuItem(Ui.L("全部外接屏", "All external displays"));
            all.Checked = string.IsNullOrEmpty(pin);
            all.Click += delegate { _cfg.Set("HotkeyTarget", ""); _cfg.Save(); };
            parent.DropDownItems.Add(all);

            foreach (var s in _mm.Controllable())
            {
                var sl = s;
                var it = new ToolStripMenuItem(sl.Name + "   " + sl.Bounds.Width + "×" + sl.Bounds.Height
                                             + "  · " + Panel_.ModeName(sl.Mode));
                it.Checked = (pin == sl.Key);
                it.Click += delegate { _cfg.Set("HotkeyTarget", sl.Key); _cfg.Save(); };
                parent.DropDownItems.Add(it);
            }
        }

        private static Icon BuildIcon()
        {
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var p = new Pen(Color.FromArgb(255, 205, 90), 2.6f))
                {
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4;
                        g.DrawLine(p,
                            (float)(16 + Math.Cos(a) * 10), (float)(16 + Math.Sin(a) * 10),
                            (float)(16 + Math.Cos(a) * 14), (float)(16 + Math.Sin(a) * 14));
                    }
                }
                using (var b = new SolidBrush(Color.FromArgb(255, 205, 90)))
                    g.FillEllipse(b, 8, 8, 16, 16);
            }
            IntPtr h = bmp.GetHicon();
            var ico = (Icon)Icon.FromHandle(h).Clone();
            Native.DestroyIcon(h);
            bmp.Dispose();
            return ico;
        }

        private void ExitApp()
        {
            _cfg.Save();
            StopSync();
            if (_wheel != null) _wheel.Dispose();
            try
            {
                Native.UnregisterHotKey(_hk.Handle, HK_ROT);
                Native.UnregisterHotKey(_hk.Handle, HK_UP);
                Native.UnregisterHotKey(_hk.Handle, HK_DOWN);
                Native.UnregisterHotKey(_hk.Handle, HK_PANEL);
                _hk.DestroyHandle();
            }
            catch { }
            // 软件调光是进程外的全局状态：不还原的话退出后屏幕会一直暗着，
            // 用户除了重开这个程序没别的办法救回来。
            try { _mm.ResetAll(); } catch { }

            _tray.Visible = false;
            _tray.Dispose();
            _mm.Dispose();
            ExitThread();
        }
    }

    /// <summary>
    /// 托盘图标上滚滚轮调亮度。Windows 不把滚轮消息转给托盘图标，
    /// 只能挂一个低级鼠标钩子，滚轮发生时看光标是不是落在本图标的矩形里，是就吃掉这次滚动。
    /// </summary>
    internal sealed class TrayWheel : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct NOTIFYICONIDENTIFIER { public int cbSize; public IntPtr hWnd; public uint uID; public Guid guidItem; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT { public int x, y; public uint mouseData, flags, time; public IntPtr extra; }
        private delegate IntPtr LowLevelProc(int code, IntPtr w, IntPtr l);

        [DllImport("shell32.dll")]
        private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER id, out Native.RECT r);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc fn, IntPtr mod, uint tid);
        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string name);

        private const int WH_MOUSE_LL = 14, WM_MOUSEWHEEL = 0x020A;

        private readonly LowLevelProc _proc;   // 必须攥在字段里，否则被 GC 回收后钩子回调直接崩
        private readonly IntPtr _hook;
        private readonly NotifyIcon _icon;
        private readonly Control _ui;
        private readonly Action<int> _onWheel;
        private IntPtr _hwnd;
        private uint _id;

        public TrayWheel(NotifyIcon icon, Control ui, Action<int> onWheel)
        {
            _icon = icon; _ui = ui; _onWheel = onWheel;
            _proc = Hook;
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero) throw new InvalidOperationException("SetWindowsHookEx 失败 " + Marshal.GetLastWin32Error());
        }

        /// <summary>NotifyIcon 没公开窗口句柄和图标 ID，只能反射拿（.NET Framework 里字段名是 window / id）。</summary>
        private bool Resolve()
        {
            if (_hwnd != IntPtr.Zero) return true;
            try
            {
                var bf = BindingFlags.NonPublic | BindingFlags.Instance;
                var w = typeof(NotifyIcon).GetField("window", bf).GetValue(_icon) as NativeWindow;
                object id = typeof(NotifyIcon).GetField("id", bf).GetValue(_icon);
                if (w == null || w.Handle == IntPtr.Zero) return false;
                _hwnd = w.Handle;
                _id = (uint)Convert.ToInt32(id);
                return true;
            }
            catch { return false; }
        }

        private IntPtr Hook(int code, IntPtr w, IntPtr l)
        {
            if (code >= 0 && w.ToInt32() == WM_MOUSEWHEEL && Resolve())
            {
                var m = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(l, typeof(MSLLHOOKSTRUCT));
                var id = new NOTIFYICONIDENTIFIER();
                id.cbSize = Marshal.SizeOf(typeof(NOTIFYICONIDENTIFIER));
                id.hWnd = _hwnd; id.uID = _id;
                Native.RECT r;
                if (Shell_NotifyIconGetRect(ref id, out r) == 0
                    && m.x >= r.left && m.x < r.right && m.y >= r.top && m.y < r.bottom)
                {
                    int dir = ((short)(m.mouseData >> 16)) > 0 ? 1 : -1;
                    // 钩子回调必须马上返回，实际调节丢回 UI 线程排队
                    if (_ui.IsHandleCreated) _ui.BeginInvoke((Action)delegate { _onWheel(dir); });
                    return (IntPtr)1;
                }
            }
            return CallNextHookEx(_hook, code, w, l);
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        }
    }

    internal class HotkeyWindow : NativeWindow
    {
        public event Action<int> Pressed;
        public HotkeyWindow() { CreateHandle(new CreateParams()); }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                var h = Pressed;
                if (h != null) h(m.WParam.ToInt32());
            }
            base.WndProc(ref m);
        }
    }

    internal static class AutoRun
    {
        private static string LnkPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                    "副屏亮度.lnk");
            }
        }

        public static bool Enabled { get { return File.Exists(LnkPath); } }

        public static void Set(bool on)
        {
            if (!on)
            {
                if (File.Exists(LnkPath)) File.Delete(LnkPath);
                return;
            }
            string exe = Application.ExecutablePath;
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { LnkPath });
            Type lt = lnk.GetType();
            lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { exe });
            lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Path.GetDirectoryName(exe) });
            lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { Ui.L("副屏亮度快捷调节", "Quick brightness for external displays") });
            lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
        }
    }

    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            bool created;
            using (var mtx = new Mutex(true, "SubBrightness_SingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show(Ui.L("副屏亮度已经在运行了（看右下角托盘）。", "SubBrightness is already running (see the system tray)."), Ui.L("副屏亮度", "SubBrightness"),
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                try { Native.SetProcessDPIAware(); } catch { }
                Ui.Init();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }
    }

    #endregion
}
