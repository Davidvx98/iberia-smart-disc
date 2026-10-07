using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace IberiaSmartDisc.UI
{
    internal enum ButtonKind
    {
        Primary,
        Secondary,
        Danger,
    }

    internal sealed class ThemedButton : Button
    {
        public ThemedButton(string text, ButtonKind kind = ButtonKind.Secondary)
        {
            Text = text;
            Kind = kind;
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = Theme.Pad(12, 4, 12, 4);
            MinimumSize = new Size(Theme.S(96), Theme.S(34));
            Margin = Theme.Pad(0, 0, 8, 0);
            Cursor = Cursors.Hand;
            Font = kind == ButtonKind.Primary ? Theme.BodyBold : Theme.Body;
            ApplyKind();
        }

        public ButtonKind Kind { get; private set; }

        public void SetKind(ButtonKind kind)
        {
            Kind = kind;
            ApplyKind();
        }

        private void ApplyKind()
        {
            switch (Kind)
            {
                case ButtonKind.Primary:
                    BackColor = Theme.Accent;
                    ForeColor = Theme.OnAccent;
                    FlatAppearance.BorderSize = 0;
                    FlatAppearance.MouseOverBackColor = Theme.AccentHover;
                    FlatAppearance.MouseDownBackColor = Theme.AccentPressed;
                    break;
                case ButtonKind.Danger:
                    BackColor = Theme.SurfaceRaised;
                    ForeColor = Theme.Danger;
                    FlatAppearance.BorderSize = 1;
                    FlatAppearance.BorderColor = Theme.Danger;
                    FlatAppearance.MouseOverBackColor = Theme.Selection;
                    FlatAppearance.MouseDownBackColor = Theme.Surface;
                    break;
                default:
                    BackColor = Theme.SurfaceRaised;
                    ForeColor = Theme.Text;
                    FlatAppearance.BorderSize = 1;
                    FlatAppearance.BorderColor = Theme.Border;
                    FlatAppearance.MouseOverBackColor = Theme.Selection;
                    FlatAppearance.MouseDownBackColor = Theme.Surface;
                    break;
            }
        }
    }

    /// <summary>Interruptor accesible (rol «casilla»), más claro que un CheckBox en tema oscuro.</summary>
    internal sealed class ToggleSwitch : Control
    {
        private bool _checked;

        public ToggleSwitch(string text, bool isChecked = false)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            Text = text;
            _checked = isChecked;
            TabStop = true;
            Cursor = Cursors.Hand;
            Margin = Theme.Pad(0, 4, 0, 4);
            AccessibleRole = AccessibleRole.CheckButton;
            UpdateSize();
        }

        public event EventHandler? CheckedChanged;

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var text = TextRenderer.MeasureText(Text, Font);
            return new Size(Theme.S(40) + Theme.S(10) + text.Width + Theme.S(4), Math.Max(Theme.S(26), text.Height + Theme.S(6)));
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            UpdateSize();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            UpdateSize();
        }

        protected override void OnClick(EventArgs e)
        {
            Focus();
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                Checked = !Checked;
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int trackWidth = Theme.S(40);
            int trackHeight = Theme.S(22);
            var track = new Rectangle(1, (Height - trackHeight) / 2, trackWidth - 2, trackHeight - 1);

            Color trackColor = !Enabled ? Theme.Surface : _checked ? Theme.Accent : Theme.SurfaceRaised;
            using (var path = Theme.RoundedRectangle(track, trackHeight / 2))
            using (var brush = new SolidBrush(trackColor))
            using (var pen = new Pen(Focused ? Theme.AccentHover : Theme.Border, Focused ? 2f : 1f))
            {
                g.FillPath(brush, path);
                if (!_checked || Focused) g.DrawPath(pen, path);
            }

            int knob = trackHeight - Theme.S(8);
            int knobX = _checked ? track.Right - knob - Theme.S(4) : track.X + Theme.S(4);
            using (var brush = new SolidBrush(_checked ? Theme.OnAccent : Theme.TextMuted))
            {
                g.FillEllipse(brush, knobX, track.Y + (track.Height - knob) / 2 + 1, knob, knob);
            }

            var textBounds = new Rectangle(trackWidth + Theme.S(10), 0, Width - trackWidth - Theme.S(10), Height);
            TextRenderer.DrawText(g, Text, Font, textBounds, Enabled ? Theme.Text : Theme.TextMuted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new ToggleAccessibleObject(this);

        private void UpdateSize() => Size = GetPreferredSize(Size.Empty);

        private sealed class ToggleAccessibleObject : ControlAccessibleObject
        {
            private readonly ToggleSwitch _owner;

            public ToggleAccessibleObject(ToggleSwitch owner)
                : base(owner)
            {
                _owner = owner;
            }

            public override AccessibleRole Role => AccessibleRole.CheckButton;

            public override string DefaultAction => _owner.Checked ? "Desactivar" : "Activar";

            public override AccessibleStates State
            {
                get
                {
                    var state = base.State | AccessibleStates.Focusable;
                    if (_owner.Checked) state |= AccessibleStates.Checked;
                    if (_owner.Focused) state |= AccessibleStates.Focused;
                    return state;
                }
            }

            public override void DoDefaultAction() => _owner.Checked = !_owner.Checked;
        }
    }

    /// <summary>Opción seleccionable con título y detalle (p. ej. «Steam — E:\SteamLibrary\…»).</summary>
    internal sealed class OptionCard : Control
    {
        private bool _selected;

        public OptionCard(string title, string? detail, object? value)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Title = title;
            Detail = detail;
            Value = value;
            Text = detail == null ? title : title + ", " + detail;
            TabStop = true;
            Cursor = Cursors.Hand;
            Height = Theme.S(detail == null ? 44 : 58);
            Margin = Theme.Pad(0, 0, 0, 8);
            AccessibleRole = AccessibleRole.RadioButton;
            AccessibleName = Text;
        }

        public event EventHandler? Picked;

        public string Title { get; }

        public string? Detail { get; }

        public object? Value { get; }

        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value) return;
                _selected = value;
                Invalidate();
                AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            Focus();
            Picked?.Invoke(this, EventArgs.Empty);
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter && !Selected)
            {
                Picked?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(1, 1, Width - 3, Height - 3);
            using (var path = Theme.RoundedRectangle(bounds, Theme.S(6)))
            using (var fill = new SolidBrush(_selected ? Theme.SurfaceRaised : Theme.Surface))
            using (var border = new Pen(_selected || Focused ? Theme.Accent : Theme.Border, _selected ? 2f : 1f))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }

            int radio = Theme.S(16);
            var circle = new Rectangle(Theme.S(14), (Height - radio) / 2, radio, radio);
            using (var pen = new Pen(_selected ? Theme.Accent : Theme.TextMuted, 1.5f))
            {
                g.DrawEllipse(pen, circle);
            }
            if (_selected)
            {
                using (var brush = new SolidBrush(Theme.Accent))
                {
                    var dot = Rectangle.Inflate(circle, -Theme.S(4), -Theme.S(4));
                    g.FillEllipse(brush, dot);
                }
            }

            int textLeft = circle.Right + Theme.S(12);
            int textWidth = Width - textLeft - Theme.S(12);
            if (Detail == null)
            {
                TextRenderer.DrawText(g, Title, Theme.BodyBold, new Rectangle(textLeft, 0, textWidth, Height), Theme.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                return;
            }
            int lineHeight = TextRenderer.MeasureText("Ag", Theme.BodyBold).Height;
            int top = (Height - lineHeight * 2) / 2;
            TextRenderer.DrawText(g, Title, Theme.BodyBold, new Rectangle(textLeft, top, textWidth, lineHeight), Theme.Text,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, Detail, Theme.Small, new Rectangle(textLeft, top + lineHeight, textWidth, lineHeight), Theme.TextMuted,
                TextFormatFlags.PathEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new OptionAccessibleObject(this);

        private sealed class OptionAccessibleObject : ControlAccessibleObject
        {
            private readonly OptionCard _owner;

            public OptionAccessibleObject(OptionCard owner)
                : base(owner)
            {
                _owner = owner;
            }

            public override AccessibleRole Role => AccessibleRole.RadioButton;

            public override AccessibleStates State
            {
                get
                {
                    var state = base.State | AccessibleStates.Focusable;
                    if (_owner.Selected) state |= AccessibleStates.Checked;
                    return state;
                }
            }
        }
    }

    /// <summary>Lista con selección en cobre y barras oscuras.</summary>
    internal sealed class ThemedListBox : ListBox
    {
        public ThemedListBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = Theme.S(28);
            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
            BorderStyle = BorderStyle.FixedSingle;
            IntegralHeight = false;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.NativeMethods.UseDarkScrollbars(this);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var brush = new SolidBrush(selected ? Theme.Selection : Theme.Surface))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }
            var bounds = new Rectangle(e.Bounds.X + Theme.S(8), e.Bounds.Y, e.Bounds.Width - Theme.S(12), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, bounds, Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis | TextFormatFlags.NoPrefix);
            if ((e.State & DrawItemState.Focus) != 0 && selected)
            {
                using (var pen = new Pen(Theme.Accent))
                {
                    e.Graphics.DrawRectangle(pen, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
                }
            }
        }
    }
}
