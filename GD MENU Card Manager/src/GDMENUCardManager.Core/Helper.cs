using GDMENUCardManager.Core.Interface;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace GDMENUCardManager.Core
{
    public static class Helper
    {
        public static IDependencyManager DependencyManager;

        public static Task<string[]> GetDirectoriesAsync(string path)
        {
            return Task.Run(() => Directory.GetDirectories(path));
        }

        public static Task<string[]> GetFilesAsync(string path)
        {
            //skip hidden files on OSX
            return Task.Run(() => Directory.GetFiles(path).Where(x => !Path.GetFileName(x).StartsWith(".")).ToArray());
        }

        public static Task MoveDirectoryAsync(string from, string to)
        {
            return Task.Run(() =>
            {
                if (Directory.Exists(to))
                {
                    Directory.Delete(to, true);
                }
                Directory.Move(from, to);
            });
        }

        public static Task CreateDirectoryAsync(string path)
        {
            return Task.Run(() => Directory.CreateDirectory(path));
        }

        public static Task DeleteDirectoryAsync(string path)
        {
            return Task.Run(() => Directory.Delete(path, true));
        }

        public static Task<bool> DirectoryExistsAsync(string path)
        {
            return Task.Run(() => Directory.Exists(path));
        }

        public static Task MoveFileAsync(string from, string to)
        {
            return Task.Run(() => File.Move(from, to, overwrite: true));
        }

        public static Task DeleteFileAsync(string path)
        {
            return Task.Run(() => File.Delete(path));
        }

        public static Task<bool> FileExistsAsync(string path)
        {
            return Task.Run(() => File.Exists(path));
        }

        public static Task<FileAttributes> GetAttributesAsync(string path)
        {
            return Task.Run(() => File.GetAttributes(path));
        }

        public static Task WriteTextFileAsync(string path, string text)
        {
            return Task.Run(() => File.WriteAllText(path, text));
        }

        public static Task<string> ReadAllTextAsync(string path)
        {
            return Task.Run(() => File.ReadAllText(path));
        }

        public static async Task CopyDirectoryAsync(string sourceDirName, string destDirName,
            System.Collections.Generic.HashSet<string> excludeFiles = null)
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);

            if (!dir.Exists)
                throw new DirectoryNotFoundException("Source directory does not exist or could not be found: " + sourceDirName);

            // If the destination directory doesn't exist, create it.
            if (!Directory.Exists(destDirName))
                await CreateDirectoryAsync(destDirName);

            // Get the files in the directory and copy them to the new location.
            await Task.Run(async () =>
            {
                FileInfo[] files = dir.GetFiles();
                foreach (FileInfo file in files)
                    if (excludeFiles == null || !excludeFiles.Contains(file.Name))
                        file.CopyTo(Path.Combine(destDirName, file.Name), true);

                DirectoryInfo[] dirs = dir.GetDirectories();
                foreach (DirectoryInfo folder in dirs)
                    await CopyDirectoryAsync(Path.Combine(sourceDirName, folder.Name), Path.Combine(destDirName, folder.Name), excludeFiles);
            });
        }

        /// <summary>
        /// Attempts to make a file or directory writable by removing read-only attributes.
        /// Works cross-platform: uses FileAttributes on Windows, chmod on Unix.
        /// Returns true if successful or path was already writable.
        /// </summary>
        public static bool TryMakeWritable(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    {
                        // Try to remove read-only attribute (works on Windows)
                        File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                    }

                    // On Unix, also try chmod if we still can't write
                    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        TryChmodWritable(path);
                    }
                    return true;
                }
                else if (Directory.Exists(path))
                {
                    var dirInfo = new DirectoryInfo(path);
                    if ((dirInfo.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    {
                        dirInfo.Attributes &= ~FileAttributes.ReadOnly;
                    }

                    // On Unix, also try chmod for directory
                    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        TryChmodWritable(path);
                    }
                    return true;
                }
                return true; // Path doesn't exist, nothing to do
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Attempts to add write permission using chmod on Unix systems.
        /// </summary>
        private static void TryChmodWritable(string path)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+w \"{path}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var process = Process.Start(startInfo))
                {
                    process?.WaitForExit(1000); // Wait up to 1 second
                }
            }
            catch
            {
                // Ignore chmod failures - we'll report the original error
            }
        }

        /// <summary>
        /// Attempts to make all files in a directory writable recursively.
        /// </summary>
        public static void TryMakeDirectoryWritable(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
                return;

            // Make the directory itself writable
            TryMakeWritable(directoryPath);

            try
            {
                // Make all files writable
                foreach (var file in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
                {
                    TryMakeWritable(file);
                }

                // Make all subdirectories writable
                foreach (var dir in Directory.GetDirectories(directoryPath, "*", SearchOption.AllDirectories))
                {
                    TryMakeWritable(dir);
                }
            }
            catch
            {
                // Ignore enumeration errors
            }
        }

        /// <summary>
        /// Checks if a file can be opened with write access.
        /// Automatically attempts to make the file writable if it's read-only.
        /// Returns null if accessible, or an error message if not.
        /// </summary>
        public static string CheckFileAccessibility(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return null; // File doesn't exist, so it's not locked

                // First attempt
                try
                {
                    using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        // File is accessible
                    }
                    return null;
                }
                catch (UnauthorizedAccessException)
                {
                    // Try to make file writable and retry
                    TryMakeWritable(filePath);
                }
                catch (IOException)
                {
                    // Could be a sharing violation or read-only, try to make writable
                    TryMakeWritable(filePath);
                }

                // Second attempt after trying to make writable
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    // File is now accessible
                }
                return null;
            }
            catch (IOException ex)
            {
                return ex.Message;
            }
            catch (UnauthorizedAccessException ex)
            {
                return ex.Message;
            }
        }

        /// <summary>
        /// Checks if a directory can be renamed (which means it can be moved/deleted).
        /// Automatically attempts to make the directory writable if it's read-only.
        /// Returns null if accessible, or an error message if locked.
        /// </summary>
        public static string CheckDirectoryCanBeRenamed(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
                return null;

            // Try to rename the directory to a temporary name and back
            // This detects if another process has the directory open (e.g., cmd, explorer)
            var tempName = directoryPath + "_accessibility_check_" + Guid.NewGuid().ToString("N");

            // First attempt
            try
            {
                Directory.Move(directoryPath, tempName);
                Directory.Move(tempName, directoryPath);
                return null;
            }
            catch (IOException)
            {
                // If rename failed but temp exists, try to rename it back
                if (Directory.Exists(tempName) && !Directory.Exists(directoryPath))
                {
                    try { Directory.Move(tempName, directoryPath); } catch { }
                }
                // Try to make directory writable and retry
                TryMakeDirectoryWritable(directoryPath);
            }
            catch (UnauthorizedAccessException)
            {
                if (Directory.Exists(tempName) && !Directory.Exists(directoryPath))
                {
                    try { Directory.Move(tempName, directoryPath); } catch { }
                }
                // Try to make directory writable and retry
                TryMakeDirectoryWritable(directoryPath);
            }

            // Second attempt after trying to make writable
            try
            {
                Directory.Move(directoryPath, tempName);
                Directory.Move(tempName, directoryPath);
                return null;
            }
            catch (IOException ex)
            {
                if (Directory.Exists(tempName) && !Directory.Exists(directoryPath))
                {
                    try { Directory.Move(tempName, directoryPath); } catch { }
                }
                return ex.Message;
            }
            catch (UnauthorizedAccessException ex)
            {
                if (Directory.Exists(tempName) && !Directory.Exists(directoryPath))
                {
                    try { Directory.Move(tempName, directoryPath); } catch { }
                }
                return ex.Message;
            }
        }

        /// <summary>
        /// Checks if all files in a directory can be accessed with write permissions,
        /// and if the directory itself can be renamed/deleted.
        /// Returns a dictionary of inaccessible paths and their error messages.
        /// </summary>
        public static Dictionary<string, string> CheckDirectoryAccessibility(string directoryPath)
        {
            var lockedFiles = new Dictionary<string, string>();

            if (!Directory.Exists(directoryPath))
                return lockedFiles; // Directory doesn't exist, nothing is locked

            // First check if the directory itself can be renamed (tests for open handles)
            var dirError = CheckDirectoryCanBeRenamed(directoryPath);
            if (dirError != null)
            {
                lockedFiles[directoryPath] = dirError;
                return lockedFiles; // No need to check files if directory is locked
            }

            try
            {
                // Check all files in the directory
                foreach (var file in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
                {
                    var error = CheckFileAccessibility(file);
                    if (error != null)
                    {
                        lockedFiles[file] = error;
                    }
                }
            }
            catch (IOException ex)
            {
                // Directory itself might be inaccessible
                lockedFiles[directoryPath] = ex.Message;
            }
            catch (UnauthorizedAccessException ex)
            {
                lockedFiles[directoryPath] = ex.Message;
            }

            return lockedFiles;
        }

        /// <summary>
        /// Checks accessibility for multiple paths (files and directories).
        /// Returns a dictionary of inaccessible paths and their error messages.
        /// </summary>
        public static async Task<Dictionary<string, string>> CheckPathsAccessibilityAsync(IEnumerable<string> paths)
        {
            return await CheckPathsAccessibilityAsync(paths, null);
        }

        /// <summary>
        /// Checks accessibility for multiple paths (files and directories) with progress reporting.
        /// Returns a dictionary of inaccessible paths and their error messages.
        /// </summary>
        public static async Task<Dictionary<string, string>> CheckPathsAccessibilityAsync(IEnumerable<string> paths, Interface.IProgressWindow progress)
        {
            var pathList = paths.Where(p => !string.IsNullOrEmpty(p)).ToList();

            if (progress != null)
            {
                progress.TotalItems = pathList.Count;
                progress.ProcessedItems = 0;
            }

            var lockedPaths = new Dictionary<string, string>();

            foreach (var path in pathList)
            {
                await Task.Run(() =>
                {
                    try
                    {
                        if (File.Exists(path))
                        {
                            var error = CheckFileAccessibility(path);
                            if (error != null)
                            {
                                lockedPaths[path] = error;
                            }
                        }
                        else if (Directory.Exists(path))
                        {
                            var dirLocked = CheckDirectoryAccessibility(path);
                            foreach (var kvp in dirLocked)
                            {
                                lockedPaths[kvp.Key] = kvp.Value;
                            }
                        }
                        // If path doesn't exist, it's not locked
                    }
                    catch (Exception ex)
                    {
                        lockedPaths[path] = ex.Message;
                    }
                });

                if (progress != null)
                {
                    progress.ProcessedItems++;
                }
            }

            return lockedPaths;
        }

        public static string RemoveDiacritics(string text)
        {
            //from https://stackoverflow.com/questions/249087/how-do-i-remove-diacritics-accents-from-a-string-in-net

            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder();

            foreach (var c in normalizedString)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }

        public static bool IsValidPrintableAscii(string text)
        {
            if (string.IsNullOrEmpty(text))
                return true;
            return text.All(c => c >= 0x20 && c <= 0x7E);
        }

        public static string StripNonPrintableAscii(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            return new string(text.Where(c => c >= 0x20 && c <= 0x7E).ToArray());
        }

        internal static System.Func<string, bool> CompressedFileExpression;// = new System.Func<string, bool>(x => x.EndsWith(".7z", StringComparison.InvariantCultureIgnoreCase) || x.EndsWith(".rar", StringComparison.InvariantCultureIgnoreCase) || x.EndsWith(".zip", StringComparison.InvariantCultureIgnoreCase));

        /// <summary>
        /// Gets the total size of all files in a directory and its subdirectories.
        /// Returns 0 if the directory doesn't exist.
        /// </summary>
        public static long GetDirectorySize(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
                return 0;

            try
            {
                var dirInfo = new DirectoryInfo(directoryPath);
                return dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Formats a byte count as a human-readable string (e.g., "1.5 GB").
        /// Negative values are handled by showing the absolute value with a negative sign.
        /// </summary>
        public static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int suffixIndex = 0;
            string sign = bytes < 0 ? "-" : "";
            double size = Math.Abs((double)bytes);

            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }

            return $"{sign}{size:0.##} {suffixes[suffixIndex]}";
        }

        /// <summary>
        /// Checks if an IOException is due to disk full (no space left on device).
        /// </summary>
        public static bool IsDiskFullException(IOException ex)
        {
            // Windows error codes
            const int ERROR_DISK_FULL = 0x70;        // 112
            const int ERROR_HANDLE_DISK_FULL = 0x27; // 39

            // Get the HResult and extract the Win32 error code
            int hr = ex.HResult;
            int errorCode = hr & 0xFFFF;

            if (errorCode == ERROR_DISK_FULL || errorCode == ERROR_HANDLE_DISK_FULL)
                return true;

            // Also check the message for cross-platform compatibility
            var message = ex.Message.ToLowerInvariant();
            return message.Contains("no space left") ||
                   message.Contains("not enough space") ||
                   message.Contains("disk full") ||
                   message.Contains("insufficient disk space");
        }
    }
}
