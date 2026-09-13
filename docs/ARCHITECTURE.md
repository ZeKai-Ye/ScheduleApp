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

## 扩展点

| 需求 | 做法 |
| --- | --- |
| 换 TTS 引擎（如 edge-tts / Azure） | 实现 `IVoiceService`，在 `AppHost.Start` 中替换 |
| 换存储（SQLite / 云同步） | `ScheduleRepository` 内部替换 `JsonStore`，对外接口不变 |
| 新增导入格式（Excel / iCal） | 复用 `CsvImportService` 的「行 → ScheduleItem」流程，实现对应的 Reader |
| 新增重复规则（如「每月第几个周一」） | 扩展 `RepeatKind` 与 `OccurrenceCalculator.OccurrenceAt` |
| 深色主题 | `Theme.xaml` 的 Brush 资源是单一入口，加一套配色 + 运行时切换字典 |
