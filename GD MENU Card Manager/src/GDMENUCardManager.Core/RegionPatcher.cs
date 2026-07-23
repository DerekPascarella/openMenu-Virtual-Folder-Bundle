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
        public int IpBinHeaderCount { get; set; }
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

        // Patch data for VGA flag: "1"
        private static readonly byte[] VgaFlagPatch = new byte[] { 0x31 };

        // Offset from IP.BIN header to region flag
        private const int RegionFlagOffset = 48;

        // Offset from IP.BIN header to VGA flag
        private const int VgaFlagOffset = 61;

        /// <summary>
        /// Builds the 92-byte region string patch for the given region combination.
        /// Structure: [Japan 28][Marker 4][USA 28][Marker 4][Europe 28] = 92 bytes.
        /// Slots for disabled regions are filled with spaces.
        /// </summary>
        private static byte[] BuildRegionStringPatch(string region)
        {
            var patch = new byte[92];
            int offset = 0;

            // PHILIPINES is the official Dreamcast spelling
            CopyPaddedString(region.Contains("J") ? "For JAPAN,TAIWAN,PHILIPINES." : string.Empty, patch, ref offset, 28);

            // Marker: 0E A0 09 00
            patch[offset++] = 0x0E;
            patch[offset++] = 0xA0;
            patch[offset++] = 0x09;
            patch[offset++] = 0x00;

            CopyPaddedString(region.Contains("U") ? "For USA and CANADA." : string.Empty, patch, ref offset, 28);

            // Marker: 0E A0 09 00
            patch[offset++] = 0x0E;
            patch[offset++] = 0xA0;
            patch[offset++] = 0x09;
            patch[offset++] = 0x00;

            CopyPaddedString(region.Contains("E") ? "For EUROPE." : string.Empty, patch, ref offset, 28);

            return patch;
        }

        /// <summary>
        /// Builds the 3-byte area symbols patch. The field is positional:
        /// byte 0 is 'J' or space, byte 1 is 'U' or space, byte 2 is 'E' or space.
        /// </summary>
        private static byte[] BuildRegionFlagPatch(string region)
        {
            return new byte[]
            {
                region.Contains("J") ? (byte)'J' : (byte)' ',
                region.Contains("U") ? (byte)'U' : (byte)' ',
                region.Contains("E") ? (byte)'E' : (byte)' '
            };
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
        public static Task<PatchResult> PatchImageAsync(string imagePath, bool patchRegion, bool patchVga)
        {
            return PatchImageAsync(imagePath, patchRegion ? "JUE" : null, patchVga);
        }

        /// <summary>
        /// Patch a disc image to a specific region combination and/or apply the VGA patch.
        /// </summary>
        /// <param name="imagePath">Path to the disc image file (.gdi or .cdi)</param>
        /// <param name="region">Target regions ("J", "U", "E" or a combination), or null to skip region patching</param>
        /// <param name="patchVga">Whether to apply VGA patch</param>
        /// <returns>Result of the patching operation</returns>
        public static async Task<PatchResult> PatchImageAsync(string imagePath, string region, bool patchVga)
        {
            var result = new PatchResult { Success = true };

            if (region == null && !patchVga)
            {
                result.Details.Add("No patches selected.");
                return result;
            }

            var extension = Path.GetExtension(imagePath).ToLowerInvariant();

            try
            {
                if (extension == ".gdi")
                {
                    await PatchGdiAsync(imagePath, region, patchVga, result);
                }
                else if (extension == ".cdi")
                {
                    await PatchSingleFileAsync(imagePath, region, patchVga, result);
                }
                else
                {
                    // For other formats (mds, ccd), try to find the associated data file
                    var dataFile = FindDataFile(imagePath);
                    if (dataFile != null)
                    {
                        await PatchSingleFileAsync(dataFile, region, patchVga, result);
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
        private static async Task PatchGdiAsync(string gdiPath, string region, bool patchVga, PatchResult result)
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
                    await PatchSingleFileAsync(trackPath, region, patchVga, result);
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
        private static async Task PatchSingleFileAsync(string filePath, string region, bool patchVga, PatchResult result)
        {
            await Task.Run(() =>
            {
                bool patchRegion = region != null;
                var regionFlagPatch = patchRegion ? BuildRegionFlagPatch(region) : null;
                var regionStringPatch = patchRegion ? BuildRegionStringPatch(region) : null;

                // Single pass: find ALL patterns at once (IP.BIN headers + region strings)
                var (ipBinHeaders, regionBlockStarts) = FindAllPatternsInSinglePass(filePath, patchRegion);

                result.IpBinHeaderCount += ipBinHeaders.Count;

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

                            // Read block to check if it already matches the target regions
                            fs.Seek(blockStart, SeekOrigin.Begin);
                            if (fs.Read(buffer, 0, 92) != 92)
                                continue;

                            if (new ReadOnlySpan<byte>(buffer, 0, 92).SequenceEqual(regionStringPatch))
                            {
                                result.Details.Add($"    Region block at {blockStart}: already matches target, skipping");
                                continue;
                            }

                            // Apply patch
                            fs.Seek(blockStart, SeekOrigin.Begin);
                            fs.Write(regionStringPatch, 0, regionStringPatch.Length);
                            result.RegionPatchCount++;
                            result.Details.Add($"    Patched region strings at {blockStart}");
                        }
                    }

                    // Patch IP.BIN headers (region flag and/or VGA flag)
                    foreach (var headerOffset in ipBinHeaders)
                    {
                        // Patch region flag at header + 48
                        if (patchRegion)
                        {
                            var flagOffset = headerOffset + RegionFlagOffset;
                            if (flagOffset + 3 <= fs.Length)
                            {
                                fs.Seek(flagOffset, SeekOrigin.Begin);
                                if (fs.Read(buffer, 0, 3) == 3)
                                {
                                    if (buffer[0] == regionFlagPatch[0] && buffer[1] == regionFlagPatch[1] && buffer[2] == regionFlagPatch[2])
                                    {
                                        result.Details.Add($"    Region flag at {flagOffset}: already {region}, skipping");
                                    }
                                    else
                                    {
                                        fs.Seek(flagOffset, SeekOrigin.Begin);
                                        fs.Write(regionFlagPatch, 0, regionFlagPatch.Length);
                                        result.RegionPatchCount++;
                                        result.Details.Add($"    Patched region flag to {region} at {flagOffset}");
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
