using InkCanvas.Ink;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace InkCanvas.Modules.Pdf;

public sealed partial class PdfWindow
{
    private readonly Border _statusBar = new() { MinHeight = 44, Padding = new Thickness(12, 4, 12, 4), BorderThickness = new Thickness(0, 1, 0, 0) };
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1, FontSize = 12 };
    private readonly TextBlock _saveState = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Margin = new Thickness(12, 0, 0, 0) };
    private readonly TextBlock _toolState = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Margin = new Thickness(12, 0, 0, 0) };
    private readonly ProgressRing _statusProgress = new() { Width = 18, Height = 18, MinWidth = 0, MinHeight = 0, Margin = new Thickness(0, 0, 8, 0), Visibility = Visibility.Collapsed };
    private string _statusMessage;
    private bool _searching;

    private void ConfigureStatusBar()
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(_statusProgress);
        Grid.SetColumn(_status, 1); row.Children.Add(_status);
        Grid.SetColumn(_saveState, 2); row.Children.Add(_saveState);
        Grid.SetColumn(_toolState, 3); row.Children.Add(_toolState);
        _statusBar.Child = row;
        AutomationProperties.SetName(_statusBar, "PDF 状态栏");
        AutomationProperties.SetName(_status, "PDF 状态信息");
        AutomationProperties.SetName(_saveState, "PDF 保存状态");
        AutomationProperties.SetName(_toolState, "PDF 当前工具");
        Grid.SetRow(_statusBar, 2); _root.Children.Add(_statusBar);
    }

    private void SetStatus(string text)
    {
        if (_closed) return;
        _statusMessage = text;
        UpdateStatusBar();
    }

    private void UpdateStatusBar()
    {
        if (_closed || Controller is null) return;
        bool busy = _saving || _searching;
        _statusProgress.IsActive = busy;
        _statusProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        string hint = _file.ReadOnly ? "此文件只允许查看" : _file.HasSignatures ? "含数字签名，请将批注另存为副本"
            : Controller.Tool == InkTool.Pan ? "拖动浏览页面 · Ctrl + 滚轮缩放" : "使用主工具栏批注";
        _status.Text = _saving ? "正在保存 PDF 矢量批注…" : _searching ? "正在搜索文字…" : _statusMessage ?? hint;
        ToolTipService.SetToolTip(_status, _status.Text);
        _saveState.Text = _file.ReadOnly ? "只读" : IsDirty ? "未保存" : "已保存";
        _toolState.Text = Controller.Tool switch
        {
            InkTool.Pan => "阅读", InkTool.Pen => "画笔", InkTool.Highlighter => "荧光笔",
            InkTool.Eraser => "橡皮", InkTool.Shape => "图形", InkTool.Select => "选择", _ => "批注",
        };
    }
}
