using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace IberiaSmartDisc.Core.Json
{
    public sealed class JsonParseException : FormatException
    {
        public JsonParseException(string message, int position)
            : base(message + " (posición " + position.ToString(CultureInfo.InvariantCulture) + ")")
        {
            Position = position;
        }

        public int Position { get; }
    }

    /// <summary>
    /// Lector JSON estricto (RFC 8259): sin comentarios, sin comas finales y con
    /// límite de anidamiento. Los manifiestos vienen de discos que no
    /// controlamos, así que cualquier cosa dudosa se rechaza.
    /// </summary>
    public static class JsonParser
    {
        public static JsonNode Parse(string text, int maxDepth = 64, bool rejectDuplicateKeys = true)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            return new Reader(text, maxDepth, rejectDuplicateKeys).ReadDocument();
        }

        private sealed class Reader
        {
            private readonly string _s;
            private readonly int _maxDepth;
            private readonly bool _rejectDuplicates;
            private int _pos;
            private int _depth;

            public Reader(string text, int maxDepth, bool rejectDuplicates)
            {
                _s = text;
                _maxDepth = maxDepth;
                _rejectDuplicates = rejectDuplicates;
            }

            public JsonNode ReadDocument()
            {
                if (_s.Length > 0 && _s[0] == (char)0xFEFF) _pos = 1;
                SkipWhitespace();
                var value = ReadValue();
                SkipWhitespace();
                if (_pos != _s.Length) throw Error("Contenido inesperado después del valor JSON");
                return value;
            }

            private JsonNode ReadValue()
            {
                if (_pos >= _s.Length) throw Error("Fin inesperado del JSON");
                char c = _s[_pos];
                switch (c)
                {
                    case '{':
                        return ReadObject();
                    case '[':
                        return ReadArray();
                    case '"':
                        return JsonNode.From(ReadString());
                    case 't':
                        Expect("true");
                        return JsonNode.From(true);
                    case 'f':
                        Expect("false");
                        return JsonNode.From(false);
                    case 'n':
                        Expect("null");
                        return JsonNode.Null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw Error("Carácter inesperado '" + c + "'");
                }
            }

            private JsonNode ReadObject()
            {
                Enter();
                _pos++;
                var result = JsonNode.NewObject();
                var seen = _rejectDuplicates ? new HashSet<string>(StringComparer.Ordinal) : null;
                SkipWhitespace();
                if (Peek() == '}')
                {
                    _pos++;
                    _depth--;
                    return result;
                }
                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != '"') throw Error("Se esperaba el nombre de una propiedad");
                    int keyPosition = _pos;
                    string key = ReadString();
                    SkipWhitespace();
                    if (Peek() != ':') throw Error("Se esperaba ':'");
                    _pos++;
                    SkipWhitespace();
                    var value = ReadValue();
                    if (seen != null)
                    {
                        if (!seen.Add(key)) throw new JsonParseException("Propiedad duplicada", keyPosition);
                        result.AddProperty(key, value);
                    }
                    else
                    {
                        result.Set(key, value);
                    }
                    SkipWhitespace();
                    char c = Peek();
                    _pos++;
                    if (c == ',') continue;
                    if (c == '}') break;
                    _pos--;
                    throw Error("Se esperaba ',' o '}'");
                }
                _depth--;
                return result;
            }

            private JsonNode ReadArray()
            {
                Enter();
                _pos++;
                var result = JsonNode.NewArray();
                SkipWhitespace();
                if (Peek() == ']')
                {
                    _pos++;
                    _depth--;
                    return result;
                }
                while (true)
                {
                    SkipWhitespace();
                    result.Add(ReadValue());
                    SkipWhitespace();
                    char c = Peek();
                    _pos++;
                    if (c == ',') continue;
                    if (c == ']') break;
                    _pos--;
                    throw Error("Se esperaba ',' o ']'");
                }
                _depth--;
                return result;
            }

            private string ReadString()
            {
                _pos++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (_pos >= _s.Length) throw Error("Cadena sin cerrar");
                    char c = _s[_pos++];
                    if (c == '"') return sb.ToString();
                    if (c < 0x20) throw Error("Carácter de control sin escapar en una cadena");
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (_pos >= _s.Length) throw Error("Escape incompleto");
                    char e = _s[_pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_pos + 4 > _s.Length) throw Error("Escape \\u incompleto");
                            int code = 0;
                            for (int i = 0; i < 4; i++)
                            {
                                int digit = HexValue(_s[_pos + i]);
                                if (digit < 0) throw Error("Escape \\u no válido");
                                code = (code << 4) | digit;
                            }
                            _pos += 4;
                            sb.Append((char)code);
                            break;
                        default:
                            throw Error("Escape no válido '\\" + e + "'");
                    }
                }
            }

            private JsonNode ReadNumber()
            {
                int start = _pos;
                if (Peek() == '-') _pos++;
                if (Peek() == '0')
                {
                    _pos++;
                }
                else if (IsDigit(Peek()))
                {
                    while (IsDigit(Peek())) _pos++;
                }
                else
                {
                    throw Error("Número no válido");
                }
                if (Peek() == '.')
                {
                    _pos++;
                    if (!IsDigit(Peek())) throw Error("Número no válido");
                    while (IsDigit(Peek())) _pos++;
                }
                if (Peek() == 'e' || Peek() == 'E')
                {
                    _pos++;
                    if (Peek() == '+' || Peek() == '-') _pos++;
                    if (!IsDigit(Peek())) throw Error("Número no válido");
                    while (IsDigit(Peek())) _pos++;
                }
                return JsonNode.FromRawNumber(_s.Substring(start, _pos - start));
            }

            private void Expect(string word)
            {
                if (_pos + word.Length > _s.Length || string.CompareOrdinal(_s, _pos, word, 0, word.Length) != 0)
                {
                    throw Error("Valor no válido");
                }
                _pos += word.Length;
            }

            private void Enter()
            {
                if (++_depth > _maxDepth) throw Error("Anidamiento demasiado profundo");
            }

            private char Peek() => _pos < _s.Length ? _s[_pos] : '\0';

            private void SkipWhitespace()
            {
                while (_pos < _s.Length)
                {
                    char c = _s[_pos];
                    if (c != ' ' && c != '\t' && c != '\n' && c != '\r') break;
                    _pos++;
                }
            }

            private static bool IsDigit(char c) => c >= '0' && c <= '9';

            private static int HexValue(char c)
            {
                if (c >= '0' && c <= '9') return c - '0';
                if (c >= 'a' && c <= 'f') return c - 'a' + 10;
                if (c >= 'A' && c <= 'F') return c - 'A' + 10;
                return -1;
            }

            private JsonParseException Error(string message) => new JsonParseException(message, _pos);
        }
    }
}
