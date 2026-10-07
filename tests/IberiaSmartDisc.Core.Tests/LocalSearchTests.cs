using System.Linq;
using System.Threading;
using IberiaSmartDisc.Core.Manifest;
using IberiaSmartDisc.Core.Platforms;
using IberiaSmartDisc.Core.Platforms.Local;
using Xunit;

namespace IberiaSmartDisc.Core.Tests
{
    public class LocalSearchTests
    {
        private static DiscManifest Manifest(string name, string local = "") => ManifestParser.Parse(
            "{\"iberiaDisc\": true, \"format\": 1, \"game\": {\"id\": \"juego\", \"name\": \"" + name + "\"}" + local + "}");

        [Fact]
        public void FindsTheExecutableNamedOnTheDisc()
        {
            using (var temp = new TempDir())
            {
                string exe = temp.File("Games/Portal 2/portal2.exe", "");
                temp.File("Games/Portal 2/unins000.exe", "");
                temp.File("Games/Portal 2/bin/otro.exe", "");
                var hits = new LocalGameSearch().Search(
                    Manifest("Portal 2", ", \"local\": {\"executables\": [\"portal2.exe\"]}"), new[] { temp.Combine("Games") }, CancellationToken.None);
                var hit = Assert.Single(hits);
                Assert.Equal(exe, hit.ExecutablePath);
                Assert.Equal(100, hit.Score);
            }
        }

        [Fact]
        public void WithoutHintsIgnoresToolsAndPrefersSimilarNames()
        {
            using (var temp = new TempDir())
            {
                string game = temp.File("Games/Hollow Knight/hollow_knight.exe", "");
                temp.File("Games/Hollow Knight/UnityCrashHandler64.exe", "");
                temp.File("Games/Hollow Knight/launcher.exe", "");
                temp.File("Games/Otro Juego/otro.exe", "");
                var hits = new LocalGameSearch().Search(Manifest("Hollow Knight"), new[] { temp.Combine("Games") }, CancellationToken.None);
                Assert.Equal(game, hits[0].ExecutablePath);
                Assert.Equal(80, hits[0].Score);
                Assert.DoesNotContain(hits, h => h.ExecutablePath.Contains("UnityCrashHandler"));
                Assert.DoesNotContain(hits, h => h.ExecutablePath.Contains("otro.exe"));
            }
        }

        [Fact]
        public void FallsBackToShallowSearchByExecutableName()
        {
            using (var temp = new TempDir())
            {
                string exe = temp.File("Games/HK/Win64/portal2.exe", "");
                var hits = new LocalGameSearch().Search(
                    Manifest("Portal 2", ", \"local\": {\"executables\": [\"PORTAL2.EXE\"]}"), new[] { temp.Combine("Games") }, CancellationToken.None);
                Assert.Equal(exe, Assert.Single(hits).ExecutablePath);
            }
        }

        [Fact]
        public void StopsWhenCancelledOrOverBudget()
        {
            using (var temp = new TempDir())
            {
                temp.File("Games/Portal 2/portal2.exe", "");
                var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                Assert.Empty(new LocalGameSearch().Search(Manifest("Portal 2"), new[] { temp.Combine("Games") }, cancelled.Token));
                Assert.Empty(new LocalGameSearch(maxDirectories: 0).Search(Manifest("Portal 2"), new[] { temp.Combine("Games") }, CancellationToken.None));
            }
        }

        [Fact]
        public void DefaultRootsOnlyIncludeExistingFolders()
        {
            using (var temp = new TempDir())
            {
                string drive = temp.Dir("C");
                temp.Dir("C", "Games");
                var system = new FakeSystem();
                system.Drives.Add(drive);
                var roots = LocalGameSearch.DefaultRoots(system, new[] { temp.Combine("NoExiste") });
                Assert.Equal(new[] { temp.Combine("C", "Games") }, roots.ToArray());
            }
        }
    }
}
