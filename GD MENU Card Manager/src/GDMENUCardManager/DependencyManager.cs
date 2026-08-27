using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using GDMENUCardManager.Core;
using GDMENUCardManager.Core.Interface;
using SharpCompress.Readers;
using SharpCompress.Archives;
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

        public ValueTask<GdItem[]> GdiShrinkWindowShowDialog(IEnumerable<GdItem> items, string title = null)
        {
            var w = new GdiShrinkWindow(items, title) { Owner = getMainWindow() };
            var picked = w.ShowDialog().GetValueOrDefault()
                ? w.List.Where(x => x.Value).Select(x => x.Key).ToArray()
                : null;
            return new ValueTask<GdItem[]>(picked);
        }

        public ValueTask<bool> ShowYesNoDialog(string caption, string text)
        {
            return new ValueTask<bool>(MessageBox.Show(getMainWindow(), text, caption, MessageBoxButton.YesNo, MessageBoxImage.None) == MessageBoxResult.Yes);
        }

        public ValueTask<ArchiveAddMode> ShowArchiveAddModeDialog(int compressedInputCount)
        {
            var dialog = new ArchiveAddModeDialog(compressedInputCount)
            {
                Owner = getMainWindow()
            };
            dialog.ShowDialog();
            return new ValueTask<ArchiveAddMode>(dialog.Result);
        }

        public ValueTask ShowWarningDialog(string caption, string text)
        {
            MessageBox.Show(getMainWindow(), text, caption, MessageBoxButton.OK, MessageBoxImage.None);
            return new ValueTask();
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

            var result = MessageBox.Show(
                getMainWindow(),
                sb.ToString(),
                "Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.None);

            return new ValueTask<bool>(result == MessageBoxResult.Yes);
        }

        public ValueTask ShowDiskFullError(string message, string incompleteFolderPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine(message);

            if (!string.IsNullOrEmpty(incompleteFolderPath) && Directory.Exists(incompleteFolderPath))
            {
                sb.AppendLine($"\nThe incomplete folder will be removed:\n{incompleteFolderPath}");

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
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.None);

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

        public string ExtractArchiveForEntry(
            string archivePath,
            string extractTo,
            ArchiveEntryInfo selectedEntry)
        {
            if (selectedEntry == null)
                throw new ArgumentNullException(nameof(selectedEntry));

            using var archive = new SevenZipExtractor(archivePath);
            var entries = archive.ArchiveFileData
                .Where(item => !item.IsDirectory)
                .ToList();
            var descriptors = entries
                .Select((item, ordinal) => new ArchiveEntryInfo(
                    ordinal,
                    item.FileName,
                    (long)item.Size))
                .ToArray();
            var extractionEntries = ArchiveEntrySelection.SelectForFlatExtraction(
                descriptors,
                selectedEntry);

            Directory.CreateDirectory(extractTo);
            archive.PreserveDirectoryStructure = false;
            archive.ExtractFiles(
                extractTo,
                extractionEntries.Select(entry => entries[entry.Ordinal].Index).ToArray());

            foreach (var entry in extractionEntries)
            {
                string outputPath = Path.Combine(
                    extractTo,
                    ArchiveEntryPath.GetLeafName(entry.FullName));
                if (!File.Exists(outputPath) || new FileInfo(outputPath).Length != entry.Size)
                    throw new InvalidDataException("An archive entry was not extracted completely.");
            }

            return Path.Combine(extractTo, ArchiveEntryPath.GetLeafName(selectedEntry.FullName));
        }

        public IReadOnlyList<ArchiveEntryInfo> GetArchiveEntries(string archivePath)
        {
            using var archive = new SevenZipExtractor(archivePath);
            return archive.ArchiveFileData
                .Where(item => !item.IsDirectory)
                .Select((item, ordinal) => new ArchiveEntryInfo(
                    ordinal,
                    item.FileName,
                    (long)item.Size))
                .ToArray();
        }

        // Decompression-work ceiling for one bounded read inside a solid
        // archive (bytes stored before the entry plus the prefix itself).
        private const long MaxSolidReadWorkBytes = 128L * 1024 * 1024;

        public byte[] ReadArchiveEntryBytes(
            string archivePath,
            ArchiveEntryInfo requestedEntry,
            long maxBytes)
        {
            if (string.IsNullOrEmpty(archivePath) || requestedEntry == null || maxBytes <= 0)
                return null;

            try
            {
                using var stream = File.OpenRead(archivePath);
                using var archive = SharpCompress.Archives.ArchiveFactory.Open(stream);
                if (archive.Type == SharpCompress.Common.ArchiveType.Rar && archive.IsSolid)
                    return null;

                var entries = archive.Entries.Where(entry => !entry.IsDirectory).ToList();
                var entry = entries.ElementAtOrDefault(requestedEntry.Ordinal);

                if (entry == null ||
                    entry.Key == null ||
                    entry.Size != requestedEntry.Size ||
                    !ArchiveEntryPath.HasSameIdentityKey(entry.Key, requestedEntry.FullName))
                    return null;

                long expectedBytes = Math.Min(requestedEntry.Size, maxBytes);

                // Reaching an entry inside a solid block first decompresses
                // everything stored before it. The byte cap alone does not
                // bound that work, so the read is skipped when it would
                // exceed the budget. SharpCompress only reports IsSolid for
                // RAR, so 7z is treated as solid outright (7-Zip archives
                // normally are).
                if (archive.Type == SharpCompress.Common.ArchiveType.SevenZip || archive.IsSolid)
                {
                    long precedingBytes = entries.Take(requestedEntry.Ordinal).Sum(e => e.Size);
                    if (precedingBytes + expectedBytes > MaxSolidReadWorkBytes)
                        return null;
                }

                using var entryStream = entry.OpenEntryStream();
                using var output = new MemoryStream();
                var buffer = new byte[8192];
                while (output.Length < expectedBytes)
                {
                    int chunk = (int)Math.Min(
                        buffer.Length,
                        expectedBytes - output.Length);
                    int read = entryStream.Read(buffer, 0, chunk);
                    if (read <= 0)
                        break;

                    output.Write(buffer, 0, read);
                }

                return output.Length == expectedBytes
                    ? output.ToArray()
                    : null;
            }
            catch
            {
                return null;
            }
        }

    }
}
