using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace GDMENUCardManager.Core
{
    public class DiscDbEntry
    {
        public string Name { get; set; }
        public string Serial { get; set; }
        public string Type { get; set; }
        public string Disc { get; set; }
        public bool? Vga { get; set; }
        public string Region { get; set; }
        public string Version { get; set; }
        public string Date { get; set; }
        public string Folder { get; set; }
        public List<string> AltFolders { get; set; }
        public bool Shrunk { get; set; }

        [JsonIgnore]
        public bool IsUsable => !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Serial);

        [JsonIgnore]
        public bool HasIpData => Disc != null && Vga != null && Version != null && Date != null && Region != null;
    }

    public class DiscDatabase
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, DiscDbEntry> Items { get; set; } = new Dictionary<string, DiscDbEntry>();

        private static readonly JsonSerializerOptions serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static string GetPath(string sdPath) => Path.Combine(sdPath, Constants.DiscDatabaseFile);

        // A bad database must never block loading. Damage that breaks the
        // JSON document itself drops the whole file and returns null.
        // Schema damage inside one entry drops only that entry. Affected
        // folders take the full parse path and the file is rewritten on save.
        public static async Task<DiscDatabase> LoadAsync(string sdPath)
        {
            var path = GetPath(sdPath);
            try
            {
                if (!await Helper.FileExistsAsync(path))
                    return null;

                var json = await Helper.ReadAllTextAsync(path);
                var db = new DiscDatabase();
                using (var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
                {
                    if (doc.RootElement.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number)
                        db.Version = v.GetInt32();

                    if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Object)
                        return null;

                    foreach (var prop in items.EnumerateObject())
                    {
                        try
                        {
                            var entry = JsonSerializer.Deserialize<DiscDbEntry>(prop.Value.GetRawText(), serializerOptions);
                            if (entry != null && entry.IsUsable)
                            {
                                if (entry.AltFolders != null)
                                    entry.AltFolders = entry.AltFolders.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();

                                db.Items[prop.Name] = entry;
                            }
                        }
                        catch (JsonException) { }
                    }
                }
                return db;
            }
            catch
            {
                return null;
            }
        }

        // A card pulled the moment this method returns must not lose the file.
        public async Task SaveAsync(string sdPath)
        {
            var path = GetPath(sdPath);
            var json = JsonSerializer.Serialize(this, serializerOptions);
            var tmp = path + ".tmp";
            var bytes = Encoding.UTF8.GetBytes(json);

            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            await Helper.MoveFileAsync(tmp, path);
        }
    }
}
