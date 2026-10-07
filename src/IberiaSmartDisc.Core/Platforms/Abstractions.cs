using System.Collections.Generic;

namespace IberiaSmartDisc.Core.Platforms
{
    public enum RegistryRoot
    {
        CurrentUser,
        LocalMachine,
        ClassesRoot,
    }

    /// <summary>
    /// Vista del registro. Steam, Epic y GOG Galaxy son aplicaciones de 32 bits
    /// y escriben en la vista de 32 bits (WOW6432Node) de HKLM.
    /// </summary>
    public enum RegistryBitness
    {
        Default,
        Registry32,
        Registry64,
    }

    /// <summary>Lectura del registro de Windows; en los tests se sustituye por un falso.</summary>
    public interface IRegistryReader
    {
        string? GetString(RegistryRoot root, string subKey, string valueName, RegistryBitness view = RegistryBitness.Default);

        bool KeyExists(RegistryRoot root, string subKey, RegistryBitness view = RegistryBitness.Default);
    }

    public enum KnownFolder
    {
        ProgramFiles,
        ProgramFilesX86,
        ProgramData,
        LocalAppData,
    }

    /// <summary>Carpetas y unidades del sistema; en los tests apuntan a carpetas temporales.</summary>
    public interface ISystemInfo
    {
        string? GetFolder(KnownFolder folder);

        /// <summary>Raíces de las unidades fijas ("C:\", "D:\"…). No toca las unidades ópticas.</summary>
        IReadOnlyList<string> GetFixedDriveRoots();
    }
}
