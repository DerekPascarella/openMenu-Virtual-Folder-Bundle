using System.IO;
using System.Threading.Tasks;

namespace GDMENUCardManager.Core
{
    /// <summary>
    /// Describes the first-image rule when an archive contains multiple disc images.
    /// </summary>
    public static class ArchiveSelectionWarning
    {
        /// <summary>
        /// Returns the warning text, or null when no warning is needed.
        /// </summary>
        public static string BuildMessage(GdItem item)
        {
            if (item?.ArchiveImageEntries == null ||
                item.ArchiveImageEntries.Count <= 1 ||
                item.SelectedArchiveEntry == null)
                return null;

            string archiveName = Path.GetFileName(item.ImageFile);
            return $"Archive \"{archiveName}\" contains {item.ArchiveImageEntries.Count} supported disc images. " +
                "GD MENU Card Manager supports one disc image per archive. " +
                $"Only the first image found, \"{item.SelectedArchiveEntry.FullName}\", will be added.";
        }

        /// <summary>
        /// Shows the warning when the item records more than one supported image.
        /// </summary>
        public static async ValueTask ShowIfNeededAsync(GdItem item)
        {
            string message = BuildMessage(item);
            if (message == null)
                return;

            await Helper.DependencyManager.ShowWarningDialog("Information", message);
        }
    }
}
