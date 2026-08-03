using GDMENUCardManager.Core.Interface;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace GDMENUCardManager.Core
{
    // Uses data from DuckStation
    //https://github.com/stenzek/duckstation/blob/master/data/resources/gamedb.json.

    /// <summary>
    /// PSX title lookup by serial, sourced from DuckStation's gamedb.json.
    /// </summary>
    public static class PlayStationDB
    {
        private static readonly List<PSDBEntry> _list = new List<PSDBEntry>();
        /// <summary>
        /// Safe to call when the file is missing, lookups then return null.
        /// </summary>
        public static void LoadFrom(string file)
        {
            if (!File.Exists(file))
                return;

            try
            {
                _list.Clear();
                using (var stream = File.OpenRead(file))
                {
                    var deserialized = JsonSerializer.Deserialize<IEnumerable<PSDBEntry>>(stream);
                    _list.AddRange(deserialized);
                }
            }
            catch
            {

            }
        }

        public static void SaveTo(string file)
        {
            var opt = new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            };

            using (var stream = File.Create(file))
            {
                System.Text.Json.JsonSerializer.Serialize(stream, _list, opt);
            }
        }

        public static PSDBEntry FindBySerial(string serial)
        {
            return _list.FirstOrDefault(x => x.serial.Equals(serial, StringComparison.InvariantCultureIgnoreCase));
        }

    }

    public class PSDBEntry
    {
        public string serial { get; set; }
        public string name { get; set; }
        //public List<string> codes { get; set; }
        public string releaseDate { get; set; }
    }
}
