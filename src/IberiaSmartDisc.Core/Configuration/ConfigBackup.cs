using System;
using System.Globalization;
using IberiaSmartDisc.Core.Json;
using IberiaSmartDisc.Core.Platforms;

namespace IberiaSmartDisc.Core.Configuration
{
    /// <summary>
    /// Exportar e importar la configuración en un solo archivo, para copias de
    /// seguridad o para llevarla a otro PC. Las rutas pueden no existir en el
    /// otro equipo: el programa lo detecta y vuelve a preguntar.
    /// </summary>
    public static class ConfigBackup
    {
        public const string Marker = "iberiaSmartDiscBackup";

        public static JsonNode Export(AppSettings settings, GameLibrary library, DateTime nowUtc)
        {
            return JsonNode.NewObject()
                .Set(Marker, 1)
                .Set("exportedAt", nowUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))
                .Set("settings", settings.ToJson())
                .Set("library", library.ToJson());
        }

        /// <summary>
        /// Lee una copia. Los ejecutables locales no se importan: un archivo ajeno no
        /// debe dejar programas que se abran al meter un disco, y en otro PC las rutas
        /// suelen ser distintas. Devuelve cuántos se han descartado.
        /// </summary>
        public static int Import(JsonNode json, out AppSettings settings, out GameLibrary library)
        {
            if (json.Kind != JsonKind.Object || json.GetInt64(Marker) != 1)
            {
                throw new FormatException("El archivo no es una copia de seguridad de Iberia Smart Disc.");
            }
            settings = AppSettings.FromJson(json.GetObject("settings"));
            library = GameLibrary.FromJson(json.GetObject("library"));
            int dropped = 0;
            foreach (var record in library.Games)
            {
                if (record.LocalExecutable != null) dropped++;
                record.LocalExecutable = null;
                if (record.PreferredKind == LaunchKind.Local)
                {
                    record.PreferredKey = null;
                    record.AlwaysUse = false;
                }
            }
            return dropped;
        }
    }
}
