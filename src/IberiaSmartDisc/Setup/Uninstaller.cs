using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using IberiaSmartDisc.Core.Common;
using IberiaSmartDisc.Logging;
using IberiaSmartDisc.Native;

namespace IberiaSmartDisc.Setup
{
    internal sealed class UninstallOptions
    {
        public bool RemoveSettings { get; set; } = true;

        public bool RemoveGames { get; set; } = true;

        public bool RemoveLogsAndCache { get; set; } = true;

        public static UninstallOptions Everything => new UninstallOptions();
    }

    /// <summary>
    /// Desinstalación limpia: inicio automático, acceso directo, entrada de
    /// «Aplicaciones instaladas», datos elegidos y el propio ejecutable.
    /// </summary>
    internal static class Uninstaller
    {
        public static bool IsInstalled =>
            File.Exists(AppPaths.InstalledExe) || StartupRegistration.IsRegistered();

        public static void Run(UninstallOptions options, AppLog log)
        {
            log.Info("Desinstalando Iberia Smart Disc");
            StartupRegistration.RemoveAll();
            StartMenuShortcut.Delete();
            UninstallEntry.Remove();
            log.Enabled = false;

            if (options.RemoveSettings)
            {
                DeleteWithVariants(AppPaths.SettingsFile);
            }
            if (options.RemoveGames)
            {
                DeleteWithVariants(AppPaths.GamesFile);
                DeleteDirectory(AppPaths.ManifestsDirectory);
                DeleteDirectory(AppPaths.CoversDirectory);
            }
            if (options.RemoveLogsAndCache)
            {
                DeleteDirectory(AppPaths.LogsDirectory);
                DeleteDirectory(AppPaths.CacheDirectory);
            }

            RemoveExecutable();
            DeleteDirectoryIfEmpty(AppPaths.CacheDirectory);
            DeleteDirectoryIfEmpty(AppPaths.InstallDirectory);
        }

        /// <summary>
        /// Windows no deja borrar un .exe en uso, pero sí moverlo dentro del mismo
        /// disco: se aparta a %TEMP% (que Windows limpia) y la carpeta queda vacía.
        /// Si TEMP está en otro disco, una copia auxiliar lo borra al salir.
        /// </summary>
        private static void RemoveExecutable()
        {
            string executable = AppPaths.InstalledExe;
            Installer.TryDelete(executable + ".old");
            Installer.TryDelete(executable + ".new");
            if (!File.Exists(executable)) return;

            if (!AppPaths.IsInstalledCopy)
            {
                for (int attempt = 0; attempt < 15; attempt++)
                {
                    try
                    {
                        File.Delete(executable);
                        return;
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        Thread.Sleep(300);
                    }
                }
                return;
            }

            string stamp = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            string parked = Path.Combine(Path.GetTempPath(), "IberiaSmartDisc-desinstalado-" + stamp + ".tmp");
            try
            {
                File.Move(executable, parked);
                return;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Installer.TryDelete(parked);
            }

            string helper = Path.Combine(Path.GetTempPath(), "IberiaSmartDisc-desinstalador-" + stamp + ".exe");
            File.Copy(executable, helper, overwrite: true);
            using (Process.Start(new ProcessStartInfo(helper, "--uninstall-helper " + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture))
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetTempPath(),
            }))
            {
            }
        }

        /// <summary>
        /// Copia auxiliar: espera a que termine el proceso principal y borra el .exe
        /// instalado. La ruta es siempre la de instalación, nunca una recibida por argumento.
        /// </summary>
        public static int RunHelper(int processId)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    // Solo lo puede pedir el programa instalado; cualquier otro proceso no borra nada.
                    if (!PathTools.SamePath(NativeMethods.GetProcessImagePath((uint)processId), AppPaths.InstalledExe)) return 1;
                    process.WaitForExit();
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
            {
                return 1;
            }

            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    if (File.Exists(AppPaths.InstalledExe)) File.Delete(AppPaths.InstalledExe);
                    break;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Thread.Sleep(500);
                }
            }
            DeleteDirectoryIfEmpty(AppPaths.InstallDirectory);
            return 0;
        }

        private static void DeleteWithVariants(string file)
        {
            Installer.TryDelete(file);
            Installer.TryDelete(file + ".tmp");
            try
            {
                string? directory = Path.GetDirectoryName(file);
                if (directory == null || !Directory.Exists(directory)) return;
                foreach (var corrupt in Directory.GetFiles(directory, Path.GetFileName(file) + ".corrupt-*")) Installer.TryDelete(corrupt);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        private static void DeleteDirectory(string directory)
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        private static void DeleteDirectoryIfEmpty(string directory)
        {
            try
            {
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }
}
