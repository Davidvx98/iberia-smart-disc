namespace IberiaSmartDisc.Core.Common
{
    /// <summary>
    /// Caracteres que no se admiten en textos y rutas que vienen del disco:
    /// controles, marcas de dirección (bidi), caracteres de ancho cero,
    /// separadores de línea, uso privado y etiquetas. Con ellos un disco podría
    /// hacer que «Manual[U+202E]fdp.exe» se vea como «Manualexe.pdf» en un
    /// diálogo de confianza. La lista es explícita (no por categorías Unicode) para que
    /// schema/iberia-disc.schema.json pueda expresar exactamente la misma regla.
    /// </summary>
    public static class TextSafety
    {
        /// <summary>
        /// Rangos del plano básico prohibidos, en el mismo orden que la clase
        /// <c>FORBIDDEN</c> del esquema JSON.
        /// </summary>
        private static readonly int[] ForbiddenRanges =
        {
            0x0000, 0x001F,
            0x007F, 0x009F,
            0x00AD, 0x00AD,
            0x0600, 0x0605,
            0x061C, 0x061C,
            0x06DD, 0x06DD,
            0x070F, 0x070F,
            0x08E2, 0x08E2,
            0x180E, 0x180E,
            0x200B, 0x200F,
            0x2028, 0x202E,
            0x2060, 0x206F,
            0xE000, 0xF8FF,
            0xFEFF, 0xFEFF,
            0xFFF9, 0xFFFB,
        };

        /// <summary>Etiquetas (U+E0000…) y uso privado de los planos 15 y 16.</summary>
        private const int FirstForbiddenSupplementary = 0xE0000;

        public static bool IsSafe(string? text)
        {
            if (text == null) return false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])) return false;
                    if (char.ConvertToUtf32(c, text[i + 1]) >= FirstForbiddenSupplementary) return false;
                    i++;
                    continue;
                }
                if (char.IsLowSurrogate(c) || IsForbidden(c)) return false;
            }
            return true;
        }

        /// <summary>Longitud en puntos de código, como cuenta maxLength en JSON Schema.</summary>
        public static int CodePointLength(string text)
        {
            int length = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
                length++;
            }
            return length;
        }

        private static bool IsForbidden(char c)
        {
            for (int i = 0; i < ForbiddenRanges.Length; i += 2)
            {
                if (c >= ForbiddenRanges[i] && c <= ForbiddenRanges[i + 1]) return true;
            }
            return false;
        }
    }
}
