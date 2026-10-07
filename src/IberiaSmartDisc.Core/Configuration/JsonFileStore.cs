using System;
using System.Globalization;
using System.IO;
using System.Text;
using IberiaSmartDisc.Core.Json;

namespace IberiaSmartDisc.Core.Configuration
{
    /// <summary>
    /// Lectura y escritura de los JSON de configuración. Se escribe en un
    /// temporal y luego se sustituye, para que un corte de luz no deje el
    /// archivo a medias. Un archivo dañado se aparta (.corrupt-fecha) y el
    /// programa sigue con los valores por defecto.
    /// </summary>
    public static class JsonFileStore
    {
        public const int MaxFileBytes = 4 * 1024 * 1024;

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public static JsonNode? Load(string path, out string? problem)
        {
            problem = null;
            if (!File.Exists(path)) return null;
            try
            {
                var info = new FileInfo(path);
                if (info.Length > MaxFileBytes) throw new FormatException("El archivo es demasiado grande.");
                return JsonParser.Parse(File.ReadAllText(path, Encoding.UTF8), maxDepth: 32, rejectDuplicateKeys: false);
            }
            catch (FormatException ex)
            {
                problem = Path.GetFileName(path) + " estaba dañado y se ha apartado: " + ex.Message;
                QuarantineCorrupt(path);
                return null;
            }
        }

        public static void Save(string path, JsonNode node)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            string temporary = path + ".tmp";
            byte[] bytes = Utf8NoBom.GetBytes(JsonWriter.Write(node));
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            if (!File.Exists(path))
            {
                File.Move(temporary, path);
                return;
            }
            try
            {
                File.Replace(temporary, path, null, ignoreMetadataErrors: true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is PlatformNotSupportedException)
            {
                // Algunos antivirus bloquean Replace un instante; copiar encima es el plan B.
                File.Copy(temporary, path, overwrite: true);
                File.Delete(temporary);
            }
        }

        private static void QuarantineCorrupt(string path)
        {
            try
            {
                string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                File.Move(path, path + ".corrupt-" + stamp);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }
}
