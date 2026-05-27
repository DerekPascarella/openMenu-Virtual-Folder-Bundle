using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace GDMENUCardManager.Core
{
    /// <summary>
    /// Wrapper for the redump2cdi CLI tool that converts Redump CD-ROM cue/bin to CDI format.
    /// </summary>
    public static class Redump2CdiConverter
    {
        private const string ToolName = "redump2cdi";
        private const string WindowsToolName = "redump2cdi.exe";
        private const string SuccessMarker = "Enjoy!";

        /// <summary>
        /// Check if a CUE file is a Redump CD-ROM image (not GD-ROM).
        /// GD-ROM images have "HIGH-DENSITY AREA" comments.
        /// </summary>
        public static bool IsRedumpCdRomCue(string cuePath)
        {
            if (string.IsNullOrEmpty(cuePath) || !File.Exists(cuePath))
                return false;

            if (!cuePath.EndsWith(".cue", StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                var content = File.ReadAllText(cuePath);
                // GD-ROM images have a HIGH-DENSITY AREA comment. If that's present, it's not a CD-ROM.
                if (content.Contains("HIGH-DENSITY AREA", StringComparison.OrdinalIgnoreCase))
                    return false;

                // Must have at least one FILE and TRACK command to be valid
                return content.Contains("FILE ", StringComparison.OrdinalIgnoreCase) &&
                       content.Contains("TRACK ", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if a CUE file is a Redump GD-ROM image.
        /// </summary>
        public static bool IsRedumpGdRomCue(string cuePath)
        {
            if (string.IsNullOrEmpty(cuePath) || !File.Exists(cuePath))
                return false;

            if (!cuePath.EndsWith(".cue", StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                var content = File.ReadAllText(cuePath);
                return content.Contains("HIGH-DENSITY AREA", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get the path to the redump2cdi tool for the current platform.
        /// </summary>
        public static string GetToolPath()
        {
            var toolsDir = Path.Combine(AppContext.BaseDirectory, "tools");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Path.Combine(toolsDir, WindowsToolName);
            }
            else
            {
                return Path.Combine(toolsDir, ToolName);
            }
        }

        /// <summary>
        /// Check if the redump2cdi tool is available.
        /// </summary>
        public static bool IsToolAvailable()
        {
            var toolPath = GetToolPath();
            return File.Exists(toolPath);
        }

        /// <summary>
        /// Convert a Redump CUE/BIN to CDI format.
        /// </summary>
        /// <param name="cuePath">Path to the input .cue file</param>
        /// <param name="cdiOutputPath">Path for the output .cdi file</param>
        /// <returns>Tuple of (success, output/error message)</returns>
        public static (bool success, string message) ConvertToCdi(string cuePath, string cdiOutputPath)
        {
            var toolPath = GetToolPath();

            if (!File.Exists(toolPath))
            {
                return (false, $"redump2cdi tool not found at: {toolPath}");
            }

            if (!File.Exists(cuePath))
            {
                return (false, $"Input CUE file not found: {cuePath}");
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = toolPath,
                    Arguments = $"--cue \"{cuePath}\" --cdi \"{cdiOutputPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                // On Unix-like systems, ensure the binary is executable
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    EnsureExecutable(toolPath);
                }

                using var process = new Process { StartInfo = startInfo };
                process.Start();

                // Read stdout and stderr in parallel to avoid deadlock
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();

                // Wait for process with timeout (5 minutes max for large files)
                bool exited = process.WaitForExit(300000);
                if (!exited)
                {
                    try { process.Kill(); } catch { }
                    return (false, "Conversion timed out after 5 minutes");
                }

                var stdout = stdoutTask.GetAwaiter().GetResult();
                var stderr = stderrTask.GetAwaiter().GetResult();

                var combinedOutput = stdout + stderr;

                // Check for success marker in output
                if (combinedOutput.Contains(SuccessMarker))
                {
                    // Verify the output file was created
                    if (File.Exists(cdiOutputPath))
                    {
                        return (true, "Conversion successful");
                    }
                    else
                    {
                        return (false, "Conversion appeared successful but output file not found");
                    }
                }
                else
                {
                    return (false, $"Conversion failed: {combinedOutput}");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Error running redump2cdi: {ex.Message}");
            }
        }

        /// <summary>
        /// Ensure a file has executable permissions on Unix-like systems.
        /// </summary>
        private static void EnsureExecutable(string filePath)
        {
            try
            {
                // Use chmod to make the file executable
                var chmodInfo = new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+x \"{filePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var chmod = Process.Start(chmodInfo);
                chmod?.WaitForExit();
            }
            catch
            {
                // Ignore chmod errors. The file might already be executable.
            }
        }

        /// <summary>
        /// Get the expected CDI output filename for a given CUE file.
        /// </summary>
        public static string GetCdiOutputName(string cuePath)
        {
            var baseName = Path.GetFileNameWithoutExtension(cuePath);
            return baseName + ".cdi";
        }
    }
}
