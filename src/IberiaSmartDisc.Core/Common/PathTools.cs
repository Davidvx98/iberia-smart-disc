using System;
using System.IO;
using System.Security;
using System.Text;

namespace IberiaSmartDisc.Core.Common
{
    public static class PathTools
    {
        private static readonly char[] InvalidNameChars = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };

        /// <summary>
        /// Normaliza una carpeta (separadores, ruta completa, sin barra final).
        /// Devuelve null si la ruta no es válida en este sistema.
        /// </summary>
        public static string? NormalizeDirectory(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                string unified = path!.Trim().Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                string full = Path.GetFullPath(unified);
                string root = Path.GetPathRoot(full) ?? string.Empty;
                while (full.Length > root.Length && full[full.Length - 1] == Path.DirectorySeparatorChar)
                {
                    full = full.Substring(0, full.Length - 1);
                }
                return full;
            }
            catch (Exception ex) when (IsPathException(ex))
            {
                return null;
            }
        }

        public static string? NormalizeFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                return Path.GetFullPath(path!.Trim().Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
            }
            catch (Exception ex) when (IsPathException(ex))
            {
                return null;
            }
        }

        /// <summary>
        /// Rutas de red (\\servidor\recurso, \\?\UNC\…). Se ignoran en bibliotecas y
        /// carpetas de juegos: tocarlas al meter un disco abriría conexiones SMB.
        /// </summary>
        public static bool IsNetworkPath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string p = path!.Trim().Replace('/', '\\');
            return p.StartsWith("\\\\", StringComparison.Ordinal);
        }

        /// <summary>Compara rutas como lo hace Windows: sin distinguir mayúsculas.</summary>
        public static bool SamePath(string? a, string? b)
        {
            var left = NormalizeFile(a);
            var right = NormalizeFile(b);
            return left != null && right != null && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Nombre simple de archivo o carpeta, sin separadores ni trucos.</summary>
        public static bool IsPlainName(string? name, int maxLength = 128)
        {
            if (string.IsNullOrEmpty(name) || TextSafety.CodePointLength(name!) > maxLength) return false;
            if (name == "." || name == "..") return false;
            if (name!.IndexOfAny(InvalidNameChars) >= 0 || !TextSafety.IsSafe(name)) return false;
            char last = name[name.Length - 1];
            return last != '.' && last != ' ';
        }

        /// <summary>Normaliza un nombre para compararlo: minúsculas y solo letras y dígitos.</summary>
        public static string Simplify(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char c in text.Normalize(NormalizationForm.FormD))
            {
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        public static bool IsPathException(Exception ex) =>
            ex is ArgumentException
            || ex is NotSupportedException
            || ex is PathTooLongException
            || ex is IOException
            || ex is UnauthorizedAccessException
            || ex is SecurityException;
    }
}
