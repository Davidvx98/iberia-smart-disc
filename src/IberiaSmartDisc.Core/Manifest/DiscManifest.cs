using System.Collections.Generic;

namespace IberiaSmartDisc.Core.Manifest
{
    public enum DiscType
    {
        /// <summary>Disco decorativo: solo identifica el juego.</summary>
        SmartDisc,

        /// <summary>Disco con contenido (instalador, juego DRM-free, extras).</summary>
        DataDisc,
    }

    public enum DiscLaunchMode
    {
        Executable,
        Installer,
    }

    /// <summary>
    /// Contenido validado de iberia-disc.json. Solo se construye mediante
    /// <see cref="ManifestParser"/>, así que todo lo que hay aquí ya es seguro de usar.
    /// </summary>
    public sealed class DiscManifest
    {
        public const string FileName = "iberia-disc.json";

        /// <summary>Formato más alto que entiende esta versión del programa.</summary>
        public const int SupportedFormat = 1;

        public const int MaxFileBytes = 64 * 1024;

        private static readonly IReadOnlyList<string> None = new string[0];

        internal DiscManifest()
        {
        }

        public int Format { get; internal set; }

        public string? DiscId { get; internal set; }

        public DiscType DiscType { get; internal set; }

        /// <summary>Identificador estable del juego; coincide con el slug del catálogo web.</summary>
        public string GameId { get; internal set; } = string.Empty;

        public string GameName { get; internal set; } = string.Empty;

        public string? Edition { get; internal set; }

        public IReadOnlyList<string> SteamAppIds { get; internal set; } = None;

        public IReadOnlyList<string> GogProductIds { get; internal set; } = None;

        public EpicReference? Epic { get; internal set; }

        public DiscLaunch? DiscLaunch { get; internal set; }

        public DiscSetInfo? DiscSet { get; internal set; }

        /// <summary>Ruta relativa (normalizada con '\') de la carátula dentro del disco.</summary>
        public string? CoverPath { get; internal set; }

        public string? IconPath { get; internal set; }

        /// <summary>Nombres de ejecutable que ayudan a «Buscar automáticamente».</summary>
        public IReadOnlyList<string> LocalExecutables { get; internal set; } = None;

        /// <summary>Nombres de carpeta habituales del juego instalado.</summary>
        public IReadOnlyList<string> LocalFolders { get; internal set; } = None;

        /// <summary>SHA-256 del texto del manifiesto: identifica este disco concreto.</summary>
        public string Sha256 { get; internal set; } = string.Empty;

        public string RawJson { get; internal set; } = string.Empty;

        public string DisplayName => Edition == null ? GameName : GameName + " · " + Edition;
    }

    public sealed class EpicReference
    {
        public EpicReference(string? appName, string? catalogNamespace, string? catalogItemId)
        {
            AppName = appName;
            Namespace = catalogNamespace;
            CatalogItemId = catalogItemId;
        }

        public string? AppName { get; }

        public string? Namespace { get; }

        public string? CatalogItemId { get; }
    }

    public sealed class DiscLaunch
    {
        public DiscLaunch(DiscLaunchMode mode, string relativePath, string? arguments)
        {
            Mode = mode;
            RelativePath = relativePath;
            Arguments = arguments;
        }

        public DiscLaunchMode Mode { get; }

        /// <summary>Ruta relativa a la raíz del disco, ya validada y con separador '\'.</summary>
        public string RelativePath { get; }

        public string? Arguments { get; }
    }

    public sealed class DiscSetInfo
    {
        public DiscSetInfo(string? id, int number, int total)
        {
            Id = id;
            Number = number;
            Total = total;
        }

        public string? Id { get; }

        public int Number { get; }

        public int Total { get; }
    }
}
