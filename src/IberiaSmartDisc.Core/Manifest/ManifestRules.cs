using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using IberiaSmartDisc.Core.Common;

namespace IberiaSmartDisc.Core.Manifest
{
    /// <summary>
    /// Reglas de los identificadores y textos del manifiesto. Son las mismas que
    /// documenta schema/iberia-disc.schema.json y que debe respetar la web al
    /// generar el archivo (tests/fixtures/manifests comprueba que coinciden).
    /// </summary>
    public static class ManifestRules
    {
        private const RegexOptions Options = RegexOptions.CultureInvariant;

        // \z y no $: en .NET, $ también acepta un salto de línea final.
        private static readonly Regex GameIdPattern = new Regex(@"^[a-z0-9]+(?:-[a-z0-9]+)*\z", Options);
        private static readonly Regex DiscIdPattern = new Regex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}\z", Options);
        private static readonly Regex NumericIdPattern = new Regex(@"^[1-9][0-9]{0,11}\z", Options);
        private static readonly Regex EpicTokenPattern = new Regex(@"^[A-Za-z0-9._-]{1,128}\z", Options);

        /// <summary>Nombres de dispositivo de Windows: no sirven como nombre de archivo en la caché.</summary>
        public static readonly IReadOnlyCollection<string> ReservedNames = new HashSet<string>(
            new[]
            {
                "con", "prn", "aux", "nul",
                "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
                "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
            },
            StringComparer.OrdinalIgnoreCase);

        public const int MaxGameIdLength = 64;
        public const int MaxArgumentsLength = 512;

        public static bool IsGameId(string? value) =>
            value != null && value.Length <= MaxGameIdLength && GameIdPattern.IsMatch(value) && !ReservedNames.Contains(value);

        public static bool IsDiscId(string? value) => value != null && DiscIdPattern.IsMatch(value);

        /// <summary>AppID de Steam o productId de GOG.</summary>
        public static bool IsNumericId(string? value) => value != null && NumericIdPattern.IsMatch(value);

        public static bool IsEpicToken(string? value) => value != null && EpicTokenPattern.IsMatch(value);

        /// <summary>Texto visible: con algo más que espacios, sin caracteres engañosos y con un máximo en puntos de código.</summary>
        public static bool IsDisplayText(string? value, int maxLength)
        {
            if (value == null || !TextSafety.IsSafe(value)) return false;
            string trimmed = value.Trim();
            return trimmed.Length > 0 && TextSafety.CodePointLength(value) <= maxLength;
        }

        public static bool IsArguments(string? value) =>
            value != null && TextSafety.CodePointLength(value) <= MaxArgumentsLength && TextSafety.IsSafe(value);
    }
}
