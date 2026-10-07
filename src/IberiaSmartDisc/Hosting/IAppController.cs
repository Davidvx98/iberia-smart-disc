using System.Collections.Generic;
using System.Windows.Forms;

namespace IberiaSmartDisc.Hosting
{
    /// <summary>Lo que las ventanas pueden pedir a la instancia residente.</summary>
    internal interface IAppController
    {
        AppState State { get; }

        bool IsPortable { get; }

        void ShowMainWindow();

        void ShowSettings(string? gameId = null);

        void ConfigureGame(string gameId, IWin32Window owner);

        void SetPaused(bool paused);

        void SetStartWithWindows(bool enabled);

        void SetTrayIcon(bool visible);

        void SetCompatibilityPolling(bool enabled);

        void SimulateDisc(string folder);

        IEnumerable<string> DescribePlatforms();

        void RequestUninstall(IWin32Window? owner);

        void ExitApplication();
    }
}
