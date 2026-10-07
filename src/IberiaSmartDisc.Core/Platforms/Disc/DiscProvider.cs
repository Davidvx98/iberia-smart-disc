using System;
using System.Collections.Generic;
using System.IO;
using IberiaSmartDisc.Core.Common;
using IberiaSmartDisc.Core.Configuration;
using IberiaSmartDisc.Core.Manifest;

namespace IberiaSmartDisc.Core.Platforms.Disc
{
    /// <summary>
    /// Discos con contenido: solo se ejecuta el archivo que el manifiesto
    /// declara en "discLaunch", nunca un .exe encontrado al azar.
    /// </summary>
    public sealed class DiscProvider : IPlatformProvider
    {
        public LaunchKind Kind => LaunchKind.Disc;

        public IReadOnlyList<LaunchCandidate> FindInstalled(ResolveContext context)
        {
            var result = new List<LaunchCandidate>();
            var launch = context.Manifest.DiscLaunch;
            if (launch == null || launch.Mode != DiscLaunchMode.Executable) return result;
            var path = ResolveOnDisc(context, launch);
            if (path == null) return result;
            result.Add(new LaunchCandidate(
                LaunchKind.Disc,
                "disc:" + launch.RelativePath,
                "Desde el disco",
                path,
                LaunchTarget.ForExecutable(LaunchKind.Disc, path, launch.Arguments, Path.GetDirectoryName(path))));
            return result;
        }

        public InstallAction? GetInstallAction(ResolveContext context)
        {
            var launch = context.Manifest.DiscLaunch;
            if (launch == null || launch.Mode != DiscLaunchMode.Installer) return null;
            var path = ResolveOnDisc(context, launch);
            if (path == null) return null;
            return new InstallAction(
                LaunchKind.Disc,
                "Instalar desde el disco",
                LaunchTarget.ForExecutable(LaunchKind.Disc, path, launch.Arguments, Path.GetDirectoryName(path), checkAlreadyRunning: false),
                requiresConfirmation: true);
        }

        public bool IsClientAvailable(AppSettings settings) => true;

        public IEnumerable<string> Describe(AppSettings settings)
        {
            yield return "Disco: solo se ejecuta el archivo indicado en «discLaunch» y siempre se pide confirmación la primera vez.";
        }

        private static string? ResolveOnDisc(ResolveContext context, DiscLaunch launch)
        {
            if (context.DiscRoot == null) return null;
            try
            {
                var path = DiscRelativePath.Combine(context.DiscRoot, launch.RelativePath);
                return File.Exists(path) ? path : null;
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
    }
}
