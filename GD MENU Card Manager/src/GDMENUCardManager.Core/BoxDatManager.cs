using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GDMENUCardManager.Core
{
    public class BoxDatEntry
    {
        public string Name { get; set; } = string.Empty;
        public uint FileNumber { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }

    public class BoxDatManager
    {
        // Constants
        public const uint EntrySize = 0x20020;  // 131,104 bytes
        public const int HeaderSize = 16;
        public const int EntryIndexSize = 16;
        public const int NameFieldLength = 10;
        public const uint StartingFileNumber = 1;

        // State
        public bool IsLoaded { get; private set; }
        public bool HasUnsavedChanges { get; set; }
        public string FilePath { get; private set; } = string.Empty;
        public string LoadError { get; private set; } = string.Empty;

        // Cached data
        private List<BoxDatEntry> _entries = new();
        private HashSet<string> _serialsWithArtwork = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Normalize a serial by stripping non-alphanumeric characters,
        /// converting to uppercase, and truncating to 10 characters.
        /// </summary>
        public static string NormalizeSerial(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial))
                return string.Empty;

            var normalized = new string(serial.Where(char.IsLetterOrDigit).ToArray());

            if (normalized.Length > NameFieldLength)
                normalized = normalized.Substring(0, NameFieldLength);

            return normalized.ToUpperInvariant();
        }

        /// <summary>
        /// Load BOX.DAT from file into memory cache.
        /// </summary>
        public void Load(string boxDatPath)
        {
            IsLoaded = false;
            LoadError = string.Empty;
            _entries.Clear();
            _serialsWithArtwork.Clear();
            FilePath = boxDatPath;

            try
            {
                if (!File.Exists(boxDatPath))
                {
                    LoadError = "BOX.DAT file not found";
                    return;
                }

                using var fs = new FileStream(boxDatPath, FileMode.Open, FileAccess.Read);
                using var reader = new BinaryReader(fs);

                // Validate minimum size for header
                if (fs.Length < HeaderSize)
                {
                    LoadError = "File too small for header";
                    return;
                }

                // Read and validate magic header
                byte[] magic = reader.ReadBytes(4);
                if (magic[0] != 'D' || magic[1] != 'A' || magic[2] != 'T' || magic[3] != 0x01)
                {
                    LoadError = "Invalid magic header (expected DAT\\x01)";
                    return;
                }

                // Read header fields
                uint entrySize = reader.ReadUInt32();
                uint fileCount = reader.ReadUInt32();
                uint reserved = reader.ReadUInt32();

                // Validate entry size
                if (entrySize != EntrySize)
                {
                    LoadError = $"Unexpected entry size 0x{entrySize:X} (expected 0x{EntrySize:X})";
                    return;
                }

                // Validate file size can contain all entries
                long headerAndEntriesSize = HeaderSize + (fileCount * EntryIndexSize);
                if (fs.Length < headerAndEntriesSize)
                {
                    LoadError = "File truncated - cannot contain all entry headers";
                    return;
                }

                // Read all entries
                fs.Seek(HeaderSize, SeekOrigin.Begin);
                for (int i = 0; i < fileCount; i++)
                {
                    // Read entry index (16 bytes: 10 name + 2 reserved + 4 fileNumber)
                    byte[] nameBytes = reader.ReadBytes(NameFieldLength);
                    string entryName = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0').Trim();
                    reader.ReadBytes(2);  // Skip reserved
                    uint fileNumber = reader.ReadUInt32();

                    var entry = new BoxDatEntry
                    {
                        Name = entryName,
                        FileNumber = fileNumber
                    };

                    // Calculate and validate data offset
                    long dataOffset = entrySize * fileNumber;
                    if (dataOffset + entrySize > fs.Length)
                    {
                        LoadError = $"Entry '{entryName}' data offset 0x{dataOffset:X} exceeds file size";
                        return;
                    }

                    // Read entry data
                    long savedPos = fs.Position;
                    fs.Seek(dataOffset, SeekOrigin.Begin);
                    entry.Data = reader.ReadBytes((int)entrySize);
                    fs.Seek(savedPos, SeekOrigin.Begin);

                    _entries.Add(entry);
                    _serialsWithArtwork.Add(entryName);
                }

                IsLoaded = true;
                HasUnsavedChanges = false;
            }
            catch (Exception ex)
            {
                LoadError = $"Error reading file: {ex.Message}";
            }
        }

        /// <summary>
        /// Check if artwork exists for the given serial.
        /// Applies Table 2 artwork translation before lookup.
        /// </summary>
        public bool HasArtworkForSerial(string serial)
        {
            // Apply Table 2 translation for artwork lookup
            var artworkSerial = SerialTranslator.TranslateForArtwork(serial);
            var normalized = NormalizeSerial(artworkSerial);
            if (string.IsNullOrEmpty(normalized))
                return false;
            return _serialsWithArtwork.Contains(normalized);
        }

        /// <summary>
        /// Get PVR data for the given serial, or null if not found.
        /// Applies Table 2 artwork translation before lookup.
        /// </summary>
        public byte[] GetPvrDataForSerial(string serial)
        {
            // Apply Table 2 translation for artwork lookup
            var artworkSerial = SerialTranslator.TranslateForArtwork(serial);
            var normalized = NormalizeSerial(artworkSerial);
            if (string.IsNullOrEmpty(normalized))
                return null;

            var entry = _entries.FirstOrDefault(e =>
                e.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase));
            return entry?.Data;
        }

        /// <summary>
        /// Set or replace artwork for the given serial.
        /// Applies Table 2 artwork translation before storing.
        /// </summary>
        public void SetArtworkForSerial(string serial, byte[] pvrData)
        {
            // Apply Table 2 translation for artwork storage
            var artworkSerial = SerialTranslator.TranslateForArtwork(serial);
            var normalized = NormalizeSerial(artworkSerial);
            if (string.IsNullOrEmpty(normalized))
                throw new ArgumentException("Serial cannot be empty");

            if (pvrData == null || pvrData.Length != EntrySize)
                throw new ArgumentException($"PVR data must be exactly {EntrySize} bytes");

            var existingEntry = _entries.FirstOrDefault(e =>
                e.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase));

            if (existingEntry != null)
            {
                // Replace existing entry's data
                existingEntry.Data = pvrData;
            }
            else
            {
                // Add new entry
                var newEntry = new BoxDatEntry
                {
                    Name = normalized,
                    FileNumber = 0,  // Will be assigned during save
                    Data = pvrData
                };
                _entries.Add(newEntry);
                _serialsWithArtwork.Add(normalized);
            }

            HasUnsavedChanges = true;
        }

        /// <summary>
        /// Delete artwork entry for the given serial.
        /// Applies Table 2 artwork translation before deletion.
        /// </summary>
        public void DeleteEntryForSerial(string serial)
        {
            // Apply Table 2 translation for artwork deletion
            var artworkSerial = SerialTranslator.TranslateForArtwork(serial);
            var normalized = NormalizeSerial(artworkSerial);
            if (string.IsNullOrEmpty(normalized))
                return;

            var entry = _entries.FirstOrDefault(e =>
                e.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase));

            if (entry != null)
            {
                _entries.Remove(entry);
                _serialsWithArtwork.Remove(normalized);
                HasUnsavedChanges = true;
            }
        }

        /// <summary>
        /// Save BOX.DAT to the specified path.
        /// </summary>
        public void Save(string outputPath)
        {
            if (File.Exists(outputPath))
                Helper.TryMakeWritable(outputPath);

            using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(fs);

            // Calculate starting file number to avoid index/data overlap
            // Index area = HeaderSize + (entry_count * EntryIndexSize)
            // First data offset = EntrySize * starting_file_num
            // We need: EntrySize * starting_file_num >= HeaderSize + entry_count * EntryIndexSize
            long indexAreaSize = HeaderSize + (_entries.Count * EntryIndexSize);
            uint startingFileNum = (uint)Math.Max(StartingFileNumber,
                (int)Math.Ceiling((double)indexAreaSize / EntrySize));

            // Write header
            writer.Write((byte)'D');
            writer.Write((byte)'A');
            writer.Write((byte)'T');
            writer.Write((byte)0x01);
            writer.Write(EntrySize);
            writer.Write((uint)_entries.Count);
            writer.Write((uint)0);  // Reserved

            // Assign file numbers and write entry index
            for (int i = 0; i < _entries.Count; i++)
            {
                _entries[i].FileNumber = startingFileNum + (uint)i;

                // Write entry: 10 bytes name (padded), 2 bytes reserved, 4 bytes file number
                byte[] nameBytes = new byte[NameFieldLength];
                byte[] nameAscii = Encoding.ASCII.GetBytes(_entries[i].Name);
                Array.Copy(nameAscii, nameBytes, Math.Min(nameAscii.Length, NameFieldLength));
                writer.Write(nameBytes);
                writer.Write((ushort)0);  // Reserved
                writer.Write(_entries[i].FileNumber);
            }

            // Pad to first data offset if needed
            long firstDataOffset = EntrySize * startingFileNum;
            long currentPos = fs.Position;
            if (currentPos < firstDataOffset)
            {
                byte[] padding = new byte[firstDataOffset - currentPos];
                writer.Write(padding);
            }

            // Write entry data
            for (int i = 0; i < _entries.Count; i++)
            {
                long expectedOffset = EntrySize * _entries[i].FileNumber;
                fs.Seek(expectedOffset, SeekOrigin.Begin);
                writer.Write(_entries[i].Data);
            }

            HasUnsavedChanges = false;
        }

        /// <summary>
        /// Backup existing BOX.DAT and save new version.
        /// Returns (success, errorMessage). If backup fails, errorMessage contains the reason.
        /// </summary>
        public (bool success, string errorMessage) BackupAndSave(
            string boxDatPath,
            string backupFolder,
            bool proceedWithoutBackupOnFailure)
        {
            string backupError = string.Empty;
            bool backupSuccess = true;

            // Create backup
            try
            {
                if (!Directory.Exists(backupFolder))
                    Directory.CreateDirectory(backupFolder);

                if (File.Exists(boxDatPath))
                {
                    string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                    string backupFileName = $"BOX_{timestamp}.DAT";
                    string backupPath = Path.Combine(backupFolder, backupFileName);
                    File.Copy(boxDatPath, backupPath);
                }
            }
            catch (Exception ex)
            {
                backupSuccess = false;
                backupError = ex.Message;
            }

            if (!backupSuccess && !proceedWithoutBackupOnFailure)
            {
                return (false, $"Failed to create backup: {backupError}");
            }

            // Save new BOX.DAT
            try
            {
                Save(boxDatPath);
                return (true, backupSuccess ? string.Empty : $"Warning: Backup failed ({backupError}), but BOX.DAT was saved.");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to save BOX.DAT: {ex.Message}");
            }
        }

        /// <summary>
        /// Get count of entries.
        /// </summary>
        public int EntryCount => _entries.Count;

        /// <summary>
        /// Get all serial names in this DAT file.
        /// </summary>
        public HashSet<string> GetAllSerials()
        {
            return new HashSet<string>(_serialsWithArtwork, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Get all entries (for regenerating ICON.DAT from BOX.DAT).
        /// </summary>
        public IReadOnlyList<BoxDatEntry> GetAllEntries()
        {
            return _entries.AsReadOnly();
        }

        /// <summary>
        /// Create an empty but valid BOX.DAT file.
        /// Uses the standard entry size (0x20020) with file_count=0.
        /// </summary>
        public static void CreateEmptyFile(string outputPath)
        {
            if (File.Exists(outputPath))
                Helper.TryMakeWritable(outputPath);

            using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(fs);

            // Write header (16 bytes)
            writer.Write((byte)'D');
            writer.Write((byte)'A');
            writer.Write((byte)'T');
            writer.Write((byte)0x01);
            writer.Write(EntrySize);   // 0x20020 - standard entry size
            writer.Write((uint)0);     // file_count = 0
            writer.Write((uint)0);     // Reserved
        }
    }
}
