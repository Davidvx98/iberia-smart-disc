using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IberiaSmartDisc.Core.Common;
using IberiaSmartDisc.Core.Json;

namespace IberiaSmartDisc.Core.Manifest
{
    /// <summary>
    /// Convierte el texto de iberia-disc.json en un <see cref="DiscManifest"/>
    /// validado. Los campos desconocidos se ignoran: así un disco nuevo con
    /// extras opcionales sigue funcionando en versiones antiguas del programa.
    /// Solo un cambio incompatible sube "format".
    /// </summary>
    public static class ManifestParser
    {
        private static readonly string[] ExecutableExtensions = { ".exe" };
        private static readonly string[] InstallerExtensions = { ".exe", ".msi" };
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp" };
        private static readonly string[] IconExtensions = { ".ico" };

        public static DiscManifest Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));

            JsonNode root;
            try
            {
                root = JsonParser.Parse(json, maxDepth: 16, rejectDuplicateKeys: true);
            }
            catch (JsonParseException ex)
            {
                throw new ManifestException(ManifestError.Malformed, "El archivo no es JSON válido: " + ex.Message, null, ex);
            }
            if (root.Kind != JsonKind.Object)
            {
                throw new ManifestException(ManifestError.Malformed, "El manifiesto debe ser un objeto JSON.");
            }

            if (root.Get("iberiaDisc")?.AsBoolean() != true)
            {
                throw new ManifestException(ManifestError.NotIberiaDisc, "Falta \"iberiaDisc\": true.", "iberiaDisc");
            }

            long? format = root.Get("format")?.AsInt64();
            if (format == null) throw Field("format", "debe ser un número entero");
            if (format < 1) throw Field("format", "debe ser 1 o mayor");
            if (format > DiscManifest.SupportedFormat)
            {
                throw new ManifestException(
                    ManifestError.UnsupportedFormat,
                    "Este disco usa el formato " + format.Value.ToString(CultureInfo.InvariantCulture)
                        + " y esta versión solo entiende hasta el " + DiscManifest.SupportedFormat.ToString(CultureInfo.InvariantCulture)
                        + ". Actualiza Iberia Smart Disc.",
                    "format");
            }

            var manifest = new DiscManifest
            {
                Format = (int)format.Value,
                RawJson = json,
                Sha256 = Sha256Hex(json),
            };

            manifest.DiscId = OptionalString(root, "discId", "discId");
            if (manifest.DiscId != null && !ManifestRules.IsDiscId(manifest.DiscId))
            {
                throw Field("discId", "solo admite letras, números, '.', '_' y '-' (máximo 64)");
            }

            ReadGame(root, manifest);
            ReadPlatforms(root, manifest);
            ReadDiscLaunch(root, manifest);
            ReadDiscSet(root, manifest);
            ReadArtwork(root, manifest);
            ReadLocalHints(root, manifest);
            return manifest;
        }

        private static void ReadGame(JsonNode root, DiscManifest manifest)
        {
            // Formato recomendado: "game": { "id", "name", "edition" }. Se aceptan
            // también "gameId" y "name" en la raíz (primeros borradores del formato).
            var game = OptionalObject(root, "game", "game");
            string? id = game != null ? OptionalString(game, "id", "game.id") : null;
            string? name = game != null ? OptionalString(game, "name", "game.name") : null;
            string? edition = game != null ? OptionalString(game, "edition", "game.edition") : null;
            id ??= OptionalString(root, "gameId", "gameId");
            name ??= OptionalString(root, "name", "name");
            edition ??= OptionalString(root, "edition", "edition");

            if (id == null) throw Field("game.id", "es obligatorio");
            if (!ManifestRules.IsGameId(id))
            {
                throw Field("game.id", "debe ir en minúsculas con guiones, por ejemplo \"portal-2\" (máximo 64)");
            }
            if (!ManifestRules.IsDisplayText(name, 120)) throw Field("game.name", "es obligatorio (1-120 caracteres, sin caracteres de control ni invisibles)");
            if (edition != null && !ManifestRules.IsDisplayText(edition, 80)) throw Field("game.edition", "admite 1-80 caracteres sin caracteres de control ni invisibles");

            manifest.GameId = id;
            manifest.GameName = name!.Trim();
            manifest.Edition = edition?.Trim();
        }

        private static void ReadPlatforms(JsonNode root, DiscManifest manifest)
        {
            var platforms = OptionalObject(root, "platforms", "platforms");
            if (platforms == null) return;

            var steam = OptionalObject(platforms, "steam", "platforms.steam");
            if (steam != null) manifest.SteamAppIds = ReadIds(steam, "appId", "appIds", "platforms.steam");

            var gog = OptionalObject(platforms, "gog", "platforms.gog");
            if (gog != null) manifest.GogProductIds = ReadIds(gog, "productId", "productIds", "platforms.gog");

            var epic = OptionalObject(platforms, "epic", "platforms.epic");
            if (epic != null)
            {
                string? appName = OptionalEpicToken(epic, "appName");
                string? catalogNamespace = OptionalEpicToken(epic, "namespace");
                string? catalogItemId = OptionalEpicToken(epic, "catalogItemId");
                if (appName != null || catalogItemId != null)
                {
                    manifest.Epic = new EpicReference(appName, catalogNamespace, catalogItemId);
                }
            }
        }

        private static void ReadDiscLaunch(JsonNode root, DiscManifest manifest)
        {
            string? discType = OptionalString(root, "discType", "discType");
            var launch = root.Get("discLaunch");
            bool hasLaunch = launch != null && !launch.IsNull;

            switch (discType)
            {
                case null:
                    manifest.DiscType = hasLaunch ? DiscType.DataDisc : DiscType.SmartDisc;
                    break;
                case "smart-disc":
                    manifest.DiscType = DiscType.SmartDisc;
                    break;
                case "data-disc":
                    manifest.DiscType = DiscType.DataDisc;
                    break;
                default:
                    throw Field("discType", "debe ser \"smart-disc\" o \"data-disc\"");
            }

            if (!hasLaunch) return;
            if (manifest.DiscType != DiscType.DataDisc) throw Field("discLaunch", "solo se permite en discos \"data-disc\"");
            if (launch!.Kind != JsonKind.Object) throw Field("discLaunch", "debe ser un objeto");

            DiscLaunchMode mode;
            switch (OptionalString(launch, "mode", "discLaunch.mode"))
            {
                case "executable":
                    mode = DiscLaunchMode.Executable;
                    break;
                case "installer":
                    mode = DiscLaunchMode.Installer;
                    break;
                default:
                    throw Field("discLaunch.mode", "debe ser \"executable\" o \"installer\"");
            }

            string? path = OptionalString(launch, "path", "discLaunch.path");
            if (path == null) throw Field("discLaunch.path", "es obligatorio");
            var extensions = mode == DiscLaunchMode.Executable ? ExecutableExtensions : InstallerExtensions;
            if (!DiscRelativePath.TryNormalize(path, extensions, out var normalized, out var error))
            {
                throw Field("discLaunch.path", error);
            }

            string? arguments = OptionalString(launch, "arguments", "discLaunch.arguments");
            if (arguments != null && mode == DiscLaunchMode.Installer)
            {
                throw Field("discLaunch.arguments", "no se admite en instaladores");
            }
            if (arguments != null && !ManifestRules.IsArguments(arguments))
            {
                throw Field("discLaunch.arguments", "admite hasta 512 caracteres sin caracteres de control");
            }
            manifest.DiscLaunch = new DiscLaunch(mode, normalized, arguments);
        }

        private static void ReadDiscSet(JsonNode root, DiscManifest manifest)
        {
            var set = OptionalObject(root, "discSet", "discSet");
            if (set == null) return;
            int number = RequiredInt(set, "disc", "discSet.disc", 1, 16);
            int total = RequiredInt(set, "total", "discSet.total", 1, 16);
            if (number > total) throw Field("discSet.disc", "no puede ser mayor que \"total\"");
            string? id = OptionalString(set, "id", "discSet.id");
            if (id != null && !ManifestRules.IsDiscId(id)) throw Field("discSet.id", "solo admite letras, números, '.', '_' y '-'");
            manifest.DiscSet = new DiscSetInfo(id, number, total);
        }

        private static void ReadArtwork(JsonNode root, DiscManifest manifest)
        {
            var artwork = OptionalObject(root, "artwork", "artwork");
            if (artwork == null) return;
            manifest.CoverPath = OptionalDiscFile(artwork, "cover", "artwork.cover", ImageExtensions);
            manifest.IconPath = OptionalDiscFile(artwork, "icon", "artwork.icon", IconExtensions);
        }

        private static void ReadLocalHints(JsonNode root, DiscManifest manifest)
        {
            var local = OptionalObject(root, "local", "local");
            if (local == null) return;
            manifest.LocalExecutables = ReadNames(local, "executables", "local.executables", ".exe");
            manifest.LocalFolders = ReadNames(local, "folders", "local.folders", null);
        }

        private static IReadOnlyList<string> ReadIds(JsonNode node, string single, string plural, string context)
        {
            var ids = new List<string>();
            void AddId(JsonNode value, string field)
            {
                string? text = value.Kind switch
                {
                    JsonKind.String => value.AsString(),
                    JsonKind.Number => value.AsInt64()?.ToString(CultureInfo.InvariantCulture),
                    _ => null,
                };
                if (!ManifestRules.IsNumericId(text)) throw Field(field, "debe ser un identificador numérico");
                if (!ids.Contains(text!)) ids.Add(text!);
            }

            var one = node.Get(single);
            if (one != null && !one.IsNull) AddId(one, context + "." + single);

            var many = node.Get(plural);
            if (many != null && !many.IsNull)
            {
                if (many.Kind != JsonKind.Array) throw Field(context + "." + plural, "debe ser una lista");
                if (many.Items.Count > 8) throw Field(context + "." + plural, "admite como máximo 8 identificadores");
                foreach (var item in many.Items) AddId(item, context + "." + plural);
            }
            return ids;
        }

        private static IReadOnlyList<string> ReadNames(JsonNode node, string name, string field, string? extension)
        {
            var array = node.Get(name);
            if (array == null || array.IsNull) return new string[0];
            if (array.Kind != JsonKind.Array) throw Field(field, "debe ser una lista");
            if (array.Items.Count > 16) throw Field(field, "admite como máximo 16 nombres");
            var names = new List<string>();
            foreach (var item in array.Items)
            {
                string? value = item.AsString();
                if (!PathTools.IsPlainName(value, 100)) throw Field(field, "solo admite nombres sin carpetas");
                if (extension != null && !value!.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    throw Field(field, "los nombres deben terminar en " + extension);
                }
                if (!names.Contains(value!)) names.Add(value!);
            }
            return names;
        }

        private static string? OptionalDiscFile(JsonNode node, string name, string field, string[] extensions)
        {
            string? value = OptionalString(node, name, field);
            if (value == null) return null;
            if (!DiscRelativePath.TryNormalize(value, extensions, out var normalized, out var error)) throw Field(field, error);
            return normalized;
        }

        private static string? OptionalEpicToken(JsonNode node, string name)
        {
            string field = "platforms.epic." + name;
            string? value = OptionalString(node, name, field);
            if (value != null && !ManifestRules.IsEpicToken(value)) throw Field(field, "solo admite letras, números, '.', '_' y '-'");
            return value;
        }

        private static int RequiredInt(JsonNode node, string name, string field, int min, int max)
        {
            long? value = node.Get(name)?.AsInt64();
            if (value == null || value < min || value > max)
            {
                throw Field(field, "debe ser un entero entre " + min.ToString(CultureInfo.InvariantCulture) + " y " + max.ToString(CultureInfo.InvariantCulture));
            }
            return (int)value.Value;
        }

        private static string? OptionalString(JsonNode node, string name, string field)
        {
            var value = node.Get(name);
            if (value == null || value.IsNull) return null;
            if (value.Kind != JsonKind.String) throw Field(field, "debe ser texto");
            return value.AsString();
        }

        private static JsonNode? OptionalObject(JsonNode node, string name, string field)
        {
            var value = node.Get(name);
            if (value == null || value.IsNull) return null;
            if (value.Kind != JsonKind.Object) throw Field(field, "debe ser un objeto");
            return value;
        }

        private static ManifestException Field(string field, string problem) =>
            new ManifestException(ManifestError.InvalidField, "\"" + field + "\" " + problem + ".", field);

        private static string Sha256Hex(string text)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }
    }
}
