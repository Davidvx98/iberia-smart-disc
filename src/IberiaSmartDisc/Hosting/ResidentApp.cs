using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using IberiaSmartDisc.Discs;
using IberiaSmartDisc.Native;
using IberiaSmartDisc.Setup;
using IberiaSmartDisc.UI;

namespace IberiaSmartDisc.Hosting
{
    /// <summary>
    /// Instancia residente. Sin ventanas abiertas solo existe la ventana oculta
    /// que escucha a Windows: sin temporizadores, sin red y sin tocar el disco.
    /// </summary>
    internal sealed class ResidentApp : ApplicationContext, IAppController
    {
        /// <summary>Tras iniciar sesión o volver de suspensión, se pregunta antes de abrir nada.</summary>
        private static readonly TimeSpan LoginGrace = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan ResumeGrace = TimeSpan.FromSeconds(60);

        private readonly HostWindow _host;
        private readonly OpticalDriveMonitor _monitor;
        private readonly DiscCoordinator _coordinator;
        private TrayIcon? _tray;
        private MainForm? _main;
        private SettingsForm? _settings;
        private DateTime _cautiousUntilUtc;
        private bool _exiting;
        private bool _shutDown;

        public ResidentApp(AppState state, CommandLine command)
        {
            State = state;
            if (!(SynchronizationContext.Current is WindowsFormsSynchronizationContext))
            {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            }

            _host = new HostWindow();
            _host.CommandReceived += OnCommand;
            _host.Resumed += () =>
            {
                _cautiousUntilUtc = DateTime.UtcNow + ResumeGrace;
                State.Log.Info("El PC vuelve de suspensión.");
            };

            _coordinator = new DiscCoordinator(this, () => DateTime.UtcNow < _cautiousUntilUtc);
            _host.SessionLocked += () => _coordinator!.OnSessionLocked();
            _host.SessionUnlocked += () =>
            {
                _cautiousUntilUtc = DateTime.UtcNow + ResumeGrace;
                _coordinator!.OnSessionUnlocked();
            };
            _monitor = new OpticalDriveMonitor(_host);
            _monitor.DiscArrived += root => _coordinator.Enqueue(new DiscEvent(root, DiscEventReason.Inserted));
            _monitor.DiscRemoved += root => _coordinator.OnRemoved(root);
            _monitor.SetCompatibilityPolling(State.Settings.CompatibilityPolling);

            if (command.Mode == RunMode.Background) _cautiousUntilUtc = DateTime.UtcNow + LoginGrace;
            if (State.Settings.TrayIcon) _tray = new TrayIcon(this);
            if (!IsPortable) RepairStartupEntry();

            State.Log.Info("Iberia Smart Disc " + AppInfo.VersionText + " en marcha (" + command.Mode + (IsPortable ? ", portátil" : string.Empty) + ")");

            switch (command.Mode)
            {
                case RunMode.Background:
                    break;
                case RunMode.Settings:
                    ShowSettings();
                    break;
                case RunMode.Configure:
                    OpenSettings(command.GameId, openGameDialog: true);
                    break;
                case RunMode.TestDisc:
                    SimulateDisc(command.Path!);
                    break;
                default:
                    ShowMainWindow(command.FirstRun);
                    break;
            }

            ScanDiscsAlreadyInserted();
            if (command.Mode == RunMode.Background) TrimMemory();
        }

        public AppState State { get; }

        public bool IsPortable => AppPaths.Portable;

        public void ShowMainWindow() => ShowMainWindow(false);

        public void ShowSettings(string? gameId = null) => OpenSettings(gameId, openGameDialog: false);

        public async void ConfigureGame(string gameId, IWin32Window owner)
        {
            try
            {
                await _coordinator.ConfigureAsync(gameId, owner);
            }
            catch (Exception ex)
            {
                State.Log.Error("Error configurando " + gameId, ex);
            }
        }

        public void SetPaused(bool paused)
        {
            State.Settings.Paused = paused;
            State.SaveSettings();
            State.Log.Info(paused ? "Detección en pausa." : "Detección reanudada.");
        }

        public void SetStartWithWindows(bool enabled)
        {
            State.Settings.StartWithWindows = enabled;
            State.SaveSettings();
            if (IsPortable) return;
            try
            {
                if (enabled) StartupRegistration.Enable(AppPaths.InstalledExe, userRequested: true);
                else StartupRegistration.Disable();
            }
            catch (Exception ex)
            {
                State.Log.Error("No se pudo cambiar el inicio automático", ex);
                ThemedDialog.Error(_main, "No se pudo cambiar el inicio con Windows", ex.Message);
            }
            State.NotifyChanged();
        }

        public void SetTrayIcon(bool visible)
        {
            State.Settings.TrayIcon = visible;
            State.SaveSettings();
            if (visible && _tray == null)
            {
                _tray = new TrayIcon(this);
            }
            else if (!visible && _tray != null)
            {
                _tray.Dispose();
                _tray = null;
            }
        }

        public void SetCompatibilityPolling(bool enabled)
        {
            State.Settings.CompatibilityPolling = enabled;
            State.SaveSettings();
            _monitor.SetCompatibilityPolling(enabled);
        }

        public void SimulateDisc(string folder)
        {
            if (!Directory.Exists(folder))
            {
                State.Log.Warn("Prueba con una carpeta que no existe.");
                ThemedDialog.Error(null, "Carpeta no encontrada", "La carpeta elegida para la prueba no existe.");
                return;
            }
            _coordinator.Enqueue(new DiscEvent(Path.GetFullPath(folder), DiscEventReason.Test));
        }

        public IEnumerable<string> DescribePlatforms()
        {
            foreach (var provider in State.Resolver.Providers)
            {
                foreach (var line in provider.Describe(State.Settings)) yield return line;
            }
        }

        public void RequestUninstall(IWin32Window? owner)
        {
            if (IsPortable) return;
            UninstallOptions options;
            using (var form = new UninstallForm())
            {
                if ((owner != null ? form.ShowDialog(owner) : form.ShowDialog()) != DialogResult.OK) return;
                options = form.Options;
            }
            _exiting = true;
            ShutDown();
            try
            {
                Uninstaller.Run(options, State.Log);
                ThemedDialog.Info(null, "Iberia Smart Disc se ha desinstalado", "Gracias por usarlo. Tus discos seguirán funcionando si vuelves a instalarlo desde " + AppInfo.WebsiteLabel + ".");
            }
            catch (Exception ex)
            {
                ThemedDialog.Error(null, "La desinstalación no terminó", ex.Message);
            }
            ExitThread();
        }

        public void ExitApplication()
        {
            if (_exiting) return;
            _exiting = true;
            State.Log.Info("Saliendo.");
            ShutDown();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ShutDown();
            base.Dispose(disposing);
        }

        private void ShutDown()
        {
            if (_shutDown) return;
            _shutDown = true;
            _coordinator.Dispose();
            _monitor.Dispose();
            _tray?.Dispose();
            _tray = null;
            DiscToast.CloseCurrent();
            _settings?.Close();
            _main?.Close();
            _host.Dispose();
        }

        private void ShowMainWindow(bool firstRun)
        {
            if (_main == null || _main.IsDisposed)
            {
                _main = new MainForm(this, firstRun);
                _main.FormClosed += (s, e) =>
                {
                    _main = null;
                    TrimMemory();
                };
            }
            BringToFront(_main);
        }

        private void OpenSettings(string? gameId, bool openGameDialog)
        {
            if (_settings == null || _settings.IsDisposed)
            {
                _settings = new SettingsForm(this, gameId, openGameDialog);
                _settings.FormClosed += (s, e) =>
                {
                    _settings = null;
                    TrimMemory();
                };
                BringToFront(_settings);
                return;
            }
            BringToFront(_settings);
            if (gameId != null)
            {
                _settings.SelectGame(gameId);
                if (openGameDialog) ConfigureGame(gameId, _settings);
            }
        }

        private static void BringToFront(Form form)
        {
            if (!form.Visible) form.Show();
            if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
            form.Activate();
            NativeMethods.SetForegroundWindow(form.Handle);
        }

        private void OnCommand(string message)
        {
            if (_exiting) return;
            string[] parts = message.Split(new[] { '\n' }, 2);
            switch (parts[0])
            {
                case "show":
                    ShowMainWindow();
                    break;
                case "settings":
                    ShowSettings();
                    break;
                case "configure" when parts.Length == 2 && Core.Manifest.ManifestRules.IsGameId(parts[1]):
                    OpenSettings(parts[1], openGameDialog: true);
                    break;
                case "test-disc" when parts.Length == 2:
                    SimulateDisc(parts[1]);
                    break;
                case "exit":
                    ExitApplication();
                    break;
                default:
                    State.Log.Warn("Orden desconocida recibida.");
                    break;
            }
        }

        /// <summary>Discos que ya estaban dentro: se procesan como «arranque» (siempre preguntan).</summary>
        private void ScanDiscsAlreadyInserted()
        {
            Task.Run(() => OpticalDriveMonitor.CurrentOpticalRoots())
                .ContinueWith(task =>
                {
                    if (task.Status != TaskStatus.RanToCompletion || _exiting) return;
                    foreach (var root in task.Result) _coordinator.Enqueue(new DiscEvent(root, DiscEventReason.Startup));
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>
        /// Si la clave Run apunta a otra ruta, se corrige. Si alguien la quitó, no se
        /// vuelve a crear: se respeta y la casilla pasa a desactivada.
        /// </summary>
        private void RepairStartupEntry()
        {
            try
            {
                string? current = StartupRegistration.CurrentCommand();
                if (current == null)
                {
                    if (State.Settings.StartWithWindows)
                    {
                        State.Settings.StartWithWindows = false;
                        State.SaveSettings();
                    }
                }
                else if (!string.Equals(current, StartupRegistration.CommandFor(AppPaths.InstalledExe), StringComparison.OrdinalIgnoreCase))
                {
                    StartupRegistration.Enable(AppPaths.InstalledExe, userRequested: false);
                }
            }
            catch (Exception ex)
            {
                State.Log.Warn("No se pudo revisar el inicio automático: " + ex.Message);
            }
        }

        private static void TrimMemory()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            NativeMethods.TrimWorkingSet();
        }
    }
}
