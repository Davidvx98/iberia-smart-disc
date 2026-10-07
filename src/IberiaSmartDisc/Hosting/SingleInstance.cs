using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using IberiaSmartDisc.Core.Common;
using IberiaSmartDisc.Native;

namespace IberiaSmartDisc.Hosting
{
    /// <summary>Una sola instancia residente por sesión de usuario.</summary>
    internal sealed class SingleInstance : IDisposable
    {
        private const string MutexName = @"Local\IberiaSmartDisc.Instance";

        private readonly Mutex? _mutex;

        private SingleInstance(Mutex? mutex)
        {
            _mutex = mutex;
        }

        public bool IsFirst => _mutex != null;

        public static SingleInstance Acquire()
        {
            var mutex = new Mutex(initiallyOwned: true, MutexName, out bool created);
            if (created) return new SingleInstance(mutex);
            mutex.Dispose();
            return new SingleInstance(null);
        }

        public void Dispose()
        {
            if (_mutex == null) return;
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
            _mutex.Dispose();
        }
    }

    /// <summary>
    /// Envía órdenes a la instancia residente (abrir ventana, salir…). Antes de
    /// enviar nada comprueba que la ventana pertenece de verdad a Iberia Smart
    /// Disc: otro programa podría crear una ventana con el mismo título para
    /// recibir las órdenes y el permiso de ponerse en primer plano.
    /// </summary>
    internal static class CommandChannel
    {
        public static bool TrySend(string message)
        {
            IntPtr window = NativeMethods.FindWindow(null, HostWindow.Caption);
            if (window == IntPtr.Zero) return false;
            NativeMethods.GetWindowThreadProcessId(window, out uint processId);
            if (processId == 0 || !IsOurImage(NativeMethods.GetProcessImagePath(processId))) return false;
            NativeMethods.AllowSetForegroundWindow(processId);
            return NativeMethods.SendCopyData(window, HostWindow.CopyDataTag, message);
        }

        /// <summary>Procesos vivos del ejecutable instalado (o de este mismo, en modo portátil), sin contar este.</summary>
        public static List<Process> FindResidents()
        {
            var result = new List<Process>();
            int self;
            using (var current = Process.GetCurrentProcess())
            {
                self = current.Id;
            }
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(AppInfo.ExeName)))
            {
                string? image = process.Id == self ? null : NativeMethods.GetProcessImagePath((uint)process.Id);
                bool resident = image != null
                    && (PathTools.SamePath(image, AppPaths.InstalledExe) || (AppPaths.Portable && PathTools.SamePath(image, AppPaths.CurrentExe)));
                if (resident)
                {
                    result.Add(process);
                }
                else
                {
                    process.Dispose();
                }
            }
            return result;
        }

        /// <summary>
        /// Pide a la instancia residente que se cierre y espera a ese proceso. No
        /// depende del mutex: si otro programa lo ocupa, no bloquea actualizar ni
        /// desinstalar.
        /// </summary>
        public static bool StopResident(TimeSpan timeout)
        {
            var residents = FindResidents();
            try
            {
                if (residents.Count == 0) return true;
                TrySend("exit");
                var clock = Stopwatch.StartNew();
                foreach (var process in residents)
                {
                    var remaining = timeout - clock.Elapsed;
                    if (remaining < TimeSpan.Zero || !process.WaitForExit((int)remaining.TotalMilliseconds)) return false;
                }
                return true;
            }
            finally
            {
                foreach (var process in residents) process.Dispose();
            }
        }

        private static bool IsOurImage(string? path) =>
            path != null && (PathTools.SamePath(path, AppPaths.InstalledExe) || PathTools.SamePath(path, AppPaths.CurrentExe));
    }
}
