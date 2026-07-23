using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GDMENUCardManager.Core.Audio;

namespace GDMENUCardManager.Core.MenuOptions
{
    public enum MenuStyle
    {
        Folders,
        Scroll,
        Grid3,
        LineDesc
    }

    public class ThemeEntry
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public bool BuiltIn { get; set; }

        public override string ToString() => DisplayName;
    }

    public class MenuOptionsConfig
    {
        public bool ForceStyleTheme { get; set; }
        public MenuStyle Style { get; set; } = MenuStyle.Folders;
        public string ThemeId { get; set; } = "DEFAULT";

        public bool BgmEnabled { get; set; }
        public string BgmSourceFile { get; set; }
        public string BgmConvertedDate { get; set; }
        public bool BgmFileExists { get; set; }
    }

    /// <summary>
    /// Reads and writes DEFAULTS.INI and BGM.ADP in menu_data. openMenu picks
    /// both up from the disc root of the generated menu image.
    /// </summary>
    public class MenuOptionsManager
    {
        public const string DefaultsIniFileName = "DEFAULTS.INI";
        public const string BgmFileName = "BGM.ADP";

        private readonly string menuDataPath;
        private readonly string themeScanPath;

        public string DefaultsIniPath => Path.Combine(menuDataPath, DefaultsIniFileName);
        public string BgmPath => Path.Combine(menuDataPath, BgmFileName);

        public MenuOptionsManager(string menuDataPath, string themeScanPath)
        {
            this.menuDataPath = menuDataPath;
            this.themeScanPath = themeScanPath;
        }

        public static string StyleToIniValue(MenuStyle style)
        {
            switch (style)
            {
                case MenuStyle.LineDesc: return "LINEDESC";
                case MenuStyle.Grid3: return "GRID3";
                case MenuStyle.Scroll: return "SCROLL";
                default: return "FOLDERS";
            }
        }

        public static bool TryParseStyle(string value, out MenuStyle style)
        {
            switch (value?.Trim().ToUpperInvariant())
            {
                case "LINEDESC": style = MenuStyle.LineDesc; return true;
                case "GRID3": style = MenuStyle.Grid3; return true;
                case "SCROLL": style = MenuStyle.Scroll; return true;
                case "FOLDERS": style = MenuStyle.Folders; return true;
                default: style = MenuStyle.Folders; return false;
            }
        }

        public static string DefaultThemeId(MenuStyle style)
        {
            return style == MenuStyle.LineDesc || style == MenuStyle.Grid3 ? "NTSC_U" : "DEFAULT";
        }

        /// <summary>
        /// Themes valid for the given style, built-ins first then the scanned
        /// theme folders. Follows the discovery rules of openMenu's load_themes,
        /// including the CUSTOM #n fallback name for folders that have no
        /// theme.ini. The ten per family cap matches the console's fixed theme
        /// arrays, which openMenu itself never bounds checks.
        /// </summary>
        public List<ThemeEntry> GetThemesForStyle(MenuStyle style)
        {
            var result = new List<ThemeEntry>();
            string prefix;

            if (style == MenuStyle.LineDesc || style == MenuStyle.Grid3)
            {
                result.Add(new ThemeEntry { Id = "NTSC_U", DisplayName = "NTSC-U", BuiltIn = true });
                result.Add(new ThemeEntry { Id = "NTSC_J", DisplayName = "NTSC-J", BuiltIn = true });
                result.Add(new ThemeEntry { Id = "PAL", DisplayName = "PAL", BuiltIn = true });
                prefix = "CUST_";
            }
            else if (style == MenuStyle.Scroll)
            {
                result.Add(new ThemeEntry { Id = "DEFAULT", DisplayName = "GDMENU", BuiltIn = true });
                prefix = "SCROLL_";
            }
            else
            {
                result.Add(new ThemeEntry
                {
                    Id = "DEFAULT",
                    DisplayName = ReadThemeName(Path.Combine(themeScanPath, "FOLDERS")) ?? "FoldersDefault",
                    BuiltIn = true
                });
                prefix = "FOLDERS_";
            }

            if (Directory.Exists(themeScanPath))
            {
                var dirs = Directory.GetDirectories(themeScanPath)
                    .Select(Path.GetFileName)
                    .Where(d => d.Length > prefix.Length && d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                    .Take(10);

                foreach (var dir in dirs)
                {
                    var name = ReadThemeName(Path.Combine(themeScanPath, dir));
                    if (name == null)
                    {
                        // openMenu names INI-less themes CUSTOM #n from the char after the prefix
                        int n = dir[prefix.Length] - '0';
                        name = $"CUSTOM #{n}";
                    }
                    result.Add(new ThemeEntry { Id = dir.ToUpperInvariant(), DisplayName = name });
                }
            }

            return result;
        }

        private static string ReadThemeName(string themeDir)
        {
            try
            {
                var iniPath = Directory.Exists(themeDir)
                    ? Directory.GetFiles(themeDir).FirstOrDefault(f =>
                        string.Equals(Path.GetFileName(f), "theme.ini", StringComparison.OrdinalIgnoreCase))
                    : null;
                if (iniPath == null)
                    return null;

                foreach (var line in File.ReadAllLines(iniPath))
                {
                    var eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;
                    if (line.Substring(0, eq).Trim().Equals("name", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = line.Substring(eq + 1).Trim();
                        return value.Length > 0 ? value : null;
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        public MenuOptionsConfig Load()
        {
            var cfg = new MenuOptionsConfig();

            var sections = ReadIniSections();
            if (sections.TryGetValue("DEFAULTS", out var defaults))
            {
                if (defaults.TryGetValue("style", out var styleValue) && TryParseStyle(styleValue, out var style))
                {
                    cfg.ForceStyleTheme = true;
                    cfg.Style = style;
                    cfg.ThemeId = defaults.TryGetValue("theme", out var theme)
                        ? theme.Trim().ToUpperInvariant()
                        : DefaultThemeId(style);

                    if (!GetThemesForStyle(style).Any(t => t.Id == cfg.ThemeId))
                        cfg.ThemeId = DefaultThemeId(style);
                }
            }

            if (sections.TryGetValue("BGM", out var bgm))
            {
                bgm.TryGetValue("source_file", out var src);
                bgm.TryGetValue("converted", out var conv);
                cfg.BgmSourceFile = src;
                cfg.BgmConvertedDate = conv;
            }

            cfg.BgmFileExists = File.Exists(BgmPath);
            cfg.BgmEnabled = cfg.BgmFileExists;
            return cfg;
        }

        public async Task ApplyStyleThemeAsync(bool force, MenuStyle style, string themeId)
        {
            var cfg = Load();
            cfg.ForceStyleTheme = force;
            cfg.Style = style;
            cfg.ThemeId = themeId;
            await SaveAsync(cfg, null);
        }

        public async Task<BgmConversionResult> ApplyBgmAsync(bool enabled, string newBgmSourcePath)
        {
            var cfg = Load();
            cfg.BgmEnabled = enabled;
            return await SaveAsync(cfg, newBgmSourcePath);
        }

        /// <summary>
        /// Applies the config to disk. When newBgmSourcePath is set the audio is
        /// converted and BGM.ADP replaced. Returns the conversion result, or null
        /// when no conversion ran.
        /// </summary>
        public async Task<BgmConversionResult> SaveAsync(MenuOptionsConfig cfg, string newBgmSourcePath)
        {
            Directory.CreateDirectory(menuDataPath);

            BgmConversionResult result = null;
            if (cfg.BgmEnabled)
            {
                if (!string.IsNullOrEmpty(newBgmSourcePath))
                {
                    result = await BgmConverter.ConvertAsync(newBgmSourcePath, BgmPath);
                    cfg.BgmSourceFile = Path.GetFileName(newBgmSourcePath);
                    cfg.BgmConvertedDate = DateTime.Now.ToString("yyyy-MM-dd");
                }
                else if (!File.Exists(BgmPath))
                {
                    throw new InvalidOperationException("Background music is enabled but no music file was selected.");
                }
            }
            else
            {
                if (File.Exists(BgmPath))
                    File.Delete(BgmPath);
                cfg.BgmSourceFile = null;
                cfg.BgmConvertedDate = null;
            }

            cfg.BgmFileExists = File.Exists(BgmPath);
            WriteIni(cfg);
            return result;
        }

        private Dictionary<string, Dictionary<string, string>> ReadIniSections()
        {
            var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            string[] lines;
            try
            {
                if (!File.Exists(DefaultsIniPath))
                    return sections;
                lines = File.ReadAllLines(DefaultsIniPath);
            }
            catch
            {
                // an unreadable file behaves like a missing one
                return sections;
            }

            Dictionary<string, string> current = null;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#"))
                    continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    var name = line.Substring(1, line.Length - 2).Trim();
                    if (!sections.TryGetValue(name, out current))
                    {
                        current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        sections[name] = current;
                    }
                    continue;
                }
                var eq = line.IndexOf('=');
                if (eq > 0 && current != null)
                    current[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return sections;
        }

        private void WriteIni(MenuOptionsConfig cfg)
        {
            // sections other than ours survive a rewrite untouched
            var foreign = ReadForeignSectionText();

            var sb = new StringBuilder();
            bool hasContent = false;

            if (cfg.ForceStyleTheme)
            {
                sb.AppendLine("[DEFAULTS]");
                sb.AppendLine($"style={StyleToIniValue(cfg.Style)}");
                sb.AppendLine($"theme={cfg.ThemeId}");
                hasContent = true;
            }

            if (cfg.BgmFileExists && cfg.BgmEnabled)
            {
                if (hasContent)
                    sb.AppendLine();
                sb.AppendLine("[BGM]");
                if (!string.IsNullOrEmpty(cfg.BgmSourceFile))
                    sb.AppendLine($"source_file={cfg.BgmSourceFile}");
                if (!string.IsNullOrEmpty(cfg.BgmConvertedDate))
                    sb.AppendLine($"converted={cfg.BgmConvertedDate}");
                hasContent = true;
            }

            if (foreign.Length > 0)
            {
                if (hasContent)
                    sb.AppendLine();
                sb.Append(foreign);
                hasContent = true;
            }

            if (hasContent)
                File.WriteAllText(DefaultsIniPath, sb.ToString());
            else if (File.Exists(DefaultsIniPath))
                File.Delete(DefaultsIniPath);
        }

        private string ReadForeignSectionText()
        {
            if (!File.Exists(DefaultsIniPath))
                return string.Empty;

            var sb = new StringBuilder();
            bool inForeign = false;
            foreach (var raw in File.ReadAllLines(DefaultsIniPath))
            {
                var line = raw.Trim();
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    var name = line.Substring(1, line.Length - 2).Trim();
                    inForeign = !name.Equals("DEFAULTS", StringComparison.OrdinalIgnoreCase)
                             && !name.Equals("BGM", StringComparison.OrdinalIgnoreCase);
                }
                if (inForeign)
                    sb.AppendLine(raw);
            }
            return sb.ToString();
        }
    }
}
