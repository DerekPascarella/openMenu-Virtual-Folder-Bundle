using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using GDMENUCardManager.Core;

namespace GDMENUCardManager
{
    public partial class AssignAltFoldersWindow : Window, INotifyPropertyChanged
    {
        private readonly string _primaryFolder;

        public ObservableCollection<AltFolderEntry> AltFolders { get; } = new ObservableCollection<AltFolderEntry>();
        public IEnumerable<string> KnownFolders { get; set; }
        public string HeaderText { get; set; }

        private bool _canAddMore = true;
        public bool CanAddMore
        {
            get => _canAddMore;
            set { _canAddMore = value; OnPropertyChanged(); }
        }

        public AssignAltFoldersWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        public AssignAltFoldersWindow(GdItem item, IEnumerable<string> knownFolders) : this()
        {
            _primaryFolder = item.Folder;
            KnownFolders = knownFolders;
            HeaderText = "Assign additional folder paths for selected item";

            for (int i = 0; i < item.AlternativeFolders.Count; i++)
            {
                AltFolders.Add(new AltFolderEntry
                {
                    FolderPath = item.AlternativeFolders[i],
                    Index = i + 1
                });
            }

            UpdateCanAddMore();
        }

        public List<string> GetAltFolders()
        {
            return AltFolders
                .Select(e => e.FolderPath?.Trim() ?? string.Empty)
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (AltFolders.Count >= 5) return;
            AltFolders.Add(new AltFolderEntry { Index = AltFolders.Count + 1 });
            UpdateCanAddMore();
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is AltFolderEntry entry)
            {
                AltFolders.Remove(entry);
                ReindexEntries();
                UpdateCanAddMore();
            }
        }

        private void FolderPath_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is AltFolderEntry entry)
            {
                var path = entry.FolderPath?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(path)) return;

                // check against primary folder
                if (!string.IsNullOrEmpty(_primaryFolder) && path == _primaryFolder)
                {
                    MessageBox.Show("This folder path is already assigned to this disc image.",
                        "Duplicate Folder Path", MessageBoxButton.OK, MessageBoxImage.Information);
                    entry.FolderPath = string.Empty;
                    return;
                }

                // check against other alt folder entries
                foreach (var other in AltFolders)
                {
                    if (other != entry && (other.FolderPath?.Trim() ?? string.Empty) == path)
                    {
                        MessageBox.Show("This folder path is already assigned to this disc image.",
                            "Duplicate Folder Path", MessageBoxButton.OK, MessageBoxImage.Information);
                        entry.FolderPath = string.Empty;
                        return;
                    }
                }
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void ReindexEntries()
        {
            for (int i = 0; i < AltFolders.Count; i++)
                AltFolders[i].Index = i + 1;
        }

        private void UpdateCanAddMore()
        {
            CanAddMore = AltFolders.Count < 5;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class AltFolderEntry : INotifyPropertyChanged
    {
        private string _folderPath = string.Empty;
        public string FolderPath
        {
            get => _folderPath;
            set
            {
                if (_folderPath != value)
                {
                    _folderPath = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _index;
        public int Index
        {
            get => _index;
            set
            {
                if (_index != value)
                {
                    _index = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
