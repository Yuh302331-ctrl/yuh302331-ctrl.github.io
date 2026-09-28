using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SysReport
{
    internal static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(11, 15, 20);
        public static readonly Color Panel = Color.FromArgb(21, 28, 36);
        public static readonly Color Track = Color.FromArgb(35, 48, 64);
        public static readonly Color Text = Color.FromArgb(230, 237, 243);
        public static readonly Color Muted = Color.FromArgb(139, 152, 165);
        public static readonly Color Ok = Color.FromArgb(57, 211, 83);
        public static readonly Color Warn = Color.FromArgb(227, 179, 65);
        public static readonly Color Bad = Color.FromArgb(248, 81, 73);
        public const string FontFamily = "Microsoft YaHei UI";
    }

    internal static class Level
    {
        public static Color Usage(double percent)
        {
            if (percent >= 90.0) return Theme.Bad;
            if (percent >= 75.0) return Theme.Warn;
            return Theme.Ok;
        }

        public static Color Battery(double percent)
        {
            if (percent <= 20.0) return Theme.Bad;
            if (percent <= 40.0) return Theme.Warn;
            return Theme.Ok;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeFileTime
    {
        public uint Low;
        public uint High;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeMemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    internal static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetSystemTimes(out NativeFileTime idle, out NativeFileTime kernel, out NativeFileTime user);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern ulong GetTickCount64();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GlobalMemoryStatusEx(ref NativeMemoryStatus buffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool AttachConsole(int processId);

        public static ulong Ticks(NativeFileTime value)
        {
            return ((ulong)value.High << 32) | value.Low;
        }
    }

    internal sealed class DiskInfo
    {
        public string Drive = "";
        public string DriveType = "";
        public bool Ready;
        public string TotalText = "";
        public string UsedText = "";
        public string FreeText = "";
        public double UsagePercent;
        public string Error;
    }

    internal sealed class Report
    {
        public string GeneratedAt = "";
        public string Hostname = "";
        public string Os = "";
        public string OsVersion = "";
        public string Arch = "";
        public string Processor = "";
        public string Runtime = "";
        public double CpuPercent = -1.0;
        public string CpuError;
        public int Cores;
        public double MemPercent = -1.0;
        public string MemError;
        public string MemTotal = "";
        public string MemUsed = "";
        public string MemAvail = "";
        public List<DiskInfo> Disks = new List<DiskInfo>();
        public bool BatteryPresent;
        public int BatteryPercent = -1;
        public string BatteryStatus = "";
        public long BatterySecondsLeft = -1;
        public string UptimeText = "";
        public int UptimeDays;
        public int UptimeHours;
        public int UptimeMinutes;
        public ulong UptimeTotalSeconds;
        public List<string> Tips = new List<string>();
    }

    internal static class Collector
    {
        public static string ReadableSize(double bytes)
        {
            double tb = bytes / 1099511627776.0;
            if (tb >= 1.0) return tb.ToString("0.0", CultureInfo.InvariantCulture) + " TB";
            double gb = bytes / 1073741824.0;
            if (gb >= 1.0) return gb.ToString("0.0", CultureInfo.InvariantCulture) + " GB";
            double mb = bytes / 1048576.0;
            if (mb >= 1.0) return mb.ToString("0", CultureInfo.InvariantCulture) + " MB";
            return (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB";
        }

        private static string RegText(string subKey, string name)
        {
            try
            {
                object raw = Registry.GetValue(@"HKEY_LOCAL_MACHINE\" + subKey, name, null);
                if (raw == null) return null;
                string text = raw as string;
                if (text == null) return raw.ToString();
                text = text.Trim();
                return text.Length == 0 ? null : text;
            }
            catch
            {
                return null;
            }
        }

        private static string ProcessorName()
        {
            string name = RegText(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString");
            if (!string.IsNullOrEmpty(name)) return name;
            string env = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
            return string.IsNullOrEmpty(env) ? "未知" : env;
        }

        private static int OsBuild()
        {
            string text = RegText(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber");
            int parsed;
            if (!string.IsNullOrEmpty(text)
                && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                return parsed;
            return Environment.OSVersion.Version.Build;
        }

        private static string OsName()
        {
            string name = RegText(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName");
            if (string.IsNullOrEmpty(name)) name = "Windows";
            if (OsBuild() >= 22000 && name.IndexOf("Windows 10", StringComparison.OrdinalIgnoreCase) >= 0)
                name = name.Replace("Windows 10", "Windows 11");
            if (name.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase))
                name = name.Substring("Microsoft ".Length);
            return name;
        }

        private static string OsVersionText()
        {
            string display = RegText(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion");
            object ubr = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR", null);
            string build = OsBuild().ToString(CultureInfo.InvariantCulture);
            if (ubr != null) build = build + "." + ubr.ToString();
            if (!string.IsNullOrEmpty(display)) return build + " (" + display + ")";
            return build;
        }

        private static string RuntimeName()
        {
            object release = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Release", null);
            int value = 0;
            if (release != null)
                int.TryParse(release.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            if (value >= 533320) return ".NET 4.8.1";
            if (value >= 528040) return ".NET 4.8";
            if (value >= 461808) return ".NET 4.7.2";
            if (value >= 460798) return ".NET 4.7";
            return ".NET " + Environment.Version.ToString(2);
        }

        private static double SampleCpu(int milliseconds)
        {
            NativeFileTime idle1, kernel1, user1, idle2, kernel2, user2;
            if (!Native.GetSystemTimes(out idle1, out kernel1, out user1)) return -1.0;
            System.Threading.Thread.Sleep(milliseconds);
            if (!Native.GetSystemTimes(out idle2, out kernel2, out user2)) return -1.0;

            ulong idle = Native.Ticks(idle2) - Native.Ticks(idle1);
            ulong kernel = Native.Ticks(kernel2) - Native.Ticks(kernel1);
            ulong user = Native.Ticks(user2) - Native.Ticks(user1);
            ulong total = kernel + user;
            if (total == 0UL) return 0.0;
            double usage = (1.0 - (double)idle / (double)total) * 100.0;
            return Math.Round(usage, 1);
        }

        private static List<DiskInfo> ReadDisks()
        {
            List<DiskInfo> list = new List<DiskInfo>();
            DriveInfo[] drives;
            try
            {
                drives = DriveInfo.GetDrives();
            }
            catch (Exception ex)
            {
                DiskInfo failed = new DiskInfo();
                failed.Error = "无法读取磁盘信息: " + ex.Message;
                list.Add(failed);
                return list;
            }

            foreach (DriveInfo drive in drives)
            {
                DiskInfo info = new DiskInfo();
                info.Drive = drive.Name;
                try
                {
                    info.DriveType = DescribeDriveType(drive.DriveType);
                    if (drive.DriveType == DriveType.NoRootDirectory || drive.DriveType == DriveType.Unknown)
                        continue;
                    if (!drive.IsReady)
                    {
                        info.Ready = false;
                        info.Error = "设备未就绪或无介质";
                        list.Add(info);
                        continue;
                    }
                    long total = drive.TotalSize;
                    long free = drive.TotalFreeSpace;
                    long used = total - free;
                    info.Ready = true;
                    info.TotalText = ReadableSize(total);
                    info.UsedText = ReadableSize(used);
                    info.FreeText = ReadableSize(free);
                    info.UsagePercent = total <= 0 ? 0.0 : Math.Round((double)used / (double)total * 100.0, 1);
                    list.Add(info);
                }
                catch (Exception ex)
                {
                    info.Ready = false;
                    info.Error = ex.Message;
                    list.Add(info);
                }
            }
            return list;
        }

        private static string DescribeDriveType(DriveType type)
        {
            switch (type)
            {
                case DriveType.Fixed: return "本地磁盘";
                case DriveType.Removable: return "可移动磁盘";
                case DriveType.Network: return "网络驱动器";
                case DriveType.CDRom: return "光驱";
                case DriveType.Ram: return "内存盘";
                default: return "未知";
            }
        }

        public static Report Build(int sampleMilliseconds)
        {
            Report report = new Report();
            report.GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            report.Hostname = Environment.MachineName;
            report.Os = OsName();
            report.OsVersion = OsVersionText();
            report.Arch = Environment.Is64BitOperatingSystem ? "x64" : "x86";
            report.Processor = ProcessorName();
            report.Runtime = RuntimeName();
            report.Cores = Environment.ProcessorCount;

            report.CpuPercent = SampleCpu(sampleMilliseconds);
            if (report.CpuPercent < 0.0) report.CpuError = "无法读取 CPU 使用率";

            NativeMemoryStatus memory = new NativeMemoryStatus();
            memory.Length = (uint)Marshal.SizeOf(typeof(NativeMemoryStatus));
            if (Native.GlobalMemoryStatusEx(ref memory))
            {
                ulong used = memory.TotalPhys - memory.AvailPhys;
                report.MemPercent = memory.MemoryLoad;
                report.MemTotal = ReadableSize(memory.TotalPhys);
                report.MemUsed = ReadableSize(used);
                report.MemAvail = ReadableSize(memory.AvailPhys);
            }
            else
            {
                report.MemError = "无法读取内存信息";
            }

            report.Disks = ReadDisks();

            try
            {
                PowerStatus power = SystemInformation.PowerStatus;
                bool flagged = (power.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) != 0;
                float percent = power.BatteryLifePercent;
                report.BatteryStatus = DescribePower(power.PowerLineStatus);
                if (!flagged && percent >= 0f && percent <= 1f)
                {
                    report.BatteryPresent = true;
                    report.BatteryPercent = (int)Math.Round(percent * 100f);
                    if (power.PowerLineStatus == PowerLineStatus.Offline && power.BatteryLifeRemaining > 0)
                        report.BatterySecondsLeft = power.BatteryLifeRemaining;
                }
            }
            catch
            {
                report.BatteryPresent = false;
            }

            ulong milliseconds = Native.GetTickCount64();
            ulong totalSeconds = milliseconds / 1000UL;
            ulong days = totalSeconds / 86400UL;
            ulong hours = (totalSeconds % 86400UL) / 3600UL;
            ulong minutes = (totalSeconds % 3600UL) / 60UL;
            report.UptimeDays = (int)days;
            report.UptimeHours = (int)hours;
            report.UptimeMinutes = (int)minutes;
            report.UptimeTotalSeconds = totalSeconds;
            report.UptimeHours = (int)hours;
            report.UptimeMinutes = (int)minutes;
            report.UptimeTotalSeconds = totalSeconds;
            report.UptimeText = string.Format(CultureInfo.InvariantCulture, "{0} 天 {1} 小时 {2} 分钟", days, hours, minutes);

            BuildTips(report);
            return report;
        }

        private static string DescribePower(PowerLineStatus status)
        {
            if (status == PowerLineStatus.Online) return "已接通电源";
            if (status == PowerLineStatus.Offline) return "使用电池";
            return "未知";
        }

        private static void BuildTips(Report report)
        {
            List<string> tips = new List<string>();
            if (report.MemError == null && report.MemPercent > 85.0)
                tips.Add("内存使用率偏高（>85%），建议关闭多余程序或考虑加装内存");
            foreach (DiskInfo disk in report.Disks)
            {
                if (disk.Error == null && disk.UsagePercent > 90.0)
                    tips.Add("磁盘 " + disk.Drive + " 使用率偏高（" + disk.UsagePercent.ToString("0.0", CultureInfo.InvariantCulture) + "%），建议清理空间");
            }
            if (report.CpuError == null && report.CpuPercent > 90.0)
                tips.Add("CPU 使用率过高（>90%），可能存在异常进程");
            if (report.UptimeDays >= 30)
                tips.Add("系统已连续运行超过 30 天，建议重启以完成更新并释放资源");
            if (report.BatteryPresent && report.BatteryStatus == "使用电池" && report.BatteryPercent <= 20)
                tips.Add("电池电量仅剩 " + report.BatteryPercent + "%，建议尽快接通电源");
            if (tips.Count == 0)
                tips.Add("各项指标正常，设备健康状况良好");
            report.Tips = tips;
        }
    }
    internal static class TextOut
    {
        public static string Bar(double percent, int width)
        {
            double clamped = Math.Max(0.0, Math.Min(100.0, percent));
            int filled = (int)Math.Round(clamped / 100.0 * width);
            return "[" + new string('#', filled) + new string('-', width - filled) + "]";
        }

        public static string Write(Report r)
        {
            StringBuilder sb = new StringBuilder();
            string rule = new string('=', 48);
            sb.AppendLine(rule);
            sb.AppendLine("        系统体检报告");
            sb.AppendLine("  生成时间：" + r.GeneratedAt);
            sb.AppendLine(rule);
            sb.AppendLine();
            sb.AppendLine("【系统信息】");
            sb.AppendLine("  主机名    : " + r.Hostname);
            sb.AppendLine("  操作系统  : " + r.Os);
            sb.AppendLine("  系统版本  : " + r.OsVersion);
            sb.AppendLine("  架构      : " + r.Arch);
            sb.AppendLine("  CPU 型号  : " + r.Processor);
            sb.AppendLine();
            if (r.CpuError == null)
            {
                sb.AppendLine("【CPU 使用率】");
                sb.AppendLine("  当前使用率: " + r.CpuPercent.ToString("0.0", CultureInfo.InvariantCulture) + "% " + Bar(r.CpuPercent, 10));
                sb.AppendLine("  逻辑核心数: " + r.Cores);
                sb.AppendLine();
            }
            if (r.MemError == null)
            {
                sb.AppendLine("【内存】");
                sb.AppendLine("  总容量    : " + r.MemTotal);
                sb.AppendLine("  已使用    : " + r.MemUsed);
                sb.AppendLine("  可用      : " + r.MemAvail);
                sb.AppendLine("  使用率    : " + r.MemPercent.ToString("0", CultureInfo.InvariantCulture) + "% " + Bar(r.MemPercent, 10));
                sb.AppendLine();
            }
            sb.AppendLine("【磁盘】");
            foreach (DiskInfo disk in r.Disks)
            {
                if (disk.Error != null)
                {
                    if (disk.Drive.Length > 0)
                        sb.AppendLine("  " + disk.Drive + "  设备未就绪或无介质（已跳过）");
                    else
                        sb.AppendLine("  读取失败  : " + disk.Error);
                    continue;
                }
                sb.AppendLine("  " + disk.Drive + "  (" + disk.DriveType + ")");
                sb.AppendLine("    总容量: " + disk.TotalText + "  已用: " + disk.UsedText + "  可用: " + disk.FreeText);
                sb.AppendLine("    使用率: " + disk.UsagePercent.ToString("0.0", CultureInfo.InvariantCulture) + "% " + Bar(disk.UsagePercent, 10));
            }
            sb.AppendLine();
            sb.AppendLine("【电源 / 电池】");
            if (r.BatteryPresent)
            {
                sb.AppendLine("  电源状态  : " + r.BatteryStatus);
                sb.AppendLine("  剩余电量  : " + r.BatteryPercent + "%");
                if (r.BatterySecondsLeft > 0)
                {
                    long hours = r.BatterySecondsLeft / 3600;
                    long minutes = (r.BatterySecondsLeft % 3600) / 60;
                    sb.AppendLine("  剩余时间  : 约 " + hours + " 小时 " + minutes + " 分钟");
                }
            }
            else
            {
                sb.AppendLine("  当前设备未检测到电池（可能为台式机）");
            }
            sb.AppendLine();
            sb.AppendLine("【运行时长】");
            sb.AppendLine("  已运行    : " + r.UptimeText);
            sb.AppendLine();
            sb.AppendLine("【健康建议】");
            foreach (string tip in r.Tips) sb.AppendLine("  " + tip);
            sb.AppendLine();
            sb.AppendLine(rule);
            return sb.ToString();
        }
    }

    internal static class JsonOut
    {
        private static string Esc(string value)
        {
            if (value == null) return "\"\"";
            StringBuilder sb = new StringBuilder();
            sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static string Num(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string Int(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static string Write(Report r)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"generated_at\": " + Esc(r.GeneratedAt) + ",");
            sb.AppendLine("  \"system\": {");
            sb.AppendLine("    \"hostname\": " + Esc(r.Hostname) + ",");
            sb.AppendLine("    \"os\": " + Esc(r.Os) + ",");
            sb.AppendLine("    \"os_version\": " + Esc(r.OsVersion) + ",");
            sb.AppendLine("    \"arch\": " + Esc(r.Arch) + ",");
            sb.AppendLine("    \"processor\": " + Esc(r.Processor) + ",");
            sb.AppendLine("    \"runtime\": " + Esc(r.Runtime));
            sb.AppendLine("  },");
            if (r.CpuError == null)
            {
                sb.AppendLine("  \"cpu\": {");
                sb.AppendLine("    \"usage_percent\": " + Num(r.CpuPercent) + ",");
                sb.AppendLine("    \"cores_logical\": " + Int(r.Cores));
                sb.AppendLine("  },");
            }
            else
            {
                sb.AppendLine("  \"cpu\": { \"error\": " + Esc(r.CpuError) + " },");
            }
            if (r.MemError == null)
            {
                sb.AppendLine("  \"memory\": {");
                sb.AppendLine("    \"total_readable\": " + Esc(r.MemTotal) + ",");
                sb.AppendLine("    \"used_readable\": " + Esc(r.MemUsed) + ",");
                sb.AppendLine("    \"available_readable\": " + Esc(r.MemAvail) + ",");
                sb.AppendLine("    \"usage_percent\": " + Num(r.MemPercent));
                sb.AppendLine("  },");
            }
            else
            {
                sb.AppendLine("  \"memory\": { \"error\": " + Esc(r.MemError) + " },");
            }
            sb.AppendLine("  \"disks\": [");
            for (int i = 0; i < r.Disks.Count; i++)
            {
                DiskInfo disk = r.Disks[i];
                if (disk.Error != null)
                {
                    sb.Append("    { \"drive\": " + Esc(disk.Drive) + ", \"ready\": false, \"error\": " + Esc(disk.Error) + " }");
                }
                else
                {
                    sb.Append("    { \"drive\": " + Esc(disk.Drive)
                        + ", \"drive_type\": " + Esc(disk.DriveType)
                        + ", \"ready\": true"
                        + ", \"total_readable\": " + Esc(disk.TotalText)
                        + ", \"used_readable\": " + Esc(disk.UsedText)
                        + ", \"free_readable\": " + Esc(disk.FreeText)
                        + ", \"usage_percent\": " + Num(disk.UsagePercent) + " }");
                }
                sb.AppendLine(i + 1 < r.Disks.Count ? "," : "");
            }
            sb.AppendLine("  ],");
            sb.AppendLine("  \"battery\": {");
            sb.AppendLine("    \"ac_status\": " + Esc(r.BatteryStatus) + ",");
            sb.AppendLine("    \"battery_present\": " + (r.BatteryPresent ? "true" : "false") + ",");
            sb.AppendLine("    \"battery_percent\": " + Int(r.BatteryPercent) + ",");
            sb.AppendLine("    \"seconds_left\": " + Int(r.BatterySecondsLeft));
            sb.AppendLine("  },");
            sb.AppendLine("  \"uptime\": {");
            sb.AppendLine("    \"days\": " + Int(r.UptimeDays) + ",");
            sb.AppendLine("    \"hours\": " + Int(r.UptimeHours) + ",");
            sb.AppendLine("    \"minutes\": " + Int(r.UptimeMinutes) + ",");
            sb.AppendLine("    \"total_seconds\": " + Int((long)r.UptimeTotalSeconds) + ",");
            sb.AppendLine("    \"readable\": " + Esc(r.UptimeText));
            sb.AppendLine("  },");
            sb.Append("  \"tips\": [");
            for (int i = 0; i < r.Tips.Count; i++)
            {
                sb.Append(Esc(r.Tips[i]));
                if (i + 1 < r.Tips.Count) sb.Append(", ");
            }
            sb.AppendLine("]");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }

    internal sealed class RingControl : Control
    {
        private const int BaseThickness = 11;
        private float _scale;
        private int _thickness;
        private double _percent = -1.0;
        private string _caption;
        private string _detail = "";
        private Color _accent = Theme.Ok;
        private readonly Font _valueFont;
        private readonly Font _captionFont;
        private readonly Font _detailFont;

        public RingControl(string caption, int size) : this(caption, size, 1f)
        {
        }

        public RingControl(string caption, int size, float scale)
        {
            _caption = caption == null ? "" : caption;
            _scale = scale <= 0f ? 1f : scale;
            _thickness = S(BaseThickness);
            _valueFont = new Font(Theme.FontFamily, 16f, FontStyle.Bold, GraphicsUnit.Point);
            _captionFont = new Font(Theme.FontFamily, 9f, FontStyle.Regular, GraphicsUnit.Point);
            _detailFont = new Font(Theme.FontFamily, 7f, FontStyle.Regular, GraphicsUnit.Point);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel;
            Size = new Size(size, size);
            TabStop = false;
        }

        private int S(int value)
        {
            return (int)Math.Round(value * _scale);
        }

        public void SetScale(float scale)
        {
            _scale = scale <= 0f ? 1f : scale;
            _thickness = S(BaseThickness);
            Invalidate();
        }

        public string Caption
        {
            get { return _caption; }
            set { _caption = value == null ? "" : value; }
        }

        public void SetValue(double percent, string detail, Color accent)
        {
            _percent = percent;
            _detail = detail == null ? "" : detail;
            _accent = accent;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (SolidBrush background = new SolidBrush(BackColor))
                g.FillRectangle(background, ClientRectangle);

            int inset = _thickness / 2 + S(2);
            Rectangle box = new Rectangle(inset, inset, Width - inset * 2, Height - inset * 2);
            using (Pen track = new Pen(Theme.Track, _thickness))
                g.DrawEllipse(track, box);

            if (_percent >= 0.0)
            {
                double clamped = Math.Max(0.0, Math.Min(100.0, _percent));
                float sweep = (float)Math.Max(0.7, clamped * 3.599);
                using (Pen pen = new Pen(_accent, _thickness))
                {
                    pen.StartCap = LineCap.Flat;
                    pen.EndCap = LineCap.Flat;
                    g.DrawArc(pen, box, -90f, sweep);
                }
            }

            string big = _percent >= 0.0
                ? ((int)Math.Round(_percent)).ToString(CultureInfo.InvariantCulture) + "%"
                : "--";
            DrawBand(g, big, _valueFont, Theme.Text, Height / 2 - S(16));
            DrawBand(g, _caption, _captionFont, Theme.Muted, Height / 2 + S(7));
            DrawBand(g, _detail, _detailFont, Theme.Muted, Height / 2 + S(25));
        }

        private void DrawBand(Graphics g, string text, Font font, Color color, int centerY)
        {
            if (string.IsNullOrEmpty(text)) return;
            RectangleF rect = new RectangleF(0f, centerY - S(12), Width, S(24));
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                format.FormatFlags = StringFormatFlags.NoWrap;
                using (SolidBrush brush = new SolidBrush(color))
                    g.DrawString(text, font, brush, rect, format);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _valueFont.Dispose();
                _captionFont.Dispose();
                _detailFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class MainForm : Form
    {
        private const int BaseWidth = 880;
        private const int BasePadX = 20;
        private const int BaseRingSize = 118;
        private const int BaseRingGap = 14;
        private const int InfoRows = 4;

        private readonly int _sampleMilliseconds;
        private float _scale;
        private int _windowWidth;
        private int _padX;
        private int _ringSize;
        private int _ringGap;
        private Panel _scroll;
        private Label _title;
        private Label _subtitle;
        private Button _refresh;
        private Panel _cardTop;
        private Panel _cardDisk;
        private Label _diskTitle;
        private Panel _cardInfo;
        private Panel _cardTips;
        private Label _tipsTitle;
        private RingControl _cpuRing;
        private RingControl _memRing;
        private RingControl _batteryRing;
        private readonly List<RingControl> _diskRings = new List<RingControl>();
        private readonly Label[] _infoKeys = new Label[InfoRows * 2];
        private readonly Label[] _infoValues = new Label[InfoRows * 2];
        private readonly Label[] _tipLabels = new Label[8];
        private Report _report;

        public MainForm(int sampleMilliseconds) : this(sampleMilliseconds, 0f)
        {
        }

        public MainForm(int sampleMilliseconds, float forcedScale)
        {
            _sampleMilliseconds = sampleMilliseconds;
            SetScaleFields(forcedScale > 0f ? forcedScale : DetectScale());
            Text = "系统体检";
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = new Font(Theme.FontFamily, 9f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ClientSize = new Size(_windowWidth, S(660));
            DoubleBuffered = true;
            _scroll = new Panel();
            _scroll.BackColor = Theme.Bg;
            _scroll.AutoScroll = true;
            _scroll.Dock = DockStyle.Fill;
            base.Controls.Add(_scroll);
            BuildUi();
        }

        private void SetScaleFields(float scale)
        {
            _scale = scale <= 0f ? 1f : scale;
            _windowWidth = S(BaseWidth);
            _padX = S(BasePadX);
            _ringSize = S(BaseRingSize);
            _ringGap = S(BaseRingGap);
        }

        private int S(int value)
        {
            return (int)Math.Round(value * _scale);
        }

        private static float DetectScale()
        {
            try
            {
                using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
                    return graphics.DpiX / 96f;
            }
            catch
            {
                return 1f;
            }
        }

        private void BuildUi()
        {
            _title = new Label();
            _title.Text = "系统体检报告";
            _title.ForeColor = Theme.Text;
            _title.BackColor = Theme.Bg;
            _title.Font = new Font(Theme.FontFamily, 17f, FontStyle.Bold, GraphicsUnit.Point);
            _title.AutoSize = false;
            _scroll.Controls.Add(_title);

            _refresh = new Button();
            _refresh.Text = "重新检测";
            _refresh.FlatStyle = FlatStyle.Flat;
            _refresh.FlatAppearance.BorderSize = 0;
            _refresh.BackColor = Theme.Panel;
            _refresh.ForeColor = Theme.Text;
            _refresh.Cursor = Cursors.Hand;
            _refresh.Click += delegate { RefreshData(); };
            _scroll.Controls.Add(_refresh);

            _subtitle = new Label();
            _subtitle.ForeColor = Theme.Muted;
            _subtitle.BackColor = Theme.Bg;
            _subtitle.AutoSize = false;
            _subtitle.Text = "正在检测…";
            _scroll.Controls.Add(_subtitle);

            _cardTop = new Panel();
            _cardTop.BackColor = Theme.Panel;
            _scroll.Controls.Add(_cardTop);
            _cpuRing = new RingControl("CPU 使用率", _ringSize, _scale);
            _memRing = new RingControl("内存", _ringSize, _scale);
            _batteryRing = new RingControl("电池", _ringSize, _scale);
            _cardTop.Controls.Add(_cpuRing);
            _cardTop.Controls.Add(_memRing);
            _cardTop.Controls.Add(_batteryRing);

            _cardDisk = new Panel();
            _cardDisk.BackColor = Theme.Panel;
            _scroll.Controls.Add(_cardDisk);
            _diskTitle = new Label();
            _diskTitle.Text = "磁盘";
            _diskTitle.ForeColor = Theme.Muted;
            _diskTitle.BackColor = Theme.Panel;
            _diskTitle.Font = new Font(Theme.FontFamily, 10f, FontStyle.Bold, GraphicsUnit.Point);
            _diskTitle.AutoSize = true;
            _cardDisk.Controls.Add(_diskTitle);

            _cardInfo = new Panel();
            _cardInfo.BackColor = Theme.Panel;
            _scroll.Controls.Add(_cardInfo);
            for (int i = 0; i < _infoKeys.Length; i++)
            {
                _infoKeys[i] = new Label();
                _infoKeys[i].ForeColor = Theme.Muted;
                _infoKeys[i].BackColor = Theme.Panel;
                _infoKeys[i].AutoSize = false;
                _infoKeys[i].TextAlign = ContentAlignment.MiddleLeft;
                _cardInfo.Controls.Add(_infoKeys[i]);

                _infoValues[i] = new Label();
                _infoValues[i].ForeColor = Theme.Text;
                _infoValues[i].BackColor = Theme.Panel;
                _infoValues[i].AutoSize = false;
                _infoValues[i].TextAlign = ContentAlignment.MiddleLeft;
                _cardInfo.Controls.Add(_infoValues[i]);
            }

            _cardTips = new Panel();
            _cardTips.BackColor = Theme.Panel;
            _scroll.Controls.Add(_cardTips);
            _tipsTitle = new Label();
            _tipsTitle.Text = "健康建议";
            _tipsTitle.ForeColor = Theme.Muted;
            _tipsTitle.BackColor = Theme.Panel;
            _tipsTitle.Font = new Font(Theme.FontFamily, 10f, FontStyle.Bold, GraphicsUnit.Point);
            _tipsTitle.AutoSize = true;
            _cardTips.Controls.Add(_tipsTitle);
            for (int i = 0; i < _tipLabels.Length; i++)
            {
                _tipLabels[i] = new Label();
                _tipLabels[i].ForeColor = Theme.Text;
                _tipLabels[i].BackColor = Theme.Panel;
                _tipLabels[i].AutoSize = false;
                _tipLabels[i].TextAlign = ContentAlignment.MiddleLeft;
                _tipLabels[i].Text = "";
                _cardTips.Controls.Add(_tipLabels[i]);
            }
        }

        public void PrepareForShot()
        {
            RefreshData();
            _scroll.AutoScrollPosition = new Point(0, 0);
        }

        public string ShotDiagnostics()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("scale        = " + _scale.ToString("0.###", CultureInfo.InvariantCulture));
            sb.AppendLine("clientSize   = " + ClientSize.Width + " x " + ClientSize.Height);
            sb.AppendLine("windowWidth  = " + _windowWidth);
            sb.AppendLine("padX/ring/gap= " + _padX + " / " + _ringSize + " / " + _ringGap);
            sb.AppendLine("desiredHeight= " + DesiredHeight());
            sb.AppendLine("screenBounds = " + Screen.PrimaryScreen.Bounds.Width + " x " + Screen.PrimaryScreen.Bounds.Height);
            sb.AppendLine("workingArea  = " + Screen.PrimaryScreen.WorkingArea.Width + " x " + Screen.PrimaryScreen.WorkingArea.Height);
            sb.AppendLine("autoScrollMin= " + _scroll.AutoScrollMinSize.Width + " x " + _scroll.AutoScrollMinSize.Height);
            sb.AppendLine("scrollClient = " + _scroll.ClientSize.Width + " x " + _scroll.ClientSize.Height);
            sb.AppendLine("scrollPos    = " + _scroll.AutoScrollPosition.X + ", " + _scroll.AutoScrollPosition.Y);
            sb.Append("titleBounds  = " + _title.Bounds);
            return sb.ToString();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RefreshData();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            SetScaleFields(e.DeviceDpiNew / 96f);
            ResizeRings();
            if (_report != null) ApplyReport();
            else Relayout();
        }

        private void ResizeRings()
        {
            RingControl[] rings = { _cpuRing, _memRing, _batteryRing };
            for (int i = 0; i < rings.Length; i++)
            {
                if (rings[i] == null) continue;
                rings[i].SetScale(_scale);
                rings[i].Size = new Size(_ringSize, _ringSize);
            }
            for (int i = 0; i < _diskRings.Count; i++)
            {
                _diskRings[i].SetScale(_scale);
                _diskRings[i].Size = new Size(_ringSize, _ringSize);
            }
        }

        private void RefreshData()
        {
            _subtitle.Text = "正在检测…（CPU 采样 " + (_sampleMilliseconds / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " 秒）";
            _subtitle.Update();
            Report report;
            try
            {
                report = Collector.Build(_sampleMilliseconds);
            }
            catch (Exception ex)
            {
                _subtitle.Text = "检测失败：" + ex.Message;
                return;
            }
            _report = report;
            ApplyReport();
        }

        private void ApplyReport()
        {
            Report r = _report;
            _subtitle.Text = r.Hostname + "   ·   " + r.Os + "   ·   " + r.Processor;

            if (r.CpuError == null)
                _cpuRing.SetValue(r.CpuPercent, r.Cores + " 个逻辑核心", Level.Usage(r.CpuPercent));
            else
                _cpuRing.SetValue(-1.0, r.CpuError, Theme.Muted);

            if (r.MemError == null)
                _memRing.SetValue(r.MemPercent, CompactPair(r.MemUsed, r.MemTotal), Level.Usage(r.MemPercent));
            else
                _memRing.SetValue(-1.0, r.MemError, Theme.Muted);

            if (r.BatteryPresent && r.BatteryPercent >= 0)
            {
                string detail = r.BatteryStatus;
                if (r.BatterySecondsLeft > 0)
                    detail = "约 " + (r.BatterySecondsLeft / 3600) + " 小时 " + ((r.BatterySecondsLeft % 3600) / 60) + " 分";
                _batteryRing.SetValue(r.BatteryPercent, detail, Level.Battery(r.BatteryPercent));
            }
            else
            {
                _batteryRing.SetValue(-1.0, "未检测到电池", Theme.Muted);
            }

            EnsureDiskRings(r.Disks.Count);
            for (int i = 0; i < r.Disks.Count; i++)
            {
                DiskInfo disk = r.Disks[i];
                string caption = disk.Drive.TrimEnd('\\');
                _diskRings[i].SetValue(
                    disk.Error == null ? disk.UsagePercent : -1.0,
                    disk.Error == null ? CompactPair(disk.UsedText, disk.TotalText) : "未就绪",
                    disk.Error == null ? Level.Usage(disk.UsagePercent) : Theme.Muted);
                _diskRings[i].Tag = caption;
            }
            for (int i = 0; i < r.Disks.Count; i++)
                SetRingCaption(_diskRings[i], r.Disks[i].Drive.TrimEnd('\\'));

            string[] keys = { "操作系统", "系统版本", "CPU 型号", "架构", "主机名", "运行时长", "生成时间", "运行时" };
            string[] values = { r.Os, r.OsVersion, r.Processor, r.Arch, r.Hostname, r.UptimeText, r.GeneratedAt, r.Runtime };
            for (int i = 0; i < _infoKeys.Length; i++)
            {
                _infoKeys[i].Text = keys[i];
                _infoValues[i].Text = values[i];
            }

            for (int i = 0; i < _tipLabels.Length; i++)
                _tipLabels[i].Text = i < r.Tips.Count ? "•  " + r.Tips[i] : "";

            int desired = DesiredHeight();
            int available = Screen.PrimaryScreen.WorkingArea.Height - 60;
            ClientSize = new Size(_windowWidth, Math.Min(desired, Math.Max(S(360), available)));
            Relayout();
        }

        private static void SetRingCaption(RingControl ring, string caption)
        {
            ring.Caption = caption;
            ring.Invalidate();
        }

        private static string CompactPair(string used, string total)
        {
            int space = used.IndexOf(' ');
            if (space > 0) used = used.Substring(0, space);
            return used + " / " + total;
        }

        private void EnsureDiskRings(int count)
        {
            while (_diskRings.Count > count)
            {
                RingControl last = _diskRings[_diskRings.Count - 1];
                _diskRings.RemoveAt(_diskRings.Count - 1);
                _cardDisk.Controls.Remove(last);
                last.Dispose();
            }
            while (_diskRings.Count < count)
            {
                RingControl ring = new RingControl("", _ringSize, _scale);
                _cardDisk.Controls.Add(ring);
                _diskRings.Add(ring);
            }
        }

        private int DiskPerRow(int cardWidth)
        {
            int usable = cardWidth - S(24);
            int slot = _ringSize + _ringGap;
            int perRow = usable / slot;
            return perRow < 1 ? 1 : perRow;
        }

        private int DesiredHeight()
        {
            int contentWidth = _windowWidth - _padX * 2;
            int perRow = DiskPerRow(contentWidth);
            int diskCount = _diskRings.Count;
            int diskRows = diskCount <= 0 ? 1 : (diskCount + perRow - 1) / perRow;
            int tipsRows = _report == null ? 1 : Math.Max(1, _report.Tips.Count);

            int y = S(18) + S(34) + S(24);
            y += _ringSize + S(20);
            y += S(10) + S(36) + diskRows * _ringSize + (diskRows - 1) * S(10) + S(14);
            y += S(10) + S(12) + InfoRows * S(24) + S(12);
            y += S(10) + S(36) + tipsRows * S(24) + S(12);
            return y + S(16);
        }

        private void Relayout()
        {
            int contentWidth = _windowWidth - _padX * 2;
            int pad = S(20);
            int y = S(18);

            _title.Location = new Point(_padX, y);
            _title.Size = new Size(S(360), S(28));
            _refresh.Location = new Point(_windowWidth - _padX - S(96), y + S(1));
            _refresh.Size = new Size(S(96), S(26));

            y += S(34);
            _subtitle.Location = new Point(_padX, y);
            _subtitle.Size = new Size(contentWidth, S(20));
            y += S(24);

            _cardTop.Location = new Point(_padX, y);
            _cardTop.Size = new Size(contentWidth, _ringSize + S(20));
            int topStart = (contentWidth - (3 * _ringSize + 2 * _ringGap)) / 2;
            RingControl[] topRings = { _cpuRing, _memRing, _batteryRing };
            for (int i = 0; i < topRings.Length; i++)
                topRings[i].Location = new Point(topStart + i * (_ringSize + _ringGap), S(10));
            y += _cardTop.Height + S(10);

            int perRow = DiskPerRow(contentWidth);
            int diskRows = _diskRings.Count <= 0 ? 1 : (_diskRings.Count + perRow - 1) / perRow;
            int diskHeight = S(36) + diskRows * _ringSize + (diskRows - 1) * S(10) + S(14);
            _cardDisk.Location = new Point(_padX, y);
            _cardDisk.Size = new Size(contentWidth, diskHeight);
            _diskTitle.Location = new Point(pad, S(12));
            for (int i = 0; i < _diskRings.Count; i++)
            {
                int row = i / perRow;
                int inRow = Math.Min(perRow, _diskRings.Count - row * perRow);
                int rowStart = (contentWidth - (inRow * _ringSize + (inRow - 1) * _ringGap)) / 2;
                _diskRings[i].Location = new Point(rowStart + (i % perRow) * (_ringSize + _ringGap), S(36) + row * (_ringSize + S(10)));
            }
            y += diskHeight + S(10);

            int infoHeight = S(12) + InfoRows * S(24) + S(12);
            _cardInfo.Location = new Point(_padX, y);
            _cardInfo.Size = new Size(contentWidth, infoHeight);
            int columnWidth = (contentWidth - S(40)) / 2;
            for (int i = 0; i < _infoKeys.Length; i++)
            {
                int x = pad + (i % 2) * columnWidth;
                int rowY = S(12) + (i / 2) * S(24);
                _infoKeys[i].Location = new Point(x, rowY);
                _infoKeys[i].Size = new Size(S(70), S(20));
                _infoValues[i].Location = new Point(x + S(74), rowY);
                _infoValues[i].Size = new Size(columnWidth - S(86), S(20));
            }
            y += infoHeight + S(10);

            int tipsRows = _report == null ? 1 : Math.Max(1, _report.Tips.Count);
            int tipsHeight = S(36) + tipsRows * S(24) + S(12);
            _cardTips.Location = new Point(_padX, y);
            _cardTips.Size = new Size(contentWidth, tipsHeight);
            _tipsTitle.Location = new Point(pad, S(12));
            for (int i = 0; i < _tipLabels.Length; i++)
            {
                _tipLabels[i].Location = new Point(pad, S(36) + i * S(24));
                _tipLabels[i].Size = new Size(contentWidth - S(40), S(22));
            }
            y += tipsHeight + S(16);

            _scroll.AutoScrollMinSize = new Size(_windowWidth, y);
            _scroll.AutoScrollPosition = new Point(0, 0);
        }    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool json = false;
            bool text = false;
            string outputFile = null;
            string shotFile = null;
            int sampleMilliseconds = 600;
            float forcedScale = 0f;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "--json") json = true;
                else if (arg == "--text") text = true;
                else if (arg == "--sample" && i + 1 < args.Length)
                {
                    double seconds;
                    if (double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
                        sampleMilliseconds = (int)Math.Max(50.0, Math.Min(10000.0, seconds * 1000.0));
                }
                else if (arg == "--scale" && i + 1 < args.Length)
                {
                    float parsedScale;
                    if (float.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out parsedScale))
                        forcedScale = Math.Max(0.5f, Math.Min(4f, parsedScale));
                }
                else if (arg == "--out" && i + 1 < args.Length) outputFile = args[++i];
                else if (arg == "--shot" && i + 1 < args.Length) shotFile = args[++i];
                else if (arg == "--help" || arg == "-h" || arg == "/?")
                {
                    WriteOutput("用法:\n  SysReport.exe                打开图形界面\n  SysReport.exe --text         输出文本体检报告\n  SysReport.exe --json         输出 JSON\n  SysReport.exe --json --out 文件.json\n  SysReport.exe --sample 1.5   自定义 CPU 采样秒数（默认 0.6）\n  SysReport.exe --shot 图.png   离屏渲染一张界面截图（调试用）\n  SysReport.exe --scale 1.5    强制界面缩放倍数（配合 --shot 调试）\n", null);
                    return;
                }
            }

            if (shotFile != null)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                MainForm preview = new MainForm(sampleMilliseconds, forcedScale);
                preview.StartPosition = FormStartPosition.Manual;
                preview.Location = new Point(-32000, -32000);
                preview.ShowInTaskbar = false;
                preview.Show();
                Application.DoEvents();
                preview.PrepareForShot();
                Application.DoEvents();
                File.WriteAllText(shotFile + ".txt", preview.ShotDiagnostics(), new UTF8Encoding(false));
                using (Bitmap bitmap = new Bitmap(preview.ClientSize.Width, preview.ClientSize.Height))
                {
                    preview.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    bitmap.Save(shotFile, System.Drawing.Imaging.ImageFormat.Png);
                }
                preview.Close();
                preview.Dispose();
                return;
            }

            if (json || text)
            {
                string content = json ? JsonOut.Write(Collector.Build(sampleMilliseconds)) : TextOut.Write(Collector.Build(sampleMilliseconds));
                WriteOutput(content, outputFile);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            {
                MessageBox.Show("界面出现异常：`n`n" + e.Exception, "系统体检", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                MessageBox.Show("程序出现异常：`n`n" + e.ExceptionObject, "系统体检", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            try
            {
                Application.Run(new MainForm(sampleMilliseconds, forcedScale));
            }
            catch (Exception ex)
            {
                MessageBox.Show("程序启动失败：`n`n" + ex, "系统体检", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void WriteOutput(string content, string outputFile)
        {
            try
            {
                if (outputFile != null)
                {
                    File.WriteAllText(outputFile, content, new UTF8Encoding(false));
                    return;
                }
                Native.AttachConsole(-1);
                using (StreamWriter writer = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)))
                {
                    writer.AutoFlush = true;
                    writer.Write(content);
                }
            }
            catch
            {
            }
        }
    }
}