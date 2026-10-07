using System;
using System.Globalization;
using System.IO;
using IberiaSmartDisc.Core.Manifest;

namespace IberiaSmartDisc
{
    internal enum RunMode
    {
        /// <summary>Doble clic: instalar, o abrir la ventana principal si ya está instalado.</summary>
        Default,

        /// <summary>Inicio con Windows: residente y sin ventana.</summary>
        Background,

        Settings,
        Configure,
        Uninstall,
        UninstallHelper,

        /// <summary>Procesa una carpeta como si fuera un disco recién insertado.</summary>
        TestDisc,

        Exit,

        Help,
    }

    internal sealed class CommandLine
    {
        public RunMode Mode { get; private set; } = RunMode.Default;

        public string? GameId { get; private set; }

        public string? Path { get; private set; }

        public int HelperProcessId { get; private set; }

        public bool Quiet { get; private set; }

        public bool Portable { get; private set; }

        public bool FirstRun { get; private set; }

        public static CommandLine Parse(string[] args)
        {
            var result = new CommandLine();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--background":
                        result.Mode = RunMode.Background;
                        break;
                    case "--settings":
                        result.Mode = RunMode.Settings;
                        break;
                    case "--configure":
                        result.Mode = RunMode.Configure;
                        result.GameId = Next(args, ref i);
                        if (!ManifestRules.IsGameId(result.GameId)) throw new ArgumentException("El identificador de juego no es válido.");
                        break;
                    case "--uninstall":
                        result.Mode = RunMode.Uninstall;
                        break;
                    case "--quiet":
                        result.Quiet = true;
                        break;
                    case "--uninstall-helper":
                        result.Mode = RunMode.UninstallHelper;
                        if (!int.TryParse(Next(args, ref i), NumberStyles.None, CultureInfo.InvariantCulture, out int pid)) throw new ArgumentException("Proceso no válido.");
                        result.HelperProcessId = pid;
                        break;
                    case "--test-disc":
                        result.Mode = RunMode.TestDisc;
                        result.Path = System.IO.Path.GetFullPath(Next(args, ref i));
                        break;
                    case "--portable":
                        result.Portable = true;
                        break;
                    case "--first-run":
                        result.FirstRun = true;
                        break;
                    case "--exit":
                        result.Mode = RunMode.Exit;
                        break;
                    case "--help":
                    case "-h":
                    case "/?":
                        result.Mode = RunMode.Help;
                        break;
                    default:
                        throw new ArgumentException("Opción desconocida: " + args[i]);
                }
            }
            if (result.Portable && (result.Mode == RunMode.Uninstall || result.Mode == RunMode.UninstallHelper))
            {
                throw new ArgumentException("--uninstall no se puede usar con --portable: para quitar la copia portátil basta con borrar su carpeta.");
            }
            return result;
        }

        /// <summary>Mensaje para la instancia que ya está en marcha.</summary>
        public string? ToChannelMessage()
        {
            switch (Mode)
            {
                case RunMode.Background:
                    return null;
                case RunMode.Settings:
                    return "settings";
                case RunMode.Configure:
                    return "configure\n" + GameId;
                case RunMode.TestDisc:
                    return "test-disc\n" + Path;
                case RunMode.Exit:
                    return "exit";
                default:
                    return "show";
            }
        }

        public static string Usage =>
            "Uso: IberiaSmartDisc.exe [opción]\n\n"
            + "  (sin opciones)        Instala o abre la ventana principal\n"
            + "  --background          Modo residente (lo usa el inicio con Windows)\n"
            + "  --settings            Abre la configuración\n"
            + "  --configure <juego>   Configura un juego (p. ej. portal-2)\n"
            + "  --test-disc <carpeta> Prueba una carpeta como si fuera un disco\n"
            + "  --portable            Funciona sin instalar (datos junto al .exe)\n"
            + "  --uninstall [--quiet] Desinstala\n"
            + "  --exit                Cierra la instancia en marcha\n"
            + "  --help                Muestra esta ayuda";

        private static string Next(string[] args, ref int index)
        {
            if (index + 1 >= args.Length) throw new ArgumentException("Falta el valor de " + args[index] + ".");
            index++;
            if (string.IsNullOrWhiteSpace(args[index])) throw new ArgumentException("Valor vacío para " + args[index - 1] + ".");
            return args[index];
        }
    }
}
