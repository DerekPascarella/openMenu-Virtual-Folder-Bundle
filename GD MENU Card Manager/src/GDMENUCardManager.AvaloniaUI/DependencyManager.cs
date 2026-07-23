using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
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

        public GdItem[] GdiShrinkWindowShowDialog(System.Collections.Generic.IEnumerable<GdItem> items, string title = null) => null;

        public async ValueTask<bool> ShowYesNoDialog(string caption, string text)
        {
            return await MessageBoxManager.GetMessageBoxStandard(caption, text, ButtonEnum.YesNo).ShowWindowDialogAsync(getMainWindow()) == ButtonResult.Yes;
        }

        public async ValueTask ShowWarningDialog(string caption, string text)
        {
            await MessageBoxManager.GetMessageBoxStandard(caption, text, ButtonEnum.Ok, Icon.Warning).ShowWindowDialogAsync(getMainWindow());
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
                // Old menu is deleted before the new one is created, so the net impact is just wiggle room.
                sb.AppendLine($"  \u2022 Menu update buffer: {Helper.FormatBytes(spaceCheck.MenuWiggleRoom)}");
            }
            else
            {
                // No existing menu, so the full space for the new menu is required.
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

            var result = await MessageBoxManager.GetMessageBoxStandard(
                "Confirmation",
                sb.ToString(),
                ButtonEnum.YesNo,
                Icon.Warning).ShowWindowDialogAsync(getMainWindow());

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

            await MessageBoxManager.GetMessageBoxStandard(
                "Error",
                sb.ToString(),
                ButtonEnum.Ok,
                Icon.Error).ShowWindowDialogAsync(getMainWindow());

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

        public byte[] ReadArchiveEntryBytes(string archivePath, string entryName, long maxBytes)
        {
            if (string.IsNullOrEmpty(archivePath) || string.IsNullOrEmpty(entryName) || maxBytes <= 0)
                return null;

            try
            {
                using var stream = File.OpenRead(archivePath);
                using var archive = ArchiveFactory.Open(stream);

                var entry = archive.Entries.FirstOrDefault(e =>
                    !e.IsDirectory &&
                    e.Key != null &&
                    string.Equals(
                        Path.GetFileName(e.Key.Replace('\\', '/')),
                        entryName,
                        StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                    return null;

                using var entryStream = entry.OpenEntryStream();
                using var ms = new MemoryStream();
                var buffer = new byte[8192];
                long remaining = maxBytes;
                while (remaining > 0)
                {
                    int chunk = (int)Math.Min(buffer.Length, remaining);
                    int read = entryStream.Read(buffer, 0, chunk);
                    if (read <= 0)
                        break;
                    ms.Write(buffer, 0, read);
                    remaining -= read;
                }
                return ms.ToArray();
            }
            catch
            {
                return null;
            }
        }
    }
}
