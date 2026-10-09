using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using IberiaSmartDisc.Core.Configuration;
using IberiaSmartDisc.Core.Json;
using IberiaSmartDisc.Core.Platforms;
using IberiaSmartDisc.Discs;
using IberiaSmartDisc.Hosting;

namespace IberiaSmartDisc.UI
{
    internal sealed class SettingsForm : Form
    {
        private readonly IAppController _app;
        private readonly Panel _content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = Theme.Pad(24, 8, 24, 8) };
        private readonly List<KeyValuePair<ThemedButton, Control>> _pages = new List<KeyValuePair<ThemedButton, Control>>();
        private readonly string? _initialGameId;
        private readonly bool _openGameDialog;

        private ListView _games = null!;
        private Label _gamesEmpty = null!;
        private ThemedButton _changeInstall = null!;
        private ThemedButton _resetChoice = null!;
        private ThemedButton _forget = null!;
        private ToggleSwitch _askMultiple = null!;
        private NumericUpDown _delay = null!;
        private ToggleSwitch _gogGalaxy = null!;
        private ToggleSwitch _polling = null!;
        private ThemedListBox _priority = null!;
        private ThemedListBox _folders = null!;
        private TextBox _diagnostics = null!;
        private TextBox _steamPath = null!;
        private ToggleSwitch _logging = null!;
        private bool _loading;

        public SettingsForm(IAppController app, string? gameId, bool openGameDialog)
        {
            _app = app;
            _initialGameId = gameId;
            _openGameDialog = openGameDialog;
            Theme.Prepare(this);
            Text = "Configuración · " + AppInfo.Name;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = Theme.S(760, 600);
            MinimumSize = Theme.S(640, 520);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Background };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var nav = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Padding = Theme.Pad(24, 18, 24, 6), BackColor = Theme.Background };
            root.Controls.Add(nav, 0, 0);
            root.Controls.Add(_content, 0, 1);

            AddPage(nav, "Mis juegos", BuildGamesPage());
            AddPage(nav, "Detección", BuildDetectionPage());
            AddPage(nav, "Plataformas", BuildPlatformsPage());
            AddPage(nav, "Avanzado", BuildAdvancedPage());

            var close = new ThemedButton("Cerrar", ButtonKind.Primary) { DialogResult = DialogResult.Cancel };
            close.Click += (s, e) => Close();
            var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = Theme.Pad(24, 8, 16, 16), BackColor = Theme.Background };
            footer.Controls.Add(close);
            root.Controls.Add(footer, 0, 2);
            CancelButton = close;

            Controls.Add(root);
            LoadValues();
            ShowPage(0);
            _app.State.Changed += OnStateChanged;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            if (_initialGameId != null)
            {
                SelectGame(_initialGameId);
                if (_openGameDialog) _app.ConfigureGame(_initialGameId, this);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _app.State.Changed -= OnStateChanged;
            base.Dispose(disposing);
        }

        public void SelectGame(string gameId)
        {
            ShowPage(0);
            foreach (ListViewItem item in _games.Items)
            {
                if ((string)item.Tag == gameId)
                {
                    item.Selected = true;
                    item.Focused = true;
                    item.EnsureVisible();
                }
            }
        }

        private void AddPage(FlowLayoutPanel nav, string title, Control page)
        {
            var button = new ThemedButton(title) { Margin = Theme.Pad(0, 0, 8, 0) };
            int index = _pages.Count;
            button.Click += (s, e) => ShowPage(index);
            nav.Controls.Add(button);
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            _content.Controls.Add(page);
            _pages.Add(new KeyValuePair<ThemedButton, Control>(button, page));
        }

        private void ShowPage(int index)
        {
            for (int i = 0; i < _pages.Count; i++)
            {
                _pages[i].Key.SetKind(i == index ? ButtonKind.Primary : ButtonKind.Secondary);
                _pages[i].Value.Visible = i == index;
            }
            if (index == 2) RefreshDiagnostics();
        }

        private void OnStateChanged()
        {
            if (!IsDisposed && !_loading) LoadGames();
        }

        // ── Mis juegos ────────────────────────────────────────────────────

        private Control BuildGamesPage()
        {
            var page = new TableLayoutPanel { ColumnCount = 1, RowCount = 4, BackColor = Theme.Background };
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            page.Controls.Add(Theme.Label("Los juegos de los discos que has insertado. Esta configuración se guarda en este PC, no en el disco.", Theme.Body, Theme.TextMuted, Theme.S(680)), 0, 0);
            _gamesEmpty = Theme.Label("Aún no has insertado ningún disco de Iberia Custom DVDs.", Theme.Body, Theme.Text);
            _gamesEmpty.Margin = Theme.Pad(0, 12, 0, 0);
            page.Controls.Add(_gamesEmpty, 0, 1);

            _games = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                OwnerDraw = true,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.None,
                Margin = Theme.Pad(0, 8, 0, 8),
                SmallImageList = new ImageList { ImageSize = new Size(1, Theme.S(30)) },
            };
            _games.Columns.Add("Juego", Theme.S(250));
            _games.Columns.Add("Se abre con", Theme.S(240));
            _games.Columns.Add("Último uso", Theme.S(150));
            _games.DrawColumnHeader += DrawHeader;
            _games.DrawItem += (s, e) => e.DrawDefault = false;
            _games.DrawSubItem += DrawGameCell;
            _games.SelectedIndexChanged += (s, e) => UpdateGameButtons();
            _games.DoubleClick += (s, e) => ChangeInstallation();
            _games.HandleCreated += (s, e) => Native.NativeMethods.UseDarkScrollbars(_games);
            page.Controls.Add(_games, 0, 2);

            _changeInstall = new ThemedButton("Cambiar instalación…");
            _changeInstall.Click += (s, e) => ChangeInstallation();
            _resetChoice = new ThemedButton("Volver a preguntar");
            _resetChoice.Click += (s, e) => WithSelectedGame(id =>
            {
                _app.State.Library.ClearPreference(id);
                _app.State.SaveLibrary();
            });
            _forget = new ThemedButton("Olvidar juego", ButtonKind.Danger);
            _forget.Click += (s, e) => WithSelectedGame(ForgetGame);
            page.Controls.Add(Theme.Row(_changeInstall, _resetChoice, _forget), 0, 3);
            return page;
        }

        private void LoadGames()
        {
            string? selected = _games.SelectedItems.Count > 0 ? (string)_games.SelectedItems[0].Tag : null;
            _games.BeginUpdate();
            _games.Items.Clear();
            foreach (var record in _app.State.Library.Games)
            {
                var item = new ListViewItem(record.DisplayName) { Tag = record.Id };
                item.SubItems.Add(OpensWith(record));
                var last = record.LastPlayedUtc ?? record.LastSeenUtc;
                item.SubItems.Add(last?.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.GetCultureInfo("es-ES")) ?? "—");
                _games.Items.Add(item);
                if (record.Id == selected) item.Selected = true;
            }
            _games.EndUpdate();
            _gamesEmpty.Visible = _games.Items.Count == 0;
            UpdateGameButtons();
        }

        private static string OpensWith(GameRecord record)
        {
            if (!record.AlwaysUse || record.PreferredKind == null) return "Automático (pregunta si hay varias)";
            if (record.PreferredKind == LaunchKind.Local && record.LocalExecutable != null)
            {
                return "Ejecutable local · " + Path.GetFileName(record.LocalExecutable);
            }
            return LaunchKinds.DisplayName(record.PreferredKind.Value);
        }

        private void UpdateGameButtons()
        {
            bool any = _games.SelectedItems.Count > 0;
            _changeInstall.Enabled = any;
            _resetChoice.Enabled = any;
            _forget.Enabled = any;
        }

        private void WithSelectedGame(Action<string> action)
        {
            if (_games.SelectedItems.Count == 0) return;
            action((string)_games.SelectedItems[0].Tag);
        }

        private void ChangeInstallation() => WithSelectedGame(id => _app.ConfigureGame(id, this));

        private void ForgetGame(string id)
        {
            var record = _app.State.Library.Find(id);
            if (record == null) return;
            if (!ThemedDialog.Confirm(this, "¿Olvidar " + record.DisplayName + "?",
                "Se borrarán la instalación elegida, el historial y la carátula guardada de este juego. El disco seguirá funcionando.",
                "Olvidar", danger: true))
            {
                return;
            }
            _app.State.Library.Forget(id);
            CoverCache.Delete(id);
            ManifestCache.Delete(id);
            _app.State.SaveLibrary();
        }

        private void DrawHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (var brush = new SolidBrush(Theme.SurfaceRaised))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }
            var bounds = new Rectangle(e.Bounds.X + Theme.S(8), e.Bounds.Y, e.Bounds.Width - Theme.S(8), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text, Theme.BodyBold, bounds, Theme.TextMuted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private void DrawGameCell(object? sender, DrawListViewSubItemEventArgs e)
        {
            bool selected = e.Item != null && e.Item.Selected;
            using (var brush = new SolidBrush(selected ? Theme.Selection : Theme.Surface))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }
            var bounds = new Rectangle(e.Bounds.X + Theme.S(8), e.Bounds.Y, e.Bounds.Width - Theme.S(12), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, e.SubItem?.Text, e.ColumnIndex == 0 ? Theme.BodyBold : Theme.Body, bounds,
                e.ColumnIndex == 0 ? Theme.Text : Theme.TextMuted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        // ── Detección ─────────────────────────────────────────────────────

        private Control BuildDetectionPage()
        {
            var scroll = new Panel { AutoScroll = true, BackColor = Theme.Background };
            var column = Theme.Column();
            int width = Theme.S(660);

            column.Controls.Add(Theme.SectionTitle("Al insertar un disco"));
            _askMultiple = new ToggleSwitch("Preguntar si el juego está en varias plataformas");
            _askMultiple.CheckedChanged += (s, e) => Save(settings => settings.AskWhenMultiple = _askMultiple.Checked);
            column.Controls.Add(_askMultiple);

            _delay = new NumericUpDown
            {
                Minimum = 0,
                Maximum = AppSettings.MaxLaunchDelaySeconds,
                Width = Theme.S(64),
                BackColor = Theme.SurfaceRaised,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = HorizontalAlignment.Center,
                AccessibleName = "Segundos de cuenta atrás",
            };
            _delay.ValueChanged += (s, e) => Save(settings => settings.LaunchDelaySeconds = (int)_delay.Value);
            var delayLabel = Theme.Label("Cuenta atrás antes de abrir el juego:", Theme.Body, Theme.Text);
            delayLabel.Margin = Theme.Pad(0, 6, 8, 0);
            var secondsLabel = Theme.Label("segundos (0 = sin espera)", Theme.Body, Theme.TextMuted);
            secondsLabel.Margin = Theme.Pad(8, 6, 0, 0);
            column.Controls.Add(Theme.Row(delayLabel, _delay, secondsLabel));
            column.Controls.Add(Theme.Label("Si el disco ya estaba dentro al encender el PC, siempre se pregunta antes de abrir nada.", Theme.Small, Theme.TextMuted, width));

            _gogGalaxy = new ToggleSwitch("Abrir los juegos de GOG con GOG Galaxy");
            _gogGalaxy.CheckedChanged += (s, e) => Save(settings => settings.GogUseGalaxy = _gogGalaxy.Checked);
            column.Controls.Add(_gogGalaxy);
            _polling = new ToggleSwitch("Modo compatibilidad: revisar los lectores cada 5 segundos");
            _polling.CheckedChanged += (s, e) =>
            {
                if (_loading) return;
                _app.SetCompatibilityPolling(_polling.Checked);
            };
            column.Controls.Add(_polling);
            column.Controls.Add(Theme.Label("Actívalo solo si tu lector no avisa a Windows al meter un disco (algunos lectores USB antiguos).", Theme.Small, Theme.TextMuted, width));

            column.Controls.Add(Theme.SectionTitle("Prioridad de plataformas"));
            column.Controls.Add(Theme.Label("Si el juego está en varias, la primera de la lista aparece marcada.", Theme.Small, Theme.TextMuted, width));
            _priority = new ThemedListBox { Size = Theme.S(280, 150) };
            var up = new ThemedButton("Subir") { Margin = Theme.Pad(0, 0, 0, 8) };
            up.Click += (s, e) => MovePriority(-1);
            var down = new ThemedButton("Bajar");
            down.Click += (s, e) => MovePriority(1);
            var moveColumn = Theme.Column();
            moveColumn.Margin = Theme.Pad(12, 0, 0, 0);
            moveColumn.Controls.Add(up);
            moveColumn.Controls.Add(down);
            column.Controls.Add(Theme.Row(_priority, moveColumn));

            column.Controls.Add(Theme.SectionTitle("Carpetas de juegos"));
            column.Controls.Add(Theme.Label("Dónde buscar juegos instalados a mano, bibliotecas de Steam en otros discos y juegos de GOG.", Theme.Small, Theme.TextMuted, width));
            _folders = new ThemedListBox { Size = Theme.S(460, 130) };
            var add = new ThemedButton("Añadir…") { Margin = Theme.Pad(0, 0, 0, 8) };
            add.Click += (s, e) => AddFolder();
            var remove = new ThemedButton("Quitar");
            remove.Click += (s, e) => RemoveFolder();
            var folderButtons = Theme.Column();
            folderButtons.Margin = Theme.Pad(12, 0, 0, 0);
            folderButtons.Controls.Add(add);
            folderButtons.Controls.Add(remove);
            column.Controls.Add(Theme.Row(_folders, folderButtons));

            scroll.Controls.Add(column);
            return scroll;
        }

        private void MovePriority(int direction)
        {
            int index = _priority.SelectedIndex;
            int target = index + direction;
            if (index < 0 || target < 0 || target >= _priority.Items.Count) return;
            var order = _app.State.Settings.PlatformPriority.ToList();
            var item = order[index];
            order.RemoveAt(index);
            order.Insert(target, item);
            _app.State.Settings.SetPriority(order);
            _app.State.SaveSettings();
            LoadPriority();
            _priority.SelectedIndex = target;
        }

        private void LoadPriority()
        {
            _priority.Items.Clear();
            int position = 1;
            foreach (var kind in _app.State.Settings.PlatformPriority)
            {
                _priority.Items.Add(position++ + ". " + LaunchKinds.DisplayName(kind));
            }
        }

        private void AddFolder()
        {
            using (var dialog = new FolderBrowserDialog { Description = "Elige una carpeta donde tengas juegos instalados", ShowNewFolderButton = false })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrEmpty(dialog.SelectedPath)) return;
                var paths = _app.State.Settings.SearchPaths;
                if (paths.Count >= AppSettings.MaxSearchPaths || paths.Contains(dialog.SelectedPath, StringComparer.OrdinalIgnoreCase)) return;
                paths.Add(dialog.SelectedPath);
                _app.State.SaveSettings();
                LoadFolders();
            }
        }

        private void RemoveFolder()
        {
            if (_folders.SelectedIndex < 0) return;
            _app.State.Settings.SearchPaths.RemoveAt(_folders.SelectedIndex);
            _app.State.SaveSettings();
            LoadFolders();
        }

        private void LoadFolders()
        {
            _folders.Items.Clear();
            foreach (var path in _app.State.Settings.SearchPaths) _folders.Items.Add(path);
        }

        // ── Plataformas ───────────────────────────────────────────────────

        private Control BuildPlatformsPage()
        {
            var page = new TableLayoutPanel { ColumnCount = 1, RowCount = 6, BackColor = Theme.Background };
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++) page.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            page.Controls.Add(Theme.Label("Lo que Iberia Smart Disc ha encontrado en este PC. Solo se consulta al insertar un disco.", Theme.Body, Theme.TextMuted, Theme.S(680)), 0, 0);
            _diagnostics = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Font = Theme.Mono,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.None,
                Margin = Theme.Pad(0, 8, 0, 8),
                WordWrap = false,
            };
            _diagnostics.HandleCreated += (s, e) => Native.NativeMethods.UseDarkScrollbars(_diagnostics);
            page.Controls.Add(_diagnostics, 0, 1);

            var refresh = new ThemedButton("Volver a detectar");
            refresh.Click += (s, e) => RefreshDiagnostics();
            var copy = new ThemedButton("Copiar informe");
            copy.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(_diagnostics.Text);
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                }
            };
            page.Controls.Add(Theme.Row(refresh, copy), 0, 2);

            page.Controls.Add(Theme.SectionTitle("Carpeta de Steam"), 0, 3);
            _steamPath = new TextBox { Width = Theme.S(380), BackColor = Theme.SurfaceRaised, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Margin = Theme.Pad(0, 4, 8, 0) };
            _steamPath.Leave += (s, e) => SaveSteamPath(_steamPath.Text);
            var browse = new ThemedButton("Examinar…");
            browse.Click += (s, e) =>
            {
                using (var dialog = new FolderBrowserDialog { Description = "Elige la carpeta donde está instalado Steam", ShowNewFolderButton = false })
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK) SaveSteamPath(dialog.SelectedPath);
                }
            };
            var automatic = new ThemedButton("Automática");
            automatic.Click += (s, e) => SaveSteamPath(string.Empty);
            page.Controls.Add(Theme.Row(_steamPath, browse, automatic), 0, 4);
            page.Controls.Add(Theme.Label("Déjalo vacío para que se detecte sola. Úsalo solo si Steam está en una ubicación poco habitual.", Theme.Small, Theme.TextMuted, Theme.S(680)), 0, 5);
            return page;
        }

        private void SaveSteamPath(string value)
        {
            string? path = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (string.Equals(path, _app.State.Settings.SteamPath, StringComparison.OrdinalIgnoreCase)) return;
            _app.State.Settings.SteamPath = path;
            _app.State.SaveSettings();
            _steamPath.Text = path ?? string.Empty;
            RefreshDiagnostics();
        }

        private void RefreshDiagnostics()
        {
            var lines = new List<string>
            {
                AppInfo.Name + " " + AppInfo.DisplayVersion + (_app.IsPortable ? " (portátil)" : string.Empty),
                "Windows " + Environment.OSVersion.Version + (Environment.Is64BitOperatingSystem ? " 64 bits" : " 32 bits"),
                "Lectores ópticos: " + string.Join(", ", OpticalDriveMonitor.CurrentOpticalRoots().DefaultIfEmpty("ninguno")),
                "Detección: " + (_app.State.Settings.Paused ? "en pausa" : "activa") + (_app.State.Settings.CompatibilityPolling ? " · modo compatibilidad" : string.Empty),
                string.Empty,
            };
            try
            {
                lines.AddRange(_app.DescribePlatforms());
            }
            catch (Exception ex)
            {
                lines.Add("Error al revisar las plataformas: " + ex.Message);
            }
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string text = string.Join(Environment.NewLine, lines);
            if (!string.IsNullOrEmpty(profile)) text = ReplaceIgnoreCase(text, profile, "%USERPROFILE%");
            _diagnostics.Text = text;
        }

        // ── Avanzado ──────────────────────────────────────────────────────

        private Control BuildAdvancedPage()
        {
            var scroll = new Panel { AutoScroll = true, BackColor = Theme.Background };
            var column = Theme.Column();
            int width = Theme.S(660);

            column.Controls.Add(Theme.SectionTitle("Registro de actividad"));
            _logging = new ToggleSwitch("Guardar un registro (logs) para resolver problemas");
            _logging.CheckedChanged += (s, e) =>
            {
                if (_loading) return;
                _app.State.Settings.Logging = _logging.Checked;
                _app.State.Log.Enabled = _logging.Checked;
                _app.State.SaveSettings();
            };
            column.Controls.Add(_logging);
            var openLogs = new ThemedButton("Abrir carpeta de logs");
            openLogs.Click += (s, e) => OpenFolder(AppPaths.LogsDirectory);
            var clearLogs = new ThemedButton("Borrar logs");
            clearLogs.Click += (s, e) =>
            {
                _app.State.Log.DeleteAll();
                ThemedDialog.Info(this, "Logs borrados", "Se han borrado los registros de actividad.");
            };
            column.Controls.Add(Theme.Row(openLogs, clearLogs));
            column.Controls.Add(Theme.Label("Los logs no salen de tu PC y no guardan el nombre de tu cuenta de Windows.", Theme.Small, Theme.TextMuted, width));

            column.Controls.Add(Theme.SectionTitle("Copia de seguridad"));
            column.Controls.Add(Theme.Label("Guarda tu configuración y tus juegos en un archivo para restaurarlos o llevarlos a otro PC.", Theme.Small, Theme.TextMuted, width));
            var export = new ThemedButton("Exportar…");
            export.Click += (s, e) => ExportConfiguration();
            var import = new ThemedButton("Importar…");
            import.Click += (s, e) => ImportConfiguration();
            column.Controls.Add(Theme.Row(export, import));

            column.Controls.Add(Theme.SectionTitle("Probar sin grabar un disco"));
            column.Controls.Add(Theme.Label("Elige una carpeta con un iberia-disc.json y se tratará como si hubieras metido ese disco. Siempre pregunta antes de abrir nada.", Theme.Small, Theme.TextMuted, width));
            var test = new ThemedButton("Probar con una carpeta…");
            test.Click += (s, e) =>
            {
                using (var dialog = new FolderBrowserDialog { Description = "Carpeta con iberia-disc.json", ShowNewFolderButton = false })
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK) _app.SimulateDisc(dialog.SelectedPath);
                }
            };
            column.Controls.Add(Theme.Row(test));

            column.Controls.Add(Theme.SectionTitle("Acerca de"));
            column.Controls.Add(Theme.Label(AppInfo.Name + " " + AppInfo.DisplayVersion + " · " + AppInfo.Publisher, Theme.Body, Theme.Text));
            column.Controls.Add(Theme.Label("Sin cuentas, sin publicidad y sin enviar datos: todo se queda en este PC.", Theme.Small, Theme.TextMuted, width));
            var web = Theme.Link(AppInfo.WebsiteLabel);
            web.LinkClicked += (s, e) => MainForm.OpenUrl(AppInfo.WebsiteUrl);
            var source = Theme.Link("Código fuente");
            source.LinkClicked += (s, e) => MainForm.OpenUrl(AppInfo.RepositoryUrl);
            var data = Theme.Link("Abrir carpeta de datos");
            data.LinkClicked += (s, e) => OpenFolder(AppPaths.DataDirectory);
            web.Margin = source.Margin = data.Margin = Theme.Pad(0, 0, 18, 0);
            column.Controls.Add(Theme.Row(web, source, data));

            scroll.Controls.Add(column);
            return scroll;
        }

        private void ExportConfiguration()
        {
            using (var dialog = new SaveFileDialog
            {
                Title = "Exportar configuración",
                Filter = "Copia de Iberia Smart Disc (*.json)|*.json",
                FileName = "iberia-smart-disc-" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json",
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var json = ConfigBackup.Export(_app.State.Settings, _app.State.Library, DateTime.UtcNow);
                    File.WriteAllText(dialog.FileName, JsonWriter.Write(json), new UTF8Encoding(false));
                    ThemedDialog.Info(this, "Configuración exportada", "Se ha guardado en " + dialog.FileName + ".");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    ThemedDialog.Error(this, "No se pudo exportar", ex.Message);
                }
            }
        }

        private void ImportConfiguration()
        {
            using (var dialog = new OpenFileDialog { Title = "Importar configuración", Filter = "Copia de Iberia Smart Disc (*.json)|*.json", CheckFileExists = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if (new FileInfo(dialog.FileName).Length > JsonFileStore.MaxFileBytes) throw new FormatException("El archivo es demasiado grande.");
                    var json = JsonParser.Parse(File.ReadAllText(dialog.FileName, Encoding.UTF8), maxDepth: 32, rejectDuplicateKeys: false);
                    int dropped = ConfigBackup.Import(json, out var settings, out var library);
                    string note = dropped > 0
                        ? " Por seguridad, los " + dropped + " ejecutable(s) local(es) de la copia no se importan: elígelos de nuevo al meter cada disco."
                        : string.Empty;
                    if (!ThemedDialog.Confirm(this, "¿Sustituir la configuración actual?",
                        "Se cargarán " + library.Count + " juego(s) y sus preferencias. La configuración actual se perderá." + note, "Importar"))
                    {
                        return;
                    }
                    _app.State.Replace(settings, library);
                    _app.SetTrayIcon(settings.TrayIcon);
                    _app.SetCompatibilityPolling(settings.CompatibilityPolling);
                    LoadValues();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is FormatException)
                {
                    ThemedDialog.Error(this, "No se pudo importar", ex.Message);
                }
            }
        }

        // ── Comunes ───────────────────────────────────────────────────────

        private void LoadValues()
        {
            _loading = true;
            try
            {
                var settings = _app.State.Settings;
                _askMultiple.Checked = settings.AskWhenMultiple;
                _delay.Value = Math.Max(_delay.Minimum, Math.Min(_delay.Maximum, settings.LaunchDelaySeconds));
                _gogGalaxy.Checked = settings.GogUseGalaxy;
                _polling.Checked = settings.CompatibilityPolling;
                _logging.Checked = settings.Logging;
                _steamPath.Text = settings.SteamPath ?? string.Empty;
                LoadPriority();
                LoadFolders();
                LoadGames();
            }
            finally
            {
                _loading = false;
            }
        }

        private void Save(Action<AppSettings> change)
        {
            if (_loading) return;
            change(_app.State.Settings);
            _app.State.SaveSettings();
        }

        private static void OpenFolder(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                using (Process.Start(new ProcessStartInfo(explorer, "\"" + path + "\"") { UseShellExecute = false }))
                {
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
            {
            }
        }

        private static string ReplaceIgnoreCase(string text, string value, string replacement)
        {
            int index;
            while ((index = text.IndexOf(value, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                text = text.Substring(0, index) + replacement + text.Substring(index + value.Length);
            }
            return text;
        }
    }
}
