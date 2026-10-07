using System.Globalization;
using System.Text;

namespace IberiaSmartDisc.Core.Json
{
    public static class JsonWriter
    {
        public static string Write(JsonNode node, bool indented = true)
        {
            var sb = new StringBuilder();
            WriteValue(sb, node, indented, 0);
            if (indented) sb.Append('\n');
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, JsonNode node, bool indented, int level)
        {
            switch (node.Kind)
            {
                case JsonKind.Null:
                    sb.Append("null");
                    break;
                case JsonKind.Boolean:
                    sb.Append(node.BooleanValue ? "true" : "false");
                    break;
                case JsonKind.Number:
                    sb.Append(node.RawText);
                    break;
                case JsonKind.String:
                    WriteString(sb, node.AsString()!);
                    break;
                case JsonKind.Array:
                    if (node.Items.Count == 0)
                    {
                        sb.Append("[]");
                        break;
                    }
                    sb.Append('[');
                    for (int i = 0; i < node.Items.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        NewLine(sb, indented, level + 1);
                        WriteValue(sb, node.Items[i], indented, level + 1);
                    }
                    NewLine(sb, indented, level);
                    sb.Append(']');
                    break;
                case JsonKind.Object:
                    if (node.Properties.Count == 0)
                    {
                        sb.Append("{}");
                        break;
                    }
                    sb.Append('{');
                    for (int i = 0; i < node.Properties.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        NewLine(sb, indented, level + 1);
                        WriteString(sb, node.Properties[i].Key);
                        sb.Append(indented ? ": " : ":");
                        WriteValue(sb, node.Properties[i].Value, indented, level + 1);
                    }
                    NewLine(sb, indented, level);
                    sb.Append('}');
                    break;
            }
        }

        private static void NewLine(StringBuilder sb, bool indented, int level)
        {
            if (!indented) return;
            sb.Append('\n').Append(' ', level * 2);
        }

        private static void WriteString(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20 || c == (char)0x2028 || c == (char)0x2029)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
