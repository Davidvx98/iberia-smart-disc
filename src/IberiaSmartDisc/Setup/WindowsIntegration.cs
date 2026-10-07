using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace IberiaSmartDisc.Setup
{
    /// <summary>
    /// Inicio con Windows mediante HKCU\...\Run: no requiere administrador, se
    /// activa y desactiva con una sola clave y no deja tareas programadas.
    /// </summary>
    internal static class StartupRegistration
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string ValueName = "IberiaSmartDisc";

        public static string CommandFor(string executable) => "\"" + executable + "\" --background";

        public static bool IsRegistered() => CurrentCommand() != null;

        public static string? CurrentCommand()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
            {
                return key?.GetValue(ValueName) as string;
            }
        }

        /// <summary>
        /// El usuario puede desactivarlo en Administrador de tareas → Aplicaciones
        /// de arranque; Windows lo apunta en StartupApproved con un primer byte impar.
        /// </summary>
        public static bool IsDisabledBySystem()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(ApprovedKey))
            {
                return key?.GetValue(ValueName) is byte[] data && data.Length > 0 && (data[0] & 1) == 1;
            }
        }

        /// <summary>
        /// Escribe la clave Run. Solo cuando el usuario lo pide expresamente
        /// (instalación o interruptor) se retira además el bloqueo que pusiera en
        /// el Administrador de tareas; una actualización o una reparación lo respetan.
        /// </summary>
        public static void Enable(string executable, bool userRequested)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                key.SetValue(ValueName, CommandFor(executable), RegistryValueKind.String);
            }
            if (userRequested && IsDisabledBySystem()) DeleteValue(ApprovedKey);
        }

        public static void Disable() => DeleteValue(RunKey);

        public static void RemoveAll()
        {
            DeleteValue(RunKey);
            DeleteValue(ApprovedKey);
        }

        private static void DeleteValue(string keyPath)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true))
            {
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
    }

    /// <summary>Entrada en Configuración → Aplicaciones instaladas (por usuario, sin admin).</summary>
    internal static class UninstallEntry
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\IberiaSmartDisc";

        public static void Write(string executable)
        {
            long sizeKb = 0;
            try
            {
                sizeKb = new FileInfo(executable).Length / 1024;
            }
            catch (IOException)
            {
            }

            using (var key = Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                key.SetValue("DisplayName", AppInfo.Name);
                key.SetValue("DisplayVersion", AppInfo.VersionText);
                key.SetValue("Publisher", AppInfo.Publisher);
                key.SetValue("DisplayIcon", executable + ",0");
                key.SetValue("InstallLocation", Path.GetDirectoryName(executable) ?? string.Empty);
                key.SetValue("UninstallString", "\"" + executable + "\" --uninstall");
                key.SetValue("QuietUninstallString", "\"" + executable + "\" --uninstall --quiet");
                key.SetValue("URLInfoAbout", AppInfo.WebsiteUrl);
                key.SetValue("HelpLink", AppInfo.RepositoryUrl);
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, Math.Max(1, sizeKb)), RegistryValueKind.DWord);
            }
        }

        public static void Remove()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
            }
            catch (Exception ex) when (ex is SecurityException || ex is UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Acceso directo en el menú Inicio para abrir la ventana aunque se oculte el icono de la bandeja.</summary>
    internal static class StartMenuShortcut
    {
        public static string ShortcutPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppInfo.Name + ".lnk");

        public static bool Exists => File.Exists(ShortcutPath);

        public static void Create(string executable)
        {
            var link = (IShellLinkW)new ShellLink();
            try
            {
                link.SetPath(executable);
                link.SetWorkingDirectory(Path.GetDirectoryName(executable) ?? string.Empty);
                link.SetDescription("Abre tus juegos al insertar un disco de Iberia Custom DVDs");
                link.SetIconLocation(executable, 0);
                Directory.CreateDirectory(Path.GetDirectoryName(ShortcutPath)!);
                ((IPersistFile)link).Save(ShortcutPath, true);
            }
            finally
            {
                Marshal.ReleaseComObject(link);
            }
        }

        public static void Delete()
        {
            try
            {
                if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink
        {
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);

            void GetIDList(out IntPtr idList);

            void SetIDList(IntPtr idList);

            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);

            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maxPath);

            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);

            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int maxPath);

            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);

            void GetHotkey(out short hotkey);

            void SetHotkey(short hotkey);

            void GetShowCmd(out int showCommand);

            void SetShowCmd(int showCommand);

            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxIconPath, out int iconIndex);

            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);

            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);

            void Resolve(IntPtr window, uint flags);

            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }
    }
}
