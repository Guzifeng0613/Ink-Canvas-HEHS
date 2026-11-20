// @parity harmony/platform/src/main/ets/assistant/AiClient.ets
using InkCanvas.Core.AiAssistant;

namespace InkCanvas.Platform.Ai;

/// <summary>
/// 本地 mock 剧本：端点为空时由 <see cref="AiClient"/> 逐条回放，事件顺序与真实 SSE 流一致，
/// 覆盖公式转写、自动求解、图形释义与自由问答四条路径，供流式渲染 / 中断 / 错误态自测。
/// </summary>
public static class AiMockScript
{
    /// <summary>按模式返回固定的 mock 事件序列，最后一条恒为 <see cref="AssistantEventKind.Done"/>。</summary>
    public static IReadOnlyList<AssistantEvent> Build(AssistantMode mode)
    {
        var events = new List<AssistantEvent>();

        switch (mode)
        {
            case AssistantMode.Formula:
                events.Add(Token("已识别到手写公式。\n\n"));
                events.Add(Formula("\\frac{1}{2}x^2 - 3x + 2 = 0"));
                events.Add(Token("\n\n对照原迹：二次项系数为 $\\frac{1}{2}$，一次项为 $-3x$，常数项为 $2$。\n"));
                events.Add(Step(1, "两边乘以 2，消去分式系数", "x^2 - 6x + 4 = 0"));
                events.Add(Step(2, "移项后配方", "(x-3)^2 = 5"));
                events.Add(Token("\n可继续追问该式的判别式或图像。"));
                break;

            case AssistantMode.Solve:
                events.Add(Token("下面开始求解。\n\n"));
                events.Add(Step(1, "原方程为", "x^2 - 5x + 6 = 0"));
                events.Add(Step(2, "因式分解", "(x-2)(x-3) = 0"));
                events.Add(Step(3, "因此", "x_1 = 2,\\ x_2 = 3"));
                events.Add(Formula("x_1 = 2,\\ x_2 = 3"));
                events.Add(Token("\n\n检验：把 $x=2$ 与 $x=3$ 代回原式均成立。"));
                break;

            case AssistantMode.Shape:
                events.Add(Token("识别到直角三角形。\n\n"));
                events.Add(Shape("直角三角形", new double[] { 120, 80 }, new double[] { 280, 80 }, new double[] { 120, 240 }, new double[] { 120, 80 }));
                events.Add(Token("已知两条直角边分别为 3 与 4，斜边为 5。\n\n"));
                events.Add(Token("可推导性质：两锐角互余，且满足 $a^2+b^2=c^2$。"));
                break;

            default:
                events.Add(Token("这是框选内容的要点：\n\n"));
                events.Add(Token("1. 先识别手写公式的结构与符号；\n"));
                events.Add(Token("2. 再给出可验证的推导步骤；\n"));
                events.Add(Token("3. 最后给出一句话结论。\n\n"));
                events.Add(Formula("E = mc^2"));
                events.Add(Token("如需更精确的识别，可重新框选更清晰的区域。"));
                break;
        }

        events.Add(new AssistantEvent { Kind = AssistantEventKind.Done });
        return events;
    }

    private static AssistantEvent Token(string text) => new()
    {
        Kind = AssistantEventKind.Token,
        Text = text,
    };

    private static AssistantEvent Formula(string latex) => new()
    {
        Kind = AssistantEventKind.Formula,
        Latex = latex,
    };

    private static AssistantEvent Step(int index, string text, string latex) => new()
    {
        Kind = AssistantEventKind.Step,
        Index = index,
        Text = text,
        Latex = latex,
    };

    private static AssistantEvent Shape(string title, params double[][] points) => new()
    {
        Kind = AssistantEventKind.ShapeAnnotation,
        Title = title,
        Points = points,
    };
}