using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using IberiaSmartDisc.Core.Common;
using IberiaSmartDisc.Core.Manifest;

namespace IberiaSmartDisc.Core.Platforms.Local
{
    public sealed class LocalSearchHit
    {
        public LocalSearchHit(string executablePath, int score, string reason)
        {
            ExecutablePath = executablePath;
            Score = score;
            Reason = reason;
        }

        public string ExecutablePath { get; }

        public int Score { get; }

        public string Reason { get; }
    }

    /// <summary>
    /// «Buscar automáticamente»: busca solo en carpetas de juegos conocidas y
    /// con límites de profundidad, carpetas y tiempo. Nunca escanea el disco
    /// duro entero y nunca ejecuta lo que encuentra: el usuario elige.
    /// </summary>
    public sealed class LocalGameSearch
    {
        private static readonly string[] SkippedDirectories =
        {
            "windows", "$recycle.bin", "system volume information", "programdata", "node_modules", ".git",
            "_commonredist", "commonredist", "redist", "redistributables", "directx", "vcredist", "dotnet",
            "__installer", "installers", "support", "crashreporter", "engine",
        };

        private static readonly string[] NonGameExecutableFragments =
        {
            "unins", "setup", "install", "redist", "vcredist", "vc_redist", "dxsetup", "dxwebsetup", "crash", "report",
            "helper", "updater", "update", "patcher", "dotnet", "directx", "physx", "ue4prereq", "prereq", "unitycrashhandler",
            "easyanticheat", "battleye", "cefprocess", "webhelper", "touchup", "cleanup", "config", "benchmark", "server",
        };

        private readonly int _maxDirectories;
        private readonly TimeSpan _budget;

        public LocalGameSearch(int maxDirectories = 6000, TimeSpan? budget = null)
        {
            _maxDirectories = maxDirectories;
            _budget = budget ?? TimeSpan.FromSeconds(15);
        }

        /// <summary>Carpetas donde se busca: las del usuario y las habituales de cada unidad.</summary>
        public static IReadOnlyList<string> DefaultRoots(ISystemInfo system, IEnumerable<string> userFolders)
        {
            var candidates = new List<string?>();
            candidates.AddRange(userFolders.Where(f => !PathTools.IsNetworkPath(f)));
            foreach (var drive in system.GetFixedDriveRoots())
            {
                candidates.Add(Path.Combine(drive, "Games"));
                candidates.Add(Path.Combine(drive, "Juegos"));
                candidates.Add(Path.Combine(drive, "GOG Games"));
                candidates.Add(Path.Combine(drive, "Epic Games"));
                candidates.Add(Path.Combine(drive, "SteamLibrary", "steamapps", "common"));
            }
            candidates.Add(system.GetFolder(KnownFolder.ProgramFiles));
            candidates.Add(system.GetFolder(KnownFolder.ProgramFilesX86));

            var roots = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in candidates)
            {
                var directory = PathTools.NormalizeDirectory(candidate);
                if (directory == null || !seen.Add(directory)) continue;
                try
                {
                    if (Directory.Exists(directory)) roots.Add(directory);
                }
                catch (Exception ex) when (PathTools.IsPathException(ex))
                {
                }
            }
            return roots;
        }

        public IReadOnlyList<LocalSearchHit> Search(DiscManifest manifest, IEnumerable<string> roots, CancellationToken cancellation)
        {
            var clock = Stopwatch.StartNew();
            var state = new SearchState(_maxDirectories, _budget, clock, cancellation);
            var hits = new Dictionary<string, LocalSearchHit>(StringComparer.OrdinalIgnoreCase);
            var rootList = roots.ToList();

            var folderHints = new HashSet<string>(manifest.LocalFolders.Select(PathTools.Simplify).Where(s => s.Length > 0));
            string gameKey = PathTools.Simplify(manifest.GameName);
            if (gameKey.Length > 0) folderHints.Add(gameKey);
            var exeHints = new HashSet<string>(manifest.LocalExecutables, StringComparer.OrdinalIgnoreCase);

            // 1) Carpetas con el nombre del juego (hasta dos niveles bajo cada raíz).
            var gameFolders = new List<string>();
            foreach (var root in rootList)
            {
                foreach (var directory in Walk(root, 2, state))
                {
                    if (folderHints.Contains(PathTools.Simplify(Path.GetFileName(directory)))) gameFolders.Add(directory);
                }
                if (state.Exhausted) break;
            }

            // 2) Dentro de esas carpetas, los ejecutables indicados en el disco o los que parezcan del juego.
            foreach (var folder in gameFolders)
            {
                foreach (var directory in new[] { folder }.Concat(Walk(folder, 4, state)))
                {
                    foreach (var file in SafeFiles(directory, "*.exe"))
                    {
                        string name = Path.GetFileName(file);
                        if (LooksLikeTool(name)) continue;
                        if (exeHints.Count > 0)
                        {
                            if (exeHints.Contains(name)) AddHit(hits, file, 100, "Coincide con " + name);
                            continue;
                        }
                        bool similar = gameKey.Length > 0 && PathTools.Simplify(Path.GetFileNameWithoutExtension(name)).Contains(gameKey);
                        AddHit(hits, file, similar ? 80 : 40, "En la carpeta " + Path.GetFileName(folder));
                    }
                    if (state.Exhausted) break;
                }
            }

            // 3) Si el disco da nombres de ejecutable y no hubo suerte, búsqueda poco profunda por nombre.
            if (hits.Count == 0 && exeHints.Count > 0)
            {
                foreach (var root in rootList)
                {
                    foreach (var directory in new[] { root }.Concat(Walk(root, 3, state)))
                    {
                        foreach (var file in SafeFiles(directory, "*.exe"))
                        {
                            string name = Path.GetFileName(file);
                            if (exeHints.Contains(name) && !LooksLikeTool(name)) AddHit(hits, file, 60, "Coincide con " + name);
                        }
                    }
                    if (state.Exhausted) break;
                }
            }

            return hits.Values.OrderByDescending(h => h.Score).ThenBy(h => h.ExecutablePath.Length).Take(20).ToList();
        }

        private static void AddHit(Dictionary<string, LocalSearchHit> hits, string path, int score, string reason)
        {
            if (!hits.TryGetValue(path, out var existing) || existing.Score < score) hits[path] = new LocalSearchHit(path, score, reason);
        }

        private static bool LooksLikeTool(string fileName)
        {
            string lower = fileName.ToLowerInvariant();
            return NonGameExecutableFragments.Any(lower.Contains);
        }

        /// <summary>Recorre subcarpetas en anchura, sin seguir enlaces ni entrar en carpetas del sistema.</summary>
        private static IEnumerable<string> Walk(string root, int maxDepth, SearchState state)
        {
            var queue = new Queue<KeyValuePair<string, int>>();
            queue.Enqueue(new KeyValuePair<string, int>(root, 0));
            while (queue.Count > 0)
            {
                if (state.Exhausted) yield break;
                var current = queue.Dequeue();
                if (current.Value >= maxDepth) continue;
                foreach (var child in SafeDirectories(current.Key))
                {
                    if (!state.CountDirectory()) yield break;
                    yield return child;
                    queue.Enqueue(new KeyValuePair<string, int>(child, current.Value + 1));
                }
            }
        }

        private static IEnumerable<string> SafeDirectories(string directory)
        {
            List<string> result;
            try
            {
                result = new List<string>();
                foreach (var child in new DirectoryInfo(directory).EnumerateDirectories())
                {
                    var attributes = child.Attributes;
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if ((attributes & FileAttributes.Hidden) != 0 && (attributes & FileAttributes.System) != 0) continue;
                    if (Array.IndexOf(SkippedDirectories, child.Name.ToLowerInvariant()) >= 0) continue;
                    result.Add(child.FullName);
                }
            }
            catch (Exception ex) when (PathTools.IsPathException(ex))
            {
                return Enumerable.Empty<string>();
            }
            return result;
        }

        private static IEnumerable<string> SafeFiles(string directory, string pattern)
        {
            try
            {
                return Directory.EnumerateFiles(directory, pattern).ToList();
            }
            catch (Exception ex) when (PathTools.IsPathException(ex))
            {
                return Enumerable.Empty<string>();
            }
        }

        private sealed class SearchState
        {
            private readonly int _maxDirectories;
            private readonly TimeSpan _budget;
            private readonly Stopwatch _clock;
            private readonly CancellationToken _cancellation;
            private int _visited;

            public SearchState(int maxDirectories, TimeSpan budget, Stopwatch clock, CancellationToken cancellation)
            {
                _maxDirectories = maxDirectories;
                _budget = budget;
                _clock = clock;
                _cancellation = cancellation;
            }

            public bool Exhausted => _cancellation.IsCancellationRequested || _clock.Elapsed > _budget || _visited >= _maxDirectories;

            public bool CountDirectory()
            {
                if (Exhausted) return false;
                _visited++;
                return true;
            }
        }
    }
}
