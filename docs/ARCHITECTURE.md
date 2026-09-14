# 架构设计

## 设计目标
1. **轻量**：不使用 Electron / 浏览器内核，不引入任何 NuGet 包，框架依赖发布后主程序几 MB
2. **可测**：核心业务逻辑 100% 位于不依赖 UI 的 Core 层，可在无界面环境下自测
3. **健壮**：数据写入原子化 + 自动备份 + 损坏自愈；受限环境下数据目录自动回退
4. **可扩展**：语音、存储、导入策略都通过接口或纯函数隔离，便于替换实现

## 分层

```text
┌──────────────────────────── Schedule.App (WPF, net9.0-windows) ─────────────────────────────┐
│ Views (XAML)      MainWindow / ItemEditorWindow / ReminderWindow / CsvImportWindow / Settings│
│ ViewModels        MainViewModel（筛选、展开、统计）                                           │
│ Services          SapiVoiceService · AudioPlayerService · TrayService · StartupService        │
│                   ReminderHost（定时调度 + 播报 + 弹窗）                                       │
│ AppHost           组合根：统一创建/释放所有服务，界面只通过它访问                                 │
│ Program           入口：命令行分流 → 单实例 → 全局异常兜底                                       │
└───────────────────────────────────────┬─────────────────────────────────────────────────────┘
                                        │ ProjectReference（单向依赖）
┌───────────────────────────────────────┴──────────── Schedule.Core (net9.0, 无 UI 依赖) ──────┐
│ Models        ScheduleItem · AppSettings · ScheduleOccurrence · VoiceDescriptor · ImportModels │
│ Csv           CsvEncodingDetector（Win32 CP936/54936 解码）· CsvReader（RFC4180）              │
│               CsvColumnMap（两轮打分映射）· FlexibleDateTime（宽容解析）· FieldParsers          │
│               CsvImportService（预览/导入/去重/报告）· CsvExporter                             │
│ Scheduling    OccurrenceCalculator（重复展开 + 快速定位）· ReminderPlanner（窗口调度）           │
│ Templating    VoiceTemplate（占位符渲染 + 标点清理）                                            │
│ Services      ScheduleRepository · SettingsService · ImportHistoryService · 校验/修复           │
│ Storage       AppPaths（多级回退）· AtomicFile（原子写 + 滚动备份）· JsonStore                   │
│ Logging       AppLog（按天滚动，永不抛异常）                                                    │
└──────────────────────────────────────────────────────────────────────────────────────────────┘
```

依赖方向严格单向：App → Core。Core 不引用 `System.Windows.*`、`System.Windows.Forms`、`System.Drawing`，
因此可以在控制台 / 服务 / 未来的 CLI 中复用。

## 关键机制

### 1. 提醒调度模型（时间窗口 + 多档位 + 已触发标记）
一条日程可以有多个**提醒档位**（`ReminderOffsets`，例如 30 / 15 / 5 / 0 分钟），每个档位各自触发：

- **档位展开**：`ScheduleItem.ComputeReminders(occurrenceStart)` 把档位换算成具体触发时刻（`ReminderPlan`），
  返回按时间升序的计划列表；绝对提醒时间（`RemindAtOverride`）与开始时间解耦，需要单独纳入
- **时间窗口**：`ReminderHost` 每 15 秒调用一次 `ReminderPlanner.CollectDue(items, lastCheck, now)`，
  只处理 `(lastCheck, now]` 区间，程序重启 / 休眠唤醒都不会漏提醒
- **窗口扩张**：某档位的触发时刻 = 发生时间 − 提前量，因此展开区间要按**最大档位**向两端扩张，
  否则「提前 1 天」的档位永远命中不了
- **去重**：每条日程用 `FiredReminders` 记录「(发生时间, 档位) → 触发时刻」，
  因此 30 分钟档触发后不会影响 5 分钟档；旧字段 `LastFiredOccurrence` 在加载时会被迁移成等价记录
- **剪枝不依赖机器时钟**：`PruneFiredReminders` 以「最近一次触发对应的发生时间」为基准，
  而不是 `DateTime.Now`——否则日程时间与系统时间相差较大时，刚写入的记录会被立即删除而重复提醒
- **休眠保护**：时间跳跃超过 2 小时时只补发最近 2 小时，避免唤醒后弹窗轰炸
- **稍后提醒**：内存中的待触发队列，到点后重新走「提示音 → 语音 → 弹窗」流程

### 2. 周期事件展开（OccurrenceCalculator）
- 规则：每天 / 工作日 / 每周（可**多选星期**）/ 每月 / 每年，另加 `RepeatInterval`（每 N 个周期）与
  `RepeatCount`（共 N 次）、`RepeatUntil`（截止日期，谁先到用谁）
- 全部基于基准时间做 `AddDays/AddMonths/AddYears`，天然处理月末压缩（1 月 31 日 → 2 月 28 日）
  与闰年（2 月 29 日 → 2 月 28 日）
- **快速定位**：按月 / 周 / 天估算起始下标，避免「2000 年开始的每日日程」展开时线性空转
  （自测里有 2000 条日程 + 25 年历史的性能断言）；仅当设置了 `RepeatCount` 时退化为顺序生成——
  此时总量有上界，正确性优先
- 每周多选星期走独立的「按周推进」通道，并保证不产生早于开始时间的实例
- 每次发生继承基准时长，且独立计算全部提醒档位

### 3. CSV 编码识别（零依赖）
.NET Core 默认不含 GBK 代码页表，而中文 Excel 导出的 CSV 大量使用 GBK。本项目的做法：

1. BOM 优先（UTF-8 / UTF-16 LE|BE / UTF-32）
2. 无 BOM 时用**严格 UTF-8** 解码（`throwOnInvalidBytes`），成功即 UTF-8
3. 失败则用 `MultiByteToWideChar` 按 **GB18030（54936）** 解码，GBK 是其子集，可无损还原
4. 全部失败再退回 UTF-8 替换非法字节，并标记 `IsFallback`

同时提供 `WideCharToMultiByte` 反向编码能力（导出 GBK / 自测构造 GBK 样本）。

### 4. 列映射打分（两轮）
表头 → 字段的自动识别采用别名打分：完全相等 100、前后缀 60、包含 40、反向包含 30。

**第一轮只接受 ≥60 的强匹配，第二轮才放开弱匹配**。这样「日期」列不会被「开始日期」这类
弱包含规则提前抢走（这是自测暴露出的真实 bug，已修复并有回归用例）。

### 5. 数据持久化
- `AtomicFile`：先写 `.tmp` 并 `Flush(flushToDisk: true)`，再 `File.Move(overwrite: true)` 整体替换
- 每次覆盖前把旧文件复制到 `backups/`，按数量滚动清理
- 读取失败时把损坏文件重命名为 `*.corrupt-yyyyMMddHHmmss` 并返回空集合，程序照常启动
- 加载时 `ScheduleItemValidator.Normalize` 修复历史脏数据（空标题、结束早于开始、负提前量等）

### 6. 语音与提示音
- 语音：`Type.GetTypeFromProgID("SAPI.SpVoice")` + `dynamic` 延迟绑定 COM，避免 System.Speech 包依赖；
  支持枚举音色（LANGID → 可读语言名）、设置音色 / 语速 / 音量、异步播报（不阻塞 UI）与停播
- 提示音：WPF `MediaPlayer` 播放自定义音频，未指定文件时退回系统提示音
- 顺序：先响铃 → 延迟 1.6 秒 → 朗读（可配置）

### 7. 启动流程与容错

```text
Main
 ├─ 解析命令行 → 有 --import/--export/--voices/--speak/--info 则走 CliRunner（不启动界面）
 ├─ AppPaths.Initialize：首选目录不可写 → 依次回退 %LOCALAPPDATA% → 程序目录\data → 临时目录
 ├─ 单实例 Mutex；已有实例则通过命名事件激活其窗口后退出
 ├─ 注册 AppDomain / Dispatcher 全局异常处理（记录日志 + 提示，不让进程静默退出）
 └─ AppHost.Start：装配仓储/设置/语音/音频/提醒/托盘/主窗口 + 60 秒自动保存
```

### 8. 日历布局引擎（周 / 月）
`Scheduling/CalendarLayout.cs` 是纯计算模块（无任何 UI 依赖，因而可被自测覆盖）：

- `BuildMonth(items, year, month, today, capacity, weekStartsOnMonday)`：固定 **6 × 7 = 42 格**，
  从当月 1 日所在周的周一开始；每格收集当天发生（全天在前、其余按开始时间升序），
  并给出 `VisibleCount` / `HiddenCount` 供界面折叠成「+N」
- `BuildWeek(items, weekStart, today, minMinutes, maxMinutes)`：跨天事件**按天切块**（结束时刻恰为
  0 点归前一天），再对同一天的时间块做**重叠分列**：先找传递性重叠簇，再贪心分配列号，
  得到每块的 `Column / ColumnCount`；界面据此计算像素位置与宽度
- 展开只做一次（`OccurrenceCalculator.Expand` 覆盖整个可见区间），避免逐格重复展开

### 9. 事件配色
- 解析顺序：日程自带 `Color` → `CategoryColors` 分类映射 → 按分类名 **FNV-1a 稳定散列** 取调色板
- 刻意不用 `string.GetHashCode()`：它在 .NET Core 中每个进程种子不同，会导致同一条日程每次启动换颜色
- 界面侧统一由 `Mvvm/BrushCache` 产出**冻结画刷**并缓存（实心 / 半透明两种），
  因此 XAML 里不需要任何颜色转换器

### 10. 本地接口（供 AI / 脚本访问）
- `Api/MiniHttpServer.cs`：基于 `TcpListener` 的极简 HTTP/1.1 服务器（只绑定回环地址）。
  刻意不用 `HttpListener`——后者需要 urlacl 或管理员权限，而 TcpListener 普通用户即可监听。
  支持请求行 / 请求头 / `Content-Length` 正文与 `Connection: close`；分块传输明确回 411
- `Api/ScheduleApi.cs`：路由 + DTO + **自描述 schema**，纯逻辑、不依赖 WPF，因此能被自测覆盖
  （测试里真的起服务器、真的发 HTTP 请求，覆盖鉴权、CRUD、PUT/PATCH 语义、导入导出与错误码）
- `Api/IApiActions`：把语音与「立即触发提醒」这类宿主能力抽象出来，Core 不反向依赖 App；
  测试用 `NullApiActions`
- App 侧 `Services/ApiHost.cs`：读取 / 生成令牌、启动监听、写 `api.json` 发现文件，
  并把**每个请求都切到 UI 线程**执行——仓储、`MediaPlayer` 与 SAPI 都要求同线程访问
- 令牌比较使用固定时间实现，避免时序侧信道；未识别字段通过响应头回传，便于 AI 发现字段拼写错误

### 11. 分档播报的解析链
`ScheduleItem.ResolveVoiceTemplate(offset, settings)` 按「越具体越优先」返回模板：

1. `ReminderVoiceTexts[档位]`（本日程 · 该档位）
2. `VoiceText`（本日程 · 通用文案）
3. `settings.OffsetVoiceTemplates[档位]`（全局 · 该档位文案）
4. `settings.VoiceTemplate`（全局模板）

铃声同理（`ReminderSoundFiles[档位]` → `SoundFile` → `DefaultSoundFile`）。
`DescribeVoiceSource` 会返回命中来源，因此界面、弹窗与接口都能告诉用户「这句话是哪儿来的」，
避免「改了设置却没生效」这类难以排查的疑惑。

### 12. 桌面集成（快捷方式 / 开机启动）
- `Services/ShortcutService.cs`：用 COM 的 `WScript.Shell` 生成 .lnk，零第三方依赖；
  统一收口桌面 / 开始菜单 / 启动文件夹三个位置，并支持 `directoryOverride`（调试与便携场景）
- `Services/StartupService.cs`：**两级落地**——先写 `HKCU\\...\\Run`，被拒时回退为启动文件夹快捷方式；
  `Detect()` 读的是**系统真实状态**而不是设置文件，因此界面能显示「设置说启用了但注册表里没有」这类不一致；
  程序启动时 `SyncAutostartSetting` 会把设置与系统对齐（路径或参数变化时重写一次）
- 开机启动命令 = `"exe" + 参数`；参数由 `StartMinimized` 与 `StartupArguments` 组合而来

### 13. 提醒弹窗定位
- `ReminderPositionMath.Compute` 是纯函数：输入位置策略、偏移、窗口尺寸、工作区矩形（含级联下标），
  输出左上角坐标并**夹取到可见区域**（副屏负坐标、任务栏占位、窗口比工作区还大都能正确处理），
  因此在 Core 测试里被完整覆盖
- WinForms 的 `Screen.WorkingArea` 是**设备像素**，而 WPF 的 `Left/Top` 是设备无关单位；
  `ReminderPositioner` 统一在设备像素下计算，再按 `VisualTreeHelper.GetDpi` 的缩放比例换算回去——
  否则 125% / 150% 缩放下弹窗会明显偏移
- 默认跟随鼠标所在显示器（`ReminderFollowCursorScreen`），可切换为固定主屏
- **X / Y 一律以设备无关单位（DIP）保存**（与窗口 `Left/Top` 同单位），`ReminderPositionMath.DipsToPixels`
  在定位时换算；两侧单位不一致正是"拖动后位置记不住"的根因
- 自定义坐标以**整个虚拟桌面**（`SystemInformation.VirtualScreen`）为界，而不是单块屏幕的工作区，
  否则把弹窗拖到第二块屏幕后会被夹回主屏
- 拖动弹窗后由 `AppHost.RememberReminderPosition` 写回 `Custom` 坐标（`ReminderRememberPosition` 可关闭）；
  `ReminderWindow` 在 `ContentRendered` 之后才开始监听 `LocationChanged`，并把与定位结果相差 < 1.5 单位的变化
  视为非拖动，否则 WPF 首次显示的延迟定位会被误判成用户拖动；保存前延迟 600ms（拖动过程不再连续落盘）
- 位置预览走 `ReminderHost.ShowPositionPreview`，只弹窗不发声

### 14. 系列事件的「仅本次」例外
- 数据模型（RFC 5545 RECURRENCE-ID 思路）：
  `ScheduleItem.ExcludedOccurrences`（被单独删除的那几次）+ `OccurrenceOverrides`（被单独修改的那几次，
  含 `NewStart/NewEnd` 与标题 / 备注 / 分类 / 颜色 / 播报文案 / 提醒档位 / 状态等可选字段）。
  定位键始终是那一次的**原始**开始时间，因此改期后依然对得上
- 展开时（`OccurrenceCalculator.Create`）把覆盖套用到系列的**副本**上，并把系列本体放进
  `ScheduleOccurrence.SeriesRef`。这样界面与播报拿到的是「有效日程」，而提醒去重（`FiredReminders`）
  与持久化仍落在系列本体上——否则改期后的提醒会因为副本上的记录无法保存而反复触发
- **`ExpandWithOverrides` 是必需的**：展开按原始开始时间枚举，一次 6/9 的发生被改到 6/16 后，
  查询 6/16 那一周的窗口枚举不到它，提醒与日历都会凭空少一条。该方法把「被改期到本区间内」的本次补进来；
  提醒调度、`CalendarLayout`、列表与接口统一使用它
- 改期整个系列时用 `ScheduleItem.Shift`：开始 / 结束 / 重复截止 / 绝对提醒一起平移，
  已记录的例外键也按同一偏移量搬走，避免例外全部失配
- 界面侧：编辑窗口的「修改范围」把同一个表单复用为「仅本次」与「整个系列」两条路径；
  周视图拖动改期走纯像素换算（`deltaY ÷ 每小时高度 × 60` 后按 15 分钟吸附，阈值 8 像素避免误触），
  Shift 表示整个系列
- **联网（云端）支持**：同一个 OpenAI 兼容客户端既连本地也连云端——
  鉴权按 `AiAuthMode`（Bearer / api-key 头 / 不带）、密钥支持 `env:VAR` 间接引用（`AiSecrets`）、
  完整端点地址（含 `/chat/completions` 与查询串）原样使用、代理可跟随系统或自填。
  服务清单是纯数据 `AiProviderPresets`（本地/云端、是否需密钥、鉴权方式、提示语），可整体自测；
  `AiEndpoint` 负责"本地还是云端""像不像推理模型"的判断，从而决定要不要发 `reasoning_effort`
  （云端服务端遇到不认识的字段容易 400）

- **不变量：非重复日程不得携带例外**。`Repeat == None` 时 `occurrenceOverrides` 没有任何意义，
  却会让 `ScheduleItemValidator.Validate` 直接拒绝保存（"非重复日程不应带有「仅本次」例外"）。
  这条不变量由三处共同保证：`MainViewModel.Reschedule` / `ApplyOccurrenceEdit` 对非重复日程一律走整体分支、
  `ScheduleItemValidator.DropOccurrenceEditsWhenNotRepeating` 在编辑窗口保存前与仓库 `Load`（经 `Normalize`）
  时自愈、界面层（周视图拖动、改期对话框）在 `!IsSeries` 时不传「仅这一次」

### 15. 主题机制（浅色 / 夜间）
- 调色板独立成字典：`Themes/Palette.Light.xaml` 与 `Themes/Palette.Dark.xaml`，**37 个键一一对应**
  （背景 / 卡片 / 输入框 / 边框 / 滚动条 / 菜单 / 提示气泡 / 滑块 / 分隔线 / 语义色）；
  `Themes/Theme.xaml` 通过 `MergedDictionaries` 引入其中一个，所有样式都用 **DynamicResource** 引用画刷，
  切换时只替换那一个字典，界面即时更新、无需重建窗口
- `Services/ThemeService.Apply` 定位 `Theme.xaml.MergedDictionaries[0]` 并整块替换；
  `AppTheme.System` 读注册表 `AppsUseLightTheme`。设置页改选即预览，取消时回滚
- 代码侧（视图模型算出来的画刷）用 `Mvvm/ThemeBrushes` 从应用资源取当前色，
  取不到再退回浅色常量；主题变化会触发 `MainViewModel.Refresh` 重建卡片集合，因此颜色跟着刷新
- **踩过的坑**：WPF 隐式样式按**精确类型**匹配，`Style TargetType="Window"` 不会作用于 `MainWindow`
  这类派生窗口。此前窗口背景 / 字体 / TextFormattingMode 因此一直没生效（浅色下看不出来，夜间就暴露了）。
  现在样式带 `x:Key="AppWindow"`，6 个窗口显式引用
- 自绘范围：输入框 / 下拉框（含弹出层）/ 复选框 / 单选按钮 / 日期选择 / 选项卡 / 滚动条 / 右键菜单 /
  菜单分隔线 / 提示气泡 / 滑块 / 卡片 / 列表头 / 树状分组。**输入框刻意比卡片更暗**，
  形成"凹陷"层次，否则夜间模式下满屏白框
- **即时生效的边界**：`DynamicResource` 只能刷新控件自身的画刷；视图模型里`ThemeBrushes` 已经取过色的
  `Brush` 对象（日历色块、优先级颜色）必须重建视图模型才换色。所以设置页改选主题时会
  `AppHost.NotifyThemeChanged()` → `MainWindow` 重建列表 / 日历集合；再彻底一点就重启进程
  （见 `RestartPlan`）
- 仍然跟随系统主题的只有：`DatePicker` / `ComboBox` 的日历与弹出层里的系统部件、系统消息框
  （`MessageBox` 由 Win32 绘制，WPF 无法覆盖）
- **踩过的坑**：给 `ScrollViewer` 打开 `HorizontalScrollBarVisibility="Auto"` 后内容会按无限宽度测量，
  栅格里的 `*` 列在测量期退化成内容宽度。WPF 在 Arrange 阶段会按最终宽度重新分配星号列，
  因此仍能撑满，但改动这类布局后**必须看截图确认**（`--ui-smoke` 已覆盖主窗口 / 编辑窗口 / 设置页的
  横向布局断言）

### 16. AI 对话（本地大模型 + 工具调用）
- 分层：`Core/Ai` 只依赖 BCL——`AiChatModels`（消息 / 工具调用 / 请求 / 响应）、
  `OpenAiCompatibleClient`（两种协议的收发与解析）、`AiToolCatalog`（工具定义 + 参数校验 + 分发）、
  `AiSession`（会话循环）、`AiItemReferences`（`#1` 编号表）、`AiActionParser`（正文兜底解析）、
  `AiJson`（序列化选项）。界面层只实现 `IAiActions`（`ScheduleAiActions`）与窗口
- **两种协议**：Ollama 原生 `/api/chat`（`think:false` 真正生效，Qwen3 从 30 秒降到 4 秒左右）与
  OpenAI 兼容 `/v1/chat/completions`（LM Studio / llama.cpp / vLLM）。`AiApiMode.Auto` 按地址判断
  （含 `:11434` 走原生）。请求体与响应解析都是**纯函数**，因此协议字段可以逐项自测
- **会话循环**：`AiSession.SendAsync` 把「用户消息 → 模型 → 工具 → 模型 → …」跑成一个循环，
  最多 `MaxToolRounds`（默认 4）轮；模型没吐原生 `tool_calls` 时用 `AiActionParser` 从正文里
  抢救 JSON / `<tool_call>` 形状的调用（换推理框架时很有用）。历史超出 `MaxHistoryMessages` 时从最早丢弃，
  但**不会把工具结果和它的调用请求拆开**（否则服务端会拒绝该请求）
- **思考等级的两种映射**：`AiThinkingLevel`（默认 `Off`）在原生协议下映射为
  `think:false / "low" / "medium" / "high"`，在 OpenAI 兼容下映射为
  `reasoning_effort: "none" / "low" / "medium" / "high"`，并在系统提示里按需追加 `/no_think`。
  实测（qwen3:8b）：关闭＝6~10 秒 / 0 字思维链；`"low"`＝42~52 秒 / 500~700 字——所以默认是最低档，
  而不是"低"。另外：非 Ollama 地址不塞 `think` 字段，避免严格服务端 400
- **工具结果不转义中文**：`AiJson.Options` 用 `UnsafeRelaxedJsonEscaping`。默认编码器会把每个汉字写成
  `\uXXXX`（6 个字符），工具结果是回传给模型的上下文，转义后 token 直接翻几倍
- **`#1` 编号表**：模型只回传 `#2`，由 `AiItemReferences` 解析成 GUID（外加"系列中的哪一次"），
  于是"把明天的例会改到 16:00"能落成「仅这一次」的例外而不是整体平移；编号只在一次会话内有效
- **错误是数据不是异常**：参数不合法 / 找不到引用都返回 `{"error":...}` 给模型，让它自己纠正；
  只有传输层失败（连不上、超时、返回不是 JSON）才抛 `AiChatException` 给人看
- 线程：对话在后台线程推进，而仓储 / WPF 要求 UI 线程，因此 `ScheduleAiActions` 的每个操作都经
  `IAiAppContext` + Dispatcher 切回 UI 线程（与本地接口同一套做法）；命令行模式下没有 Dispatcher，直接同步执行

## 扩展点

| 需求 | 做法 |
| --- | --- |
| 换 TTS 引擎（如 edge-tts / Azure） | 实现 `IVoiceService`，在 `AppHost.Start` 中替换 |
| 换存储（SQLite / 云同步） | `ScheduleRepository` 内部替换 `JsonStore`，对外接口不变 |
| 新增导入格式（Excel / iCal） | 复用 `CsvImportService` 的「行 → ScheduleItem」流程，实现对应的 Reader |
| 新增重复规则（如「每月第几个周一」） | 扩展 `RepeatKind` 与 `OccurrenceCalculator.OccurrenceAt` |
| 再加一套配色（如高对比度） | 复制 `Palette.Light.xaml` 改色 → 在 `ThemeService` 里登记 → `AppTheme` 加枚举值；
  键集合必须与另外两份完全一致（`--ui-smoke` 会打印关键色，便于核对） |
| 需要"重新加载界面"的新设置 | 参照主题：设置页写好后调 `AppHost.Restart`（自动或按钮触发），
  不要自己去 Kill 进程 —— `RestartPlan` 已处理单实例互斥体与数据目录 |
| 给 AI 加一个新工具 | 在 `AiToolCatalog.Definitions` 里加 schema，在 `IAiActions` 加方法，
  在 `ScheduleAiActions` 实现（记得用 `Invoke` 切 UI 线程）；补一条参数校验自测即可 |
| 接别的推理框架（llama.cpp server 等） | 只要它提供 OpenAI 兼容接口就不用改代码；否则参照
  `OpenAiCompatibleClient` 的两种协议分支再加一种，请求拼装与响应解析都写成纯函数便于自测 |
| 新的可换肤控件 | 在 `Theme.xaml` 里用自绘 `ControlTemplate` + `DynamicResource` 画刷，避免系统模板在夜间露白 |
