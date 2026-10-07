using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IberiaSmartDisc.Core.Common;

namespace IberiaSmartDisc.Core.Manifest
{
    /// <summary>
    /// Rutas dentro del disco. El manifiesto solo puede apuntar a archivos que
    /// estén en el propio disco: nada de rutas absolutas, "..", unidades,
    /// flujos alternativos (":") ni nombres reservados de Windows.
    /// </summary>
    public static class DiscRelativePath
    {
        public const int MaxLength = 240;

        private static readonly char[] Separators = { '\\', '/' };
        private static readonly char[] InvalidChars = { '<', '>', ':', '"', '|', '?', '*' };

        private static readonly HashSet<string> ReservedNames = new HashSet<string>(
            new[]
            {
                "CON", "PRN", "AUX", "NUL",
                "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
            },
            StringComparer.OrdinalIgnoreCase);

        public static bool TryNormalize(string? input, string[]? allowedExtensions, out string normalized, out string error)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(input))
            {
                error = "la ruta está vacía";
                return false;
            }
            string value = input!;
            if (TextSafety.CodePointLength(value) > MaxLength)
            {
                error = "la ruta es demasiado larga";
                return false;
            }
            if (value.IndexOfAny(InvalidChars) >= 0 || !TextSafety.IsSafe(value))
            {
                error = "la ruta contiene caracteres no permitidos";
                return false;
            }
            if (value[0] == '\\' || value[0] == '/')
            {
                error = "la ruta debe ser relativa a la raíz del disco";
                return false;
            }

            string[] parts = value.Split(Separators);
            foreach (string part in parts)
            {
                if (part.Length == 0)
                {
                    error = "la ruta tiene separadores vacíos";
                    return false;
                }
                if (part == "." || part == "..")
                {
                    error = "la ruta no puede salir de la carpeta del disco";
                    return false;
                }
                char last = part[part.Length - 1];
                if (last == '.' || last == ' ' || part[0] == ' ')
                {
                    error = "la ruta contiene un nombre no válido";
                    return false;
                }
                string stem = part.Split('.')[0];
                if (ReservedNames.Contains(stem))
                {
                    error = "la ruta usa un nombre reservado de Windows";
                    return false;
                }
            }

            if (allowedExtensions != null && allowedExtensions.Length > 0)
            {
                string extension = Path.GetExtension(parts[parts.Length - 1]);
                if (!allowedExtensions.Any(e => string.Equals(e, extension, StringComparison.OrdinalIgnoreCase)))
                {
                    error = "el archivo debe ser " + string.Join(" o ", allowedExtensions);
                    return false;
                }
            }

            normalized = string.Join("\\", parts);
            error = string.Empty;
            return true;
        }

        /// <summary>
        /// Combina la raíz del disco con una ruta ya normalizada y comprueba que el
        /// resultado sigue dentro de la raíz.
        /// </summary>
        public static string Combine(string root, string normalizedRelative)
        {
            if (!TryNormalize(normalizedRelative, null, out var safe, out var error))
            {
                throw new ManifestException(ManifestError.InvalidField, "Ruta del disco no válida: " + error + ".");
            }
            string rootFull = Path.GetFullPath(root);
            string prefix = rootFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? rootFull
                : rootFull + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(rootFull, safe.Replace('\\', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new ManifestException(ManifestError.InvalidField, "La ruta sale de la raíz del disco.");
            }
            return full;
        }
    }
}
