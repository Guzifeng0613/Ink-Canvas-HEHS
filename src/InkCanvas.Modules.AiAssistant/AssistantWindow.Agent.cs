using InkCanvas.Host.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace InkCanvas.Modules.AiAssistant;

public sealed partial class AssistantWindow
{
    private readonly Flyout _agentFlyout = new();
    private readonly TextBlock _agentStatus = new() { Text = "尚未接入 Agent 服务", FontSize = 13, TextWrapping = TextWrapping.Wrap };
    private readonly ToggleSwitch _agentSwitch = new() { Header = "使用 Agent 操作画布", OnContent = "Agent", OffContent = "助教", IsEnabled = false };
    private bool _agentAvailable;
    public bool AgentMode => _agentAvailable && _agentSwitch.IsOn;

    private void BuildAgentMenu()
    {
        var content = new StackPanel { Width = 264, Spacing = 12 };
        content.Children.Add(new TextBlock { Text = "Agent", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(_agentStatus); content.Children.Add(_agentSwitch);
        content.Children.Add(new TextBlock { Text = "读取画布与选区\n创建可编辑图形对象\n调整选中对象\n获取画布截图用于识别",
            FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 24 });
        _agentSwitch.Toggled += (_, _) => _followUp.PlaceholderText = AgentMode ? "描述要在画布上完成的操作…" : "继续追问…";
        _agentFlyout.Content = content;
    }

    public void SetAgentAvailability(string providerName)
    {
        _agentAvailable = !string.IsNullOrWhiteSpace(providerName);
        _agentStatus.Text = _agentAvailable ? $"已连接 {providerName}" : "尚未接入 Agent 服务";
        _agentSwitch.IsEnabled = _agentAvailable;
        if (!_agentAvailable) _agentSwitch.IsOn = false;
        ToolTipService.SetToolTip(_agentButton, _agentStatus.Text);
    }

    public void SetAgentProgress(AssistantAgentProgress progress)
    {
        _agentStatus.Text = string.IsNullOrWhiteSpace(progress.Tool) ? progress.Message : $"{progress.Tool} · {progress.Message}";
        ShowNotice(_agentStatus.Text);
    }
}
