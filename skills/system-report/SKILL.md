---
name: system-report
description: 对 Windows 电脑进行系统体检并生成体检报告，采集 CPU 使用率、物理内存、各磁盘容量、电池/电源状态、系统版本与运行时长，并给出健康建议。当用户询问电脑配置、性能状况、内存/磁盘空间占用、电脑卡不卡、是否需要升级硬件、电池健康等问题时使用本技能。零第三方依赖，纯标准库实现。
---

# System Report（系统体检报告）

对 Windows 电脑执行一次快速体检，输出易读的中文报告，包含：

- 系统信息：主机名、操作系统版本、架构、CPU 型号、Python 版本
- CPU：当前使用率、逻辑核心数
- 内存：总容量、已使用、可用、使用率
- 磁盘：每个盘符的类型、容量、已用、可用、使用率
- 电源/电池：电源状态、剩余电量、剩余续航（笔记本）
- 运行时长：系统已运行时间
- 健康建议：根据指标给出内存/磁盘/CPU/续航的预警与建议

## 环境要求

- 操作系统：Windows 10 / 11
- Python：3.7+（仅标准库，无需 `pip install` 任何包）

## 使用方法

```bash
python system_report.py                # 输出中文体检报告
python system_report.py --json         # 输出 JSON 格式（便于程序解析）
python system_report.py --save         # 报告同时保存到本地文件
python system_report.py --json --save  # JSON 格式并保存
python system_report.py --sample 1.5   # 自定义 CPU 采样时长（秒，默认 0.6）
```

脚本路径：`skills/system-report/scripts/system_report.py`

## 输出字段（JSON）

- `system`：`hostname` / `os` / `os_version` / `arch` / `processor` / `python`
- `cpu`：`usage_percent` / `sample_seconds` / `cores_physical` / `cores_logical`
- `memory`：`total_readable` / `used_readable` / `available_readable` / `usage_percent` 及对应字节数字段
- `disks`：每个盘符的 `drive` / `drive_type` / `ready` / `total_readable` / `used_readable` / `free_readable` / `usage_percent`
- `battery`：`ac_status` / `battery_present` / `battery_percent` / `seconds_left`（使用电池时）
- `uptime`：`days` / `hours` / `minutes` / `total_seconds` / `readable`

## 使用建议

1. **回答用户问题前先跑体检**：当用户问"电脑卡不卡 / 内存够不够 / 磁盘还剩多少 / 要不要升级硬件"时，先执行脚本拿到真实数据，再基于数据回答，不要凭空猜测。
2. **关注预警项**：内存使用率 >85%、磁盘使用率 >90%、CPU 使用率 >90% 时，报告会给出对应建议，回答时优先提示用户这些风险点。
3. **结合使用场景**：体检数据是"当前状态快照"，判断"是否要升级硬件"时可结合用户日常用途（办公/游戏/剪辑）给出建议。

## 注意事项

- 输出为当前时刻快照，CPU 使用率是采样估算值（默认 0.6 秒），仅供参考；需要更稳定的数值可加大 `--sample`。
- 台式机通常无电池，报告中会注明"未检测到电池"。
- 未插介质的光驱 / 空读卡器会以"设备未就绪或无介质（已跳过）"列出，不会卡住或弹窗。
- 控制台中文编码已自动处理；若仍显示乱码，可手动执行 `chcp 65001`。
- 脚本仅使用 Windows API 与注册表读取，不依赖 WMI，因此在 WMI 被禁用或受限的环境下同样可用。