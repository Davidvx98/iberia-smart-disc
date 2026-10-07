using System;
using System.Threading;
using System.Windows.Forms;
using IberiaSmartDisc.Hosting;
using IberiaSmartDisc.Logging;
using IberiaSmartDisc.Native;
using IberiaSmartDisc.Setup;
using IberiaSmartDisc.UI;

namespace IberiaSmartDisc
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // Lo primero: las DLL se buscan solo en System32, nunca junto al .exe (Descargas, %TEMP%).
            NativeMethods.RestrictDllSearchToSystem32();
            // Sin diálogos de «No hay disco en la unidad» al mirar lectores vacíos.
            NativeMethods.SetErrorMode(NativeMethods.SEM_FAILCRITICALERRORS | NativeMethods.SEM_NOOPENFILEERRORBOX);

            CommandLine command;
            try
            {
                command = CommandLine.Parse(args);
            }
            catch (ArgumentException ex)
            {
                Application.EnableVisualStyles();
                MessageBox.Show(ex.Message + "\n\n" + CommandLine.Usage, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 2;
            }

            if (command.Mode == RunMode.UninstallHelper) return Uninstaller.RunHelper(command.HelperProcessId);
            if (command.Mode == RunMode.Help)
            {
                Application.EnableVisualStyles();
                MessageBox.Show(CommandLine.Usage, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            Theme.Initialize();

            // Elevado, abriría juegos y programas como administrador con datos que cualquier
            // programa del usuario puede cambiar. Con el UAC desactivado no hay diferencia.
            if (NativeMethods.IsElevatedWithUac())
            {
                ThemedDialog.Error(null, "Ábrelo sin permisos de administrador",
                    "Iberia Smart Disc no necesita ni debe usar permisos de administrador. Ciérralo y ábrelo con doble clic normal, sin «Ejecutar como administrador».");
                return 3;
            }

            if (command.Portable) AppPaths.UsePortable();

            var log = new AppLog(AppPaths.LogsDirectory, enabled: LoggingPreference());
            Application.ThreadException += (s, e) => log.Error("Error no controlado", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => log.Error("Error fatal", e.ExceptionObject as Exception);

            try
            {
                if (command.Mode == RunMode.Exit) return CommandChannel.StopResident(TimeSpan.FromSeconds(10)) ? 0 : 1;
                if (command.Mode == RunMode.Uninstall) return RunUninstall(command, log);
                if (!command.Portable && !AppPaths.IsInstalledCopy) return RunSetup(command, log);
                return RunResident(command, log);
            }
            catch (Exception ex)
            {
                log.Error("Error al arrancar", ex);
                ThemedDialog.Error(null, "Iberia Smart Disc no pudo arrancar", ex.Message);
                return 1;
            }
        }

        /// <summary>Copia instalada (o portátil): una sola instancia residente.</summary>
        private static int RunResident(CommandLine command, AppLog log)
        {
            using (var instance = SingleInstance.Acquire())
            {
                if (!instance.IsFirst)
                {
                    string? message = command.ToChannelMessage();
                    bool delivered = message != null && CommandChannel.TrySend(message);
                    var residents = CommandChannel.FindResidents();
                    bool residentAlive = residents.Count > 0;
                    foreach (var process in residents) process.Dispose();
                    if (delivered || residentAlive) return 0;
                    // El nombre de instancia está ocupado, pero no por Iberia Smart Disc: se arranca igual.
                    log.Warn("Otro programa ocupa el nombre de instancia de Iberia Smart Disc; se arranca igualmente.");
                }

                Installer.CleanupLeftovers();
                var state = AppState.Load(log);
                log.Enabled = state.Settings.Logging;
                log.DeleteOld();
                using (var app = new ResidentApp(state, command))
                {
                    Application.Run(app);
                }
                return 0;
            }
        }

        /// <summary>
        /// Ejecutable descargado: instala, actualiza si es más nuevo o abre la
        /// copia instalada. Repetir el doble clic nunca duplica nada.
        /// </summary>
        private static int RunSetup(CommandLine command, AppLog log)
        {
            var installed = Installer.InstalledVersion();
            if (installed == null)
            {
                using (var form = new InstallForm(InstallMode.Install, null, log))
                {
                    if (form.ShowDialog() != DialogResult.OK) return 0;
                }
                Installer.StartInstalled("--first-run");
                return 0;
            }

            if (AppInfo.ComparableVersion > installed)
            {
                using (var form = new InstallForm(InstallMode.Update, installed, log))
                {
                    if (form.ShowDialog() != DialogResult.OK) return 0;
                }
                Installer.StartInstalled(string.Empty);
                return 0;
            }

            // Misma versión o anterior: se abre la instalada.
            string message = command.ToChannelMessage() ?? "show";
            if (!CommandChannel.TrySend(message)) Installer.StartInstalled(ForwardArguments(command));
            return 0;
        }

        private static int RunUninstall(CommandLine command, AppLog log)
        {
            if (!Uninstaller.IsInstalled)
            {
                if (!command.Quiet) ThemedDialog.Info(null, AppInfo.Name, "Iberia Smart Disc no está instalado en este usuario.");
                return 0;
            }

            var options = UninstallOptions.Everything;
            if (!command.Quiet)
            {
                using (var form = new UninstallForm())
                {
                    if (form.ShowDialog() != DialogResult.OK) return 0;
                    options = form.Options;
                }
            }

            if (!CommandChannel.StopResident(TimeSpan.FromSeconds(10)))
            {
                if (!command.Quiet) ThemedDialog.Error(null, "No se pudo desinstalar", "Iberia Smart Disc sigue abierto. Ciérralo desde el icono de la bandeja (Salir) y vuelve a intentarlo.");
                return 1;
            }

            Uninstaller.Run(options, log);
            if (!command.Quiet)
            {
                ThemedDialog.Info(null, "Iberia Smart Disc se ha desinstalado", "Gracias por usarlo. Tus discos seguirán funcionando si vuelves a instalarlo desde " + AppInfo.WebsiteLabel + ".");
            }
            return 0;
        }

        /// <summary>Preferencia de logs guardada (también para instalar y desinstalar).</summary>
        private static bool LoggingPreference()
        {
            try
            {
                return Core.Configuration.AppSettings.FromJson(Core.Configuration.JsonFileStore.Load(AppPaths.SettingsFile, out _)).Logging;
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                return true;
            }
        }

        private static string ForwardArguments(CommandLine command)
        {
            switch (command.Mode)
            {
                case RunMode.Settings:
                    return "--settings";
                case RunMode.Configure:
                    return "--configure " + command.GameId;
                case RunMode.TestDisc:
                    // Una barra final antes de la comilla la escaparía ("D:\" → D:").
                    string path = command.Path!;
                    if (path.EndsWith("\\", StringComparison.Ordinal)) path += "\\";
                    return "--test-disc \"" + path + "\"";
                default:
                    return string.Empty;
            }
        }
    }
}
