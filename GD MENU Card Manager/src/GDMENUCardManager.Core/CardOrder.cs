using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GDMENUCardManager.Core.Interface;

namespace GDMENUCardManager.Core
{
    public enum ReorderOutcome
    {
        Skipped,
        Ordered,
        NotConverged,
        Canceled
    }

    public sealed class ReorderResult
    {
        public ReorderOutcome Outcome { get; }
        public int Passes { get; }

        public ReorderResult(ReorderOutcome outcome, int passes)
        {
            Outcome = outcome;
            Passes = passes;
        }
    }

    /// <summary>
    /// Keeps the card root's stored directory order matching numeric folder order.
    /// GDEMU resolves folders by number and does not need this, so nothing here may
    /// fail a save unless a folder could not be put back where the menu expects it.
    /// </summary>
    public static class CardOrder
    {
        // Lives here rather than in Constants.cs, which UpdateVersion.ps1 regenerates.
        // Uppercase 8.3 so it costs one directory entry and needs no long name.
        public const string StagingFolderName = "GDMCMTMP";

        // Linux and macOS allocate first-fit and land in one pass. The Windows FAT
        // driver fills never-used tail slots before reusing freed ones, which
        // rotates the sequence once and lands clean on the second pass.
        public const int MaxPasses = 3;

        public static string StagingPath(string sdPath) => Path.Combine(sdPath, StagingFolderName);

        /// <summary>
        /// Whether a root folder name is one this app would write itself.
        /// </summary>
        public static bool IsEligibleName(string name)
        {
            if (!int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                return false;
            return number >= 1 && number < 10000 && Manager.FormatFolderNumber(number) == name;
        }

        /// <summary>
        /// Eligible folder names in the order the volume stores them. Never sorted.
        /// </summary>
        public static List<string> NumberedFolders(string sdPath)
        {
            var names = new List<string>();
            foreach (var dir in Directory.EnumerateDirectories(sdPath))
            {
                var name = Path.GetFileName(dir);
                if (IsEligibleName(name))
                    names.Add(name);
            }
            return names;
        }

        public static bool IsOrdered(IReadOnlyList<string> names)
        {
            for (int i = 1; i < names.Count; i++)
            {
                if (int.Parse(names[i], CultureInfo.InvariantCulture) <= int.Parse(names[i - 1], CultureInfo.InvariantCulture))
                    return false;
            }
            return true;
        }

        public static bool IsCardOrdered(string sdPath) => IsOrdered(NumberedFolders(sdPath));

        /// <summary>
        /// Whether enumeration order is the stored order. True only for FAT family
        /// volumes, where the OS walks the directory as laid out on disk.
        /// </summary>
        public static bool IsFatVolume(string sdPath)
        {
            var drive = Helper.GetDriveInfoForPath(sdPath);
            if (drive == null)
                return false;

            try
            {
                if (drive.DriveType == DriveType.Network)
                    return false;

                var format = drive.DriveFormat;
                return format.IndexOf("fat", StringComparison.OrdinalIgnoreCase) >= 0
                    || format.Equals("msdos", StringComparison.OrdinalIgnoreCase)
                    || format.Equals("lifs", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Returns folders an interrupted pass left in staging to the card root, each
        /// under the name it already carries. Never deletes a folder.
        /// </summary>
        /// <returns>One line per folder that could not be restored.</returns>
        public static List<string> RecoverStaged(string sdPath)
        {
            return RecoverStagedDetailed(sdPath).Select(f => $"{f.Path}: {f.Reason}").ToList();
        }

        private static List<(string Path, string Reason)> RecoverStagedDetailed(string sdPath)
        {
            var failures = new List<(string Path, string Reason)>();
            var staging = StagingPath(sdPath);

            if (!Directory.Exists(staging))
                return failures;

            foreach (var dir in Directory.GetDirectories(staging))
            {
                var name = Path.GetFileName(dir);
                var destination = Path.Combine(sdPath, name);

                if (Directory.Exists(destination) || File.Exists(destination))
                {
                    failures.Add((dir, $"\"{name}\" already exists at the card root. Check the card for errors (chkdsk or fsck) before deleting either copy."));
                    continue;
                }

                try
                {
                    Directory.Move(dir, destination);
                }
                catch (Exception ex)
                {
                    // A second load racing this one may have moved it already.
                    if (!Directory.Exists(dir) && Directory.Exists(destination))
                        continue;
                    failures.Add((dir, ex.Message));
                }
            }

            DeleteStagingIfEmpty(staging);
            return failures;
        }

        public static string StrandedMessage(string sdPath, IEnumerable<string> failures)
        {
            return "Some folders could not be moved back to the card root and are still inside \"" + StagingPath(sdPath) + "\":\n\n" +
                string.Join("\n", failures) +
                "\n\nClose anything using the card, then reload it in this app to restore them. Until then those entries will not boot.";
        }

        /// <summary>
        /// Lays the root's numbered folders down in ascending order when they are
        /// not stored that way already. Every folder is at either its root path or
        /// its staging path at all times, always under its own name.
        /// </summary>
        public static async Task<ReorderResult> ReorderAsync(string sdPath)
        {
            if (!Directory.Exists(sdPath))
                throw new Exception(CardGoneMessage(sdPath));

            if (!IsFatVolume(sdPath))
                return new ReorderResult(ReorderOutcome.Skipped, 0);

            var staging = StagingPath(sdPath);
            var (names, stagingBusy) = await Task.Run(() =>
                (NumberedFolders(sdPath), Directory.Exists(staging) && Directory.EnumerateDirectories(staging).Any()));

            if (stagingBusy || IsOrdered(names))
                return new ReorderResult(ReorderOutcome.Skipped, 0);

            try
            {
                await Task.Run(() => Directory.CreateDirectory(staging));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Nothing has moved yet. A full card fails here and fails the save later anyway.
                return new ReorderResult(ReorderOutcome.Skipped, 0);
            }

            var dm = Helper.DependencyManager;
            var ordered = names.OrderBy(n => int.Parse(n, CultureInfo.InvariantCulture)).ToList();
            int total = ordered.Count * 2;
            int processed = 0;
            string text = "Reordering card folders...";
            IProgressWindow progress = null;

            async Task OpenProgress()
            {
                progress = dm.CreateAndShowProgressWindow();
                progress.IsIndeterminate = false;
                progress.TotalItems = total;
                progress.ProcessedItems = processed;
                progress.TextContent = text;
                do { await Task.Delay(50); } while (!progress.IsInitialized);
            }

            void CloseProgress()
            {
                if (progress == null)
                    return;
                progress.AllowClose();
                progress.Close();
                progress = null;
            }

            // Moves one folder, asking the user to retry on a lock. False means the
            // pass must be abandoned: the user canceled, or the card is out of space.
            async Task<bool> MoveAsync(string from, string to, string displayPath)
            {
                while (true)
                {
                    try
                    {
                        await Task.Run(() => MoveExact(from, to));
                        return true;
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        if (!Directory.Exists(sdPath))
                            throw new Exception(CardGoneMessage(sdPath));

                        if (ex is IOException ioEx && Helper.IsDiskFullException(ioEx))
                            return false;

                        CloseProgress();
                        bool retry = await dm.ShowLockedFilesDialog(new Dictionary<string, string> { [displayPath] = ex.Message });
                        if (!retry)
                            return false;
                        await OpenProgress();
                    }
                }
            }

            async Task<ReorderResult> AbandonAsync(int pass)
            {
                CloseProgress();

                List<(string Path, string Reason)> stranded;
                while (true)
                {
                    stranded = await Task.Run(() => RecoverStagedDetailed(sdPath));
                    if (stranded.Count == 0 || !Directory.Exists(sdPath))
                        break;

                    var locked = stranded.ToDictionary(
                        f => Path.Combine(sdPath, Path.GetFileName(f.Path)),
                        f => f.Reason);
                    if (!await dm.ShowLockedFilesDialog(locked))
                        break;
                }

                if (stranded.Count > 0)
                    throw new Exception(StrandedMessage(sdPath, stranded.Select(f => $"{f.Path}: {f.Reason}")));

                return new ReorderResult(ReorderOutcome.Canceled, pass);
            }

            try
            {
                await OpenProgress();

                for (int pass = 1; pass <= MaxPasses; pass++)
                {
                    if (pass > 1)
                    {
                        processed = 0;
                        text = $"Reordering card folders (pass {pass} of {MaxPasses})...";
                        progress.ProcessedItems = processed;
                        progress.TextContent = text;
                    }

                    // Stage with 01 last, so the card is without its menu folder as briefly as possible.
                    for (int i = ordered.Count - 1; i >= 0; i--)
                    {
                        var rootPath = Path.Combine(sdPath, ordered[i]);
                        if (!await MoveAsync(rootPath, Path.Combine(staging, ordered[i]), rootPath))
                            return await AbandonAsync(pass);
                        progress.ProcessedItems = ++processed;
                    }

                    foreach (var name in ordered)
                    {
                        var rootPath = Path.Combine(sdPath, name);
                        if (!await MoveAsync(Path.Combine(staging, name), rootPath, rootPath))
                            return await AbandonAsync(pass);
                        progress.ProcessedItems = ++processed;
                    }

                    if (await Task.Run(() => IsCardOrdered(sdPath)))
                    {
                        await Task.Run(() => DeleteStagingIfEmpty(staging));
                        return new ReorderResult(ReorderOutcome.Ordered, pass);
                    }
                }

                await Task.Run(() => DeleteStagingIfEmpty(staging));
                CloseProgress();
                await dm.ShowWarningDialog("Folder Order",
                    $"The card's folders could not be stored in numeric order after {MaxPasses} attempts.\n\nEvery folder is in place and the card works normally. Only the order the card stores them in is untidy.");
                return new ReorderResult(ReorderOutcome.NotConverged, MaxPasses);
            }
            finally
            {
                CloseProgress();
            }
        }

        // Plain rename that refuses an existing destination on every runtime. The
        // shared Helper.MoveDirectoryAsync deletes the destination first, and .NET 6
        // on Unix lets rename(2) replace an empty directory silently.
        private static void MoveExact(string from, string to)
        {
            if (Directory.Exists(to) || File.Exists(to))
                throw new IOException($"\"{to}\" already exists.");
            Directory.Move(from, to);
        }

        private static void DeleteStagingIfEmpty(string staging)
        {
            try
            {
                if (Directory.Exists(staging) && !Directory.EnumerateDirectories(staging).Any())
                    Directory.Delete(staging, true);
            }
            catch
            {
                // Litter only. The next pass or recovery deletes it.
            }
        }

        private static string CardGoneMessage(string sdPath)
        {
            return $"The SD card is no longer accessible at \"{sdPath}\".\n\nPlease reconnect the SD card and try again.";
        }
    }
}
