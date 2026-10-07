using System;
using System.Collections.Generic;
using System.IO;
using IberiaSmartDisc.Core.Platforms;

namespace IberiaSmartDisc.Core.Tests
{
    /// <summary>Carpeta temporal que se borra al terminar el test.</summary>
    internal sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "isd-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Combine(params string[] parts)
        {
            var all = new List<string> { Path };
            all.AddRange(parts);
            return System.IO.Path.Combine(all.ToArray());
        }

        public string Dir(params string[] parts)
        {
            var path = Combine(parts);
            Directory.CreateDirectory(path);
            return path;
        }

        public string File(string relative, string content)
        {
            var path = Combine(relative.Split('/'));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    internal sealed class FakeRegistry : IRegistryReader
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public FakeRegistry Set(RegistryRoot root, string key, string name, string value)
        {
            _values[root + "\\" + key + "\\" + name] = value;
            _keys.Add(root + "\\" + key);
            return this;
        }

        public FakeRegistry AddKey(RegistryRoot root, string key)
        {
            _keys.Add(root + "\\" + key);
            return this;
        }

        public string? GetString(RegistryRoot root, string subKey, string valueName, RegistryBitness view = RegistryBitness.Default) =>
            _values.TryGetValue(root + "\\" + subKey + "\\" + valueName, out var value) ? value : null;

        public bool KeyExists(RegistryRoot root, string subKey, RegistryBitness view = RegistryBitness.Default) =>
            _keys.Contains(root + "\\" + subKey);
    }

    internal sealed class FakeSystem : ISystemInfo
    {
        private readonly Dictionary<KnownFolder, string> _folders = new Dictionary<KnownFolder, string>();

        public List<string> Drives { get; } = new List<string>();

        public FakeSystem With(KnownFolder folder, string path)
        {
            _folders[folder] = path;
            return this;
        }

        public string? GetFolder(KnownFolder folder) => _folders.TryGetValue(folder, out var path) ? path : null;

        public IReadOnlyList<string> GetFixedDriveRoots() => Drives;
    }
}
