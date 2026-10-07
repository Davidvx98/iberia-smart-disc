using System;
using System.Collections.Generic;
using System.Linq;
using IberiaSmartDisc.Core.Json;
using IberiaSmartDisc.Core.Platforms;

namespace IberiaSmartDisc.Core.Configuration
{
    /// <summary>
    /// settings.json. La lectura es tolerante: un valor raro o editado a mano
    /// vuelve a su valor por defecto en lugar de impedir que el programa arranque.
    /// </summary>
    public sealed class AppSettings
    {
        public const int CurrentVersion = 1;
        public const int MaxLaunchDelaySeconds = 30;
        public const int MaxSearchPaths = 32;

        private List<LaunchKind> _priority = LaunchKinds.DefaultPriority.ToList();

        /// <summary>Registrar el inicio automático en HKCU\...\Run.</summary>
        public bool StartWithWindows { get; set; } = true;

        /// <summary>Abrir el juego al insertar el disco (con la cuenta atrás si está activa).</summary>
        public bool AutoLaunch { get; set; } = true;

        /// <summary>Mostrar el aviso con cuenta atrás antes de abrir el juego.</summary>
        public bool ShowLaunchNotice { get; set; } = true;

        public int LaunchDelaySeconds { get; set; } = 5;

        public bool TrayIcon { get; set; } = true;

        public bool Logging { get; set; } = true;

        public bool Paused { get; set; }

        /// <summary>Preguntar con qué abrir el juego si está en varias plataformas.</summary>
        public bool AskWhenMultiple { get; set; } = true;

        public bool GogUseGalaxy { get; set; }

        /// <summary>Sondeo cada 5 s para lectores que no avisan a Windows. Desactivado por defecto.</summary>
        public bool CompatibilityPolling { get; set; }

        /// <summary>Carpeta de Steam elegida a mano; null = detección automática.</summary>
        public string? SteamPath { get; set; }

        public List<string> SearchPaths { get; set; } = new List<string>();

        public IReadOnlyList<LaunchKind> PlatformPriority => _priority;

        public void SetPriority(IEnumerable<LaunchKind> order)
        {
            var result = new List<LaunchKind>();
            foreach (var kind in order)
            {
                if (!result.Contains(kind)) result.Add(kind);
            }
            foreach (var kind in LaunchKinds.DefaultPriority)
            {
                if (!result.Contains(kind)) result.Add(kind);
            }
            _priority = result;
        }

        public int PriorityOf(LaunchKind kind)
        {
            int index = _priority.IndexOf(kind);
            return index < 0 ? int.MaxValue : index;
        }

        public JsonNode ToJson()
        {
            var priority = JsonNode.NewArray();
            foreach (var kind in _priority) priority.Add(JsonNode.From(LaunchKinds.ToId(kind)));
            return JsonNode.NewObject()
                .Set("version", CurrentVersion)
                .Set("startup", StartWithWindows)
                .Set("autoLaunch", AutoLaunch)
                .Set("notifications", ShowLaunchNotice)
                .Set("launchDelaySeconds", LaunchDelaySeconds)
                .Set("trayIcon", TrayIcon)
                .Set("logging", Logging)
                .Set("paused", Paused)
                .Set("askWhenMultiple", AskWhenMultiple)
                .Set("gogUseGalaxy", GogUseGalaxy)
                .Set("compatibilityPolling", CompatibilityPolling)
                .Set("steamPath", SteamPath)
                .Set("platformPriority", priority)
                .Set("searchPaths", JsonNode.FromStrings(SearchPaths));
        }

        public static AppSettings FromJson(JsonNode? json)
        {
            var settings = new AppSettings();
            if (json == null || json.Kind != JsonKind.Object) return settings;

            settings.StartWithWindows = json.GetBoolean("startup") ?? settings.StartWithWindows;
            settings.AutoLaunch = json.GetBoolean("autoLaunch") ?? settings.AutoLaunch;
            settings.ShowLaunchNotice = json.GetBoolean("notifications") ?? settings.ShowLaunchNotice;
            settings.TrayIcon = json.GetBoolean("trayIcon") ?? settings.TrayIcon;
            settings.Logging = json.GetBoolean("logging") ?? settings.Logging;
            settings.Paused = json.GetBoolean("paused") ?? settings.Paused;
            settings.AskWhenMultiple = json.GetBoolean("askWhenMultiple") ?? settings.AskWhenMultiple;
            settings.GogUseGalaxy = json.GetBoolean("gogUseGalaxy") ?? settings.GogUseGalaxy;
            settings.CompatibilityPolling = json.GetBoolean("compatibilityPolling") ?? settings.CompatibilityPolling;

            long? delay = json.GetInt64("launchDelaySeconds");
            if (delay != null) settings.LaunchDelaySeconds = (int)Math.Max(0, Math.Min(MaxLaunchDelaySeconds, delay.Value));

            string? steamPath = json.GetString("steamPath");
            settings.SteamPath = string.IsNullOrWhiteSpace(steamPath) ? null : steamPath!.Trim();

            var paths = json.GetArray("searchPaths");
            if (paths != null)
            {
                foreach (var item in paths.Items)
                {
                    string? path = item.AsString()?.Trim();
                    if (string.IsNullOrEmpty(path) || path!.Length > 260) continue;
                    if (settings.SearchPaths.Count >= MaxSearchPaths) break;
                    if (!settings.SearchPaths.Contains(path, StringComparer.OrdinalIgnoreCase)) settings.SearchPaths.Add(path);
                }
            }

            var priority = json.GetArray("platformPriority");
            if (priority != null)
            {
                var kinds = new List<LaunchKind>();
                foreach (var item in priority.Items)
                {
                    if (LaunchKinds.TryParse(item.AsString(), out var kind)) kinds.Add(kind);
                }
                settings.SetPriority(kinds);
            }
            return settings;
        }
    }
}
