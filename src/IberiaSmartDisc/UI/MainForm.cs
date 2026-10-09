using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using IberiaSmartDisc.Core.Platforms;
using IberiaSmartDisc.Discs;
using IberiaSmartDisc.Hosting;
using IberiaSmartDisc.Setup;

namespace IberiaSmartDisc.UI
{
    /// <summary>Ventana principal: pequeña, con la web bien visible y lo esencial a mano.</summary>
    internal sealed class MainForm : Form
    {
        private readonly IAppController _app;
        private readonly Label _status;
        private readonly LinkLabel _pauseLink;
        private readonly ToggleSwitch _startup;
        private readonly ToggleSwitch _autoLaunch;
        private readonly ToggleSwitch _notice;
        private readonly ToggleSwitch _tray;
        private readonly Label _startupHint;
        private readonly PictureBox _lastCover;
        private readonly Label _lastGame;
        private readonly Label _lastPlatform;
        private bool _refreshing;

        public MainForm(IAppController app, bool firstRun)
        {
            _app = app;
            Theme.Prepare(this);
            Text = AppInfo.Name + " · " + AppInfo.DisplayVersion;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            int width = Theme.S(400);

            var layout = Theme.Column(24);
            layout.MinimumSize = new Size(width + Theme.S(48), 0);

            // Cabecera
            var header = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, BackColor = Color.Transparent, Margin = Theme.Pad(0, 0, 0, 14) };
            header.Controls.Add(new PictureBox { Image = Theme.Logo, SizeMode = PictureBoxSizeMode.Zoom, Size = Theme.S(56, 56), Margin = Theme.Pad(0, 0, 14, 0) }, 0, 0);
            var titles = Theme.Column();
            titles.Controls.Add(Theme.Label(AppInfo.Name, Theme.Title));
            titles.Controls.Add(Theme.Label("v" + AppInfo.DisplayVersion, Theme.BodyBold, Theme.Accent));
            titles.Controls.Add(Theme.Label(firstRun ? "¡Listo! Mete un disco y a jugar." : "Mete tu disco y juega.", Theme.Body, Theme.TextMuted));
            header.Controls.Add(titles, 1, 0);
            layout.Controls.Add(header);

            // La web, bien visible.
            var web = new Panel { BackColor = Theme.Surface, Padding = Theme.Pad(16, 12, 16, 12), Margin = Theme.Pad(0, 0, 0, 14), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(width, 0) };
            var webColumn = Theme.Column();
            webColumn.Controls.Add(Theme.Label("Visítanos en nuestra web:", Theme.Body, Theme.TextMuted));
            var link = Theme.Link(AppInfo.WebsiteLabel, Theme.Hero);
            link.LinkClicked += (s, e) => OpenUrl(AppInfo.WebsiteUrl);
            link.AccessibleName = "Abrir " + AppInfo.WebsiteLabel + " en el navegador";
            webColumn.Controls.Add(link);
            web.Controls.Add(webColumn);
            layout.Controls.Add(web);

            // Estado
            _status = Theme.Label(string.Empty, Theme.BodyBold);
            _pauseLink = Theme.Link(string.Empty);
            _pauseLink.LinkClicked += (s, e) => _app.SetPaused(!_app.State.Settings.Paused);
            layout.Controls.Add(Theme.Row(Theme.Label("Estado:", Theme.Body, Theme.TextMuted), _status, _pauseLink));

            // Opciones principales
            _startup = new ToggleSwitch("Iniciar con Windows");
            _startup.CheckedChanged += (s, e) => { if (!_refreshing) _app.SetStartWithWindows(_startup.Checked); };
            _startupHint = Theme.Label("Desactivado en el Administrador de tareas; actívalo aquí o allí.", Theme.Small, Theme.Warning, width);
            _autoLaunch = new ToggleSwitch("Abrir el juego al insertar el disco");
            _autoLaunch.CheckedChanged += (s, e) => ApplySetting(() => _app.State.Settings.AutoLaunch = _autoLaunch.Checked);
            _notice = new ToggleSwitch("Mostrar aviso con cuenta atrás");
            _notice.CheckedChanged += (s, e) => ApplySetting(() => _app.State.Settings.ShowLaunchNotice = _notice.Checked);
            _tray = new ToggleSwitch("Icono en la bandeja del sistema");
            _tray.CheckedChanged += (s, e) => { if (!_refreshing) _app.SetTrayIcon(_tray.Checked); };
            layout.Controls.Add(_startup);
            layout.Controls.Add(_startupHint);
            layout.Controls.Add(_autoLaunch);
            layout.Controls.Add(_notice);
            layout.Controls.Add(_tray);

            // Último disco
            var last = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, BackColor = Color.Transparent, Margin = Theme.Pad(0, 14, 0, 14) };
            _lastCover = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Size = Theme.S(36, 48), Margin = Theme.Pad(0, 0, 12, 0) };
            last.Controls.Add(_lastCover, 0, 0);
            var lastText = Theme.Column();
            _lastGame = Theme.Label(string.Empty, Theme.Body, Theme.Text, width - Theme.S(60));
            _lastPlatform = Theme.Label(string.Empty, Theme.Body, Theme.TextMuted, width - Theme.S(60));
            lastText.Controls.Add(_lastGame);
            lastText.Controls.Add(_lastPlatform);
            last.Controls.Add(lastText, 1, 0);
            layout.Controls.Add(last);

            // Botones
            var settings = new ThemedButton("Configuración");
            settings.Click += (s, e) => _app.ShowSettings();
            var minimize = new ThemedButton("Minimizar");
            minimize.Click += (s, e) => MinimizeToBackground();
            var buttons = Theme.Row(settings, minimize);
            if (!_app.IsPortable)
            {
                var uninstall = new ThemedButton("Desinstalar", ButtonKind.Danger);
                uninstall.Click += (s, e) => _app.RequestUninstall(this);
                buttons.Controls.Add(uninstall);
            }
            layout.Controls.Add(buttons);

            var footer = Theme.Label(
                "Cerrar esta ventana no detiene la detección. v" + AppInfo.DisplayVersion + (_app.IsPortable ? " · modo portátil" : string.Empty),
                Theme.Small, Theme.TextMuted, width);
            footer.Margin = Theme.Pad(0, 14, 0, 0);
            layout.Controls.Add(footer);

            Controls.Add(layout);
            _app.State.Changed += RefreshState;
            RefreshState();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _app.State.Changed -= RefreshState;
                _lastCover.Image?.Dispose();
            }
            base.Dispose(disposing);
        }

        private void RefreshState()
        {
            if (IsDisposed) return;
            _refreshing = true;
            try
            {
                var settings = _app.State.Settings;
                _status.Text = settings.Paused ? "● En pausa" : "● Activo";
                _status.ForeColor = settings.Paused ? Theme.Warning : Theme.Success;
                _pauseLink.Text = settings.Paused ? "Reanudar" : "Pausar";

                bool registered = !_app.IsPortable && SafeIsRegistered();
                bool blocked = registered && SafeIsBlocked();
                _startup.Checked = registered && !blocked;
                _startup.Enabled = !_app.IsPortable;
                _startupHint.Visible = blocked;
                _autoLaunch.Checked = settings.AutoLaunch;
                _notice.Checked = settings.ShowLaunchNotice;
                _tray.Checked = settings.TrayIcon;

                var library = _app.State.Library;
                var record = library.LastGameId != null ? library.Find(library.LastGameId) : null;
                _lastCover.Image?.Dispose();
                _lastCover.Image = record != null ? CoverCache.Load(record.Id) : null;
                _lastCover.Visible = _lastCover.Image != null;
                _lastGame.Text = "Último disco: " + (record?.DisplayName ?? "ninguno todavía");
                _lastPlatform.Text = "Plataforma: " + (record?.LastPlatform is LaunchKind kind ? LaunchKinds.DisplayName(kind) : "—")
                    + (record?.LastPlayedUtc is DateTime played ? " · " + played.ToLocalTime().ToString("d MMM, HH:mm", CultureInfo.GetCultureInfo("es-ES")) : string.Empty);
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void ApplySetting(Action change)
        {
            if (_refreshing) return;
            change();
            _app.State.SaveSettings();
        }

        private void MinimizeToBackground()
        {
            if (_app.State.Settings.TrayIcon) Close();
            else WindowState = FormWindowState.Minimized;
        }

        private static bool SafeIsRegistered()
        {
            try
            {
                return StartupRegistration.IsRegistered();
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static bool SafeIsBlocked()
        {
            try
            {
                return StartupRegistration.IsDisabledBySystem();
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        internal static void OpenUrl(string url)
        {
            try
            {
                using (Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }))
                {
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
            }
        }
    }
}
