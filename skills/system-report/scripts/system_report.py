#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
system_report.py — Windows 系统体检报告生成器（纯标准库，零依赖）

采集并输出：系统信息 / CPU / 内存 / 磁盘 / 电池 / 运行时长，
以易读的中文报告形式呈现，也可输出 JSON 供程序调用。

用法：
    python system_report.py                # 输出体检报告
    python system_report.py --json         # 输出 JSON 格式
    python system_report.py --save         # 同时保存报告到 ./system_report_<日期>.txt
    python system_report.py --sample 1.0   # 指定 CPU 采样时长（秒，默认 0.6）
"""

import argparse
import ctypes
import datetime
import json
import os
import platform
import string
import sys
import time

try:
    import winreg
except ImportError:
    winreg = None

IS_WINDOWS = os.name == "nt"
_kernel32 = ctypes.windll.kernel32 if IS_WINDOWS else None

# SetErrorMode 标志位：避免查询未就绪设备时弹出「请插入磁盘」阻塞式对话框
SEM_FAILCRITICALERRORS = 0x0001
SEM_NOOPENFILEERRORBOX = 0x8000

# 盘符类型（GetDriveTypeW 返回值）
DRIVE_UNKNOWN = 0
DRIVE_NO_ROOT_DIR = 1
DRIVE_REMOVABLE = 2
DRIVE_FIXED = 3
DRIVE_REMOTE = 4
DRIVE_CDROM = 5
DRIVE_RAMDISK = 6

_DRIVE_TYPE_NAMES = {
    DRIVE_REMOVABLE: "可移动磁盘",
    DRIVE_FIXED: "本地磁盘",
    DRIVE_REMOTE: "网络驱动器",
    DRIVE_CDROM: "光驱",
    DRIVE_RAMDISK: "内存盘",
}


# ---------- Windows API 结构体（ctypes，零依赖） ----------

class MEMORYSTATUSEX(ctypes.Structure):
    _fields_ = [
        ("dwLength", ctypes.c_ulong),
        ("dwMemoryLoad", ctypes.c_ulong),
        ("ullTotalPhys", ctypes.c_ulonglong),
        ("ullAvailPhys", ctypes.c_ulonglong),
        ("ullTotalPageFile", ctypes.c_ulonglong),
        ("ullAvailPageFile", ctypes.c_ulonglong),
        ("ullTotalVirtual", ctypes.c_ulonglong),
        ("ullAvailVirtual", ctypes.c_ulonglong),
        ("ullAvailExtendedVirtual", ctypes.c_ulonglong),
    ]


class SYSTEM_POWER_STATUS(ctypes.Structure):
    _fields_ = [
        # 这些字段是 Windows 的 BYTE（无符号）；用 c_byte 会把 255 读成 -1，
        # 导致「无电池 / 未检测到电池」的判断永远不成立。
        ("ACLineStatus", ctypes.c_ubyte),
        ("BatteryFlag", ctypes.c_ubyte),
        ("BatteryLifePercent", ctypes.c_ubyte),
        ("SystemStatusFlag", ctypes.c_ubyte),
        ("BatteryLifeTime", ctypes.c_ulong),
        ("BatteryFullLifeTime", ctypes.c_ulong),
    ]


class FILETIME(ctypes.Structure):
    _fields_ = [
        ("dwLowDateTime", ctypes.c_ulong),
        ("dwHighDateTime", ctypes.c_ulong),
    ]


def _bind_prototypes():
    """显式声明 argtypes/restype，避免 64 位返回值被 ctypes 默认按 c_int 截断。"""
    if not IS_WINDOWS:
        return
    k = _kernel32
    signatures = [
        ("GetSystemTimes", [ctypes.POINTER(FILETIME)] * 3, ctypes.c_bool),
        ("GetTickCount64", [], ctypes.c_ulonglong),
        ("GetLogicalDrives", [], ctypes.c_uint32),
        ("GetDriveTypeW", [ctypes.c_wchar_p], ctypes.c_uint),
        (
            "GetDiskFreeSpaceExW",
            [
                ctypes.c_wchar_p,
                ctypes.POINTER(ctypes.c_ulonglong),
                ctypes.POINTER(ctypes.c_ulonglong),
                ctypes.POINTER(ctypes.c_ulonglong),
            ],
            ctypes.c_bool,
        ),
        ("GetSystemPowerStatus", [ctypes.POINTER(SYSTEM_POWER_STATUS)], ctypes.c_bool),
        ("GlobalMemoryStatusEx", [ctypes.POINTER(MEMORYSTATUSEX)], ctypes.c_bool),
        ("SetErrorMode", [ctypes.c_uint], ctypes.c_uint),
        ("SetConsoleOutputCP", [ctypes.c_uint], ctypes.c_bool),
    ]
    for name, argtypes, restype in signatures:
        try:
            fn = getattr(k, name)
        except AttributeError:
            continue
        fn.argtypes = argtypes
        fn.restype = restype


def _setup_console():
    """中文在 UTF-8 / GBK 控制台下都能正常输出，失败时静默降级。"""
    stdout = sys.stdout
    if stdout is None or not hasattr(stdout, "reconfigure"):
        return
    try:
        stdout.reconfigure(errors="replace")
    except Exception:
        pass
    if not IS_WINDOWS or not stdout.isatty():
        return
    encoding = getattr(stdout, "encoding", None)
    if encoding:
        try:
            "系统体检".encode(encoding)
            return
        except Exception:
            pass
    try:
        _kernel32.SetConsoleOutputCP(65001)
        stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass


def _suppress_device_errors():
    """关掉系统级错误弹窗，避免空光驱/读卡器把脚本卡住。"""
    if not IS_WINDOWS:
        return
    try:
        _kernel32.SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOOPENFILEERRORBOX)
    except Exception:
        pass


# ---------- 格式化辅助 ----------

def _gb_to_readable(num):
    """字节数转可读字符串"""
    tb = num / (1024 ** 4)
    if tb >= 1:
        return f"{tb:.1f} TB"
    gb = num / (1024 ** 3)
    if gb >= 1:
        return f"{gb:.1f} GB"
    mb = num / (1024 ** 2)
    if mb >= 1:
        return f"{mb:.0f} MB"
    return f"{num / 1024:.0f} KB"


def _pct(used, total):
    if total <= 0:
        return 0.0
    return round(used / total * 100, 1)


def _bar(percent, width=10):
    """用量条；控制台编码不支持方块字符时自动降级为 ASCII。"""
    filled_char, empty_char = "█", "░"
    encoding = getattr(sys.stdout, "encoding", None) or "utf-8"
    try:
        filled_char.encode(encoding)
    except Exception:
        filled_char, empty_char = "#", "-"
    try:
        value = max(0.0, min(100.0, float(percent)))
    except (TypeError, ValueError):
        value = 0.0
    filled = int(round(value / 100 * width))
    return "[" + filled_char * filled + empty_char * (width - filled) + "]"


# ---------- 采集 ----------

def get_processor_name():
    """CPU 型号：优先读注册表 ProcessorNameString，回退 platform.processor()。"""
    if winreg is not None:
        try:
            path = r"HARDWARE\DESCRIPTION\System\CentralProcessor\0"
            with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, path) as key:
                name, _ = winreg.QueryValueEx(key, "ProcessorNameString")
            if name and name.strip():
                return name.strip()
        except Exception:
            pass
    return platform.processor() or platform.machine() or "未知"


def get_system_info():
    """系统与硬件基础信息"""
    uname = platform.uname()
    return {
        "hostname": uname.node,
        "os": f"{uname.system} {uname.release}",
        "os_version": platform.version(),
        "arch": uname.machine,
        "processor": get_processor_name(),
        "python": platform.python_version(),
    }


def get_memory():
    """物理内存信息（GlobalMemoryStatusEx）"""
    if not IS_WINDOWS:
        return {"error": "仅支持 Windows"}
    st = MEMORYSTATUSEX()
    st.dwLength = ctypes.sizeof(MEMORYSTATUSEX)
    if not _kernel32.GlobalMemoryStatusEx(ctypes.byref(st)):
        return {"error": "无法读取内存信息"}
    total = st.ullTotalPhys
    avail = st.ullAvailPhys
    used = total - avail
    return {
        "total": total,
        "total_readable": _gb_to_readable(total),
        "available": avail,
        "available_readable": _gb_to_readable(avail),
        "used": used,
        "used_readable": _gb_to_readable(used),
        "usage_percent": st.dwMemoryLoad,
    }


def get_cpu_usage(sample_seconds=0.6):
    """CPU 使用率（GetSystemTimes 两次采样计算，避免依赖 psutil）"""
    if not IS_WINDOWS:
        return {"error": "仅支持 Windows"}

    def _sample():
        idle, kernel, user = FILETIME(), FILETIME(), FILETIME()
        if not _kernel32.GetSystemTimes(
            ctypes.byref(idle), ctypes.byref(kernel), ctypes.byref(user)
        ):
            return None
        idle_t = (idle.dwHighDateTime << 32) | idle.dwLowDateTime
        kernel_t = (kernel.dwHighDateTime << 32) | kernel.dwLowDateTime
        user_t = (user.dwHighDateTime << 32) | user.dwLowDateTime
        return idle_t, kernel_t, user_t

    t1 = _sample()
    if t1 is None:
        return {"error": "无法读取 CPU 使用率"}

    time.sleep(sample_seconds)

    t2 = _sample()
    if t2 is None:
        return {"error": "无法读取 CPU 使用率"}

    idle_delta = t2[0] - t1[0]
    kernel_delta = t2[1] - t1[1]
    user_delta = t2[2] - t1[2]
    total_delta = kernel_delta + user_delta
    if total_delta <= 0:
        usage = 0.0
    else:
        usage = (1 - idle_delta / total_delta) * 100

    cores = os.cpu_count() or 0
    return {
        "usage_percent": round(usage, 1),
        "sample_seconds": sample_seconds,
        "cores_physical": cores,
        "cores_logical": cores,
    }


def get_disks():
    """所有逻辑盘符的容量信息（GetDiskFreeSpaceExW）"""
    if not IS_WINDOWS:
        return [{"error": "仅支持 Windows"}]

    drives_bitmask = _kernel32.GetLogicalDrives()
    if drives_bitmask == 0:
        return [{"error": "无法读取磁盘信息"}]

    disks = []
    for i, letter in enumerate(string.ascii_uppercase):
        if not drives_bitmask & (1 << i):
            continue

        root = f"{letter}:\\"
        drive_type = _kernel32.GetDriveTypeW(root)
        if drive_type in (DRIVE_UNKNOWN, DRIVE_NO_ROOT_DIR):
            continue

        free = ctypes.c_ulonglong(0)
        total = ctypes.c_ulonglong(0)
        ok = _kernel32.GetDiskFreeSpaceExW(
            root, None, ctypes.byref(total), ctypes.byref(free)
        )
        if not ok:
            disks.append(
                {
                    "drive": root,
                    "drive_type": _DRIVE_TYPE_NAMES.get(drive_type, "未知"),
                    "ready": False,
                    "error": "设备未就绪或无介质",
                }
            )
            continue

        used = total.value - free.value
        disks.append(
            {
                "drive": root,
                "drive_type": _DRIVE_TYPE_NAMES.get(drive_type, "未知"),
                "ready": True,
                "total": total.value,
                "total_readable": _gb_to_readable(total.value),
                "used": used,
                "used_readable": _gb_to_readable(used),
                "free": free.value,
                "free_readable": _gb_to_readable(free.value),
                "usage_percent": _pct(used, total.value),
            }
        )
    return disks


def get_battery():
    """电池与电源状态（GetSystemPowerStatus），台式机可能无电池"""
    if not IS_WINDOWS:
        return {"error": "仅支持 Windows"}

    st = SYSTEM_POWER_STATUS()
    if not _kernel32.GetSystemPowerStatus(ctypes.byref(st)):
        return {"error": "无法读取电源状态"}

    aclines = {0: "使用电池", 1: "已接通电源", 255: "未知"}
    result = {
        "ac_status": aclines.get(st.ACLineStatus, "未知"),
        "battery_flag": st.BatteryFlag,
    }

    # BatteryFlag 位 7（128）= 无系统电池；255 = 状态未知；电量百分比 255 = 无电池
    no_battery = bool(st.BatteryFlag & 128) or st.BatteryFlag == 255
    if no_battery or st.BatteryLifePercent == 255:
        result["battery_present"] = False
        return result

    result["battery_present"] = True
    result["battery_percent"] = st.BatteryLifePercent
    # BatteryLifeTime：秒；0xFFFFFFFF 表示未知或已接通电源
    if st.ACLineStatus == 0 and st.BatteryLifeTime not in (0, 0xFFFFFFFF):
        result["seconds_left"] = st.BatteryLifeTime
    return result


def get_uptime():
    """系统运行时长（GetTickCount64，毫秒）"""
    if not IS_WINDOWS:
        return {"error": "仅支持 Windows"}
    try:
        ms = int(_kernel32.GetTickCount64())
    except Exception:
        return {"error": "无法读取运行时长"}

    total_seconds = ms // 1000
    days, rem = divmod(total_seconds, 86400)
    hours, rem = divmod(rem, 3600)
    minutes, rem = divmod(rem, 60)
    return {
        "days": days,
        "hours": hours,
        "minutes": minutes,
        "total_seconds": total_seconds,
        "readable": f"{days} 天 {hours} 小时 {minutes} 分钟",
    }


# ---------- 报告输出 ----------

def build_report(sample_seconds=0.6):
    report = {
        "generated_at": datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
        "system": get_system_info(),
        "cpu": get_cpu_usage(sample_seconds),
        "memory": get_memory(),
        "disks": get_disks(),
        "battery": get_battery(),
        "uptime": get_uptime(),
    }
    return report


def _collect_tips(report):
    """根据指标生成健康建议"""
    tips = []
    mem = report["memory"]
    cpu = report["cpu"]
    bat = report["battery"]
    up = report["uptime"]

    if "error" not in mem and mem["usage_percent"] > 85:
        tips.append("  内存使用率偏高（>85%），建议关闭多余程序或考虑加装内存")

    for d in report["disks"]:
        if "error" not in d and d["usage_percent"] > 90:
            tips.append(f"  磁盘 {d['drive']} 使用率偏高（{d['usage_percent']}%），建议清理空间")

    if "error" not in cpu and cpu["usage_percent"] > 90:
        tips.append("  CPU 使用率过高（>90%），可能存在异常进程")

    if "error" not in up and up["days"] >= 30:
        tips.append("  系统已连续运行超过 30 天，建议重启以完成更新并释放资源")

    if bat.get("battery_present") and bat.get("ac_status") == "使用电池":
        percent = bat.get("battery_percent", 100)
        if percent <= 20:
            tips.append(f"  电池电量仅剩 {percent}%，建议尽快接通电源")

    if not tips:
        tips.append("  各项指标正常，设备健康状况良好")
    return tips


def format_text(report):
    """格式化中文文本报告"""
    lines = []
    lines.append("=" * 48)
    lines.append("        系统体检报告")
    lines.append(f"  生成时间：{report['generated_at']}")
    lines.append("=" * 48)

    sys_info = report["system"]
    lines.append("\n【系统信息】")
    lines.append(f"  主机名    : {sys_info['hostname']}")
    lines.append(f"  操作系统  : {sys_info['os']}")
    lines.append(f"  系统版本  : {sys_info['os_version']}")
    lines.append(f"  架构      : {sys_info['arch']}")
    lines.append(f"  CPU 型号  : {sys_info['processor']}")
    lines.append(f"  Python    : {sys_info['python']}")

    cpu = report["cpu"]
    if "error" not in cpu:
        lines.append("\n【CPU 使用率】")
        lines.append(f"  当前使用率: {cpu['usage_percent']}% {_bar(cpu['usage_percent'])}")
        lines.append(f"  逻辑核心数: {cpu['cores_logical']}")

    mem = report["memory"]
    if "error" not in mem:
        lines.append("\n【内存】")
        lines.append(f"  总容量    : {mem['total_readable']}")
        lines.append(f"  已使用    : {mem['used_readable']}")
        lines.append(f"  可用      : {mem['available_readable']}")
        lines.append(f"  使用率    : {mem['usage_percent']}% {_bar(mem['usage_percent'])}")

    lines.append("\n【磁盘】")
    for d in report["disks"]:
        if "error" in d:
            if "drive" in d:
                lines.append(f"  {d['drive']}  设备未就绪或无介质（已跳过）")
            else:
                lines.append(f"  读取失败  : {d['error']}")
            continue
        lines.append(f"  {d['drive']}  ({d['drive_type']})")
        lines.append(
            f"    总容量: {d['total_readable']}  已用: {d['used_readable']}  可用: {d['free_readable']}"
        )
        lines.append(f"    使用率: {d['usage_percent']}% {_bar(d['usage_percent'])}")

    bat = report["battery"]
    if "error" not in bat:
        lines.append("\n【电源 / 电池】")
        if bat.get("battery_present"):
            lines.append(f"  电源状态  : {bat['ac_status']}")
            lines.append(f"  剩余电量  : {bat['battery_percent']}%")
            if "seconds_left" in bat:
                hours, minutes = divmod(bat["seconds_left"] // 60, 60)
                lines.append(f"  剩余时间  : 约 {hours} 小时 {minutes} 分钟")
        else:
            lines.append("  当前设备未检测到电池（可能为台式机）")

    up = report["uptime"]
    if "error" not in up:
        lines.append("\n【运行时长】")
        lines.append(f"  已运行    : {up['readable']}")

    lines.append("\n【健康建议】")
    lines.extend(_collect_tips(report))
    lines.append("\n" + "=" * 48)
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description="Windows 系统体检报告生成器（零依赖）")
    parser.add_argument("--json", action="store_true", help="输出 JSON 格式")
    parser.add_argument("--save", action="store_true", help="将报告保存为本地文件")
    parser.add_argument(
        "--sample",
        type=float,
        default=0.6,
        metavar="SECONDS",
        help="CPU 采样时长（秒），默认 0.6",
    )
    args = parser.parse_args()

    _setup_console()

    if not IS_WINDOWS:
        print("本脚本依赖 Windows API，仅支持 Windows 10 / 11。", file=sys.stderr)
        return 2

    _suppress_device_errors()

    sample_seconds = max(0.05, min(10.0, args.sample))
    report = build_report(sample_seconds)

    if args.json:
        output = json.dumps(report, ensure_ascii=False, indent=2)
    else:
        output = format_text(report)

    print(output)

    if args.save:
        suffix = "json" if args.json else "txt"
        fname = f"system_report_{datetime.datetime.now().strftime('%Y%m%d_%H%M%S')}.{suffix}"
        try:
            with open(fname, "w", encoding="utf-8") as f:
                f.write(output)
        except OSError as exc:
            print(f"\n报告保存失败: {exc}", file=sys.stderr)
            return 1
        print(f"\n报告已保存至: {os.path.abspath(fname)}")

    return 0


if __name__ == "__main__":
    sys.exit(main())