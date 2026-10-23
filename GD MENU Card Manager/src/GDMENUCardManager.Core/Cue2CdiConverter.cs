#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace GDMENUCardManager.Core;

public static class Cue2CdiConverter
{
    public static async Task<bool> ConvertAsync(string cueFilePath, string destinationDir,
        IProgress<string>? progress = null)
    {
        return await Task.Run(() =>
        {
            string cuePath = Path.GetFullPath(cueFilePath);
            if (!File.Exists(cuePath))
                throw new FileNotFoundException($"File not found: {cuePath}");

            string outputDir = Path.GetFullPath(destinationDir);
            string baseName = Path.GetFileNameWithoutExtension(cuePath);
            string cdiPath = Path.Combine(outputDir, baseName + ".cdi");
            progress?.Report($"Converting {baseName} from CUE to CDI...");

            var cue = CueSheet.Parse(cuePath);
            var disc = DiscLayout.Build(cue);
            if (File.Exists(cdiPath))
            {
                string destination = FilePaths.Resolve(cdiPath);
                var inputs = cue.Files.Select(f => f.FullPath).Append(cuePath);
                if (cue.CdTextFile != null) inputs = inputs.Append(cue.CdTextFile);
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (inputs.Any(input => string.Equals(FilePaths.Resolve(input), destination, comparison)))
                    throw new IOException("The output path refers to an input file.");
            }

            foreach (string extension in new[] { ".sbi", ".lsd", ".sub" })
            {
                string sidecar = Path.ChangeExtension(cuePath, extension);
                if (File.Exists(sidecar))
                    progress?.Report($"Warning: {Path.GetFileName(sidecar)} is not imported. Its separate subchannel data will not be included.");
            }

            Directory.CreateDirectory(outputDir);
            CdiWriter.Generate(disc, cdiPath);
            return true;
        });
    }

    public static Dictionary<string, long> ExpectedOutput(string cueFilePath)
    {
        string cuePath = Path.GetFullPath(cueFilePath);
        var disc = DiscLayout.Build(CueSheet.Parse(cuePath));
        string name = Path.GetFileNameWithoutExtension(cuePath) + ".cdi";
        return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            [name] = CdiWriter.ExpectedLength(disc, name)
        };
    }

    private static class FilePaths
    {
        public static string Resolve(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var buffer = new StringBuilder(32768);
                uint length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 2);
                if (length == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (length >= buffer.Capacity) throw new PathTooLongException(path);
                return buffer.ToString();
            }

            IntPtr resolved = RealPath(path, IntPtr.Zero);
            if (resolved == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try { return Marshal.PtrToStringUTF8(resolved)!; }
            finally { Free(resolved); }
        }

        [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint capacity, uint flags);

        [DllImport("libc", EntryPoint = "realpath", SetLastError = true)]
        static extern IntPtr RealPath([MarshalAs(UnmanagedType.LPUTF8Str)] string path, IntPtr buffer);

        [DllImport("libc", EntryPoint = "free")]
        static extern void Free(IntPtr pointer);
    }

    // CUE Sheet Parser

    private class CueSheet
    {
        public List<CueFile> Files { get; } = new();
        public string? Catalog { get; set; }
        public string? CdTextFile { get; set; }
        public Dictionary<byte, string> Text { get; } = new();

        public static CueSheet Parse(string path)
        {
            var sheet = new CueSheet();
            CueFile? curFile = null;
            CueTrack? curTrack = null;
            string dir = Path.GetDirectoryName(path)!;
            int lineNumber = 0;
            int trackNumber = 0;

            string content;
            try
            {
                content = File.ReadAllText(path, new UTF8Encoding(false, true));
            }
            catch (DecoderFallbackException)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                content = File.ReadAllText(path, Encoding.GetEncoding(1252));
            }
            foreach (string raw in content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
            {
                lineNumber++;
                string line = raw.Trim();
                if (line.Length == 0) continue;
                try
                {
                    var p = Regex.Matches(line, "\"([^\"]*)\"|([^\\s\"]+)")
                        .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToArray();
                    if (line.Count(c => c == '"') % 2 != 0)
                        throw new FormatException("Unclosed quoted string.");
                    string command = p[0].ToUpperInvariant();
                    if (command == "REM")
                    {
                        if (p.Length > 1 && p[1].Equals("SESSION", StringComparison.OrdinalIgnoreCase))
                            throw new FormatException("Multisession CUE extensions are not supported.");
                        continue;
                    }
                    switch (command)
                    {
                        case "FILE":
                            Require(p.Length == 3, "Expected FILE <filename> <type>.");
                            Require(p[2].ToUpperInvariant() is "BINARY" or "MOTOROLA" or "WAVE", $"Unsupported FILE type: {p[2]}");
                            curFile = new CueFile(p[1], Path.GetFullPath(Path.Combine(dir, p[1].Replace('\\', Path.DirectorySeparatorChar))), p[2].ToUpperInvariant());
                            sheet.Files.Add(curFile);
                            curTrack = null;
                            break;
                        case "TRACK":
                            Require(p.Length == 3 && curFile != null, "TRACK must follow FILE and include a number and mode.");
                            int number = ParseNumber(p[1], 1, 99);
                            Require(number == ++trackNumber, "Track numbers must be consecutive, starting at 01.");
                            string mode = p[2].ToUpperInvariant();
                            Require(mode is "AUDIO" or "MODE1/2352" or "MODE1/2048" or "MODE2/2352" or "MODE2/2336" or "CDI/2352" or "CDI/2336" or "CDG", $"Unsupported track mode: {mode}");
                            curTrack = new CueTrack(number, mode, curFile!);
                            curFile!.Tracks.Add(curTrack);
                            break;
                        case "INDEX":
                            Require(p.Length == 3 && curTrack != null, "INDEX must follow TRACK and include a number and time.");
                            int index = ParseNumber(p[1], 0, 99);
                            int position = ParseMsf(p[2]);
                            var previous = curTrack!.Indices.LastOrDefault();
                            Require(previous == null ? index <= 1 : index == previous.Number + 1 && position >= previous.Position,
                                "Indices must be consecutive and have nondecreasing positions.");
                            curTrack.Indices.Add(new CueIndex(index, position));
                            break;
                        case "PREGAP":
                        case "POSTGAP":
                            Require(p.Length == 2 && curTrack != null, $"{command} must follow TRACK and include a time.");
                            if (command == "PREGAP")
                            {
                                Require(!curTrack!.HasPregap && curTrack.Indices.Count == 0, "PREGAP must occur once, before INDEX.");
                                curTrack.Pregap = ParseMsf(p[1]);
                                curTrack.HasPregap = true;
                            }
                            else
                            {
                                Require(!curTrack!.HasPostgap, "Duplicate POSTGAP.");
                                curTrack.Postgap = ParseMsf(p[1]);
                                curTrack.HasPostgap = true;
                            }
                            break;
                        case "CATALOG":
                            Require(p.Length == 2 && sheet.Catalog == null && Regex.IsMatch(p[1], "^[0-9]{13}$"), "CATALOG must contain exactly 13 digits and occur once.");
                            sheet.Catalog = p[1];
                            break;
                        case "CDTEXTFILE":
                            Require(p.Length == 2 && sheet.CdTextFile == null, "CDTEXTFILE must name one file and occur once.");
                            sheet.CdTextFile = Path.GetFullPath(Path.Combine(dir, p[1].Replace('\\', Path.DirectorySeparatorChar)));
                            break;
                        case "TITLE":
                        case "PERFORMER":
                        case "SONGWRITER":
                        case "COMPOSER":
                        case "ARRANGER":
                        case "MESSAGE":
                            Require(p.Length == 2, $"{command} must contain one quoted string.");
                            byte textType = command switch
                            {
                                "TITLE" => 0x80,
                                "PERFORMER" => 0x81,
                                "SONGWRITER" => 0x82,
                                "COMPOSER" => 0x83,
                                "ARRANGER" => 0x84,
                                _ => 0x85
                            };
                            var text = curTrack == null ? sheet.Text : curTrack.Text;
                            Require(!text.ContainsKey(textType), $"Duplicate {command}.");
                            text.Add(textType, p[1]);
                            break;
                        case "ISRC":
                            Require(p.Length == 2 && curTrack != null && curTrack.Isrc == null && Regex.IsMatch(p[1], "^[A-Za-z]{2}[A-Za-z0-9]{3}[0-9]{7}$"), "ISRC must contain a 12-character recording code and occur once per track.");
                            curTrack!.Isrc = p[1].ToUpperInvariant();
                            break;
                        case "FLAGS":
                            Require(p.Length > 1 && curTrack != null && !curTrack.HasFlags, "FLAGS must occur once per track and contain at least one flag.");
                            foreach (string flag in p.Skip(1))
                                curTrack!.Flags |= flag.ToUpperInvariant() switch
                                {
                                    "PRE" => 1,
                                    "DCP" => 2,
                                    "4CH" => 8,
                                    "SCMS" => 16,
                                    _ => throw new FormatException($"Unsupported track flag: {flag}")
                                };
                            curTrack!.HasFlags = true;
                            Require((curTrack.Flags & 18) != 18, "SCMS and DCP cannot be combined.");
                            Require(curTrack.IsAudio || (curTrack.Flags & 25) == 0, "PRE, 4CH and SCMS apply only to audio tracks.");
                            break;
                        default:
                            throw new FormatException($"Unsupported CUE command: {p[0]}");
                    }
                }
                catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
                {
                    throw new FormatException($"CUE line {lineNumber}: {ex.Message}", ex);
                }
            }

            Require(sheet.Files.Count > 0, "The CUE contains no FILE entries.");
            Require(sheet.Files.All(f => f.Tracks.Count > 0), "Every FILE must contain at least one TRACK.");
            return sheet;
        }

        static int ParseNumber(string value, int min, int max)
        {
            Require(int.TryParse(value, out int result) && result >= min && result <= max, $"Invalid number: {value}");
            return result;
        }

        static int ParseMsf(string value)
        {
            string[] p = value.Split(':');
            Require(p.Length == 3, $"Invalid time: {value}");
            return ParseNumber(p[0], 0, 99) * 4500 + ParseNumber(p[1], 0, 59) * 75 + ParseNumber(p[2], 0, 74);
        }

        public static void Require(bool condition, string message)
        {
            if (!condition) throw new FormatException(message);
        }
    }

    private record CueFile(string Name, string FullPath, string Type)
    {
        public List<CueTrack> Tracks { get; } = new();
    }

    private record CueIndex(int Number, int Position);

    private class CueTrack
    {
        public int Number { get; }
        public string CueType { get; }
        public CueFile File { get; }
        public List<CueIndex> Indices { get; } = new();
        public int Pregap { get; set; }
        public int Postgap { get; set; }
        public bool HasPregap { get; set; }
        public bool HasPostgap { get; set; }
        public bool HasFlags { get; set; }
        public int Flags { get; set; }
        public string? Isrc { get; set; }
        public Dictionary<byte, string> Text { get; } = new();

        public CueTrack(int number, string cueType, CueFile file)
        {
            Number = number;
            CueType = cueType;
            File = file;
        }

        public bool IsAudio => CueType is "AUDIO" or "CDG";
        public int SourceSectorSize => CueType switch
        {
            "MODE1/2048" => 2048,
            "MODE2/2336" or "CDI/2336" => 2336,
            "CDG" => 2448,
            _ => 2352
        };
        public int CdiMode => IsAudio ? 0 : CueType.StartsWith("MODE1") ? 1 : 2;
        public int Control => (Flags & 15) | (IsAudio ? 0 : 4);
    }

    // Disc Layout Builder

    private class DiscLayout
    {
        public List<DiscTrack> Tracks { get; } = new();
        public long TotalSectors { get; set; }
        public string? Catalog { get; set; }
        public byte[] CdText { get; set; } = Array.Empty<byte>();
        public int SessionType => Tracks.Any(t => t.CdiMode == 2) ? 2 : Tracks.Any(t => !t.IsAudio) ? 1 : 0;

        public static DiscLayout Build(CueSheet cue)
        {
            var disc = new DiscLayout { Catalog = cue.Catalog, CdText = CdTextBuilder.Build(cue) };
            long currentSector = 0;
            foreach (var file in cue.Files)
            {
                long dataStart = 0;
                long fileSize = new FileInfo(file.FullPath).Length;
                if (file.Type == "WAVE")
                {
                    CueSheet.Require(file.Tracks.All(t => t.CueType == "AUDIO"), "WAVE files can contain only AUDIO tracks.");
                    (dataStart, fileSize) = WavReader.GetDataRange(file.FullPath);
                }
                long sourceOffset = 0;
                foreach (var track in file.Tracks)
                    CueSheet.Require(track.Indices.Any(i => i.Number == 1), $"Track {track.Number:D2} has no INDEX 01.");
                CueSheet.Require(file.Tracks[0].Indices[0].Number != 0 || file.Tracks[0].Indices[0].Position == 0,
                    $"INDEX 00 in {file.Name} must begin at 00:00:00.");

                for (int ti = 0; ti < file.Tracks.Count; ti++)
                {
                    var ct = file.Tracks[ti];
                    int fileStart = ti == 0 ? 0 : ct.Indices[0].Position;
                    int fileIndex01 = ct.Indices.First(i => i.Number == 1).Position;
                    long sourceSectors;
                    if (ti + 1 < file.Tracks.Count)
                    {
                        sourceSectors = file.Tracks[ti + 1].Indices[0].Position - fileStart;
                        CueSheet.Require(sourceSectors > 0 && sourceOffset + sourceSectors * ct.SourceSectorSize <= fileSize,
                            $"Track {ct.Number:D2} has an invalid source boundary.");
                    }
                    else
                    {
                        long remaining = fileSize - sourceOffset;
                        CueSheet.Require(remaining >= 0 && (file.Type == "WAVE" || remaining % ct.SourceSectorSize == 0),
                            $"File {file.Name} ends with a partial {ct.SourceSectorSize}-byte sector.");
                        sourceSectors = (remaining + ct.SourceSectorSize - 1) / ct.SourceSectorSize;
                    }

                    int embeddedPregap = fileIndex01 - fileStart;
                    CueSheet.Require(sourceSectors > embeddedPregap && ct.Indices[^1].Position < fileStart + sourceSectors,
                        $"Track {ct.Number:D2} has an index outside its source data.");
                    int generatedPregap = ct.Pregap;
                    if (disc.Tracks.Count == 0)
                        generatedPregap = Math.Max(generatedPregap, 150 - embeddedPregap);

                    var dt = new DiscTrack
                    {
                        Number = ct.Number,
                        CueType = ct.CueType,
                        CdiMode = ct.CdiMode,
                        ControlField = ct.Control,
                        SourceSectorSize = ct.SourceSectorSize,
                        SourceFilePath = file.FullPath,
                        SourceOffsetInFile = dataStart + sourceOffset,
                        SourceDataEnd = dataStart + fileSize,
                        SwapAudio = file.Type == "MOTOROLA" && ct.IsAudio,
                        SourceSectorCount = sourceSectors,
                        GeneratedPregap = generatedPregap,
                        PregapLength = generatedPregap + embeddedPregap,
                        PostgapLength = ct.Postgap,
                        AbsoluteStart = currentSector,
                        Isrc = ct.Isrc
                    };
                    dt.Scms = (ct.Flags & 16) != 0;
                    dt.Catalog = cue.Catalog;
                    dt.IndexLengths.Add(dt.PregapLength);
                    var indices = ct.Indices.Where(i => i.Number >= 1).ToList();
                    for (int i = 0; i < indices.Count; i++)
                    {
                        long end = i + 1 < indices.Count ? indices[i + 1].Position : fileStart + sourceSectors + ct.Postgap;
                        dt.IndexLengths.Add(end - indices[i].Position);
                    }
                    disc.Tracks.Add(dt);
                    sourceOffset += sourceSectors * ct.SourceSectorSize;
                    currentSector += dt.SectorCount;
                }
            }
            CueSheet.Require(currentSector <= 100 * 4500, "Disc length exceeds the two-digit CD minute range.");
            disc.TotalSectors = currentSector;
            return disc;
        }
    }

    private class DiscTrack
    {
        public int Number;
        public string CueType = "";
        public int CdiMode;
        public int ControlField;
        public int SourceSectorSize;
        public string SourceFilePath = "";
        public long SourceOffsetInFile;
        public long SourceDataEnd;
        public bool SwapAudio;
        public long SourceSectorCount;
        public int GeneratedPregap;
        public int PregapLength;
        public int PostgapLength;
        public long AbsoluteStart;
        public string? Isrc;
        public string? Catalog;
        public bool Scms;
        public List<long> IndexLengths { get; } = new();
        public bool IsAudio => CdiMode == 0;
        public long SectorCount => GeneratedPregap + SourceSectorCount + PostgapLength;
        public int OutputSectorSize => CueType == "CDG" || Scms ? 2448 : 2352;
    }

    // CD-TEXT Packs

    private static class CdTextBuilder
    {
        public static byte[] Build(CueSheet cue)
        {
            if (cue.CdTextFile != null)
            {
                byte[] data = File.ReadAllBytes(cue.CdTextFile);
                if (data.Length % 18 == 4 && data.Length >= 4 &&
                    ((data[0] << 8) | data[1]) == data.Length - 2 && data[2] == 0 && data[3] == 0)
                    data = data[4..];
                CueSheet.Require(data.Length > 0 && data.Length % 18 == 0, "CDTEXTFILE must contain 18-byte CD-TEXT packs.");
                for (int i = 0; i < data.Length; i += 18)
                    CueSheet.Require(Crc16.Compute(data.AsSpan(i, 16)) == ((data[i + 16] << 8) | data[i + 17]),
                        $"CDTEXTFILE has an invalid CRC in pack {i / 18}.");
                return data;
            }

            var texts = new List<Dictionary<byte, string>> { cue.Text };
            texts.AddRange(cue.Files.SelectMany(f => f.Tracks).Select(t => t.Text));
            var types = texts.SelectMany(t => t.Keys).Distinct().OrderBy(t => t).ToList();
            if (types.Count == 0) return Array.Empty<byte>();
            var packs = new List<byte[]>();
            foreach (byte type in types)
            {
                var strings = texts.Select(t => t.GetValueOrDefault(type, "")).ToList();
                foreach (string value in strings)
                    CueSheet.Require(value.All(c => c >= 32 && c <= 255 && !(c >= 127 && c < 160)),
                        "CD-TEXT strings must use printable ISO-8859-1 characters. Use CDTEXTFILE for other encodings.");
                int track = 0;
                int character = 0;
                while (track < strings.Count)
                {
                    byte[] pack = new byte[18];
                    pack[0] = type;
                    pack[1] = (byte)track;
                    pack[2] = (byte)packs.Count;
                    pack[3] = (byte)Math.Min(character, 15);
                    for (int i = 4; i < 16 && track < strings.Count; i++)
                    {
                        if (character < strings[track].Length)
                            pack[i] = (byte)strings[track][character++];
                        else
                        {
                            track++;
                            character = 0;
                        }
                    }
                    packs.Add(pack);
                    CueSheet.Require(packs.Count <= 253, "CD-TEXT exceeds the single-block 256-pack limit.");
                }
            }

            byte[] sizes = new byte[36];
            sizes[1] = 1;
            sizes[2] = (byte)(texts.Count - 1);
            foreach (byte[] pack in packs) sizes[4 + pack[0] - 0x80]++;
            sizes[19] = 3;
            sizes[20] = (byte)(packs.Count + 2);
            for (int i = 0; i < 3; i++)
            {
                byte[] pack = new byte[18];
                pack[0] = 0x8F;
                pack[1] = (byte)i;
                pack[2] = (byte)packs.Count;
                Array.Copy(sizes, i * 12, pack, 4, 12);
                packs.Add(pack);
            }
            foreach (byte[] pack in packs)
            {
                ushort crc = Crc16.Compute(pack.AsSpan(0, 16));
                pack[16] = (byte)(crc >> 8);
                pack[17] = (byte)crc;
            }
            return packs.SelectMany(p => p).ToArray();
        }
    }

    // WAV File Reader

    private static class WavReader
    {
        public static (long Offset, long Length) GetDataRange(string path)
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            CueSheet.Require(stream.Length >= 12 && Encoding.ASCII.GetString(reader.ReadBytes(4)) == "RIFF", "Not a RIFF file.");
            long end = reader.ReadUInt32() + 8L;
            CueSheet.Require(end <= stream.Length && Encoding.ASCII.GetString(reader.ReadBytes(4)) == "WAVE", "Invalid or truncated WAVE file.");
            bool hasFormat = false;
            long dataOffset = -1;
            long dataLength = 0;
            while (stream.Position < end)
            {
                CueSheet.Require(end - stream.Position >= 8, "Truncated WAVE chunk header.");
                string id = Encoding.ASCII.GetString(reader.ReadBytes(4));
                uint size = reader.ReadUInt32();
                long next = stream.Position + size + (size & 1);
                CueSheet.Require(next <= end, "Truncated WAVE chunk.");
                if (id == "fmt ")
                {
                    CueSheet.Require(!hasFormat && size >= 16, "Invalid WAVE format chunk.");
                    ushort format = reader.ReadUInt16();
                    ushort channels = reader.ReadUInt16();
                    uint rate = reader.ReadUInt32();
                    uint bytesPerSecond = reader.ReadUInt32();
                    ushort alignment = reader.ReadUInt16();
                    ushort bits = reader.ReadUInt16();
                    CueSheet.Require(format == 1 && channels == 2 && rate == 44100 && bytesPerSecond == 176400 && alignment == 4 && bits == 16,
                        "WAVE must contain 44,100 Hz, stereo, 16-bit uncompressed PCM audio.");
                    hasFormat = true;
                }
                else if (id == "data")
                {
                    CueSheet.Require(dataOffset < 0 && size > 0 && size % 4 == 0, "Invalid or duplicate WAVE data chunk.");
                    dataOffset = stream.Position;
                    dataLength = size;
                }
                stream.Position = next;
            }
            CueSheet.Require(hasFormat && dataOffset >= 0, "WAVE requires both fmt and data chunks.");
            return (dataOffset, dataLength);
        }
    }

    // Mode 1 Sector Builder

    private static class Mode1Sector
    {
        static readonly byte[] EccFTable = new byte[256];
        static readonly byte[] EccBTable = new byte[256];

        static Mode1Sector()
        {
            for (int i = 0; i < 256; i++)
            {
                int j = (i << 1) ^ ((i & 0x80) != 0 ? 0x11D : 0);
                EccFTable[i] = (byte)j;
                EccBTable[i ^ j] = (byte)i;
            }
        }

        public static byte[] Build(long lba, byte[] userData)
        {
            byte[] sector = new byte[2352];
            SectorHeader.Write(sector, lba, 1);
            Array.Copy(userData, 0, sector, 16, 2048);
            Edc.Write(sector, 0, 2064, 2064);
            ComputeEcc(sector, 86, 24, 2, 86, 2076);
            ComputeEcc(sector, 52, 43, 86, 88, 2248);
            return sector;
        }

        static void ComputeEcc(byte[] sector, int majorCount, int minorCount, int majorMult, int minorInc, int destOffset)
        {
            int size = majorCount * minorCount;
            for (int major = 0; major < majorCount; major++)
            {
                int index = (major >> 1) * majorMult + (major & 1);
                byte eccA = 0, eccB = 0;
                for (int minor = 0; minor < minorCount; minor++)
                {
                    byte value = sector[12 + index];
                    index += minorInc;
                    if (index >= size) index -= size;
                    eccA ^= value;
                    eccB ^= value;
                    eccA = EccFTable[eccA];
                }
                eccA = EccBTable[(byte)(EccFTable[eccA] ^ eccB)];
                sector[destOffset + major] = eccA;
                sector[destOffset + major + majorCount] = (byte)(eccA ^ eccB);
            }
        }
    }

    // Mode 2 Sector Builder

    private static class Mode2Sector
    {
        public static byte[] Build(long lba, byte[] data)
        {
            byte[] sector = new byte[2352];
            SectorHeader.Write(sector, lba, 2);
            Array.Copy(data, 0, sector, 16, 2336);
            return sector;
        }

        public static byte[] BuildGap(long lba)
        {
            byte[] sector = Build(lba, new byte[2336]);
            sector[18] = sector[22] = 0x20;
            Edc.Write(sector, 16, 2332, 2348);
            return sector;
        }
    }

    private static class SectorHeader
    {
        public static void Write(byte[] sector, long lba, byte mode)
        {
            Array.Fill(sector, (byte)0xFF, 1, 10);
            long absolute = lba + 150;
            sector[12] = ToBcd((int)(absolute / 4500));
            sector[13] = ToBcd((int)(absolute / 75 % 60));
            sector[14] = ToBcd((int)(absolute % 75));
            sector[15] = mode;
        }

        public static byte ToBcd(int value) => (byte)(((value / 10) << 4) | (value % 10));
    }

    private static class Edc
    {
        static readonly uint[] Table = new uint[256];

        static Edc()
        {
            for (int i = 0; i < 256; i++)
            {
                uint value = (uint)i;
                for (int j = 0; j < 8; j++)
                    value = (value >> 1) ^ ((value & 1) != 0 ? 0xD8018001u : 0u);
                Table[i] = value;
            }
        }

        public static void Write(byte[] sector, int start, int count, int dest)
        {
            uint value = 0;
            for (int i = start; i < start + count; i++)
                value = Table[(value ^ sector[i]) & 0xFF] ^ (value >> 8);
            for (int i = 0; i < 4; i++) sector[dest + i] = (byte)(value >> (i * 8));
        }
    }

    private static class Subchannel
    {
        public static void Write(Span<byte> destination, DiscTrack track, long absolute)
        {
            long relative = absolute - track.AbsoluteStart - track.PregapLength;
            bool pregap = relative < 0;
            int index = 0;
            long indexEnd = track.AbsoluteStart;
            foreach (long length in track.IndexLengths)
            {
                indexEnd += length;
                if (absolute < indexEnd) break;
                index++;
            }
            byte[] q = new byte[12];
            int control = track.ControlField;
            if (track.Scms && ((absolute - 150) & 7) >= 4) control |= 2;
            q[0] = (byte)((control << 4) | 1);
            q[1] = SectorHeader.ToBcd(track.Number);
            q[2] = SectorHeader.ToBcd(index);
            WriteTime(q, 3, Math.Abs(relative));
            WriteTime(q, 7, absolute);
            bool indexStart = absolute == indexEnd - track.IndexLengths[Math.Min(index, track.IndexLengths.Count - 1)];
            if (!pregap && !indexStart && relative % 100 == 25 && track.Catalog != null)
            {
                Array.Clear(q, 1, 8);
                q[0] = (byte)((control << 4) | 2);
                for (int i = 0; i < 13; i++)
                    q[1 + i / 2] |= (byte)((track.Catalog[i] - '0') << (i % 2 == 0 ? 4 : 0));
            }
            else if (!pregap && !indexStart && relative % 100 == 50 && track.Isrc != null)
            {
                Array.Clear(q, 1, 8);
                q[0] = (byte)((control << 4) | 3);
                uint letters = 0;
                for (int i = 0; i < 5; i++)
                {
                    char c = track.Isrc[i];
                    letters = (letters << 6) | (uint)(c <= '9' ? c - '0' : c - 'A' + 17);
                }
                letters <<= 2;
                for (int i = 0; i < 4; i++) q[i + 1] = (byte)(letters >> (24 - i * 8));
                for (int i = 0; i < 7; i++)
                    q[5 + i / 2] |= (byte)((track.Isrc[5 + i] - '0') << (i % 2 == 0 ? 4 : 0));
            }
            ushort crc = Crc16.Compute(q.AsSpan(0, 10));
            q[10] = (byte)(crc >> 8);
            q[11] = (byte)crc;
            for (int bit = 0; bit < 96; bit++)
                destination[bit] = (byte)((pregap ? 0x80 : 0) | (((q[bit / 8] >> (7 - bit % 8)) & 1) << 6));
        }

        static void WriteTime(byte[] buffer, int offset, long frames)
        {
            buffer[offset] = SectorHeader.ToBcd((int)(frames / 4500));
            buffer[offset + 1] = SectorHeader.ToBcd((int)(frames / 75 % 60));
            buffer[offset + 2] = SectorHeader.ToBcd((int)(frames % 75));
        }
    }

    private static class Crc16
    {
        public static ushort Compute(ReadOnlySpan<byte> data)
        {
            int crc = 0;
            foreach (byte value in data)
            {
                crc ^= value << 8;
                for (int bit = 0; bit < 8; bit++)
                    crc = ((crc << 1) ^ ((crc & 0x8000) != 0 ? 0x1021 : 0)) & 0xFFFF;
            }
            return (ushort)~crc;
        }
    }

    // CDI File Writer

    private static class CdiWriter
    {
        public static long ExpectedLength(DiscLayout disc, string path)
        {
            using var descriptor = new MemoryStream();
            WriteDescriptor(descriptor, disc, path);
            return disc.Tracks.Sum(track => track.SectorCount * track.OutputSectorSize) + descriptor.Length;
        }

        static void ReadExactly(Stream input, byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                int read = input.Read(buffer, offset, count);
                if (read == 0) throw new EndOfStreamException("Unable to read beyond the end of the stream.");
                offset += read;
                count -= read;
            }
        }

        public static void Generate(DiscLayout disc, string path)
        {
            string partialPath = path + "." + Guid.NewGuid().ToString("N") + ".partial";
            try
            {
                using (var output = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024))
                {
                    foreach (var track in disc.Tracks)
                    {
                        WriteGap(output, track, track.AbsoluteStart, track.GeneratedPregap);
                        using var input = new FileStream(track.SourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024);
                        input.Position = track.SourceOffsetInFile;
                        byte[] buffer = new byte[track.SourceSectorSize * 256];
                        long remaining = track.SourceSectorCount;
                        while (remaining > 0)
                        {
                            int count = (int)Math.Min(remaining, 256);
                            int bytes = count * track.SourceSectorSize;
                            int available = (int)Math.Min(bytes, track.SourceDataEnd - input.Position);
                            ReadExactly(input, buffer, 0, available);
                            Array.Clear(buffer, available, bytes - available);
                            if (track.SwapAudio)
                            {
                                for (int sector = 0; sector < count; sector++)
                                    for (int i = sector * track.SourceSectorSize; i < sector * track.SourceSectorSize + 2352; i += 2)
                                        (buffer[i], buffer[i + 1]) = (buffer[i + 1], buffer[i]);
                            }
                            if (track.SourceSectorSize == track.OutputSectorSize)
                                output.Write(buffer, 0, bytes);
                            else if (track.Scms)
                            {
                                byte[] sector = new byte[2448];
                                for (int i = 0; i < count; i++)
                                {
                                    Array.Copy(buffer, i * 2352, sector, 0, 2352);
                                    Subchannel.Write(sector.AsSpan(2352), track,
                                        track.AbsoluteStart + track.GeneratedPregap + track.SourceSectorCount - remaining + i);
                                    output.Write(sector);
                                }
                            }
                            else
                            {
                                for (int i = 0; i < count; i++)
                                {
                                    long lba = track.AbsoluteStart + track.GeneratedPregap + track.SourceSectorCount - remaining + i - 150;
                                    byte[] data = buffer.AsSpan(i * track.SourceSectorSize, track.SourceSectorSize).ToArray();
                                    byte[] sector = track.CdiMode == 1 ? Mode1Sector.Build(lba, data) : Mode2Sector.Build(lba, data);
                                    output.Write(sector);
                                }
                            }
                            remaining -= count;
                        }
                        WriteGap(output, track, track.AbsoluteStart + track.GeneratedPregap + track.SourceSectorCount, track.PostgapLength);
                    }
                    WriteDescriptor(output, disc, path);
                    output.Flush(true);
                }
                File.Move(partialPath, path, true);
            }
            finally
            {
                if (File.Exists(partialPath)) File.Delete(partialPath);
            }
        }

        static void WriteGap(Stream output, DiscTrack track, long start, int count)
        {
            for (int i = 0; i < count; i++)
            {
                long lba = start + i - 150;
                byte[] sector = track.CdiMode switch
                {
                    1 => Mode1Sector.Build(lba, new byte[2048]),
                    2 => Mode2Sector.BuildGap(lba),
                    _ => new byte[track.OutputSectorSize]
                };
                if (track.OutputSectorSize == 2448)
                    Subchannel.Write(sector.AsSpan(2352), track, start + i);
                output.Write(sector);
            }
        }

        static void WriteDescriptor(Stream output, DiscLayout disc, string path)
        {
            long start = output.Position;
            using var writer = new BinaryWriter(output, Encoding.UTF8, true);
            byte[] name = Encoding.UTF8.GetBytes(Path.GetFileName(path));
            if (name.Length > 255) name = Encoding.ASCII.GetBytes("image.cdi");
            writer.Write((byte)1);
            WriteSession(writer, disc.Tracks.Count);
            foreach (var track in disc.Tracks)
            {
                WriteHeader(writer, name, disc.Tracks.Count);
                writer.Write((ushort)track.IndexLengths.Count);
                foreach (long length in track.IndexLengths) writer.Write((uint)length);
                writer.Write(0);
                writer.Write((ushort)0);
                writer.Write(track.CdiMode);
                writer.Write(0);
                writer.Write(0);
                writer.Write(track.Number - 1);
                writer.Write((uint)track.AbsoluteStart);
                writer.Write((uint)track.SectorCount);
                writer.Write(new byte[16]);
                writer.Write(track.OutputSectorSize == 2448 ? 4 : 2);
                writer.Write(track.ControlField);
                writer.Write((byte)0);
                writer.Write((uint)track.SectorCount);
                writer.Write(0);
                writer.Write(track.Isrc == null ? new byte[12] : Encoding.ASCII.GetBytes(track.Isrc));
                writer.Write(track.Isrc == null ? 0 : 1);
                byte[] tail = new byte[99];
                Array.Fill(tail, (byte)0xFF, 1, 8);
                tail[9] = 1;
                tail[13] = 0x80;
                tail[17] = 2;
                tail[21] = 16;
                tail[25] = 0x44;
                tail[26] = 0xAC;
                Array.Fill(tail, (byte)0xFF, 71, 4);
                bool last = track == disc.Tracks[^1];
                tail[87] = last ? (byte)disc.SessionType : (byte)0;
                tail[93] = last ? (byte)0 : (byte)1;
                if (!last) { tail[97] = 0xFF; tail[98] = 0xFF; }
                writer.Write(tail);
            }
            WriteSession(writer, 0);
            WriteHeader(writer, name, disc.Tracks.Count);
            writer.Write((uint)disc.TotalSectors);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write(1);
            writer.Write(1);
            writer.Write(disc.Catalog == null ? new byte[13] : Encoding.ASCII.GetBytes(disc.Catalog));
            writer.Write(disc.Catalog == null ? 0 : 1);
            writer.Write(disc.CdText.Length);
            writer.Write(disc.CdText);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0x80000006u);
            writer.Write((uint)(output.Position - start + 4));
        }

        static void WriteSession(BinaryWriter writer, int tracks)
        {
            byte[] header = new byte[15];
            header[1] = (byte)tracks;
            if (tracks > 0)
            {
                header[9] = 1;
                header[13] = 0xFF;
                header[14] = 0xFF;
            }
            writer.Write(header);
        }

        static void WriteHeader(BinaryWriter writer, byte[] name, int tracks)
        {
            writer.Write(new byte[] { 0xFF, 0xFF, 0, 0, 1, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0, (byte)tracks });
            writer.Write((byte)name.Length);
            writer.Write(name);
            byte[] header = new byte[31];
            header[11] = 2;
            header[22] = 0x80;
            header[23] = 0x40;
            header[24] = 0x7E;
            header[25] = 5;
            header[29] = 0x98;
            writer.Write(header);
        }
    }
}
