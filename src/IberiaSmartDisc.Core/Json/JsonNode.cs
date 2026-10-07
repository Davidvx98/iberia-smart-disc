using System;
using System.Collections.Generic;
using System.Globalization;

namespace IberiaSmartDisc.Core.Json
{
    public enum JsonKind
    {
        Null,
        Boolean,
        Number,
        String,
        Array,
        Object,
    }

    /// <summary>
    /// Árbol JSON mínimo. El proyecto evita dependencias externas para que el
    /// programa siga siendo un único ejecutable pequeño; los objetos conservan
    /// el orden de sus propiedades para que los archivos guardados sean legibles.
    /// </summary>
    public sealed class JsonNode
    {
        private static readonly IReadOnlyList<JsonNode> NoItems = new JsonNode[0];
        private static readonly IReadOnlyList<KeyValuePair<string, JsonNode>> NoProperties = new KeyValuePair<string, JsonNode>[0];

        private readonly List<KeyValuePair<string, JsonNode>>? _properties;
        private readonly List<JsonNode>? _items;
        private readonly string? _text;
        private readonly bool _boolean;

        private JsonNode(JsonKind kind, string? text = null, bool boolean = false)
        {
            Kind = kind;
            _text = text;
            _boolean = boolean;
            if (kind == JsonKind.Object) _properties = new List<KeyValuePair<string, JsonNode>>();
            else if (kind == JsonKind.Array) _items = new List<JsonNode>();
        }

        public static JsonNode Null { get; } = new JsonNode(JsonKind.Null);

        public JsonKind Kind { get; }

        public bool IsNull => Kind == JsonKind.Null;

        public IReadOnlyList<KeyValuePair<string, JsonNode>> Properties =>
            (IReadOnlyList<KeyValuePair<string, JsonNode>>?)_properties ?? NoProperties;

        public IReadOnlyList<JsonNode> Items => (IReadOnlyList<JsonNode>?)_items ?? NoItems;

        internal string? RawText => _text;

        internal bool BooleanValue => _boolean;

        public static JsonNode NewObject() => new JsonNode(JsonKind.Object);

        public static JsonNode NewArray() => new JsonNode(JsonKind.Array);

        public static JsonNode From(string? value) => value == null ? Null : new JsonNode(JsonKind.String, value);

        public static JsonNode From(bool value) => new JsonNode(JsonKind.Boolean, boolean: value);

        public static JsonNode From(long value) =>
            new JsonNode(JsonKind.Number, value.ToString(CultureInfo.InvariantCulture));

        internal static JsonNode FromRawNumber(string raw) => new JsonNode(JsonKind.Number, raw);

        public static JsonNode FromStrings(IEnumerable<string> values)
        {
            var array = NewArray();
            foreach (var value in values) array.Add(From(value));
            return array;
        }

        public JsonNode? Get(string name)
        {
            if (_properties == null) return null;
            foreach (var property in _properties)
            {
                if (string.Equals(property.Key, name, StringComparison.Ordinal)) return property.Value;
            }
            return null;
        }

        public JsonNode Set(string name, JsonNode value)
        {
            if (_properties == null) throw new InvalidOperationException("El nodo JSON no es un objeto.");
            if (value == null) throw new ArgumentNullException(nameof(value));
            for (int i = 0; i < _properties.Count; i++)
            {
                if (string.Equals(_properties[i].Key, name, StringComparison.Ordinal))
                {
                    _properties[i] = new KeyValuePair<string, JsonNode>(name, value);
                    return this;
                }
            }
            _properties.Add(new KeyValuePair<string, JsonNode>(name, value));
            return this;
        }

        public JsonNode Set(string name, string? value) => Set(name, From(value));

        public JsonNode Set(string name, bool value) => Set(name, From(value));

        public JsonNode Set(string name, long value) => Set(name, From(value));

        public JsonNode Add(JsonNode item)
        {
            if (_items == null) throw new InvalidOperationException("El nodo JSON no es un array.");
            _items.Add(item ?? throw new ArgumentNullException(nameof(item)));
            return this;
        }

        internal void AddProperty(string name, JsonNode value) =>
            _properties!.Add(new KeyValuePair<string, JsonNode>(name, value));

        public string? AsString() => Kind == JsonKind.String ? _text : null;

        public bool? AsBoolean() => Kind == JsonKind.Boolean ? _boolean : (bool?)null;

        /// <summary>
        /// Valor entero. Como en JSON Schema, 1.0 y 6.2e2 también son enteros;
        /// 1.5 o un número fuera de rango devuelven null.
        /// </summary>
        public long? AsInt64()
        {
            if (Kind != JsonKind.Number) return null;
            if (long.TryParse(_text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)) return value;
            if (!decimal.TryParse(_text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return null;
            if (number != decimal.Truncate(number) || number < long.MinValue || number > long.MaxValue) return null;
            return (long)number;
        }

        public string? GetString(string name) => Get(name)?.AsString();

        public bool? GetBoolean(string name) => Get(name)?.AsBoolean();

        public long? GetInt64(string name) => Get(name)?.AsInt64();

        public JsonNode? GetObject(string name)
        {
            var node = Get(name);
            return node != null && node.Kind == JsonKind.Object ? node : null;
        }

        public JsonNode? GetArray(string name)
        {
            var node = Get(name);
            return node != null && node.Kind == JsonKind.Array ? node : null;
        }

        public override string ToString() => JsonWriter.Write(this, indented: false);
    }
}
