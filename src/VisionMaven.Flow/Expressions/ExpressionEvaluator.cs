using System.Globalization;
using VisionMaven.Core.Exceptions;

namespace VisionMaven.Flow.Expressions;

/// <summary>
/// 受限表达式求值器：支持数值、变量引用（<c>N3.count</c>）、括号、算术、比较与逻辑运算。
/// 不执行任意代码，仅用于 <c>judge.rule</c> 与 <c>script.expression</c>。
/// </summary>
public static class ExpressionEvaluator
{
    /// <summary>求值并返回布尔结果。</summary>
    public static bool EvaluateBoolean(string expression, Func<string, object?> resolver)
        => ToBoolean(Evaluate(expression, resolver));

    /// <summary>求值并返回数值结果。</summary>
    public static double EvaluateNumber(string expression, Func<string, object?> resolver)
        => ToNumber(Evaluate(expression, resolver));

    public static object? Evaluate(string expression, Func<string, object?> resolver)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        var parser = new Parser(expression, resolver);
        var value = parser.ParseExpression();
        parser.EnsureEnd();
        return value;
    }

    public static double ToNumber(object? value) => value switch
    {
        null => 0d,
        bool flag => flag ? 1d : 0d,
        double number => number,
        float number => number,
        int number => number,
        long number => number,
        string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => 0d
    };

    public static bool ToBoolean(object? value) => value switch
    {
        null => false,
        bool flag => flag,
        double number => Math.Abs(number) > double.Epsilon,
        _ => ToNumber(value) != 0d
    };

    private sealed class Parser
    {
        private readonly string _text;
        private readonly Func<string, object?> _resolver;
        private int _position;

        public Parser(string text, Func<string, object?> resolver)
        {
            _text = text;
            _resolver = resolver;
        }

        public void EnsureEnd()
        {
            SkipWhitespace();
            if (_position < _text.Length)
            {
                throw new ConfigurationException(
                    ErrorCodes.ConfigParseFailed,
                    $"表达式存在无法解析的内容：{_text[_position..]}");
            }
        }

        public object? ParseExpression() => ParseOr();

        private object? ParseOr()
        {
            var left = ParseAnd();
            while (Match("||"))
            {
                var right = ParseAnd();
                left = ToBoolean(left) || ToBoolean(right);
            }

            return left;
        }

        private object? ParseAnd()
        {
            var left = ParseComparison();
            while (Match("&&"))
            {
                var right = ParseComparison();
                left = ToBoolean(left) && ToBoolean(right);
            }

            return left;
        }

        private object? ParseComparison()
        {
            var left = ParseAdditive();

            while (true)
            {
                SkipWhitespace();
                if (Match(">="))
                {
                    left = ToNumber(left) >= ToNumber(ParseAdditive());
                }
                else if (Match("<="))
                {
                    left = ToNumber(left) <= ToNumber(ParseAdditive());
                }
                else if (Match("=="))
                {
                    left = Math.Abs(ToNumber(left) - ToNumber(ParseAdditive())) < 1e-9;
                }
                else if (Match("!="))
                {
                    left = Math.Abs(ToNumber(left) - ToNumber(ParseAdditive())) >= 1e-9;
                }
                else if (Match(">"))
                {
                    left = ToNumber(left) > ToNumber(ParseAdditive());
                }
                else if (Match("<"))
                {
                    left = ToNumber(left) < ToNumber(ParseAdditive());
                }
                else
                {
                    return left;
                }
            }
        }

        private object? ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (true)
            {
                SkipWhitespace();
                if (Match("+"))
                {
                    left = ToNumber(left) + ToNumber(ParseMultiplicative());
                }
                else if (Match("-"))
                {
                    left = ToNumber(left) - ToNumber(ParseMultiplicative());
                }
                else
                {
                    return left;
                }
            }
        }

        private object? ParseMultiplicative()
        {
            var left = ParseUnary();
            while (true)
            {
                SkipWhitespace();
                if (Match("*"))
                {
                    left = ToNumber(left) * ToNumber(ParseUnary());
                }
                else if (Match("/"))
                {
                    var divisor = ToNumber(ParseUnary());
                    left = Math.Abs(divisor) < 1e-12 ? 0d : ToNumber(left) / divisor;
                }
                else if (Match("%"))
                {
                    var divisor = ToNumber(ParseUnary());
                    left = Math.Abs(divisor) < 1e-12 ? 0d : ToNumber(left) % divisor;
                }
                else
                {
                    return left;
                }
            }
        }

        private object? ParseUnary()
        {
            SkipWhitespace();
            if (Match("!"))
            {
                return !ToBoolean(ParseUnary());
            }

            if (Match("-"))
            {
                return -ToNumber(ParseUnary());
            }

            return ParsePrimary();
        }

        private object? ParsePrimary()
        {
            SkipWhitespace();
            if (_position >= _text.Length)
            {
                throw new ConfigurationException(ErrorCodes.ConfigParseFailed, "表达式意外结束");
            }

            if (Match("("))
            {
                var value = ParseExpression();
                if (!Match(")"))
                {
                    throw new ConfigurationException(ErrorCodes.ConfigParseFailed, "表达式缺少右括号");
                }

                return value;
            }

            var current = _text[_position];
            if (current == '\'' || current == '"')
            {
                return ParseStringLiteral(current);
            }

            if (char.IsDigit(current) || current == '.')
            {
                return ParseNumber();
            }

            if (char.IsLetter(current) || current == '_')
            {
                var identifier = ParseIdentifier();
                if (string.Equals(identifier, "true", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.Equals(identifier, "false", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return _resolver(identifier);
            }

            throw new ConfigurationException(
                ErrorCodes.ConfigParseFailed,
                $"表达式无法识别的字符：{current}");
        }

        private string ParseStringLiteral(char quote)
        {
            _position++;
            var start = _position;
            while (_position < _text.Length && _text[_position] != quote)
            {
                _position++;
            }

            var value = _text[start.._position];
            if (_position < _text.Length)
            {
                _position++;
            }

            return value;
        }

        private double ParseNumber()
        {
            var start = _position;
            while (_position < _text.Length && (char.IsDigit(_text[_position]) || _text[_position] == '.'))
            {
                _position++;
            }

            var text = _text[start.._position];
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0d;
        }

        private string ParseIdentifier()
        {
            var start = _position;
            while (_position < _text.Length
                   && (char.IsLetterOrDigit(_text[_position]) || _text[_position] == '_' || _text[_position] == '.'))
            {
                _position++;
            }

            return _text[start.._position];
        }

        private void SkipWhitespace()
        {
            while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
            {
                _position++;
            }
        }

        private bool Match(string token)
        {
            SkipWhitespace();
            if (_position + token.Length > _text.Length)
            {
                return false;
            }

            if (!_text.AsSpan(_position, token.Length).SequenceEqual(token))
            {
                return false;
            }

            _position += token.Length;
            return true;
        }
    }
}
