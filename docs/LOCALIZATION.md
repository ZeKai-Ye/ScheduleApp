# 本地化（中文 / English）

## 一句话

界面文案一律在调用点写两份：代码里 `Loc.T("中文", "English")`，XAML 里 `{l:Text '中文|English'}`。
没有 key、没有 resx、没有资源字典 —— 删掉代码，文案跟着一起消失，也不会留下孤儿资源。

## 三个组成部分

| 位置 | 作用 |
| --- | --- |
| `src/Schedule.Core/Localization/Loc.cs` | 语言的唯一事实来源：`AppLanguage`（System / Chinese / English）、`Loc.T`、`Loc.F`、`Loc.Parse`、`Loc.Culture` |
| `src/Schedule.App/Mvvm/TextExtension.cs` | XAML 标记扩展 `{l:Text '中文|English'}`，窗口加载时求值 |
| `src/Schedule.App/Services/LanguageService.cs` | 启动时把设置落成 `Loc.Current` + 界面文化 + WPF `FrameworkElement.Language` |

## 代码里怎么写

```csharp
using Schedule.Core.Localization;

// 纯文案
Title = Loc.T("新建日程", "New event");

// 带占位符：两种语言各自一个格式串，别让英文去将就中文语序
StatusText.Text = Loc.F("共 {0} 条", "{0} items", items.Count);

// 条件分支（例如「跟随系统」的当前状态）
hint = Loc.IsEnglish ? "Follow system" : "跟随系统";
```

## XAML 里怎么写

```xml
<Window xmlns:l="clr-namespace:Schedule.App.Mvvm"
        Title="{l:Text '日程提醒|Reminder'}">
    <Button Content="{l:Text '知道了|Got it'}" />
</Window>
```

规则：

1. 两段用 `|` 分隔，**左边永远是中文**，右边是英文；只写一段时中英文都用它（适合纯符号 / 数字）。
2. 英文里不要出现半角撇号 `'`（会跟 XAML 的单引号定界符冲突），写全角 `’`。
3. 文案里含字面 `{` `}`（例如语音模板占位符 `{title}`）时，别放进 XAML，
   在代码隐藏里用 `Loc.T(...)` 赋值。
4. `x:Name`、`x:Key`、`Tag`、`Style`、`Binding`、资源键一律不动。

## 什么翻译、什么不翻译

翻译（用户看得见）：窗口标题、按钮、标签、提示、ToolTip、`MessageBox`、托盘菜单、气泡通知、
导入报告、校验与错误提示、提醒档位文案、语音播报默认文案、命令行帮助与输出。

不翻译（数据或开发用）：

- 日程标题 / 分类名 / 备注 —— 用户自己写的数据，永远原样显示
- CSV 表头与列别名（`CsvSchema` / `CsvColumnMap`）—— 解析依赖它们
- `AppLog.*` 日志、代码注释、只进日志的异常消息
- JSON 字段名、注册表路径、文件路径、命令行开关、API 路由

## 为什么切换语言要重启

XAML 里的文案在**窗口加载时**求值，和主题切换是同一类问题：已经构造出来的控件不会自己变语言。
所以设置页改语言后保存会弹「现在重启吗？」，`AutoRestartOnThemeChange` 只影响主题，语言始终询问一次。

## 新增一门语言？

`AppLanguage` 加一个枚举值，`Loc.T` 的签名从二元改成字典查找即可。
按当前设计（每个调用点写死两段），加第三门语言要动所有调用点 —— 这是刻意的取舍：
两种语言时它最省心，超过两种就该换成 resx / 资源表了。

## 自测怎么守住这些约定

`tests/Schedule.Core.Tests/Cases/LocalizationTests.cs` 会：

1. 断言 `Loc` 的解析 / 切换 / 格式化 / 回退行为；
2. 扫描 `src` 下所有 `.xaml`，发现 **`Text`/`Content`/`Header`/`ToolTip`/`Title` 写死中文** 就失败；
3. 扫描所有 `{l:Text '...'}` 与 `Loc.T(...)` / `Loc.F(...)`，**英文为空**或**英文里混进中文**也失败。

```powershell
dotnet run --project tests/Schedule.Core.Tests -c Release
```

## 验证英文界面

```powershell
# 临时覆盖语言（不写回设置），顺便给每个窗口截图
ScheduleApp.exe --ui-smoke --lang en
# 截图在 <数据目录>\logs\ui-*.png
```

无界面模式（`--import` / `--info` 等）没写 `--lang` 时，会读 `settings.json` 里的语言设置。

## 已知边界

- 本地接口的自描述 JSON（`GET /api/schema`）与接口错误信息仍是中文：它们是给脚本 / AI 读的协议文本，
  不属于界面语言，改动会牵动接口契约。
- 日志始终中文，便于按日志搜问题。
- 无界面模式的 `--help` 跟随语言设置；纯新环境（没有 `settings.json`）按系统显示语言。
