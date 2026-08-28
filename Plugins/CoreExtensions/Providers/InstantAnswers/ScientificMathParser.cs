namespace Lertaro.Plugins.CoreExtensions.Providers.InstantAnswers;

/// <summary>
/// Self-contained, robust recursive descent parser for mathematical, trigonometric, and logarithmic expressions.
/// </summary>
internal class ScientificMathParser
{
    private readonly string _expr;
    private int _pos;

    public ScientificMathParser(string expr)
    {
        _expr = expr;
        _pos = 0;
    }

    public double Parse()
    {
        var result = ParseExpression();
        SkipWhitespace();
        if (_pos < _expr.Length)
            throw new Exception("Unexpected character: " + _expr[_pos]);
        return result;
    }

    private double ParseExpression()
    {
        var result = ParseTerm();
        while (true)
        {
            SkipWhitespace();
            if (_pos >= _expr.Length) break;
            var op = _expr[_pos];
            if (op == '+' || op == '-')
            {
                _pos++;
                var nextTerm = ParseTerm();
                if (op == '+') result += nextTerm;
                else result -= nextTerm;
            }
            else
            {
                break;
            }
        }
        return result;
    }

    private double ParseTerm()
    {
        var result = ParseFactor();
        while (true)
        {
            SkipWhitespace();
            if (_pos >= _expr.Length) break;
            var op = _expr[_pos];
            if (op == '*' || op == '/' || op == '%')
            {
                _pos++;
                var nextFactor = ParseFactor();
                if (op == '*') result *= nextFactor;
                else if (op == '/')
                {
                    if (nextFactor == 0) throw new DivideByZeroException();
                    result /= nextFactor;
                }
                else
                {
                    result %= nextFactor;
                }
            }
            else
            {
                break;
            }
        }
        return result;
    }

    private double ParseFactor()
    {
        var result = ParsePrimary();
        SkipWhitespace();
        if (_pos < _expr.Length && _expr[_pos] == '^')
        {
            _pos++;
            var exponent = ParseFactor(); // Right associative
            result = Math.Pow(result, exponent);
        }
        return result;
    }

    private double ParsePrimary()
    {
        SkipWhitespace();
        if (_pos >= _expr.Length)
            throw new Exception("Unexpected end of expression");

        var c = _expr[_pos];

        // Unary plus/minus
        if (c == '+')
        {
            _pos++;
            return ParsePrimary();
        }
        if (c == '-')
        {
            _pos++;
            return -ParsePrimary();
        }

        // Parentheses
        if (c == '(')
        {
            _pos++;
            var result = ParseExpression();
            SkipWhitespace();
            if (_pos >= _expr.Length || _expr[_pos] != ')')
                throw new Exception("Missing closing parenthesis");
            _pos++;
            return result;
        }

        // Numbers (decimal, hex, binary)
        if (char.IsDigit(c) || c == '.' || (_pos + 1 < _expr.Length && c == '0' && (_expr[_pos + 1] == 'x' || _expr[_pos + 1] == 'b')))
        {
            return ParseNumber();
        }

        // Word/Identifier (constants, functions)
        if (char.IsLetter(c) || c == 'π')
        {
            return ParseIdentifier();
        }

        throw new Exception("Unexpected character: " + c);
    }

    private double ParseNumber()
    {
        var start = _pos;
        if (_pos + 1 < _expr.Length && _expr[_pos] == '0' && _expr[_pos + 1] == 'x')
        {
            _pos += 2;
            while (_pos < _expr.Length && char.IsAsciiHexDigit(_expr[_pos]))
            {
                _pos++;
            }
            var hexStr = _expr[start.._pos];
            return Convert.ToInt64(hexStr, 16);
        }
        // Binary literals (0b...) are handled right below; an older revision kept a dead
        // "0b" detection branch here that compared _expr[_pos + 1] twice and had an empty body.
        if (_pos + 1 < _expr.Length && _expr[_pos] == '0' && _expr[_pos + 1] == 'b')
        {
            _pos += 2;
            while (_pos < _expr.Length && (_expr[_pos] == '0' || _expr[_pos] == '1'))
            {
                _pos++;
            }
            var binStr = _expr[start.._pos];
            return Convert.ToInt64(binStr[2..], 2);
        }

        var integerStart = _pos;
        while (_pos < _expr.Length && char.IsDigit(_expr[_pos]))
            _pos++;

        if (_pos - integerStart is >= 1 and <= 3)
        {
            while (HasThousandsSeparator())
            {
                _pos++;
                _pos += 3;
            }
        }

        while (_pos < _expr.Length && (char.IsDigit(_expr[_pos]) || _expr[_pos] == '.' || _expr[_pos] == 'e' || _expr[_pos] == 'E'))
        {
            // Handle scientific notation e.g. 1e+5, 2e-3
            if ((_expr[_pos] == 'e' || _expr[_pos] == 'E') && _pos + 1 < _expr.Length)
            {
                var next = _expr[_pos + 1];
                if (next == '+' || next == '-' || char.IsDigit(next))
                {
                    _pos += 2;
                    continue;
                }
            }
            _pos++;
        }
        var numStr = _expr[start.._pos].Replace(",", string.Empty, StringComparison.Ordinal);
        if (double.TryParse(numStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var val))
        {
            return val;
        }
        throw new Exception("Invalid number format: " + numStr);
    }

    private bool HasThousandsSeparator()
    {
        if (_pos >= _expr.Length || _expr[_pos] != ',')
            return false;

        var groupStart = _pos + 1;
        var groupEnd = groupStart + 3;
        if (groupEnd > _expr.Length)
            return false;

        for (var index = groupStart; index < groupEnd; index++)
        {
            if (!char.IsDigit(_expr[index]))
                return false;
        }

        return groupEnd == _expr.Length || !char.IsDigit(_expr[groupEnd]);
    }

    private double ParseIdentifier()
    {
        var start = _pos;
        if (_expr[_pos] == 'π')
        {
            _pos++;
            return Math.PI;
        }

        while (_pos < _expr.Length && char.IsLetterOrDigit(_expr[_pos]))
        {
            _pos++;
        }
        var id = _expr[start.._pos].ToLowerInvariant();

        // Constants
        if (id == "pi") return Math.PI;
        if (id == "e") return Math.E;

        // Functions
        SkipWhitespace();
        if (_pos >= _expr.Length || _expr[_pos] != '(')
        {
            throw new Exception("Expected '(' after function " + id);
        }
        _pos++; // Skip '('

        var args = new List<double>();
        while (true)
        {
            args.Add(ParseExpression());
            SkipWhitespace();
            if (_pos < _expr.Length && _expr[_pos] == ',')
            {
                _pos++;
                continue;
            }
            break;
        }

        if (_pos >= _expr.Length || _expr[_pos] != ')')
            throw new Exception("Expected ')' after function arguments");
        _pos++; // Skip ')'

        return id switch
        {
            "sin" => Math.Sin(args[0]),
            "cos" => Math.Cos(args[0]),
            "tan" => Math.Tan(args[0]),
            "asin" => Math.Asin(args[0]),
            "acos" => Math.Acos(args[0]),
            "atan" => Math.Atan(args[0]),
            "sqrt" => Math.Sqrt(args[0]),
            "cbrt" => Math.Cbrt(args[0]),
            "abs" => Math.Abs(args[0]),
            "ln" => Math.Log(args[0]),
            "log" => args.Count > 1 ? Math.Log(args[0], args[1]) : Math.Log10(args[0]),
            "log2" => Math.Log2(args[0]),
            "log10" => Math.Log10(args[0]),
            "exp" => Math.Exp(args[0]),
            "floor" => Math.Floor(args[0]),
            "ceil" => Math.Ceiling(args[0]),
            "round" => args.Count > 1 ? Math.Round(args[0], (int)args[1]) : Math.Round(args[0]),
            "min" => Math.Min(args[0], args[1]),
            "max" => Math.Max(args[0], args[1]),
            _ => throw new Exception("Unknown function: " + id),
        };
    }

    private void SkipWhitespace()
    {
        while (_pos < _expr.Length && char.IsWhiteSpace(_expr[_pos]))
        {
            _pos++;
        }
    }
}
