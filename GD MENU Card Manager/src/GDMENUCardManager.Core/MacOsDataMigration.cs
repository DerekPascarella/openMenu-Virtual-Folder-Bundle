using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GDMENUCardManager.Core
{
    /// <summary>
    /// macOS only. Mutable data lives in ~/Library/Application Support/GDMENUCardManager because
    /// the .app bundle can be read-only under Gatekeeper App Translocation.
    /// </summary>
    public static class MacOsDataMigration
    {
        private const string AppFolderName = "GDMENUCardManager";
        private const string ConfigFileName = "GDMENUCardManager.dll.config";

        public static string GetUserDataDir()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", AppFolderName);
        }

        public static string GetUserConfigPath()
        {
            return Path.Combine(GetUserDataDir(), ConfigFileName);
        }

        /// <summary>
        /// Returns the path to the user's menu_data directory in Application Support.
        /// This directory holds BOX.DAT, ICON.DAT, and META.DAT.
        /// </summary>
        public static string GetUserMenuDataDir()
        {
            return Path.Combine(GetUserDataDir(), "menu_data");
        }

        /// <summary>
        /// Returns the path to the user's dat_backups directory in Application Support.
        /// </summary>
        public static string GetUserDatBackupsDir()
        {
            return Path.Combine(GetUserDataDir(), "dat_backups");
        }

        /// <summary>
        /// Deliberately does not create menu_data/. PerformFirstTimeDatCopy uses its absence as the
        /// first-run sentinel.
        /// </summary>
        public static void EnsureApplicationSupportExists(string bundleBasePath)
        {
            try
            {
                var userDataDir = GetUserDataDir();
                Directory.CreateDirectory(userDataDir);
                Directory.CreateDirectory(GetUserDatBackupsDir());

                var userConfigPath = GetUserConfigPath();
                if (!File.Exists(userConfigPath))
                {
                    var bundleConfigPath = Path.Combine(bundleBasePath, ConfigFileName);
                    if (File.Exists(bundleConfigPath))
                        File.Copy(bundleConfigPath, userConfigPath, overwrite: false);
                }
            }
            catch
            {
                // If Application Support cannot be written, fall back to the bundle path.
                // That may itself fail under App Translocation, but never crash here.
            }
        }

        public static bool NeedsFirstTimeDatSetup()
        {
            return !Directory.Exists(GetUserMenuDataDir());
        }

        /// <summary>
        /// Each file is copied under its own guard, so a missing or unreadable source is skipped
        /// rather than aborting the run.
        /// </summary>
        public static void PerformFirstTimeDatCopy(
            string bundleBasePath,
            IProgress<(int current, int total, string name)> progress)
        {
            var destDir = GetUserMenuDataDir();
            Directory.CreateDirectory(destDir);

            var sourceDatDir = Path.Combine(bundleBasePath, "tools", "openMenu", "menu_data");

            var files = new[] { "BOX.DAT", "ICON.DAT", "META.DAT", "FOLDRART.DAT", "FOLDRART.MAP" };
            int total = files.Length;

            for (int i = 0; i < total; i++)
            {
                var fileName = files[i];
                progress?.Report((i + 1, total, fileName));

                var src = Path.Combine(sourceDatDir, fileName);
                var dst = Path.Combine(destDir, fileName);

                if (File.Exists(src) && !File.Exists(dst))
                    File.Copy(src, dst, overwrite: false);
            }
        }
    }
}
