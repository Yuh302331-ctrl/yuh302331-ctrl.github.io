#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
system_report.py — Windows 系统体检报告生成器（纯标准库，零依赖）

采集并输出：系统信息 / CPU / 内存 / 磁盘 / 电池 / 运行时长，
以易读的中文报告形式呈现，也可输出 JSON 供程序调用。

用法：
    python system_report.py            # 输出体检报告
    python system_report.py --json     # 输出 JSON 格式
    python system_report.py --save     # 同时保存报告到 ./system_report_<日期>.txt
"""

import argparse
import ctypes
import datetime
import json
import os
import platform
import string
import sys

# 兼容 GBK 控制台：输出编码异常时不崩溃
try:
    sys.stdout.reconfigure(errors="replace")
except Exception:
    pass


# ---------- Windows API 封装（ctypes，零依赖） ----------

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
        ("ACLineStatus", ctypes.c_byte),
        ("BatteryFlag", ctypes.c_byte),
        ("BatteryLifePercent", ctypes.c_byte),
        ("SystemStatusFlag", ctypes.c_byte),
        ("BatteryLifeTime", ctypes.c_ulong),
        ("BatteryFullLifeTime", ctypes.c_ulong),
    ]


def _gb_to_readable(num):
    """字节数转可读字符串"""
    gb = num / (1024 ** 3)
    if gb >= 1:
        return f"{gb:.1f} GB"
    mb = num / (1024 ** 2)
    return f"{mb:.0f} MB"


def _pct(used, total):
    if total <= 0:
        return 0.0
    return round(used / total * 100, 1)


def get_system_info():
    """系统与硬件基础信息"""
    uname = platform.uname()
    return {
        "hostname": uname.node,
        "os": f"{uname.system} {uname.release}",
        "os_version": platform.version(),
        "arch": uname.machine,
        "processor": platform.processor() or uname.processor,
        "python": platform.python_version(),
    }


def get_memory():
    """物理内存信息（GlobalMemoryStatusEx）"""
    st = MEMORYSTATUSEX()
    st.dwLength = ctypes.sizeof(MEMORYSTATUSEX)
    if not ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(st)):
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
    kernel32 = ctypes.windll.kernel32

    class FILETIME(ctypes.Structure):
        _fields_ = [("dwLowDateTime", ctypes.c_ulong), ("dwHighDateTime", ctypes.c_ulong)]

    def _sample():
        idle, kernel, user = FILETIME(), FILETIME(), FILETIME()
        if not kernel32.GetSystemTimes(
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
    import time

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
    return {"usage_percent": round(usage, 1), "cores_physical": os.cpu_count() or 0}


def get_disks():
    """所有逻辑盘符的容量信息（GetDiskFreeSpaceExW）"""
    kernel32 = ctypes.windll.kernel32
    drives_bitmask = kernel32.GetLogicalDrives()
    if drives_bitmask == 0:
        return [{"error": "无法读取磁盘信息"}]

    disks = []
    for i, letter in enumerate(string.ascii_uppercase):
        if drives_bitmask & (1 << i):
            root = f"{letter}:\\"
            free = ctypes.c_ulonglong(0)
            total = ctypes.c_ulonglong(0)
            ok = kernel32.GetDiskFreeSpaceExW(
                root,
                ctypes.byref(ctypes.c_ulonglong(0)),
                ctypes.byref(total),
                ctypes.byref(free),
            )
            if ok:
                used = total.value - free.value
                disks.append(
                    {
                        "drive": root,
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
    st = SYSTEM_POWER_STATUS()
    if not ctypes.windll.kernel32.GetSystemPowerStatus(ctypes.byref(st)):
        return {"error": "无法读取电源状态"}

    aclines = {0: "使用电池", 1: "已接通电源", 255: "未知"}
    result = {
        "ac_status": aclines.get(st.ACLineStatus, "未知"),
        "battery_percent": st.BatteryLifePercent,
        "battery_flag": st.BatteryFlag,
    }
    # 255 表示无电池或未知
    if st.BatteryLifePercent == 255:
        result["battery_present"] = False
        result.pop("battery_percent", None)
    else:
        result["battery_present"] = True
    return result


def get_uptime():
    """系统运行时长（GetTickCount64，毫秒）"""
    try:
        ms = ctypes.windll.kernel32.GetTickCount64()
        seconds = ms // 1000
        days, rem = divmod(seconds, 86400)
        hours, rem = divmod(rem, 3600)
        minutes = rem // 60
        return {"days": days, "hours": hours, "minutes": minutes}
    except Exception:
        return {"error": "无法读取运行时长"}


# ---------- 报告输出 ----------

def build_report():
    report = {
        "generated_at": datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
        "system": get_system_info(),
        "cpu": get_cpu_usage(),
        "memory": get_memory(),
        "disks": get_disks(),
        "battery": get_battery(),
        "uptime": get_uptime(),
    }
    return report


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
        lines.append(f"  当前使用率: {cpu['usage_percent']}%")
        lines.append(f"  逻辑核心数: {cpu['cores_physical']}")

    mem = report["memory"]
    if "error" not in mem:
        lines.append("\n【内存】")
        lines.append(f"  总容量    : {mem['total_readable']}")
        lines.append(f"  已使用    : {mem['used_readable']}")
        lines.append(f"  可用      : {mem['available_readable']}")
        lines.append(f"  使用率    : {mem['usage_percent']}%")

    lines.append("\n【磁盘】")
    for d in report["disks"]:
        if "error" in d:
            lines.append(f"  读取失败  : {d['error']}")
            continue
        lines.append(f"  {d['drive']}")
        lines.append(f"    总容量: {d['total_readable']}  已用: {d['used_readable']}  可用: {d['free_readable']}")
        lines.append(f"    使用率: {d['usage_percent']}%")

    bat = report["battery"]
    if "error" not in bat:
        lines.append("\n【电源 / 电池】")
        if bat.get("battery_present"):
            lines.append(f"  电源状态  : {bat['ac_status']}")
            lines.append(f"  剩余电量  : {bat['battery_percent']}%")
        else:
            lines.append("  当前设备未检测到电池（可能为台式机）")

    up = report["uptime"]
    if "error" not in up:
        lines.append("\n【运行时长】")
        lines.append(f"  已运行    : {up['days']} 天 {up['hours']} 小时 {up['minutes']} 分钟")

    # 简单健康建议
    lines.append("\n【健康建议】")
    tips = []
    if "error" not in mem and mem["usage_percent"] > 85:
        tips.append("  内存使用率偏高（>85%），建议关闭多余程序或考虑加装内存")
    for d in report["disks"]:
        if "error" not in d and d["usage_percent"] > 90:
            tips.append(f"  磁盘 {d['drive']} 使用率偏高（{d['usage_percent']}%），建议清理空间")
    if "error" not in cpu and cpu["usage_percent"] > 90:
        tips.append("  CPU 使用率过高（>90%），可能存在异常进程")
    if not tips:
        tips.append("  各项指标正常，设备健康状况良好")
    lines.extend(tips)
    lines.append("\n" + "=" * 48)
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description="Windows 系统体检报告生成器（零依赖）")
    parser.add_argument("--json", action="store_true", help="输出 JSON 格式")
    parser.add_argument("--save", action="store_true", help="将报告保存为本地文件")
    args = parser.parse_args()

    report = build_report()

    if args.json:
        output = json.dumps(report, ensure_ascii=False, indent=2)
    else:
        output = format_text(report)

    print(output)

    if args.save:
        suffix = "json" if args.json else "txt"
        fname = f"system_report_{datetime.datetime.now().strftime('%Y%m%d_%H%M%S')}.{suffix}"
        with open(fname, "w", encoding="utf-8") as f:
            f.write(output)
        print(f"\n报告已保存至: {os.path.abspath(fname)}")


if __name__ == "__main__":
    main()
