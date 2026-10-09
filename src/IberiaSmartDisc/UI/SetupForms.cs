using System;
using System.Drawing;
using System.Windows.Forms;
using IberiaSmartDisc.Logging;
using IberiaSmartDisc.Setup;

namespace IberiaSmartDisc.UI
{
    internal enum InstallMode
    {
        Install,
        Update,
    }

    /// <summary>Instalación en un paso: doble clic, dos interruptores y listo.</summary>
    internal sealed class InstallForm : Form
    {
        private readonly InstallMode _mode;
        private readonly AppLog _log;
        private readonly ToggleSwitch? _startup;
        private readonly ToggleSwitch? _shortcut;
        private readonly ThemedButton _accept;

        public InstallForm(InstallMode mode, Version? installedVersion, AppLog log)
        {
            _mode = mode;
            _log = log;
            Theme.Prepare(this);
            Text = AppInfo.Name + " · " + AppInfo.DisplayVersion;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            int width = Theme.S(420);

            var layout = Theme.Column(28);
            var header = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, BackColor = Color.Transparent, Margin = Theme.Pad(0, 0, 0, 16) };
            header.Controls.Add(new PictureBox { Image = Theme.Logo, SizeMode = PictureBoxSizeMode.Zoom, Size = Theme.S(72, 72), Margin = Theme.Pad(0, 0, 16, 0) }, 0, 0);
            var titles = Theme.Column();
            titles.Controls.Add(Theme.Label(mode == InstallMode.Install ? "Instalar Iberia Smart Disc" : "Actualizar Iberia Smart Disc", Theme.Title, Theme.Text, width - Theme.S(90)));
            titles.Controls.Add(Theme.Label("Versión " + AppInfo.DisplayVersion, Theme.BodyBold, Theme.Accent));
            header.Controls.Add(titles, 1, 0);
            layout.Controls.Add(header);

            string message = mode == InstallMode.Install
                ? "Se queda en segundo plano y, cuando metes un disco de Iberia Custom DVDs, abre el juego en tu PC. No necesita permisos de administrador ni conexión a Internet."
                : "Tienes instalada la versión " + (installedVersion?.ToString(3) ?? "anterior") + ". Se actualizará conservando tu configuración y tus juegos.";
            layout.Controls.Add(Theme.Label(message, Theme.Body, Theme.Text, width));

            if (mode == InstallMode.Install)
            {
                _startup = new ToggleSwitch("Iniciar con Windows (recomendado)", true) { Margin = Theme.Pad(0, 14, 0, 4) };
                _shortcut = new ToggleSwitch("Crear acceso en el menú Inicio", true);
                layout.Controls.Add(_startup);
                layout.Controls.Add(_shortcut);
                var where = Theme.Label("Se instala solo para tu usuario en " + AppPaths.InstallDirectory, Theme.Small, Theme.TextMuted, width);
                where.Margin = Theme.Pad(0, 10, 0, 0);
                layout.Controls.Add(where);
            }

            var buttons = Theme.Row();
            buttons.Margin = Theme.Pad(0, 20, 0, 0);
            _accept = new ThemedButton(mode == InstallMode.Install ? "Instalar" : "Actualizar", ButtonKind.Primary);
            _accept.Click += (s, e) => Run();
            var cancel = new ThemedButton("Cancelar") { DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(_accept);
            buttons.Controls.Add(cancel);
            layout.Controls.Add(buttons);

            Controls.Add(layout);
            AcceptButton = _accept;
            CancelButton = cancel;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
        }

        private void Run()
        {
            _accept.Enabled = false;
            UseWaitCursor = true;
            try
            {
                if (_mode == InstallMode.Install)
                {
                    Installer.Install(new InstallOptions { StartWithWindows = _startup!.Checked, StartMenuShortcut = _shortcut!.Checked }, _log);
                }
                else
                {
                    Installer.Update(_log);
                }
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                _log.Error("Falló la instalación", ex);
                UseWaitCursor = false;
                _accept.Enabled = true;
                ThemedDialog.Error(this, _mode == InstallMode.Install ? "No se pudo instalar" : "No se pudo actualizar", ex.Message);
            }
        }
    }

    /// <summary>Desinstalación: el usuario elige qué datos se borran además del programa.</summary>
    internal sealed class UninstallForm : Form
    {
        private readonly ToggleSwitch _settings;
        private readonly ToggleSwitch _games;
        private readonly ToggleSwitch _logs;

        public UninstallForm()
        {
            Theme.Prepare(this);
            Text = AppInfo.Name;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            int width = Theme.S(400);

            var layout = Theme.Column(28);
            layout.Controls.Add(Theme.Label("¿Desinstalar Iberia Smart Disc?", Theme.Title, Theme.Text, width));
            layout.Controls.Add(Theme.Label("Los discos no se ven afectados: si vuelves a instalarlo, seguirán funcionando.", Theme.Body, Theme.TextMuted, width));

            var startup = new ToggleSwitch("Eliminar inicio automático", true) { Enabled = false, Margin = Theme.Pad(0, 14, 0, 4) };
            _settings = new ToggleSwitch("Eliminar configuración", true);
            _games = new ToggleSwitch("Eliminar juegos recordados", true);
            _logs = new ToggleSwitch("Eliminar logs y caché", true);
            layout.Controls.Add(startup);
            layout.Controls.Add(_settings);
            layout.Controls.Add(_games);
            layout.Controls.Add(_logs);

            var buttons = Theme.Row();
            buttons.Margin = Theme.Pad(0, 20, 0, 0);
            var cancel = new ThemedButton("Cancelar", ButtonKind.Primary) { DialogResult = DialogResult.Cancel };
            var uninstall = new ThemedButton("Desinstalar", ButtonKind.Danger) { DialogResult = DialogResult.OK };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(uninstall);
            layout.Controls.Add(buttons);

            Controls.Add(layout);
            CancelButton = cancel;
        }

        public UninstallOptions Options => new UninstallOptions
        {
            RemoveSettings = _settings.Checked,
            RemoveGames = _games.Checked,
            RemoveLogsAndCache = _logs.Checked,
        };

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
        }
    }
}
