using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace IberiaSmartDisc.UI
{
    /// <summary>Sustituto de MessageBox con el tema del programa.</summary>
    internal static class ThemedDialog
    {
        public static void Info(IWin32Window? owner, string title, string message) =>
            Show(owner, title, message, "Aceptar", null, ButtonKind.Primary);

        public static void Error(IWin32Window? owner, string title, string message) =>
            Show(owner, title, message, "Aceptar", null, ButtonKind.Primary, isError: true);

        public static bool Confirm(IWin32Window? owner, string title, string message, string confirm, string cancel = "Cancelar", bool danger = false) =>
            Show(owner, title, message, confirm, cancel, danger ? ButtonKind.Danger : ButtonKind.Primary);

        /// <summary>
        /// Confirmación de algo que puede ser peligroso (ejecutar un programa del
        /// disco). Cancelar es el botón por defecto: un Intro pulsado sin querer
        /// mientras se escribe en otra ventana no confirma. El botón de confirmar se
        /// activa pasado un momento, para que dé tiempo a leer.
        /// </summary>
        public static bool ConfirmRisky(string title, string intro, string highlight, IList<string> details, string warning, string confirm)
        {
            using (var form = new Form())
            {
                Theme.Prepare(form);
                form.Text = AppInfo.Name;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.ShowInTaskbar = true;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.TopMost = true;
                form.AutoSize = true;
                form.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                int width = Theme.S(460);

                var layout = Theme.Column(22);
                layout.MinimumSize = new Size(Theme.S(400), 0);
                layout.Controls.Add(Theme.Label(title, Theme.Heading, Theme.Text, width));
                layout.Controls.Add(Theme.Label(intro, Theme.Body, Theme.TextMuted, width));
                var file = Theme.Label(highlight, Theme.Title, Theme.Accent, width);
                file.RightToLeft = RightToLeft.No;
                file.Margin = Theme.Pad(0, 4, 0, 6);
                layout.Controls.Add(file);
                foreach (var line in details)
                {
                    var detail = Theme.Label(line, Theme.Small, Theme.TextMuted, width);
                    detail.RightToLeft = RightToLeft.No;
                    layout.Controls.Add(detail);
                }
                var caution = Theme.Label(warning, Theme.Body, Theme.Warning, width);
                caution.Margin = Theme.Pad(0, 12, 0, 18);
                layout.Controls.Add(caution);

                var cancel = new ThemedButton("Cancelar", ButtonKind.Primary) { DialogResult = DialogResult.Cancel };
                var accept = new ThemedButton(confirm, ButtonKind.Danger) { DialogResult = DialogResult.OK, Enabled = false };
                layout.Controls.Add(Theme.Row(cancel, accept));
                form.Controls.Add(layout);
                form.AcceptButton = cancel;
                form.CancelButton = cancel;

                var delay = new Timer { Interval = 1500 };
                delay.Tick += (s, e) =>
                {
                    delay.Stop();
                    accept.Enabled = true;
                };
                form.Shown += (s, e) =>
                {
                    cancel.Focus();
                    delay.Start();
                };
                try
                {
                    return form.ShowDialog() == DialogResult.OK;
                }
                finally
                {
                    delay.Dispose();
                }
            }
        }

        private static bool Show(IWin32Window? owner, string title, string message, string confirm, string? cancel, ButtonKind confirmKind, bool isError = false)
        {
            using (var form = new Form())
            {
                Theme.Prepare(form);
                form.Text = AppInfo.Name;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.ShowInTaskbar = owner == null;
                form.StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
                form.TopMost = owner == null;
                form.AutoSize = true;
                form.AutoSizeMode = AutoSizeMode.GrowAndShrink;

                var layout = Theme.Column(22);
                layout.MinimumSize = new Size(Theme.S(380), 0);
                layout.Controls.Add(Theme.Label(title, Theme.Heading, isError ? Theme.Danger : Theme.Text, Theme.S(440)));
                var body = Theme.Label(message, Theme.Body, Theme.TextMuted, Theme.S(440));
                body.Margin = Theme.Pad(0, 6, 0, 18);
                layout.Controls.Add(body);

                var buttons = Theme.Row();
                var ok = new ThemedButton(confirm, confirmKind) { DialogResult = DialogResult.OK };
                buttons.Controls.Add(ok);
                form.AcceptButton = ok;
                if (cancel != null)
                {
                    var no = new ThemedButton(cancel) { DialogResult = DialogResult.Cancel };
                    buttons.Controls.Add(no);
                    form.CancelButton = no;
                }
                else
                {
                    form.CancelButton = ok;
                }
                layout.Controls.Add(buttons);
                form.Controls.Add(layout);
                form.Shown += (s, e) => form.Activate();
                return form.ShowDialog(owner) == DialogResult.OK;
            }
        }
    }
}
