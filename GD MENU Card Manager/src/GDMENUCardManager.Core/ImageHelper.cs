using Aaru.CommonTypes;
using Aaru.CommonTypes.Interfaces;
using Aaru.Filesystems;
using DiscUtils.Iso9660;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace GDMENUCardManager.Core
{
    /// <summary>
    /// Controls whether archive metadata is read while adding a row.
    /// </summary>
    public enum ArchiveAddMode
    {
        ParseNow,
        DeferToSave,
        Cancel
    }

    public static class ImageHelper
    {
        private static readonly char[] katanachar = "SEGA SEGAKATANA SEGA ENTERPRISES".ToCharArray();

        public static async Task<GdItem> CreateGdItemAsync(
            string fileOrFolderPath,
            ArchiveAddMode archiveAddMode = ArchiveAddMode.ParseNow)
        {
            string folderPath;
            string[] files;

            FileAttributes attr = await Helper.GetAttributesAsync(fileOrFolderPath);//path is a file or folder?
            if (attr.HasFlag(FileAttributes.Directory))
            {
                folderPath = fileOrFolderPath;
                files = await Helper.GetFilesAsync(folderPath);
            }
            else
            {
                folderPath = Path.GetDirectoryName(fileOrFolderPath);
                files = new string[] { fileOrFolderPath };
            }

            var item = new GdItem
            {
                Guid = Guid.NewGuid().ToString(),
                FullFolderPath = folderPath,
                FileFormat = FileFormat.Uncompressed
            };

            IpBin ip = null;
            string itemImageFile = null;

            // Is uncompressed?
            foreach (var file in files)
            {
                var fileExt = Path.GetExtension(file).ToLower();
                if (Manager.supportedImageFormats.Any(x => x == fileExt))
                {
                    itemImageFile = file;
                    break;
                }
            }

            // Is compressed?
            string archiveSidecarName = null;
            string archiveSidecarSerial = null;
            if (itemImageFile == null && files.Any(Helper.CompressedFileExpression))
            {
                string compressedFile = files.First(Helper.CompressedFileExpression);

                var archiveEntries = await Task.Run(() =>
                    Helper.DependencyManager.GetArchiveEntries(compressedFile));
                var imageEntries = archiveEntries
                    .Where(entry => Manager.supportedImageFormats.Any(format =>
                        format.Equals(
                            Path.GetExtension(entry.FullName),
                            StringComparison.OrdinalIgnoreCase)))
                    .ToArray();

                item.ArchiveImageEntries = Array.AsReadOnly(imageEntries);
                item.SelectedArchiveEntry = imageEntries.FirstOrDefault();
                itemImageFile = item.SelectedArchiveEntry?.FullName;
                item.CanApplyGDIShrink = item.SelectedArchiveEntry != null &&
                    Path.GetExtension(item.SelectedArchiveEntry.FullName)
                        .Equals(".gdi", StringComparison.OrdinalIgnoreCase);

                if (!string.IsNullOrEmpty(itemImageFile))
                {
                    item.ImageFiles.Add(Path.GetFileName(compressedFile));

                    var itemName = Path.GetFileNameWithoutExtension(compressedFile);
                    var m = RegularExpressions.TosecnNameRegexp.Match(itemName);
                    if (m.Success)
                        itemName = itemName.Substring(0, m.Index);

                    if (archiveAddMode != ArchiveAddMode.DeferToSave)
                    {
                        ReadOnlyMemory<byte>? gdiManifestBytes = null;
                        if (Path.GetExtension(itemImageFile).Equals(".gdi", StringComparison.OrdinalIgnoreCase))
                        {
                            ip = await ArchiveIpBinReader.TryReadLateGdiTrackOneAsync(
                                compressedFile,
                                archiveEntries,
                                item.SelectedArchiveEntry);
                            if (ip == null)
                            {
                                var gdiInspection = await LegacyRedumpGdiDetector.InspectGdiInArchiveAsync(
                                    compressedFile,
                                    archiveEntries,
                                    item.SelectedArchiveEntry);

                                if (gdiInspection.IsLegacy)
                                    throw new UnsupportedDiscFormatException(LegacyRedumpGdiDetector.ShortMessage);

                                if (gdiInspection.ReadSucceeded)
                                    gdiManifestBytes = gdiInspection.ManifestBytes;
                            }
                        }
                        else if (Path.GetExtension(itemImageFile).Equals(
                                     ".cdi",
                                     StringComparison.OrdinalIgnoreCase) ||
                                 Path.GetExtension(itemImageFile).Equals(
                                     ".ccd",
                                     StringComparison.OrdinalIgnoreCase) ||
                                 Path.GetExtension(itemImageFile).Equals(
                                     ".mds",
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            ip = await ArchiveBoundedDiscMetadataReader.TryReadAsync(
                                compressedFile,
                                archiveEntries,
                                item.SelectedArchiveEntry);
                        }

                        if (ip == null)
                        {
                            ip = await ArchiveIpBinReader.TryReadAsync(
                                compressedFile,
                                archiveEntries,
                                item.SelectedArchiveEntry,
                                gdiManifestBytes);
                        }

                        if (ip == null &&
                            Path.GetExtension(itemImageFile).Equals(
                                ".cue",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            ip = await ArchivePlayStationMetadataReader.TryReadAsync(
                                compressedFile,
                                archiveEntries,
                                item.SelectedArchiveEntry);
                            if (ip != null)
                                item.DiscType = "PSX";
                        }
                    }

                    bool archiveMetadataParsed = ip != null;
                    if (ip == null)
                    {
                        ip = new IpBin
                        {
                            Name = itemName,
                            Disc = "?/?",
                            ProductNumber = String.Empty
                        };
                    }

                    // Sidecar text files inside the archive override the item
                    // fields below, like the ones beside an uncompressed image
                    // do. The parsed Ip keeps the disc's real identity.
                    if (archiveAddMode != ArchiveAddMode.DeferToSave)
                    {
                        archiveSidecarName = await ReadArchiveSidecarTextAsync(
                            compressedFile,
                            archiveEntries,
                            item.SelectedArchiveEntry,
                            Constants.NameTextFile);

                        archiveSidecarSerial = await ReadArchiveSidecarTextAsync(
                            compressedFile,
                            archiveEntries,
                            item.SelectedArchiveEntry,
                            Constants.SerialTextFile);
                    }

                    item.Length = ByteSizeLib.ByteSize.FromBytes(archiveEntries.Sum(entry => entry.Size));
                    item.FileFormat = FileFormat.SevenZip;
                    // Save replaces filename metadata after extracting the selected image.
                    item.IsArchiveMetadataPending =
                        archiveAddMode == ArchiveAddMode.DeferToSave ||
                        !archiveMetadataParsed;
                }
            }

            if (itemImageFile == null)
                throw new Exception("Cant't read data from file");

            if (item.FileFormat == FileFormat.Uncompressed &&
                Path.GetExtension(itemImageFile).Equals(".gdi", StringComparison.OrdinalIgnoreCase) &&
                LegacyRedumpGdiDetector.IsLegacyRedumpGdi(itemImageFile))
                throw new UnsupportedDiscFormatException(LegacyRedumpGdiDetector.ShortMessage);

            // Compressed CUE/BIN is handled later, during extraction in Manager.cs.
            if (item.FileFormat == FileFormat.Uncompressed &&
                Path.GetExtension(itemImageFile).Equals(".cue", StringComparison.OrdinalIgnoreCase))
            {
                var cueParser = new CueSheetParser();
                cueParser.Parse(itemImageFile);

                var dataTrack = cueParser.GetPrimaryDataTrack();
                if (dataTrack != null)
                    ip = cueParser.TryParseIpBin();

                if (ip != null)
                {
                    // Dreamcast disc

                    item.FileFormat = FileFormat.RedumpCueBin;
                }
                else
                {
                    // Not a Dreamcast disc, will convert to CCD
                    item.FileFormat = FileFormat.CueBinNonGame;

                    var itemName = Path.GetFileNameWithoutExtension(itemImageFile);
                    var m = RegularExpressions.TosecnNameRegexp.Match(itemName);
                    if (m.Success)
                        itemName = itemName.Substring(0, m.Index);

                    // Check if it's a PSX disc
                    if (dataTrack != null)
                    {
                        var binPath = Path.Combine(cueParser.CueDirectory, dataTrack.BinFilename);
                        if (File.Exists(binPath) && IsPlayStationDisc(binPath))
                        {
                            // It's a PSX disc, try to get serial from SYSTEM.CNF.
                            var serial = TryExtractPlayStationSerial(binPath);

                            ip = new IpBin
                            {
                                ProductNumber = serial ?? string.Empty,
                                Region = "JUE",
                                CRC = string.Empty,
                                Version = string.Empty,
                                Vga = true,
                                Disc = "PS1",
                                SpecialDisc = SpecialDisc.BleemGame
                            };

                            if (!string.IsNullOrEmpty(serial))
                            {
                                var psEntry = PlayStationDB.FindBySerial(serial);
                                if (psEntry != null)
                                {
                                    ip.Name = psEntry.name;
                                    if (DateOnly.TryParse(psEntry.releaseDate, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateOnly releaseDate))
                                        ip.ReleaseDate = releaseDate.ToString("yyyyMMdd");
                                    else
                                        ip.ReleaseDate = "19990909";
                                }
                                else
                                {
                                    ip.Name = serial;
                                    ip.ReleaseDate = "19990909";
                                }
                            }
                            else
                            {
                                ip.Name = itemName;
                                ip.ReleaseDate = "19990909";
                            }

                            item.DiscType = "PSX";
                        }
                    }

                    // Not PSX, not Dreamcast.
                    if (ip == null)
                    {
                        ip = new IpBin
                        {
                            Name = itemName,
                            Disc = "1/1",
                            ProductNumber = string.Empty
                        };

                        item.DiscType = "Other";
                    }
                }

                item.ImageFiles.Add(Path.GetFileName(itemImageFile));

                foreach (var binFile in cueParser.GetAllBinFiles())
                {
                    if (!item.ImageFiles.Any(x => x.Equals(binFile, StringComparison.OrdinalIgnoreCase)))
                        item.ImageFiles.Add(binFile);
                }

                long totalSize = new FileInfo(itemImageFile).Length;
                totalSize += cueParser.GetTotalBinSize();
                item.Length = ByteSizeLib.ByteSize.FromBytes(totalSize);

                item.CanApplyGDIShrink = item.FileFormat == FileFormat.RedumpCueBin;
            }
            else if (item.FileFormat == FileFormat.Uncompressed &&
                     Path.GetExtension(itemImageFile).Equals(".chd", StringComparison.OrdinalIgnoreCase))
            {
                using var chd = new ChdReader(itemImageFile);
                if (!chd.IsGdRom)
                    throw new Exception("This CHD contains a CD-ROM image, which is not supported. Only GD-ROM CHD files are supported. Please use the original CDI or CUE/BIN files instead.");

                var ipData = chd.GetIpBin();
                // CHD returns raw 2352-byte sectors. Try parsing as-is first,
                // then try at offset 16 (MODE1_RAW: 12 sync + 4 header bytes)
                ip = GetIpData(ipData);
                if (ip == null && ipData.Length >= 16 + 256)
                {
                    var userData = new byte[ipData.Length - 16];
                    Array.Copy(ipData, 16, userData, 0, userData.Length);
                    ip = GetIpData(userData);
                }
                if (ip == null)
                    throw new Exception("Cannot read Dreamcast IP.BIN from CHD image");

                item.FileFormat = FileFormat.Chd;
                item.ImageFiles.Add(Path.GetFileName(itemImageFile));
                item.Length = ByteSizeLib.ByteSize.FromBytes(chd.Header.LogicalBytes);
                item.CanApplyGDIShrink = chd.IsGdRom;
            }
            else if (item.FileFormat == FileFormat.Uncompressed)
            {
                var filtersList = new FiltersList();
                IFilter inputFilter = null;
                try
                {
                    inputFilter = await Task.Run(() => filtersList.GetFilter(itemImageFile));

                    //todo check inputFilter null Cannot open specified file.

                    IOpticalMediaImage opticalImage;

                    switch (Path.GetExtension(itemImageFile).ToLower())
                    {
                        case ".gdi":
                            opticalImage = new Aaru.DiscImages.Gdi();
                            break;
                        case ".cdi":
                            opticalImage = new Aaru.DiscImages.DiscJuggler();
                            break;
                        case ".mds":
                            opticalImage = new Aaru.DiscImages.Alcohol120();
                            break;
                        case ".ccd":
                            opticalImage = new Aaru.DiscImages.CloneCd();
                            break;
                        default:
                            throw new NotSupportedException();
                    }

                    //if(!opticalImage.Identify(inputFilter))
                    //    throw new NotSupportedException();

                    //todo check imageFormat null Image format not identified.

                    try
                    {
                        bool useAaru;
                        try
                        {
                            useAaru = await Task.Run(() => opticalImage.Open(inputFilter));
                        }
                        catch (Exception)
                        {
                            useAaru = false;
                            opticalImage?.Close();
                        }


                        if (useAaru) //try to load file using Aaru
                        {
                            try
                            {
                                Partition partition;

                                if (Path.GetExtension(itemImageFile).Equals(".gdi", StringComparison.InvariantCultureIgnoreCase))//first track not audio and skip one
                                {
                                    partition = opticalImage.Partitions.Where(x => x.Type != "Audio").Skip(1).First();
                                    ip = await GetIpData(opticalImage, partition);
                                }
                                else
                                {
                                    // It's a ps1 disc?
                                    if (opticalImage.Info.MediaType == MediaType.CDROMXA && opticalImage.Partitions.Any())
                                    {
                                        partition = opticalImage.Partitions.First();

                                        ISO9660.DecodedVolumeDescriptor? pvd;
                                        if (ISO9660.GetDecodedPVD(opticalImage, partition, out pvd) == Aaru.CommonTypes.Structs.Errno.NoError && pvd.Value.ApplicationIdentifier == "PLAYSTATION" || pvd.Value.SystemIdentifier == "PLAYSTATION")
                                        {
                                            // It's a ps1 disc!

                                            var systemcnf = ImageHelper.extractFileFromPartition(opticalImage, partition, "SYSTEM.CNF");
                                            if (systemcnf == null) //could not open SYSTEM.CNF file
                                                throw new Exception();

                                            string serial;
                                            using (var ms = new MemoryStream(systemcnf))
                                            {
                                                using (var sr = new StreamReader(ms))
                                                {
                                                    serial = sr.ReadLine();
                                                }
                                            }

                                            serial = serial.Substring(serial.LastIndexOf('\\') + 1);
                                            var lastIndex = serial.LastIndexOf(';');
                                            if (lastIndex != -1)
                                                serial = serial.Substring(0, lastIndex);

                                            serial = serial.Replace('_', '-');
                                            serial = serial.Replace(".", string.Empty);


                                            //var serial = pvd.VolumeIdentifier.Replace('_', '-');
                                            ip = new IpBin
                                            {
                                                ProductNumber = serial,
                                                Region = "JUE",
                                                CRC = string.Empty,
                                                Version = string.Empty,
                                                Vga = true,
                                                Disc = "PS1",
                                                SpecialDisc = SpecialDisc.BleemGame
                                            };

                                            var psEntry = PlayStationDB.FindBySerial(serial);
                                            if (psEntry == null)
                                            {
                                                ip.Name = serial;
                                                ip.ReleaseDate = "19990909";
                                            }
                                            else
                                            {
                                                ip.Name = psEntry.name;
                                                if (DateOnly.TryParse(psEntry.releaseDate, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateOnly releaseDate))
                                                    ip.ReleaseDate = releaseDate.ToString("yyyyMMdd");
                                                else
                                                    ip.ReleaseDate = "19990909";
                                            }
                                        }
                                    }
                                    else//it's not a ps1 disc. try to read as dreamcast. start from from last partition
                                    {
                                        for (int i = opticalImage.Partitions.Count - 1; i >= 0; i--)
                                        {
                                            partition = opticalImage.Partitions[i];
                                            ip = await GetIpData(opticalImage, partition);
                                            if (ip != null)
                                                break;
                                        }
                                    }
                                }

                                //Aaru fails to read the ip.bin from some cdis in CdMode2Formless.
                                if (ip == null)
                                    throw new Exception();

                                //var imageFiles = new List<string> { Path.GetFileName(item.ImageFile) };
                                item.ImageFiles.Add(Path.GetFileName(itemImageFile));
                                foreach (var track in opticalImage.Tracks)
                                {
                                    if (!string.IsNullOrEmpty(track.TrackFile) && !item.ImageFiles.Any(x => x.Equals(track.TrackFile, StringComparison.InvariantCultureIgnoreCase)))
                                        item.ImageFiles.Add(track.TrackFile);
                                    if (!string.IsNullOrEmpty(track.TrackSubchannelFile) && !item.ImageFiles.Any(x => x.Equals(track.TrackSubchannelFile, StringComparison.InvariantCultureIgnoreCase)))
                                        item.ImageFiles.Add(track.TrackSubchannelFile);
                                }

                                item.CanApplyGDIShrink = Path.GetExtension(itemImageFile).Equals(".gdi", StringComparison.InvariantCultureIgnoreCase);

                                Manager.UpdateItemLength(item);
                            }
                            catch
                            {
                                useAaru = false;
                            }
                            finally
                            {
                                opticalImage?.Close();
                            }
                        }


                        if (!useAaru) //if cant open using Aaru, try to parse file manually
                        {
                            if (inputFilter != null && inputFilter.IsOpened())
                                inputFilter.Close();

                            var temp = await CreateGdItem2Async(itemImageFile);

                            if (temp == null || temp.Ip == null)
                                throw new Exception("Unable to open image format");

                            ip = temp.Ip;
                            item = temp;
                        }

                    }
                    finally
                    {
                        opticalImage?.Close();
                    }

                }
                //catch (Exception ex)
                //{

                //    throw;
                //}
                finally
                {
                    if (inputFilter != null && inputFilter.IsOpened())
                        inputFilter.Close();
                }
            }

            if (ip == null)
                throw new Exception("Cant't read data from file");


            item.Ip = ip;
            if (ip.SpecialDisc == SpecialDisc.BleemGame)
                item.DiscType = "PSX";
            item.Name = ip.Name;
            item.ProductNumber = ip.ProductNumber;

            if (archiveSidecarName != null)
                item.Name = archiveSidecarName;
            if (archiveSidecarSerial != null)
                item.ProductNumber = archiveSidecarSerial;

            var itemNamePath = Path.Combine(item.FullFolderPath, Constants.NameTextFile);
            if (await Helper.FileExistsAsync(itemNamePath))
                item.Name = await Helper.ReadAllTextAsync(itemNamePath);

            var itemSerialPath = Path.Combine(item.FullFolderPath, Constants.SerialTextFile);
            if (await Helper.FileExistsAsync(itemSerialPath))
                item.ProductNumber = await Helper.ReadAllTextAsync(itemSerialPath);

            item.Name = item.Name.Trim();

            if (item.FullFolderPath.StartsWith(Manager.sdPath, StringComparison.InvariantCultureIgnoreCase) && int.TryParse(Path.GetFileName(Path.GetDirectoryName(itemImageFile)), out int number))
            {
                item.SdNumber = number;

                // Only card folders carry a trustworthy marker. A stray shrunk.txt
                // next to images on the PC must not flag the item.
                if (await Helper.FileExistsAsync(Path.Combine(item.FullFolderPath, Constants.ShrunkTextFile)))
                    item.WasShrunk = true;
            }

            //item.ImageFile = Path.GetFileName(item.ImageFile);

            return item;
        }

        private static Task<IpBin> GetIpData(IOpticalMediaImage opticalImage, Partition partition)
        {
            return Task.Run(() => GetIpData(opticalImage.ReadSector(partition.Start)));
        }

        internal static IpBin GetIpData(byte[] ipData)
        {

            var dreamcastip = Aaru.Decoders.Sega.Dreamcast.DecodeIPBin(ipData);
            if (dreamcastip == null)
                return null;

            var ipbin = dreamcastip.Value;

            var special = SpecialDisc.None;
            var releaseDate = GetString(ipbin.release_date);
            var version = GetString(ipbin.product_version);

            string disc;
            if (ipbin.disc_no == 32 || ipbin.disc_total_nos == 32)
            {
                disc = "1/1";
                if (GetString(ipbin.dreamcast_media) == "FCD" && releaseDate == "20000627" && version == "V1.000" && GetString(ipbin.boot_filename) == "PELICAN.BIN")
                    special = SpecialDisc.CodeBreaker;
            }
            else
            {
                disc = $"{(char)ipbin.disc_no}/{(char)ipbin.disc_total_nos}";
            }

            //int iPeripherals = int.Parse(Encoding.ASCII.GetString(ipbin.peripherals), System.Globalization.NumberStyles.HexNumber);

            var ip = new IpBin
            {
                CRC = GetString(ipbin.dreamcast_crc),
                Disc = disc,
                // area symbols are positional ("J E" means Japan+Europe), drop the space placeholders
                Region = GetString(ipbin.region_codes).Replace(" ", string.Empty),
                Vga = ipbin.peripherals[5] == 49,
                ProductNumber = GetString(ipbin.product_no),
                Version = version,
                ReleaseDate = releaseDate,
                Name = GetString(ipbin.product_name),
                SpecialDisc = special
            };

            return ip;
        }

        private static string GetString(byte[] bytearray)
        {
            var str = Encoding.ASCII.GetString(bytearray).Trim();

            // Handle null terminated string.
            int index = str.IndexOf('\0');
            if (index > -1)
                str = str.Substring(0, index).Trim();
            return str;
        }


        // Reads a sidecar text file stored inside the archive, beside the selected
        // image or at the archive root. The image's own directory wins over the root.
        private static async Task<string> ReadArchiveSidecarTextAsync(
            string compressedFile,
            IReadOnlyList<ArchiveEntryInfo> archiveEntries,
            ArchiveEntryInfo selectedEntry,
            string sidecarFileName)
        {
            // Sidecar text files are tiny. Anything bigger is not a sidecar.
            const long maxSidecarBytes = 4096;

            string selectedDirectory;
            try
            {
                selectedDirectory = ArchiveEntryPath.GetDirectoryKey(selectedEntry.FullName);
            }
            catch (ArgumentException)
            {
                return null;
            }

            ArchiveEntryInfo match = null;
            foreach (var entry in archiveEntries)
            {
                if (entry.Size > maxSidecarBytes)
                    continue;

                string directory;
                string leaf;
                try
                {
                    directory = ArchiveEntryPath.GetDirectoryKey(entry.FullName);
                    leaf = ArchiveEntryPath.GetLeafName(entry.FullName);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (!leaf.Equals(sidecarFileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                bool inSelectedDirectory = selectedDirectory.Length > 0 &&
                    string.Equals(directory, selectedDirectory, StringComparison.OrdinalIgnoreCase);
                if (inSelectedDirectory)
                {
                    match = entry;
                    break;
                }

                if (directory.Length == 0 && match == null)
                    match = entry;
            }

            if (match == null)
                return null;

            var bytes = await Task.Run(() =>
                Helper.DependencyManager.ReadArchiveEntryBytes(compressedFile, match, maxSidecarBytes));
            if (bytes == null)
                return null;

            // File.ReadAllText strips the UTF-8 byte order mark, so match it.
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>
        /// Reads from the image every time. Does not touch the cached Ip on the item.
        /// </summary>
        public static async Task<IpBin> GetIpBinFromImage(string itemImageFile)
        {
            if (Path.GetExtension(itemImageFile).Equals(".cue", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var cueParser = new CueSheetParser();
                    cueParser.Parse(itemImageFile);
                    return cueParser.TryParseIpBin();
                }
                catch
                {
                    return null;
                }
            }

            // Special handling for CHD format
            if (Path.GetExtension(itemImageFile).Equals(".chd", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var chd = new ChdReader(itemImageFile);
                    if (!chd.IsGdRom)
                        return null;

                    var ipData = chd.GetIpBin();
                    // CHD returns raw 2352-byte sectors. Try as-is, then at offset 16.
                    var ip = GetIpData(ipData);
                    if (ip == null && ipData.Length >= 16 + 256)
                    {
                        var userData = new byte[ipData.Length - 16];
                        Array.Copy(ipData, 16, userData, 0, userData.Length);
                        ip = GetIpData(userData);
                    }
                    return ip;
                }
                catch
                {
                    return null;
                }
            }

            var filtersList = new FiltersList();
            IFilter inputFilter = null;
            try
            {
                inputFilter = filtersList.GetFilter(itemImageFile);
                if (inputFilter == null)
                    return null;

                IOpticalMediaImage opticalImage;

                switch (Path.GetExtension(itemImageFile).ToLower())
                {
                    case ".gdi":
                        opticalImage = new Aaru.DiscImages.Gdi();
                        break;
                    case ".cdi":
                        opticalImage = new Aaru.DiscImages.DiscJuggler();
                        break;
                    case ".mds":
                        opticalImage = new Aaru.DiscImages.Alcohol120();
                        break;
                    case ".ccd":
                        opticalImage = new Aaru.DiscImages.CloneCd();
                        break;
                    default:
                        return null;
                }

                try
                {
                    if (!await Task.Run(() => opticalImage.Open(inputFilter)))
                        return null;

                    Partition partition;
                    if (Path.GetExtension(itemImageFile).Equals(".gdi", StringComparison.InvariantCultureIgnoreCase))
                    {
                        // For GDI: skip audio tracks, then skip first data track
                        partition = opticalImage.Partitions.Where(x => x.Type != "Audio").Skip(1).First();
                        return await GetIpData(opticalImage, partition);
                    }
                    else
                    {
                        // For CDI/MDS/CCD: iterate backwards through partitions to find IP.BIN
                        for (int i = opticalImage.Partitions.Count - 1; i >= 0; i--)
                        {
                            partition = opticalImage.Partitions[i];
                            var ip = await GetIpData(opticalImage, partition);
                            if (ip != null)
                                return ip;
                        }
                    }
                    return null;
                }
                finally
                {
                    opticalImage?.Close();
                }
            }
            finally
            {
                if (inputFilter != null && inputFilter.IsOpened())
                    inputFilter.Close();
            }
        }

        // Returns null if file not exists on image. Throw on any error.
        public static async Task<byte[]> GetGdText(string itemImageFile)
        {
            var filtersList = new FiltersList();
            IFilter inputFilter = null;
            try
            {
                inputFilter = filtersList.GetFilter(itemImageFile);

                //todo check inputFilter null Cannot open specified file.

                IOpticalMediaImage opticalImage;

                switch (Path.GetExtension(itemImageFile).ToLower())
                {
                    case ".gdi":
                        opticalImage = new Aaru.DiscImages.Gdi();
                        break;
                    case ".cdi":
                        opticalImage = new Aaru.DiscImages.DiscJuggler();
                        break;
                    case ".mds":
                        opticalImage = new Aaru.DiscImages.Alcohol120();
                        break;
                    case ".ccd":
                        opticalImage = new Aaru.DiscImages.CloneCd();
                        break;
                    default:
                        throw new NotSupportedException();
                }

                //if(!opticalImage.Identify(inputFilter))
                //    throw new NotSupportedException();

                //todo check imageFormat null Image format not identified.

                try
                {
                    if (!await Task.Run(() => opticalImage.Open(inputFilter)))
                        throw new Exception("Unable to find or read file");

                    Partition partition;
                    string filename = "0GDTEX.PVR";
                    if (Path.GetExtension(itemImageFile).Equals(".gdi", StringComparison.InvariantCultureIgnoreCase))//first track not audio and skip one
                    {
                        partition = opticalImage.Partitions.Where(x => x.Type != "Audio").Skip(1).First();
                        return await Task.Run(() => extractFileFromPartition(opticalImage, partition, filename));
                    }
                    else//try to find from last
                    {
                        for (int i = opticalImage.Partitions.Count - 1; i >= 0; i--)
                        {
                            partition = opticalImage.Partitions[i];
                            if ((await GetIpData(opticalImage, partition)) != null)
                                return await Task.Run(() => extractFileFromPartition(opticalImage, partition, filename));
                        }
                    }
                    return null;
                }
                finally
                {
                    opticalImage?.Close();
                }
            }
            finally
            {
                if (inputFilter != null && inputFilter.IsOpened())
                    inputFilter.Close();
            }
        }

        //true when GetGdText can read this item as-is. compressed and unconverted images only become
        // Readable after Save Changes turns them into one of the supported formats on the sd card.
        public static bool CanExtractGdText(GdItem item)
        {
            if (item == null || item.FileFormat != FileFormat.Uncompressed || item.DiscType != "Game" || string.IsNullOrEmpty(item.ImageFile))
                return false;

            switch (Path.GetExtension(item.ImageFile).ToLower())
            {
                case ".gdi":
                case ".cdi":
                case ".mds":
                case ".ccd":
                    return true;
                default:
                    return false;
            }
        }

        public static string GetGdTextUnavailableMessage(GdItem item)
        {
            return CanExtractGdText(item)
                ? null
                : "Cannot be read from this disc image until SD card changes are saved.";
        }

        private static byte[] extractFileFromPartition(IOpticalMediaImage opticalImage, Partition partition, string fileName)
        {
            var iso = new ISO9660();
            try
            {
                //string information;
                //iso.GetInformation(opticalImage, partition, out information, Encoding.ASCII);

                var dict = new Dictionary<string, string>();
                iso.Mount(opticalImage, partition, Encoding.ASCII, dict, "normal");
                //System.Collections.Generic.List<string> strlist = null;
                //iso.ReadDir("/", out strlist);

                if (iso.Stat(fileName, out var stat) == Aaru.CommonTypes.Structs.Errno.NoError && stat.Length > 0)
                {
                    // File exists.
                    var buff = new byte[stat.Length];
                    iso.Read(fileName, 0, stat.Length, ref buff);
                    return buff;
                }
            }
            finally
            {
                iso.Unmount();
            }
            return null;
        }


        #region fallback methods if cant parse using Aaru
        internal static async Task<GdItem> CreateGdItem2Async(string filePath)
        {
            string folderPath = Path.GetDirectoryName(filePath);

            var item = new GdItem
            {
                Guid = Guid.NewGuid().ToString(),
                FullFolderPath = folderPath,
                FileFormat = FileFormat.Uncompressed
            };

            IpBin ip = null;

            var ext = Path.GetExtension(filePath).ToLower();
            string itemImageFile = null;
            string dataFile = null;

            item.ImageFiles.Add(Path.GetFileName(filePath));

            if (ext == ".gdi")
            {
                itemImageFile = filePath;

                var gdi = await GetGdiFileListAsync(filePath);

                foreach (var datafile in gdi.Where(x => !x.EndsWith(".raw", StringComparison.InvariantCultureIgnoreCase)).Skip(1))
                {
                    ip = await Task.Run(() => GetIpData(Path.Combine(item.FullFolderPath, datafile)));
                    if (ip != null)
                        break;
                }

                var gdifiles = gdi.Distinct().ToArray();
                item.ImageFiles.AddRange(gdifiles);
            }
            else
            {
                var imageNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
                if (ext == ".ccd")
                {

                    var img = Path.ChangeExtension(filePath, ".img");
                    if (!File.Exists(img))
                        throw new Exception("Missing file: " + img);
                    item.ImageFiles.Add(Path.GetFileName(img));

                    var sub = Path.ChangeExtension(filePath, ".sub");
                    if (File.Exists(sub))
                        item.ImageFiles.Add(Path.GetFileName(sub));

                    dataFile = img;
                }
                else if (ext == ".mds")
                {
                    var mdf = Path.ChangeExtension(filePath, ".mdf");
                    if (!File.Exists(mdf))
                        throw new Exception("Missing file: " + mdf);
                    item.ImageFiles.Add(Path.GetFileName(mdf));

                    dataFile = mdf;
                }
                else //cdi
                {
                    dataFile = filePath;
                }

                ip = await Task.Run(() => GetIpData(dataFile));
            }


            if (ip == null)
            {
                if (ext == ".gdi")
                    throw new Exception("Cant't read data from file");

                // No KATANA header, not a DC game. Use filename as display name.
                ip = new IpBin
                {
                    Name = Path.GetFileNameWithoutExtension(filePath),
                    Disc = "1/1",
                    ProductNumber = string.Empty
                };

                // Distinguish PSX from other non-DC discs (audio CDs, data discs, etc.)
                // by searching for the ISO9660 PVD System Identifier "PLAYSTATION" at CD001+7.
                if (dataFile != null && IsPlayStationDisc(dataFile))
                    item.DiscType = "PSX";
                else
                    item.DiscType = "Other";
            }


            item.Ip = ip;
            if (ip.SpecialDisc == SpecialDisc.BleemGame)
                item.DiscType = "PSX";
            item.Name = ip.Name;
            item.ProductNumber = ip.ProductNumber;

            var itemNamePath = Path.Combine(item.FullFolderPath, Constants.NameTextFile);
            if (await Helper.FileExistsAsync(itemNamePath))
                item.Name = await Helper.ReadAllTextAsync(itemNamePath);

            var itemSerialPath = Path.Combine(item.FullFolderPath, Constants.SerialTextFile);
            if (await Helper.FileExistsAsync(itemSerialPath))
                item.ProductNumber = await Helper.ReadAllTextAsync(itemSerialPath);

            item.Name = item.Name.Trim();

            if (item.FullFolderPath.StartsWith(Manager.sdPath, StringComparison.InvariantCultureIgnoreCase) && int.TryParse(new DirectoryInfo(item.FullFolderPath).Name, out int number))
            {
                item.SdNumber = number;

                // Only card folders carry a trustworthy marker. A stray shrunk.txt
                // next to images on the PC must not flag the item.
                if (await Helper.FileExistsAsync(Path.Combine(item.FullFolderPath, Constants.ShrunkTextFile)))
                    item.WasShrunk = true;
            }

            Manager.UpdateItemLength(item);

            return item;
        }

        // Scans up to 50 MB for the CD001 + PLAYSTATION PVD pattern, since the PVD is
        // not at a fixed sector in every dump.
        private static bool IsPlayStationDisc(string dataFilePath)
        {
            const long searchLimit = 50L * 1024 * 1024;
            const int bufferSize = 65536;
            const int overlap = 17; // pattern spans 18 bytes max (i..i+17), so overlap = 17

            try
            {
                using var fs = new FileStream(dataFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var buf = new byte[bufferSize];
                long pos = 0;

                while (pos < searchLimit)
                {
                    fs.Position = pos;
                    int read = fs.Read(buf, 0, bufferSize);
                    if (read < 18) break; // not enough bytes for the full pattern

                    for (int i = 0; i <= read - 18; i++)
                    {
                        // "CD001" at i, then version+unused at i+5,i+6, then System Identifier at i+7
                        if (buf[i] == 'C' && buf[i + 1] == 'D' && buf[i + 2] == '0' &&
                            buf[i + 3] == '0' && buf[i + 4] == '1' &&
                            buf[i + 7] == 'P' && buf[i + 8] == 'L' && buf[i + 9] == 'A' &&
                            buf[i + 10] == 'Y' && buf[i + 11] == 'S' && buf[i + 12] == 'T' &&
                            buf[i + 13] == 'A' && buf[i + 14] == 'T' && buf[i + 15] == 'I' &&
                            buf[i + 16] == 'O' && buf[i + 17] == 'N')
                            return true;
                    }

                    if (read < bufferSize) break; // reached end of file
                    pos += bufferSize - overlap;   // slide window with overlap
                }
            }
            catch { }

            return false;
        }

        // Minimal ISO9660 walk of PVD, root directory and SYSTEM.CNF, because Aaru cannot
        // open a bare .bin without its cue.
        private static string TryExtractPlayStationSerial(string dataFilePath)
        {
            try
            {
                using var fs = new FileStream(dataFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);

                var syncCheck = new byte[12];
                fs.Read(syncCheck, 0, 12);
                bool isRawSector = syncCheck[0] == 0x00 && syncCheck[1] == 0xFF && syncCheck[2] == 0xFF &&
                                   syncCheck[10] == 0xFF && syncCheck[11] == 0x00;

                // User data offset: MODE2/2352=24, MODE1/2352=16, cooked 2048=0
                int sectorSize, dataOffset;
                if (isRawSector)
                {
                    sectorSize = 2352;
                    // Mode byte at offset 15.
                    fs.Position = 15;
                    int mode = fs.ReadByte();
                    dataOffset = (mode == 2) ? 24 : 16;
                }
                else
                {
                    sectorSize = 2048;
                    dataOffset = 0;
                }

                // PVD is at sector 16, root directory record at PVD offset 156
                var pvdData = new byte[2048];
                fs.Position = (long)16 * sectorSize + dataOffset;
                if (fs.Read(pvdData, 0, 2048) < 2048)
                    return null;

                // Verify PVD
                if (pvdData[0] != 1 || pvdData[1] != 'C' || pvdData[2] != 'D' ||
                    pvdData[3] != '0' || pvdData[4] != '0' || pvdData[5] != '1')
                    return null;

                int rootSector = BitConverter.ToInt32(pvdData, 156 + 2);  // extent location (LE)
                int rootLength = BitConverter.ToInt32(pvdData, 156 + 10); // data length (LE)

                // Read the root directory
                int rootSectors = (rootLength + 2047) / 2048;
                var rootData = new byte[rootSectors * 2048];
                for (int s = 0; s < rootSectors; s++)
                {
                    fs.Position = (long)(rootSector + s) * sectorSize + dataOffset;
                    fs.Read(rootData, s * 2048, 2048);
                }

                // Find SYSTEM.CNF in the root directory
                int pos = 0;
                while (pos < rootLength)
                {
                    int recordLen = rootData[pos];
                    if (recordLen == 0)
                    {
                        // Skip to next sector boundary
                        pos = ((pos / 2048) + 1) * 2048;
                        if (pos >= rootLength) break;
                        continue;
                    }

                    int nameLen = rootData[pos + 32];
                    if (nameLen >= 10 && pos + 33 + nameLen <= rootData.Length)
                    {
                        var name = Encoding.ASCII.GetString(rootData, pos + 33, nameLen);
                        // Filenames may have ";1" version suffix.
                        if (name.Equals("SYSTEM.CNF;1", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("SYSTEM.CNF", StringComparison.OrdinalIgnoreCase))
                        {
                            // Read the file content.
                            int fileSector = BitConverter.ToInt32(rootData, pos + 2);
                            int fileLength = BitConverter.ToInt32(rootData, pos + 10);
                            if (fileLength > 4096) fileLength = 4096; // sanity cap

                            var fileData = new byte[fileLength];
                            fs.Position = (long)fileSector * sectorSize + dataOffset;
                            fs.Read(fileData, 0, fileLength);

                            // first line: "BOOT = cdrom:\SLPS_010.91;1"
                            string firstLine;
                            using (var ms = new MemoryStream(fileData))
                            using (var sr = new StreamReader(ms))
                            {
                                firstLine = sr.ReadLine();
                            }

                            if (string.IsNullOrEmpty(firstLine))
                                return null;

                            // Parse serial from boot path.
                            var serial = firstLine.Substring(firstLine.LastIndexOf('\\') + 1);
                            var lastIndex = serial.LastIndexOf(';');
                            if (lastIndex != -1)
                                serial = serial.Substring(0, lastIndex);

                            serial = serial.Replace('_', '-');
                            serial = serial.Replace(".", string.Empty);

                            if (serial.Length >= 7)
                                return serial;
                        }
                    }

                    pos += recordLen;
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Full paths of files referenced by index files in the same dropped batch
        /// (cue bins, gdi tracks, ccd img/sub, mds mdf). Such files belong to their
        /// index item and must not be treated as standalone drops.
        /// </summary>
        public static async Task<HashSet<string>> GetCompanionFilePathsAsync(IEnumerable<string> paths)
        {
            var companions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in paths)
            {
                try
                {
                    if ((await Helper.GetAttributesAsync(path)).HasFlag(FileAttributes.Directory))
                        continue;

                    var dir = Path.GetDirectoryName(path);
                    switch (Path.GetExtension(path).ToLowerInvariant())
                    {
                        case ".cue":
                            var cueParser = new CueSheetParser();
                            cueParser.Parse(path);
                            foreach (var bin in cueParser.GetAllBinFiles())
                                companions.Add(Path.GetFullPath(Path.Combine(dir, bin)));
                            break;
                        case ".gdi":
                            foreach (var track in await GetGdiFileListAsync(path))
                                companions.Add(Path.GetFullPath(Path.Combine(dir, track)));
                            break;
                        case ".ccd":
                            companions.Add(Path.ChangeExtension(path, ".img"));
                            companions.Add(Path.ChangeExtension(path, ".sub"));
                            break;
                        case ".mds":
                            companions.Add(Path.ChangeExtension(path, ".mdf"));
                            break;
                    }
                }
                catch
                {
                    // A malformed index contributes nothing here and will fail on
                    // its own when it is parsed as an item.
                }
            }

            return companions;
        }

        private static async Task<string[]> GetGdiFileListAsync(string gdiFilePath)
        {
            var tracks = new List<string>();

            var files = await File.ReadAllLinesAsync(gdiFilePath);
            foreach (var item in files.Skip(1))
            {
                var m = RegularExpressions.GdiRegexp.Match(item);
                if (m.Success)
                    tracks.Add(m.Groups[1].Value);
            }
            return tracks.ToArray();
        }

        private static IpBin GetIpData(string filepath)
        {
            using (FileStream fs = new FileStream(filepath, FileMode.Open, FileAccess.Read))
            {
                long headerOffset = GetHeaderOffset(fs);

                if (headerOffset == -1)
                    return null; // no KATANA header

                fs.Seek(headerOffset, SeekOrigin.Begin);

                byte[] buffer = new byte[512];
                fs.Read(buffer, 0, buffer.Length);
                return GetIpData(buffer);
            }
        }

        private static long GetHeaderOffset(Stream stream)
        {
            /// based on https://keestalkstech.com/2010/11/seek-position-of-a-string-in-a-file-or-filestream/

            char[] search = katanachar;
            long result = -1, position = 0, stored = -1,
            begin = stream.Position;
            int c;

            // Read byte by byte.
            while ((c = stream.ReadByte()) != -1)
            {
                // Check if data in array matches.
                if ((char)c == search[position])
                {
                    //if charater matches first character of 
                    // Seek string, store it for later.
                    if (stored == -1 && position > 0 && (char)c == search[0])
                    {
                        stored = stream.Position;
                    }

                    // Check if we're done.
                    if (position + 1 == search.Length)
                    {
                        // Correct position for array lenth.
                        result = stream.Position - search.Length;
                        //set position in stream
                        stream.Position = result;
                        break;
                    }

                    // Advance position in the array.
                    position++;
                }
                // No match, check if we have a stored position.
                else if (stored > -1)
                {
                    // Go to stored position + 1.
                    stream.Position = stored + 1;
                    position = 1;
                    stored = -1; //reset stored position!
                }
                // No match, no stored position, reset array
                //position and continue reading.
                else
                {
                    position = 0;
                }
            }

            // Reset stream position if no match has been found.
            if (result == -1)
            {
                stream.Position = begin;
            }

            return result;
        }
        #endregion
    }
}
