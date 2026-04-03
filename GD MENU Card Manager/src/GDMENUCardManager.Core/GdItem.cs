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

                // If setting to the same translated value, skip to preserve translation tracking.
                // But if translation hasn't happened yet (WasSerialTranslated=false), allow re-processing
                // with potentially new Ip context (date/name).
                if (cleaned == _ProductNumber && WasSerialTranslated)
                    return;

                _ProductNumber = cleaned;
                OriginalSerial = null;
                WasSerialTranslated = false;

                if (_ProductNumber != null)
                {
                    if (_ProductNumber.Length > serialmaxlen)
                        _ProductNumber = _ProductNumber.Substring(0, serialmaxlen);

                    // Store the cleaned serial before translation
                    string beforeTranslation = _ProductNumber;

                    // Apply OpenMenu serial translation (Table 1 only - for UI and OPENMENU.INI)
                    // Table 2 (artwork remap) is applied separately in BoxDatManager/IconDatManager
                    // Use Ip context if available, with fallback to item.Name for the name check
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

        /// <summary>
        /// True if the serial was automatically translated by Table 1.
        /// </summary>
        public bool WasSerialTranslated { get; private set; }

        /// <summary>
        /// Reverts the serial to its original (pre-translation) value.
        /// Only has effect if WasSerialTranslated is true.
        /// </summary>
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
        /// Clears the translation tracking flags without changing the serial.
        /// Call this after user acknowledges the translation.
        /// </summary>
        public void AcknowledgeSerialTranslation()
        {
            OriginalSerial = null;
            WasSerialTranslated = false;
            RaisePropertyChanged(nameof(HasArtwork));
        }

        /// <summary>
        /// Cleans a serial number by removing hyphens and taking only the part before any space.
        /// This ensures consistency between serial.txt, OPENMENU.INI, and BOX.DAT lookups.
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
            set { _Ip = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(Disc)); }
        }

        /// <summary>
        /// Wrapper property for Ip.Disc to enable proper change notification.
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

        /// <summary>
        /// Returns true if the item is a menu disc (GDMENU or openMenu).
        /// </summary>
        public bool IsMenuItem
        {
            get
            {
                var name = Ip?.Name;
                return name == "GDMENU" || name == "openMenu";
            }
        }

        /// <summary>
        /// Returns true if artwork exists for this item's serial in BOX.DAT.
        /// </summary>
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
        /// Returns true if the Art column button should be enabled for this item.
        /// True when: not a menu item AND valid serial.
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

        /// <summary>
        /// Notify the UI that HasArtwork may have changed.
        /// Call this after modifying artwork in BoxDatManager.
        /// </summary>
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

        /// <summary>
        /// Notifies the UI that the Ip property has changed (e.g., after modifying Ip.Disc).
        /// </summary>
        public void NotifyIpChanged()
        {
            RaisePropertyChanged(nameof(Ip));
            RaisePropertyChanged(nameof(Disc));
        }
    }
}
