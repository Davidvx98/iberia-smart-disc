using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using IberiaSmartDisc.Core.Configuration;
using IberiaSmartDisc.Core.Manifest;
using IberiaSmartDisc.Core.Platforms;
using IberiaSmartDisc.Core.Platforms.Local;
using IberiaSmartDisc.Core.Resolution;
using IberiaSmartDisc.Hosting;
using IberiaSmartDisc.Launching;
using IberiaSmartDisc.Native;
using IberiaSmartDisc.UI;

namespace IberiaSmartDisc.Discs
{
    internal enum DiscEventReason
    {
        /// <summary>Disco metido con el programa ya en marcha.</summary>
        Inserted,

        /// <summary>Disco que ya estaba dentro al arrancar el programa.</summary>
        Startup,

        /// <summary>Carpeta elegida para probar (--test-disc).</summary>
        Test,
    }

    internal sealed class DiscEvent
    {
        public DiscEvent(string root, DiscEventReason reason)
        {
            Root = root;
            Reason = reason;
        }

        public string Root { get; }

        public DiscEventReason Reason { get; }
    }

    /// <summary>
    /// Decide qué hacer con cada disco. Procesa uno cada vez (dos lectores o
    /// dos discos no abren dos diálogos a la vez) y nunca abre nada de golpe
    /// si el disco ya estaba dentro al encender el PC o al volver de suspensión.
    /// </summary>
    internal sealed class DiscCoordinator : IDisposable
    {
        private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(20);

        private readonly IAppController _app;
        private readonly Func<bool> _isCautiousPeriod;
        private readonly Queue<DiscEvent> _queue = new Queue<DiscEvent>();
        private readonly Dictionary<string, DateTime> _recent = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private bool _busy;
        private bool _disposed;
        private readonly HashSet<string> _deferredWhileLocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string? _activeRoot;
        private DiscEventReason? _activeReason;
        private DiscToast? _activeToast;
        private bool _activeRemoved;
        private bool _locked;

        public DiscCoordinator(IAppController app, Func<bool> isCautiousPeriod)
        {
            _app = app;
            _isCautiousPeriod = isCautiousPeriod;
        }

        private AppState State => _app.State;

        public void Enqueue(DiscEvent discEvent)
        {
            if (_disposed) return;
            if (_locked && discEvent.Reason == DiscEventReason.Inserted)
            {
                // Con la sesión bloqueada no se abre nada: se pregunta al desbloquear.
                _deferredWhileLocked.Add(discEvent.Root);
                State.Log.Info("Sesión bloqueada: " + discEvent.Root + " se revisará al desbloquear.");
                return;
            }
            if (_queue.Any(e => SameRoot(e.Root, discEvent.Root)) || (_activeRoot != null && SameRoot(_activeRoot, discEvent.Root))) return;
            // Las pruebas con carpeta llegan de otros procesos: una cada vez.
            if (discEvent.Reason == DiscEventReason.Test && (_activeReason == DiscEventReason.Test || _queue.Any(e => e.Reason == DiscEventReason.Test))) return;
            _queue.Enqueue(discEvent);
            Pump();
        }

        public void OnSessionLocked()
        {
            _locked = true;
            State.Log.Info("Sesión bloqueada.");
            _activeToast?.Cancel();
        }

        public void OnSessionUnlocked()
        {
            _locked = false;
            var deferred = _deferredWhileLocked.ToList();
            _deferredWhileLocked.Clear();
            foreach (var root in deferred) Enqueue(new DiscEvent(root, DiscEventReason.Startup));
        }

        /// <summary>Disco expulsado: se cancela su aviso y se olvida para poder volver a meterlo.</summary>
        public void OnRemoved(string root)
        {
            var pending = _queue.Where(e => !SameRoot(e.Root, root)).ToList();
            _queue.Clear();
            foreach (var e in pending) _queue.Enqueue(e);
            foreach (var key in _recent.Keys.Where(k => k.StartsWith(root + "|", StringComparison.OrdinalIgnoreCase)).ToList()) _recent.Remove(key);
            _deferredWhileLocked.Remove(root);
            if (_activeRoot != null && SameRoot(_activeRoot, root))
            {
                State.Log.Info("Disco expulsado en " + root + ": se cancela el aviso.");
                _activeRemoved = true;
                _activeToast?.Cancel();
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _queue.Clear();
            _activeToast?.Cancel();
        }

        private async void Pump()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                while (_queue.Count > 0 && !_disposed)
                {
                    var next = _queue.Dequeue();
                    try
                    {
                        await ProcessAsync(next);
                    }
                    catch (Exception ex)
                    {
                        State.Log.Error("Error procesando " + next.Root, ex);
                    }
                    finally
                    {
                        _activeRoot = null;
                        _activeReason = null;
                        _activeToast = null;
                        _activeRemoved = false;
                    }
                }
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task ProcessAsync(DiscEvent discEvent)
        {
            var log = State.Log;
            if (State.Settings.Paused && discEvent.Reason != DiscEventReason.Test)
            {
                log.Info("Detección en pausa: se ignora " + discEvent.Root);
                return;
            }
            if (discEvent.Reason == DiscEventReason.Inserted && !IsActiveConsoleSession())
            {
                log.Info("Otra sesión de Windows está activa: se ignora " + discEvent.Root);
                return;
            }

            _activeRoot = discEvent.Root;
            _activeReason = discEvent.Reason;
            log.Info("Disco detectado: " + discEvent.Root + " (" + discEvent.Reason + ")");
            var timeout = discEvent.Reason == DiscEventReason.Inserted ? TimeSpan.FromSeconds(25) : TimeSpan.FromSeconds(4);
            var read = await Task.Run(() => DiscReader.WaitAndRead(discEvent.Root, timeout));
            if (_disposed) return;
            // Número de serie del volumen: identifica este disco hasta el momento de abrir nada.
            uint? serial = DiscReader.IsDriveRoot(discEvent.Root) ? NativeMethods.GetVolumeSerial(discEvent.Root) : null;

            switch (read.Status)
            {
                case ManifestReadStatus.NotFound:
                    log.Info("Sin " + DiscManifest.FileName + ": no es un disco de Iberia, se ignora.");
                    if (discEvent.Reason == DiscEventReason.Test) DiscToast.Info("Prueba de disco", "No hay " + DiscManifest.FileName, discEvent.Root);
                    return;
                case ManifestReadStatus.NotReady:
                    log.Info("La unidad " + discEvent.Root + " no estuvo lista a tiempo.");
                    return;
                case ManifestReadStatus.ReadError:
                    log.Warn("No se pudo leer el disco: " + read.Message);
                    DiscToast.Info("Iberia Smart Disc", "No se pudo leer el disco.", "Límpialo y vuelve a meterlo.");
                    return;
                case ManifestReadStatus.Invalid:
                    log.Warn("Manifiesto no válido: " + read.Message);
                    string title = read.Error?.Error == ManifestError.UnsupportedFormat ? "Actualiza Iberia Smart Disc" : "El manifiesto Iberia Disc no es válido.";
                    DiscToast.Info("Disco no compatible", title, read.Message ?? string.Empty, null, 12);
                    return;
            }

            var manifest = read.Manifest!;
            string duplicateKey = discEvent.Root + "|" + manifest.Sha256;
            if (discEvent.Reason != DiscEventReason.Test && _recent.TryGetValue(duplicateKey, out var seenAt) && DateTime.UtcNow - seenAt < DuplicateWindow)
            {
                log.Info("Aviso duplicado del mismo disco: se ignora.");
                return;
            }
            _recent[duplicateKey] = DateTime.UtcNow;
            log.Info("Juego: " + manifest.GameId + " (formato " + manifest.Format + ", " + (manifest.DiscType == DiscType.DataDisc ? "con contenido" : "Smart Disc") + ")");

            var record = State.Library.RecordInsertion(manifest, DateTime.UtcNow);
            State.SaveLibrary();
            await Task.Run(() =>
            {
                ManifestCache.Store(manifest, log);
                CoverCache.StoreFromDisc(manifest, discEvent.Root, log);
            });
            State.NotifyChanged();

            var context = new ResolveContext(manifest, discEvent.Root, State.Settings);
            var resolution = await Task.Run(() => State.Resolver.Resolve(context, record));
            foreach (var warning in resolution.Warnings) log.Warn(warning);
            log.Info("Instalaciones: " + (resolution.Candidates.Count == 0 ? "ninguna" : string.Join(", ", resolution.Candidates.Select(c => LaunchKinds.DisplayName(c.Kind) + " → " + c.Detail))));
            if (_disposed) return;

            bool cautious = discEvent.Reason != DiscEventReason.Inserted || _isCautiousPeriod();
            await DecideAsync(discEvent, manifest, record, resolution, cautious, serial);
        }

        private async Task DecideAsync(DiscEvent discEvent, DiscManifest manifest, GameRecord record, ResolutionResult resolution, bool cautious, uint? serial)
        {
            var settings = State.Settings;
            var choice = resolution.Preferred;
            bool explicitChoice = false;

            if (choice == null)
            {
                bool mustAsk = resolution.PreferredMissing
                    || resolution.Candidates.Count == 0
                    || (resolution.Candidates.Count > 1 && settings.AskWhenMultiple);
                if (!mustAsk)
                {
                    choice = resolution.Candidates[0];
                }
                else
                {
                    var mode = resolution.PreferredMissing ? ChoiceMode.Broken
                        : resolution.Candidates.Count == 0 ? ChoiceMode.NotInstalled
                        : ChoiceMode.Multiple;
                    var result = ShowChoice(manifest, resolution, mode, alwaysUse: true, owner: null);
                    if (result == null) return;
                    if (result.OpenSettings)
                    {
                        _app.ShowSettings(manifest.GameId);
                        return;
                    }
                    if (result.Install != null)
                    {
                        var install = result.Install;
                        if (install.Kind == LaunchKind.Disc)
                        {
                            if (!ConfirmDiscProgram(manifest, install.Target, installer: true)) return;
                            if (!StillSameDisc(discEvent.Root, manifest, serial)) return;
                        }
                        RunLaunch(manifest, install.Target, install.Label, recordLaunch: false);
                        return;
                    }
                    choice = result.Candidate;
                    if (choice == null) return;
                    State.Library.SetPreference(manifest.GameId, choice, result.AlwaysUse);
                    State.SaveLibrary();
                    explicitChoice = true;
                }
            }

            if (choice.Kind == LaunchKind.Disc)
            {
                // Siempre se pregunta: el manifiesto se puede copiar a otro disco con otro programa.
                if (!ConfirmDiscProgram(manifest, choice.Target, installer: false)) return;
                explicitChoice = true;
            }

            if (!explicitChoice)
            {
                string caption = discEvent.Reason switch
                {
                    DiscEventReason.Startup => "Hay un disco en la unidad " + DriveLabel(discEvent.Root),
                    DiscEventReason.Test => "Prueba de disco",
                    _ => "Disco detectado · " + DriveLabel(discEvent.Root),
                };
                string subtitle = "Se abrirá con " + choice.DisplayTitle;
                if (cautious || !settings.AutoLaunch)
                {
                    // Al encender el PC con el disco dentro, nunca se abre nada sin preguntar.
                    if (!await AwaitToast(DiscToast.Prompt(caption, cautious ? "¿Jugar a " + manifest.DisplayName + "?" : manifest.DisplayName, subtitle, CoverCache.Load(manifest.GameId))))
                    {
                        State.Log.Info("El usuario decidió no jugar.");
                        return;
                    }
                }
                else if (settings.ShowLaunchNotice && settings.LaunchDelaySeconds > 0)
                {
                    if (!await AwaitToast(DiscToast.Countdown(caption, manifest.DisplayName, subtitle, CoverCache.Load(manifest.GameId), settings.LaunchDelaySeconds)))
                    {
                        State.Log.Info("Cuenta atrás cancelada.");
                        return;
                    }
                }
                else if (settings.ShowLaunchNotice)
                {
                    DiscToast.Info(caption, manifest.DisplayName, "Abriendo con " + choice.DisplayTitle + "…", CoverCache.Load(manifest.GameId), 5);
                }
            }

            if (_locked)
            {
                State.Log.Info("La sesión se bloqueó antes de abrir el juego: no se abre.");
                return;
            }
            if (choice.Kind == LaunchKind.Disc && !StillSameDisc(discEvent.Root, manifest, serial)) return;
            RunLaunch(manifest, choice.Target, choice.DisplayTitle, recordLaunch: true);
        }

        /// <summary>Configurar un juego desde la ventana de configuración, sin disco.</summary>
        public async Task ConfigureAsync(string gameId, IWin32Window owner)
        {
            var manifest = ManifestCache.Load(gameId);
            var record = State.Library.Find(gameId);
            if (manifest == null || record == null)
            {
                ThemedDialog.Info(owner, "Inserta el disco una vez", "Para configurar este juego, mete su disco al menos una vez con Iberia Smart Disc en marcha.");
                return;
            }
            var context = new ResolveContext(manifest, null, State.Settings);
            var resolution = await Task.Run(() => State.Resolver.Resolve(context, record));
            var result = ShowChoice(manifest, resolution, ChoiceMode.Configure, alwaysUse: record.AlwaysUse || record.PreferredKey == null, owner: owner);
            if (result == null) return;
            if (result.Install != null)
            {
                RunLaunch(manifest, result.Install.Target, result.Install.Label, recordLaunch: false);
                return;
            }
            if (result.Candidate == null) return;
            State.Library.SetPreference(gameId, result.Candidate, result.AlwaysUse);
            State.SaveLibrary();
        }

        /// <summary>
        /// Confirmación informada para ejecutar algo del disco: nombre del archivo a
        /// la vista, carpeta, argumentos y aviso de que el origen no está verificado.
        /// Por defecto se cancela.
        /// </summary>
        private bool ConfirmDiscProgram(DiscManifest manifest, LaunchTarget target, bool installer)
        {
            string path = target.ExecutablePath ?? string.Empty;
            var details = new List<string> { "Carpeta: " + (Path.GetDirectoryName(path) ?? string.Empty) };
            if (!string.IsNullOrEmpty(target.Arguments)) details.Add("Argumentos: " + target.Arguments);
            bool accepted = ThemedDialog.ConfirmRisky(
                installer ? "¿Ejecutar el instalador del disco?" : "¿Abrir el programa del disco?",
                manifest.DisplayName + " quiere ejecutar:",
                Path.GetFileName(path),
                details,
                "Origen no verificado: Iberia Smart Disc no puede comprobar quién grabó este disco. Continúa solo si es un disco original de Iberia Custom DVDs o confías en quien te lo dio. Se preguntará cada vez.",
                installer ? "Ejecutar instalador" : "Abrir programa");
            State.Log.Info(accepted ? "El usuario aceptó ejecutar el programa del disco." : "El usuario no aceptó ejecutar el programa del disco.");
            return accepted;
        }

        /// <summary>Justo antes de abrir algo del disco: ¿sigue siendo el mismo disco?</summary>
        private bool StillSameDisc(string root, DiscManifest manifest, uint? serial)
        {
            bool same = !_activeRemoved;
            if (same && DiscReader.IsDriveRoot(root))
            {
                uint? now = NativeMethods.GetVolumeSerial(root);
                same = now != null && now == serial;
            }
            if (same)
            {
                var read = ManifestReader.Read(root);
                same = read.Status == ManifestReadStatus.Ok && read.Manifest!.Sha256 == manifest.Sha256;
            }
            if (!same)
            {
                State.Log.Warn("El disco cambió antes de abrir el programa: no se abre nada.");
                DiscToast.Info("No se ha abierto nada", manifest.DisplayName, "El disco se ha cambiado o expulsado. Vuelve a meterlo para empezar de nuevo.", null, 10);
            }
            return same;
        }

        private ChoiceResult? ShowChoice(DiscManifest manifest, ResolutionResult resolution, ChoiceMode mode, bool alwaysUse, IWin32Window? owner)
        {
            var roots = LocalGameSearch.DefaultRoots(State.SystemInfo, State.Settings.SearchPaths);
            var search = new LocalGameSearch();
            using (var form = new GameChoiceForm(manifest, resolution, mode, alwaysUse, CoverCache.Load(manifest.GameId),
                token => Task.Run(() => search.Search(manifest, roots, token), token)))
            {
                var dialogResult = owner != null ? form.ShowDialog(owner) : form.ShowDialog();
                if (form.Result.OpenSettings) return form.Result;
                return dialogResult == DialogResult.OK ? form.Result : null;
            }
        }

        private async Task<bool> AwaitToast(DiscToast toast)
        {
            _activeToast = toast;
            try
            {
                return await toast.Result;
            }
            finally
            {
                _activeToast = null;
            }
        }

        private void RunLaunch(DiscManifest manifest, LaunchTarget target, string via, bool recordLaunch)
        {
            State.Log.Info("Abriendo " + manifest.GameId + " con " + via + ": " + target);
            var outcome = GameLauncher.Launch(target);
            switch (outcome.Status)
            {
                case LaunchStatus.Started:
                    if (recordLaunch)
                    {
                        State.Library.RecordLaunch(manifest.GameId, target.Kind, DateTime.UtcNow);
                        State.SaveLibrary();
                    }
                    break;
                case LaunchStatus.AlreadyRunning:
                    DiscToast.Info("Ya está abierto", manifest.DisplayName, "Lo hemos traído al frente.", CoverCache.Load(manifest.GameId));
                    break;
                case LaunchStatus.Cancelled:
                    State.Log.Info("El usuario canceló el permiso de Windows.");
                    break;
                case LaunchStatus.NotFound:
                    State.Log.Warn("No se encontró lo que había que abrir: " + outcome.Message);
                    DiscToast.Info("No se pudo abrir", manifest.DisplayName, "La instalación configurada ya no existe. Cámbiala en Configuración → Mis juegos.", CoverCache.Load(manifest.GameId), 12);
                    break;
                default:
                    State.Log.Warn("Fallo al abrir: " + outcome.Message);
                    DiscToast.Info("No se pudo abrir", manifest.DisplayName, outcome.Message ?? "Error desconocido.", CoverCache.Load(manifest.GameId), 12);
                    break;
            }
        }

        private static bool IsActiveConsoleSession()
        {
            uint active = NativeMethods.WTSGetActiveConsoleSessionId();
            if (active == 0xFFFFFFFF) return true;
            using (var current = Process.GetCurrentProcess())
            {
                return current.SessionId == active;
            }
        }

        private static string DriveLabel(string root) => DiscReader.IsDriveRoot(root) ? root.Substring(0, 2) : Path.GetFileName(root.TrimEnd('\\', '/'));

        private static bool SameRoot(string a, string b) =>
            string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
    }
}
