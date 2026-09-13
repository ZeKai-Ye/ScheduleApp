namespace Schedule.Core.Models;

/// <summary>提醒弹窗的位置策略。</summary>
public enum ReminderPositionMode
{
    /// <summary>工作区右下角（默认）。</summary>
    BottomRight = 0,
    /// <summary>工作区左下角。</summary>
    BottomLeft = 1,
    /// <summary>工作区右上角。</summary>
    TopRight = 2,
    /// <summary>工作区左上角。</summary>
    TopLeft = 3,
    /// <summary>屏幕中央。</summary>
    Center = 4,
    /// <summary>自定义绝对坐标（由拖动弹窗或手工填写得到）。</summary>
    Custom = 5,
}

/// <summary>
/// 提醒弹窗的定位计算（纯数学，便于自测）。
/// 由调用方提供「工作区」矩形（已排除任务栏），因此逻辑与 WPF / WinForms / 多显示器解耦。
/// </summary>
public static class ReminderPositionMath
{
    public const double DefaultMargin = 18;
    public const double DefaultCascadeStep = 34;

    /// <summary>
    /// 计算弹窗左上角坐标。
    /// </summary>
    /// <param name="mode">位置策略。</param>
    /// <param name="offsetX">右下 / 左下 / 右上 / 左上 / 居中：相对基准位置的额外偏移；自定义：绝对 X。</param>
    /// <param name="offsetY">同 offsetX 的纵向含义。</param>
    /// <param name="windowWidth">弹窗宽度。</param>
    /// <param name="windowHeight">弹窗高度。</param>
    /// <param name="workLeft">工作区左边界（多显示器时可能为负）。</param>
    /// <param name="workTop">工作区上边界。</param>
    /// <param name="workWidth">工作区宽度。</param>
    /// <param name="workHeight">工作区高度。</param>
    /// <param name="cascadeIndex">同屏多个弹窗时依次错开的下标。</param>
    public static (double Left, double Top) Compute(
        ReminderPositionMode mode,
        int offsetX,
        int offsetY,
        double windowWidth,
        double windowHeight,
        double workLeft,
        double workTop,
        double workWidth,
        double workHeight,
        int cascadeIndex = 0,
        double margin = DefaultMargin,
        double cascadeStep = DefaultCascadeStep)
    {
        if (windowWidth < 0) windowWidth = 0;
        if (windowHeight < 0) windowHeight = 0;

        var cascade = Math.Max(0, cascadeIndex) * cascadeStep;

        double left;
        double top;

        switch (mode)
        {
            case ReminderPositionMode.BottomLeft:
                left = workLeft + margin + offsetX;
                top = workTop + workHeight - windowHeight - margin + offsetY - cascade;
                break;

            case ReminderPositionMode.TopRight:
                left = workLeft + workWidth - windowWidth - margin + offsetX;
                top = workTop + margin + offsetY + cascade;
                break;

            case ReminderPositionMode.TopLeft:
                left = workLeft + margin + offsetX;
                top = workTop + margin + offsetY + cascade;
                break;

            case ReminderPositionMode.Center:
                left = workLeft + ((workWidth - windowWidth) / 2) + offsetX;
                top = workTop + ((workHeight - windowHeight) / 2) + offsetY;
                break;

            case ReminderPositionMode.Custom:
                left = offsetX;
                top = offsetY;
                break;

            default:
                left = workLeft + workWidth - windowWidth - margin + offsetX;
                top = workTop + workHeight - windowHeight - margin + offsetY - cascade;
                break;
        }

        return (Clamp(left, workLeft, workLeft + workWidth - windowWidth),
                Clamp(top, workTop, workTop + workHeight - windowHeight));
    }

    /// <summary>把区间约束到 [min, max]；当 max &lt; min（窗口比工作区还大）时贴住 min。</summary>
    private static double Clamp(double value, double min, double max)
    {
        if (max < min) return min;
        if (value < min) return min;
        return value > max ? max : value;
    }

    public static string Describe(ReminderPositionMode mode) => mode switch
    {
        ReminderPositionMode.BottomLeft => "左下角",
        ReminderPositionMode.TopRight => "右上角",
        ReminderPositionMode.TopLeft => "左上角",
        ReminderPositionMode.Center => "屏幕中央",
        ReminderPositionMode.Custom => "自定义位置",
        _ => "右下角",
    };
}
