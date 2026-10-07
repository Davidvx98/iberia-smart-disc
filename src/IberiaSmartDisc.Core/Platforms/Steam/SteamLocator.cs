using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using IberiaSmartDisc.Core.Common;
using IberiaSmartDisc.Core.Vdf;

namespace IberiaSmartDisc.Core.Platforms.Steam
{
    public sealed class SteamApp
    {
        public SteamApp(string appId, string? name, string installDirectory, string libraryPath, bool fullyInstalled)
        {
            AppId = appId;
            Name = name;
            InstallDirectory = installDirectory;
            LibraryPath = libraryPath;
            FullyInstalled = fullyInstalled;
        }

        public string AppId { get; }

        public string? Name { get; }

        public string InstallDirectory { get; }

        public string LibraryPath { get; }

        /// <summary>StateFlags incluye 4 (instalado por completo); si no, Steam lo actualizará al abrirlo.</summary>
        public bool FullyInstalled { get; }
    }

    /// <summary>
    /// Encuentra Steam y sus bibliotecas sin depender de dónde se instaló:
    /// registro, rutas habituales y cualquier unidad fija.
    /// </summary>
    public sealed class SteamLocator
    {
        private const long MaxVdfBytes = 4 * 1024 * 1024;

        private readonly IRegistryReader _registry;
        private readonly ISystemInfo _system;

        public SteamLocator(IRegistryReader registry, ISystemInfo system)
        {
            _registry = registry;
            _system = system;
        }

        public string? FindRoot(string? overridePath)
        {
            foreach (var candidate in RootCandidates(overridePath))
            {
                if (PathTools.IsNetworkPath(candidate)) continue;
                var directory = PathTools.NormalizeDirectory(candidate);
                if (directory != null && SafeDirectoryExists(Path.Combine(directory, "steamapps"))) return directory;
            }
            return null;
        }

        private IEnumerable<string?> RootCandidates(string? overridePath)
        {
            yield return overridePath;
            yield return _registry.GetString(RegistryRoot.CurrentUser, @"Software\Valve\Steam", "SteamPath");
            yield return _registry.GetString(RegistryRoot.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath", RegistryBitness.Registry32);
            yield return _registry.GetString(RegistryRoot.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath", RegistryBitness.Registry64);
            var programFilesX86 = _system.GetFolder(KnownFolder.ProgramFilesX86);
            if (programFilesX86 != null) yield return Path.Combine(programFilesX86, "Steam");
            var programFiles = _system.GetFolder(KnownFolder.ProgramFiles);
            if (programFiles != null) yield return Path.Combine(programFiles, "Steam");
            foreach (var drive in _system.GetFixedDriveRoots())
            {
                yield return Path.Combine(drive, "Steam");
                yield return Path.Combine(drive, "Program Files (x86)", "Steam");
            }
        }

        /// <summary>
        /// Bibliotecas: la carpeta de Steam, las de libraryfolders.vdf (formato
        /// nuevo con "path" y antiguo con "1" "D:\\SteamLibrary"), las carpetas
        /// que añadió el usuario y X:\SteamLibrary en cada unidad.
        /// </summary>
        public IReadOnlyList<string> FindLibraries(string steamRoot, IEnumerable<string> extraFolders)
        {
            var libraries = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddLibrary(string? path)
            {
                if (PathTools.IsNetworkPath(path)) return;
                var directory = PathTools.NormalizeDirectory(path);
                if (directory == null || !seen.Add(directory)) return;
                if (SafeDirectoryExists(Path.Combine(directory, "steamapps"))) libraries.Add(directory);
            }

            AddLibrary(steamRoot);
            foreach (var vdfPath in new[]
            {
                Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"),
                Path.Combine(steamRoot, "config", "libraryfolders.vdf"),
            })
            {
                var folders = TryReadVdf(vdfPath)?.Get("libraryfolders");
                if (folders == null) continue;
                foreach (var entry in folders.Children)
                {
                    if (entry.Value.IsObject) AddLibrary(entry.Value.GetValue("path"));
                    else if (IsDigits(entry.Key)) AddLibrary(entry.Value.Value);
                }
            }

            foreach (var folder in extraFolders)
            {
                var directory = PathTools.NormalizeDirectory(folder);
                if (directory == null) continue;
                if (string.Equals(Path.GetFileName(directory), "steamapps", StringComparison.OrdinalIgnoreCase))
                {
                    AddLibrary(Path.GetDirectoryName(directory));
                }
                else
                {
                    AddLibrary(directory);
                    AddLibrary(Path.Combine(directory, "SteamLibrary"));
                }
            }

            foreach (var drive in _system.GetFixedDriveRoots()) AddLibrary(Path.Combine(drive, "SteamLibrary"));
            return libraries;
        }

        public SteamApp? FindApp(IEnumerable<string> libraries, string appId)
        {
            foreach (var library in libraries)
            {
                var manifestPath = Path.Combine(library, "steamapps", "appmanifest_" + appId + ".acf");
                var state = TryReadVdf(manifestPath)?.Get("AppState");
                if (state == null) continue;
                var installDir = state.GetValue("installdir");
                if (!PathTools.IsPlainName(installDir, 255)) continue;
                var directory = Path.Combine(library, "steamapps", "common", installDir!);
                if (!SafeDirectoryExists(directory)) continue;
                int.TryParse(state.GetValue("StateFlags"), out int flags);
                return new SteamApp(appId, state.GetValue("name"), directory, library, (flags & 4) != 0);
            }
            return null;
        }

        internal static VdfNode? TryReadVdf(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > MaxVdfBytes) return null;
                return VdfParser.Parse(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (FormatException)
            {
                return null;
            }
            catch (Exception ex) when (PathTools.IsPathException(ex))
            {
                return null;
            }
        }

        private static bool SafeDirectoryExists(string path)
        {
            try
            {
                return Directory.Exists(path);
            }
            catch (Exception ex) when (PathTools.IsPathException(ex))
            {
                return false;
            }
        }

        private static bool IsDigits(string text)
        {
            if (text.Length == 0) return false;
            foreach (char c in text)
            {
                if (c < '0' || c > '9') return false;
            }
            return true;
        }
    }
}
