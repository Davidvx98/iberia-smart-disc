using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using IberiaSmartDisc.Core.Common;
using IberiaSmartDisc.Core.Platforms;
using IberiaSmartDisc.Native;

namespace IberiaSmartDisc.Launching
{
    internal enum LaunchStatus
    {
        Started,
        AlreadyRunning,
        Cancelled,
        NotFound,
        Failed,
    }

    internal sealed class LaunchOutcome
    {
        public LaunchOutcome(LaunchStatus status, string? message = null)
        {
            Status = status;
            Message = message;
        }

        public LaunchStatus Status { get; }

        public string? Message { get; }
    }

    internal static class GameLauncher
    {
        private const int ErrorFileNotFound = 2;
        private const int ErrorPathNotFound = 3;
        private const int ErrorNoAssociation = 1155;
        private const int ErrorCancelled = 1223;

        public static LaunchOutcome Launch(LaunchTarget target)
        {
            try
            {
                if (target.Uri != null)
                {
                    // Enlaces de plataforma (steam://, com.epicgames.launcher://, goggalaxy://).
                    using (Process.Start(new ProcessStartInfo(target.Uri) { UseShellExecute = true }))
                    {
                    }
                    return new LaunchOutcome(LaunchStatus.Started);
                }

                string executable = target.ExecutablePath ?? string.Empty;
                if (!File.Exists(executable)) return new LaunchOutcome(LaunchStatus.NotFound, "No se encuentra " + executable);
                if (target.CheckAlreadyRunning && TryActivateRunning(executable)) return new LaunchOutcome(LaunchStatus.AlreadyRunning);

                // UseShellExecute permite que Windows pida permisos si el juego o el instalador los necesita.
                var startInfo = new ProcessStartInfo(executable, target.Arguments ?? string.Empty)
                {
                    UseShellExecute = true,
                    WorkingDirectory = target.WorkingDirectory ?? Path.GetDirectoryName(executable) ?? string.Empty,
                };
                using (Process.Start(startInfo))
                {
                }
                return new LaunchOutcome(LaunchStatus.Started);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                return new LaunchOutcome(LaunchStatus.Cancelled);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorFileNotFound || ex.NativeErrorCode == ErrorPathNotFound)
            {
                return new LaunchOutcome(LaunchStatus.NotFound, ex.Message);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorNoAssociation)
            {
                return new LaunchOutcome(LaunchStatus.Failed, LaunchKinds.DisplayName(target.Kind) + " no está disponible en este PC.");
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is IOException || ex is UnauthorizedAccessException)
            {
                return new LaunchOutcome(LaunchStatus.Failed, ex.Message);
            }
        }

        /// <summary>Si el juego ya está abierto, lo trae al frente en lugar de abrir otra copia.</summary>
        private static bool TryActivateRunning(string executable)
        {
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
            {
                using (process)
                {
                    try
                    {
                        if (!PathTools.SamePath(process.MainModule?.FileName, executable)) continue;
                        IntPtr window = process.MainWindowHandle;
                        if (window != IntPtr.Zero)
                        {
                            if (NativeMethods.IsIconic(window)) NativeMethods.ShowWindow(window, NativeMethods.SW_RESTORE);
                            NativeMethods.SetForegroundWindow(window);
                        }
                        return true;
                    }
                    catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is NotSupportedException)
                    {
                        // Procesos protegidos o que ya terminaron: se ignoran.
                    }
                }
            }
            return false;
        }
    }
}
