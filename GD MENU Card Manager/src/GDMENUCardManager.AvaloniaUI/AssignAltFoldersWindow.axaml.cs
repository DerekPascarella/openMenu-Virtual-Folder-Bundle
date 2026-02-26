using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MessageBox.Avalonia;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using GDMENUCardManager.Core;

namespace GDMENUCardManager
{
    public class AltFolderEntryAvalonia : INotifyPropertyChanged
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

        private string _label = "Folder Path 1:";
        public string Label
        {
            get => _label;
            set
            {
                if (_label != value)
                {
                    _label = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class AssignAltFoldersWindow : Window
    {
        private readonly string _primaryFolder;
        private readonly ObservableCollection<AltFolderEntryAvalonia> _altFolders = new ObservableCollection<AltFolderEntryAvalonia>();

        private ItemsControl _listControl;
        private Button _addButton;

        public AssignAltFoldersWindow()
        {
            InitializeComponent();
        }

        public AssignAltFoldersWindow(GdItem item, IEnumerable<string> knownFolders) : this()
        {
            _primaryFolder = item.Folder;

            var headerLabel = this.FindControl<TextBlock>("HeaderLabel");
            if (headerLabel != null)
                headerLabel.Text = "Assign additional folder paths for selected item";

            _listControl = this.FindControl<ItemsControl>("AltFoldersList");
            _addButton = this.FindControl<Button>("AddButton");

            for (int i = 0; i < item.AlternativeFolders.Count; i++)
            {
                _altFolders.Add(new AltFolderEntryAvalonia
                {
                    FolderPath = item.AlternativeFolders[i],
                    Label = $"Folder Path {i + 1}:"
                });
            }

            if (_listControl != null)
                _listControl.Items = _altFolders;

            UpdateAddButton();
        }

        public List<string> GetAltFolders()
        {
            return _altFolders
                .Select(e => e.FolderPath?.Trim() ?? string.Empty)
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (_altFolders.Count >= 5) return;
            _altFolders.Add(new AltFolderEntryAvalonia
            {
                Label = $"Folder Path {_altFolders.Count + 1}:"
            });
            UpdateAddButton();
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AltFolderEntryAvalonia entry)
            {
                _altFolders.Remove(entry);
                ReindexEntries();
                UpdateAddButton();
            }
        }

        private async void FolderPath_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.DataContext is AltFolderEntryAvalonia entry)
            {
                var path = entry.FolderPath?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(path)) return;

                bool isDuplicate = false;

                if (!string.IsNullOrEmpty(_primaryFolder) && path == _primaryFolder)
                    isDuplicate = true;

                if (!isDuplicate)
                {
                    foreach (var other in _altFolders)
                    {
                        if (other != entry && (other.FolderPath?.Trim() ?? string.Empty) == path)
                        {
                            isDuplicate = true;
                            break;
                        }
                    }
                }

                if (isDuplicate)
                {
                    await MessageBoxManager.GetMessageBoxStandardWindow("Duplicate Folder Path",
                        "This folder path is already assigned to this disc image.",
                        icon: MessageBox.Avalonia.Enums.Icon.Info).ShowDialog(this);
                    entry.FolderPath = string.Empty;
                }
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Close(true);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close(false);
        }

        private void ReindexEntries()
        {
            for (int i = 0; i < _altFolders.Count; i++)
                _altFolders[i].Label = $"Folder Path {i + 1}:";
        }

        private void UpdateAddButton()
        {
            if (_addButton != null)
                _addButton.IsEnabled = _altFolders.Count < 5;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
