using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;
using IberiaSmartDisc.Native;

namespace IberiaSmartDisc.UI
{
    /// <summary>
    /// Aviso en la esquina inferior derecha, como al meter un disco en una
    /// consola: carátula, nombre del juego y, si procede, cuenta atrás que se
    /// puede cancelar. No roba el foco (no saca al usuario de lo que esté haciendo).
    /// </summary>
    internal sealed class DiscToast : Form
    {
        private static DiscToast? _current;

        private readonly TaskCompletionSource<bool> _result = new TaskCompletionSource<bool>();
        private readonly Image? _cover;
        private readonly bool _ownsCover;
        private readonly int _seconds;
        private readonly int _timerSeconds;
        private readonly Label _status;
        private readonly CountdownBar? _bar;
        private readonly Timer? _timer;
        private DateTime _deadlineUtc;
        private bool _completed;

        private DiscToast(string caption, string title, string subtitle, Image? cover, string? primary, string? secondary, int seconds, int autoCloseSeconds)
        {
            Theme.Prepare(this);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Theme.Surface;
            Size = Theme.S(420, 156);
            Padding = Theme.Pad(14);
            Text = AppInfo.Name;
            AccessibleName = caption + ". " + title + ". " + subtitle;

            _cover = cover;
            _ownsCover = cover != null;
            _seconds = seconds;
            _timerSeconds = seconds > 0 ? seconds : autoCloseSeconds;

            var picture = new PictureBox
            {
                Image = cover ?? Theme.Logo,
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = Theme.S(96, 128),
                Location = new Point(Theme.S(14), Theme.S(14)),
                BackColor = Color.Transparent,
            };
            Controls.Add(picture);

            int left = picture.Right + Theme.S(14);
            int width = Width - left - Theme.S(14);
            var column = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Location = new Point(left, Theme.S(12)),
                Size = new Size(width, Height - Theme.S(24)),
                BackColor = Color.Transparent,
            };
            Controls.Add(column);

            column.Controls.Add(Theme.Label(caption, Theme.Small, Theme.Accent, width));
            var titleLabel = Theme.Label(title, Theme.Heading, Theme.Text, width);
            titleLabel.AutoEllipsis = true;
            column.Controls.Add(titleLabel);
            _status = Theme.Label(subtitle.Length > 240 ? subtitle.Substring(0, 240) + "…" : subtitle, Theme.Body, Theme.TextMuted, width);
            column.Controls.Add(_status);

            if (seconds > 0)
            {
                _bar = new CountdownBar { Size = new Size(width - Theme.S(4), Theme.S(4)), Margin = Theme.Pad(0, 2, 0, 8) };
                column.Controls.Add(_bar);
            }

            if (primary != null || secondary != null)
            {
                var buttons = Theme.Row();
                buttons.Margin = Theme.Pad(0, 4, 0, 0);
                if (primary != null)
                {
                    var play = new ThemedButton(primary, ButtonKind.Primary) { MinimumSize = Theme.S(88, 30) };
                    play.Click += (s, e) => Complete(true);
                    buttons.Controls.Add(play);
                }
                if (secondary != null)
                {
                    var cancel = new ThemedButton(secondary) { MinimumSize = Theme.S(88, 30) };
                    cancel.Click += (s, e) => Complete(false);
                    buttons.Controls.Add(cancel);
                }
                column.Controls.Add(buttons);
            }

            // Si el mensaje ocupa varias líneas, el aviso crece para que no se corten los botones.
            int needed = column.GetPreferredSize(new Size(width, 0)).Height + Theme.S(24);
            if (needed > Height)
            {
                Height = needed;
                column.Height = needed - Theme.S(24);
            }

            if (_timerSeconds > 0)
            {
                _timer = new Timer { Interval = 100 };
                _timer.Tick += (s, e) => Tick();
            }
        }

        public Task<bool> Result => _result.Task;

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOPMOST;
                return parameters;
            }
        }

        /// <summary>Cuenta atrás: devuelve true si llega a cero o se pulsa «Jugar ahora».</summary>
        public static DiscToast Countdown(string caption, string title, string subtitle, Image? cover, int seconds) =>
            Present(new DiscToast(caption, title, subtitle, cover, "Jugar ahora", "Cancelar", seconds, 0));

        /// <summary>Pregunta sin cuenta atrás (arranque del PC, ejecución automática desactivada).</summary>
        public static DiscToast Prompt(string caption, string title, string subtitle, Image? cover, string primary = "Jugar", string secondary = "Ahora no") =>
            Present(new DiscToast(caption, title, subtitle, cover, primary, secondary, 0, 0));

        /// <summary>Aviso informativo que se cierra solo.</summary>
        public static void Info(string caption, string title, string subtitle, Image? cover = null, int seconds = 6) =>
            Present(new DiscToast(caption, title, subtitle, cover, null, "Cerrar", 0, seconds));

        public static void CloseCurrent() => _current?.Complete(false);

        public void Cancel() => Complete(false);

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            NativeMethods.UseRoundedCorners(Handle);
            if (_timer != null)
            {
                _deadlineUtc = DateTime.UtcNow.AddSeconds(_timerSeconds);
                _timer.Start();
                Tick();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.Border))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Complete(false, close: false);
            if (_current == this) _current = null;
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer?.Dispose();
                if (_ownsCover) _cover?.Dispose();
            }
            base.Dispose(disposing);
        }

        private static DiscToast Present(DiscToast toast)
        {
            _current?.Complete(false);
            _current = toast;
            var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
            toast.Location = new Point(area.Right - toast.Width - Theme.S(16), area.Bottom - toast.Height - Theme.S(16));
            toast.Show();
            return toast;
        }

        private void Tick()
        {
            double remaining = (_deadlineUtc - DateTime.UtcNow).TotalSeconds;
            if (_seconds > 0)
            {
                _bar!.Fraction = Math.Max(0, remaining / _seconds);
                _status.Text = remaining > 0 ? "Empieza en " + Math.Ceiling(remaining) + " s…" : "Abriendo…";
                if (remaining <= 0) Complete(true);
            }
            else if (remaining <= 0)
            {
                Complete(false);
            }
        }

        private void Complete(bool accepted, bool close = true)
        {
            if (_completed) return;
            _completed = true;
            _timer?.Stop();
            _result.TrySetResult(accepted);
            if (close && !IsDisposed && IsHandleCreated) BeginInvoke(new Action(Close));
        }

        private sealed class CountdownBar : Control
        {
            private double _fraction = 1;

            public CountdownBar()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            public double Fraction
            {
                get => _fraction;
                set
                {
                    _fraction = value;
                    Invalidate();
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.None;
                using (var track = new SolidBrush(Theme.SurfaceRaised))
                using (var fill = new SolidBrush(Theme.Accent))
                {
                    e.Graphics.FillRectangle(track, ClientRectangle);
                    e.Graphics.FillRectangle(fill, 0, 0, (int)(Width * _fraction), Height);
                }
            }
        }
    }
}
