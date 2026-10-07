using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using IberiaSmartDisc.Core.Platforms;
using Microsoft.Win32;

namespace IberiaSmartDisc.Platform
{
    internal sealed class WindowsRegistryReader : IRegistryReader
    {
        public string? GetString(RegistryRoot root, string subKey, string valueName, RegistryBitness view = RegistryBitness.Default)
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(Hive(root), View(view)))
                using (var key = baseKey.OpenSubKey(subKey))
                {
                    return key?.GetValue(valueName) as string;
                }
            }
            catch (Exception ex) when (ex is SecurityException || ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        public bool KeyExists(RegistryRoot root, string subKey, RegistryBitness view = RegistryBitness.Default)
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(Hive(root), View(view)))
                using (var key = baseKey.OpenSubKey(subKey))
                {
                    return key != null;
                }
            }
            catch (Exception ex) when (ex is SecurityException || ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static RegistryHive Hive(RegistryRoot root) => root switch
        {
            RegistryRoot.LocalMachine => RegistryHive.LocalMachine,
            RegistryRoot.ClassesRoot => RegistryHive.ClassesRoot,
            _ => RegistryHive.CurrentUser,
        };

        private static RegistryView View(RegistryBitness view) => view switch
        {
            RegistryBitness.Registry32 => RegistryView.Registry32,
            RegistryBitness.Registry64 => RegistryView.Registry64,
            _ => RegistryView.Default,
        };
    }

    internal sealed class WindowsSystemInfo : ISystemInfo
    {
        public string? GetFolder(KnownFolder folder)
        {
            var special = folder switch
            {
                KnownFolder.ProgramFiles => Environment.SpecialFolder.ProgramFiles,
                KnownFolder.ProgramFilesX86 => Environment.SpecialFolder.ProgramFilesX86,
                KnownFolder.ProgramData => Environment.SpecialFolder.CommonApplicationData,
                _ => Environment.SpecialFolder.LocalApplicationData,
            };
            var path = Environment.GetFolderPath(special);
            return string.IsNullOrEmpty(path) ? null : path;
        }

        public IReadOnlyList<string> GetFixedDriveRoots()
        {
            try
            {
                // DriveType no lee el medio: es inmediato incluso con discos de red o lectores vacíos.
                return DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed).Select(d => d.Name).ToList();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return new string[0];
            }
        }
    }
}
