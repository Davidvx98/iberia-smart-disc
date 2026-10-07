using System.IO;
using System.Linq;
using IberiaSmartDisc.Core.Configuration;
using IberiaSmartDisc.Core.Manifest;
using IberiaSmartDisc.Core.Platforms;
using IberiaSmartDisc.Core.Platforms.Disc;
using IberiaSmartDisc.Core.Platforms.Epic;
using IberiaSmartDisc.Core.Platforms.Gog;
using IberiaSmartDisc.Core.Platforms.Steam;
using Xunit;

namespace IberiaSmartDisc.Core.Tests
{
    public class PlatformTests
    {
        private static string Vdf(string path) => path.Replace("\\", "\\\\");

        private static DiscManifest Manifest(string platforms, string extra = "") =>
            ManifestParser.Parse("{\"iberiaDisc\": true, \"format\": 1, \"game\": {\"id\": \"juego\", \"name\": \"Juego\"}, \"platforms\": " + platforms + extra + "}");

        [Fact]
        public void SteamFindsGamesInAdditionalLibraries()
        {
            using (var temp = new TempDir())
            {
                string steam = temp.Dir("Steam", "steamapps");
                string library = temp.Dir("OtroDisco", "SteamLibrary");
                temp.Dir("OtroDisco", "SteamLibrary", "steamapps", "common", "Portal 2");
                File.WriteAllText(Path.Combine(steam, "libraryfolders.vdf"),
                    "\"libraryfolders\" { \"0\" { \"path\" \"" + Vdf(temp.Combine("Steam")) + "\" } \"1\" { \"path\" \"" + Vdf(library) + "\" } }");
                File.WriteAllText(Path.Combine(library, "steamapps", "appmanifest_620.acf"),
                    "\"AppState\" { \"appid\" \"620\" \"name\" \"Portal 2\" \"StateFlags\" \"4\" \"installdir\" \"Portal 2\" }");

                var registry = new FakeRegistry().Set(RegistryRoot.CurrentUser, @"Software\Valve\Steam", "SteamPath", temp.Combine("Steam").Replace('\\', '/'));
                var provider = new SteamProvider(new SteamLocator(registry, new FakeSystem()), registry);
                var context = new ResolveContext(Manifest("{\"steam\": {\"appId\": 620}}"), null, new AppSettings());

                var candidate = Assert.Single(provider.FindInstalled(context));
                Assert.Equal("steam:620", candidate.Key);
                Assert.Equal("steam://rungameid/620", candidate.Target.Uri);
                Assert.Equal(Path.Combine(library, "steamapps", "common", "Portal 2"), candidate.Detail);
                Assert.Equal("steam://install/620", provider.GetInstallAction(context)!.Target.Uri);
            }
        }

        [Fact]
        public void SteamReadsLegacyLibraryFormatAndPendingUpdates()
        {
            using (var temp = new TempDir())
            {
                temp.Dir("Steam", "steamapps");
                string library = temp.Dir("Lib");
                temp.Dir("Lib", "steamapps", "common", "Juego");
                temp.File("Steam/steamapps/libraryfolders.vdf", "\"LibraryFolders\" { \"TimeNextStatsReport\" \"1\" \"1\" \"" + Vdf(library) + "\" }");
                temp.File("Lib/steamapps/appmanifest_10.acf", "\"AppState\" { \"StateFlags\" \"6\" \"installdir\" \"Juego\" }");

                var locator = new SteamLocator(new FakeRegistry(), new FakeSystem());
                var root = locator.FindRoot(temp.Combine("Steam"));
                Assert.NotNull(root);
                var app = locator.FindApp(locator.FindLibraries(root!, new string[0]), "10");
                Assert.NotNull(app);
                Assert.True(app!.FullyInstalled);
            }
        }

        [Fact]
        public void SteamIgnoresManifestsWhoseFolderIsGoneOrUnsafe()
        {
            using (var temp = new TempDir())
            {
                temp.Dir("Steam", "steamapps", "common");
                temp.File("Steam/steamapps/appmanifest_1.acf", "\"AppState\" { \"installdir\" \"Borrado\" }");
                temp.File("Steam/steamapps/appmanifest_2.acf", "\"AppState\" { \"installdir\" \"..\" }");
                var locator = new SteamLocator(new FakeRegistry(), new FakeSystem());
                var libraries = locator.FindLibraries(temp.Combine("Steam"), new string[0]);
                Assert.Null(locator.FindApp(libraries, "1"));
                Assert.Null(locator.FindApp(libraries, "2"));
            }
        }

        [Fact]
        public void SteamWithoutClientOffersNothing()
        {
            var registry = new FakeRegistry();
            var provider = new SteamProvider(new SteamLocator(registry, new FakeSystem()), registry);
            var context = new ResolveContext(Manifest("{\"steam\": {\"appId\": 620}}"), null, new AppSettings());
            Assert.Empty(provider.FindInstalled(context));
            Assert.Null(provider.GetInstallAction(context));
            Assert.False(provider.IsClientAvailable(new AppSettings()));
        }

        [Fact]
        public void EpicMatchesByAppNameAndSkipsDlc()
        {
            using (var temp = new TempDir())
            {
                string install = temp.Dir("Epic Games", "Juego");
                string manifests = temp.Dir("ProgramData", "Epic", "EpicGamesLauncher", "Data", "Manifests");
                string json = "{\"AppName\": \"Sugar\", \"MainGameAppName\": \"Sugar\", \"DisplayName\": \"Juego\", \"InstallLocation\": \"" + Vdf(install)
                    + "\", \"CatalogNamespace\": \"ns1\", \"CatalogItemId\": \"item1\"}";
                File.WriteAllText(Path.Combine(manifests, "A.item"), json);
                File.WriteAllText(Path.Combine(manifests, "B.item"), "{\"AppName\": \"SugarDlc\", \"MainGameAppName\": \"Sugar\", \"InstallLocation\": \"" + Vdf(install) + "\"}");
                File.WriteAllText(Path.Combine(manifests, "C.item"), "{ roto");

                var registry = new FakeRegistry().AddKey(RegistryRoot.ClassesRoot, "com.epicgames.launcher");
                var provider = new EpicProvider(registry, new FakeSystem().With(KnownFolder.ProgramData, temp.Combine("ProgramData")));
                var context = new ResolveContext(Manifest("{\"epic\": {\"appName\": \"Sugar\", \"namespace\": \"ns1\", \"catalogItemId\": \"item1\"}}"), null, new AppSettings());

                var candidate = Assert.Single(provider.FindInstalled(context));
                Assert.Equal("epic:Sugar", candidate.Key);
                Assert.Equal("com.epicgames.launcher://apps/ns1%3Aitem1%3ASugar?action=launch&silent=true", candidate.Target.Uri);
                Assert.Equal("com.epicgames.launcher://apps/ns1%3Aitem1%3ASugar?action=install&silent=true", provider.GetInstallAction(context)!.Target.Uri);
            }
        }

        [Fact]
        public void EpicUriFallsBackToLegacyFormat()
        {
            Assert.Equal("com.epicgames.launcher://apps/Sugar?action=launch&silent=true", EpicProvider.BuildUri(null, null, "Sugar", "launch"));
        }

        [Fact]
        public void GogUsesRegistryAndOptionallyGalaxy()
        {
            using (var temp = new TempDir())
            {
                string directory = temp.Dir("GOG Games", "Juego");
                string exe = temp.File("GOG Games/Juego/juego.exe", "");
                string galaxy = temp.Dir("Galaxy");
                temp.File("Galaxy/GalaxyClient.exe", "");
                var registry = new FakeRegistry()
                    .Set(RegistryRoot.LocalMachine, @"SOFTWARE\GOG.com\Games\1207658930", "exe", exe)
                    .Set(RegistryRoot.LocalMachine, @"SOFTWARE\GOG.com\Games\1207658930", "path", directory)
                    .Set(RegistryRoot.LocalMachine, @"SOFTWARE\GOG.com\GalaxyClient\paths", "client", galaxy);
                var provider = new GogProvider(registry, new FakeSystem());
                var context = new ResolveContext(Manifest("{\"gog\": {\"productId\": \"1207658930\"}}"), null, new AppSettings());

                var direct = Assert.Single(provider.FindInstalled(context));
                Assert.Equal("gog:1207658930", direct.Key);
                Assert.Equal(exe, direct.Target.ExecutablePath);
                Assert.True(direct.Target.CheckAlreadyRunning);

                var viaGalaxy = Assert.Single(provider.FindInstalled(new ResolveContext(context.Manifest, null, new AppSettings { GogUseGalaxy = true })));
                Assert.Equal(Path.Combine(galaxy, "GalaxyClient.exe"), viaGalaxy.Target.ExecutablePath);
                Assert.Contains("/gameId=1207658930", viaGalaxy.Target.Arguments);
                Assert.False(viaGalaxy.Target.CheckAlreadyRunning);
            }
        }

        [Fact]
        public void GogFindsCopiedGamesThroughInfoFile()
        {
            using (var temp = new TempDir())
            {
                string games = temp.Dir("MisJuegos");
                temp.File("MisJuegos/Witcher/goggame-123.info",
                    "{\"name\": \"Witcher\", \"playTasks\": [{\"isPrimary\": true, \"type\": \"FileTask\", \"path\": \"bin\\\\x64\\\\witcher.exe\", \"arguments\": \"-skip\"}]}");
                string exe = temp.File("MisJuegos/Witcher/bin/x64/witcher.exe", "");
                var provider = new GogProvider(new FakeRegistry(), new FakeSystem());
                var settings = new AppSettings();
                settings.SearchPaths.Add(games);

                var candidate = Assert.Single(provider.FindInstalled(new ResolveContext(Manifest("{\"gog\": {\"productId\": 123}}"), null, settings)));
                Assert.Equal(exe, candidate.Target.ExecutablePath);
                Assert.Equal("-skip", candidate.Target.Arguments);
            }
        }

        [Fact]
        public void DiscProviderOnlyRunsTheDeclaredFile()
        {
            using (var temp = new TempDir())
            {
                temp.File("Game/Game.exe", "");
                temp.File("Otro.exe", "");
                var manifest = ManifestParser.Parse("{\"iberiaDisc\": true, \"format\": 1, \"game\": {\"id\": \"mi-juego\", \"name\": \"Mi Juego\"}, \"discLaunch\": {\"mode\": \"executable\", \"path\": \"Game\\\\Game.exe\"}}");
                var provider = new DiscProvider();

                var candidate = Assert.Single(provider.FindInstalled(new ResolveContext(manifest, temp.Path, new AppSettings())));
                Assert.Equal("disc:Game\\Game.exe", candidate.Key);
                Assert.Equal(temp.Combine("Game", "Game.exe"), candidate.Target.ExecutablePath);
                Assert.Empty(provider.FindInstalled(new ResolveContext(manifest, null, new AppSettings())));
            }
        }

        [Fact]
        public void DiscInstallerAlwaysRequiresConfirmation()
        {
            using (var temp = new TempDir())
            {
                temp.File("Setup.exe", "");
                var manifest = ManifestParser.Parse("{\"iberiaDisc\": true, \"format\": 1, \"game\": {\"id\": \"a\", \"name\": \"A\"}, \"discLaunch\": {\"mode\": \"installer\", \"path\": \"Setup.exe\"}}");
                var provider = new DiscProvider();
                var context = new ResolveContext(manifest, temp.Path, new AppSettings());
                Assert.Empty(provider.FindInstalled(context));
                var action = provider.GetInstallAction(context)!;
                Assert.True(action.RequiresConfirmation);
                Assert.False(action.Target.CheckAlreadyRunning);
            }
        }

        [Fact]
        public void KeysMapBackToKinds()
        {
            Assert.Equal(LaunchKind.Steam, LaunchKinds.KindOfKey("steam:620"));
            Assert.Equal(LaunchKind.Local, LaunchKinds.KindOfKey(@"local:D:\Juegos\x.exe"));
            Assert.Null(LaunchKinds.KindOfKey("raro:1"));
            Assert.Null(LaunchKinds.KindOfKey(null));
            Assert.All(LaunchKinds.DefaultPriority, kind =>
            {
                Assert.True(LaunchKinds.TryParse(LaunchKinds.ToId(kind), out var parsed));
                Assert.Equal(kind, parsed);
            });
            Assert.Equal(5, LaunchKinds.DefaultPriority.Distinct().Count());
        }
    }
}
