using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using IberiaSmartDisc.Core.Common;
using IberiaSmartDisc.Core.Configuration;
using IberiaSmartDisc.Core.Json;
using IberiaSmartDisc.Core.Manifest;

namespace IberiaSmartDisc.Core.Platforms.Gog
{
    public sealed class GogGame
    {
        public GogGame(string productId, string? name, string directory, string executable, string? arguments, string? workingDirectory)
        {
            ProductId = productId;
            Name = name;
            Directory = directory;
            Executable = executable;
            Arguments = arguments;
            WorkingDirectory = workingDirectory;
        }

        public string ProductId { get; }

        public string? Name { get; }

        public string Directory { get; }

        public string Executable { get; }

        public string? Arguments { get; }

        public string? WorkingDirectory { get; }
    }

    /// <summary>
    /// GOG: los instaladores (con o sin Galaxy) registran cada juego en
    /// HKLM\SOFTWARE\GOG.com\Games\{productId}. Si se copió la carpeta a mano,
    /// se busca el archivo goggame-{productId}.info que GOG deja en ella, pero
    /// solo en carpetas que eligió el usuario o que solo puede tocar un
    /// administrador: en C:\ o en discos secundarios cualquier cuenta del PC
    /// puede crear carpetas, y un .info falso abriría su programa.
    /// </summary>
    public sealed class GogProvider : IPlatformProvider
    {
        private const string GamesKey = @"SOFTWARE\GOG.com\Games";
        private const string GalaxyPathsKey = @"SOFTWARE\GOG.com\GalaxyClient\paths";
        private const string GalaxyProtocol = "goggalaxy";
        private const long MaxInfoBytes = 1024 * 1024;

        private readonly IRegistryReader _registry;
        private readonly ISystemInfo _system;

        public GogProvider(IRegistryReader registry, ISystemInfo system)
        {
            _registry = registry;
            _system = system;
        }

        public LaunchKind Kind => LaunchKind.Gog;

        public IReadOnlyList<LaunchCandidate> FindInstalled(ResolveContext context)
        {
            var result = new List<LaunchCandidate>();
            foreach (var id in context.Manifest.GogProductIds)
            {
                var game = FromRegistry(id) ?? FromInfoFile(id, context.Settings.SearchPaths);
                if (game == null) continue;
                result.Add(new LaunchCandidate(LaunchKind.Gog, "gog:" + id, "GOG", game.Directory, BuildTarget(game, context.Settings), game.Name));
            }
            return result;
        }

        public InstallAction? GetInstallAction(ResolveContext context)
        {
            var ids = context.Manifest.GogProductIds;
            if (ids.Count == 0 || !_registry.KeyExists(RegistryRoot.ClassesRoot, GalaxyProtocol)) return null;
            return new InstallAction(LaunchKind.Gog, "Instalar con GOG Galaxy", LaunchTarget.ForUri(LaunchKind.Gog, "goggalaxy://openGameView/" + ids[0]), requiresConfirmation: false);
        }

        /// <summary>Los juegos de GOG no necesitan cliente: se abren con su propio ejecutable.</summary>
        public bool IsClientAvailable(AppSettings settings) => true;

        public IEnumerable<string> Describe(AppSettings settings)
        {
            var galaxy = GalaxyClientPath();
            yield return galaxy != null ? "GOG Galaxy: " + galaxy : "GOG Galaxy: no encontrado (los juegos DRM-free se abren directamente).";
            yield return "Carpetas GOG revisadas: " + string.Join("; ", InfoSearchRoots(settings.SearchPaths).ToArray());
        }

        public GogGame? FromRegistry(string productId)
        {
            string key = GamesKey + "\\" + productId;
            var rawExecutable = _registry.GetString(RegistryRoot.LocalMachine, key, "exe", RegistryBitness.Registry32);
            if (PathTools.IsNetworkPath(rawExecutable)) return null;
            var executable = PathTools.NormalizeFile(rawExecutable);
            if (executable == null || !SafeFileExists(executable)) return null;
            var directory = PathTools.NormalizeDirectory(_registry.GetString(RegistryRoot.LocalMachine, key, "path", RegistryBitness.Registry32))
                ?? Path.GetDirectoryName(executable)!;
            var workingDirectory = PathTools.NormalizeDirectory(_registry.GetString(RegistryRoot.LocalMachine, key, "workingDir", RegistryBitness.Registry32));
            var arguments = _registry.GetString(RegistryRoot.LocalMachine, key, "launchParam", RegistryBitness.Registry32);
            var name = _registry.GetString(RegistryRoot.LocalMachine, key, "gameName", RegistryBitness.Registry32);
            return new GogGame(productId, name, directory, executable, string.IsNullOrWhiteSpace(arguments) ? null : arguments, workingDirectory);
        }

        public GogGame? FromInfoFile(string productId, IEnumerable<string> searchPaths)
        {
            string fileName = "goggame-" + productId + ".info";
            foreach (var root in InfoSearchRoots(searchPaths))
            {
                IEnumerable<string> directories;
                try
                {
                    directories = new[] { root }.Concat(Directory.EnumerateDirectories(root).Take(500)).ToList();
                }
                catch (Exception ex) when (PathTools.IsPathException(ex))
                {
                    continue;
                }
                foreach (var directory in directories)
                {
                    var game = TryReadInfo(Path.Combine(directory, fileName), directory, productId);
                    if (game != null) return game;
                }
            }
            return null;
        }

        private IEnumerable<string> InfoSearchRoots(IEnumerable<string> searchPaths)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var candidates = new List<string?>();
            candidates.AddRange(searchPaths);
            var programFilesX86 = _system.GetFolder(KnownFolder.ProgramFilesX86);
            if (programFilesX86 != null) candidates.Add(Path.Combine(programFilesX86, "GOG Galaxy", "Games"));
            foreach (var candidate in candidates)
            {
                if (PathTools.IsNetworkPath(candidate)) continue;
                var directory = PathTools.NormalizeDirectory(candidate);
                if (directory == null || !seen.Add(directory)) continue;
                bool exists;
                try
                {
                    exists = Directory.Exists(directory);
                }
                catch (Exception ex) when (PathTools.IsPathException(ex))
                {
                    exists = false;
                }
                if (exists) yield return directory;
            }
        }

        private LaunchTarget BuildTarget(GogGame game, AppSettings settings)
        {
            if (settings.GogUseGalaxy)
            {
                var client = GalaxyClientPath();
                if (client != null)
                {
                    // Una barra final antes de la comilla la escaparía ("D:\" → D:").
                    string directory = game.Directory.EndsWith("\\", StringComparison.Ordinal) ? game.Directory + "\\" : game.Directory;
                    string arguments = "/command=runGame /gameId=" + game.ProductId + " /path=\"" + directory + "\"";
                    return LaunchTarget.ForExecutable(LaunchKind.Gog, client, arguments, Path.GetDirectoryName(client), checkAlreadyRunning: false);
                }
            }
            return LaunchTarget.ForExecutable(LaunchKind.Gog, game.Executable, game.Arguments, game.WorkingDirectory ?? Path.GetDirectoryName(game.Executable));
        }

        private string? GalaxyClientPath()
        {
            var directory = PathTools.NormalizeDirectory(_registry.GetString(RegistryRoot.LocalMachine, GalaxyPathsKey, "client", RegistryBitness.Registry32));
            if (directory == null) return null;
            var executable = Path.Combine(directory, "GalaxyClient.exe");
            return SafeFileExists(executable) ? executable : null;
        }

        private static GogGame? TryReadInfo(string path, string directory, string productId)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > MaxInfoBytes) return null;
                var json = JsonParser.Parse(File.ReadAllText(path, Encoding.UTF8), maxDepth: 32, rejectDuplicateKeys: false);
                var tasks = json.GetArray("playTasks");
                if (tasks == null) return null;
                var primary = tasks.Items.FirstOrDefault(t => t.GetBoolean("isPrimary") == true && t.GetString("type") == "FileTask")
                    ?? tasks.Items.FirstOrDefault(t => t.GetString("type") == "FileTask");
                if (primary == null) return null;
                if (!DiscRelativePath.TryNormalize(primary.GetString("path"), new[] { ".exe" }, out var relative, out _)) return null;
                var executable = DiscRelativePath.Combine(directory, relative);
                if (!SafeFileExists(executable)) return null;

                string? workingDirectory = null;
                var workingDir = primary.GetString("workingDir");
                if (!string.IsNullOrEmpty(workingDir) && DiscRelativePath.TryNormalize(workingDir, null, out var relativeDir, out _))
                {
                    workingDirectory = DiscRelativePath.Combine(directory, relativeDir);
                }
                var arguments = primary.GetString("arguments");
                return new GogGame(productId, json.GetString("name"), directory, executable, string.IsNullOrWhiteSpace(arguments) ? null : arguments, workingDirectory);
            }
            catch (FormatException)
            {
                return null;
            }
            catch (ManifestException)
            {
                return null;
            }
            catch (Exception ex) when (PathTools.IsPathException(ex))
            {
                return null;
            }
        }

        private static bool SafeFileExists(string path)
        {
            try
            {
                return File.Exists(path);
            }
            catch (Exception ex) when (PathTools.IsPathException(ex))
            {
                return false;
            }
        }
    }
}
