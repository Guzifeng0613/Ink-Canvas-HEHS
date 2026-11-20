using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace InkCanvas.AnalyticGeometry.Algebra;

/// <summary>Bridges the plot expression syntax and the MathML subset accepted by RichEditBox.</summary>
public static class MathMarkup
{
    private static readonly XNamespace Math = "http://www.w3.org/1998/Math/MathML";
    private static readonly HashSet<string> Functions = new(StringComparer.Ordinal)
    {
        "sin", "cos", "tan", "asin", "acos", "atan", "sinh", "cosh", "tanh", "sqrt", "abs",
        "exp", "ln", "log", "log10", "floor", "ceil", "round", "min", "max", "pow", "atan2",
    };

    public static string ToMathML(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > 2048)
            throw new FormatException("公式为空或超过 2048 个字符。");
        var parser = new Parser(expression);
        XElement body = parser.Parse();
        return new XDocument(new XElement(Math + "math", new XAttribute("display", "block"), body))
            .ToString(SaveOptions.DisableFormatting);
    }

    public static string ToExpression(string mathMl)
    {
        if (string.IsNullOrWhiteSpace(mathMl) || mathMl.Length > 32768)
            throw new FormatException("MathML 为空或超过允许长度。");
        using var reader = XmlReader.Create(new StringReader(mathMl), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 32768,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        });
        var document = XDocument.Load(reader, LoadOptions.None);
        var root = document.Root ?? throw new FormatException("MathML 缺少根节点。");
        var expression = ReadNode(root).Trim();
        if (expression.Length == 0 || expression.Length > 2048) throw new FormatException("MathML 不包含可绘制的公式。");
        return expression;
    }

    private static string ReadNode(XElement element)
    {
        string name = element.Name.LocalName;
        var children = element.Elements().ToArray();
        string Child(int index) => index < children.Length ? ReadNode(children[index]) : "";
        switch (name)
        {
            case "math":
            case "mrow":
            case "mstyle":
            case "mpadded":
                return string.Concat(children.Select(ReadNode));
            case "semantics":
                return children.FirstOrDefault(child => child.Name.LocalName != "annotation" && child.Name.LocalName != "annotation-xml") is { } presentation
                    ? ReadNode(presentation) : "";
            case "annotation":
            case "annotation-xml":
            case "mspace":
            case "none":
                return "";
            case "mi":
            case "mn":
            case "mtext":
                return NormalizeAtom(element.Value);
            case "mo":
                return NormalizeOperator(element.Value);
            case "mfrac":
                RequireChildren(children, 2, name);
                return "(" + Child(0) + ")/(" + Child(1) + ")";
            case "msup":
                RequireChildren(children, 2, name);
                return "(" + Child(0) + ")^(" + Child(1) + ")";
            case "msub":
                RequireChildren(children, 2, name);
                return Child(0) + "_" + Child(1);
            case "msubsup":
                RequireChildren(children, 3, name);
                return Child(0) + "_" + Child(1) + "^(" + Child(2) + ")";
            case "msqrt":
                return "sqrt(" + string.Concat(children.Select(ReadNode)) + ")";
            case "mroot":
                RequireChildren(children, 2, name);
                return "pow(" + Child(0) + ",1/(" + Child(1) + "))";
            case "mfenced":
            {
                string open = (string)element.Attribute("open") ?? "(";
                string close = (string)element.Attribute("close") ?? ")";
                string separators = (string)element.Attribute("separators") ?? ",";
                string separator = separators.Length == 0 ? "," : separators[..1];
                return open + string.Join(separator, children.Select(ReadNode)) + close;
            }
            default:
                if (children.Length == 0) return NormalizeOperator(element.Value);
                throw new FormatException("暂不支持此 MathML 结构：" + name + "。");
        }
    }

    private static string NormalizeAtom(string value)
    {
        value = value.Trim();
        if (value is "π" or "Π") return "pi";
        if (value is "θ" or "ϑ") return "t";
        if (value == "α") return "a";
        if (value == "β") return "b";
        if (value == "γ") return "c";
        if (value == "−∞") return "-1e100";
        if (value == "∞") return "1e100";
        return value;
    }

    private static string NormalizeOperator(string value) => value.Trim() switch
    {
        "⁢" or "·" or "×" => "*",
        "÷" or "⁄" => "/",
        "−" or "–" => "-",
        "⁡" => "",
        "≤" or "⩽" => "<=",
        "≥" or "⩾" => ">=",
        "≠" => "!=",
        "ⅆ" => "d",
        var text => text,
    };

    private static void RequireChildren(XElement[] children, int count, string name)
    {
        if (children.Length != count) throw new FormatException("MathML " + name + " 结构不完整。");
    }

    private sealed class Parser
    {
        private readonly string _text;
        private int _position;
        private Token _token;
        private int _depth, _nodes;

        private enum TokenKind { End, Number, Name, Plus, Minus, Multiply, Divide, Power, Open, Close, Comma, Equals }
        private readonly record struct Token(TokenKind Kind, string Text);

        public Parser(string text)
        {
            _text = text.Normalize(NormalizationForm.FormKC).ToLowerInvariant()
                .Replace('−', '-').Replace('×', '*').Replace('·', '*').Replace('÷', '/').Replace("π", "pi");
            Next();
        }

        public XElement Parse()
        {
            XElement left = Sum();
            if (_token.Kind == TokenKind.Equals)
            {
                Next();
                left = Row(left, Operator("="), Sum());
            }
            if (_token.Kind != TokenKind.End) throw Error("公式包含无法排版的符号 " + _token.Text);
            return left;
        }

        private XElement Sum()
        {
            Enter();
            try
            {
                XElement result = Product();
                while (_token.Kind is TokenKind.Plus or TokenKind.Minus)
                {
                    var operation = _token;
                    Next();
                    result = Row(result, Operator(operation.Text), Product());
                    Count();
                }
                return result;
            }
            finally { _depth--; }
        }

        private XElement Product()
        {
            XElement result = Unary();
            while (_token.Kind is TokenKind.Multiply or TokenKind.Divide or TokenKind.Open or TokenKind.Name or TokenKind.Number)
            {
                TokenKind operation = _token.Kind;
                if (operation is TokenKind.Multiply or TokenKind.Divide) Next();
                XElement right = Unary();
                result = operation == TokenKind.Divide
                    ? new XElement(Math + "mfrac", result, right)
                    : Row(result, Operator(operation == TokenKind.Multiply ? "×" : "⁢"), right);
                Count();
            }
            return result;
        }

        private XElement Unary()
        {
            Enter();
            try
            {
                if (_token.Kind is TokenKind.Plus or TokenKind.Minus)
                {
                    string operation = _token.Text;
                    Next();
                    return Row(Operator(operation), Unary());
                }
                XElement result = Primary();
                if (_token.Kind == TokenKind.Power)
                {
                    Next();
                    result = new XElement(Math + "msup", result, Unary());
                    Count();
                }
                return result;
            }
            finally { _depth--; }
        }

        private XElement Primary()
        {
            Count();
            if (_token.Kind == TokenKind.Number)
            {
                string number = _token.Text;
                Next();
                return new XElement(Math + "mn", number);
            }
            if (_token.Kind == TokenKind.Open)
            {
                Next();
                var value = Sum();
                Expect(TokenKind.Close, "缺少右括号");
                return Row(Operator("("), value, Operator(")"));
            }
            if (_token.Kind != TokenKind.Name) throw Error("此处需要数字、变量或括号。");
            string name = _token.Text;
            Next();
            if (_token.Kind == TokenKind.Open && Functions.Contains(name))
            {
                Next();
                var arguments = new List<XElement>();
                if (_token.Kind != TokenKind.Close)
                {
                    arguments.Add(Sum());
                    while (_token.Kind == TokenKind.Comma)
                    {
                        Next();
                        arguments.Add(Sum());
                    }
                }
                Expect(TokenKind.Close, "函数缺少右括号");
                if (name == "sqrt" && arguments.Count == 1) return new XElement(Math + "msqrt", arguments[0]);
                return Row(new XElement(Math + "mi", name), Operator("⁡"), Operator("("),
                    new XElement(Math + "mrow", arguments.SelectMany((argument, index) => index == 0
                        ? new[] { argument } : new[] { Operator(","), argument })), Operator(")"));
            }
            if (name == "pi") return new XElement(Math + "mi", "π");
            if (name.Length > 1 && name.All(letter => "xytabc".Contains(letter)))
                return Row(name.Select(letter => new XElement(Math + "mi", letter.ToString())).ToArray());
            return new XElement(Math + "mi", name);
        }

        private void Expect(TokenKind kind, string message)
        {
            if (_token.Kind != kind) throw Error(message);
            Next();
        }

        private FormatException Error(string message) => new(message + "，位置 " + System.Math.Max(1, _position) + "。");
        private void Enter() { if (++_depth > 48) throw Error("公式嵌套过深。"); }
        private void Count() { if (++_nodes > 512) throw Error("公式过于复杂。"); }
        private static XElement Operator(string text) => new(Math + "mo", text);
        private static XElement Row(params XElement[] items) => new(Math + "mrow", items);

        private void Next()
        {
            while (_position < _text.Length && char.IsWhiteSpace(_text[_position])) _position++;
            if (_position == _text.Length) { _token = new(TokenKind.End, ""); return; }
            int start = _position;
            char current = _text[_position++];
            if (char.IsAsciiDigit(current) || current == '.')
            {
                while (_position < _text.Length && (char.IsAsciiDigit(_text[_position]) || _text[_position] == '.')) _position++;
                if (_position < _text.Length && _text[_position] == 'e')
                {
                    int end = _position + 1;
                    if (end < _text.Length && _text[end] is '+' or '-') end++;
                    if (end < _text.Length && char.IsAsciiDigit(_text[end]))
                    {
                        _position = end + 1;
                        while (_position < _text.Length && char.IsAsciiDigit(_text[_position])) _position++;
                    }
                }
                _token = new(TokenKind.Number, _text[start.._position]);
                return;
            }
            if (char.IsAsciiLetter(current))
            {
                while (_position < _text.Length && char.IsAsciiLetterOrDigit(_text[_position])) _position++;
                _token = new(TokenKind.Name, _text[start.._position]);
                return;
            }
            _token = new(current switch
            {
                '+' => TokenKind.Plus, '-' => TokenKind.Minus, '*' => TokenKind.Multiply, '/' => TokenKind.Divide,
                '^' => TokenKind.Power, '(' => TokenKind.Open, ')' => TokenKind.Close, ',' => TokenKind.Comma,
                '=' => TokenKind.Equals, _ => throw Error("不支持的符号 " + current),
            }, current.ToString());
        }
    }
}
