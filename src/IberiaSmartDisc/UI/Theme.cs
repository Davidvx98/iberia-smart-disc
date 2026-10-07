using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using IberiaSmartDisc.Native;

namespace IberiaSmartDisc.UI
{
    /// <summary>
    /// Identidad de Iberia Custom DVDs: tinta oscura, crema y cobre (los mismos
    /// tonos que la web). Todos los tamaños en píxeles pasan por <see cref="S(int)"/>
    /// para escalar con el DPI de Windows.
    /// </summary>
    internal static class Theme
    {
        public static readonly Color Background = Color.FromArgb(0x14, 0x11, 0x0F);
        public static readonly Color Surface = Color.FromArgb(0x1F, 0x1A, 0x16);
        public static readonly Color SurfaceRaised = Color.FromArgb(0x2A, 0x23, 0x1E);
        public static readonly Color Selection = Color.FromArgb(0x4A, 0x34, 0x26);
        public static readonly Color Border = Color.FromArgb(0x3D, 0x34, 0x2C);
        public static readonly Color Text = Color.FromArgb(0xF1, 0xEB, 0xE0);
        public static readonly Color TextMuted = Color.FromArgb(0xBF, 0xAE, 0x93);
        public static readonly Color Accent = Color.FromArgb(0xCF, 0x8F, 0x55);
        public static readonly Color AccentHover = Color.FromArgb(0xDB, 0xA8, 0x78);
        public static readonly Color AccentPressed = Color.FromArgb(0xB8, 0x73, 0x33);
        public static readonly Color OnAccent = Color.FromArgb(0x0D, 0x0B, 0x0A);
        public static readonly Color Success = Color.FromArgb(0x7D, 0xB0, 0x7F);
        public static readonly Color Warning = Color.FromArgb(0xD9, 0xA4, 0x41);
        public static readonly Color Danger = Color.FromArgb(0xE0, 0x79, 0x6B);

        private static float _scale = 1f;
        private static Icon? _icon;
        private static Image? _logo;

        public static readonly Font Body = new Font("Segoe UI", 9.75f);
        public static readonly Font BodyBold = new Font("Segoe UI Semibold", 9.75f);
        public static readonly Font Small = new Font("Segoe UI", 8.25f);
        public static readonly Font Heading = new Font("Segoe UI Semibold", 12f);
        public static readonly Font Title = new Font("Segoe UI Semibold", 16f);
        public static readonly Font Hero = new Font("Segoe UI", 20f, FontStyle.Bold);
        public static readonly Font Mono = new Font("Consolas", 9f);

        public static void Initialize()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero))
            {
                _scale = Math.Max(1f, g.DpiX / 96f);
            }
        }

        public static int S(int value) => (int)Math.Round(value * _scale);

        public static Size S(int width, int height) => new Size(S(width), S(height));

        public static Padding Pad(int all) => new Padding(S(all));

        public static Padding Pad(int left, int top, int right, int bottom) => new Padding(S(left), S(top), S(right), S(bottom));

        public static Icon AppIcon => _icon ??= LoadIcon(32);

        public static Icon TrayIcon => LoadIcon(SystemInformation.SmallIconSize.Width);

        public static Image Logo => _logo ??= LoadLogo();

        /// <summary>Aplica el tema a una ventana: colores, fuente, icono y barra de título oscura.</summary>
        public static void Prepare(Form form)
        {
            form.AutoScaleMode = AutoScaleMode.None;
            form.BackColor = Background;
            form.ForeColor = Text;
            form.Font = Body;
            form.Icon = AppIcon;
            form.HandleCreated += (sender, args) => NativeMethods.UseDarkTitleBar(((Form)sender!).Handle);
        }

        public static Label Label(string text, Font? font = null, Color? color = null, int maxWidth = 0)
        {
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                Font = font ?? Body,
                ForeColor = color ?? Text,
                BackColor = Color.Transparent,
                Margin = Pad(0, 0, 0, 4),
                UseMnemonic = false,
            };
            if (maxWidth > 0) label.MaximumSize = new Size(maxWidth, 0);
            return label;
        }

        public static Label SectionTitle(string text)
        {
            var label = Label(text, Heading, Text);
            label.Margin = Pad(0, 14, 0, 6);
            return label;
        }

        public static LinkLabel Link(string text, Font? font = null)
        {
            var link = new LinkLabel
            {
                Text = text,
                AutoSize = true,
                Font = font ?? Body,
                LinkColor = Accent,
                ActiveLinkColor = AccentHover,
                VisitedLinkColor = Accent,
                LinkBehavior = LinkBehavior.HoverUnderline,
                BackColor = Color.Transparent,
                Margin = Pad(0, 0, 0, 4),
                UseMnemonic = false,
            };
            return link;
        }

        public static FlowLayoutPanel Row(params Control[] controls)
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = Pad(0, 4, 0, 4),
                BackColor = Color.Transparent,
            };
            row.Controls.AddRange(controls);
            return row;
        }

        public static TableLayoutPanel Column(int padding = 0)
        {
            return new TableLayoutPanel
            {
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = Pad(padding),
                Margin = Padding.Empty,
                BackColor = Color.Transparent,
            };
        }

        public static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Max(1, radius * 2);
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Icon LoadIcon(int size)
        {
            using (var stream = typeof(Theme).Assembly.GetManifestResourceStream("IberiaSmartDisc.app.ico"))
            {
                if (stream == null) return SystemIcons.Application;
                return new Icon(stream, size, size);
            }
        }

        private static Image LoadLogo()
        {
            using (var stream = typeof(Theme).Assembly.GetManifestResourceStream("IberiaSmartDisc.logo.png"))
            {
                if (stream == null) return new Bitmap(1, 1);
                using (var copy = new MemoryStream())
                {
                    stream.CopyTo(copy);
                    copy.Position = 0;
                    using (var image = Image.FromStream(copy))
                    {
                        return new Bitmap(image);
                    }
                }
            }
        }
    }
}
