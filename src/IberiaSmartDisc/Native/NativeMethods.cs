using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace IberiaSmartDisc.Native
{
    internal static class NativeMethods
    {
        public const int WM_DEVICECHANGE = 0x0219;
        public const int WM_COPYDATA = 0x004A;
        public const int WM_POWERBROADCAST = 0x0218;
        public const int WM_WTSSESSION_CHANGE = 0x02B1;

        public const int WTS_SESSION_LOCK = 0x7;
        public const int WTS_SESSION_UNLOCK = 0x8;
        private const int NOTIFY_FOR_THIS_SESSION = 0;

        /// <summary>Solo System32 al buscar DLL: nada de la carpeta de Descargas.</summary>
        private const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;

        private const int TokenElevationType = 18;
        private const int TokenElevationTypeFull = 2;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        public const int DBT_DEVICEARRIVAL = 0x8000;
        public const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
        public const int DBT_DEVTYP_VOLUME = 0x0002;

        public const int PBT_APMRESUMESUSPEND = 0x0007;
        public const int PBT_APMRESUMEAUTOMATIC = 0x0012;

        public const int SW_RESTORE = 9;

        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        /// <summary>Evita el diálogo «No hay ningún disco en la unidad» al consultar lectores vacíos.</summary>
        public const uint SEM_FAILCRITICALERRORS = 0x0001;
        public const uint SEM_NOOPENFILEERRORBOX = 0x8000;

        private const uint SMTO_BLOCK = 0x0001;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct DEV_BROADCAST_HDR
        {
            public int Size;
            public int DeviceType;
            public int Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DEV_BROADCAST_VOLUME
        {
            public int Size;
            public int DeviceType;
            public int Reserved;
            public int UnitMask;
            public short Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct COPYDATASTRUCT
        {
            public IntPtr dwData;
            public int cbData;
            public IntPtr lpData;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindow(string? className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, ref COPYDATASTRUCT lParam, uint flags, uint timeout, out IntPtr result);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AllowSetForegroundWindow(uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

        [DllImport("kernel32.dll")]
        public static extern uint WTSGetActiveConsoleSessionId();

        [DllImport("kernel32.dll")]
        public static extern uint SetErrorMode(uint mode);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetVolumeInformation(
            string rootPathName,
            StringBuilder? volumeNameBuffer,
            int volumeNameSize,
            out uint volumeSerialNumber,
            out uint maximumComponentLength,
            out uint fileSystemFlags,
            StringBuilder? fileSystemNameBuffer,
            int fileSystemNameSize);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDefaultDllDirectories(uint directoryFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder exeName, ref int size);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetShortPathName(string longPath, StringBuilder shortPath, int bufferSize);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetTokenInformation(IntPtr token, int informationClass, out int information, int length, out int returnLength);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WTSRegisterSessionNotification(IntPtr hWnd, int flags);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

        /// <summary>
        /// Primera instrucción del programa: a partir de aquí las DLL se buscan
        /// solo en System32 y no en la carpeta del .exe (normalmente Descargas).
        /// </summary>
        public static void RestrictDllSearchToSystem32()
        {
            try
            {
                SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32);
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        /// <summary>
        /// Proceso elevado con el UAC activo («Ejecutar como administrador»). Con el
        /// UAC desactivado el tipo es «predeterminado» y no se considera elevación.
        /// </summary>
        public static bool IsElevatedWithUac()
        {
            using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
            {
                return GetTokenInformation(identity.Token, TokenElevationType, out int type, sizeof(int), out _) && type == TokenElevationTypeFull;
            }
        }

        /// <summary>Ruta del ejecutable de otro proceso, o null si no se puede consultar.</summary>
        public static string? GetProcessImagePath(uint processId)
        {
            IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (process == IntPtr.Zero) return null;
            try
            {
                var buffer = new StringBuilder(1024);
                int size = buffer.Capacity;
                return QueryFullProcessImageName(process, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
            }
            finally
            {
                CloseHandle(process);
            }
        }

        public static string? GetShortPath(string path)
        {
            var buffer = new StringBuilder(1024);
            int length = GetShortPathName(path, buffer, buffer.Capacity);
            return length > 0 && length < buffer.Capacity ? buffer.ToString(0, length) : null;
        }

        public static void RegisterSessionNotifications(IntPtr window)
        {
            try
            {
                WTSRegisterSessionNotification(window, NOTIFY_FOR_THIS_SESSION);
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
            }
        }

        public static void UnregisterSessionNotifications(IntPtr window)
        {
            try
            {
                WTSUnRegisterSessionNotification(window);
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
            }
        }

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimum, IntPtr maximum);

        /// <summary>Envía un texto a otra instancia mediante WM_COPYDATA (sin red ni archivos).</summary>
        public static bool SendCopyData(IntPtr window, IntPtr tag, string text)
        {
            IntPtr buffer = Marshal.StringToHGlobalUni(text);
            try
            {
                var data = new COPYDATASTRUCT { dwData = tag, cbData = (text.Length + 1) * 2, lpData = buffer };
                IntPtr sent = SendMessageTimeout(window, WM_COPYDATA, IntPtr.Zero, ref data, SMTO_BLOCK | SMTO_ABORTIFHUNG, 5000, out IntPtr result);
                return sent != IntPtr.Zero && result != IntPtr.Zero;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>Número de serie del volumen o null si la unidad no tiene disco.</summary>
        public static uint? GetVolumeSerial(string root)
        {
            return GetVolumeInformation(root, null, 0, out uint serial, out _, out _, null, 0) ? serial : (uint?)null;
        }

        public static void UseDarkTitleBar(IntPtr hwnd)
        {
            try
            {
                int enabled = 1;
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref enabled, sizeof(int));
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
            }
        }

        public static void UseRoundedCorners(IntPtr hwnd)
        {
            try
            {
                int preference = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
            }
        }

        /// <summary>Barras de desplazamiento oscuras en listas (Windows 10 1809+).</summary>
        public static void UseDarkScrollbars(Control control)
        {
            try
            {
                SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
            }
        }

        /// <summary>Devuelve a Windows la memoria que no se usa mientras el programa espera.</summary>
        public static void TrimWorkingSet()
        {
            try
            {
                SetProcessWorkingSetSize(GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1));
            }
            catch (EntryPointNotFoundException)
            {
            }
        }
    }
}
