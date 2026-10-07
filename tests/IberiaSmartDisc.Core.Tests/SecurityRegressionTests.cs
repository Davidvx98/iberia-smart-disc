using System;
using System.IO;
using System.Linq;
using System.Threading;
using IberiaSmartDisc.Core.Common;
using IberiaSmartDisc.Core.Configuration;
using IberiaSmartDisc.Core.Json;
using IberiaSmartDisc.Core.Manifest;
using IberiaSmartDisc.Core.Platforms;
using IberiaSmartDisc.Core.Platforms.Gog;
using IberiaSmartDisc.Core.Platforms.Local;
using IberiaSmartDisc.Core.Platforms.Steam;
using Xunit;

namespace IberiaSmartDisc.Core.Tests
{
    /// <summary>
    /// Un test por hallazgo de la auditoría de seguridad (2026-10-07), con su
    /// identificador en el nombre, para que no vuelvan a aparecer.
    /// </summary>
    public class SecurityRegressionTests
    {
        private static string Json(string text) => JsonWriter.Write(JsonNode.From(text), indented: false);

        private static string WithName(string name) =>
            "{\"iberiaDisc\": true, \"format\": 1, \"game\": {\"id\": \"a\", \"name\": " + Json(name) + "}}";

        private static string WithLaunch(string path, string mode = "executable", string? arguments = null) =>
            "{\"iberiaDisc\": true, \"format\": 1, \"game\": {\"id\": \"a\", \"name\": \"A\"}, \"discLaunch\": {\"mode\": \"" + mode + "\", \"path\": " + Json(path)
            + (arguments == null ? string.Empty : ", \"arguments\": " + Json(arguments)) + "}}";

        [Theory]
        [InlineData(0x202E)] // RIGHT-TO-LEFT OVERRIDE
        [InlineData(0x2066)] // LEFT-TO-RIGHT ISOLATE
        [InlineData(0x200B)] // ZERO WIDTH SPACE
        [InlineData(0x2028)] // LINE SEPARATOR
        [InlineData(0xFEFF)] // ZERO WIDTH NO-BREAK SPACE
        [InlineData(0xE000)] // uso privado
        [InlineData(0x00AD)] // SOFT HYPHEN
        [InlineData(0x0085)] // NEXT LINE
        public void Disco04_InvisibleOrBidiCharactersAreRejected(int code)
        {
            string c = ((char)code).ToString();
            Assert.Equal("game.name", Assert.Throws<ManifestException>(() => ManifestParser.Parse(WithName("Portal" + c + "2"))).Field);
            Assert.Equal("discLaunch.path", Assert.Throws<ManifestException>(() => ManifestParser.Parse(WithLaunch("Manual" + c + "fdp.exe"))).Field);
            Assert.Equal("discLaunch.arguments", Assert.Throws<ManifestException>(() => ManifestParser.Parse(WithLaunch("Game.exe", arguments: "-a" + c))).Field);
        }

        [Fact]
        public void Disco04_TagCharactersAndLoneSurrogatesAreRejected()
        {
            Assert.False(TextSafety.IsSafe("Portal" + char.ConvertFromUtf32(0xE0041)));
            Assert.False(TextSafety.IsSafe("Portal" + (char)0xD800));
            Assert.False(TextSafety.IsSafe("Portal" + (char)0xDC00 + "x"));
        }

        [Fact]
        public void Disco04_NormalTextIsStillAccepted()
        {
            Assert.Equal("Pokémon: Edición Ñandú 東方", ManifestParser.Parse(WithName("Pokémon: Edición Ñandú 東方")).GameName);
            var emoji = string.Concat(Enumerable.Repeat(char.ConvertFromUtf32(0x1F3AE), 100));
            Assert.Equal(emoji, ManifestParser.Parse(WithName(emoji)).GameName); // 100 puntos de código, como cuenta el esquema
            Assert.Equal(@"Juego (2004)\Ñandú.exe", ManifestParser.Parse(WithLaunch(@"Juego (2004)\Ñandú.exe")).DiscLaunch!.RelativePath);
        }

        [Fact]
        public void Disco06_InstallersCannotCarryHiddenArguments()
        {
            Assert.Equal("discLaunch.arguments", Assert.Throws<ManifestException>(() => ManifestParser.Parse(WithLaunch("Setup.msi", "installer", "/qn TARGETDIR=C:\\"))).Field);
            Assert.Equal("-windowed", ManifestParser.Parse(WithLaunch("Game.exe", arguments: "-windowed")).DiscLaunch!.Arguments);
        }

        [Theory]
        [InlineData("con")]
        [InlineData("nul")]
        [InlineData("aux")]
        [InlineData("com1")]
        [InlineData("lpt9")]
        public void Disco10_DeviceNamesAreNotGameIds(string id)
        {
            Assert.False(ManifestRules.IsGameId(id));
            Assert.True(ManifestRules.IsGameId(id + "-2"));
        }

        [Fact]
        public void Pub10_TrailingNewlineIsRejectedInIdentifiers()
        {
            Assert.False(ManifestRules.IsGameId("portal-2\n"));
            Assert.False(ManifestRules.IsDiscId("ICD-1\n"));
            Assert.False(ManifestRules.IsNumericId("620\n"));
            Assert.False(ManifestRules.IsEpicToken("Sugar\n"));
        }

        [Fact]
        public void Disco11_ErrorsDoNotRepeatTextFromTheDisc()
        {
            var error = Assert.Throws<ManifestException>(() => ManifestParser.Parse("{\"iberiaDisc\": true, \"secreto\": 1, \"secreto\": 2}"));
            Assert.DoesNotContain("secreto", error.Message);
        }

        [Fact]
        public void Pub01_IntegersWrittenWithDecimalsMatchJsonSchema()
        {
            var manifest = ManifestParser.Parse("{\"iberiaDisc\": true, \"format\": 1.0, \"game\": {\"id\": \"a\", \"name\": \"A\"}, \"platforms\": {\"steam\": {\"appId\": 6.2e2}}}");
            Assert.Equal(new[] { "620" }, manifest.SteamAppIds);
            Assert.Throws<ManifestException>(() => ManifestParser.Parse("{\"iberiaDisc\": true, \"format\": 1.5, \"game\": {\"id\": \"a\", \"name\": \"A\"}}"));
        }

        [Fact]
        public void Pub01_IdListLimitMatchesJsonSchema()
        {
            string eight = string.Join(", ", Enumerable.Range(1, 8).Select(i => i.ToString()));
            var manifest = ManifestParser.Parse("{\"iberiaDisc\": true, \"format\": 1, \"game\": {\"id\": \"a\", \"name\": \"A\"}, \"platforms\": {\"steam\": {\"appId\": 100, \"appIds\": [" + eight + "]}}}");
            Assert.Equal(9, manifest.SteamAppIds.Count);
            Assert.Throws<ManifestException>(() => ManifestParser.Parse("{\"iberiaDisc\": true, \"format\": 1, \"game\": {\"id\": \"a\", \"name\": \"A\"}, \"platforms\": {\"steam\": {\"appIds\": [" + eight + ", 9]}}}"));
        }

        [Fact]
        public void Disco07_ImageHeadersAreCheckedBeforeDecoding()
        {
            var png = new byte[33];
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 2, 0x58, 0, 0, 3, 0x84 }.CopyTo(png, 0);
            Assert.True(ImageHeader.TryRead(png, out var kind, out var width, out var height));
            Assert.Equal((ImageKind.Png, 600, 900), (kind, width, height));

            var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x03, 0x84, 0x02, 0x58, 0x03 };
            Assert.True(ImageHeader.TryRead(jpeg, out kind, out width, out height));
            Assert.Equal((ImageKind.Jpeg, 600, 900), (kind, width, height));

            var bmp = new byte[54];
            bmp[0] = (byte)'B';
            bmp[1] = (byte)'M';
            BitConverter.GetBytes(40).CopyTo(bmp, 14);
            BitConverter.GetBytes(600).CopyTo(bmp, 18);
            BitConverter.GetBytes(-900).CopyTo(bmp, 22);
            Assert.True(ImageHeader.TryRead(bmp, out kind, out width, out height));
            Assert.Equal((ImageKind.Bmp, 600, 900), (kind, width, height));

            // Un EMF (metarchivo) o un PNG truncado no pasan.
            Assert.False(ImageHeader.TryRead(new byte[] { 0x01, 0x00, 0x00, 0x00, 0x6C, 0x00, 0x00, 0x00 }, out _, out _, out _));
            Assert.False(ImageHeader.TryRead(png.Take(20).ToArray(), out _, out _, out _));
            Assert.False(ImageHeader.IsAcceptableSize(60000, 60000));
            Assert.False(ImageHeader.IsAcceptableSize(8000, 8000));
            Assert.True(ImageHeader.IsAcceptableSize(2400, 3600));
            Assert.Equal(ImageKind.Jpeg, ImageHeader.KindForExtension(".JPEG"));
        }

        [Fact]
        public void Disco09_DiscHintsCannotOfferUninstallers()
        {
            using (var temp = new TempDir())
            {
                temp.File("Games/Juego/unins000.exe", "");
                var manifest = ManifestParser.Parse("{\"iberiaDisc\": true, \"format\": 1, \"game\": {\"id\": \"juego\", \"name\": \"Juego\"}, \"local\": {\"executables\": [\"unins000.exe\"]}}");
                Assert.Empty(new LocalGameSearch().Search(manifest, new[] { temp.Combine("Games") }, CancellationToken.None));
            }
        }

        [Fact]
        public void Win04_GogInfoFilesOnSharedDriveRootsAreIgnored()
        {
            using (var temp = new TempDir())
            {
                // Una carpeta que cualquier cuenta del PC podría crear en la raíz de una unidad.
                string drive = temp.Dir("D");
                temp.File("D/GOG Games/Falso/goggame-123.info", "{\"playTasks\": [{\"isPrimary\": true, \"type\": \"FileTask\", \"path\": \"malo.exe\"}]}");
                temp.File("D/GOG Games/Falso/malo.exe", "");
                var system = new FakeSystem();
                system.Drives.Add(drive);
                var provider = new GogProvider(new FakeRegistry(), system);
                Assert.Null(provider.FromInfoFile("123", new string[0]));
                // Si el usuario añade esa carpeta a mano, sí se usa.
                Assert.NotNull(provider.FromInfoFile("123", new[] { temp.Combine("D", "GOG Games") }));
            }
        }

        [Fact]
        public void Win04_NetworkPathsAreNeverTouched()
        {
            Assert.True(PathTools.IsNetworkPath(@"\\servidor\juegos"));
            Assert.True(PathTools.IsNetworkPath("//servidor/juegos"));
            Assert.True(PathTools.IsNetworkPath(@"\\?\UNC\servidor\juegos"));
            Assert.False(PathTools.IsNetworkPath(@"D:\Juegos"));

            var locator = new SteamLocator(new FakeRegistry(), new FakeSystem());
            Assert.Null(locator.FindRoot(@"\\servidor\Steam"));
            Assert.Empty(LocalGameSearch.DefaultRoots(new FakeSystem(), new[] { @"\\servidor\juegos" }));
        }

        [Fact]
        public void Disco08_PlatformNameIsShownAndSanitized()
        {
            var target = LaunchTarget.ForUri(LaunchKind.Steam, "steam://rungameid/620");
            Assert.Equal("Steam · Portal 2", new LaunchCandidate(LaunchKind.Steam, "steam:620", "Steam", null, target, "Portal 2").DisplayTitle);
            Assert.Equal("Steam", new LaunchCandidate(LaunchKind.Steam, "steam:620", "Steam", null, target, "Portal" + (char)0x202E + "2").DisplayTitle);
        }
    }
}
