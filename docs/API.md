# 本地接口（供 AI / 脚本直接读写日程）

程序内置一个**只监听 127.0.0.1** 的 HTTP JSON 接口，AI 助手或脚本可以直接增删改查日程、修改全局语音设置、
让程序朗读文本，甚至立即触发一次提醒。**零第三方依赖**，由内置的极简 HTTP 服务器提供。

## 1. 发现与鉴权

启动程序后，数据目录会出现 `api.json`（默认 `%APPDATA%\ScheduleApp\api.json`）：

FENCEjson
{
  "name": "日程助手 ScheduleApp 本地接口",
  "url": "http://127.0.0.1:43121",
  "schema": "http://127.0.0.1:43121/api/schema",
  "port": 43121,
  "token": "8f3c……（48 位十六进制）",
  "authHeader": "X-Api-Token",
  "version": "0.3.0",
  "dataDirectory": "C:\\Users\\<你>\\AppData\\Roaming\\ScheduleApp",
  "docs": "docs/API.md"
}
FENCE

**AI 的接入方式**：读 `api.json` 拿到 `url` 与 `token` → 每个请求带上 `X-Api-Token` 请求头即可。
也可以直接用 `?token=……` 查询串（便于手工 curl）。

FENCEbash
# 读取发现文件（PowerShell）
$api = Get-Content "$env:APPDATA\ScheduleApp\api.json" -Raw | ConvertFrom-Json
$h = @{ 'X-Api-Token' = $api.token }

# 先读自描述，AI 可据此了解全部字段与用法
curl -s -H "X-Api-Token: $($api.token)" $api.schema
FENCE

- 令牌首次启动自动生成，可用 `PATCH /api/settings` 或直接改 `settings.json` 重置
- 缺少/错误令牌返回 **401**；接口仅绑定回环地址，不对外网开放
- 支持 `--serve`（只跑接口与提醒、不开窗口）、`--port <端口>`、`--no-api`

## 2. 接口一览

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/schema` | **自描述**：端点、字段、枚举、文案优先级、示例 |
| GET | `/api/info` | 版本、数据目录、数量、语音状态、下一次提醒 |
| GET | `/api/items` | 查询日程；`from` `to` `q` `category` `status` `limit` `expand` `days` |
| POST | `/api/items` | 新建；返回 201 与完整对象 |
| GET | `/api/items/{id}` | 读取单条 |
| PUT | `/api/items/{id}` | 全量替换（未提供的字段复位默认值） |
| PATCH | `/api/items/{id}` | **局部修改（推荐）** |
| DELETE | `/api/items/{id}` | 删除 |
| POST | `/api/items/{id}/status` | `{"status":"Done"|"Pending"|"Canceled"}` |
| POST | `/api/items/{id}/complete` | 标记完成的简写 |
| POST | `/api/items/{id}/reminder-now` | 立即按某档位触发一次提醒 |
| POST | `/api/items/{id}/occurrence` | **只改系列中的某一次**（改期 / 仅本次内容） |
| POST | `/api/items/{id}/occurrence/delete` | **只删除某一次** |
| POST | `/api/items/{id}/occurrence/restore` | 撤销某一次的例外 |
| GET | `/api/occurrences` | **把重复日程展开**，并给出每档位将播报的文本 |
| GET | `/api/reminders/next` | 接下来 N 次提醒（含解析后的播报文本） |
| POST | `/api/reminders/test` | 立即触发提醒：`{"itemId":"…","offsetMinutes":30}` |
| GET | `/api/voices` | 系统可用音色 |
| POST | `/api/speak` | 朗读 `{"text":"…"}` 或 `{"itemId":"…","offsetMinutes":30}` |
| POST | `/api/import` | 导入 CSV：`{"path":"D:\\a.csv"}` 或 `{"content":"标题,开始时间…"}` |
| GET | `/api/export` | 导出 `?format=csv`（默认）或 `json` |
| GET | `/api/settings` | 读取全局设置（含语音与档位文案） |
| PATCH | `/api/settings` | 修改全局设置 |
| POST | `/api/reload` | 从磁盘重新加载（手工改过 json 后） |
| GET | `/api/system/info` | 可执行文件路径、桌面/开始菜单快捷方式是否存在、开机启动方式与命令行 |
| POST | `/api/system/shortcut` | 创建 / 删除快捷方式 |
| POST | `/api/system/autostart` | 启用 / 停用开机启动 |

时间字段接受宽松写法：`2025-06-03 14:00`、`2025-06-03T14:00:00`、`2025/6/3 14:00`、`明天 9:00` 均可。
未识别的字段不会静默忽略，会通过响应头 `X-Ignored-Fields` 告诉你（避免拼错字段名却不自知）。

## 3. 日程字段

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `title` | string | 必填 |
| `start` `end` | 时间字符串 | 开始 / 结束 |
| `allDay` | bool | 全天 |
| `category` | string | 分类（决定自动配色） |
| `color` | string | `#RRGGBB` 或中文色名（红/蓝/灰…）；空 = 跟随分类 |
| `priority` | `Low`/`Normal`/`High` | 优先级 |
| `repeat` | `None`/`Daily`/`Weekdays`/`Weekly`/`Monthly`/`Yearly` | 重复类型 |
| `repeatInterval` | int | 每 N 个周期 |
| `repeatWeekdays` | string[] | `["Monday","Wednesday"]`，仅 Weekly 有效 |
| `repeatCount` / `repeatUntil` | int / 日期 | 共 N 次 / 截止日期 |
| `reminderOffsets` | int[] | **提醒档位**（提前分钟数），如 `[30,15,5,0]`；0 = 准点 |
| `reminderVoiceTexts` | `{"30":"还有30分钟"}` | **按档位自定义播报文案** |
| `reminderSoundFiles` | `{"30":"D:\\bell.mp3"}` | 按档位自定义提示音 |
| `voiceText` | string | 本日程通用播报文案 |
| `remindAtOverride` | 时间 | 额外的绝对提醒时刻 |
| `status` | `Pending`/`Done`/`Canceled` | 状态 |

## 4. 同一日程、不同提醒时间说不同的话

这是本接口与 CSV 都支持的核心能力。播报文案按下面的优先级解析（越靠前越优先）：

1. `reminderVoiceTexts[档位]` —— 本日程 · 该档位
2. `voiceText` —— 本日程 · 通用文案
3. `settings.offsetVoiceTemplates[档位]` —— **全局 · 该档位文案**（`PATCH /api/settings` 可改）
4. `settings.voiceTemplate` —— 全局模板

最终文本会用占位符渲染：
`{title}` `{time}` `{date}` `{weekday}` `{category}` `{notes}` `{priority}` `{countdown}` `{status}` `{offset}`

FENCEbash
# 建一条：提前 30 分钟提醒"还有半小时"、5 分钟提醒"马上开始"、准点说"现在开始"
curl -s -X POST "$url/api/items" -H "X-Api-Token: $token" -H "Content-Type: application/json" -d '{
  "title": "项目周会",
  "start": "2025-06-03 14:00",
  "category": "工作",
  "repeat": "Weekly",
  "repeatWeekdays": ["Monday", "Wednesday"],
  "reminderOffsets": [30, 15, 5, 0],
  "reminderVoiceTexts": {
    "30": "还有 30 分钟：{title}",
    "5":  "{title} 5 分钟后开始",
    "0":  "{title} 现在开始"
  }
}'
FENCE

想看「到点到底会说什么」，直接查发生列表（overlap 也能查）：

FENCEbash
curl -s -H "X-Api-Token: $token" "$url/api/occurrences?from=2025-06-01&to=2025-06-15&q=项目周会"
FENCE

返回中每次发生的 `reminders[]` 都带有 `offsetMinutes` `fireAt` `voiceText` `voiceSource` `soundFile`：

FENCEjson
{
  "itemId": "…", "title": "项目周会", "start": "2025-06-04T14:00:00",
  "reminders": [
    { "offsetMinutes": 30, "offsetText": "提前 30 分钟", "fireAt": "2025-06-04T13:30:00",
      "voiceSource": "本日程 · 该档位", "voiceText": "还有 30 分钟：项目周会", "soundFile": "" }
  ]
}
FENCE

### 全局语音选项
不想每条日程都写一遍，就把「按档位的默认文案」设成全局的：

FENCEbash
curl -s -X PATCH "$url/api/settings" -H "X-Api-Token: $token" -H "Content-Type: application/json" -d '{
  "offsetVoiceTemplates": { "30": "还有 30 分钟", "15": "还有 15 分钟", "0": "现在开始" },
  "voiceTemplate": "{time}，提醒您：{title}",
  "voiceRate": 0,
  "voiceVolume": 90,
  "speechEnabled": true,
  "soundEnabled": true,
  "defaultSoundFile": "D:\\sounds\\soft.mp3",
  "soundBeforeSpeech": true
}'
FENCE

之后**没有自定义文案的日程**都会自动套用这些档位文案；日程自身写了文案的仍以日程为准（可在上面的返回里看 `voiceSource` 确认）。

## 5. 系列事件：只改 / 只删某一次

重复日程的某一次常常要单独处理。接口用「原始开始时间」定位那一次（改期后这个键不变）：

```bash
# 把 6/9 10:00 那次改期到 6/11 15:00
curl -s -X POST -H "$H" -H "Content-Type: application/json" \
  "$BASE/api/items/$ID/occurrence" \
  -d '{"originalStart":"2025-06-09 10:00","newStart":"2025-06-11 15:00","newEnd":"2025-06-11 16:00"}'

# 只改这一次的文案与提醒档位（不改时间）
curl -s -X POST -H "$H" -H "Content-Type: application/json" \
  "$BASE/api/items/$ID/occurrence" \
  -d '{"originalStart":"2025-06-16 10:00","title":"站会（改线上）","reminderOffsets":[5]}'

# 只删除 6/23 那一次
curl -s -X POST -H "$H" -H "Content-Type: application/json" \
  "$BASE/api/items/$ID/occurrence/delete" -d '{"occurrenceStart":"2025-06-23 10:00"}'

# 撤销这一次的例外（恢复成系列默认）
curl -s -X POST -H "$H" -H "Content-Type: application/json" \
  "$BASE/api/items/$ID/occurrence/restore" -d '{"occurrenceStart":"2025-06-16 10:00"}'
```

- `GET /api/occurrences` 返回项里的 `start` 就是改期后的实际时间
- 日程对象的 `excludedOccurrences`（被删的那几次）与 `occurrenceOverrides`（被改的那几次）可直接读，
  也可用 `PATCH /api/items/{id}` 整体替换
- 非重复日程调用这些端点返回 **422**；时间点不是该系列的发生返回 **404**

## 6. 典型 AI 用法

FENCEbash
BASE=$url; H="X-Api-Token: $token"

# 1) 今天还有什么
curl -s -H "$H" "$BASE/api/items?from=$(date +%F)&to=$(date -d tomorrow +%F)"

# 2) 接下来三次提醒会说什么
curl -s -H "$H" "$BASE/api/reminders/next?limit=3"

# 3) 把某条日程改成多档提醒并加文案
ID=…; curl -s -X PATCH -H "$H" -H "Content-Type: application/json" "$BASE/api/items/$ID" -d '{
  "reminderOffsets": [60,30,10],
  "reminderVoiceTexts": { "60": "一小时后有安排", "10": "十分钟后出发" }
}'

# 4) 试听某条日程在「提前 10 分钟」这一档会说什么
curl -s -X POST -H "$H" -H "Content-Type: application/json" "$BASE/api/speak" -d '{"itemId":"'$ID'","offsetMinutes":10}'

# 5) 立刻弹一次提醒验证效果（会播报 + 弹窗）
curl -s -X POST -H "$H" -H "Content-Type: application/json" "$BASE/api/reminders/test" -d '{"itemId":"'$ID'","offsetMinutes":10}'

# 6) 批量导入一份 CSV（可直接给内容，不用落盘）
curl -s -X POST -H "$H" -H "Content-Type: application/json" "$BASE/api/import" -d '{
  "fileName": "from-ai.csv",
  "content": "标题,开始时间,提前提醒,语音内容\n复盘,2025-06-06 16:00,\"30,5\",\"30:还有30分钟|5:马上开始\"\n"
}'
FENCE

## 7. 桌面集成与弹窗位置

```bash
# 看系统状态：快捷方式在不在、开机启动怎么落地的
curl -s -H "$H" "$BASE/api/system/info"

# 创建桌面快捷方式（directory 可选，用于便携 / 调试）
curl -s -X POST -H "$H" -H "Content-Type: application/json" "$BASE/api/system/shortcut" \
  -d '{"target":"desktop","create":true}'
# 删除开始菜单快捷方式
curl -s -X POST -H "$H" -H "Content-Type: application/json" "$BASE/api/system/shortcut" \
  -d '{"target":"startmenu","create":false}'

# 开机启动：默认附加 --minimized（进托盘不弹窗）
curl -s -X POST -H "$H" -H "Content-Type: application/json" "$BASE/api/system/autostart" \
  -d '{"enabled":true,"arguments":"--minimized"}'
# 关闭
curl -s -X POST -H "$H" -H "Content-Type: application/json" "$BASE/api/system/autostart" -d '{"enabled":false}'

# 把提醒弹窗固定到左上角并微调
curl -s -X PATCH -H "$H" -H "Content-Type: application/json" "$BASE/api/settings" \
  -d '{"reminderPosition":"TopLeft","reminderPositionX":120,"reminderPositionY":80,"reminderRememberPosition":true}'
```

`autostart` 返回的 `mode` 是真实落地方式：`Registry`（注册表 Run 键）或 `StartupFolder`（启动文件夹快捷方式）。
宿主无法完成时返回 **501** 并附原因（例如被安全软件拦截）。
`reminderPosition` 可取 `BottomRight` `BottomLeft` `TopRight` `TopLeft` `Center` `Custom`；
前五种下 X / Y 是相对该基准的偏移，`Custom` 下是弹窗左上角的屏幕坐标（越界自动贴回可见区域）。
**X / Y 的单位是设备无关单位（DIP，与窗口 Left / Top 一致）**，程序内部再按屏幕缩放换算成设备像素，
因此 125% / 150% 缩放下填入的数值就是实际落点；`Custom` 坐标以整个虚拟桌面为界，多显示器下可跨屏。

改 `theme` 时接口只保存设置并即时换调色板，**不会**自动重启进程（避免远程调用把界面重启掉）；
界面侧在设置页保存主题时才会按 `autoRestartOnThemeChange` 自动重启。

## 8. 约定与错误

- 状态码：`200` 成功、`201` 已创建、`204` 已删除、`400` 参数/JSON 错误、**`401` 令牌错误**、
  `404` 不存在、`405` 方法不允许、`411` 用了 chunked 传输（请带 `Content-Length`）、
  `422` 校验失败（返回具体原因）、`500` 服务端错误、`501` 当前进程不支持该能力（如无语音）
- 错误响应统一为 `{"error":"…","hint":"…","status":…}`
- 请求体必须是 JSON 对象；不支持 `Transfer-Encoding: chunked`
- 每个请求都会切到 UI 线程执行，因此与界面操作天然串行、不会打架
- 用了 `--serve` 时没有主窗口，但提醒弹窗照常出现；托盘图标可打开界面或退出

## 9. CSV 中的等价写法

CSV 的 `语音内容` / `提醒铃声` 列支持 `档位:文案` 形式（用 `|` 分隔），与接口字段一一对应：

FENCEcsv
标题,开始时间,提前提醒,语音内容,提醒铃声
项目周会,2025-06-03 14:00,"30,5,0","30:还有30分钟|5:马上开始|0:现在开始|*:周会提醒","30:C:\bells\early.mp3|*:C:\bells\normal.mp3"
FENCE

`*:` 表示「通用文案」。只有当每一段都是 `档位:值` 且档位确实存在于该行时才按编码解析，
所以像 `8:30 开会：记得带材料` 这样的普通文案会原样保留。
