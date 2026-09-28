---
name: system-report
description: 对 Windows 电脑进行系统体检并生成体检报告，采集 CPU 使用率/频率/物理核心、物理内存、各磁盘容量、电池容量与充电状态、系统版本与运行时长，并给出健康建议。当用户询问电脑配置、性能状况、内存/磁盘空间占用、电脑卡不卡、是否需要升级硬件、电池/充电状态等问题时使用本技能。零第三方依赖，纯标准库实现。
---

# System Report（系统体检报告）

对 Windows 电脑执行一次快速体检，输出易读的中文报告，包含：

- 系统信息：主机名、操作系统版本、架构、CPU 型号、运行时
- CPU：当前使用率、逻辑核心数、物理核心数、当前频率（含基频）
- 内存：总容量、已使用、可用、使用率
- 磁盘：每个盘符的类型、容量、已用、可用、使用率
- 电源/电池：容量百分比与充电状态分开显示（充电中 / 已充满 / 已接通电源 · 未充电 / 使用电池）
- 运行时长：系统已运行时间
- 健康建议：根据指标给出内存/磁盘/CPU/温区的预警与建议

本技能提供两个等价实现，按场景选一个即可：

| 实现 | 文件 | 依赖 | 适用场景 |
| --- | --- | --- | --- |
| 桌面应用 | `系统体检.exe` / `SysReport.exe`（源码 `SysReport.cs`） | 无（.NET Framework 4.x 为系统自带） | 给人用，双击即开图形界面，体积约 93 KB |
| 命令行脚本 | `system_report.py` | Python 3.7+ 标准库 | 给 agent 调用，输出文本/JSON |

## 桌面应用（推荐）

双击 `系统体检.exe` 直接打开深色仪表盘，不需要安装 Python 或任何运行库。`SysReport.exe` 是同一个程序的英文名副本。

命令行参数：

```text
系统体检.exe                     打开图形界面
系统体检.exe --text              输出文本体检报告
系统体检.exe --json              输出 JSON
系统体检.exe --json --out 文件.json
系统体检.exe --sample 1.5        自定义 CPU 采样秒数（默认 0.6）
系统体检.exe --temp-check 20     空闲/满载对比温度，判断温区是否真跟 CPU 相关（默认 20 秒）
系统体检.exe --stress 200        连续刷新 200 轮，输出内存 / GDI / 句柄变化（排查泄漏用）
系统体检.exe --shot 图.png       离屏渲染一张界面截图，同时写出 图.png.txt 记录几何参数（调试用）
系统体检.exe --scale 1.5         强制界面缩放倍数，配合 --shot 验证高 DPI 布局
```

界面说明：

- 顶部三个圆环：CPU 使用率 / 内存 / 电池；中间一排是各磁盘圆环
- 圆环中间是大号百分比，环内是标签，环下是实际用量（如 `88.1 / 125.2 GB`）
- 颜色阈值：
  - CPU、内存：绿（<75%）、黄（75–90%）、红（≥90%）
  - 磁盘：绿（<70%）、黄（70–85%）、红（≥85%，提醒快满）
  - 电池：按容量判断，低于 20% 红、低于 40% 黄；**插电时改用状态色**（充电中为蓝色，插电未充电为黄色）
- 右上角「重新检测」可重新采样，无需关窗口
- 未就绪的磁盘、无电池的台式机会显示 `--` 并给出说明

## 关于 CPU 温度（重要，不要搞错）

**本软件不显示 CPU 温度，因为 Windows 普通进程读不到它。**

- 真正的 CPU 封装温度在 MSR 里（`IA32_THERM_STATUS` / `IA32_PACKAGE_THERM_STATUS`），读 MSR 需要 ring0，即必须有签名的内核驱动。
- WMI 的 `MSAcpi_ThermalZoneTemperature` 需要管理员权限，而且大量机型直接拒绝访问（标准用户下 `Win32_Processor` 都读不到）。
- 唯一免权限的温度来源是性能计数器 `\Thermal Zone Information(*)\High Precision Temperature`（值为十分之一开尔文，换算 `值/10 - 273.15`），**但它是主板 ACPI 温区，不一定和 CPU 有关**。

所以软件把这行标为「**主板温区（非 CPU）**」，绝不冒充 CPU 温度。实测本机（Dell XPS 13 9350 / i7-6560U）满载 60 秒 CPU 100% 时，该温区稳定在 25.05 °C 一动不动 —— 明显与 CPU 无关。

想知道你机器上的温区到底有没有参考价值，跑一次：

```text
系统体检.exe --temp-check 20
```

它会读空闲温度 → 施加 20 秒满载 → 再读一次，比较变化量：

- 变化 ≥ 3 °C：该温区跟随 CPU 负载，可作粗略参考，软件会照常显示「主板温区」。
- 变化 ≈ 0：该温区与 CPU 无关，别拿它当 CPU 温度。

需要真实 CPU 温度，请用 **HWiNFO / AIDA64** 这类自带签名驱动的工具。注意 OpenHardwareMonitor 系（WinRing0）的驱动已被微软列入易受攻击驱动黑名单，新系统上可能加载失败。

## CPU 频率与核心数

- **频率**：基频读注册表 `HARDWARE\DESCRIPTION\System\CentralProcessor\0` 的 `~MHz`；当前频率 = 基频 × 性能计数器 `\Processor Information(_Total)\% Processor Performance` / 100。这是任务管理器同口径，能反映睿频（例如 2.21 GHz 基频跑到 2.8 GHz）。
- **物理核心**：`GetLogicalProcessorInformation` 统计 `RelationProcessorCore` 条目数（无需 WMI）。界面显示成 `2 核 / 4 线程`。

## 高 DPI 清晰度（重要）

`app.manifest` 声明了 `PerMonitorV2` DPI 感知。**没有它，Windows 会在 125%/150% 缩放（笔记本常见）下把窗口按逻辑分辨率渲染再整体拉伸，界面会糊成"马赛克"。**

程序启动时用 `Graphics.FromHwnd(IntPtr.Zero).DpiX / 96f` 读出缩放倍数，再据此换算窗口宽高、圆环尺寸、间距和圆环线宽（`S()` 辅助函数）。字体用磅值，本身会跟随系统 DPI，无需换算。窗口在不同 DPI 的显示器之间拖动时会触发 `OnDpiChanged`，重新计算缩放并重排。

如果你看到界面模糊，先确认 `app.manifest` 是不是和 exe 一起用 `/win32manifest` 编译进去了。

## 图标

`app.ico` 必须包含**多个尺寸**（16/20/24/32/40/48/64 用 BMP，128/256 用 PNG）。只放一张 256×256 的话，标题栏和任务栏会退回 .NET 默认图标。

需要重新生成图标时：

```text
csc /nologo /target:exe /out:make-icon.exe /r:System.Drawing.dll make-icon.cs
make-icon.exe app.ico
```

程序里还用 `Icon.ExtractAssociatedIcon(Application.ExecutablePath)` 显式设置窗口图标，确保标题栏和任务栏都显示。

## 内存占用排查（--stress）

担心刷新多了会泄漏时，用：

```text
系统体检.exe --stress 200 --sample 0.05 --out stress.txt
```

会连续刷新 200 轮并记录工作集 / 私有内存 / 托管堆 / GDI 对象 / USER 对象 / 句柄数。实测 200 轮后：句柄恒定 645 不变、GDI 无增长、工作集回到起始值、GC 后托管堆回落 —— **没有泄漏**。程序常驻内存约 50 MB。

程序里所有 `Pen` / `Brush` / `StringFormat` / `Bitmap` / `Graphics` 都放在 `using` 中，性能计数器实例是静态缓存复用（不是每次刷新新建），磁盘圆环在数量减少时会 `Dispose`，所以重复刷新不会累积。

## 重新编译

改完 `SysReport.cs` 后：

```text
build.cmd
```

构建使用 Windows 自带的 `csc.exe`（.NET Framework 4.x），无需安装任何 SDK。`build.cmd` 会带上 `app.ico`（图标）和 `app.manifest`（DPI 感知），两者必须与 `SysReport.cs` 放在同一目录。改完记得再复制一份为 `系统体检.exe`。

## 命令行脚本

```bash
python system_report.py                # 输出中文体检报告
python system_report.py --json         # 输出 JSON 格式（便于程序解析）
python system_report.py --save         # 报告同时保存到本地文件
python system_report.py --json --save  # JSON 格式并保存
python system_report.py --sample 1.5   # 自定义 CPU 采样时长（秒，默认 0.6）
python system_report.py --gui          # 打开 tkinter 图形界面
```

## 输出字段（JSON）

- `system`：`hostname` / `os` / `os_version` / `arch` / `processor`（+ 脚本版的 `python`、应用版的 `runtime`）
- `cpu`：`usage_percent` / `cores_logical` / `physical_cores` / `base_mhz` / `current_mhz` / `thermal_zone_celsius`（ACPI 温区，**不是 CPU 温度**，-1 表示读不到）
- `memory`：`total_readable` / `used_readable` / `available_readable` / `usage_percent`
- `disks`：每个盘符的 `drive` / `drive_type` / `ready` / `total_readable` / `used_readable` / `free_readable` / `usage_percent`
- `battery`：`ac_status`（状态文案）/ `ac_online` / `charging` / `battery_present` / `battery_percent`（容量百分比）/ `seconds_left`
- `uptime`：`days` / `hours` / `minutes` / `total_seconds` / `readable`
- `tips`：健康建议字符串数组

## 使用建议

1. **回答用户问题前先跑体检**：当用户问"电脑卡不卡 / 内存够不够 / 磁盘还剩多少 / 要不要升级硬件"时，先执行脚本拿到真实数据，再基于数据回答，不要凭空猜测。
2. **不要拿主板温区当 CPU 温度回答用户**。要谈 CPU 温度就先用 `--temp-check` 确认，或者直接说明普通进程读不到、需要 HWiNFO 这类工具。
3. **关注预警项**：内存使用率 >85%、磁盘使用率 ≥85%、CPU 使用率 >90%、主板温区 ≥85 °C 时，报告会给出对应建议。
4. **电池要看状态而不是只看百分比**：插电时容量低不代表有问题。`ac_online=true` 且 `charging=false` 且容量不高时，通常是电池保护模式（很多笔记本限制充到 60–80%）或充电受限，报告会单独提示。
5. **结合使用场景**：体检数据是"当前状态快照"，判断"是否要升级硬件"时可结合用户日常用途（办公/游戏/剪辑）给出建议。

## 注意事项

- 输出为当前时刻快照，CPU 使用率是采样估算值（默认 0.6 秒），仅供参考；需要更稳定的数值可加大 `--sample`。
- **CPU 使用率是整机（所有逻辑核心）口径**，与任务管理器一致。持续接近 100% 说明机器真的满载，可再用 `Get-Process` 对比两次 `CPU` 字段找出占用进程。
- 系统版本读自注册表 `CurrentBuildNumber`，不依赖 `Environment.OSVersion`（后者会被 Windows 版本伪装成 6.2.9200）。
- 台式机通常无电池，报告中会注明"未检测到电池"。
- 未插介质的光驱 / 空读卡器会以"设备未就绪或无介质（已跳过）"列出，不会卡住或弹窗。
- 控制台中文编码已自动处理；若仍显示乱码，可手动执行 `chcp 65001`。
- 两个实现都只用 Windows API、注册表与性能计数器，**不依赖 WMI**。在 WMI 被策略禁用的账户下（例如标准用户）本技能依然可用，而 `Get-CimInstance Win32_Processor` 之类会直接"拒绝访问"。
- 配套启动脚本：`run.cmd`（Python 文本报告）、`run-gui.cmd`（Python 图形界面）、`系统体检.exe`（独立桌面应用）。

## 文件清单

| 文件 | 说明 |
| --- | --- |
| `系统体检.exe` / `SysReport.exe` | 编译好的桌面应用，双击即用 |
| `SysReport.cs` | 桌面应用全部源码（单文件，C# 5 语法，可用 csc.exe 编译） |
| `app.ico` | 应用图标（9 种尺寸：BMP 16–64，PNG 128/256） |
| `app.manifest` | DPI 感知声明（PerMonitorV2），**必须随 exe 一起编译** |
| `make-icon.cs` | 图标生成器源码（改图标设计时用） |
| `build.cmd` | 一键重新编译脚本 |
| `gui-preview.png` | 界面预览截图（150% 缩放下渲染） |
| `system_report.py` | Python 命令行版本（含 `--gui`） |
| `run.cmd` / `run-gui.cmd` | Python 版本的启动器 |