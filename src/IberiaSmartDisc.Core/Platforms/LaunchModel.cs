using System;
using System.Collections.Generic;
using IberiaSmartDisc.Core.Configuration;
using IberiaSmartDisc.Core.Manifest;

namespace IberiaSmartDisc.Core.Platforms
{
    public enum LaunchKind
    {
        Disc,
        Steam,
        Gog,
        Epic,
        Local,
    }

    public static class LaunchKinds
    {
        public static readonly IReadOnlyList<LaunchKind> DefaultPriority =
            new[] { LaunchKind.Disc, LaunchKind.Steam, LaunchKind.Gog, LaunchKind.Epic, LaunchKind.Local };

        public static string ToId(LaunchKind kind) => kind switch
        {
            LaunchKind.Disc => "disc",
            LaunchKind.Steam => "steam",
            LaunchKind.Gog => "gog",
            LaunchKind.Epic => "epic",
            _ => "local",
        };

        public static bool TryParse(string? id, out LaunchKind kind)
        {
            switch (id)
            {
                case "disc": kind = LaunchKind.Disc; return true;
                case "steam": kind = LaunchKind.Steam; return true;
                case "gog": kind = LaunchKind.Gog; return true;
                case "epic": kind = LaunchKind.Epic; return true;
                case "local": kind = LaunchKind.Local; return true;
                default: kind = LaunchKind.Local; return false;
            }
        }

        public static string DisplayName(LaunchKind kind) => kind switch
        {
            LaunchKind.Disc => "Desde el disco",
            LaunchKind.Steam => "Steam",
            LaunchKind.Gog => "GOG",
            LaunchKind.Epic => "Epic Games",
            _ => "Ejecutable local",
        };

        /// <summary>Tipo de una clave de candidato ("steam:620" → Steam).</summary>
        public static LaunchKind? KindOfKey(string? key)
        {
            if (key == null) return null;
            int colon = key.IndexOf(':');
            if (colon <= 0) return null;
            return TryParse(key.Substring(0, colon), out var kind) ? kind : (LaunchKind?)null;
        }
    }

    /// <summary>Cómo se abre un juego: un enlace de plataforma o un ejecutable.</summary>
    public sealed class LaunchTarget
    {
        private LaunchTarget(LaunchKind kind, string? uri, string? executable, string? arguments, string? workingDirectory, bool checkAlreadyRunning)
        {
            Kind = kind;
            Uri = uri;
            ExecutablePath = executable;
            Arguments = arguments;
            WorkingDirectory = workingDirectory;
            CheckAlreadyRunning = checkAlreadyRunning;
        }

        public LaunchKind Kind { get; }

        public string? Uri { get; }

        public string? ExecutablePath { get; }

        public string? Arguments { get; }

        public string? WorkingDirectory { get; }

        /// <summary>
        /// Si es el ejecutable del propio juego, antes de abrirlo se mira si ya está
        /// en marcha para traerlo al frente en vez de abrir otra copia.
        /// </summary>
        public bool CheckAlreadyRunning { get; }

        public static LaunchTarget ForUri(LaunchKind kind, string uri) => new LaunchTarget(kind, uri, null, null, null, false);

        public static LaunchTarget ForExecutable(LaunchKind kind, string path, string? arguments, string? workingDirectory, bool checkAlreadyRunning = true) =>
            new LaunchTarget(kind, null, path, arguments, workingDirectory, checkAlreadyRunning);

        public override string ToString() => Uri ?? ExecutablePath ?? string.Empty;
    }

    /// <summary>Una instalación encontrada con la que se puede abrir el juego.</summary>
    public sealed class LaunchCandidate
    {
        public LaunchCandidate(LaunchKind kind, string key, string title, string? detail, LaunchTarget target, string? platformGameName = null)
        {
            Kind = kind;
            Key = key;
            Title = title;
            Detail = detail;
            Target = target;
            PlatformGameName = string.IsNullOrWhiteSpace(platformGameName) || !Common.TextSafety.IsSafe(platformGameName) ? null : platformGameName!.Trim();
        }

        public LaunchKind Kind { get; }

        /// <summary>Identificador estable que se guarda como preferencia ("steam:620", "local:D:\…").</summary>
        public string Key { get; }

        public string Title { get; }

        public string? Detail { get; }

        public LaunchTarget Target { get; }

        /// <summary>
        /// Nombre que da la propia plataforma (appmanifest de Steam, Epic, GOG). Un
        /// disco puede llamarse como quiera; esto dice qué se va a abrir de verdad.
        /// </summary>
        public string? PlatformGameName { get; }

        public string DisplayTitle => PlatformGameName == null ? Title : Title + " · " + PlatformGameName;

        public static string LocalKey(string executable) => "local:" + executable;
    }

    /// <summary>Acción para instalar el juego cuando no está instalado.</summary>
    public sealed class InstallAction
    {
        public InstallAction(LaunchKind kind, string label, LaunchTarget target, bool requiresConfirmation)
        {
            Kind = kind;
            Label = label;
            Target = target;
            RequiresConfirmation = requiresConfirmation;
        }

        public LaunchKind Kind { get; }

        public string Label { get; }

        public LaunchTarget Target { get; }

        /// <summary>Ejecutar un instalador del disco siempre se confirma antes.</summary>
        public bool RequiresConfirmation { get; }
    }

    public sealed class ResolveContext
    {
        public ResolveContext(DiscManifest manifest, string? discRoot, AppSettings settings)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            DiscRoot = discRoot;
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public DiscManifest Manifest { get; }

        /// <summary>Raíz del disco; null cuando se configura un juego sin el disco puesto.</summary>
        public string? DiscRoot { get; }

        public AppSettings Settings { get; }
    }

    /// <summary>
    /// Una plataforma (Steam, GOG, Epic, el propio disco). Se separan identificar
    /// el disco, resolver la instalación y lanzar el juego, así que añadir una
    /// plataforma nueva es añadir una clase que implemente esto.
    /// </summary>
    public interface IPlatformProvider
    {
        LaunchKind Kind { get; }

        IReadOnlyList<LaunchCandidate> FindInstalled(ResolveContext context);

        InstallAction? GetInstallAction(ResolveContext context);

        /// <summary>Si el cliente de la plataforma está instalado en este PC.</summary>
        bool IsClientAvailable(AppSettings settings);

        /// <summary>Líneas de diagnóstico para la pestaña «Plataformas».</summary>
        IEnumerable<string> Describe(AppSettings settings);
    }
}
