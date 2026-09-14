# 开发指南

## 环境
- Windows 10/11
- .NET 9 SDK（`dotnet --list-sdks` 应包含 9.x）
- 无需 NuGet 包；离线环境可直接构建

## 构建

```powershell
dotnet build ScheduleApp.sln -c Release -m:1
```

> **为什么建议加 `-m:1`？**
> MSBuild 多节点构建会在工作进程之间创建命名管道。在受限沙箱 / 某些受限终端环境中命名管道被拒绝，
> 表现为「生成失败。0 个警告 0 个错误」这种没有任何错误明细的静默失败。
> 强制单节点（`-m:1`）后构建在进程内完成，问题消失。普通桌面环境不加也能正常构建。

只构建单个项目不受影响：

```powershell
dotnet build src/Schedule.App/Schedule.App.csproj -c Release
```

## 自测

```powershell
dotnet run --project tests/Schedule.Core.Tests -c Release
```

- 自研零依赖测试运行器（离线环境装不了 xunit / nunit）
- 输出每项断言结果，全部通过时退出码 0，可直接接入 CI：

```powershell
if ($LASTEXITCODE -ne 0) { throw "自测失败" }
```

## 一键脚本

```powershell
.\build.ps1                 # 清理 + 构建 + 自测
.\build.ps1 -Run            # 构建 + 自测 + 启动程序
.\build.ps1 -Publish        # 额外发布单文件到 publish/
.\build.ps1 -SelfContained  # 与 -Publish 搭配，发布自包含版本
```

## 调试技巧

| 目的 | 做法 |
| --- | --- |
| 使用独立数据目录 | `ScheduleApp.exe --data D:\temp\scheddata` |
| 不启动界面验证导入 | `ScheduleApp.exe --data ... --import x.csv --out result.txt` |
| 检查语音引擎 | `ScheduleApp.exe --voices` |
| 查看运行时日志 | 数据目录下 `logs\app-yyyyMMdd.log` |
| 缩短提醒轮询间隔 | `ReminderHost.IntervalSeconds` 设为 5 秒 |
| 诊断 WPF 绑定错误 | 设环境变量 `SCHEDULEAPP_BINDING_TRACE=1`（或 `all`）后启动，日志目录生成 `binding-*.log` |
| bin 被正在运行的实例占用 | 用 `dotnet build ScheduleApp.sln -c Release -m:1 -p:BaseOutputPath=<其它目录>` 把产物挪走，不用结束用户进程 |
| 并行运行多个实例 | 用不同的 `--data` 目录（单实例互斥体按数据目录隔离） |
| 调试本地接口 | `ScheduleApp.exe --serve --port 43121 --data D:\tmp\api`，令牌在 `<数据目录>\api.json` |
| 查看接口自描述 | `curl -H "X-Api-Token: …" http://127.0.0.1:43121/api/schema` |
| 接口代码位置 | `src/Schedule.Core/Api/`（服务器 + 路由，可被自测覆盖）与 `src/Schedule.App/Services/ApiHost.cs`（宿主 + 线程切换） |
| 测试快捷方式生成 | `ScheduleApp.exe --shortcut desktop --shortcut-dir .\tmp-lnk`（写进工作区，不污染桌面） |
| 测试开机启动 | `ScheduleApp.exe --startup on`，再用 `--info` 看落地方式（注册表 / 启动文件夹） |
| 验证弹窗位置 | 设置页「试一次位置」，或 `curl -X POST .../api/reminders/test` |
| 用夜间主题跑一遍 | `ScheduleApp.exe --theme dark`（临时覆盖，不写回设置） |
| 给界面截图核对 | `ScheduleApp.exe --ui-smoke --theme dark` → 数据目录 `logs\ui-*.png`（含列表 / 周 / 月 / 设置 / 编辑 / 提醒 / 改期共 7 张） |
| 只改配色 | `src/Schedule.App/Themes/Palette.Light.xaml` / `Palette.Dark.xaml`：**两份键集合必须完全一致**，外观改动优先在这里做 |
| 改控件外观 | `src/Schedule.App/Themes/Theme.xaml`：一律用自绘 `ControlTemplate` + `DynamicResource`，别用系统默认模板（夜间会露白底） |
| 布局回归 | `--ui-smoke` 会断言：列表列宽合计 ≤ 可视宽度且横向滚动可用、编辑窗口自定义颜色框不越界、设置页最右列不越界、写进设置的自定义弹窗坐标必须真的生效 |
| 验证重启命令 | `--ui-smoke` 会打印 `RESTART 命令=...`（含数据目录与 `--restart-of`），并断言新进程用的是同一份数据、等待的是本进程 PID；自检**不会**真的重启 |
| 主题即时生效的边界 | 控件画刷走 `DynamicResource`；视图模型算出来的画刷要靠 `AppHost.ThemeChanged` 触发重建，否则要重启才换色 |

## 代码约定
- `Nullable` 与 `ImplicitUsings` 全开，构建保持 **0 警告**
- `Schedule.App` 同时启用 WPF 与 WinForms，已在 csproj 中移除 WinForms 的隐式 using 以避免类型二义性；
  使用时显式 `using System.Windows.Forms;` / `using System.Drawing;`
- Core 层禁止引用任何 UI / Windows 专有类型（`NativeEncoding` 的 P/Invoke 是唯一例外，且有降级路径）
- 新增业务逻辑优先写进 Core 并补自测，界面层只做编排与展示

## 发布

```powershell
# 框架依赖单文件（约几 MB，需 .NET 9 桌面运行时）
dotnet publish src/Schedule.App/Schedule.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish

# 自包含单文件（约 150 MB，免运行环境；WPF 不支持 PublishTrimmed）
dotnet publish src/Schedule.App/Schedule.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

> **离线注意**：dotnet publish -r <RID> 首次需要联网还原 RID 专用的 apphost 包
> （Microsoft.NETCore.App.Host.win-x64）。完全离线的机器上，请改用 build.ps1 构建后直接运行
> src/Schedule.App/bin/Release/net9.0-windows/ScheduleApp.exe。

## 目录速查

```text
src/Schedule.Core/Csv/              导入导出的全部逻辑（编码、解析、映射、宽容日期）
src/Schedule.Core/Scheduling/       重复展开与提醒调度（纯函数，最值得写测试的地方）
src/Schedule.App/Services/          SAPI 语音、提示音、托盘、开机自启、提醒宿主
src/Schedule.App/Views/             5 个窗口的 XAML 与事件处理
src/Schedule.App/Themes/Theme.xaml  全部配色与控件样式（改主题从这里入手）
tests/Schedule.Core.Tests/Cases/    按主题拆分的测试用例
```
