using System;
using System.Collections.Generic;

namespace GDMENUCardManager.Core
{
    /// <summary>
    /// Translates raw disc serial IDs for OpenMenu compatibility.
    ///
    /// There are two types of translations:
    ///
    /// 1. Serial Translation (Table 1): These 14 discs have incorrect/duplicate serial IDs
    ///    that OpenMenu translates. The translated serial should be used everywhere:
    ///    UI display, OPENMENU.INI, and artwork DAT files.
    ///
    /// 2. Artwork Translation (Table 2): These 12 discs are regional variants that share
    ///    artwork with another version. The translation should ONLY be used for BOX.DAT
    ///    and ICON.DAT operations, NOT for UI display or OPENMENU.INI.
    /// </summary>
    public static class SerialTranslator
    {
        /// <summary>
        /// Table 1: Serial ID fix table. These 14 discs need translation based on product + date (or name).
        /// The translated serial is used EVERYWHERE (UI, INI, and artwork).
        /// </summary>
        private static string ApplyTable1(string product, string date, string name)
        {
            // All comparisons are case-sensitive and exact-match (except the name check)

            if (product == "T15117N" && date == "20010423")
                return "T15112D05";  // Alone in the Dark (PAL)

            if (product == "MK51035" && date == "20000120")
                return "MK5103550";  // Crazy Taxi (PAL)

            if (product == "T17714D50" && date == "20001116")
                return "T17719N";    // Donald Duck: Goin' Quackers (USA)

            if (product == "MK51114" && date == "20010920")
                return "MK5111450";  // Floigan Bros (PAL)

            if (product == "T36802N" && date == "19991220")
                return "T36803D05";  // Legacy of Kain (PAL)

            if (product == "MK51178" && date == "20011129")
                return "MK5117850";  // NBA 2K2 (PAL)

            if (product == "T9706D50" && date == "19991201")
                return "T9705D50";   // NBA Showtime (PAL)

            if (product == "T9504M" && date == "20000407")
                return "T9504N";     // Nightmare Creatures II (USA)

            if (product == "T7005D" && date == "20000711")
                return "T7003D";     // Plasma Sword (PAL)

            if (product == "MK51052" && date == "20010306")
                return "MK5105250";  // Skies of Arcadia (PAL)

            if (product == "T13008N" && date == "20010402")
                return "T13011D50";  // Spider-Man (PAL)

            if (product == "T0000M" && date == "19990813")
                return "T13701N";    // TNN Motorsports (USA)

            if (product == "T0006M" && date == "20030609")
                return "T0010M";     // Maximum Speed (Atomiswave)

            // NOTE: This one uses case-insensitive substring match on name, not date
            if (product == "T0009M" && !string.IsNullOrEmpty(name) &&
                name.IndexOf("orth", StringComparison.OrdinalIgnoreCase) >= 0)
                return "T0026M";     // Fist of the North Star (Atomiswave)

            // No match, so return the original.
            return product;
        }

        /// <summary>
        /// Table 2: Artwork-only remap table. These regional variants share artwork
        /// with another version. Used ONLY for BOX.DAT/ICON.DAT operations.
        /// </summary>
        private static readonly Dictionary<string, string> ArtworkRemapTable = new Dictionary<string, string>
        {
            // PAL Regional Duplicates (share artwork with base version)
            ["T13001D05"] = "T13001D",      // Blue Stinger
            ["T8111D58"] = "T8111D50",      // ECW Hardcore Revolution
            ["T45001D09"] = "T45001D05",    // Rainbow Six
            ["T45001D18"] = "T45001D05",    // Rainbow Six
            ["T45002D09"] = "T45002D05",    // Rainbow Six: Rogue Spear
            ["T36815D06"] = "T36804D05",    // Tomb Raider Chronicles
            ["T36815D13"] = "T36804D05",    // Tomb Raider Chronicles
            ["T36815D18"] = "T36804D05",    // Tomb Raider Chronicles
            ["MK5109506"] = "MK5109505",    // UEFA Dream Soccer
            ["MK5109509"] = "MK5109505",    // UEFA Dream Soccer
            ["MK5109518"] = "MK5109505",    // UEFA Dream Soccer
            ["T8103N18"] = "T8103N50",      // WWF Attitude
        };

        /// <summary>
        /// Apply Table 2 artwork remap to a serial.
        /// </summary>
        private static string ApplyTable2(string serial)
        {
            if (ArtworkRemapTable.TryGetValue(serial, out string remapped))
                return remapped;

            // No remap, so use the serial as-is.
            return serial;
        }

        /// <summary>
        /// Translates a raw disc serial for display and OPENMENU.INI.
        /// Applies only Table 1 (serial ID fixes).
        ///
        /// Use this for:
        /// - Serial shown in the Games List UI
        /// - Serial written to OPENMENU.INI
        /// </summary>
        /// <param name="rawProduct">Product ID (normalized: no hyphen, trimmed)</param>
        /// <param name="date">Date from IP.BIN (8 chars, YYYYMMDD), or null/empty if unavailable</param>
        /// <param name="name">Name from IP.BIN (trimmed), or null/empty if unavailable</param>
        /// <returns>The translated serial for display/INI use</returns>
        public static string TranslateSerial(string rawProduct, string date, string name)
        {
            if (string.IsNullOrWhiteSpace(rawProduct))
                return rawProduct;

            return ApplyTable1(rawProduct, date ?? "", name ?? "");
        }

        /// <summary>
        /// Translates a serial (already Table 1 translated) for artwork operations.
        /// Applies only Table 2 (artwork remap for regional variants).
        ///
        /// Use this for:
        /// - Looking up artwork in BOX.DAT
        /// - Writing artwork to BOX.DAT/ICON.DAT
        /// - Checking if artwork exists
        /// </summary>
        /// <param name="serial">Serial that has already had Table 1 applied (i.e., ProductNumber)</param>
        /// <returns>The artwork serial for BOX.DAT/ICON.DAT operations</returns>
        public static string TranslateForArtwork(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial))
                return serial;

            return ApplyTable2(serial);
        }
    }
}
