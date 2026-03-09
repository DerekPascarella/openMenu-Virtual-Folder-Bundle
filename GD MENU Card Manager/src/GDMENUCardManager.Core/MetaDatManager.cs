using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GDMENUCardManager.Core
{
    /// <summary>
    /// Represents a single entry in META.DAT containing game metadata.
    /// </summary>
    public class MetaDatEntry
    {
        public string Name { get; set; } = string.Empty;
        public uint FileNumber { get; set; }

        // Metadata fields (8 bytes of structured data + 376 bytes description)
        public byte NumPlayers { get; set; }
        public byte VmuBlocks { get; set; }
        public ushort Accessories { get; set; }
        public byte Network { get; set; }
        public ushort Genre { get; set; }
        public byte Padding { get; set; }
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Raw 384-byte data block for lossless round-trip.
        /// </summary>
        public byte[] RawData { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// Manages META.DAT files for openMenu metadata storage.
    /// </summary>
    public class MetaDatManager
    {
        // Constants
        public const uint EntrySize = 0x180;  // 384 bytes
        public const int HeaderSize = 16;
        public const int EntryIndexSize = 16;
        public const int NameFieldLength = 10;
        public const uint StartingFileNumber = 1;
        public const int DescriptionLength = 376;

        // State
        public bool IsLoaded { get; private set; }
        public bool HasUnsavedChanges { get; set; }
        public string FilePath { get; private set; } = string.Empty;
        public string LoadError { get; private set; } = string.Empty;

        // Cached data
        private List<MetaDatEntry> _entries = new();
        private HashSet<string> _serialsWithMetadata = new(StringComparer.OrdinalIgnoreCase);

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
        /// Load META.DAT from file into memory cache.
        /// </summary>
        public void Load(string metaDatPath)
        {
            IsLoaded = false;
            LoadError = string.Empty;
            _entries.Clear();
            _serialsWithMetadata.Clear();
            FilePath = metaDatPath;

            try
            {
                if (!File.Exists(metaDatPath))
                {
                    LoadError = "META.DAT file not found";
                    return;
                }

                using var fs = new FileStream(metaDatPath, FileMode.Open, FileAccess.Read);
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

                    // Skip entries with empty names (placeholder entries)
                    if (string.IsNullOrEmpty(entryName))
                        continue;

                    var entry = new MetaDatEntry
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
                    entry.RawData = reader.ReadBytes((int)entrySize);

                    // Parse the metadata fields from raw data
                    ParseEntryData(entry);

                    fs.Seek(savedPos, SeekOrigin.Begin);

                    _entries.Add(entry);
                    _serialsWithMetadata.Add(entryName);
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
        /// Parse metadata fields from the raw 384-byte data block.
        /// </summary>
        private void ParseEntryData(MetaDatEntry entry)
        {
            if (entry.RawData == null || entry.RawData.Length < EntrySize)
                return;

            entry.NumPlayers = entry.RawData[0];
            entry.VmuBlocks = entry.RawData[1];
            entry.Accessories = BitConverter.ToUInt16(entry.RawData, 2);
            entry.Network = entry.RawData[4];
            entry.Genre = BitConverter.ToUInt16(entry.RawData, 5);
            entry.Padding = entry.RawData[7];

            // Description is 376 bytes starting at offset 8
            int descEnd = 8;
            while (descEnd < EntrySize && entry.RawData[descEnd] != 0)
                descEnd++;
            entry.Description = Encoding.ASCII.GetString(entry.RawData, 8, descEnd - 8);
        }

        /// <summary>
        /// Check if metadata exists for the given serial.
        /// </summary>
        public bool HasEntryForSerial(string serial)
        {
            var normalized = NormalizeSerial(serial);
            if (string.IsNullOrEmpty(normalized))
                return false;
            return _serialsWithMetadata.Contains(normalized);
        }

        /// <summary>
        /// Get metadata entry for the given serial, or null if not found.
        /// </summary>
        public MetaDatEntry GetEntryForSerial(string serial)
        {
            var normalized = NormalizeSerial(serial);
            if (string.IsNullOrEmpty(normalized))
                return null;

            return _entries.FirstOrDefault(e =>
                e.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Get raw data for the given serial, or null if not found.
        /// </summary>
        public byte[] GetRawDataForSerial(string serial)
        {
            var entry = GetEntryForSerial(serial);
            return entry?.RawData;
        }

        /// <summary>
        /// Set or replace metadata for the given serial using raw data.
        /// </summary>
        public void SetEntryForSerial(string serial, byte[] rawData)
        {
            var normalized = NormalizeSerial(serial);
            if (string.IsNullOrEmpty(normalized))
                throw new ArgumentException("Serial cannot be empty");

            if (rawData == null || rawData.Length != EntrySize)
                throw new ArgumentException($"Raw data must be exactly {EntrySize} bytes");

            var existingEntry = _entries.FirstOrDefault(e =>
                e.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase));

            if (existingEntry != null)
            {
                // Replace existing entry's data
                existingEntry.RawData = rawData;
                ParseEntryData(existingEntry);
            }
            else
            {
                // Add new entry
                var newEntry = new MetaDatEntry
                {
                    Name = normalized,
                    FileNumber = 0,  // Will be assigned during save
                    RawData = rawData
                };
                ParseEntryData(newEntry);
                _entries.Add(newEntry);
                _serialsWithMetadata.Add(normalized);
            }

            HasUnsavedChanges = true;
        }

        /// <summary>
        /// Set or replace metadata for the given serial using a MetaDatEntry.
        /// </summary>
        public void SetEntryForSerial(string serial, MetaDatEntry sourceEntry)
        {
            if (sourceEntry?.RawData != null && sourceEntry.RawData.Length == EntrySize)
            {
                SetEntryForSerial(serial, sourceEntry.RawData);
            }
        }

        /// <summary>
        /// Delete metadata entry for the given serial.
        /// </summary>
        public void DeleteEntryForSerial(string serial)
        {
            var normalized = NormalizeSerial(serial);
            if (string.IsNullOrEmpty(normalized))
                return;

            var entry = _entries.FirstOrDefault(e =>
                e.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase));

            if (entry != null)
            {
                _entries.Remove(entry);
                _serialsWithMetadata.Remove(normalized);
                HasUnsavedChanges = true;
            }
        }

        /// <summary>
        /// Save META.DAT to the specified path.
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
                writer.Write(_entries[i].RawData);
            }

            HasUnsavedChanges = false;
        }

        /// <summary>
        /// Backup existing META.DAT and save new version.
        /// Returns (success, errorMessage). If backup fails, errorMessage contains the reason.
        /// </summary>
        public (bool success, string errorMessage) BackupAndSave(
            string metaDatPath,
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

                if (File.Exists(metaDatPath))
                {
                    string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                    string backupFileName = $"META_{timestamp}.DAT";
                    string backupPath = Path.Combine(backupFolder, backupFileName);
                    File.Copy(metaDatPath, backupPath);
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

            // Save new META.DAT
            try
            {
                Save(metaDatPath);
                return (true, backupSuccess ? string.Empty : $"Warning: Backup failed ({backupError}), but META.DAT was saved.");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to save META.DAT: {ex.Message}");
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
            return new HashSet<string>(_serialsWithMetadata, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Get all entries.
        /// </summary>
        public IReadOnlyList<MetaDatEntry> GetAllEntries()
        {
            return _entries.AsReadOnly();
        }

        /// <summary>
        /// Merge entries from another MetaDatManager.
        /// </summary>
        /// <param name="source">Source MetaDatManager to merge from</param>
        /// <param name="overwriteExisting">If true, overwrite existing entries; if false, only add missing entries</param>
        /// <returns>Number of entries merged</returns>
        public int MergeFrom(MetaDatManager source, bool overwriteExisting)
        {
            if (source == null || !source.IsLoaded)
                return 0;

            int mergedCount = 0;
            foreach (var sourceEntry in source._entries)
            {
                var normalized = NormalizeSerial(sourceEntry.Name);
                if (string.IsNullOrEmpty(normalized))
                    continue;

                bool exists = HasEntryForSerial(normalized);

                if (!exists || overwriteExisting)
                {
                    SetEntryForSerial(normalized, sourceEntry.RawData);
                    mergedCount++;
                }
            }

            return mergedCount;
        }

        /// <summary>
        /// Clear all entries and reset to empty state.
        /// </summary>
        public void Clear()
        {
            _entries.Clear();
            _serialsWithMetadata.Clear();
            HasUnsavedChanges = true;
        }

        /// <summary>
        /// Create an empty but valid META.DAT file (bare minimum structure).
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
            writer.Write(EntrySize);   // 0x180 - standard entry size
            writer.Write((uint)1);     // file_count = 1 (placeholder entry)
            writer.Write((uint)0);     // Reserved

            // Write placeholder entry index (16 bytes)
            // Empty name (10 bytes of zeros)
            writer.Write(new byte[NameFieldLength]);
            writer.Write((ushort)0);   // Reserved
            writer.Write((uint)1);     // FileNumber = 1

            // Pad to first data offset (0x180 = 384 bytes)
            long currentPos = fs.Position;
            long firstDataOffset = EntrySize * 1;
            if (currentPos < firstDataOffset)
            {
                byte[] padding = new byte[firstDataOffset - currentPos];
                writer.Write(padding);
            }

            // Write empty data block (384 bytes of zeros)
            writer.Write(new byte[EntrySize]);
        }
    }
}
