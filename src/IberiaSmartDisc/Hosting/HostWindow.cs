using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using IberiaSmartDisc.Native;

namespace IberiaSmartDisc.Hosting
{
    /// <summary>
    /// Ventana invisible que recibe los avisos de Windows. Tiene que ser de
    /// nivel superior: Windows envía los avisos de discos (DBT_DEVICEARRIVAL de
    /// volúmenes) a todas las ventanas de nivel superior, pero no a las ventanas
    /// «solo mensajes» (HWND_MESSAGE). No consume CPU: solo despierta con eventos.
    /// </summary>
    internal sealed class HostWindow : NativeWindow, IDisposable
    {
        public const string Caption = "IberiaSmartDisc.Host.6f1c2a";

        /// <summary>Marca de los mensajes WM_COPYDATA propios.</summary>
        public static readonly IntPtr CopyDataTag = new IntPtr(0x1BE7D15C);

        private const int MaxCommandBytes = 16 * 1024;

        private readonly SynchronizationContext _ui;

        public HostWindow()
        {
            _ui = SynchronizationContext.Current ?? throw new InvalidOperationException("Falta el contexto de interfaz.");
            CreateHandle(new CreateParams { Caption = Caption });
            NativeMethods.RegisterSessionNotifications(Handle);
        }

        public event Action<char>? VolumeArrived;

        public event Action<char>? VolumeRemoved;

        public event Action<string>? CommandReceived;

        public event Action? Resumed;

        public event Action? SessionLocked;

        public event Action? SessionUnlocked;

        public void Dispose()
        {
            if (Handle == IntPtr.Zero) return;
            NativeMethods.UnregisterSessionNotifications(Handle);
            DestroyHandle();
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case NativeMethods.WM_DEVICECHANGE:
                    HandleDeviceChange(m.WParam.ToInt64(), m.LParam);
                    m.Result = new IntPtr(1);
                    return;
                case NativeMethods.WM_COPYDATA:
                    m.Result = HandleCopyData(m.LParam) ? new IntPtr(1) : IntPtr.Zero;
                    return;
                case NativeMethods.WM_WTSSESSION_CHANGE:
                    long change = m.WParam.ToInt64();
                    if (change == NativeMethods.WTS_SESSION_LOCK) _ui.Post(_ => SessionLocked?.Invoke(), null);
                    else if (change == NativeMethods.WTS_SESSION_UNLOCK) _ui.Post(_ => SessionUnlocked?.Invoke(), null);
                    break;
                case NativeMethods.WM_POWERBROADCAST:
                    long power = m.WParam.ToInt64();
                    if (power == NativeMethods.PBT_APMRESUMEAUTOMATIC || power == NativeMethods.PBT_APMRESUMESUSPEND)
                    {
                        _ui.Post(_ => Resumed?.Invoke(), null);
                    }
                    break;
            }
            base.WndProc(ref m);
        }

        private void HandleDeviceChange(long eventType, IntPtr data)
        {
            if (data == IntPtr.Zero) return;
            if (eventType != NativeMethods.DBT_DEVICEARRIVAL && eventType != NativeMethods.DBT_DEVICEREMOVECOMPLETE) return;

            var header = Marshal.PtrToStructure<NativeMethods.DEV_BROADCAST_HDR>(data);
            if (header.DeviceType != NativeMethods.DBT_DEVTYP_VOLUME) return;
            // Solo se lee dbcv_unitmask (desplazamiento 12) y solo si cabe en el tamaño declarado.
            if (header.Size < 16) return;
            int unitMask = Marshal.ReadInt32(data, 12);

            bool arrival = eventType == NativeMethods.DBT_DEVICEARRIVAL;
            for (int bit = 0; bit < 26; bit++)
            {
                if ((unitMask & (1 << bit)) == 0) continue;
                char letter = (char)('A' + bit);
                // Se procesa fuera del mensaje: Windows espera una respuesta rápida.
                _ui.Post(_ => (arrival ? VolumeArrived : VolumeRemoved)?.Invoke(letter), null);
            }
        }

        private bool HandleCopyData(IntPtr data)
        {
            if (data == IntPtr.Zero) return false;
            var copy = Marshal.PtrToStructure<NativeMethods.COPYDATASTRUCT>(data);
            if (copy.dwData != CopyDataTag || copy.lpData == IntPtr.Zero || copy.cbData <= 0 || copy.cbData > MaxCommandBytes) return false;
            string text = Marshal.PtrToStringUni(copy.lpData, copy.cbData / 2).TrimEnd('\0');
            _ui.Post(_ => CommandReceived?.Invoke(text), null);
            return true;
        }
    }
}
