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
                RaisePropertyChanged(nameof(HasArtwork));
                RaisePropertyChanged(nameof(CanManageArtwork));
            }
        }

        /// <summary>
        /// The original serial before Table 1 translation was applied.
        /// Null if no translation occurred.
        /// </summary>
        public string OriginalSerial { get; private set; }

        public bool WasSerialTranslated { get; private set; }

        public void RevertSerialTranslation()
        {
            if (WasSerialTranslated && OriginalSerial != null)
            {
                _ProductNumber = OriginalSerial;
                OriginalSerial = null;
                WasSerialTranslated = false;
                RaisePropertyChanged(nameof(ProductNumber));
                RaisePropertyChanged(nameof(HasArtwork));
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
            set { _Ip = value; _ImageRegion = NormalizeRegion(value?.Region); RaisePropertyChanged(); RaisePropertyChanged(nameof(Disc)); RaisePropertyChanged(nameof(Region)); }
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

        private FileFormat _FileFormat;
        public FileFormat FileFormat
        {
            get { return _FileFormat; }
            set { _FileFormat = value; RaisePropertyChanged(); }
        }

        private string _DiscType = "Game";
        public string DiscType
        {
            get { return _DiscType; }
            set { _DiscType = value; RaisePropertyChanged(); }
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
            RaisePropertyChanged(nameof(Region));
        }
    }
}
