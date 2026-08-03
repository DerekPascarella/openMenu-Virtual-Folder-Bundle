using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GDMENUCardManager.Core
{
    /// <summary>
    /// Manages FOLDRART.DAT, which stores 256x256 box art for openMenu folders.
    /// Same container format as BOX.DAT, but entries are keyed by a hash of the
    /// folder path ("F" + 8 hex digits of FNV-1a 32) since the 10-byte ID field
    /// cannot hold a path. A FOLDRART.MAP sidecar records key=path lines so the
    /// original folder names stay known even after a folder disappears.
    /// </summary>
    public class FolderArtDatManager
    {
        public const uint EntrySize = BoxDatManager.EntrySize;
        public const int HeaderSize = BoxDatManager.HeaderSize;
        public const int EntryIndexSize = BoxDatManager.EntryIndexSize;
        public const int NameFieldLength = BoxDatManager.NameFieldLength;
        public const uint StartingFileNumber = BoxDatManager.StartingFileNumber;

        public bool IsLoaded { get; private set; }
        public bool HasUnsavedChanges { get; set; }
        public string FilePath { get; private set; } = string.Empty;
        public string LoadError { get; private set; } = string.Empty;

        private List<BoxDatEntry> _entries = new();

        // Key -> original folder path, mirrored to FOLDRART.MAP.
        private Dictionary<string, string> _keyToPath = new(StringComparer.Ordinal);

        /// <summary>
        /// Hashes a folder path into a DAT entry key: "F" + FNV-1a 32 as 8 uppercase
        /// hex digits. The path must be the exact string written to OPENMENU.INI
        /// (backslash separators, already normalized by GdItem.CleanFolderPath).
        /// openMenu computes the same hash at runtime, so this must never change.
        /// </summary>
        public static string KeyForPath(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
                return null;

            uint h = 2166136261;
            foreach (char c in folderPath)
            {
                h ^= (byte)c;
                h *= 16777619;
            }

            return "F" + h.ToString("X8");
        }

        public void Load(string datPath, string mapPath)
        {
            IsLoaded = false;
            LoadError = string.Empty;
            _entries.Clear();
            _keyToPath.Clear();
            FilePath = datPath;

            LoadMap(mapPath);

            try
            {
                if (!File.Exists(datPath))
                {
                    LoadError = "FOLDRART.DAT file not found";
                    return;
                }

                using var fs = new FileStream(datPath, FileMode.Open, FileAccess.Read);
                using var reader = new BinaryReader(fs);

                if (fs.Length < HeaderSize)
                {
                    LoadError = "File too small for header";
                    return;
                }

                byte[] magic = reader.ReadBytes(4);
                if (magic[0] != 'D' || magic[1] != 'A' || magic[2] != 'T' || magic[3] != 0x01)
                {
                    LoadError = "Invalid magic header (expected DAT\\x01)";
                    return;
                }

                uint entrySize = reader.ReadUInt32();
                uint fileCount = reader.ReadUInt32();
                reader.ReadUInt32();

                if (entrySize != EntrySize)
                {
                    LoadError = $"Unexpected entry size 0x{entrySize:X} (expected 0x{EntrySize:X})";
                    return;
                }

                long headerAndEntriesSize = HeaderSize + (fileCount * EntryIndexSize);
                if (fs.Length < headerAndEntriesSize)
                {
                    LoadError = "File truncated - cannot contain all entry headers";
                    return;
                }

                fs.Seek(HeaderSize, SeekOrigin.Begin);
                for (int i = 0; i < fileCount; i++)
                {
                    byte[] nameBytes = reader.ReadBytes(NameFieldLength);
                    string entryName = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0').Trim();
                    reader.ReadBytes(2);
                    uint fileNumber = reader.ReadUInt32();

                    var entry = new BoxDatEntry
                    {
                        Name = entryName,
                        FileNumber = fileNumber
                    };

                    long dataOffset = entrySize * fileNumber;
                    if (dataOffset + entrySize > fs.Length)
                    {
                        LoadError = $"Entry '{entryName}' data offset 0x{dataOffset:X} exceeds file size";
                        return;
                    }

                    long savedPos = fs.Position;
                    fs.Seek(dataOffset, SeekOrigin.Begin);
                    entry.Data = reader.ReadBytes((int)entrySize);
                    fs.Seek(savedPos, SeekOrigin.Begin);

                    _entries.Add(entry);
                }

                // Drop map lines that no longer have a DAT entry behind them
                var validKeys = new HashSet<string>(_entries.Select(e => e.Name), StringComparer.Ordinal);
                foreach (var stale in _keyToPath.Keys.Where(k => !validKeys.Contains(k)).ToList())
                    _keyToPath.Remove(stale);

                IsLoaded = true;
                HasUnsavedChanges = false;
            }
            catch (Exception ex)
            {
                LoadError = $"Error reading file: {ex.Message}";
            }
        }

        private void LoadMap(string mapPath)
        {
            try
            {
                if (!File.Exists(mapPath))
                    return;

                foreach (var line in File.ReadAllLines(mapPath))
                {
                    var idx = line.IndexOf('=');
                    if (idx <= 0)
                        continue;

                    var key = line.Substring(0, idx).Trim();
                    var path = line.Substring(idx + 1).Trim();

                    if (key.Length == 0 || path.Length == 0)
                        continue;

                    _keyToPath[key] = path;
                }
            }
            catch
            {
                // A broken map only costs display names, never block loading the DAT
            }
        }

        public bool HasArtworkForFolder(string folderPath)
        {
            var key = KeyForPath(folderPath);
            return key != null && _entries.Any(e => e.Name.Equals(key, StringComparison.Ordinal));
        }

        public byte[] GetPvrDataForFolder(string folderPath)
        {
            return GetPvrDataForKey(KeyForPath(folderPath));
        }

        public byte[] GetPvrDataForKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;

            return _entries.FirstOrDefault(e => e.Name.Equals(key, StringComparison.Ordinal))?.Data;
        }

        public string GetPathForKey(string key)
        {
            if (key != null && _keyToPath.TryGetValue(key, out var path))
                return path;
            return null;
        }

        public void SetArtworkForFolder(string folderPath, byte[] pvrData)
        {
            var key = KeyForPath(folderPath);
            if (key == null)
                throw new ArgumentException("Folder path cannot be empty");

            if (pvrData == null || pvrData.Length != EntrySize)
                throw new ArgumentException($"PVR data must be exactly {EntrySize} bytes");

            // Two different paths hashing to the same key would silently share art.
            // Practically unreachable, but cheap to refuse outright.
            if (_keyToPath.TryGetValue(key, out var existingPath) &&
                !existingPath.Equals(folderPath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Folder path hash collision: \"{folderPath}\" and \"{existingPath}\" share key {key}. " +
                    "Rename one of the folders to work around this.");
            }

            var existingEntry = _entries.FirstOrDefault(e => e.Name.Equals(key, StringComparison.Ordinal));
            if (existingEntry != null)
            {
                existingEntry.Data = pvrData;
            }
            else
            {
                _entries.Add(new BoxDatEntry
                {
                    Name = key,
                    FileNumber = 0,
                    Data = pvrData
                });
            }

            _keyToPath[key] = folderPath;
            HasUnsavedChanges = true;
        }

        public void DeleteEntryForFolder(string folderPath)
        {
            DeleteEntryForKey(KeyForPath(folderPath));
        }

        public void DeleteEntryForKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;

            var entry = _entries.FirstOrDefault(e => e.Name.Equals(key, StringComparison.Ordinal));
            if (entry != null)
            {
                _entries.Remove(entry);
                _keyToPath.Remove(key);
                HasUnsavedChanges = true;
            }
        }

        /// <summary>
        /// Moves an entry from one folder path to another, used when a folder is
        /// renamed so its art follows. Overwrites any art already on the new path.
        /// Returns false if the old path had no entry.
        /// </summary>
        public bool RekeyFolder(string oldPath, string newPath)
        {
            var oldKey = KeyForPath(oldPath);
            var newKey = KeyForPath(newPath);
            if (oldKey == null || newKey == null || oldKey == newKey)
                return false;

            var entry = _entries.FirstOrDefault(e => e.Name.Equals(oldKey, StringComparison.Ordinal));
            if (entry == null)
                return false;

            DeleteEntryForKey(newKey);

            entry.Name = newKey;
            _keyToPath.Remove(oldKey);
            _keyToPath[newKey] = newPath;
            HasUnsavedChanges = true;
            return true;
        }

        /// <summary>
        /// Applies a batch of path moves. All source entries are lifted out before any
        /// are written back, so swapped folder names can't clobber each other's art.
        /// </summary>
        public void ApplyRekeys(IEnumerable<(string OldPath, string NewPath)> moves)
        {
            var staged = new List<(string OldPath, string NewPath, byte[] Data)>();

            foreach (var move in moves)
            {
                var data = GetPvrDataForFolder(move.OldPath);
                if (data == null)
                    continue;

                DeleteEntryForFolder(move.OldPath);
                staged.Add((move.OldPath, move.NewPath, data));
            }

            foreach (var entry in staged)
            {
                try
                {
                    SetArtworkForFolder(entry.NewPath, entry.Data);
                }
                catch (InvalidOperationException)
                {
                    // Hash collision on the target path, put the art back where it was
                    SetArtworkForFolder(entry.OldPath, entry.Data);
                }
            }
        }

        /// <summary>
        /// Moves an orphaned entry onto a current folder. The stored art is the only
        /// copy, so this is the recovery path when the source image file is gone.
        /// </summary>
        public bool ReassignKeyToFolder(string orphanKey, string newFolderPath)
        {
            var entry = _entries.FirstOrDefault(e => e.Name.Equals(orphanKey, StringComparison.Ordinal));
            if (entry == null)
                return false;

            var newKey = KeyForPath(newFolderPath);
            if (newKey == null)
                return false;

            if (newKey != orphanKey)
            {
                DeleteEntryForKey(newKey);
                entry.Name = newKey;
                _keyToPath.Remove(orphanKey);
            }

            _keyToPath[newKey] = newFolderPath;
            HasUnsavedChanges = true;
            return true;
        }

        public int EntryCount => _entries.Count;

        public List<string> GetAllKeys()
        {
            return _entries.Select(e => e.Name).ToList();
        }

        /// <summary>
        /// Entries whose folder path no longer exists in the current item list.
        /// </summary>
        public List<(string Key, string Path)> GetOrphans(IEnumerable<string> currentFolderPaths)
        {
            var currentKeys = new HashSet<string>(
                currentFolderPaths.Select(KeyForPath).Where(k => k != null),
                StringComparer.Ordinal);

            return _entries
                .Where(e => !currentKeys.Contains(e.Name))
                .Select(e => (e.Name, GetPathForKey(e.Name)))
                .ToList();
        }

        public void Save(string datPath, string mapPath)
        {
            if (File.Exists(datPath))
                Helper.TryMakeWritable(datPath);

            using (var fs = new FileStream(datPath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(fs))
            {
                long indexAreaSize = HeaderSize + (_entries.Count * EntryIndexSize);
                uint startingFileNum = (uint)Math.Max(StartingFileNumber,
                    (int)Math.Ceiling((double)indexAreaSize / EntrySize));

                writer.Write((byte)'D');
                writer.Write((byte)'A');
                writer.Write((byte)'T');
                writer.Write((byte)0x01);
                writer.Write(EntrySize);
                writer.Write((uint)_entries.Count);
                writer.Write((uint)0);

                for (int i = 0; i < _entries.Count; i++)
                {
                    _entries[i].FileNumber = startingFileNum + (uint)i;

                    byte[] nameBytes = new byte[NameFieldLength];
                    byte[] nameAscii = Encoding.ASCII.GetBytes(_entries[i].Name);
                    Array.Copy(nameAscii, nameBytes, Math.Min(nameAscii.Length, NameFieldLength));
                    writer.Write(nameBytes);
                    writer.Write((ushort)0);
                    writer.Write(_entries[i].FileNumber);
                }

                long firstDataOffset = EntrySize * startingFileNum;
                long currentPos = fs.Position;
                if (currentPos < firstDataOffset)
                {
                    byte[] padding = new byte[firstDataOffset - currentPos];
                    writer.Write(padding);
                }

                for (int i = 0; i < _entries.Count; i++)
                {
                    long expectedOffset = EntrySize * _entries[i].FileNumber;
                    fs.Seek(expectedOffset, SeekOrigin.Begin);
                    writer.Write(_entries[i].Data);
                }
            }

            SaveMap(mapPath);
            HasUnsavedChanges = false;
        }

        private void SaveMap(string mapPath)
        {
            if (File.Exists(mapPath))
                Helper.TryMakeWritable(mapPath);

            var sb = new StringBuilder();
            foreach (var entry in _entries)
            {
                if (_keyToPath.TryGetValue(entry.Name, out var path))
                    sb.Append(entry.Name).Append('=').AppendLine(path);
            }

            File.WriteAllText(mapPath, sb.ToString());
        }

        public (bool success, string errorMessage) BackupAndSave(
            string datPath,
            string mapPath,
            string backupFolder,
            bool proceedWithoutBackupOnFailure)
        {
            string backupError = string.Empty;
            bool backupSuccess = true;

            try
            {
                if (!Directory.Exists(backupFolder))
                    Directory.CreateDirectory(backupFolder);

                string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");

                if (File.Exists(datPath))
                    File.Copy(datPath, Path.Combine(backupFolder, $"FOLDRART_{timestamp}.DAT"));
                if (File.Exists(mapPath))
                    File.Copy(mapPath, Path.Combine(backupFolder, $"FOLDRART_{timestamp}.MAP"));
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

            try
            {
                Save(datPath, mapPath);
                return (true, backupSuccess ? string.Empty : $"Warning: Backup failed ({backupError}), but FOLDRART.DAT was saved.");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to save FOLDRART.DAT: {ex.Message}");
            }
        }
    }
}
