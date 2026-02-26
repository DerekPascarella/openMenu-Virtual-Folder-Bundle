using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace GDMENUCardManager.Core
{
    public enum ManualUpdateReason
    {
        None,
        UnsupportedPlatform,
        KillSwitch
    }

    public class UpdateCheckResult
    {
        public bool UpdateAvailable { get; set; }
        public bool ManualUpdateRequired { get; set; }
        public ManualUpdateReason ManualReason { get; set; }
        public string LatestTag { get; set; }
        public string LatestVersion { get; set; }
    }

    public class DownloadProgress
    {
        public long BytesRead { get; set; }
        public long TotalBytes { get; set; }
        public double SpeedBytesPerSecond { get; set; }
    }

    public class PreservationOptions
    {
        public bool PreserveDats { get; set; } = true;
        public bool PreserveThemes { get; set; }
        public bool PreserveCheats { get; set; }
        public bool PreserveSettings { get; set; } = true;
    }

    public static class UpdateManager
    {
        private static readonly HttpClient _client;
        private const string DefaultRepo = "DerekPascarella/openMenu-Virtual-Folder-Bundle";
        private const string StagingDirName = "GDMENUCardManager_update";
        private const string AutoUpdateKillSwitch = "This release cannot be auto-updated.";
        private const string WindowsScriptName = "_gdmenu_updater.bat";
        private const string UnixScriptName = "_gdmenu_updater.sh";

        // Override update repo for testing (leave null/empty for production)
        public static string RepoOverride { get; set; }

        static UpdateManager()
        {
            _client = new HttpClient();
            _client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("GDMENUCardManager-UpdateCheck/1.0");
        }

        private static string GetRepoPath()
        {
            return string.IsNullOrWhiteSpace(RepoOverride) ? DefaultRepo : RepoOverride;
        }

        private static Version ParseVersion(string versionString)
        {
            if (string.IsNullOrWhiteSpace(versionString))
                return new Version(0, 0);

            var cleaned = versionString.TrimStart('v');
            var hyphenIndex = cleaned.IndexOf('-');
            if (hyphenIndex > 0)
                cleaned = cleaned.Substring(0, hyphenIndex);

            return Version.TryParse(cleaned, out var v) ? v : new Version(0, 0);
        }

        public static async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
        {
            var result = new UpdateCheckResult();

            try
            {
                var repoPath = GetRepoPath();
                var url = $"https://api.github.com/repos/{repoPath}/releases/latest";

                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(10));

                    using (var response = await _client.GetAsync(url, cts.Token))
                    {
                        response.EnsureSuccessStatusCode();
                        using (var stream = await response.Content.ReadAsStreamAsync())
                        {
                            var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
                            var tagName = doc.RootElement.GetProperty("tag_name").GetString();

                            var body = "";
                            if (doc.RootElement.TryGetProperty("body", out var bodyElement))
                                body = bodyElement.GetString() ?? "";

                            result.LatestTag = tagName;
                            result.LatestVersion = "v" + tagName;

                            var currentVersion = ParseVersion(Constants.Version);
                            var latestVersion = ParseVersion(tagName);

                            var isNewer = latestVersion > currentVersion;
                            var killSwitchActive = body.Contains(AutoUpdateKillSwitch, StringComparison.OrdinalIgnoreCase);
                            var isMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

                            result.UpdateAvailable = isNewer && !killSwitchActive && !isMacOS;
                            result.ManualUpdateRequired = isNewer && (killSwitchActive || isMacOS);
                            if (result.ManualUpdateRequired)
                                result.ManualReason = isMacOS ? ManualUpdateReason.UnsupportedPlatform : ManualUpdateReason.KillSwitch;
                        }
                    }
                }
            }
            catch
            {
                // swallow network/parse errors
                result.UpdateAvailable = false;
            }

            return result;
        }

        private static string GetAssetSuffix()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return RuntimeInformation.ProcessArchitecture == Architecture.X86
                    ? "win-x86.zip"
                    : "win-x64.zip";
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return "osx-x64-AppBundle.tar.gz";
            return "linux-x64.tar.gz";
        }

        private static string GetAssetUrl(string tag)
        {
            var repoPath = GetRepoPath();
            var suffix = GetAssetSuffix();
            var assetVersion = "v" + tag;
            return $"https://github.com/{repoPath}/releases/download/{tag}/GDMENUCardManager.{assetVersion}-{suffix}";
        }

        private static string GetStagingDir()
        {
            return Path.Combine(Path.GetTempPath(), StagingDirName);
        }

        public static async Task DownloadUpdateAsync(string tag, IProgress<DownloadProgress> progress, CancellationToken cancellationToken)
        {
            var stagingDir = GetStagingDir();
            var downloadDir = Path.Combine(stagingDir, "download");

            // Clean up any previous staging
            if (Directory.Exists(stagingDir))
                Directory.Delete(stagingDir, true);

            Directory.CreateDirectory(downloadDir);

            var url = GetAssetUrl(tag);
            var suffix = GetAssetSuffix();
            var assetVersion = "v" + tag;
            var fileName = $"GDMENUCardManager.{assetVersion}-{suffix}";
            var downloadPath = Path.Combine(downloadDir, fileName);

            try
            {
                using (var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1;

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
                    {
                        var buffer = new byte[65536];
                        long bytesRead = 0;
                        int read;
                        var sw = Stopwatch.StartNew();
                        long lastReportBytes = 0;
                        double lastReportTime = 0;

                        while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, read, cancellationToken);
                            bytesRead += read;

                            var elapsed = sw.Elapsed.TotalSeconds;
                            if (elapsed - lastReportTime >= 0.25 || bytesRead == totalBytes)
                            {
                                var speed = (elapsed - lastReportTime) > 0
                                    ? (bytesRead - lastReportBytes) / (elapsed - lastReportTime)
                                    : 0;
                                lastReportBytes = bytesRead;
                                lastReportTime = elapsed;

                                progress?.Report(new DownloadProgress
                                {
                                    BytesRead = bytesRead,
                                    TotalBytes = totalBytes,
                                    SpeedBytesPerSecond = speed
                                });
                            }
                        }
                    }
                }
            }
            catch
            {
                // Clean up on failure
                CleanupStagingDirectory();
                throw;
            }
        }

        public static async Task ExtractUpdateAsync(string tag, CancellationToken cancellationToken)
        {
            var stagingDir = GetStagingDir();
            var downloadDir = Path.Combine(stagingDir, "download");
            var extractedDir = Path.Combine(stagingDir, "extracted");

            Directory.CreateDirectory(extractedDir);

            var suffix = GetAssetSuffix();
            var assetVersion = "v" + tag;
            var fileName = $"GDMENUCardManager.{assetVersion}-{suffix}";
            var archivePath = Path.Combine(downloadDir, fileName);

            try
            {
                if (suffix.EndsWith(".zip"))
                {
                    await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, extractedDir), cancellationToken);
                }
                else
                {
                    // tar.gz
                    await ExtractTarGzAsync(archivePath, extractedDir, cancellationToken);
                }
            }
            catch
            {
                CleanupStagingDirectory();
                throw;
            }
        }

        private static async Task ExtractTarGzAsync(string archivePath, string extractedDir, CancellationToken cancellationToken)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "tar",
                    Arguments = $"-xzf \"{archivePath}\" -C \"{extractedDir}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(psi))
                {
                    if (process != null)
                    {
                        await process.WaitForExitAsync(cancellationToken);
                        if (process.ExitCode == 0)
                            return;
                    }
                }
            }
            catch
            {
                // tar not available, fall through
            }

            throw new Exception("Could not extract tar.gz archive. Please ensure 'tar' is available on your system.");
        }

        public static async Task ApplyPreservationOptionsAsync(PreservationOptions options)
        {
            var stagingDir = GetStagingDir();
            var extractedDir = Path.Combine(stagingDir, "extracted");

            // Find where the app files actually are (might be in a subfolder)
            var contentRoot = FindContentRoot(extractedDir);
            if (contentRoot == null)
                throw new Exception("Could not find application files in the extracted archive.");

            // Flatten if content is nested
            if (contentRoot != extractedDir)
            {
                var tempMove = Path.Combine(stagingDir, "content_temp");
                Directory.Move(contentRoot, tempMove);
                // Delete the now-empty extracted dir structure
                if (Directory.Exists(extractedDir))
                    Directory.Delete(extractedDir, true);
                Directory.Move(tempMove, extractedDir);
            }

            await Task.Run(() =>
            {
                var menuDataPath = Path.Combine(extractedDir, "tools", "openMenu", "menu_data");

                if (options.PreserveDats)
                {
                    DeleteIfExists(Path.Combine(menuDataPath, "BOX.DAT"));
                    DeleteIfExists(Path.Combine(menuDataPath, "META.DAT"));
                    DeleteIfExists(Path.Combine(menuDataPath, "ICON.DAT"));
                }

                if (options.PreserveThemes)
                {
                    var themePath = Path.Combine(menuDataPath, "theme");
                    if (Directory.Exists(themePath))
                        Directory.Delete(themePath, true);
                }

                if (options.PreserveCheats)
                {
                    var cheatsPath = Path.Combine(menuDataPath, "CHEATS");
                    if (Directory.Exists(cheatsPath))
                        Directory.Delete(cheatsPath, true);
                }

                if (options.PreserveSettings)
                {
                    MergeSettings(extractedDir);
                }
            });
        }

        private static string FindContentRoot(string extractedDir)
        {
            if (HasAppFiles(extractedDir))
                return extractedDir;

            foreach (var subDir in Directory.GetDirectories(extractedDir))
            {
                if (HasAppFiles(subDir))
                    return subDir;

                var macosDir = Path.Combine(subDir, "Contents", "MacOS");
                if (Directory.Exists(macosDir) && HasAppFiles(macosDir))
                    return macosDir;
            }

            foreach (var subDir in Directory.GetDirectories(extractedDir))
            {
                foreach (var subSubDir in Directory.GetDirectories(subDir))
                {
                    if (HasAppFiles(subSubDir))
                        return subSubDir;
                }
            }

            return null;
        }

        private static bool HasAppFiles(string dir)
        {
            return File.Exists(Path.Combine(dir, "GDMENUCardManager.exe")) ||
                   File.Exists(Path.Combine(dir, "GDMENUCardManager")) ||
                   File.Exists(Path.Combine(dir, "GDMENUCardManager.dll"));
        }

        private static void MergeSettings(string extractedDir)
        {
            var appDir = AppContext.BaseDirectory;

            var currentConfigPath = Path.Combine(appDir, "GDMENUCardManager.dll.config");
            if (!File.Exists(currentConfigPath))
                currentConfigPath = Path.Combine(appDir, "GDMENUCardManager.exe.config");
            if (!File.Exists(currentConfigPath))
                currentConfigPath = Path.Combine(appDir, "App.config");

            var newConfigPath = Path.Combine(extractedDir, "GDMENUCardManager.dll.config");
            if (!File.Exists(newConfigPath))
                newConfigPath = Path.Combine(extractedDir, "GDMENUCardManager.exe.config");
            if (!File.Exists(newConfigPath))
                newConfigPath = Path.Combine(extractedDir, "App.config");

            if (!File.Exists(currentConfigPath) || !File.Exists(newConfigPath))
            {
                DeleteAllConfigs(extractedDir);
                return;
            }

            try
            {
                var currentDoc = XDocument.Load(currentConfigPath);
                var newDoc = XDocument.Load(newConfigPath);

                var currentSettings = currentDoc.Descendants("appSettings").FirstOrDefault();
                var newSettings = newDoc.Descendants("appSettings").FirstOrDefault();

                if (currentSettings == null || newSettings == null)
                {
                    DeleteAllConfigs(extractedDir);
                    return;
                }

                var currentKeys = currentSettings.Elements("add")
                    .ToDictionary(e => e.Attribute("key")?.Value ?? "", e => e.Attribute("value")?.Value ?? "");
                var newKeys = newSettings.Elements("add")
                    .ToDictionary(e => e.Attribute("key")?.Value ?? "", e => e.Attribute("value")?.Value ?? "");

                foreach (var kvp in newKeys)
                {
                    if (!currentKeys.ContainsKey(kvp.Key))
                    {
                        currentSettings.Add(new XElement("add",
                            new XAttribute("key", kvp.Key),
                            new XAttribute("value", kvp.Value)));
                    }
                }

                var mergedPath = Path.Combine(Path.GetDirectoryName(newConfigPath), Path.GetFileName(newConfigPath) + ".merged");
                currentDoc.Save(mergedPath);

                DeleteAllConfigs(extractedDir);

                var targetConfigName = Path.GetFileName(currentConfigPath);
                File.Move(mergedPath, Path.Combine(extractedDir, targetConfigName));
            }
            catch
            {
                DeleteAllConfigs(extractedDir);
            }
        }

        private static void DeleteAllConfigs(string extractedDir)
        {
            DeleteIfExists(Path.Combine(extractedDir, "GDMENUCardManager.dll.config"));
            DeleteIfExists(Path.Combine(extractedDir, "GDMENUCardManager.exe.config"));
            DeleteIfExists(Path.Combine(extractedDir, "App.config"));
        }

        public static void LaunchUpdaterAndExit()
        {
            var stagingDir = GetStagingDir();
            var extractedDir = Path.Combine(stagingDir, "extracted");
            var appDir = AppContext.BaseDirectory;
            var pid = Process.GetCurrentProcess().Id;
            var processName = Process.GetCurrentProcess().ProcessName;

            string scriptPath;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                scriptPath = Path.Combine(Path.GetTempPath(), WindowsScriptName);
                var script = GenerateWindowsScript(pid, processName, extractedDir, appDir);
                File.WriteAllText(scriptPath, script);

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{scriptPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            else
            {
                scriptPath = Path.Combine(Path.GetTempPath(), UnixScriptName);
                var script = GenerateUnixScript(pid, extractedDir, appDir);
                File.WriteAllText(scriptPath, script);

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "chmod",
                        Arguments = $"+x \"{scriptPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    })?.WaitForExit(2000);
                }
                catch { }

                Process.Start(new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"\"{scriptPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }

            Environment.Exit(0);
        }

        private static string GenerateWindowsScript(int pid, string processName, string extractedDir, string appDir)
        {
            var escaped_extracted = extractedDir.Replace("/", "\\");
            var escaped_app = appDir.TrimEnd('\\').Replace("/", "\\");

            return $@"@echo off
:waitloop
tasklist /FI ""PID eq {pid}"" 2>NUL | find /I ""{processName}"" >NUL
if not errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto waitloop
)

xcopy /E /Y ""{escaped_extracted}\*"" ""{escaped_app}\""

rmdir /S /Q ""{escaped_extracted}""
rmdir /S /Q ""{GetStagingDir().Replace("/", "\\")}""

start """" ""{Path.Combine(escaped_app, "GDMENUCardManager.exe")}""

del ""%~f0""
";
        }

        private static string GenerateUnixScript(int pid, string extractedDir, string appDir)
        {
            var escaped_app = appDir.TrimEnd('/');

            return $@"#!/bin/bash

# Wait for the app to exit
while kill -0 {pid} 2>/dev/null; do
    sleep 1
done

# Copy staged files over current install
cp -rf ""{extractedDir}/""* ""{escaped_app}/""

# Clean up staging directory
rm -rf ""{GetStagingDir()}""

# Fix permissions
chmod +x ""{escaped_app}/GDMENUCardManager""

# Relaunch the app
""{escaped_app}/GDMENUCardManager"" &

# Delete this script
rm ""$0""
";
        }

        public static void CleanupStaleStagingData()
        {
            try
            {
                var stagingDir = GetStagingDir();
                if (Directory.Exists(stagingDir))
                    Directory.Delete(stagingDir, true);
            }
            catch { }

            try
            {
                var batScript = Path.Combine(Path.GetTempPath(), WindowsScriptName);
                if (File.Exists(batScript))
                    File.Delete(batScript);
            }
            catch { }

            try
            {
                var shScript = Path.Combine(Path.GetTempPath(), UnixScriptName);
                if (File.Exists(shScript))
                    File.Delete(shScript);
            }
            catch { }
        }

        public static void CleanupStagingDirectory()
        {
            try
            {
                var stagingDir = GetStagingDir();
                if (Directory.Exists(stagingDir))
                    Directory.Delete(stagingDir, true);
            }
            catch { }
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
