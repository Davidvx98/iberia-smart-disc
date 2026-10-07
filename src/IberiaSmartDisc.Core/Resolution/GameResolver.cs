using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IberiaSmartDisc.Core.Common;
using IberiaSmartDisc.Core.Configuration;
using IberiaSmartDisc.Core.Platforms;

namespace IberiaSmartDisc.Core.Resolution
{
    public sealed class ResolutionResult
    {
        public ResolutionResult(
            IReadOnlyList<LaunchCandidate> candidates,
            IReadOnlyList<InstallAction> installActions,
            LaunchCandidate? preferred,
            string? preferredMissingMessage,
            IReadOnlyList<string> warnings)
        {
            Candidates = candidates;
            InstallActions = installActions;
            Preferred = preferred;
            PreferredMissingMessage = preferredMissingMessage;
            Warnings = warnings;
        }

        /// <summary>Instalaciones encontradas, ordenadas por la prioridad del usuario.</summary>
        public IReadOnlyList<LaunchCandidate> Candidates { get; }

        public IReadOnlyList<InstallAction> InstallActions { get; }

        /// <summary>La opción guardada con «Usar siempre», si sigue existiendo.</summary>
        public LaunchCandidate? Preferred { get; }

        /// <summary>Si había una opción guardada que ya no existe, el motivo para mostrarlo.</summary>
        public string? PreferredMissingMessage { get; }

        public bool PreferredMissing => PreferredMissingMessage != null;

        public IReadOnlyList<string> Warnings { get; }
    }

    /// <summary>
    /// Une identificación del disco y lanzamiento: pregunta a cada plataforma
    /// por instalaciones, añade el ejecutable local recordado y aplica la
    /// preferencia guardada y la prioridad de plataformas.
    /// </summary>
    public sealed class GameResolver
    {
        private readonly IReadOnlyList<IPlatformProvider> _providers;

        public GameResolver(IEnumerable<IPlatformProvider> providers)
        {
            _providers = providers.ToList();
        }

        public IReadOnlyList<IPlatformProvider> Providers => _providers;

        public ResolutionResult Resolve(ResolveContext context, GameRecord? record)
        {
            var candidates = new List<LaunchCandidate>();
            var actions = new List<InstallAction>();
            var warnings = new List<string>();

            foreach (var provider in _providers)
            {
                try
                {
                    candidates.AddRange(provider.FindInstalled(context));
                    var action = provider.GetInstallAction(context);
                    if (action != null) actions.Add(action);
                }
                catch (Exception ex)
                {
                    // Una plataforma rota no debe impedir abrir el juego con otra.
                    warnings.Add(LaunchKinds.DisplayName(provider.Kind) + ": " + ex.Message);
                }
            }

            var localExecutable = record?.LocalExecutable;
            if (localExecutable != null && SafeFileExists(localExecutable))
            {
                candidates.Add(CreateLocalCandidate(localExecutable, null));
            }

            var ordered = candidates
                .GroupBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Select((c, index) => new { Candidate = c, Index = index })
                .OrderBy(x => context.Settings.PriorityOf(x.Candidate.Kind))
                .ThenBy(x => x.Index)
                .Select(x => x.Candidate)
                .ToList();

            LaunchCandidate? preferred = null;
            string? missing = null;
            if (record != null && record.AlwaysUse && record.PreferredKey != null)
            {
                preferred = ordered.FirstOrDefault(c => string.Equals(c.Key, record.PreferredKey, StringComparison.OrdinalIgnoreCase));
                var kind = LaunchKinds.KindOfKey(record.PreferredKey);
                // Si la preferencia era «desde el disco» y este disco no lo permite, no es un error.
                if (preferred == null && kind != null && kind != LaunchKind.Disc)
                {
                    var provider = _providers.FirstOrDefault(p => p.Kind == kind);
                    missing = provider != null && !provider.IsClientAvailable(context.Settings)
                        ? LaunchKinds.DisplayName(kind.Value) + " no está disponible en este PC."
                        : "La instalación configurada ya no existe.";
                }
            }

            return new ResolutionResult(ordered, actions, preferred, missing, warnings);
        }

        public static LaunchCandidate CreateLocalCandidate(string executable, string? arguments)
        {
            return new LaunchCandidate(
                LaunchKind.Local,
                LaunchCandidate.LocalKey(executable),
                "Ejecutable local",
                executable,
                LaunchTarget.ForExecutable(LaunchKind.Local, executable, arguments, Path.GetDirectoryName(executable)));
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
