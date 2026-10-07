using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using IberiaSmartDisc.Core.Common;
using IberiaSmartDisc.Core.Configuration;
using IberiaSmartDisc.Core.Json;

namespace IberiaSmartDisc.Core.Platforms.Epic
{
    public sealed class EpicInstalledGame
    {
        public EpicInstalledGame(string appName, string? mainGameAppName, string? displayName, string installLocation, string? catalogNamespace, string? catalogItemId)
        {
            AppName = appName;
            MainGameAppName = mainGameAppName;
            DisplayName = displayName;
            InstallLocation = installLocation;
            Namespace = catalogNamespace;
            CatalogItemId = catalogItemId;
        }

        public string AppName { get; }

        public string? MainGameAppName { get; }

        public string? DisplayName { get; }

        public string InstallLocation { get; }

        public string? Namespace { get; }

        public string? CatalogItemId { get; }

        public bool IsDlc => !string.IsNullOrEmpty(MainGameAppName) && !string.Equals(MainGameAppName, AppName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Epic guarda un manifiesto JSON (*.item) por juego instalado en
    /// %ProgramData%\Epic\EpicGamesLauncher\Data\Manifests, con la ruta de
    /// instalación y los identificadores necesarios para abrirlo.
    /// </summary>
    public sealed class EpicProvider : IPlatformProvider
    {
        private const string LauncherProtocol = "com.epicgames.launcher";
        private const long MaxItemBytes = 2 * 1024 * 1024;

        private readonly IRegistryReader _registry;
        private readonly ISystemInfo _system;

        public EpicProvider(IRegistryReader registry, ISystemInfo system)
        {
            _registry = registry;
            _system = system;
        }

        public LaunchKind Kind => LaunchKind.Epic;

        public string? FindManifestsDirectory()
        {
            var candidates = new List<string>();
            var appData = _registry.GetString(RegistryRoot.LocalMachine, @"SOFTWARE\Epic Games\EpicGamesLauncher", "AppDataPath", RegistryBitness.Registry32);
            if (!string.IsNullOrWhiteSpace(appData)) candidates.Add(Path.Combine(appData!, "Manifests"));
            var programData = _system.GetFolder(KnownFolder.ProgramData);
            if (programData != null) candidates.Add(Path.Combine(programData, "Epic", "EpicGamesLauncher", "Data", "Manifests"));

            foreach (var candidate in candidates)
            {
                var directory = PathTools.NormalizeDirectory(candidate);
                try
                {
                    if (directory != null && Directory.Exists(directory)) return directory;
                }
                catch (Exception ex) when (PathTools.IsPathException(ex))
                {
                }
            }
            return null;
        }

        public IReadOnlyList<EpicInstalledGame> ReadInstalledGames()
        {
            var games = new List<EpicInstalledGame>();
            var directory = FindManifestsDirectory();
            if (directory == null) return games;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*.item");
            }
            catch (Exception ex) when (PathTools.IsPathException(ex))
            {
                return games;
            }

            int count = 0;
            foreach (var file in files)
            {
                if (++count > 2000) break;
                var game = TryReadItem(file);
                if (game != null) games.Add(game);
            }
            return games;
        }

        public IReadOnlyList<LaunchCandidate> FindInstalled(ResolveContext context)
        {
            var result = new List<LaunchCandidate>();
            var wanted = context.Manifest.Epic;
            if (wanted == null) return result;

            foreach (var game in ReadInstalledGames())
            {
                if (game.IsDlc || PathTools.IsNetworkPath(game.InstallLocation)) continue;
                bool matches = (wanted.AppName != null && string.Equals(game.AppName, wanted.AppName, StringComparison.OrdinalIgnoreCase))
                    || (wanted.CatalogItemId != null && string.Equals(game.CatalogItemId, wanted.CatalogItemId, StringComparison.OrdinalIgnoreCase));
                if (!matches) continue;
                bool exists;
                try
                {
                    exists = Directory.Exists(game.InstallLocation);
                }
                catch (Exception ex) when (PathTools.IsPathException(ex))
                {
                    exists = false;
                }
                if (!exists) continue;
                var uri = BuildUri(game.Namespace, game.CatalogItemId, game.AppName, "launch");
                result.Add(new LaunchCandidate(LaunchKind.Epic, "epic:" + game.AppName, "Epic Games", game.InstallLocation, LaunchTarget.ForUri(LaunchKind.Epic, uri), game.DisplayName));
            }
            return result;
        }

        public InstallAction? GetInstallAction(ResolveContext context)
        {
            var wanted = context.Manifest.Epic;
            if (wanted?.AppName == null) return null;
            if (!_registry.KeyExists(RegistryRoot.ClassesRoot, LauncherProtocol)) return null;
            var uri = BuildUri(wanted.Namespace, wanted.CatalogItemId, wanted.AppName, "install");
            return new InstallAction(LaunchKind.Epic, "Instalar con Epic Games", LaunchTarget.ForUri(LaunchKind.Epic, uri), requiresConfirmation: false);
        }

        public bool IsClientAvailable(AppSettings settings) =>
            _registry.KeyExists(RegistryRoot.ClassesRoot, LauncherProtocol) || FindManifestsDirectory() != null;

        public IEnumerable<string> Describe(AppSettings settings)
        {
            var directory = FindManifestsDirectory();
            if (directory == null)
            {
                yield return "Epic Games: no encontrado.";
                yield break;
            }
            var games = ReadInstalledGames();
            yield return "Epic Games: " + games.Count + " juego(s) instalado(s) según " + directory;
        }

        /// <summary>
        /// Formato moderno: apps/{namespace}:{catalogItemId}:{appName}. Si faltan
        /// datos se usa el formato antiguo apps/{appName}, que Epic sigue aceptando.
        /// </summary>
        public static string BuildUri(string? catalogNamespace, string? catalogItemId, string appName, string action)
        {
            string app = Uri.EscapeDataString(appName);
            string path = !string.IsNullOrEmpty(catalogNamespace) && !string.IsNullOrEmpty(catalogItemId)
                ? Uri.EscapeDataString(catalogNamespace) + "%3A" + Uri.EscapeDataString(catalogItemId) + "%3A" + app
                : app;
            return LauncherProtocol + "://apps/" + path + "?action=" + action + "&silent=true";
        }

        private static EpicInstalledGame? TryReadItem(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Length > MaxItemBytes) return null;
                var json = JsonParser.Parse(File.ReadAllText(path, Encoding.UTF8), maxDepth: 32, rejectDuplicateKeys: false);
                if (json.Kind != JsonKind.Object) return null;
                if (json.GetBoolean("bIsIncompleteInstall") == true) return null;
                var appName = json.GetString("AppName");
                var location = json.GetString("InstallLocation");
                if (string.IsNullOrWhiteSpace(appName) || string.IsNullOrWhiteSpace(location)) return null;
                return new EpicInstalledGame(
                    appName!,
                    json.GetString("MainGameAppName"),
                    json.GetString("DisplayName"),
                    PathTools.NormalizeDirectory(location) ?? location!,
                    json.GetString("CatalogNamespace"),
                    json.GetString("CatalogItemId"));
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
    }
}
