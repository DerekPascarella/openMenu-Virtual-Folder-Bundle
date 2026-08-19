using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using ByteSizeLib;

namespace GDMENUCardManager.Core
{

    public sealed class GdItem : INotifyPropertyChanged
    {
        public static int namemaxlen = 256;
        public static int serialmaxlen = 12;
        public static int foldermaxlen = 512;

        public string Guid { get; set; }

        private ByteSize _Length;
        public ByteSize Length
        {
            get { return _Length; }
            set { _Length = value; RaisePropertyChanged(); }
        }

        //public long CdiTarget { get; set; }

        private string _Name;
        public string Name
        {
            get { return _Name; }
            set
            {
                _Name = value;
                if (_Name != null)
                {
                    if (_Name.Length > namemaxlen)
                        _Name = _Name.Substring(0, namemaxlen);
                    _Name = Helper.StripNonPrintableAscii(
                        Helper.RemoveDiacritics(_Name).Replace("_", " ").Trim());
                }

                RaisePropertyChanged();
            }
        }

        private bool _hasUserEditedCompressedTitle;
        private ArchiveMetadataField _manualArchiveMetadataFields;

        /// <summary>
        /// True when a user action changed the title while this item was compressed.
        /// </summary>
        public bool HasUserEditedCompressedTitle => _hasUserEditedCompressedTitle;

        private bool _isMatch;

        /// <summary>
        /// Whether the current search text matches Name or ProductNumber.
        /// Transient row highlight state, never saved.
        /// </summary>
        public bool IsMatch
        {
            get { return _isMatch; }
            set { if (_isMatch != value) { _isMatch = value; RaisePropertyChanged(); } }
        }

        /// <summary>
        /// Stores a title from a user action and records its compressed origin.
        /// </summary>
        public bool CommitUserTitle(string previousTitle, string requestedTitle)
        {
            Name = requestedTitle;
            if (Name == previousTitle)
                return false;

            if (FileFormat == FileFormat.SevenZip)
                _hasUserEditedCompressedTitle = true;

            return true;
        }

        internal void RestoreTitleState(string title, bool hasUserEditedCompressedTitle)
        {
            Name = title;
            _hasUserEditedCompressedTitle = hasUserEditedCompressedTitle;
        }

        private string _ProductNumber;
        public string ProductNumber
        {
            get { return _ProductNumber; }
            set
            {
                var cleaned = Helper.StripNonPrintableAscii(CleanSerial(value));

                // Re-assigning the same value would clear the translation tracking, so bail out.
                // Before the first translation there is nothing to protect, and Ip may since
                // have arrived with the date and name the tables match on.
                if (cleaned == _ProductNumber && WasSerialTranslated)
                    return;

                _ProductNumber = cleaned;
                OriginalSerial = null;
                WasSerialTranslated = false;

                if (_ProductNumber != null)
                {
                    if (_ProductNumber.Length > serialmaxlen)
                        _ProductNumber = _ProductNumber.Substring(0, serialmaxlen);

                    string beforeTranslation = _ProductNumber;

                    // Table 1 only. Table 2 is applied inside the DAT managers instead.
                    // Falls back to Name when Ip has not been read yet.
                    string dateContext = Ip?.ReleaseDate ?? "";
                    string nameContext = Ip?.Name ?? Name ?? "";
                    _ProductNumber = SerialTranslator.TranslateSerial(_ProductNumber, dateContext, nameContext);

                    // Track if translation occurred
                    if (_ProductNumber != beforeTranslation)
                    {
                        OriginalSerial = beforeTranslation;
                        WasSerialTranslated = true;
                    }
                }

                RaisePropertyChanged();
                RaisePropertyChanged(nameof(ArchiveSerialDisplay));
                RaisePropertyChanged(nameof(HasArtwork));
                RaisePropertyChanged(nameof(ArtworkButtonToolTip));
                RaisePropertyChanged(nameof(CanManageArtwork));
            }
        }

        /// <summary>
        /// The original serial before Table 1 translation was applied.
        /// Null if no translation occurred.
        /// </summary>
        public string OriginalSerial { get; private set; }

        public bool WasSerialTranslated { get; private set; }

        public GdItem CreateArchivePreparationCopy()
        {
            var copy = new GdItem
            {
                Guid = Guid,
                _Length = _Length,
                _Name = _Name,
                _hasUserEditedCompressedTitle = _hasUserEditedCompressedTitle,
                _manualArchiveMetadataFields = _manualArchiveMetadataFields,
                _ProductNumber = _ProductNumber,
                OriginalSerial = OriginalSerial,
                WasSerialTranslated = WasSerialTranslated,
                _Folder = _Folder,
                _AlternativeFolders = new List<string>(_AlternativeFolders),
                _FullFolderPath = _FullFolderPath,
                _Ip = CopyIp(_Ip),
                _ImageRegion = _ImageRegion,
                _SdNumber = _SdNumber,
                _Work = _Work,
                CanApplyGDIShrink = CanApplyGDIShrink,
                WasShrunk = WasShrunk,
                _FileFormat = _FileFormat,
                _DiscType = _DiscType,
                ArchiveImageEntries = ArchiveImageEntries?.ToArray() ?? Array.Empty<ArchiveEntryInfo>(),
                SelectedArchiveEntry = SelectedArchiveEntry,
                IsArchiveMetadataPending = IsArchiveMetadataPending
            };
            copy.ImageFiles.AddRange(ImageFiles);
            return copy;
        }

        public ArchiveMetadataFieldState CaptureArchiveMetadataFieldState(
            ArchiveMetadataField field)
        {
            return new ArchiveMetadataFieldState(
                GetArchiveMetadataFieldValue(field),
                (_manualArchiveMetadataFields & field) == field,
                field == ArchiveMetadataField.Serial ? OriginalSerial : null,
                field == ArchiveMetadataField.Serial && WasSerialTranslated);
        }

        public bool CommitUserArchiveMetadata(
            ArchiveMetadataField field,
            string requestedValue)
        {
            if (FileFormat != FileFormat.SevenZip || IsArchiveMetadataPending)
                return false;
            if (!IsSingleArchiveMetadataField(field))
                throw new ArgumentOutOfRangeException(nameof(field));
            if (field == ArchiveMetadataField.Type &&
                !IsValidDiscType(requestedValue))
                return false;

            ArchiveMetadataFieldState oldState =
                CaptureArchiveMetadataFieldState(field);
            SetArchiveMetadataFieldValue(field, requestedValue);
            ArchiveMetadataFieldState newState =
                CaptureArchiveMetadataFieldState(field);
            if (oldState == newState)
                return false;

            _manualArchiveMetadataFields |= field;
            return true;
        }

        public void RestoreArchiveMetadataFieldState(
            ArchiveMetadataField field,
            ArchiveMetadataFieldState state)
        {
            if (!IsSingleArchiveMetadataField(field))
                throw new ArgumentOutOfRangeException(nameof(field));
            if (field == ArchiveMetadataField.Serial)
                RestoreSerialState(state);
            else
                SetArchiveMetadataFieldValue(field, state.Value);
            if (state.IsManual)
                _manualArchiveMetadataFields |= field;
            else
                _manualArchiveMetadataFields &= ~field;
        }

        private void RestoreSerialState(ArchiveMetadataFieldState state)
        {
            _ProductNumber = state.Value;
            OriginalSerial = state.OriginalSerial;
            WasSerialTranslated = state.WasSerialTranslated;
            RaisePropertyChanged(nameof(ProductNumber));
            RaisePropertyChanged(nameof(ArchiveSerialDisplay));
            RaisePropertyChanged(nameof(HasArtwork));
            RaisePropertyChanged(nameof(ArtworkButtonToolTip));
            RaisePropertyChanged(nameof(CanManageArtwork));
        }

        internal ArchiveMetadataField ManualArchiveMetadataFields =>
            _manualArchiveMetadataFields;

        private string GetArchiveMetadataFieldValue(ArchiveMetadataField field)
        {
            return field switch
            {
                ArchiveMetadataField.Serial => ProductNumber,
                ArchiveMetadataField.Type => DiscType,
                ArchiveMetadataField.Disc => Disc,
                ArchiveMetadataField.Region => Region,
                _ => throw new ArgumentOutOfRangeException(nameof(field))
            };
        }

        private void SetArchiveMetadataFieldValue(
            ArchiveMetadataField field,
            string value)
        {
            switch (field)
            {
                case ArchiveMetadataField.Serial:
                    ProductNumber = value;
                    break;
                case ArchiveMetadataField.Type:
                    if (!IsValidDiscType(value))
                        throw new ArgumentException("The disc type is invalid.", nameof(value));
                    DiscType = value;
                    break;
                case ArchiveMetadataField.Disc:
                    Disc = value;
                    break;
                case ArchiveMetadataField.Region:
                    Region = NormalizeRegion(value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(field));
            }
        }

        private static bool IsSingleArchiveMetadataField(ArchiveMetadataField field)
        {
            return field == ArchiveMetadataField.Serial ||
                field == ArchiveMetadataField.Type ||
                field == ArchiveMetadataField.Disc ||
                field == ArchiveMetadataField.Region;
        }

        private static bool IsValidDiscType(string value)
        {
            return string.Equals(value, "Game", StringComparison.Ordinal) ||
                string.Equals(value, "PSX", StringComparison.Ordinal) ||
                string.Equals(value, "Other", StringComparison.Ordinal);
        }

        public void PublishPreparedArchiveState(
            GdItem prepared,
            string cardPath,
            int folderNumber,
            bool backfillBlankSerial)
        {
            if (prepared == null)
                throw new ArgumentNullException(nameof(prepared));

            _Ip = CopyIp(prepared._Ip);
            _ImageRegion = prepared._ImageRegion;
            ImageFiles.Clear();
            ImageFiles.AddRange(prepared.ImageFiles);
            _DiscType = prepared._DiscType;
            _FileFormat = prepared._FileFormat;
            _Length = prepared._Length;
            CanApplyGDIShrink = prepared.CanApplyGDIShrink;
            WasShrunk = prepared.WasShrunk;
            bool publishPendingTitle = IsArchiveMetadataPending &&
                !_hasUserEditedCompressedTitle;
            if (publishPendingTitle)
                _Name = prepared._Name;

            bool shouldBackfillBlankSerial =
                backfillBlankSerial && string.IsNullOrWhiteSpace(_ProductNumber);
            if (shouldBackfillBlankSerial)
            {
                _ProductNumber = prepared._ProductNumber;
                OriginalSerial = prepared.OriginalSerial;
                WasSerialTranslated = prepared.WasSerialTranslated;
            }

            // Publication is the commit point for this artifact.
            // Precommit failures leave the live row pending.
            IsArchiveMetadataPending = false;

            _FullFolderPath = cardPath;
            _SdNumber = folderNumber;
            _Work = WorkMode.None;

            RaisePropertyChanged(nameof(Ip));
            if (publishPendingTitle)
                RaisePropertyChanged(nameof(Name));
            RaisePropertyChanged(nameof(Disc));
            RaisePropertyChanged(nameof(ArchiveDiscDisplay));
            RaisePropertyChanged(nameof(Region));
            RaisePropertyChanged(nameof(ArchiveRegionDisplay));
            RaisePropertyChanged(nameof(DiscType));
            RaisePropertyChanged(nameof(ArchiveTypeDisplay));
            RaisePropertyChanged(nameof(FullFolderPath));
            RaisePropertyChanged(nameof(Work));
            RaisePropertyChanged(nameof(SdNumber));
            RaisePropertyChanged(nameof(Location));
            RaisePropertyChanged(nameof(IsNotOnSdCard));
            RaisePropertyChanged(nameof(FileFormat));
            if (shouldBackfillBlankSerial)
            {
                RaisePropertyChanged(nameof(ProductNumber));
                RaisePropertyChanged(nameof(HasArtwork));
                RaisePropertyChanged(nameof(ArtworkButtonToolTip));
                RaisePropertyChanged(nameof(CanManageArtwork));
            }
            RaisePropertyChanged(nameof(ArchiveSerialDisplay));
            RaisePropertyChanged(nameof(Length));
        }

        private static IpBin CopyIp(IpBin ip)
        {
            if (ip == null)
                return null;

            var copy = new IpBin
            {
                Region = ip.Region,
                Vga = ip.Vga,
                Version = ip.Version,
                ReleaseDate = ip.ReleaseDate,
                Name = ip.Name,
                CRC = ip.CRC,
                ProductNumber = ip.ProductNumber,
                SpecialDisc = ip.SpecialDisc,
                IsDefaultIpBin = ip.IsDefaultIpBin
            };
            if (ip.Disc != null)
                copy.Disc = ip.Disc;
            return copy;
        }

        public void RevertSerialTranslation()
        {
            if (WasSerialTranslated && OriginalSerial != null)
            {
                _ProductNumber = OriginalSerial;
                OriginalSerial = null;
                WasSerialTranslated = false;
                RaisePropertyChanged(nameof(ProductNumber));
                RaisePropertyChanged(nameof(ArchiveSerialDisplay));
                RaisePropertyChanged(nameof(HasArtwork));
                RaisePropertyChanged(nameof(ArtworkButtonToolTip));
                RaisePropertyChanged(nameof(CanManageArtwork));
            }
        }

        /// <summary>
        /// Clears the tracking flags only. The serial keeps its translated value.
        /// </summary>
        public void AcknowledgeSerialTranslation()
        {
            OriginalSerial = null;
            WasSerialTranslated = false;
            RaisePropertyChanged(nameof(HasArtwork));
            RaisePropertyChanged(nameof(ArtworkButtonToolTip));
        }

        /// <summary>
        /// No hyphens, first token only. serial.txt, OPENMENU.INI and the DAT lookups all key on
        /// this form.
        /// </summary>
        public static string CleanSerial(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial))
                return serial;

            return serial.Trim().Replace("-", "").Split(' ')[0];
        }

        public static string CleanFolderPath(string path)
        {
            if (path == null)
                return path;

            var segments = path.Split(new[] { '\\' }, StringSplitOptions.None);

            for (int i = 0; i < segments.Length; i++)
            {
                segments[i] = Helper.StripNonPrintableAscii(segments[i].Trim());
                if (segments[i].Length > namemaxlen)
                    segments[i] = segments[i].Substring(0, namemaxlen);
            }

            segments = segments.Where(s => !string.IsNullOrEmpty(s)).ToArray();
            var result = string.Join("\\", segments);

            if (result.Length > foldermaxlen)
                result = result.Substring(0, foldermaxlen);

            return result;
        }

        private string _Folder;
        public string Folder
        {
            get { return _Folder; }
            set
            {
                _Folder = CleanFolderPath(value);
                RaisePropertyChanged();
            }
        }

        private List<string> _AlternativeFolders = new List<string>();
        public List<string> AlternativeFolders
        {
            get { return _AlternativeFolders; }
            set
            {
                if (value == null)
                {
                    _AlternativeFolders = new List<string>();
                }
                else
                {
                    _AlternativeFolders = value
                        .Select(p => CleanFolderPath(p))
                        .Where(p => !string.IsNullOrEmpty(p))
                        .Distinct(StringComparer.Ordinal)
                        .Take(5)
                        .ToList();
                }
                RaisePropertyChanged();
            }
        }

        //private string _ImageFile;
        public string ImageFile
        {
            get { return ImageFiles.FirstOrDefault(); }
            //set { _ImageFile = value; RaisePropertyChanged(); }
        }

        public readonly System.Collections.Generic.List<string> ImageFiles = new System.Collections.Generic.List<string>();

        /// <summary>
        /// Lists the supported disc images found in archive order.
        /// </summary>
        public IReadOnlyList<ArchiveEntryInfo> ArchiveImageEntries { get; internal set; } =
            Array.Empty<ArchiveEntryInfo>();

        /// <summary>
        /// Identifies the archive image chosen when the item was added.
        /// </summary>
        public ArchiveEntryInfo SelectedArchiveEntry { get; internal set; }

        /// <summary>
        /// True until archive metadata has been resolved and published.
        /// </summary>
        private bool _isArchiveMetadataPending;
        public bool IsArchiveMetadataPending
        {
            get { return _isArchiveMetadataPending; }
            internal set
            {
                if (_isArchiveMetadataPending == value)
                    return;

                _isArchiveMetadataPending = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(ArchiveSerialDisplay));
                RaisePropertyChanged(nameof(ArchiveTypeDisplay));
                RaisePropertyChanged(nameof(ArchiveDiscDisplay));
                RaisePropertyChanged(nameof(ArchiveRegionDisplay));
                RaisePropertyChanged(nameof(ArchiveMetadataToolTip));
                RaisePropertyChanged(nameof(ArtworkButtonToolTip));
                RaisePropertyChanged(nameof(CanEditParsedArchiveMetadata));
            }
        }

        public string ArchiveSerialDisplay =>
            IsArchiveMetadataPending ? string.Empty : ProductNumber;

        public string ArchiveTypeDisplay =>
            IsArchiveMetadataPending ? string.Empty : DiscType;

        public string ArchiveDiscDisplay =>
            IsArchiveMetadataPending ? string.Empty : Disc;

        public string ArchiveRegionDisplay =>
            IsArchiveMetadataPending ? string.Empty : Region;

        public string ArchiveMetadataToolTip => IsArchiveMetadataPending
            ? "Cannot be edited until after SD card changes are saved."
            : null;

        public string ArtworkButtonToolTip => IsArchiveMetadataPending
            ? ArchiveMetadataToolTip
            : HasArtwork
                ? "Edit currently assigned artwork"
                : "Assign artwork";

        public bool CanEditParsedArchiveMetadata =>
            FileFormat == FileFormat.SevenZip && !IsArchiveMetadataPending;

        private string _FullFolderPath;
        public string FullFolderPath
        {
            get { return _FullFolderPath; }
            set { _FullFolderPath = value; RaisePropertyChanged(); }
        }

        private IpBin _Ip;
        public IpBin Ip
        {
            get { return _Ip; }
            set
            {
                _Ip = value;
                _ImageRegion = NormalizeRegion(value?.Region);
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(Disc));
                RaisePropertyChanged(nameof(ArchiveDiscDisplay));
                RaisePropertyChanged(nameof(Region));
                RaisePropertyChanged(nameof(ArchiveRegionDisplay));
            }
        }

        /// <summary>
        /// Region currently in the disc image file, normalized to JUE order.
        /// </summary>
        private string _ImageRegion;
        public string ImageRegion
        {
            get { return _ImageRegion; }
            set { _ImageRegion = value; }
        }

        /// <summary>
        /// Region the image still needs to be patched to on save, or null if it already matches.
        /// </summary>
        public string PendingRegionChange
        {
            get
            {
                var current = NormalizeRegion(_Ip?.Region);
                return current == null || current == _ImageRegion ? null : current;
            }
        }

        /// <summary>
        /// Wraps Ip.Disc so the grid gets a change notification.
        /// </summary>
        public string Disc
        {
            get { return _Ip?.Disc; }
            set
            {
                if (_Ip != null)
                {
                    _Ip.Disc = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(ArchiveDiscDisplay));
                }
            }
        }

        /// <summary>
        /// Wrapper property for Ip.Region to enable proper change notification.
        /// </summary>
        public string Region
        {
            get { return _Ip?.Region; }
            set
            {
                if (_Ip != null)
                {
                    _Ip.Region = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(ArchiveRegionDisplay));
                }
            }
        }

        /// <summary>
        /// Returns the region string in canonical JUE order. Characters other than
        /// J, U and E are stripped. Returns null if nothing usable remains.
        /// </summary>
        public static string NormalizeRegion(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            bool j = false, u = false, e = false;
            foreach (var c in value)
            {
                switch (char.ToUpperInvariant(c))
                {
                    case 'J': j = true; break;
                    case 'U': u = true; break;
                    case 'E': e = true; break;
                }
            }

            var result = (j ? "J" : "") + (u ? "U" : "") + (e ? "E" : "");
            return result.Length == 0 ? null : result;
        }

        private int _SdNumber;
        public int SdNumber
        {
            get { return _SdNumber; }
            set { _SdNumber = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(Location)); RaisePropertyChanged(nameof(IsNotOnSdCard)); }
        }

        public bool IsNotOnSdCard
        {
            get { return SdNumber == 0; }
        }

        private WorkMode _Work;
        public WorkMode Work
        {
            get { return _Work; }
            set { _Work = value; RaisePropertyChanged(); }
        }

        public string Location
        {
            get { return SdNumber == 0 ? "Other" : "SD card"; }
        }

        public bool CanApplyGDIShrink { get; set; }

        // True when this disc was shrunk, either during this session or on an
        // earlier save that left a "shrunk.txt" marker in the folder.
        public bool WasShrunk { get; set; }

        private FileFormat _FileFormat;
        public FileFormat FileFormat
        {
            get { return _FileFormat; }
            set
            {
                _FileFormat = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(CanEditParsedArchiveMetadata));
            }
        }

        private string _DiscType = "Game";
        public string DiscType
        {
            get { return _DiscType; }
            set
            {
                _DiscType = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(ArchiveTypeDisplay));
            }
        }

        public string GetDiscTypeFileValue()
        {
            switch (DiscType)
            {
                case "Game": return "game";
                case "Other": return "other";
                case "PSX": return "psx";
                default: return "game";
            }
        }

        public static string GetDiscTypeDisplayValue(string fileValue)
        {
            if (string.IsNullOrWhiteSpace(fileValue))
                return "Game";

            switch (fileValue.ToLower().Trim())
            {
                case "game": return "Game";
                case "other": return "Other";
                case "psx": return "PSX";
                default: return "Game";
            }
        }

        // Artwork support
        internal static BoxDatManager BoxDatManagerInstance { get; set; }

        public bool IsMenuItem
        {
            get
            {
                var name = Ip?.Name;
                return name == "GDMENU" || name == "openMenu";
            }
        }

        public bool HasArtwork
        {
            get
            {
                if (BoxDatManagerInstance == null || !BoxDatManagerInstance.IsLoaded)
                    return false;
                // Use original serial if translation hasn't been confirmed yet
                var serialToCheck = (WasSerialTranslated && OriginalSerial != null)
                    ? OriginalSerial
                    : ProductNumber;
                return BoxDatManagerInstance.HasArtworkForSerial(serialToCheck);
            }
        }

        /// <summary>
        /// False for menu discs and for items with no usable serial.
        /// </summary>
        public bool CanManageArtwork
        {
            get
            {
                if (IsMenuItem)
                    return false;
                return !string.IsNullOrWhiteSpace(ProductNumber);
            }
        }

        public void RefreshArtworkStatus()
        {
            RaisePropertyChanged(nameof(HasArtwork));
            RaisePropertyChanged(nameof(ArtworkButtonToolTip));
        }

#if DEBUG
        public override string ToString()
        {
            return $"{Location} {SdNumber} {Name}";
        }
#endif

        public event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void NotifyIpChanged()
        {
            RaisePropertyChanged(nameof(Ip));
            RaisePropertyChanged(nameof(Disc));
            RaisePropertyChanged(nameof(ArchiveDiscDisplay));
            RaisePropertyChanged(nameof(Region));
            RaisePropertyChanged(nameof(ArchiveRegionDisplay));
        }
    }
}
