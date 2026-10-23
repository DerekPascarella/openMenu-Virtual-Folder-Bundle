using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GDMENUCardManager.Core
{
    internal sealed class CardSaveTransaction
    {
        internal const string DirectoryName = "GDMCM_SAVE";
        internal const string JournalFileName = "GDMCM_SAVE.JSON";
        internal const string NextSuffix = ".NEXT";
        internal sealed class Move
        {
            public string Original { get; set; }
            public string Stage { get; set; }
            public string Final { get; set; }
        }

        internal sealed class Import
        {
            public string Stage { get; set; }
            public string Final { get; set; }
            public string Phase { get; set; } = "Preparing";
            public DiscDbEntry Entry { get; set; }
        }

        internal sealed class Menu
        {
            public string Stage { get; set; }
            public string Backup { get; set; }
            public string Final { get; set; } = "01";
            public DiscDbEntry Entry { get; set; }
            public bool HadOld { get; set; }
            public string Phase { get; set; } = "Preparing";
        }

        internal sealed class Journal
        {
            public string Owner { get; set; } = "GDMENUCardManager.Save";
            public int Version { get; set; } = 1;
            public string Phase { get; set; } = "Prepared";
            public List<Move> Moves { get; set; } = new List<Move>();
            public string ForwardDatabase { get; set; }
            public string Work { get; set; }
            [JsonIgnore]
            public List<Import> Imports { get; set; } = new List<Import>();
            [JsonIgnore]
            public Dictionary<string, DiscDbEntry> Updates { get; set; } = new Dictionary<string, DiscDbEntry>();
            public Menu Menu { get; set; }
            public string FinalDatabase { get; set; }
        }

        private readonly string root;
        private readonly string staging;
        private readonly Journal journal;
        internal bool IsCommitted => journal.Phase == "Committed";
        internal IReadOnlyList<Move> Moves => journal.Moves;
        internal string WorkPath => journal.Work == null ? null : Staged(journal.Work);

        private CardSaveTransaction(string root, Journal journal)
        {
            this.root = Path.GetFullPath(root);
            staging = Path.Combine(this.root, DirectoryName);
            this.journal = journal;
        }

        internal static CardSaveTransaction Begin(string root, IEnumerable<Move> moves, string forwardDatabase, bool ownWork = false)
        {
            var transaction = new CardSaveTransaction(root, new Journal
            {
                Moves = moves.ToList(),
                ForwardDatabase = forwardDatabase,
                Work = ownWork ? "work-" + Guid.NewGuid().ToString("N") : null
            });
            if (Directory.Exists(transaction.staging) || File.Exists(transaction.staging) ||
                File.Exists(Path.Combine(root, JournalFileName)) || File.Exists(Path.Combine(root, JournalFileName) + NextSuffix))
                throw new IOException($"Unresolved save staging: {transaction.staging}");
            transaction.Validate();
            foreach (var move in transaction.journal.Moves)
            {
                RequireDirectory(transaction.Card(move.Original));
                if (!transaction.journal.Moves.Any(x => x.Original == move.Final))
                    RequireAbsent(transaction.Card(move.Final));
            }
            Directory.CreateDirectory(transaction.staging);
            transaction.Persist();
            return transaction;
        }

        internal void Renumber()
        {
            foreach (var move in journal.Moves)
                MoveDirectory(Card(move.Original), Staged(move.Stage));
            journal.Phase = "Publishing";
            Persist();
            foreach (var move in journal.Moves)
                MoveDirectory(Staged(move.Stage), Card(move.Final));
            foreach (var move in journal.Moves)
                RequireDirectory(Card(move.Final));
            journal.Phase = "Finalize";
            Persist();
            FinishRenumber();
        }

        private void FinishRenumber()
        {
            foreach (var move in journal.Moves)
            {
                RequireDirectory(Card(move.Final));
                RequireAbsent(Staged(move.Stage));
            }
            if (journal.ForwardDatabase != null)
                InstallDatabase(journal.ForwardDatabase);
            journal.Phase = "Committed";
            Persist();
        }

        internal static CardSaveTransaction Recover(string root, Action<string> replacementPublished = null)
        {
            string directory = Path.Combine(root, DirectoryName);
            string path = Path.Combine(root, JournalFileName);
            CheckNotReparse(directory);
            CheckNotReparse(path);
            CheckNotReparse(path + NextSuffix);
            if (File.Exists(directory) || Directory.Exists(path) || Directory.Exists(path + NextSuffix))
                throw new IOException($"A reserved save recovery path is occupied: {path}, {directory}");
            if (!File.Exists(path))
            {
                if (Directory.Exists(directory) || File.Exists(path + NextSuffix))
                    throw new IOException($"Unrecorded save staging was preserved: {directory}, {path + NextSuffix}");
                return null;
            }
            Journal record;
            try
            {
                record = ReadJournal(path);
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException)
            {
                throw new IOException($"Cannot read save recovery record. Preserve this folder: {directory}", ex);
            }
            var transaction = new CardSaveTransaction(root, record);
            transaction.ReadImports();
            transaction.Validate();
            transaction.Recover(replacementPublished);
            return transaction;
        }

        internal void Recover(Action<string> replacementPublished = null)
        {
            Validate();
            if (journal.Phase == "Publishing")
            {
                journal.Phase = "Restaging";
                Persist();
            }
            if (journal.Phase == "Restaging")
            {
                foreach (var move in journal.Moves)
                    ResolveMove(Card(move.Final), Staged(move.Stage));
                journal.Phase = "Restoring";
                Persist();
            }
            if (journal.Phase == "Prepared" || journal.Phase == "Restoring")
            {
                foreach (var move in journal.Moves)
                    ResolveMove(Staged(move.Stage), Card(move.Original));
                RemoveRecord();
                return;
            }
            if (journal.Phase == "Finalize")
                FinishRenumber();
            RecoverMenu(replacementPublished);
            VerifyCommittedMoves();
            foreach (var import in journal.Imports)
            {
                if (import.Phase == "Preparing")
                    throw new IncompletePreparationException(Staged(import.Stage));
                ResolveMove(Staged(import.Stage), Card(import.Final));
                import.Phase = "Committed";
            }
            if (journal.FinalDatabase != null)
                InstallDatabase(journal.FinalDatabase);
            else if (journal.ForwardDatabase != null && (journal.Imports.Count > 0 || journal.Updates.Count > 0))
            {
                var database = DiscDatabase.Parse(File.ReadAllText(DiscDatabase.GetPath(root)));
                foreach (var import in journal.Imports)
                    database.Items[import.Final] = import.Entry;
                foreach (var update in journal.Updates)
                    database.Items[update.Key] = update.Value;
                InstallDatabase(database.Serialize());
            }
            RetireImports();
            Persist();
            RemoveRecord();
        }

        internal string BeginImport(int number)
        {
            if (!IsCommitted)
                throw new InvalidOperationException("Renumbering must finish before an import starts.");
            var import = new Import { Stage = "import-" + Guid.NewGuid().ToString("N"), Final = Manager.FormatFolderNumber(number) };
            RequireAbsent(Card(import.Final));
            journal.Imports.Add(import);
            WriteImport(import);
            return Staged(import.Stage);
        }

        internal void PublishImport(string path, DiscDbEntry entry)
        {
            var import = journal.Imports.Single(x => Staged(x.Stage) == path);
            RequireDirectory(path);
            import.Entry = entry;
            import.Phase = "Publish";
            WriteImport(import);
            MoveDirectory(path, Card(import.Final));
            import.Phase = "Committed";
            WriteImport(import);
        }

        internal string BeginMenu(bool hadOld) => BeginReplacement(1, hadOld);

        internal string BeginReplacement(int number, bool hadOld)
        {
            if (!IsCommitted || journal.Menu != null)
                throw new InvalidOperationException("The previous card publication is unfinished.");
            journal.Menu = new Menu
            {
                Stage = "menu-" + Guid.NewGuid().ToString("N"),
                Backup = "backup-" + Guid.NewGuid().ToString("N"),
                HadOld = hadOld,
                Final = Manager.FormatFolderNumber(number)
            };
            Persist();
            return Staged(journal.Menu.Stage);
        }

        internal void PublishMenu(DiscDbEntry entry = null, Action<string> replacementPublished = null)
        {
            journal.Menu.Entry = entry;
            RequireDirectory(Staged(journal.Menu.Stage));
            journal.Menu.Phase = "Publishing";
            Persist();
            RecoverMenu(replacementPublished);
        }

        private void RecoverMenu(Action<string> replacementPublished)
        {
            var menu = journal.Menu;
            if (menu == null)
                return;
            var prepared = Staged(menu.Stage);
            var backup = Staged(menu.Backup);
            var final = Card(menu.Final);
            if (menu.Phase == "Preparing")
                throw new IncompletePreparationException(prepared);
            if (menu.Phase == "Publishing")
            {
                if (Directory.Exists(prepared))
                {
                    if (menu.HadOld)
                        ResolveMove(final, backup);
                    else
                        RequireAbsent(backup);
                    MoveDirectory(prepared, final);
                }
                else
                {
                    RequireDirectory(final);
                    if (menu.HadOld)
                        RequireDirectory(backup);
                    else
                        RequireAbsent(backup);
                }
                journal.Menu.Phase = "Cleanup";
                Persist();
            }
            RequireDirectory(final);
            RequireAbsent(prepared);
            if (!menu.HadOld)
                RequireAbsent(backup);
            replacementPublished?.Invoke(menu.Stage);
            if (menu.Entry != null)
            {
                WriteAtomic(Staged("update-" + menu.Final + ".json"), JsonSerializer.Serialize(menu.Entry));
                journal.Updates[menu.Final] = menu.Entry;
            }
            if (Directory.Exists(backup))
            {
                CheckNotReparse(backup);
                Directory.Delete(backup, true);
            }
            journal.Menu = null;
            Persist();
        }

        internal void CommitDatabase(string payload)
        {
            journal.FinalDatabase = payload;
            Persist();
            if (payload != null)
                InstallDatabase(payload);
            RetireImports();
            Persist();
        }

        internal void Complete()
        {
            if (!IsCommitted || journal.Menu != null || journal.Imports.Count != 0 || journal.Updates.Count != 0)
                throw new IOException($"The save has unresolved publications: {staging}");
            VerifyCommittedMoves();
            RemoveRecord();
        }

        private void VerifyCommittedMoves()
        {
            foreach (var move in journal.Moves)
            {
                RequireDirectory(Card(move.Final));
                RequireAbsent(Staged(move.Stage));
            }
        }

        private void InstallDatabase(string payload)
        {
            DiscDatabase.Parse(payload);
            WriteAtomic(DiscDatabase.GetPath(root), payload);
            if (File.ReadAllText(DiscDatabase.GetPath(root)) != payload)
                throw new IOException($"The saved database could not be verified: {DiscDatabase.GetPath(root)}");
        }

        internal sealed class IncompletePreparationException : IOException
        {
            internal string PreparationPath { get; }
            internal IncompletePreparationException(string path)
                : base($"An incomplete preparation was preserved at {path}. Numbered games were not removed.")
            {
                PreparationPath = path;
            }
        }

        private static Journal ReadJournal(string path)
        {
            string payload = File.ReadAllText(path);
            using var document = JsonDocument.Parse(payload);
            RequireProperties(document.RootElement, path, "Owner", "Version", "Phase", "Moves");
            if (document.RootElement.TryGetProperty("Menu", out var menu) && menu.ValueKind != JsonValueKind.Null)
                RequireProperties(menu, path, "Stage", "Backup", "HadOld", "Phase", "Final");
            return JsonSerializer.Deserialize<Journal>(payload);
        }

        private static void RequireProperties(JsonElement element, string path, params string[] names)
        {
            if (element.ValueKind != JsonValueKind.Object || names.Any(name => !element.TryGetProperty(name, out _)))
                throw new IOException($"Incomplete save recovery record: {path}");
            var properties = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
                if (!properties.Add(property.Name))
                    throw new IOException($"Duplicate field in save recovery record: {path}");
        }

        private void ReadImports()
        {
            if (!Directory.Exists(staging))
                return;
            foreach (string path in Directory.EnumerateFiles(staging, "update-*.json"))
            {
                CheckNotReparse(path);
                string number = Path.GetFileNameWithoutExtension(path).Substring("update-".Length);
                var entry = JsonSerializer.Deserialize<DiscDbEntry>(File.ReadAllText(path));
                if (!IsNumber(number) || number == "01" || entry == null)
                    throw new IOException($"Invalid replacement metadata record: {path}");
                journal.Updates.Add(number, entry);
            }
            foreach (string path in Directory.EnumerateFiles(staging, "import-*.json"))
            {
                CheckNotReparse(path);
                string payload = File.ReadAllText(path);
                using var document = JsonDocument.Parse(payload);
                RequireProperties(document.RootElement, path, "Stage", "Final", "Phase");
                var import = JsonSerializer.Deserialize<Import>(payload);
                if (import == null || Path.GetFileName(path) != import.Stage + ".json")
                    throw new IOException($"Invalid import recovery record: {path}");
                journal.Imports.Add(import);
            }
        }

        private void WriteImport(Import import)
        {
            WriteAtomic(Staged(import.Stage + ".json"), JsonSerializer.Serialize(import));
        }

        private void RetireImports()
        {
            foreach (var import in journal.Imports)
            {
                RequireAbsent(Staged(import.Stage));
                string path = Staged(import.Stage + ".json");
                if (File.Exists(path + NextSuffix))
                    File.Delete(path + NextSuffix);
                File.Delete(path);
            }
            journal.Imports.Clear();
            foreach (var number in journal.Updates.Keys)
            {
                File.Delete(Staged("update-" + number + ".json"));
                File.Delete(Staged("update-" + number + ".json" + NextSuffix));
            }
            journal.Updates.Clear();
        }

        internal static void DiscardIncomplete(string root, string preparationPath)
        {
            var transaction = new CardSaveTransaction(root, ReadJournal(Path.Combine(root, JournalFileName)));
            transaction.ReadImports();
            transaction.Validate();
            var import = transaction.journal.Imports.SingleOrDefault(x => transaction.Staged(x.Stage) == preparationPath && x.Phase == "Preparing");
            bool menu = transaction.journal.Menu?.Phase == "Preparing" && transaction.Staged(transaction.journal.Menu.Stage) == preparationPath;
            if (import == null && !menu)
                throw new IOException($"The path is not a recorded incomplete preparation: {preparationPath}");
            CheckNotReparse(preparationPath);
            if (Directory.Exists(preparationPath))
                Directory.Delete(preparationPath, true);
            if (import != null)
            {
                File.Delete(transaction.Staged(import.Stage + ".json"));
                File.Delete(transaction.Staged(import.Stage + ".json" + NextSuffix));
            }
            else
            {
                transaction.journal.Menu = null;
                transaction.Persist();
            }
        }

        private void Persist()
        {
            WriteAtomic(Path.Combine(root, JournalFileName), JsonSerializer.Serialize(journal));
        }

        internal static void WriteAtomic(string path, string text)
        {
            CheckNotReparse(path);
            CheckNotReparse(path + NextSuffix);
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            using (var stream = new FileStream(path + NextSuffix, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            File.Move(path + NextSuffix, path, true);
        }

        private void RemoveRecord()
        {
            Validate();
            if (journal.Work != null)
            {
                CheckNotReparse(WorkPath);
                if (File.Exists(WorkPath))
                    throw new IOException($"The recorded temporary directory is occupied by a file: {WorkPath}");
                if (Directory.Exists(WorkPath))
                    Directory.Delete(WorkPath, true);
            }
            if (Directory.Exists(staging))
            {
                if (Directory.EnumerateFileSystemEntries(staging).Any())
                    throw new IOException($"Unfinished save records or folders remain: {staging}");
                Directory.Delete(staging);
            }
            string path = Path.Combine(root, JournalFileName);
            if (File.Exists(path + NextSuffix))
                File.Delete(path + NextSuffix);
            File.Delete(path);
        }

        private void Validate()
        {
            CheckNotReparse(root);
            CheckNotReparse(staging);
            CheckNotReparse(Path.Combine(root, JournalFileName));
            CheckNotReparse(Path.Combine(root, JournalFileName) + NextSuffix);
            if (journal == null || journal.Owner != "GDMENUCardManager.Save" || journal.Version != 1 ||
                journal.Moves == null || journal.Imports == null ||
                !new[] { "Prepared", "Publishing", "Restaging", "Restoring", "Finalize", "Committed" }.Contains(journal.Phase))
                throw new IOException($"Unrecognized save recovery record: {staging}");
            var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var finals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var records = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (journal.Work != null && (!IsStage(journal.Work, "work-") || !stages.Add(journal.Work)))
                throw new IOException($"Invalid temporary work mapping in {staging}");
            foreach (var move in journal.Moves)
            {
                if (move == null || !IsNumber(move.Original) || !IsNumber(move.Final) || move.Original == move.Final ||
                    !sources.Add(move.Original) || !finals.Add(move.Final) || !IsStage(move.Stage, "move-") || !stages.Add(move.Stage))
                    throw new IOException($"Invalid folder mappings in {staging}");
                CheckNotReparse(Card(move.Original));
                CheckNotReparse(Card(move.Final));
            }
            foreach (var import in journal.Imports)
            {
                if (import == null || !IsNumber(import.Final) || import.Final == "01" || !finals.Add(import.Final) ||
                    !IsStage(import.Stage, "import-") || !stages.Add(import.Stage) ||
                    !new[] { "Preparing", "Publish", "Committed" }.Contains(import.Phase) ||
                    (import.Phase != "Preparing" && import.Entry == null))
                    throw new IOException($"Invalid import record in {staging}");
                records.Add(import.Stage + ".json");
                records.Add(import.Stage + ".json" + NextSuffix);
                CheckNotReparse(Card(import.Final));
            }
            foreach (var update in journal.Updates)
            {
                if (!IsNumber(update.Key) || update.Key == "01" || update.Value == null)
                    throw new IOException($"Invalid replacement metadata in {staging}");
                records.Add("update-" + update.Key + ".json");
                records.Add("update-" + update.Key + ".json" + NextSuffix);
            }
            if (journal.Menu != null)
            {
                var menu = journal.Menu;
                if (!IsNumber(menu.Final) || (menu.Final != "01" && menu.Phase != "Preparing" && menu.Entry == null) || !IsStage(menu.Stage, "menu-") || !stages.Add(menu.Stage) ||
                    !IsStage(menu.Backup, "backup-") || !stages.Add(menu.Backup) ||
                    !new[] { "Preparing", "Publishing", "Cleanup" }.Contains(menu.Phase))
                    throw new IOException($"Invalid menu record in {staging}");
                CheckNotReparse(Card(menu.Final));
                records.Add("update-" + menu.Final + ".json" + NextSuffix);
            }
            if (journal.Phase != "Committed" && (journal.Imports.Count != 0 || journal.Menu != null || journal.FinalDatabase != null))
                throw new IOException($"Inconsistent save phases in {staging}");
            if (journal.ForwardDatabase != null)
                DiscDatabase.Parse(journal.ForwardDatabase);
            if (journal.FinalDatabase != null)
                DiscDatabase.Parse(journal.FinalDatabase);
            if (Directory.Exists(staging))
                foreach (var path in Directory.EnumerateFileSystemEntries(staging))
                {
                    CheckNotReparse(path);
                    var name = Path.GetFileName(path);
                    if (Directory.Exists(path) ? !stages.Contains(name) : !records.Contains(name))
                        throw new IOException($"Unrecognized data in save staging: {path}");
                }
        }

        private static bool IsStage(string value, string prefix) => value != null && value.StartsWith(prefix, StringComparison.Ordinal) &&
            value.Length > prefix.Length && value.All(c => (c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9') || c == '-');

        internal static bool IsNumber(string value) => int.TryParse(value, out int number) && number > 0 && number < 10000 && Manager.FormatFolderNumber(number) == value;
        private string Card(string name) => Path.Combine(root, name);
        private string Staged(string name) => Path.Combine(staging, name);

        internal static void CheckNotReparse(string path)
        {
            if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Save recovery refuses a linked path: {path}");
        }

        private static void RequireAbsent(string path)
        {
            if (File.Exists(path) || Directory.Exists(path))
                throw new IOException($"Save destination is already occupied: {path}");
        }

        private static void RequireDirectory(string path)
        {
            CheckNotReparse(path);
            if (!Directory.Exists(path))
                throw new IOException($"Expected save folder is missing: {path}");
        }

        private static void MoveDirectory(string from, string to)
        {
            RequireDirectory(from);
            RequireAbsent(to);
            Directory.Move(from, to);
        }

        private static void ResolveMove(string from, string to)
        {
            bool source = Directory.Exists(from);
            bool destination = Directory.Exists(to);
            if (source == destination || File.Exists(from) || File.Exists(to))
                throw new IOException($"Ambiguous save recovery locations. Preserve both paths: {from}, {to}");
            if (source)
                MoveDirectory(from, to);
            else
                RequireDirectory(to);
        }

        internal static void CheckLegacyFolders(string root)
        {
            var folders = Directory.EnumerateDirectories(root)
                .Where(folder => Guid.TryParse(Path.GetFileName(folder), out _) &&
                    Directory.EnumerateFiles(folder).Any(x => Manager.supportedImageFormats.Contains(Path.GetExtension(x).ToLowerInvariant())))
                .ToList();
            if (folders.Count > 0)
                throw new IOException("Unrecorded game folders were preserved. Review these folders before saving:\n\n" + string.Join(Environment.NewLine, folders));
        }
    }
}
