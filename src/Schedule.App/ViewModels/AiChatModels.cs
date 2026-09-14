using System.Windows;
using System.Windows.Media;
using Schedule.App.Mvvm;

namespace Schedule.App.ViewModels;

/// <summary>对话窗口里的一条气泡（用户 / 助手 / 工具结果 / 错误）。</summary>
public sealed class AiChatBubble : ObservableObject
{
    private string _text = string.Empty;
    private string? _reasoning;
    private string? _details;

    public bool IsUser { get; init; }

    public bool IsTool { get; init; }

    public bool IsError { get; init; }

    public bool IsAssistant => !IsUser && !IsTool && !IsError;

    public string Header { get; init; } = string.Empty;

    public string TimeText { get; init; } = DateTime.Now.ToString("HH:mm");

    /// <summary>正文（生成中会更新，所以是可通知属性）。</summary>
    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    public string? Reasoning
    {
        get => _reasoning;
        set
        {
            if (!SetProperty(ref _reasoning, value)) return;
            OnPropertyChanged(nameof(ReasoningVisibility));
        }
    }

    public string? Details
    {
        get => _details;
        set
        {
            if (!SetProperty(ref _details, value)) return;
            OnPropertyChanged(nameof(DetailsVisibility));
        }
    }

    public HorizontalAlignment Alignment => IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    public Brush Background => IsUser
        ? ThemeBrushes.AccentSoft
        : IsTool ? ThemeBrushes.Hint
        : IsError ? ThemeBrushes.DangerSoft
        : ThemeBrushes.Card;

    public Brush BorderBrush => IsError
        ? ThemeBrushes.Danger
        : IsUser ? ThemeBrushes.Accent
        : ThemeBrushes.HintBorder;

    public Brush HeaderBrush => IsUser
        ? ThemeBrushes.Accent
        : IsError ? ThemeBrushes.Danger
        : ThemeBrushes.Subtle;

    public Visibility ReasoningVisibility
        => string.IsNullOrWhiteSpace(Reasoning) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility DetailsVisibility
        => string.IsNullOrWhiteSpace(Details) ? Visibility.Collapsed : Visibility.Visible;

    public static AiChatBubble User(string text) => new() { IsUser = true, Header = "我", Text = text };

    public static AiChatBubble Assistant(string text) => new() { Header = "AI 助手", Text = text };

    public static AiChatBubble Tool(string name, string summary, string details)
        => new() { IsTool = true, Header = "工具 " + name, Text = summary, Details = details };

    public static AiChatBubble Error(string text) => new() { IsError = true, Header = "出错了", Text = text };
}
