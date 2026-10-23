using System.Configuration;

namespace GDMENUCardManager
{
    internal static class RetiredSettings
    {
        // Keys older versions shipped in GDMENUCardManager.dll.config.
        private static readonly string[] Keys = { "ProductIdMaxLength", "CharLimit" };

        internal static void Remove(Configuration config)
        {
            foreach (var key in Keys)
                config.AppSettings.Settings.Remove(key);
        }
    }
}
