using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using MessageBox.Avalonia;
using MessageBox.Avalonia.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GDMENUCardManager.Core;
using GDMENUCardManager.Core.Interface;
using SharpCompress.Readers;
using SharpCompress.Common;
using SharpCompress.Archives;

namespace GDMENUCardManager
{
    public class DependencyManager : IDependencyManager
    {
        private Window getMainWindow() => ((IClassicDesktopStyleApplicationLifetime)App.Current.ApplicationLifetime).MainWindow;

        public IProgressWindow CreateAndShowProgressWindow()
        {
            var p = new ProgressWindow();
            p.Show(getMainWindow());
            return p;
        }

        public GdItem[] GdiShrinkWindowShowDialog(System.Collections.Generic.IEnumerable<GdItem> items) => null;

        public async ValueTask<bool> ShowYesNoDialog(string caption, string text)
        {
            return await MessageBoxManager.GetMessageBoxStandardWindow(caption, text, ButtonEnum.YesNo).ShowDialog(getMainWindow()) == ButtonResult.Yes;
        }

        public async ValueTask<bool> ShowLockedFilesDialog(Dictionary<string, string> lockedFiles)
        {
            var dialog = new LockedFilesDialog(lockedFiles);
            await dialog.ShowDialog(getMainWindow());
            return dialog.Result;
        }

        public async ValueTask<bool> ShowConfigReadOnlyDialog(string configPath, string error)
        {
            var dialog = new ConfigReadOnlyDialog(configPath, error);
            await dialog.ShowDialog(getMainWindow());
            return dialog.Result;
        }

        public async ValueTask ShowSerialTranslationDialog(IEnumerable<GdItem> translatedItems)
        {
            var itemsList = translatedItems.ToList();
            if (itemsList.Count > 0)
            {
                var dialog = new SerialTranslationDialog(itemsList);
                await dialog.ShowDialog(getMainWindow());
            }
        }

        public async ValueTask<bool> ShowGdemuTypeDialog()
        {
            var dialog = new GdemuTypeDialog();
            await dialog.ShowDialog(getMainWindow());
            return dialog.IsAuthentic;
        }

        public async ValueTask<bool> ShowSpaceWarningDialog(SpaceCheckResult spaceCheck)
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

            var result = await MessageBoxManager.GetMessageBoxStandardWindow(
                "Insufficient Space",
                sb.ToString(),
                ButtonEnum.YesNo,
                Icon.Warning).ShowDialog(getMainWindow());

            return result == ButtonResult.Yes;
        }

        public async ValueTask ShowDiskFullError(string message, string incompleteFolderPath)
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

            await MessageBoxManager.GetMessageBoxStandardWindow(
                "Disk Full",
                sb.ToString(),
                ButtonEnum.Ok,
                Icon.Error).ShowDialog(getMainWindow());

            // Exit the application
            var lifetime = App.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            lifetime?.Shutdown();
        }

        public void ExtractArchive(string archivePath, string extractTo)
        {
            var extOptions = new ExtractionOptions()
            {
                ExtractFullPath = false,
                Overwrite = true
            };

            using (var stream = File.OpenRead(archivePath))
            using (var archive = ArchiveFactory.Open(stream))
            using (var reader = archive.ExtractAllEntries())
                reader.WriteAllToDirectory(extractTo, extOptions);
        }

        public Dictionary<string, long> GetArchiveFiles(string archivePath)
        {
            var toReturn = new Dictionary<string, long>();
            using (var stream = File.OpenRead(archivePath))
            using (var archive = ArchiveFactory.Open(stream))
                foreach (var item in archive.Entries)
                    if (!item.IsDirectory && !toReturn.ContainsKey(item.Key))
                        toReturn.Add(item.Key, item.Size);
            return toReturn;
        }
    }
}
