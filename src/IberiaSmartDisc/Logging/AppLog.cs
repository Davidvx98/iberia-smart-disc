using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace IberiaSmartDisc.Logging
{
    /// <summary>
    /// Log mínimo para soporte: un archivo por día en logs\, 14 días como
    /// máximo y 1 MB por archivo. Sustituye la carpeta del usuario por
    /// %USERPROFILE% para no guardar el nombre de la cuenta.
    /// </summary>
    internal sealed class AppLog
    {
        private const long MaxFileBytes = 1024 * 1024;
        private const int KeepDays = 14;

        private readonly object _gate = new object();
        private readonly string _userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        private readonly string? _userProfileShort = ShortProfile();

        public AppLog(string directory, bool enabled)
        {
            Directory = directory;
            Enabled = enabled;
        }

        public string Directory { get; }

        public bool Enabled { get; set; }

        public void Info(string message) => Write("INFO", message);

        public void Warn(string message) => Write("AVISO", message);

        public void Error(string message, Exception? exception = null) =>
            Write("ERROR", exception == null ? message : message + " | " + exception.GetType().Name + ": " + exception.Message);

        public void DeleteAll()
        {
            lock (_gate)
            {
                try
                {
                    if (!System.IO.Directory.Exists(Directory)) return;
                    foreach (var file in System.IO.Directory.GetFiles(Directory, "*.log")) TryDelete(file);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                }
            }
        }

        public void DeleteOld()
        {
            try
            {
                if (!System.IO.Directory.Exists(Directory)) return;
                var limit = DateTime.UtcNow.AddDays(-KeepDays);
                foreach (var file in System.IO.Directory.GetFiles(Directory, "*.log"))
                {
                    if (File.GetLastWriteTimeUtc(file) < limit) TryDelete(file);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        private void Write(string level, string message)
        {
            if (!Enabled) return;
            lock (_gate)
            {
                try
                {
                    System.IO.Directory.CreateDirectory(Directory);
                    var now = DateTime.Now;
                    string path = Path.Combine(Directory, "iberia-smart-disc-" + now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > MaxFileBytes) return;
                    string line = now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " [" + level + "] " + Sanitize(message) + Environment.NewLine;
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // Un log que falla nunca debe tumbar el programa.
                }
            }
        }

        private string Sanitize(string message)
        {
            string clean = message.Replace("\r", " ").Replace("\n", " | ");
            clean = ReplaceIgnoreCase(clean, _userProfile, "%USERPROFILE%");
            return _userProfileShort == null ? clean : ReplaceIgnoreCase(clean, _userProfileShort, "%USERPROFILE%");
        }

        private static string ReplaceIgnoreCase(string text, string? value, string replacement)
        {
            if (string.IsNullOrEmpty(value)) return text;
            int index;
            while ((index = text.IndexOf(value, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                text = text.Substring(0, index) + replacement + text.Substring(index + value!.Length);
            }
            return text;
        }

        /// <summary>Forma 8.3 de la carpeta del usuario (C:\Users\NOMBRE~1), si es distinta.</summary>
        private static string? ShortProfile()
        {
            try
            {
                string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string? shortPath = string.IsNullOrEmpty(profile) ? null : Native.NativeMethods.GetShortPath(profile);
                return shortPath != null && !string.Equals(shortPath, profile, StringComparison.OrdinalIgnoreCase) ? shortPath : null;
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                return null;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }
}
