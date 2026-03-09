using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using GDMENUCardManager.Core;
using GDMENUCardManager.Core.Interface;
using SevenZip;

namespace GDMENUCardManager
{
    public class DependencyManager : IDependencyManager
    {
        private Window getMainWindow() => App.Current.MainWindow;

        public IProgressWindow CreateAndShowProgressWindow()
        {
            var p = new ProgressWindow() { Owner = getMainWindow() };
            p.Show();
            return p;
        }

        public GdItem[] GdiShrinkWindowShowDialog(IEnumerable<GdItem> items)
        {
            var w = new GdiShrinkWindow(items) { Owner = getMainWindow() };
            return w.ShowDialog().GetValueOrDefault() ? w.List.Where(x => x.Value).Select(x => x.Key).ToArray() : null;
        }

        public ValueTask<bool> ShowYesNoDialog(string caption, string text)
        {
            return new ValueTask<bool>(MessageBox.Show(getMainWindow(), text, caption, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
        }

        public ValueTask<bool> ShowLockedFilesDialog(Dictionary<string, string> lockedFiles)
        {
            var dialog = new LockedFilesDialog(lockedFiles) { Owner = getMainWindow() };
            var result = dialog.ShowDialog();
            return new ValueTask<bool>(result == true);
        }

        public ValueTask<bool> ShowConfigReadOnlyDialog(string configPath, string error)
        {
            var dialog = new ConfigReadOnlyDialog(configPath, error) { Owner = getMainWindow() };
            var result = dialog.ShowDialog();
            return new ValueTask<bool>(result == true);
        }

        public ValueTask ShowSerialTranslationDialog(IEnumerable<GdItem> translatedItems)
        {
            var itemsList = translatedItems.ToList();
            if (itemsList.Count > 0)
            {
                var dialog = new SerialTranslationDialog(itemsList) { Owner = getMainWindow() };
                dialog.ShowDialog();
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> ShowGdemuTypeDialog()
        {
            var dialog = new GdemuTypeDialog { Owner = getMainWindow() };
            dialog.ShowDialog();
            return new ValueTask<bool>(dialog.IsAuthentic);
        }

        public ValueTask<bool> ShowSpaceWarningDialog(SpaceCheckResult spaceCheck)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Insufficient space on SD card.\n");
            sb.AppendLine("Space needed:");
            sb.AppendLine($"  \u2022 New disc images ({spaceCheck.NewItemCount}): {Helper.FormatBytes(spaceCheck.NewItemsSize)}");
            if (spaceCheck.MenuFolderExists)
            {
                // Old menu will be deleted before new is created - net impact is just wiggle room
                sb.AppendLine($"  \u2022 Menu update buffer: {Helper.FormatBytes(spaceCheck.MenuWiggleRoom)}");
            }
            else
            {
                // No existing menu - need full space for new menu
                sb.AppendLine($"  \u2022 Menu disc image: ~{Helper.FormatBytes(spaceCheck.MenuBaseSize + spaceCheck.MenuWiggleRoom)}");
            }
            sb.AppendLine($"  \u2022 Metadata files: ~{Helper.FormatBytes(spaceCheck.MetadataBuffer)}");
            sb.AppendLine($"  Total: ~{Helper.FormatBytes(spaceCheck.TotalNeeded)}\n");
            sb.AppendLine($"Space available: {Helper.FormatBytes(spaceCheck.AvailableSpace)}");
            if (spaceCheck.SpaceToBeFreed > 0)
            {
                sb.AppendLine($"Space to be freed: {Helper.FormatBytes(spaceCheck.SpaceToBeFreed)}");
                sb.AppendLine($"Effective available: {Helper.FormatBytes(spaceCheck.EffectiveAvailable)}");
            }
            sb.AppendLine($"\nShortfall: ~{Helper.FormatBytes(spaceCheck.Shortfall)}");

            if (spaceCheck.ShrinkingEnabled)
            {
                sb.AppendLine("\nNote: Actual space needed may be less if GDI shrinking reduces file sizes.");
            }
            if (spaceCheck.ContainsCompressedFiles)
            {
                sb.AppendLine("\nNote: Some items are compressed and their uncompressed sizes are estimates.");
            }

            sb.AppendLine("\nDo you want to proceed anyway?");

            var result = MessageBox.Show(
                getMainWindow(),
                sb.ToString(),
                "Insufficient Space",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            return new ValueTask<bool>(result == MessageBoxResult.Yes);
        }

        public ValueTask ShowDiskFullError(string message, string incompleteFolderPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine(message);

            if (!string.IsNullOrEmpty(incompleteFolderPath) && Directory.Exists(incompleteFolderPath))
            {
                sb.AppendLine($"\nThe incomplete folder will be removed:\n{incompleteFolderPath}");

                // Delete the incomplete folder
                try
                {
                    Directory.Delete(incompleteFolderPath, true);
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"\nWarning: Could not delete incomplete folder: {ex.Message}");
                }
            }

            sb.AppendLine("\nPlease free up space on the SD card and try again.");
            sb.AppendLine("\nThe application will now close.");

            MessageBox.Show(
                getMainWindow(),
                sb.ToString(),
                "Disk Full",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            // Exit the application
            Application.Current.Shutdown();

            return ValueTask.CompletedTask;
        }

        public void ExtractArchive(string archivePath, string extractTo)
        {
            using (var extr = new SevenZipExtractor(archivePath))
            {
                extr.PreserveDirectoryStructure = false;
                extr.ExtractArchive(extractTo);
            }
        }

        public Dictionary<string, long> GetArchiveFiles(string archivePath)
        {
            var toReturn = new Dictionary<string, long>();
            using (var compressedfile = new SevenZipExtractor(archivePath))
                foreach (var item in compressedfile.ArchiveFileData.Where(x => !x.IsDirectory))
                    if (!toReturn.ContainsKey(item.FileName))
                        toReturn.Add(item.FileName, (long)item.Size);
            return toReturn;
        }
    }
}
