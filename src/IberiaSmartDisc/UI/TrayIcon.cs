using System;
using System.Drawing;
using System.Windows.Forms;
using IberiaSmartDisc.Hosting;

namespace IberiaSmartDisc.UI
{
    /// <summary>Icono opcional en la bandeja del sistema (se puede ocultar del todo).</summary>
    internal sealed class TrayIcon : IDisposable
    {
        private readonly IAppController _app;
        private readonly NotifyIcon _icon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _pause;
        private readonly Icon _image;

        public TrayIcon(IAppController app)
        {
            _app = app;
            _menu = new ContextMenuStrip
            {
                Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()),
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                Font = Theme.Body,
                ShowImageMargin = false,
                ShowCheckMargin = true,
            };
            var open = new ToolStripMenuItem("Abrir", null, (s, e) => _app.ShowMainWindow()) { Font = Theme.BodyBold };
            _pause = new ToolStripMenuItem("Pausar detección", null, (s, e) => _app.SetPaused(!_app.State.Settings.Paused));
            _menu.Items.Add(open);
            _menu.Items.Add(_pause);
            _menu.Items.Add(new ToolStripMenuItem("Configuración", null, (s, e) => _app.ShowSettings()));
            _menu.Items.Add(new ToolStripSeparator());
            if (!_app.IsPortable) _menu.Items.Add(new ToolStripMenuItem("Desinstalar…", null, (s, e) => _app.RequestUninstall(null)));
            _menu.Items.Add(new ToolStripMenuItem("Salir", null, (s, e) => _app.ExitApplication()));
            foreach (ToolStripItem item in _menu.Items) item.ForeColor = Theme.Text;

            _image = Theme.TrayIcon;
            _icon = new NotifyIcon { Icon = _image, ContextMenuStrip = _menu, Visible = true };
            _icon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) _app.ShowMainWindow();
            };
            Refresh();
            _app.State.Changed += Refresh;
        }

        public void Dispose()
        {
            _app.State.Changed -= Refresh;
            _icon.Visible = false;
            _icon.Dispose();
            _menu.Dispose();
            _image.Dispose();
        }

        private void Refresh()
        {
            bool paused = _app.State.Settings.Paused;
            _pause.Checked = paused;
            _icon.Text = AppInfo.Name + " · " + AppInfo.DisplayVersion + (paused ? " (en pausa)" : string.Empty);
        }

        private sealed class DarkMenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Theme.Surface;

            public override Color MenuBorder => Theme.Border;

            public override Color MenuItemBorder => Theme.Accent;

            public override Color MenuItemSelected => Theme.Selection;

            public override Color MenuItemSelectedGradientBegin => Theme.Selection;

            public override Color MenuItemSelectedGradientEnd => Theme.Selection;

            public override Color ImageMarginGradientBegin => Theme.Surface;

            public override Color ImageMarginGradientMiddle => Theme.Surface;

            public override Color ImageMarginGradientEnd => Theme.Surface;

            public override Color SeparatorDark => Theme.Border;

            public override Color SeparatorLight => Theme.Border;

            public override Color CheckBackground => Theme.Selection;

            public override Color CheckSelectedBackground => Theme.Selection;

            public override Color CheckPressedBackground => Theme.Selection;
        }
    }
}
