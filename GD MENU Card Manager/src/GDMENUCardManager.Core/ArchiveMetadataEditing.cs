using System;

namespace GDMENUCardManager.Core
{
    [Flags]
    public enum ArchiveMetadataField
    {
        None = 0,
        Serial = 1,
        Type = 2,
        Disc = 4,
        Region = 8
    }

    public readonly record struct ArchiveMetadataFieldState(
        string Value,
        bool IsManual,
        string OriginalSerial = null,
        bool WasSerialTranslated = false);
}
