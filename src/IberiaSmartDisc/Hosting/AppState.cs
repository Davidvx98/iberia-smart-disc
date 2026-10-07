using System;
using System.Collections.Generic;
using IberiaSmartDisc.Core.Configuration;
using IberiaSmartDisc.Core.Platforms;
using IberiaSmartDisc.Core.Platforms.Disc;
using IberiaSmartDisc.Core.Platforms.Epic;
using IberiaSmartDisc.Core.Platforms.Gog;
using IberiaSmartDisc.Core.Platforms.Steam;
using IberiaSmartDisc.Core.Resolution;
using IberiaSmartDisc.Logging;
using IberiaSmartDisc.Platform;

namespace IberiaSmartDisc.Hosting
{
    /// <summary>Configuración, biblioteca y plataformas de la instancia en marcha.</summary>
    internal sealed class AppState
    {
        private readonly AppLog _log;

        private AppState(AppLog log, AppSettings settings, GameLibrary library)
        {
            _log = log;
            Settings = settings;
            Library = library;
            Registry = new WindowsRegistryReader();
            SystemInfo = new WindowsSystemInfo();
            var providers = new List<IPlatformProvider>
            {
                new DiscProvider(),
                new SteamProvider(new SteamLocator(Registry, SystemInfo), Registry),
                new GogProvider(Registry, SystemInfo),
                new EpicProvider(Registry, SystemInfo),
            };
            Resolver = new GameResolver(providers);
        }

        public event Action? Changed;

        public AppSettings Settings { get; private set; }

        public GameLibrary Library { get; private set; }

        public GameResolver Resolver { get; }

        public IRegistryReader Registry { get; }

        public ISystemInfo SystemInfo { get; }

        public AppLog Log => _log;

        public static AppState Load(AppLog log)
        {
            var settingsJson = JsonFileStore.Load(AppPaths.SettingsFile, out var settingsProblem);
            if (settingsProblem != null) log.Warn(settingsProblem);
            var gamesJson = JsonFileStore.Load(AppPaths.GamesFile, out var gamesProblem);
            if (gamesProblem != null) log.Warn(gamesProblem);
            return new AppState(log, AppSettings.FromJson(settingsJson), GameLibrary.FromJson(gamesJson));
        }

        public void SaveSettings()
        {
            try
            {
                JsonFileStore.Save(AppPaths.SettingsFile, Settings.ToJson());
            }
            catch (Exception ex)
            {
                _log.Error("No se pudo guardar settings.json", ex);
            }
            NotifyChanged();
        }

        public void SaveLibrary()
        {
            try
            {
                JsonFileStore.Save(AppPaths.GamesFile, Library.ToJson());
            }
            catch (Exception ex)
            {
                _log.Error("No se pudo guardar games.json", ex);
            }
            NotifyChanged();
        }

        public void Replace(AppSettings settings, GameLibrary library)
        {
            Settings = settings;
            Library = library;
            SaveSettings();
            SaveLibrary();
        }

        public void NotifyChanged() => Changed?.Invoke();
    }
}
