using IberiaSmartDisc.Core.Json;
using IberiaSmartDisc.Core.Vdf;
using Xunit;

namespace IberiaSmartDisc.Core.Tests
{
    public class JsonTests
    {
        [Fact]
        public void ParsesObjectsArraysAndEscapes()
        {
            var node = JsonParser.Parse("{\"a\": [1, -2.5e3, true, null], \"b\": \"x\\\"y\\u00e9\\n\", \"c\": {}}");
            Assert.Equal(JsonKind.Object, node.Kind);
            Assert.Equal(4, node.GetArray("a")!.Items.Count);
            Assert.Equal(1, node.GetArray("a")!.Items[0].AsInt64());
            Assert.Equal("x\"yé\n", node.GetString("b"));
            Assert.Equal(JsonKind.Object, node.Get("c")!.Kind);
        }

        [Fact]
        public void AcceptsByteOrderMark()
        {
            var node = JsonParser.Parse(((char)0xFEFF) + "{\"ok\": true}");
            Assert.True(node.GetBoolean("ok"));
        }

        [Theory]
        [InlineData("{\"a\": 1,}")]
        [InlineData("[1, 2,]")]
        [InlineData("{\"a\": 1} // comentario")]
        [InlineData("{'a': 1}")]
        [InlineData("{\"a\": 01}")]
        [InlineData("{\"a\": \"línea\nrota\"}")]
        [InlineData("{\"a\": tru}")]
        [InlineData("{\"a\": 1} {}")]
        [InlineData("")]
        public void RejectsInvalidJson(string text)
        {
            Assert.Throws<JsonParseException>(() => JsonParser.Parse(text));
        }

        [Fact]
        public void RejectsDuplicateKeysWhenAsked()
        {
            Assert.Throws<JsonParseException>(() => JsonParser.Parse("{\"a\": 1, \"a\": 2}"));
            Assert.Equal(2, JsonParser.Parse("{\"a\": 1, \"a\": 2}", rejectDuplicateKeys: false).GetInt64("a"));
        }

        [Fact]
        public void LimitsNestingDepth()
        {
            string deep = new string('[', 100) + new string(']', 100);
            Assert.Throws<JsonParseException>(() => JsonParser.Parse(deep, maxDepth: 64));
        }

        [Fact]
        public void WriterRoundTrips()
        {
            var original = JsonNode.NewObject()
                .Set("texto", "comillas \" barra \\ salto\n y control " + (char)1)
                .Set("numero", 42)
                .Set("nulo", (string?)null)
                .Set("lista", JsonNode.FromStrings(new[] { "a", "b" }));
            var parsed = JsonParser.Parse(JsonWriter.Write(original));
            Assert.Equal(original.GetString("texto"), parsed.GetString("texto"));
            Assert.Equal(42, parsed.GetInt64("numero"));
            Assert.True(parsed.Get("nulo")!.IsNull);
            Assert.Equal(2, parsed.GetArray("lista")!.Items.Count);
        }
    }

    public class VdfTests
    {
        [Fact]
        public void ParsesModernLibraryFolders()
        {
            const string text = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t\t\"apps\"\n\t\t{\n\t\t\t\"620\"\t\t\"12345\"\n\t\t}\n\t}\n}";
            var root = VdfParser.Parse(text);
            var folder = root.Get("LibraryFolders")!.Get("0")!;
            Assert.Equal(@"C:\Program Files (x86)\Steam", folder.GetValue("path"));
            Assert.Equal("12345", folder.Get("apps")!.GetValue("620"));
        }

        [Fact]
        public void ParsesLegacyFormatCommentsAndConditionals()
        {
            const string text = "// cabecera\n\"LibraryFolders\" {\n \"TimeNextStatsReport\" \"123\"\n \"1\" \"D:\\\\SteamLibrary\" [$WIN32]\n}";
            var folders = VdfParser.Parse(text).Get("libraryfolders")!;
            Assert.Equal(@"D:\SteamLibrary", folders.GetValue("1"));
        }

        [Fact]
        public void KeepsUnknownEscapes()
        {
            var root = VdfParser.Parse("\"a\" \"C:\\Juegos\"");
            Assert.Equal(@"C:\Juegos", root.GetValue("a"));
        }

        [Fact]
        public void RejectsUnbalancedBraces()
        {
            Assert.Throws<System.FormatException>(() => VdfParser.Parse("\"a\" { \"b\" \"c\""));
            Assert.Throws<System.FormatException>(() => VdfParser.Parse("}"));
        }
    }
}
