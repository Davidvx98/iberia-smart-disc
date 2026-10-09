using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using IberiaSmartDisc.Core.Common;

// Sin esto, el CLR busca primero las DLL de las P/Invoke en la carpeta del .exe.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace IberiaSmartDisc
{
    internal static class AppInfo
    {
        public const string Name = "Iberia Smart Disc";
        public const string ExeName = "IberiaSmartDisc.exe";
        public const string Publisher = "Iberia Custom DVDs";
        public const string WebsiteUrl = "https://iberiacustomdvds.es";
        public const string WebsiteLabel = "IberiaCustomDVDs.es";
        public const string RepositoryUrl = "https://github.com/Davidvx98/iberia-smart-disc";

        public static Version Version => typeof(AppInfo).Assembly.GetName().Version ?? new Version(0, 0, 0);

        /// <summary>Versión de tres partes, comparable con la del .exe instalado.</summary>
        public static Version ComparableVersion => new Version(Version.Major, Version.Minor, Math.Max(0, Version.Build));

        /// <summary>Nombre público de la versión, incluido su sufijo.</summary>
        public static string VersionText =>
            typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? ComparableVersion.ToString(3);

        public static string ReleaseChannel
        {
            get
            {
                foreach (var metadata in typeof(AppInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
                {
                    if (metadata.Key == "ReleaseChannel") return metadata.Value ?? string.Empty;
                }
                return string.Empty;
            }
        }

        public static string DisplayVersion => VersionText + (ReleaseChannel.Length > 0 ? " · " + ReleaseChannel : string.Empty);
    }

    /// <summary>
    /// Rutas del programa. La instalación vive en %LOCALAPPDATA%\IberiaSmartDisc
    /// (sin admin). En modo portátil los datos van junto al ejecutable.
    /// </summary>
    internal static class AppPaths
    {
        private static string? _dataDirectory;

        public static bool Portable { get; private set; }

        public static string InstallDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IberiaSmartDisc");

        public static string InstalledExe => Path.Combine(InstallDirectory, AppInfo.ExeName);

        public static string CurrentExe => Application.ExecutablePath;

        public static bool IsInstalledCopy => PathTools.SamePath(CurrentExe, InstalledExe);

        public static string DataDirectory => _dataDirectory ?? InstallDirectory;

        public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

        public static string GamesFile => Path.Combine(DataDirectory, "games.json");

        public static string LogsDirectory => Path.Combine(DataDirectory, "logs");

        public static string CacheDirectory => Path.Combine(DataDirectory, "cache");

        public static string CoversDirectory => Path.Combine(CacheDirectory, "covers");

        public static string ManifestsDirectory => Path.Combine(CacheDirectory, "manifests");

        public static void UsePortable()
        {
            Portable = true;
            _dataDirectory = Path.Combine(Path.GetDirectoryName(CurrentExe)!, "IberiaSmartDisc-datos");
        }
    }
}
