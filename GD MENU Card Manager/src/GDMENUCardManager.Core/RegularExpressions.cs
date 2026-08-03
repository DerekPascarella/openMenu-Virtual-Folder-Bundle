using System.Text.RegularExpressions;

namespace GDMENUCardManager.Core
{
    public static class RegularExpressions
    {
        public static readonly Regex GdiRegexp = new Regex(@"\d+ \d+ \d+ \d+ (track\d+.\w+) \d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        /// <summary>
        /// Matches the version and year tail of a TOSEC-style filename (e.g., " v1.001 (1999)").
        /// </summary>
        public static readonly Regex TosecnNameRegexp = new Regex(@" (V\d\.\d{3}) (\(\d{4}\))", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }
}
