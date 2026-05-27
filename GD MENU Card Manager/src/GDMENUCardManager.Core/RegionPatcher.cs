using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GDMENUCardManager.Core
{
    /// <summary>
    /// Result of a patching operation.
    /// </summary>
    public class PatchResult
    {
        public bool Success { get; set; }
        public int RegionPatchCount { get; set; }
        public int VgaPatchCount { get; set; }
        public string ErrorMessage { get; set; }
        public List<string> Details { get; } = new List<string>();
    }

    /// <summary>
    /// Provides region-free and VGA patching for Dreamcast disc images.
    /// </summary>
    public static class RegionPatcher
    {
        // Search pattern for locating IP.BIN header (for region flag and VGA flag)
        // "SEGA SEGAKATANA " (16 bytes)
        private static readonly byte[] IpBinHeaderPattern = new byte[]
        {
            0x53, 0x45, 0x47, 0x41, 0x20, 0x53, 0x45, 0x47,
            0x41, 0x4B, 0x41, 0x54, 0x41, 0x4E, 0x41, 0x20
        };

        // Region string search patterns
        // "For JAPAN,TAIWAN,PHILIPINES." (28 bytes). Note that PHILIPINES is the official Dreamcast spelling.
        private static readonly byte[] JapanRegionString = Encoding.ASCII.GetBytes("For JAPAN,TAIWAN,PHILIPINES.");
        // "For USA and CANADA." (19 bytes)
        private static readonly byte[] UsaRegionString = Encoding.ASCII.GetBytes("For USA and CANADA.");
        // "For EUROPE." (11 bytes)
        private static readonly byte[] EuropeRegionString = Encoding.ASCII.GetBytes("For EUROPE.");

        // Marker that appears before the Japan region string (used for validation)
        private static readonly byte[] RegionBlockMarker = new byte[] { 0x0E, 0xA0, 0x09, 0x00 };

        // Full region-free patch data (92 bytes)
        // Structure: [Japan 28 bytes][0E A0 09 00][USA 28 bytes][0E A0 09 00][Europe 28 bytes]
        private static readonly byte[] RegionStringPatch = BuildRegionStringPatch();

        // Patch data for region flag: "JUE" (Japan, USA, Europe)
        private static readonly byte[] RegionFlagPatch = new byte[] { 0x4A, 0x55, 0x45 };

        // Patch data for VGA flag: "1"
        private static readonly byte[] VgaFlagPatch = new byte[] { 0x31 };

        // Offset from IP.BIN header to region flag
        private const int RegionFlagOffset = 48;

        // Offset from IP.BIN header to VGA flag
        private const int VgaFlagOffset = 61;

        // Offsets within the region block (relative to Japan string start)
        private const int UsaStringOffset = 32;    // Japan (28) + marker (4)
        private const int EuropeStringOffset = 64; // Japan (28) + marker (4) + USA (28) + marker (4)

        /// <summary>
        /// Builds the 92-byte region-free patch data.
        /// Structure: [Japan 28][Marker 4][USA 28][Marker 4][Europe 28] = 92 bytes
        /// </summary>
        private static byte[] BuildRegionStringPatch()
        {
            var patch = new byte[92];
            int offset = 0;

            // Japan: "For JAPAN,TAIWAN,PHILIPINES." (exactly 28 bytes)
            CopyPaddedString("For JAPAN,TAIWAN,PHILIPINES.", patch, ref offset, 28);

            // Marker: 0E A0 09 00
            patch[offset++] = 0x0E;
            patch[offset++] = 0xA0;
            patch[offset++] = 0x09;
            patch[offset++] = 0x00;

            // USA: "For USA and CANADA." padded to 28 bytes with spaces
            CopyPaddedString("For USA and CANADA.", patch, ref offset, 28);

            // Marker: 0E A0 09 00
            patch[offset++] = 0x0E;
            patch[offset++] = 0xA0;
            patch[offset++] = 0x09;
            patch[offset++] = 0x00;

            // Europe: "For EUROPE." padded to 28 bytes with spaces
            CopyPaddedString("For EUROPE.", patch, ref offset, 28);

            return patch;
        }

        /// <summary>
        /// Copies a string to a byte array, padding with spaces to reach the target length.
        /// </summary>
        private static void CopyPaddedString(string str, byte[] dest, ref int offset, int targetLength)
        {
            var bytes = Encoding.ASCII.GetBytes(str);
            Array.Copy(bytes, 0, dest, offset, bytes.Length);
            // Fill remaining bytes with spaces (0x20)
            for (int i = bytes.Length; i < targetLength; i++)
            {
                dest[offset + i] = 0x20;
            }
            offset += targetLength;
        }

        /// <summary>
        /// Patch a disc image with region-free and/or VGA patches.
        /// Supports GDI and CDI formats.
        /// </summary>
        /// <param name="imagePath">Path to the disc image file (.gdi or .cdi)</param>
        /// <param name="patchRegion">Whether to apply region-free patch</param>
        /// <param name="patchVga">Whether to apply VGA patch</param>
        /// <returns>Result of the patching operation</returns>
        public static async Task<PatchResult> PatchImageAsync(string imagePath, bool patchRegion, bool patchVga)
        {
            var result = new PatchResult { Success = true };

            if (!patchRegion && !patchVga)
            {
                result.Details.Add("No patches selected.");
                return result;
            }

            var extension = Path.GetExtension(imagePath).ToLowerInvariant();

            try
            {
                if (extension == ".gdi")
                {
                    await PatchGdiAsync(imagePath, patchRegion, patchVga, result);
                }
                else if (extension == ".cdi")
                {
                    await PatchSingleFileAsync(imagePath, patchRegion, patchVga, result);
                }
                else
                {
                    // For other formats (mds, ccd), try to find the associated data file
                    var dataFile = FindDataFile(imagePath);
                    if (dataFile != null)
                    {
                        await PatchSingleFileAsync(dataFile, patchRegion, patchVga, result);
                    }
                    else
                    {
                        result.Success = false;
                        result.ErrorMessage = $"Unsupported format or data file not found: {extension}";
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Patch a GDI disc image by parsing the .gdi file and patching all data tracks.
        /// </summary>
        private static async Task PatchGdiAsync(string gdiPath, bool patchRegion, bool patchVga, PatchResult result)
        {
            var baseFolder = Path.GetDirectoryName(gdiPath);
            var dataTracks = await ParseGdiFileAsync(gdiPath);

            if (dataTracks.Count == 0)
            {
                result.Details.Add("No data tracks found in GDI file.");
                return;
            }

            foreach (var track in dataTracks)
            {
                var trackPath = Path.Combine(baseFolder, track);
                if (File.Exists(trackPath))
                {
                    result.Details.Add($"Processing track: {track}");
                    await PatchSingleFileAsync(trackPath, patchRegion, patchVga, result);
                }
                else
                {
                    result.Details.Add($"Track file not found: {track}");
                }
            }
        }

        /// <summary>
        /// Parse a GDI file and return a list of data track filenames (.bin or .iso).
        /// </summary>
        private static async Task<List<string>> ParseGdiFileAsync(string gdiPath)
        {
            var dataTracks = new List<string>();

            var lines = await Task.Run(() => File.ReadAllLines(gdiPath));
            foreach (var line in lines)
            {
                // Match lines referencing BIN or ISO data tracks
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var trimmed = part.Trim('"');
                    if (trimmed.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
                    {
                        dataTracks.Add(trimmed);
                    }
                }
            }

            return dataTracks;
        }

        /// <summary>
        /// Patch a single binary file (data track or CDI).
        /// Uses optimized single-pass search for all patterns.
        /// </summary>
        private static async Task PatchSingleFileAsync(string filePath, bool patchRegion, bool patchVga, PatchResult result)
        {
            await Task.Run(() =>
            {
                // Single pass: find ALL patterns at once (IP.BIN headers + region strings)
                var (ipBinHeaders, regionBlockStarts) = FindAllPatternsInSinglePass(filePath, patchRegion);

                if (ipBinHeaders.Count > 0)
                {
                    result.Details.Add($"  Found {ipBinHeaders.Count} IP.BIN header(s)");
                }
                else
                {
                    result.Details.Add("  No IP.BIN headers found");
                }

                if (patchRegion && regionBlockStarts.Count > 0)
                {
                    result.Details.Add($"  Found {regionBlockStarts.Count} region string block(s)");
                }

                // Nothing to patch?
                if (ipBinHeaders.Count == 0 && regionBlockStarts.Count == 0)
                    return;

                // Open file once for all read/write operations
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    var buffer = new byte[92]; // Reusable buffer

                    // Patch region string blocks (92-byte patches)
                    if (patchRegion)
                    {
                        foreach (var blockStart in regionBlockStarts.OrderBy(x => x))
                        {
                            // Validate position
                            if (blockStart < 4 || blockStart + 92 > fs.Length)
                            {
                                result.Details.Add($"    Region block at {blockStart}: invalid position");
                                continue;
                            }

                            // Validate marker at (blockStart - 4)
                            fs.Seek(blockStart - 4, SeekOrigin.Begin);
                            if (fs.Read(buffer, 0, 4) != 4)
                                continue;

                            if (buffer[0] != 0x0E || buffer[1] != 0xA0 || buffer[2] != 0x09 || buffer[3] != 0x00)
                            {
                                result.Details.Add($"    Region block at {blockStart}: marker validation failed");
                                continue;
                            }

                            // Read block to check if already region-free
                            fs.Seek(blockStart, SeekOrigin.Begin);
                            if (fs.Read(buffer, 0, 92) != 92)
                                continue;

                            bool hasJapan = MatchesPattern(buffer, 0, JapanRegionString);
                            bool hasUsa = MatchesPattern(buffer, UsaStringOffset, UsaRegionString);
                            bool hasEurope = MatchesPattern(buffer, EuropeStringOffset, EuropeRegionString);

                            if (hasJapan && hasUsa && hasEurope)
                            {
                                result.Details.Add($"    Region block at {blockStart}: already region-free, skipping");
                                continue;
                            }

                            // Apply patch
                            fs.Seek(blockStart, SeekOrigin.Begin);
                            fs.Write(RegionStringPatch, 0, RegionStringPatch.Length);
                            result.RegionPatchCount++;
                            result.Details.Add($"    Patched region strings at {blockStart}");
                        }
                    }

                    // Patch IP.BIN headers (region flag and/or VGA flag)
                    foreach (var headerOffset in ipBinHeaders)
                    {
                        // Patch region flag (JUE) at header + 48
                        if (patchRegion)
                        {
                            var flagOffset = headerOffset + RegionFlagOffset;
                            if (flagOffset + 3 <= fs.Length)
                            {
                                fs.Seek(flagOffset, SeekOrigin.Begin);
                                if (fs.Read(buffer, 0, 3) == 3)
                                {
                                    // Check if already JUE
                                    if (buffer[0] == 0x4A && buffer[1] == 0x55 && buffer[2] == 0x45)
                                    {
                                        result.Details.Add($"    Region flag at {flagOffset}: already JUE, skipping");
                                    }
                                    else
                                    {
                                        fs.Seek(flagOffset, SeekOrigin.Begin);
                                        fs.Write(RegionFlagPatch, 0, RegionFlagPatch.Length);
                                        result.RegionPatchCount++;
                                        result.Details.Add($"    Patched region flag to JUE at {flagOffset}");
                                    }
                                }
                            }
                        }

                        // Patch VGA flag at header + 61
                        if (patchVga)
                        {
                            var vgaOffset = headerOffset + VgaFlagOffset;
                            if (vgaOffset + 1 <= fs.Length)
                            {
                                fs.Seek(vgaOffset, SeekOrigin.Begin);
                                fs.Write(VgaFlagPatch, 0, VgaFlagPatch.Length);
                                result.VgaPatchCount++;
                                result.Details.Add($"    Patched VGA flag at {vgaOffset}");
                            }
                        }
                    }
                }
            });
        }

        // 20-byte marker pattern that precedes the region strings in IP.BIN
        private static readonly byte[] RegionStringMarker = new byte[]
        {
            0x00, 0x38, 0x00, 0x70, 0x00, 0xE0, 0x01, 0xC0,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x0E, 0xA0, 0x09, 0x00
        };

        /// <summary>
        /// Search for patterns using chunked sequential file reading.
        /// </summary>
        private static (List<long> ipBinHeaders, HashSet<long> regionBlockStarts) FindAllPatternsInSinglePass(string filePath, bool searchRegionStrings)
        {
            var ipBinHeaders = new List<long>();
            var regionBlockStarts = new List<long>();

            var fileInfo = new FileInfo(filePath);
            long fileSize = fileInfo.Length;

            // For files under 256MB, read entire file into memory for fastest search
            if (fileSize <= 256 * 1024 * 1024)
            {
                byte[] fileData = File.ReadAllBytes(filePath);
                SearchAllPatterns(fileData, fileData.Length, 0, ipBinHeaders, regionBlockStarts, searchRegionStrings);
            }
            else
            {
                // For larger files, use chunked reading with large buffers
                const int chunkSize = 16 * 1024 * 1024; // 16MB chunks
                int overlapSize = RegionStringMarker.Length - 1;

                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: chunkSize, FileOptions.SequentialScan))
                {
                    var buffer = new byte[chunkSize + overlapSize];
                    long fileOffset = 0;
                    int carryOver = 0;

                    while (true)
                    {
                        int bytesRead = fs.Read(buffer, carryOver, chunkSize);
                        if (bytesRead == 0)
                            break;

                        int totalBytes = carryOver + bytesRead;
                        SearchAllPatterns(buffer, totalBytes, fileOffset, ipBinHeaders, regionBlockStarts, searchRegionStrings);

                        fileOffset += totalBytes - overlapSize;

                        if (bytesRead == chunkSize)
                        {
                            Buffer.BlockCopy(buffer, totalBytes - overlapSize, buffer, 0, overlapSize);
                            carryOver = overlapSize;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }

            // Remove duplicates (from overlap regions)
            return (ipBinHeaders.Distinct().ToList(), new HashSet<long>(regionBlockStarts));
        }

        /// <summary>
        /// Search for all patterns in a buffer.
        /// </summary>
        private static void SearchAllPatterns(byte[] buffer, int length, long baseOffset,
            List<long> ipBinHeaders, List<long> regionBlockStarts, bool searchRegionStrings)
        {
            var span = new ReadOnlySpan<byte>(buffer, 0, length);

            // Search for IP.BIN headers
            int pos = 0;
            while (pos <= length - IpBinHeaderPattern.Length)
            {
                int idx = span.Slice(pos).IndexOf(IpBinHeaderPattern);
                if (idx < 0) break;
                ipBinHeaders.Add(baseOffset + pos + idx);
                pos += idx + 1;
            }

            // Search for region string markers
            if (searchRegionStrings)
            {
                pos = 0;
                while (pos <= length - RegionStringMarker.Length)
                {
                    int idx = span.Slice(pos).IndexOf(RegionStringMarker);
                    if (idx < 0) break;
                    regionBlockStarts.Add(baseOffset + pos + idx + 20); // +20 to get past marker
                    pos += idx + 1;
                }
            }
        }

        /// <summary>
        /// Checks if a pattern matches at a specific offset in a buffer.
        /// </summary>
        private static bool MatchesPattern(byte[] buffer, int offset, byte[] pattern)
        {
            if (offset + pattern.Length > buffer.Length)
                return false;

            return new ReadOnlySpan<byte>(buffer, offset, pattern.Length).SequenceEqual(pattern);
        }

        /// <summary>
        /// Find the associated data file for MDS or CCD formats.
        /// </summary>
        private static string FindDataFile(string imagePath)
        {
            var extension = Path.GetExtension(imagePath).ToLowerInvariant();
            var basePath = Path.ChangeExtension(imagePath, null);

            string[] possibleExtensions;
            if (extension == ".mds")
            {
                possibleExtensions = new[] { ".mdf" };
            }
            else if (extension == ".ccd")
            {
                possibleExtensions = new[] { ".img" };
            }
            else
            {
                return null;
            }

            foreach (var ext in possibleExtensions)
            {
                var dataPath = basePath + ext;
                if (File.Exists(dataPath))
                    return dataPath;
            }

            return null;
        }

        /// <summary>
        /// Check if an image can be patched (is it a supported format).
        /// </summary>
        public static bool CanPatch(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath))
                return false;

            var extension = Path.GetExtension(imagePath).ToLowerInvariant();
            return extension == ".gdi" || extension == ".cdi" ||
                   extension == ".mds" || extension == ".ccd";
        }
    }
}
