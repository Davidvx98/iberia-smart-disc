using System;
using System.IO;
using IberiaSmartDisc.Core.Manifest;
using Xunit;

namespace IberiaSmartDisc.Core.Tests
{
    public class ManifestTests
    {
        private static DiscManifest Parse(string json) => ManifestParser.Parse(json);

        private static ManifestException Fails(string json) => Assert.Throws<ManifestException>(() => ManifestParser.Parse(json));

        [Theory]
        [InlineData("smart-disc")]
        [InlineData("data-disc")]
        [InlineData("multi-platform")]
        public void ExamplesAreValid(string folder)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "examples", folder, "iberia-disc.json");
            var manifest = Parse(File.ReadAllText(path));
            Assert.Equal(1, manifest.Format);
            Assert.False(string.IsNullOrEmpty(manifest.GameId));
        }

        [Fact]
        public void ReadsRecommendedFormat()
        {
            var manifest = Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "examples", "smart-disc", "iberia-disc.json")));
            Assert.Equal("portal-2", manifest.GameId);
            Assert.Equal("Portal 2", manifest.GameName);
            Assert.Equal("Portal 2 · Iberia Custom Edition", manifest.DisplayName);
            Assert.Equal(DiscType.SmartDisc, manifest.DiscType);
            Assert.Equal(new[] { "620" }, manifest.SteamAppIds);
            Assert.Empty(manifest.GogProductIds);
            Assert.Null(manifest.Epic);
            Assert.Null(manifest.DiscLaunch);
            Assert.Equal("cover.png", manifest.CoverPath);
            Assert.Equal(new[] { "portal2.exe" }, manifest.LocalExecutables);
            Assert.Equal(64, manifest.Sha256.Length);
        }

        [Fact]
        public void AcceptsFlatFormatFromFirstSpecExamples()
        {
            var manifest = Parse("{\"iberiaDisc\": true, \"format\": 1, \"discType\": \"smart-disc\", \"gameId\": \"portal2\", \"name\": \"Portal 2\", \"platforms\": {\"steam\": {\"appId\": \"620\"}}}");
            Assert.Equal("portal2", manifest.GameId);
            Assert.Equal(new[] { "620" }, manifest.SteamAppIds);
        }

        [Fact]
        public void MergesAppIdAndAppIdsWithoutDuplicates()
        {
            var manifest = Parse("{\"iberiaDisc\": true, \"format\": 1, \"game\": {\"id\": \"gta-v\", \"name\": \"GTA V\"}, \"platforms\": {\"steam\": {\"appId\": 271590, \"appIds\": [271590, \"3240220\"]}}}");
            Assert.Equal(new[] { "271590", "3240220" }, manifest.SteamAppIds);
        }

        [Fact]
        public void DataDiscIsInferredFromDiscLaunch()
        {
            var manifest = Parse("{\"iberiaDisc\": true, \"format\": 1, \"game\": {\"id\": \"mi-juego\", \"name\": \"Mi Juego\"}, \"discLaunch\": {\"mode\": \"installer\", \"path\": \"Setup/Instalar.msi\"}}");
            Assert.Equal(DiscType.DataDisc, manifest.DiscType);
            Assert.Equal(DiscLaunchMode.Installer, manifest.DiscLaunch!.Mode);
            Assert.Equal(@"Setup\Instalar.msi", manifest.DiscLaunch.RelativePath);
        }

        [Fact]
        public void UnknownFieldsAreIgnoredForForwardCompatibility()
        {
            var manifest = Parse("{\"iberiaDisc\": true, \"format\": 1, \"game\": {\"id\": \"a\", \"name\": \"A\", \"futuro\": 1}, \"extras\": [{\"x\": 1}]}");
            Assert.Equal("a", manifest.GameId);
        }

        [Fact]
        public void RejectsDiscsThatAreNotOurs()
        {
            Assert.Equal(ManifestError.NotIberiaDisc, Fails("{\"format\": 1, \"game\": {\"id\": \"a\", \"name\": \"A\"}}").Error);
            Assert.Equal(ManifestError.NotIberiaDisc, Fails("{\"iberiaDisc\": \"true\", \"format\": 1}").Error);
        }

        [Fact]
        public void NewerFormatAsksToUpdate()
        {
            var error = Fails("{\"iberiaDisc\": true, \"format\": 2, \"game\": {\"id\": \"a\", \"name\": \"A\"}}");
            Assert.Equal(ManifestError.UnsupportedFormat, error.Error);
            Assert.Contains("Actualiza", error.Message);
        }

        [Theory]
        [InlineData("{\"iberiaDisc\": true, \"format\": \"1\", \"game\": {\"id\": \"a\", \"name\": \"A\"}}", "format")]
        [InlineData("{\"iberiaDisc\": true, \"format\": 1, \"game\": {\"id\": \"Portal 2\", \"name\": \"A\"}}", "game.id")]
        [InlineData("{\"iberiaDisc\": true, \"format\": 1, \"game\": {\"id\": \"a\"}}", "game.name")]
        [InlineData("{\"iberiaDisc\": true, \"format\": 1, \"game\": {\"id\": \"a\", \"name\": \"A\"}, \"platforms\": {\"steam\": {\"appId\": \"620; rm\"}}}", "platforms.steam.appId")]
        [InlineData("{\"iberiaDisc\": true, \"format\": 1, \"game\": {\"id\": \"a\", \"name\": \"A\"}, \"platforms\": {\"epic\": {\"appName\": \"x?action=uninstall\"}}}", "platforms.epic.appName")]
        [InlineData("{\"iberiaDisc\": true, \"format\": 1, \"discType\": \"smart-disc\", \"game\": {\"id\": \"a\", \"name\": \"A\"}, \"discLaunch\": {\"mode\": \"executable\", \"path\": \"a.exe\"}}", "discLaunch")]
        [InlineData("{\"iberiaDisc\": true, \"format\": 1, \"game\": {\"id\": \"a\", \"name\": \"A\"}, \"discLaunch\": {\"mode\": \"script\", \"path\": \"a.bat\"}}", "discLaunch.mode")]
        [InlineData("{\"iberiaDisc\": true, \"format\": 1, \"game\": {\"id\": \"a\", \"name\": \"A\"}, \"discSet\": {\"disc\": 3, \"total\": 2}}", "discSet.disc")]
        [InlineData("{\"iberiaDisc\": true, \"format\": 1, \"game\": {\"id\": \"a\", \"name\": \"A\"}, \"local\": {\"executables\": [\"..\\\\x.exe\"]}}", "local.executables")]
        public void ReportsTheInvalidField(string json, string field)
        {
            var error = Fails(json);
            Assert.Equal(field, error.Field);
        }

        [Theory]
        [InlineData("..\\\\Windows\\\\system32\\\\cmd.exe")]
        [InlineData("Game\\\\..\\\\..\\\\evil.exe")]
        [InlineData("C:\\\\Windows\\\\notepad.exe")]
        [InlineData("\\\\\\\\server\\\\share\\\\x.exe")]
        [InlineData("/etc/x.exe")]
        [InlineData("Game.exe:stream")]
        [InlineData("CON.exe")]
        [InlineData("Game\\\\\\\\Game.exe")]
        [InlineData("Game.bat")]
        [InlineData("Game .exe ")]
        public void DiscLaunchCannotLeaveTheDisc(string path)
        {
            var error = Fails("{\"iberiaDisc\": true, \"format\": 1, \"game\": {\"id\": \"a\", \"name\": \"A\"}, \"discLaunch\": {\"mode\": \"executable\", \"path\": \"" + path + "\"}}");
            Assert.Equal("discLaunch.path", error.Field);
        }

        [Fact]
        public void HashChangesWhenTheManifestChanges()
        {
            var a = Parse("{\"iberiaDisc\": true, \"format\": 1, \"game\": {\"id\": \"a\", \"name\": \"A\"}}");
            var b = Parse("{\"iberiaDisc\": true, \"format\": 1, \"game\": {\"id\": \"a\", \"name\": \"B\"}}");
            Assert.NotEqual(a.Sha256, b.Sha256);
        }

        [Fact]
        public void ReaderHandlesMissingInvalidAndOversizedFiles()
        {
            using (var temp = new TempDir())
            {
                Assert.Equal(ManifestReadStatus.NotFound, ManifestReader.Read(temp.Path).Status);

                temp.File(DiscManifest.FileName, "{ roto");
                var invalid = ManifestReader.Read(temp.Path);
                Assert.Equal(ManifestReadStatus.Invalid, invalid.Status);
                Assert.Equal(ManifestError.Malformed, invalid.Error!.Error);

                temp.File(DiscManifest.FileName, "{\"iberiaDisc\": true, \"format\": 1, \"pad\": \"" + new string('x', DiscManifest.MaxFileBytes) + "\"}");
                Assert.Equal(ManifestError.TooLarge, ManifestReader.Read(temp.Path).Error!.Error);

                File.WriteAllBytes(temp.Combine(DiscManifest.FileName), new byte[] { 0x7B, 0xFF, 0xFE, 0x7D });
                Assert.Equal(ManifestError.Encoding, ManifestReader.Read(temp.Path).Error!.Error);

                temp.File(DiscManifest.FileName, "{\"iberiaDisc\": true, \"format\": 1, \"game\": {\"id\": \"a\", \"name\": \"A\"}}");
                Assert.Equal(ManifestReadStatus.Ok, ManifestReader.Read(temp.Path).Status);
            }
        }

        [Fact]
        public void CombineStaysInsideTheDisc()
        {
            using (var temp = new TempDir())
            {
                string full = DiscRelativePath.Combine(temp.Path, @"Game\Game.exe");
                Assert.StartsWith(temp.Path, full);
                Assert.EndsWith(Path.Combine("Game", "Game.exe"), full);
                Assert.Throws<ManifestException>(() => DiscRelativePath.Combine(temp.Path, @"..\x.exe"));
            }
        }
    }
}
