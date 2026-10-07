using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using IberiaSmartDisc.Hosting;
using IberiaSmartDisc.Native;

namespace IberiaSmartDisc.Discs
{
    /// <summary>
    /// Convierte los avisos de volúmenes de Windows en «disco insertado» y
    /// «disco retirado» para unidades CD/DVD/BD, incluidas las USB y las ISO
    /// montadas. Sin sondeo salvo que el usuario active el modo compatibilidad.
    /// </summary>
    internal sealed class OpticalDriveMonitor : IDisposable
    {
        private const int PollMilliseconds = 5000;

        private readonly HostWindow _host;
        private readonly SynchronizationContext _ui;
        private readonly Dictionary<string, uint> _serials = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        private Timer? _poll;
        private int _polling;
        private bool _seeded;

        public OpticalDriveMonitor(HostWindow host)
        {
            _host = host;
            _ui = SynchronizationContext.Current ?? throw new InvalidOperationException("Falta el contexto de interfaz.");
            _host.VolumeArrived += OnVolumeArrived;
            _host.VolumeRemoved += OnVolumeRemoved;
        }

        public event Action<string>? DiscArrived;

        public event Action<string>? DiscRemoved;

        public static IReadOnlyList<string> CurrentOpticalRoots()
        {
            try
            {
                return DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.CDRom).Select(d => d.Name).ToList();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return new string[0];
            }
        }

        public void SetCompatibilityPolling(bool enabled)
        {
            if (enabled && _poll == null)
            {
                _seeded = false;
                _poll = new Timer(Poll, null, 0, PollMilliseconds);
            }
            else if (!enabled && _poll != null)
            {
                _poll.Dispose();
                _poll = null;
            }
        }

        public void Dispose()
        {
            _host.VolumeArrived -= OnVolumeArrived;
            _host.VolumeRemoved -= OnVolumeRemoved;
            _poll?.Dispose();
            _poll = null;
        }

        private void OnVolumeArrived(char letter)
        {
            string root = letter + @":\";
            if (IsOptical(root)) DiscArrived?.Invoke(root);
        }

        private void OnVolumeRemoved(char letter) => DiscRemoved?.Invoke(letter + @":\");

        private static bool IsOptical(string root)
        {
            try
            {
                return new DriveInfo(root).DriveType == DriveType.CDRom;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// Modo compatibilidad: compara el número de serie de cada volumen óptico.
        /// En unidades vacías la consulta falla al instante y no hace girar el lector.
        /// </summary>
        private void Poll(object? state)
        {
            if (Interlocked.Exchange(ref _polling, 1) == 1) return;
            try
            {
                var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var root in CurrentOpticalRoots())
                {
                    uint? serial = NativeMethods.GetVolumeSerial(root);
                    if (serial == null) continue;
                    present.Add(root);
                    bool known = _serials.TryGetValue(root, out uint previous);
                    _serials[root] = serial.Value;
                    if (_seeded && (!known || previous != serial.Value))
                    {
                        _ui.Post(_ => DiscArrived?.Invoke(root), null);
                    }
                }
                foreach (var root in _serials.Keys.Where(r => !present.Contains(r)).ToList())
                {
                    _serials.Remove(root);
                    if (_seeded) _ui.Post(_ => DiscRemoved?.Invoke(root), null);
                }
                _seeded = true;
            }
            finally
            {
                Interlocked.Exchange(ref _polling, 0);
            }
        }
    }
}
