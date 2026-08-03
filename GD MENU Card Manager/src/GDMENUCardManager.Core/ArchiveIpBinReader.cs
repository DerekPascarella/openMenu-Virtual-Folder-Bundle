using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GDMENUCardManager.Core
{
    internal static class ArchiveIpBinReader
    {
        private static readonly byte[] DreamcastSignature =
            Encoding.ASCII.GetBytes("SEGA SEGAKATANA SEGA ENTERPRISES");

        private const int DataTrackReadBytes = 64 * 1024;
        private const int GdiTextReadBytes = 16 * 1024;
        private const int HighDensityAreaLba = 45000;

        /// <summary>
        /// Reads IP.BIN from inside an archive without extracting it. Null when the archive holds
        /// no readable data track.
        /// </summary>
        public static async Task<IpBin> TryReadAsync(
            string archivePath,
            Dictionary<string, long> archiveContents)
        {
            if (string.IsNullOrEmpty(archivePath) || archiveContents == null || archiveContents.Count == 0)
                return null;

            try
            {
                string dataTrackEntryName = await ResolveDataTrackEntryAsync(archivePath, archiveContents);
                if (string.IsNullOrEmpty(dataTrackEntryName))
                    return null;

                dataTrackEntryName = Path.GetFileName(dataTrackEntryName.Replace('\\', '/'));
                if (string.IsNullOrEmpty(dataTrackEntryName))
                    return null;

                var trackBytes = await Task.Run(() =>
                    Helper.DependencyManager.ReadArchiveEntryBytes(
                        archivePath, dataTrackEntryName, DataTrackReadBytes));

                if (trackBytes == null || trackBytes.Length < 256)
                    return null;

                int sigOffset = FindSignature(trackBytes, DreamcastSignature);
                if (sigOffset < 0)
                    return null;

                int available = trackBytes.Length - sigOffset;
                if (available < 256)
                    return null;

                var ipBytes = new byte[Math.Min(512, available)];
                Array.Copy(trackBytes, sigOffset, ipBytes, 0, ipBytes.Length);

                return ImageHelper.GetIpData(ipBytes);
            }
            catch
            {
                return null;
            }
        }

        private static async Task<string> ResolveDataTrackEntryAsync(
            string archivePath,
            Dictionary<string, long> archiveContents)
        {
            var allFiles = archiveContents.Keys.ToList();

            var gdiEntry = allFiles.FirstOrDefault(f =>
                Path.GetExtension(f).Equals(".gdi", StringComparison.OrdinalIgnoreCase));

            if (gdiEntry != null)
            {
                var gdiName = Path.GetFileName(gdiEntry.Replace('\\', '/'));
                var gdiBytes = await Task.Run(() =>
                    Helper.DependencyManager.ReadArchiveEntryBytes(
                        archivePath, gdiName, GdiTextReadBytes));

                if (gdiBytes == null || gdiBytes.Length == 0)
                    return null;

                return ParseGdiForHdDataTrack(Encoding.ASCII.GetString(gdiBytes));
            }

            var cueEntry = allFiles.FirstOrDefault(f =>
                Path.GetExtension(f).Equals(".cue", StringComparison.OrdinalIgnoreCase));

            if (cueEntry != null)
            {
                // Skip parsing the cue. In a solid 7z it can be costly to reach, and
                // the largest .bin or .iso is always the HD data track for a GD-ROM rip.
                var dataCandidate = archiveContents
                    .Where(kv =>
                    {
                        var ext = Path.GetExtension(kv.Key);
                        return ext.Equals(".bin", StringComparison.OrdinalIgnoreCase)
                            || ext.Equals(".iso", StringComparison.OrdinalIgnoreCase);
                    })
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => kv.Key)
                    .FirstOrDefault();

                if (dataCandidate == null)
                    return null;

                return Path.GetFileName(dataCandidate.Replace('\\', '/'));
            }

            return null;
        }

        internal static string ParseGdiForHdDataTrack(string gdiText)
        {
            if (string.IsNullOrEmpty(gdiText))
                return null;

            var lines = gdiText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                var parts = SplitGdiLine(line);
                if (parts.Length < 5)
                    continue;

                if (!int.TryParse(parts[0], out _))
                    continue;
                if (!int.TryParse(parts[1], out int lba))
                    continue;
                if (!int.TryParse(parts[2], out int type))
                    continue;

                if (type == 4 && lba >= HighDensityAreaLba)
                    return parts[4];
            }

            return null;
        }

        private static string[] SplitGdiLine(string line)
        {
            var result = new List<string>();
            var cur = new StringBuilder();
            bool inQuotes = false;

            foreach (var c in line)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (!inQuotes && (c == ' ' || c == '\t'))
                {
                    if (cur.Length > 0)
                    {
                        result.Add(cur.ToString());
                        cur.Clear();
                    }
                }
                else
                {
                    cur.Append(c);
                }
            }

            if (cur.Length > 0)
                result.Add(cur.ToString());

            return result.ToArray();
        }

        private static int FindSignature(byte[] haystack, byte[] needle)
        {
            if (haystack == null || needle == null || needle.Length == 0 || haystack.Length < needle.Length)
                return -1;

            int limit = haystack.Length - needle.Length;
            for (int i = 0; i <= limit; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    return i;
            }
            return -1;
        }
    }
}
