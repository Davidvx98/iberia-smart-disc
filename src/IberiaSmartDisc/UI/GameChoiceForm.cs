using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using IberiaSmartDisc.Core.Manifest;
using IberiaSmartDisc.Core.Platforms;
using IberiaSmartDisc.Core.Platforms.Local;
using IberiaSmartDisc.Core.Resolution;

namespace IberiaSmartDisc.UI
{
    internal enum ChoiceMode
    {
        /// <summary>El juego está en varias plataformas.</summary>
        Multiple,

        /// <summary>No se encontró ninguna instalación.</summary>
        NotInstalled,

        /// <summary>La instalación guardada ya no existe.</summary>
        Broken,

        /// <summary>Cambiar la instalación desde la configuración.</summary>
        Configure,
    }

    internal sealed class ChoiceResult
    {
        public LaunchCandidate? Candidate { get; set; }

        public bool AlwaysUse { get; set; }

        public InstallAction? Install { get; set; }

        public bool OpenSettings { get; set; }
    }

    internal sealed class GameChoiceForm : Form
    {
        private readonly Func<CancellationToken, Task<IReadOnlyList<LocalSearchHit>>> _search;
        private readonly FlowLayoutPanel _options;
        private readonly Label _empty;
        private readonly Label _searchStatus;
        private readonly ThemedButton _searchButton;
        private readonly ThemedButton _accept;
        private readonly ToggleSwitch _alwaysUse;
        private readonly Image? _cover;
        private readonly int _optionWidth = Theme.S(500);
        private CancellationTokenSource? _searchCancellation;
        private OptionCard? _selected;

        public GameChoiceForm(
            DiscManifest manifest,
            ResolutionResult resolution,
            ChoiceMode mode,
            bool alwaysUseDefault,
            Image? cover,
            Func<CancellationToken, Task<IReadOnlyList<LocalSearchHit>>> search)
        {
            _search = search;
            _cover = cover;
            Theme.Prepare(this);
            Text = AppInfo.Name;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = mode != ChoiceMode.Configure;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var layout = Theme.Column(24);

            // Cabecera: carátula, nombre y mensaje.
            var header = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = Theme.Pad(0, 0, 0, 12), BackColor = Color.Transparent };
            header.Controls.Add(new PictureBox
            {
                Image = cover ?? Theme.Logo,
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = Theme.S(64, 84),
                Margin = Theme.Pad(0, 0, 16, 0),
                BackColor = Color.Transparent,
            }, 0, 0);
            var titles = Theme.Column();
            string heading = mode switch
            {
                ChoiceMode.Multiple => manifest.DisplayName + " encontrado",
                ChoiceMode.Configure => manifest.DisplayName,
                _ => manifest.DisplayName + " detectado",
            };
            string message = mode switch
            {
                ChoiceMode.Multiple => "¿Con qué instalación quieres abrirlo?",
                ChoiceMode.NotInstalled => "No encontramos el juego instalado en este PC.",
                ChoiceMode.Broken => (resolution.PreferredMissingMessage ?? "La instalación configurada ya no existe.") + " Elige otra opción.",
                _ => "Elige con qué instalación se abrirá este juego al insertar el disco.",
            };
            titles.Controls.Add(Theme.Label(heading, Theme.Title, Theme.Text, _optionWidth - Theme.S(80)));
            titles.Controls.Add(Theme.Label(message, Theme.Body, mode == ChoiceMode.Broken ? Theme.Warning : Theme.TextMuted, _optionWidth - Theme.S(80)));
            header.Controls.Add(titles, 1, 0);
            layout.Controls.Add(header);

            // Opciones encontradas.
            _options = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MaximumSize = new Size(_optionWidth + Theme.S(24), Theme.S(300)),
                AutoScroll = true,
                Margin = Theme.Pad(0, 0, 0, 4),
                BackColor = Color.Transparent,
            };
            layout.Controls.Add(_options);
            _empty = Theme.Label("Todavía no hay ninguna instalación para elegir.", Theme.Body, Theme.TextMuted, _optionWidth);
            layout.Controls.Add(_empty);
            foreach (var candidate in resolution.Candidates) AddOption(candidate, select: false);

            _searchStatus = Theme.Label(string.Empty, Theme.Small, Theme.TextMuted, _optionWidth);
            _searchStatus.Visible = false;
            layout.Controls.Add(_searchStatus);

            // Acciones: instalar, buscar, elegir ejecutable, configuración.
            var actions = Theme.Row();
            actions.MaximumSize = new Size(_optionWidth + Theme.S(24), 0);
            foreach (var install in resolution.InstallActions)
            {
                var button = new ThemedButton(install.Label) { Margin = Theme.Pad(0, 0, 8, 8) };
                button.Click += (s, e) => ChooseInstall(install);
                actions.Controls.Add(button);
            }
            _searchButton = new ThemedButton("Buscar automáticamente") { Margin = Theme.Pad(0, 0, 8, 8) };
            _searchButton.Click += async (s, e) => await ToggleSearchAsync();
            actions.Controls.Add(_searchButton);
            var browse = new ThemedButton("Seleccionar ejecutable…") { Margin = Theme.Pad(0, 0, 8, 8) };
            browse.Click += (s, e) => BrowseExecutable();
            actions.Controls.Add(browse);
            if (mode != ChoiceMode.Configure)
            {
                var settings = new ThemedButton("Configurar plataformas") { Margin = Theme.Pad(0, 0, 8, 8) };
                settings.Click += (s, e) =>
                {
                    Result.OpenSettings = true;
                    DialogResult = DialogResult.Cancel;
                };
                actions.Controls.Add(settings);
            }
            layout.Controls.Add(actions);

            _alwaysUse = new ToggleSwitch("Usar siempre esta opción", alwaysUseDefault) { Margin = Theme.Pad(0, 8, 0, 12) };
            layout.Controls.Add(_alwaysUse);

            var footer = Theme.Row();
            _accept = new ThemedButton(mode == ChoiceMode.Configure ? "Guardar" : "Jugar", ButtonKind.Primary);
            _accept.Click += (s, e) => Accept();
            var cancel = new ThemedButton("Cancelar") { DialogResult = DialogResult.Cancel };
            footer.Controls.Add(_accept);
            footer.Controls.Add(cancel);
            layout.Controls.Add(footer);

            Controls.Add(layout);
            AcceptButton = _accept;
            CancelButton = cancel;

            var first = _options.Controls.OfType<OptionCard>().FirstOrDefault();
            if (first != null) Select(first);
            UpdateState();
        }

        public ChoiceResult Result { get; } = new ChoiceResult();

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            (_selected ?? (Control)_searchButton).Focus();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _searchCancellation?.Cancel();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _searchCancellation?.Dispose();
                _cover?.Dispose();
            }
            base.Dispose(disposing);
        }

        private OptionCard AddOption(LaunchCandidate candidate, bool select)
        {
            var existing = _options.Controls.OfType<OptionCard>()
                .FirstOrDefault(o => o.Value is LaunchCandidate c && string.Equals(c.Key, candidate.Key, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (select) Select(existing);
                return existing;
            }
            var card = new OptionCard(candidate.DisplayTitle, candidate.Detail, candidate) { Width = _optionWidth };
            card.Picked += (s, e) => Select((OptionCard)s!);
            card.DoubleClick += (s, e) => Accept();
            _options.Controls.Add(card);
            if (select) Select(card);
            UpdateState();
            return card;
        }

        private void Select(OptionCard card)
        {
            foreach (var option in _options.Controls.OfType<OptionCard>()) option.Selected = option == card;
            _selected = card;
            _options.ScrollControlIntoView(card);
            UpdateState();
        }

        private void UpdateState()
        {
            if (_accept == null) return;
            bool any = _options.Controls.Count > 0;
            _empty.Visible = !any;
            _options.Visible = any;
            _accept.Enabled = _selected != null;
        }

        private void Accept()
        {
            if (_selected?.Value is not LaunchCandidate candidate) return;
            Result.Candidate = candidate;
            Result.AlwaysUse = _alwaysUse.Checked;
            DialogResult = DialogResult.OK;
        }

        private void ChooseInstall(InstallAction install)
        {
            // Si es un instalador del disco, el coordinador pide la confirmación informada.
            Result.Install = install;
            DialogResult = DialogResult.OK;
        }

        private void BrowseExecutable()
        {
            using (var dialog = new OpenFileDialog
            {
                Title = "Selecciona el ejecutable del juego",
                Filter = "Programas (*.exe)|*.exe",
                CheckFileExists = true,
                DereferenceLinks = true,
                RestoreDirectory = true,
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (!string.Equals(Path.GetExtension(dialog.FileName), ".exe", StringComparison.OrdinalIgnoreCase))
                {
                    ThemedDialog.Error(this, "Archivo no válido", "Elige el archivo .exe del juego.");
                    return;
                }
                AddOption(GameResolver.CreateLocalCandidate(dialog.FileName, null), select: true);
            }
        }

        private async Task ToggleSearchAsync()
        {
            if (_searchCancellation != null)
            {
                _searchCancellation.Cancel();
                return;
            }

            _searchCancellation = new CancellationTokenSource();
            _searchButton.Text = "Detener búsqueda";
            _searchStatus.Text = "Buscando en tus carpetas de juegos…";
            _searchStatus.Visible = true;
            try
            {
                var hits = await _search(_searchCancellation.Token);
                if (IsDisposed) return;
                foreach (var hit in hits)
                {
                    var candidate = GameResolver.CreateLocalCandidate(hit.ExecutablePath, null);
                    AddOption(new LaunchCandidate(candidate.Kind, candidate.Key, "Encontrado · " + Path.GetFileName(hit.ExecutablePath), hit.ExecutablePath, candidate.Target), select: false);
                }
                // No se preselecciona nada encontrado: el usuario debe revisar la ruta y elegir.
                _searchStatus.Text = hits.Count > 0
                    ? "Encontrado: revisa la ruta antes de jugar."
                    : "No lo encontramos. Prueba con «Seleccionar ejecutable…» o añade tu carpeta de juegos en Configuración.";
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed) _searchStatus.Text = "Búsqueda detenida.";
            }
            finally
            {
                _searchCancellation?.Dispose();
                _searchCancellation = null;
                if (!IsDisposed) _searchButton.Text = "Buscar automáticamente";
            }
        }
    }
}
