using System.Collections.Generic;
using IberiaSmartDisc.Core.Configuration;

namespace IberiaSmartDisc.Core.Platforms.Steam
{
    public sealed class SteamProvider : IPlatformProvider
    {
        private readonly SteamLocator _locator;
        private readonly IRegistryReader _registry;

        public SteamProvider(SteamLocator locator, IRegistryReader registry)
        {
            _locator = locator;
            _registry = registry;
        }

        public LaunchKind Kind => LaunchKind.Steam;

        public IReadOnlyList<LaunchCandidate> FindInstalled(ResolveContext context)
        {
            var ids = context.Manifest.SteamAppIds;
            var result = new List<LaunchCandidate>();
            if (ids.Count == 0) return result;

            var root = _locator.FindRoot(context.Settings.SteamPath);
            if (root == null) return result;
            var libraries = _locator.FindLibraries(root, context.Settings.SearchPaths);

            foreach (var id in ids)
            {
                var app = _locator.FindApp(libraries, id);
                if (app == null) continue;
                string detail = app.FullyInstalled ? app.InstallDirectory : app.InstallDirectory + " (Steam lo actualizará al abrirlo)";
                // steam://rungameid abre el juego a través de Steam (y abre Steam si estaba cerrado).
                result.Add(new LaunchCandidate(LaunchKind.Steam, "steam:" + id, "Steam", detail, LaunchTarget.ForUri(LaunchKind.Steam, "steam://rungameid/" + id), app.Name));
            }
            return result;
        }

        public InstallAction? GetInstallAction(ResolveContext context)
        {
            var ids = context.Manifest.SteamAppIds;
            if (ids.Count == 0) return null;
            if (!IsClientAvailable(context.Settings)) return null;
            // steam://install abre el diálogo de instalación de Steam; no descarga nada sin confirmar.
            return new InstallAction(LaunchKind.Steam, "Instalar con Steam", LaunchTarget.ForUri(LaunchKind.Steam, "steam://install/" + ids[0]), requiresConfirmation: false);
        }

        public bool IsClientAvailable(AppSettings settings) =>
            _locator.FindRoot(settings.SteamPath) != null || _registry.KeyExists(RegistryRoot.ClassesRoot, "steam");

        public IEnumerable<string> Describe(AppSettings settings)
        {
            var root = _locator.FindRoot(settings.SteamPath);
            if (root == null)
            {
                yield return "Steam: no encontrado.";
                yield break;
            }
            yield return "Steam: " + root;
            foreach (var library in _locator.FindLibraries(root, settings.SearchPaths))
            {
                yield return "  Biblioteca: " + library;
            }
        }
    }
}
