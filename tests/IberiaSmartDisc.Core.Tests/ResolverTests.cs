using System;
using System.Collections.Generic;
using System.Linq;
using IberiaSmartDisc.Core.Configuration;
using IberiaSmartDisc.Core.Manifest;
using IberiaSmartDisc.Core.Platforms;
using IberiaSmartDisc.Core.Resolution;
using Xunit;

namespace IberiaSmartDisc.Core.Tests
{
    public class ResolverTests
    {
        private static readonly DiscManifest Portal = ManifestParser.Parse(
            "{\"iberiaDisc\": true, \"format\": 1, \"game\": {\"id\": \"portal-2\", \"name\": \"Portal 2\"}}");

        private sealed class FakeProvider : IPlatformProvider
        {
            private readonly LaunchCandidate[] _candidates;

            public FakeProvider(LaunchKind kind, bool clientAvailable, params string[] keys)
            {
                Kind = kind;
                ClientAvailable = clientAvailable;
                _candidates = keys.Select(k => new LaunchCandidate(kind, k, LaunchKinds.DisplayName(kind), k, LaunchTarget.ForUri(kind, "test://" + k))).ToArray();
            }

            public LaunchKind Kind { get; }

            public bool ClientAvailable { get; }

            public bool Throws { get; set; }

            public IReadOnlyList<LaunchCandidate> FindInstalled(ResolveContext context)
            {
                if (Throws) throw new InvalidOperationException("registro roto");
                return _candidates;
            }

            public InstallAction? GetInstallAction(ResolveContext context) => null;

            public bool IsClientAvailable(AppSettings settings) => ClientAvailable;

            public IEnumerable<string> Describe(AppSettings settings) => new string[0];
        }

        private static ResolutionResult Resolve(AppSettings settings, GameRecord? record, params IPlatformProvider[] providers) =>
            new GameResolver(providers).Resolve(new ResolveContext(Portal, null, settings), record);

        [Fact]
        public void OrdersCandidatesByPriority()
        {
            var steam = new FakeProvider(LaunchKind.Steam, true, "steam:620");
            var gog = new FakeProvider(LaunchKind.Gog, true, "gog:1");

            var byDefault = Resolve(new AppSettings(), null, gog, steam);
            Assert.Equal(new[] { "steam:620", "gog:1" }, byDefault.Candidates.Select(c => c.Key));

            var settings = new AppSettings();
            settings.SetPriority(new[] { LaunchKind.Gog });
            var custom = Resolve(settings, null, gog, steam);
            Assert.Equal(new[] { "gog:1", "steam:620" }, custom.Candidates.Select(c => c.Key));
            Assert.Null(custom.Preferred);
            Assert.False(custom.PreferredMissing);
        }

        [Fact]
        public void SavedPreferenceWinsWhenItStillExists()
        {
            var record = new GameRecord("portal-2", "Portal 2") { PreferredKey = "gog:1", AlwaysUse = true };
            var result = Resolve(new AppSettings(), record, new FakeProvider(LaunchKind.Steam, true, "steam:620"), new FakeProvider(LaunchKind.Gog, true, "gog:1"));
            Assert.Equal("gog:1", result.Preferred!.Key);
        }

        [Fact]
        public void PreferenceWithoutAlwaysUseOnlyAffectsTheList()
        {
            var record = new GameRecord("portal-2", "Portal 2") { PreferredKey = "gog:1", AlwaysUse = false };
            var result = Resolve(new AppSettings(), record, new FakeProvider(LaunchKind.Gog, true, "gog:1"));
            Assert.Null(result.Preferred);
        }

        [Fact]
        public void ReportsMissingInstallations()
        {
            var record = new GameRecord("portal-2", "Portal 2") { PreferredKey = "steam:620", AlwaysUse = true };

            var uninstalled = Resolve(new AppSettings(), record, new FakeProvider(LaunchKind.Steam, true));
            Assert.Equal("La instalación configurada ya no existe.", uninstalled.PreferredMissingMessage);

            var noClient = Resolve(new AppSettings(), record, new FakeProvider(LaunchKind.Steam, false));
            Assert.Equal("Steam no está disponible en este PC.", noClient.PreferredMissingMessage);
        }

        [Fact]
        public void DiscPreferenceOnAnotherDiscIsNotAnError()
        {
            var record = new GameRecord("portal-2", "Portal 2") { PreferredKey = "disc:Game\\Game.exe", AlwaysUse = true };
            var result = Resolve(new AppSettings(), record, new FakeProvider(LaunchKind.Steam, true, "steam:620"));
            Assert.Null(result.Preferred);
            Assert.False(result.PreferredMissing);
            Assert.Single(result.Candidates);
        }

        [Fact]
        public void RemembersLocalExecutablesThatStillExist()
        {
            using (var temp = new TempDir())
            {
                string exe = temp.File("Juegos/portal2.exe", "");
                var record = new GameRecord("portal-2", "Portal 2") { LocalExecutable = exe, PreferredKey = LaunchCandidate.LocalKey(exe), AlwaysUse = true };
                var result = Resolve(new AppSettings(), record);
                Assert.Equal(exe, result.Preferred!.Target.ExecutablePath);

                record.LocalExecutable = temp.Combine("Juegos", "borrado.exe");
                record.PreferredKey = LaunchCandidate.LocalKey(record.LocalExecutable);
                var gone = Resolve(new AppSettings(), record);
                Assert.Empty(gone.Candidates);
                Assert.True(gone.PreferredMissing);
            }
        }

        [Fact]
        public void BrokenProviderDoesNotHideTheOthers()
        {
            var broken = new FakeProvider(LaunchKind.Epic, true, "epic:x") { Throws = true };
            var result = Resolve(new AppSettings(), null, broken, new FakeProvider(LaunchKind.Steam, true, "steam:620"));
            Assert.Single(result.Candidates);
            Assert.Contains(result.Warnings, w => w.Contains("registro roto"));
        }

        [Fact]
        public void DuplicateKeysAreMerged()
        {
            var result = Resolve(new AppSettings(), null, new FakeProvider(LaunchKind.Steam, true, "steam:620", "STEAM:620"));
            Assert.Single(result.Candidates);
        }
    }
}
