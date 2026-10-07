using System;
using System.Collections.Generic;
using System.Text;

namespace IberiaSmartDisc.Core.Vdf
{
    /// <summary>
    /// Nodo KeyValues de Valve (libraryfolders.vdf, appmanifest_*.acf). Las
    /// claves se comparan sin distinguir mayúsculas, como hace Steam.
    /// </summary>
    public sealed class VdfNode
    {
        private static readonly IReadOnlyList<KeyValuePair<string, VdfNode>> NoChildren = new KeyValuePair<string, VdfNode>[0];
        private readonly List<KeyValuePair<string, VdfNode>>? _children;

        internal VdfNode()
        {
            _children = new List<KeyValuePair<string, VdfNode>>();
        }

        internal VdfNode(string value)
        {
            Value = value;
        }

        public string? Value { get; }

        public bool IsObject => _children != null;

        public IReadOnlyList<KeyValuePair<string, VdfNode>> Children =>
            (IReadOnlyList<KeyValuePair<string, VdfNode>>?)_children ?? NoChildren;

        public VdfNode? Get(string key)
        {
            if (_children == null) return null;
            foreach (var child in _children)
            {
                if (string.Equals(child.Key, key, StringComparison.OrdinalIgnoreCase)) return child.Value;
            }
            return null;
        }

        public string? GetValue(string key) => Get(key)?.Value;

        internal void Add(string key, VdfNode value) => _children!.Add(new KeyValuePair<string, VdfNode>(key, value));
    }

    public static class VdfParser
    {
        private const int MaxDepth = 32;

        public static VdfNode Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var tokenizer = new Tokenizer(text);
            var root = new VdfNode();
            ReadBody(tokenizer, root, 0, topLevel: true);
            return root;
        }

        private static void ReadBody(Tokenizer tokens, VdfNode node, int depth, bool topLevel)
        {
            while (true)
            {
                var token = tokens.Next();
                if (token == null)
                {
                    if (!topLevel) throw new FormatException("VDF: falta '}' de cierre.");
                    return;
                }
                if (token.Kind == TokenKind.Close)
                {
                    if (topLevel) throw new FormatException("VDF: '}' inesperado.");
                    return;
                }
                if (token.Kind == TokenKind.Open) throw new FormatException("VDF: '{' inesperado.");

                string key = token.Text;
                var next = tokens.Next();
                if (next != null && next.IsConditional) next = tokens.Next();
                if (next == null) throw new FormatException("VDF: falta el valor de \"" + key + "\".");

                if (next.Kind == TokenKind.Open)
                {
                    if (depth + 1 > MaxDepth) throw new FormatException("VDF: anidamiento demasiado profundo.");
                    var child = new VdfNode();
                    ReadBody(tokens, child, depth + 1, topLevel: false);
                    node.Add(key, child);
                }
                else if (next.Kind == TokenKind.Text)
                {
                    node.Add(key, new VdfNode(next.Text));
                    var after = tokens.Peek();
                    if (after != null && after.IsConditional) tokens.Next();
                }
                else
                {
                    throw new FormatException("VDF: '}' inesperado tras \"" + key + "\".");
                }
            }
        }

        private enum TokenKind
        {
            Text,
            Open,
            Close,
        }

        private sealed class Token
        {
            public Token(TokenKind kind, string text, bool quoted)
            {
                Kind = kind;
                Text = text;
                Quoted = quoted;
            }

            public TokenKind Kind { get; }

            public string Text { get; }

            public bool Quoted { get; }

            /// <summary>Condicionales de plataforma como [$WIN32], que se ignoran.</summary>
            public bool IsConditional => Kind == TokenKind.Text && !Quoted && Text.Length > 1 && Text[0] == '[';
        }

        private sealed class Tokenizer
        {
            private readonly string _s;
            private int _pos;
            private Token? _peeked;

            public Tokenizer(string text)
            {
                _s = text;
                if (_s.Length > 0 && _s[0] == (char)0xFEFF) _pos = 1;
            }

            public Token? Peek() => _peeked ??= Read();

            public Token? Next()
            {
                if (_peeked != null)
                {
                    var token = _peeked;
                    _peeked = null;
                    return token;
                }
                return Read();
            }

            private Token? Read()
            {
                SkipWhitespaceAndComments();
                if (_pos >= _s.Length) return null;
                char c = _s[_pos];
                if (c == '{')
                {
                    _pos++;
                    return new Token(TokenKind.Open, "{", false);
                }
                if (c == '}')
                {
                    _pos++;
                    return new Token(TokenKind.Close, "}", false);
                }
                if (c == '"') return ReadQuoted();

                int start = _pos;
                while (_pos < _s.Length)
                {
                    char d = _s[_pos];
                    if (char.IsWhiteSpace(d) || d == '{' || d == '}' || d == '"') break;
                    _pos++;
                }
                return new Token(TokenKind.Text, _s.Substring(start, _pos - start), false);
            }

            private Token ReadQuoted()
            {
                _pos++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (_pos >= _s.Length) throw new FormatException("VDF: cadena sin cerrar.");
                    char c = _s[_pos++];
                    if (c == '"') break;
                    if (c == '\\' && _pos < _s.Length)
                    {
                        char e = _s[_pos];
                        switch (e)
                        {
                            case '\\': sb.Append('\\'); _pos++; continue;
                            case '"': sb.Append('"'); _pos++; continue;
                            case 'n': sb.Append('\n'); _pos++; continue;
                            case 't': sb.Append('\t'); _pos++; continue;
                            case 'r': sb.Append('\r'); _pos++; continue;
                        }
                        // Escape desconocido: Steam conserva la barra tal cual.
                        sb.Append('\\');
                        continue;
                    }
                    sb.Append(c);
                }
                return new Token(TokenKind.Text, sb.ToString(), true);
            }

            private void SkipWhitespaceAndComments()
            {
                while (_pos < _s.Length)
                {
                    char c = _s[_pos];
                    if (char.IsWhiteSpace(c))
                    {
                        _pos++;
                        continue;
                    }
                    if (c == '/' && _pos + 1 < _s.Length && _s[_pos + 1] == '/')
                    {
                        while (_pos < _s.Length && _s[_pos] != '\n') _pos++;
                        continue;
                    }
                    break;
                }
            }
        }
    }
}
