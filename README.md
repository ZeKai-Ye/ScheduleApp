# 日程助手 ScheduleApp

> Windows 11 桌面日程软件 · CSV 批量导入 · 自定义语音提醒 · 原生轻量实现

一个专注于「**把日程导进来、到点用嘴提醒你**」的桌面小工具。基于 .NET 9 + WPF 原生开发，
**零第三方 NuGet 依赖**，框架依赖模式发布后主程序仅几 MB，启动快、占用低，不打包 Electron / 浏览器内核。

---

## 功能特性

### 日程管理
- 新建 / 编辑 / 删除 / 完成日程，支持全天日程、起止时间、分类、优先级、备注
- 重复规则：不重复 / 每天 / 工作日 / 每周 / 每月 / 每年，可设置重复截止日期
- 主界面按「今天 · 6月3日 星期二」自动分组，支持搜索、分类筛选、6 种时间范围筛选
- 自动保存（每 60 秒 + 关键操作即时落盘），保存时自动滚动备份，JSON 损坏可自愈

### CSV 导入（重点能力）
- **编码自动识别**：UTF-8（含/不含 BOM）、UTF-16、**GBK / GB18030**（中文 Excel 导出常见），
  无需 System.Text.Encoding.CodePages 包，直接走 Win32 API 解码
- **表头自动识别**：中文 / 英文列名自动映射（标题、开始时间、结束时间、日期、分类、优先级、重复、提前提醒、语音内容、铃声、备注、状态…）
- **日期时间宽容解析**：`2025-06-01 09:30`、`2025/6/1 9:30`、`2025年6月1日 下午3点`、`20250601`、
  Excel 序列号、`明天 9:00`、`下周三 14:00`、`3天后`、ISO 8601 等
- **导入向导**：预览前 30 行、手工调整列映射、选择导入方式（追加 / 更新重复项 / 覆盖）
- **导入报告**：新增 / 更新 / 跳过 / 失败计数，失败精确到行号与原因
- 支持导出为 CSV（UTF-8 BOM，Excel 直接打开不乱码）

### 自定义语音提醒
- 到点弹窗 + **语音播报**，播报内容可用模板自定义：
  `{title}` `{time}` `{date}` `{weekday}` `{category}` `{notes}` `{priority}` `{countdown}` `{status}`
- 音色可选（读取 Windows SAPI 语音库）、语速 -10~10、音量 0~100，支持一键试听
- 提示音可换成任意 wav / mp3 / wma / m4a 文件，可选「先响铃再说话」
- 每条日程可单独指定播报文本与铃声，未指定则用全局模板
- 弹窗支持「稍后提醒（1/5/10/30/60 分钟）」「标记完成」「重播语音」

### 桌面集成
- 系统托盘：悬停显示下一次提醒，右键菜单可新建日程 / 查看今天 / 退出
- 最小化到托盘、关闭到托盘、开机自启（注册表 HKCU Run）
- 单实例运行：重复启动会激活已有窗口
- 休眠 / 关机唤醒后不会补弹一堆历史提醒（只补最近 2 小时）

---

## 快速开始

### 环境要求
- Windows 10 / 11（推荐 Windows 11）
- [.NET 9 SDK](https://dotnet.microsoft.com/download)（开发）/ .NET 9 Desktop Runtime（仅运行框架依赖版）
- 无需任何 NuGet 包，**完全离线可构建**

### 构建与运行

```powershell
git clone <仓库地址>
cd 日程软件

# 构建（沙箱 / 多节点受限环境请加 -m:1，详见 docs/DEVELOPMENT.md）
dotnet build ScheduleApp.sln -c Release -m:1

# 运行
.\src\Schedule.App\bin\Release\net9.0-windows\ScheduleApp.exe
```

或使用一键脚本：

```powershell
.\build.ps1              # 构建 + 运行自测
.\build.ps1 -Run         # 构建 + 自测 + 启动程序
.\build.ps1 -Publish     # 发布单文件 exe 到 publish 目录
```

### 发布为轻量单文件

```powershell
# 框架依赖（体积小，需装 .NET 9 桌面运行时）
dotnet publish src/Schedule.App/Schedule.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish

# 自包含（免运行环境，体积大）
dotnet publish src/Schedule.App/Schedule.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

> 首次发布需要联网还原 RID 专用的 apphost 包（Microsoft.NETCore.App.Host.win-x64）。
> 纯离线环境请直接用 build.ps1 构建后运行 bin 目录下的 exe，或先在有网机器上发布一次。

---

## 使用说明

### 1. 导入 CSV
主界面「导入 CSV」→ 选择文件 → 向导会自动识别编码、表头与列含义 → 确认后点「开始导入」。
列名不认识也没关系，可以在向导里手工把「字段」对应到「第 N 列」。
字段与写法详见 [docs/CSV_FORMAT.md](docs/CSV_FORMAT.md)，可直接用 [samples/日程示例-UTF8.csv](samples/日程示例-UTF8.csv) 试跑。

### 2. 配置语音提醒
「设置 → 语音播报」中：

1. 勾选「启用语音播报」
2. 选择音色（可点「刷新」重新读取系统语音库）
3. 调整语速 / 音量，编辑播报模板，点「试听」听效果
4. 「设置 → 提醒」里可指定自定义提示音、稍后提醒默认时长、弹窗自动关闭秒数

默认模板：`{time}，提醒您：{title}。{category}`，实际播报「14:00，提醒您：项目周会。工作」

### 3. 命令行模式（可脚本化 / 无界面）

```text
ScheduleApp.exe --import <csv> [--mode append|upsert|replace] [--encoding auto|utf-8|gbk]
ScheduleApp.exe --export <csv>
ScheduleApp.exe --voices                  # 列出系统语音
ScheduleApp.exe --speak "测试文本"         # 测试语音
ScheduleApp.exe --info                    # 数据目录与统计
ScheduleApp.exe --data <目录>              # 指定数据目录
ScheduleApp.exe --out <文件>               # 输出同时写入文件
```

### 4. 数据存放位置

| 模式 | 位置 |
| --- | --- |
| 默认 | `%APPDATA%\ScheduleApp\`（不可写时自动回退到 `%LOCALAPPDATA%` / 程序目录 `data` / 临时目录） |
| 便携 | 程序目录放一个空文件 `portable.txt`，数据存到 `程序目录\data\` |
| 指定 | 启动参数 `--data D:\日程数据` |

文件：`schedules.json`（日程）、`settings.json`（设置）、`import-history.json`（导入历史）、`backups\`（自动备份）、`logs\`（按天滚动日志）。

---

## 项目结构

```text
ScheduleApp.sln
├─ src/Schedule.Core            纯逻辑层（net9.0，不依赖任何 UI / Windows API）
│   ├─ Models/                  领域模型、设置、导入模型
│   ├─ Csv/                     编码识别、RFC4180 解析、列映射、宽容日期解析、导入/导出
│   ├─ Scheduling/              重复规则展开、提醒调度
│   ├─ Templating/              语音播报模板引擎
│   ├─ Services/                仓储、设置、导入历史、数据校验
│   └─ Storage/                 路径策略、原子写入、JSON 存储
├─ src/Schedule.App             WPF 表现层（net9.0-windows）
│   ├─ Views/                   主窗口、日程编辑、提醒弹窗、CSV 导入向导、设置
│   ├─ ViewModels/              MVVM 视图模型
│   ├─ Services/                SAPI 语音、提示音、托盘、开机自启、提醒宿主
│   ├─ Themes/Theme.xaml        主题与控件样式
│   └─ Cli/                     无界面命令行模式
└─ tests/Schedule.Core.Tests    零依赖自测（209 项断言）
```

详细设计见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。

---

## 自测

```powershell
dotnet run --project tests/Schedule.Core.Tests -c Release
```

覆盖：日期时间解析、CSV 解析 / 编码 / 列映射 / 端到端导入 / 导出往返、重复规则边界（月末压缩、闰年、工作日）、
提醒调度窗口（含提前两天、绝对时间、去重）、语音模板、存储与损坏自愈。

---

## 已知限制
- 语音引擎依赖 Windows SAPI5 语音包；系统未装中文语音时可在「Windows 设置 → 时间和语言 → 语音」添加
- 重复日程的「完成」作用于整个系列，不针对单次发生
- 深色主题已在设置中预留字段，但尚未提供深色配色
- WPF 不支持 PublishTrimmed，自包含发布体积约 150 MB；追求轻量请用框架依赖版

## 路线图
- [ ] 单次发生的例外处理（跳过某一次、仅修改本次）
- [ ] 月历视图与拖拽调整时间
- [ ] 深色主题
- [ ] 多语言界面

## 许可
MIT
