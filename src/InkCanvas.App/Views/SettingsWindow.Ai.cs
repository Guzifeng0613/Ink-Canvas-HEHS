using InkCanvas.Host.Contracts;

namespace InkCanvas.App.Views;

public sealed partial class SettingsWindow
{
    private void BuildAiConnections()
    {
        AddSection("AI 服务与模型");
        var service = _services.Modules.GetExport<IAiConnectionSettings>();
        if (service is null)
        {
            _content.Children.Add(InfoRow("请在“模块与插件”中安装并启用 AI 助教，重启后即可管理 API 端点与模型。"));
            return;
        }
        _content.Children.Add(Searchable(Card(service.CreateSettingsView()), "API 连接与模型",
            "供应商 API Key OpenAI Responses Anthropic Google DeepSeek GLM TokenHub Qwen StepFun MiMo MiniMax Kimi OpenCode PackyCode gateway 搜索"));
    }
}
