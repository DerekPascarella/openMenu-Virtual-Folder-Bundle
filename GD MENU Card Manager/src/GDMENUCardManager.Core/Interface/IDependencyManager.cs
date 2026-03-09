using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GDMENUCardManager.Core.Interface
{
    public interface IDependencyManager
    {
        public GdItem[] GdiShrinkWindowShowDialog(System.Collections.Generic.IEnumerable<GdItem> items);
        public IProgressWindow CreateAndShowProgressWindow();
        public ValueTask<bool> ShowYesNoDialog(string caption, string text);

        /// <summary>
        /// Shows a dialog displaying locked files/folders that cannot be accessed.
        /// Returns true if user wants to retry, false to cancel the operation.
        /// </summary>
        public ValueTask<bool> ShowLockedFilesDialog(Dictionary<string, string> lockedFiles);

        /// <summary>
        /// Shows a warning dialog when there is insufficient space on the SD card.
        /// Returns true if user wants to proceed anyway, false to cancel.
        /// </summary>
        public ValueTask<bool> ShowSpaceWarningDialog(SpaceCheckResult spaceCheck);

        /// <summary>
        /// Shows a disk full error dialog with an Exit button.
        /// The incompleteFolderPath parameter indicates the folder that should be deleted.
        /// </summary>
        public ValueTask ShowDiskFullError(string message, string incompleteFolderPath);

        /// <summary>
        /// Shows a dialog informing the user about serial translations that were applied.
        /// The dialog allows the user to revert individual translations if desired.
        /// </summary>
        public ValueTask ShowSerialTranslationDialog(IEnumerable<GdItem> translatedItems);

        /// <summary>
        /// Shows a dialog asking the user to select their GDEMU type (authentic or clone)
        /// so that the correct timing values can be written to GDEMU.INI.
        /// Returns true for authentic, false for clone.
        /// </summary>
        public ValueTask<bool> ShowGdemuTypeDialog();

        /// <summary>
        /// Shows a dialog when the config file is read-only and settings cannot be saved.
        /// Returns true if user wants to retry, false to proceed without saving settings.
        /// </summary>
        public ValueTask<bool> ShowConfigReadOnlyDialog(string configPath, string error);

        public void ExtractArchive(string archivePath, string extractTo);
        public Dictionary<string, long> GetArchiveFiles(string archivePath);
    }

    public interface IProgressWindow
    {
        public void Close();
        /// <summary>
        /// Allow the window to be closed. Must be called before Close().
        /// </summary>
        public void AllowClose();
        public bool IsInitialized { get; }
        public bool IsVisible { get; }
        public int TotalItems { get; set; }
        public int ProcessedItems { get; set; }
        public string TextContent { get; set; }
    }
}