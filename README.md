# 日程助手 ScheduleApp

Windows 11 桌面日程工具 · .NET 9 + WPF · 零第三方依赖 · 中文 / English

[中文](#中文) · [English](#english)

---

## 中文

### 功能

**日程与视图**
- 列表 / 周 / 月三种视图，工具条一键切换（`Ctrl+1/2/3`）；列表按「今天 · 明天 · 本周 · 全部」自动分组
- 周视图直接拖动事件块改时间（15 分钟吸附）；月视图把事件块拖到另一天即可改日期
- 拖动重复日程默认只改这一次，按住 `Shift` 平移整个系列；双击空白处在对应时间点新建

**重复与例外**
- 每天 / 工作日 / 每周（可多选星期）/ 每月 / 每年，支持「每 N 个周期」「共 N 次」「截止日期」
- 单次例外：只删除某一次、只修改某一次，都不影响系列其它发生

**提醒**
- 多档提醒（例如 30 / 15 / 5 分钟前 + 准点），可给每档单独写播报文案
- Windows SAPI5 语音播报（音色 / 语速 / 音量 / 模板可调）、提示音、弹窗位置可自定义
- 稍后提醒、标记完成、重播；弹窗位置拖动后自动记住

**导入导出**
- CSV：统一 17 列格式，自动识别 GBK / GB18030 / UTF-8 / UTF-16，导出文件可原样再导入
- iCalendar：`.ics` 导入（含 RRULE / EXDATE / VALARM）与导出
- 导入前预览、列映射、表头诊断；格式不匹配时明确阻止，不会写坏数据

**AI 对话**
- 本机 Ollama 或任意 OpenAI 兼容服务（DeepSeek / OpenAI / 通义千问 / 智谱 / Kimi / OpenRouter / Azure），内置服务预设
- **流式输出**：模型边生成边显示，可随时停止
- 工具调用：直接新建 / 修改 / 删除 / 查询日程，改动即时反映到主界面

**界面**
- 双语：设置 →「外观与默认值 → 界面语言」，可选跟随系统 / 中文 / English（切换后提示重启生效）
- 主题：浅色 / 夜间 / 跟随系统，全部控件自绘，夜间不露白底

**桌面集成**
- 托盘菜单、最小化到托盘、开机启动（注册表，被拦截时自动改用启动文件夹）、桌面 / 开始菜单快捷方式
- 本地接口：`127.0.0.1` 上的 HTTP JSON 接口 + 令牌，供脚本 / AI 直接读写

### 快速开始

需要 Windows 10/11 与 .NET 9 SDK。

```powershell
dotnet build ScheduleApp.sln -c Release -m:1
dotnet run --project src/Schedule.App -c Release
```

发布单文件（框架依赖约几 MB；WPF 不支持裁剪，自包含约 150 MB）：

```powershell
dotnet publish src/Schedule.App/Schedule.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

### 命令行

无界面执行，适合脚本与自测：

| 选项 | 说明 |
| --- | --- |
| `--import <文件>` | 导入 CSV 或 `.ics`，按扩展名 / 内容自动识别 |
| `--export <文件>` | 导出为 CSV 或 `.ics` |
| `--mode append\|upsert\|replace` | 导入模式（默认 append） |
| `--csv-template <文件>` | 导出 CSV 模板 |
| `--serve` | 只跑接口与提醒，不显示主窗口 |
| `--ui-smoke` | 界面冒烟自检：构造全部窗口、截图后退出 |
| `--lang zh\|en\|system` | 临时指定界面语言（不写回设置） |
| `--theme light\|dark\|system` | 临时指定主题 |
| `--help` | 完整参数列表（跟随界面语言） |

### 数据与文件

数据目录默认 `%APPDATA%\ScheduleApp`（可用 `--data` 指定；把 `portable.txt` 放到程序目录则改为程序目录下的 `data`）：

- `schedules.json` / `settings.json`：日程与设置，原子写入并保留滚动备份
- `backups/`：每次保存前的历史副本；文件损坏时自动重命名并自愈
- `api.json`：本地接口地址与令牌；`logs/`：按天滚动的运行日志

### 项目结构

```text
src/Schedule.Core/   业务核心：模型、重复展开、提醒调度、CSV/ICS、存储、本地接口、AI 客户端（无 UI 依赖）
src/Schedule.App/    WPF 界面：窗口、视图模型、服务（语音 / 托盘 / 提醒 / 接口宿主）
tests/               零依赖自测运行器与全部用例
docs/                架构、CSV 格式、AI 工具、开发与本地化说明
samples/             可直接导入的 CSV / ICS 样例
```

### 自测

```powershell
dotnet run --project tests/Schedule.Core.Tests -c Release
```

覆盖日期解析、CSV / ICS 往返、重复规则边界、提醒调度、语音模板、存储自愈、AI 流式解析、双语完整性扫描等（1500+ 项断言，退出码可用于 CI）。

### 已知限制

- 语音依赖系统已安装的 SAPI5 语音包
- 日期选择弹层与系统消息框沿用 Windows 自身主题
- 重复日程的「完成」作用于整个系列
- 本地接口仅监听回环地址、不做 HTTPS 与多用户鉴权
- `GET /api/schema` 的自描述 JSON 固定中文（协议文本，不随界面语言变化）

---

## English

### Features

**Events and views**
- List / Week / Month views (`Ctrl+1/2/3`); the list groups events into Today · Tomorrow · This week · All
- Drag an event block in Week view to change its time (15-minute snap); drag a chip in Month view onto another day to change the date
- Dragging a repeating event moves only that occurrence — hold `Shift` to shift the whole series; double-click empty space to create an event there

**Recurrence and exceptions**
- Daily / weekdays / weekly (any set of days) / monthly / yearly, with “every N periods”, “N times” and “until” limits
- Per-occurrence exceptions: delete just one occurrence or edit just one, without touching the rest of the series

**Reminders**
- Multiple lead times (for example 30 / 15 / 5 minutes before, plus on time), each with its own spoken text
- Windows SAPI5 speech (voice, rate, volume, template), sound, custom popup position, snooze, mark done, replay
- The popup remembers where you dragged it

**Import / export**
- CSV: one fixed 17-column format; GBK / GB18030 / UTF-8 / UTF-16 detected automatically; exported files import back unchanged
- iCalendar: `.ics` import (RRULE / EXDATE / VALARM) and export
- Preview, column mapping and header diagnosis before importing; a mismatched file is blocked instead of writing garbage

**AI chat**
- Local Ollama or any OpenAI-compatible service (DeepSeek / OpenAI / Qwen / Zhipu / Kimi / OpenRouter / Azure) with built-in presets
- **Streaming output**: text appears as the model generates it, and can be stopped at any time
- Tool calls create, update, delete and query events; changes show up in the main window immediately

**Interface**
- Bilingual UI: Settings → “Appearance and defaults → Language” (follow system / 中文 / English; a restart prompt applies it)
- Light / dark / follow-system themes, every control custom-drawn so dark mode never shows a white block

**Desktop integration**
- Tray menu, minimize to tray, start with Windows (registry, with automatic fallback to a Startup-folder shortcut), desktop / Start-menu shortcuts
- Local API: HTTP JSON on `127.0.0.1` with a token, for scripts and AI agents

### Quick start

Windows 10/11 and the .NET 9 SDK:

```powershell
dotnet build ScheduleApp.sln -c Release -m:1
dotnet run --project src/Schedule.App -c Release
```

Single-file publish (framework-dependent is a few MB; WPF cannot be trimmed, so self-contained is about 150 MB):

```powershell
dotnet publish src/Schedule.App/Schedule.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

### Command line

Headless mode, useful for scripts and self-checks:

| Option | Purpose |
| --- | --- |
| `--import <file>` | Import CSV or `.ics`, detected by extension / content |
| `--export <file>` | Export to CSV or `.ics` |
| `--mode append\|upsert\|replace` | Import mode (default append) |
| `--csv-template <file>` | Write a CSV template |
| `--serve` | Run API and reminders only, no main window |
| `--ui-smoke` | UI smoke test: build every window, take screenshots, exit |
| `--lang zh\|en\|system` | Temporary UI language (not saved) |
| `--theme light\|dark\|system` | Temporary theme |
| `--help` | Full option list (follows the UI language) |

### Data and files

The data directory defaults to `%APPDATA%\ScheduleApp` (override with `--data`; drop `portable.txt` next to the executable to use `data` beside it):

- `schedules.json` / `settings.json`: events and settings, written atomically with rolling backups
- `backups/`: a copy before every save; corrupted files are renamed and recovered automatically
- `api.json`: local API address and token; `logs/`: daily rolling logs

### Project layout

```text
src/Schedule.Core/   Business core: models, recurrence, reminder scheduling, CSV/ICS, storage, local API, AI client (no UI)
src/Schedule.App/    WPF UI: windows, view models, services (speech / tray / reminders / API host)
tests/               Zero-dependency test runner and all cases
docs/                Architecture, CSV format, AI tools, development and localization notes
samples/             Ready-to-import CSV / ICS samples
```

### Tests

```powershell
dotnet run --project tests/Schedule.Core.Tests -c Release
```

Covers date parsing, CSV / ICS round-trips, recurrence edge cases, reminder scheduling, voice templates, storage recovery, AI streaming parsing and a bilingual completeness scan (1500+ assertions; the exit code works in CI).

### Known limitations

- Speech needs a SAPI5 voice pack installed in Windows
- The date-picker popup and system message boxes keep the Windows theme
- Marking a repeating event done applies to the whole series
- The local API listens on loopback only and has no HTTPS or multi-user auth
- The `GET /api/schema` self-description is always Chinese (protocol text, independent of the UI language)

---

## 许可 / License

MIT
