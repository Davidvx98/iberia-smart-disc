using System;
using System.IO;
using System.Linq;
using IberiaSmartDisc.Core.Configuration;
using IberiaSmartDisc.Core.Json;
using IberiaSmartDisc.Core.Manifest;
using IberiaSmartDisc.Core.Platforms;
using IberiaSmartDisc.Core.Resolution;
using Xunit;

namespace IberiaSmartDisc.Core.Tests
{
    public class ConfigurationTests
    {
        private static DiscManifest Manifest(string id = "portal-2", string extra = "") => ManifestParser.Parse(
            "{\"iberiaDisc\": true, \"format\": 1, \"discId\": \"ICD-1\", \"game\": {\"id\": \"" + id + "\", \"name\": \"Portal 2\"}" + extra + "}");

        [Fact]
        public void SettingsRoundTrip()
        {
            var settings = new AppSettings { AutoLaunch = false, LaunchDelaySeconds = 9, TrayIcon = false, SteamPath = @"D:\Steam", GogUseGalaxy = true };
            settings.SearchPaths.Add(@"E:\Juegos");
            settings.SetPriority(new[] { LaunchKind.Local, LaunchKind.Gog });

            var copy = AppSettings.FromJson(JsonParser.Parse(JsonWriter.Write(settings.ToJson())));
            Assert.False(copy.AutoLaunch);
            Assert.Equal(9, copy.LaunchDelaySeconds);
            Assert.False(copy.TrayIcon);
            Assert.True(copy.GogUseGalaxy);
            Assert.Equal(@"D:\Steam", copy.SteamPath);
            Assert.Equal(new[] { @"E:\Juegos" }, copy.SearchPaths);
            Assert.Equal(new[] { LaunchKind.Local, LaunchKind.Gog, LaunchKind.Disc, LaunchKind.Steam, LaunchKind.Epic }, copy.PlatformPriority);
        }

        [Fact]
        public void SettingsToleratesHandEditedGarbage()
        {
            var settings = AppSettings.FromJson(JsonParser.Parse(
                "{\"startup\": \"sí\", \"launchDelaySeconds\": 999, \"searchPaths\": [1, \"\", \"D:\\\\Juegos\", \"d:\\\\juegos\"], \"platformPriority\": [\"steam\", \"steam\", \"xbox\"]}"));
            Assert.True(settings.StartWithWindows);
            Assert.Equal(AppSettings.MaxLaunchDelaySeconds, settings.LaunchDelaySeconds);
            Assert.Equal(new[] { @"D:\Juegos" }, settings.SearchPaths);
            Assert.Equal(LaunchKind.Steam, settings.PlatformPriority[0]);
            Assert.Equal(5, settings.PlatformPriority.Count);
            Assert.True(AppSettings.FromJson(JsonParser.Parse("[]")).AutoLaunch);
        }

        [Fact]
        public void LibraryRecordsInsertionsLaunchesAndPreferences()
        {
            var library = new GameLibrary();
            var now = new DateTime(2026, 10, 4, 10, 42, 0, DateTimeKind.Utc);
            library.RecordInsertion(Manifest(), now);
            var record = library.RecordInsertion(Manifest(), now.AddMinutes(5));
            Assert.Equal(2, record.TimesInserted);
            Assert.Equal(new[] { "ICD-1" }, record.DiscIds);
            Assert.Equal(now, record.FirstSeenUtc);
            Assert.Equal("portal-2", library.LastGameId);

            library.RecordLaunch("portal-2", LaunchKind.Steam, now.AddMinutes(6));
            library.SetPreference("portal-2", GameResolver.CreateLocalCandidate(@"D:\Juegos\portal2.exe", null), alwaysUse: true);

            var copy = GameLibrary.FromJson(JsonParser.Parse(JsonWriter.Write(library.ToJson())));
            var loaded = copy.Find("portal-2")!;
            Assert.Equal(LaunchKind.Steam, loaded.LastPlatform);
            Assert.Equal(now.AddMinutes(6), loaded.LastPlayedUtc);
            Assert.True(loaded.AlwaysUse);
            Assert.Equal(LaunchKind.Local, loaded.PreferredKind);
            Assert.Equal(@"D:\Juegos\portal2.exe", loaded.LocalExecutable);
            Assert.Equal("portal-2", copy.LastGameId);

            copy.ClearPreference("portal-2");
            Assert.False(copy.Find("portal-2")!.AlwaysUse);
            Assert.True(copy.Forget("portal-2"));
            Assert.Null(copy.LastGameId);
        }

        [Fact]
        public void DiscTrustIsNoLongerStored()
        {
            // DISCO-01 / WIN-03: la confianza en ejecutables de discos ya no se guarda ni se importa.
            var library = GameLibrary.FromJson(JsonParser.Parse(
                "{\"games\": {\"mi-juego\": {\"name\": \"Mi Juego\"}}, \"trustedDiscLaunches\": [{\"gameId\": \"mi-juego\", \"manifestSha256\": \"x\", \"path\": \"Game.exe\"}]}"));
            string saved = JsonWriter.Write(library.ToJson());
            Assert.DoesNotContain("trusted", saved, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("arguments", saved, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void LibrarySkipsInvalidEntries()
        {
            var library = GameLibrary.FromJson(JsonParser.Parse(
                "{\"games\": {\"../malo\": {\"name\": \"x\"}, \"bueno\": {\"name\": \"Bueno\", \"preferredKey\": \"rara:1\", \"timesInserted\": -4}, \"lista\": []}, \"lastGameId\": \"no-existe\"}"));
            Assert.Equal(1, library.Count);
            Assert.Null(library.Find("bueno")!.PreferredKey);
            Assert.Equal(0, library.Find("bueno")!.TimesInserted);
            Assert.Null(library.LastGameId);
        }

        [Fact]
        public void FileStoreSavesAtomicallyAndQuarantinesCorruptFiles()
        {
            using (var temp = new TempDir())
            {
                string path = temp.Combine("settings.json");
                JsonFileStore.Save(path, new AppSettings { LaunchDelaySeconds = 3 }.ToJson());
                JsonFileStore.Save(path, new AppSettings { LaunchDelaySeconds = 7 }.ToJson());
                Assert.Equal(7, AppSettings.FromJson(JsonFileStore.Load(path, out var none)).LaunchDelaySeconds);
                Assert.Null(none);
                Assert.False(File.Exists(path + ".tmp"));

                File.WriteAllText(path, "{ roto");
                Assert.Null(JsonFileStore.Load(path, out var problem));
                Assert.NotNull(problem);
                Assert.False(File.Exists(path));
                Assert.Single(Directory.GetFiles(temp.Path, "settings.json.corrupt-*"));
            }
        }

        [Fact]
        public void BackupRoundTripsAndRejectsOtherFiles()
        {
            var settings = new AppSettings { LaunchDelaySeconds = 12 };
            var library = new GameLibrary();
            library.RecordInsertion(Manifest(), DateTime.UtcNow);
            library.RecordInsertion(Manifest("otro"), DateTime.UtcNow);
            library.SetPreference("portal-2", GameResolver.CreateLocalCandidate(@"C:\Windows\System32\cmd.exe", null), alwaysUse: true);
            library.SetPreference("otro", new LaunchCandidate(LaunchKind.Steam, "steam:620", "Steam", null, LaunchTarget.ForUri(LaunchKind.Steam, "steam://rungameid/620")), alwaysUse: true);

            var json = JsonParser.Parse(JsonWriter.Write(ConfigBackup.Export(settings, library, DateTime.UtcNow)));
            int dropped = ConfigBackup.Import(json, out var importedSettings, out var importedLibrary);
            Assert.Equal(12, importedSettings.LaunchDelaySeconds);

            // WIN-03: una copia ajena no puede dejar programas que se abran al meter un disco.
            Assert.Equal(1, dropped);
            var imported = importedLibrary.Find("portal-2")!;
            Assert.Null(imported.LocalExecutable);
            Assert.Null(imported.PreferredKey);
            Assert.False(imported.AlwaysUse);
            // Las preferencias de plataforma (que abren por Steam, no un .exe) se conservan.
            Assert.True(importedLibrary.Find("otro")!.AlwaysUse);

            Assert.Throws<FormatException>(() => ConfigBackup.Import(JsonParser.Parse("{\"games\": {}}"), out _, out _));
        }
    }
}
