using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GDMENUCardManager.Core
{
    /// <summary>
    /// Handles macOS-specific Application Support directory setup and migration.
    /// On macOS, mutable user data (DAT files, settings) must live in
    /// ~/Library/Application Support/GDMENUCardManager/ rather than inside the .app bundle,
    /// because the bundle may be read-only under Gatekeeper App Translocation.
    /// All methods in this class are safe to call only when running on macOS.
    /// </summary>
    public static class MacOsDataMigration
    {
        private const string AppFolderName = "GDMENUCardManager";
        private const string ConfigFileName = "GDMENUCardManager.dll.config";

        /// <summary>
        /// Returns ~/Library/Application Support/GDMENUCardManager
        /// </summary>
        public static string GetUserDataDir()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", AppFolderName);
        }

        /// <summary>
        /// Returns the path to the user's settings config file in Application Support.
        /// </summary>
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
        /// Ensures the Application Support directory structure exists and that the
        /// settings config file has been seeded from the bundle defaults.
        /// Creates: UserDataDir/ and UserDataDir/dat_backups/
        /// Does NOT create menu_data/ - that is PerformFirstTimeDatCopy's job (used as sentinel).
        /// This method is entirely wrapped in try-catch for graceful degradation.
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
                // Graceful degradation: if Application Support cannot be created/written to,
                // the app will fall back to reading/writing from the bundle path.
                // This may fail under App Translocation, but we must not crash here.
            }
        }

        /// <summary>
        /// Returns true if the first-time DAT file copy to Application Support has not yet
        /// been performed. Uses the existence of the menu_data directory as the sentinel.
        /// </summary>
        public static bool NeedsFirstTimeDatSetup()
        {
            return !Directory.Exists(GetUserMenuDataDir());
        }

        /// <summary>
        /// Copies BOX.DAT, ICON.DAT, and META.DAT from the bundle's tools/openMenu/menu_data/
        /// directory to ~/Library/Application Support/GDMENUCardManager/menu_data/.
        /// Creates the menu_data directory (the sentinel for NeedsFirstTimeDatSetup).
        /// Reports progress as (current, total, filename).
        /// Safe to call even if source files are missing - each copy is individually guarded.
        /// </summary>
        public static void PerformFirstTimeDatCopy(
            string bundleBasePath,
            IProgress<(int current, int total, string name)> progress)
        {
            var destDir = GetUserMenuDataDir();
            Directory.CreateDirectory(destDir);

            var sourceDatDir = Path.Combine(bundleBasePath, "tools", "openMenu", "menu_data");

            var files = new[] { "BOX.DAT", "ICON.DAT", "META.DAT" };
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
