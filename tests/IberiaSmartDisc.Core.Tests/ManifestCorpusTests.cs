using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IberiaSmartDisc.Core.Manifest;
using Xunit;

namespace IberiaSmartDisc.Core.Tests
{
    /// <summary>
    /// Corpus compartido con schema/iberia-disc.schema.json (tools/check-schema.py
    /// comprueba el lado del esquema). Si el esquema acepta algo que el programa
    /// rechaza, la web podría grabar discos que no funcionan.
    /// </summary>
    public class ManifestCorpusTests
    {
        private static string Folder(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

        public static IEnumerable<object[]> Valid() => Files("valid");

        public static IEnumerable<object[]> Invalid() => Files("invalid").Concat(Files("invalid-csharp-only"));

        private static IEnumerable<object[]> Files(string folder) =>
            Directory.GetFiles(Folder(folder), "*.json").OrderBy(f => f, StringComparer.Ordinal).Select(f => new object[] { folder + "/" + Path.GetFileName(f) });

        [Theory]
        [MemberData(nameof(Valid))]
        public void AcceptsValidManifests(string file)
        {
            var manifest = ManifestParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", file)));
            Assert.Equal(1, manifest.Format);
        }

        [Theory]
        [MemberData(nameof(Invalid))]
        public void RejectsInvalidManifests(string file)
        {
            Assert.Throws<ManifestException>(() => ManifestParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", file))));
        }

        [Fact]
        public void CorpusIsNotEmpty()
        {
            Assert.True(Valid().Count() >= 10);
            Assert.True(Invalid().Count() >= 40);
        }
    }
}
