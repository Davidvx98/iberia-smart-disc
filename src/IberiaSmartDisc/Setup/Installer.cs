using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using IberiaSmartDisc.Core.Configuration;
using IberiaSmartDisc.Hosting;
using IberiaSmartDisc.Logging;

namespace IberiaSmartDisc.Setup
{
    internal sealed class InstallOptions
    {
        public bool StartWithWindows { get; set; } = true;

        public bool StartMenuShortcut { get; set; } = true;
    }

    /// <summary>
    /// Instalación por usuario en %LOCALAPPDATA%\IberiaSmartDisc. Nunca pide
    /// administrador: copia el .exe, guarda la configuración y registra el
    /// inicio automático, el acceso directo y la entrada de desinstalación.
    /// </summary>
    internal static class Installer
    {
        public static Version? InstalledVersion()
        {
            try
            {
                if (!File.Exists(AppPaths.InstalledExe)) return null;
                var info = FileVersionInfo.GetVersionInfo(AppPaths.InstalledExe);
                return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException)
            {
                return null;
            }
        }

        public static void Install(InstallOptions options, AppLog log)
        {
            Directory.CreateDirectory(AppPaths.InstallDirectory);
            CopyExecutable(AppPaths.CurrentExe, AppPaths.InstalledExe);

            var existing = JsonFileStore.Load(AppPaths.SettingsFile, out _);
            var settings = AppSettings.FromJson(existing);
            settings.StartWithWindows = options.StartWithWindows;
            JsonFileStore.Save(AppPaths.SettingsFile, settings.ToJson());

            if (options.StartWithWindows) StartupRegistration.Enable(AppPaths.InstalledExe, userRequested: true);
            else StartupRegistration.Disable();
            if (options.StartMenuShortcut)
            {
                try
                {
                    StartMenuShortcut.Create(AppPaths.InstalledExe);
                }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException || ex is UnauthorizedAccessException || ex is IOException)
                {
                    log.Warn("No se pudo crear el acceso directo: " + ex.Message);
                }
            }
            UninstallEntry.Write(AppPaths.InstalledExe);
            log.Info("Instalado " + AppInfo.VersionText + " en " + AppPaths.InstallDirectory);
        }

        /// <summary>Sustituye el ejecutable instalado conservando la configuración.</summary>
        public static void Update(AppLog log)
        {
            if (!CommandChannel.StopResident(TimeSpan.FromSeconds(10)))
            {
                throw new InvalidOperationException("Iberia Smart Disc sigue abierto. Ciérralo desde el icono de la bandeja (Salir) y vuelve a intentarlo.");
            }
            CopyExecutable(AppPaths.CurrentExe, AppPaths.InstalledExe);

            var settings = AppSettings.FromJson(JsonFileStore.Load(AppPaths.SettingsFile, out _));
            if (settings.StartWithWindows && StartupRegistration.IsRegistered()) StartupRegistration.Enable(AppPaths.InstalledExe, userRequested: false);
            UninstallEntry.Write(AppPaths.InstalledExe);
            log.Info("Actualizado a " + AppInfo.VersionText);
        }

        public static void StartInstalled(string arguments)
        {
            using (Process.Start(new ProcessStartInfo(AppPaths.InstalledExe, arguments)
            {
                UseShellExecute = false,
                WorkingDirectory = AppPaths.InstallDirectory,
            }))
            {
            }
        }

        /// <summary>Restos de una actualización anterior (el .exe viejo renombrado).</summary>
        public static void CleanupLeftovers()
        {
            TryDelete(AppPaths.InstalledExe + ".old");
            TryDelete(AppPaths.InstalledExe + ".new");
            // Restos de una desinstalación anterior (el .exe apartado y la copia auxiliar).
            try
            {
                foreach (var file in Directory.GetFiles(Path.GetTempPath(), "IberiaSmartDisc-desinstala*")) TryDelete(file);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        /// <summary>
        /// Copia leyendo y escribiendo un archivo nuevo (sin heredar la marca «descargado
        /// de Internet» del original). Si el .exe instalado sigue bloqueado, se renombra
        /// primero: Windows permite renombrar un ejecutable en uso.
        /// </summary>
        private static void CopyExecutable(string source, string target)
        {
            string temporary = target + ".new";
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                input.CopyTo(output);
            }

            if (File.Exists(target))
            {
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        File.Delete(target);
                        break;
                    }
                    catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < 5)
                    {
                        Thread.Sleep(300);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        string old = target + ".old";
                        TryDelete(old);
                        File.Move(target, old);
                        break;
                    }
                }
            }
            File.Move(temporary, target);
        }

        internal static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }
}
