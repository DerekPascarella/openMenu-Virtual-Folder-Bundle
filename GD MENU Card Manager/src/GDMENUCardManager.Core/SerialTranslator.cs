using System;
using System.Collections.Generic;

namespace GDMENUCardManager.Core
{
    /// <summary>
    /// Two independent tables, mirroring openMenu's own behavior. Table 1 fixes wrong or duplicated
    /// serials and its result is used everywhere: UI, INI and art DATs. Table 2 points regional
    /// variants at shared artwork and applies to BOX.DAT and ICON.DAT lookups only, never to the UI
    /// or the INI.
    /// </summary>
    public static class SerialTranslator
    {
        // Table 1, matched on product plus release date. One entry matches on name instead.
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

            return product;
        }

        // Table 2, artwork-only remap. Policy is on the class summary.
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

        private static string ApplyTable2(string serial)
        {
            if (ArtworkRemapTable.TryGetValue(serial, out string remapped))
                return remapped;

            return serial;
        }

        /// <summary>
        /// Table 1 only. This is the serial for the UI and OPENMENU.INI.
        /// </summary>
        /// <param name="rawProduct">Must already be normalized: no hyphen, trimmed.</param>
        /// <param name="date">YYYYMMDD from IP.BIN, or empty when unknown.</param>
        public static string TranslateSerial(string rawProduct, string date, string name)
        {
            if (string.IsNullOrWhiteSpace(rawProduct))
                return rawProduct;

            return ApplyTable1(rawProduct, date ?? "", name ?? "");
        }

        /// <summary>
        /// Table 2 only. For BOX.DAT and ICON.DAT operations.
        /// </summary>
        /// <param name="serial">Must already be Table 1 translated (i.e.,
        /// GdItem.ProductNumber).</param>
        public static string TranslateForArtwork(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial))
                return serial;

            return ApplyTable2(serial);
        }
    }
}
