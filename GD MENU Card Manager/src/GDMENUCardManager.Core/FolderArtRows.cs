using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace GDMENUCardManager.Core
{
    public class FolderArtRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public string Path { get; }
        public int Depth { get; }

        // Leaf folder name, indented by depth.
        public string Display { get; }

        private bool _HasArt;
        public bool HasArt
        {
            get { return _HasArt; }
            set { _HasArt = value; RaisePropertyChanged(); }
        }

        public FolderArtRow(string path, bool hasArt)
        {
            Path = path;
            Depth = path?.Count(c => c == '\\') ?? 0;

            var leaf = path ?? string.Empty;
            var lastSep = leaf.LastIndexOf('\\');
            if (lastSep >= 0)
                leaf = leaf.Substring(lastSep + 1);

            Display = Depth == 0 ? leaf : "  " + new string(' ', (Depth - 1) * 4) + "└─ " + leaf;
            _HasArt = hasArt;
        }

        private void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Row model for artwork entries whose folder no longer exists.
    /// </summary>
    public class FolderArtOrphanRow
    {
        public string Key { get; set; }

        // Original folder path from FOLDRART.MAP, null when the map entry was lost
        public string Path { get; set; }

        public string Display => string.IsNullOrEmpty(Path) ? Key : Path;
    }
}
