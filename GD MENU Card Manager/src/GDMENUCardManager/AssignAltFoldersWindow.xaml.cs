using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GDMENUCardManager.Core;

namespace GDMENUCardManager
{
    public partial class AssignAltFoldersWindow : Window, INotifyPropertyChanged
    {
        private readonly string _primaryFolder;
        private bool _duplicateMessageOpen;
        private bool _closing;
        private ComboBox _editingCombo;
        private string _textBeforeEdit;

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
            Loaded += (s, e) =>
            {
                var row = AltFoldersList.ItemContainerGenerator.ContainerFromIndex(0) as ContentPresenter;
                var first = row?.ContentTemplate.FindName("FolderPathCombo", row) as UIElement;
                (first ?? AddButton).Focus();
            };
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

        private void FolderPath_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox != _editingCombo)
            {
                _editingCombo = comboBox;
                _textBeforeEdit = comboBox.Text;
            }
        }

        // Enter accepts the typed text and Escape undoes it. Escape with nothing to undo falls through to Cancel.
        private void FolderPath_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!(sender is ComboBox comboBox) || comboBox.IsDropDownOpen || !(comboBox.DataContext is AltFolderEntry entry))
                return;

            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                comboBox.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
                if (IsDuplicate(entry))
                    ClearDuplicate(entry);
                _textBeforeEdit = comboBox.Text;
                // Autocomplete leaves the filled-in tail selected.
                if (e.OriginalSource is TextBox textBox)
                    textBox.CaretIndex = textBox.Text.Length;
            }
            else if (e.Key == Key.Escape && comboBox.Text != _textBeforeEdit)
            {
                e.Handled = true;
                comboBox.Text = _textBeforeEdit;
            }
        }

        private void FolderPath_LostFocus(object sender, RoutedEventArgs e)
        {
            _editingCombo = null;

            // Cancel, Escape and the title-bar X don't save, and OK has already checked.
            if (_closing || Keyboard.FocusedElement == CancelButton)
                return;

            if (sender is FrameworkElement element && element.DataContext is AltFolderEntry entry)
            {
                // This row's X removes it, so there's nothing left to check.
                if (Keyboard.FocusedElement is Button removeButton && removeButton.DataContext == entry)
                    return;

                if (IsDuplicate(entry))
                    ClearDuplicate(entry);
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var duplicate = AltFolders.FirstOrDefault(IsDuplicate);
            if (duplicate != null)
            {
                ClearDuplicate(duplicate);
                return;
            }

            DialogResult = true;
        }

        private bool IsDuplicate(AltFolderEntry entry)
        {
            var path = GdItem.CleanFolderPath(entry.FolderPath) ?? string.Empty;
            if (string.IsNullOrEmpty(path)) return false;

            if (!string.IsNullOrEmpty(_primaryFolder) && path == _primaryFolder)
                return true;

            return AltFolders.Any(other => other != entry && GdItem.CleanFolderPath(other.FolderPath) == path);
        }

        private void ClearDuplicate(AltFolderEntry entry)
        {
            // LostFocus and OK both find the same duplicate while its message is still open.
            if (_duplicateMessageOpen)
                return;

            _duplicateMessageOpen = true;
            MessageBox.Show(this, "This folder path is already assigned to this disc image.",
                "Information", MessageBoxButton.OK, MessageBoxImage.None);
            entry.FolderPath = string.Empty;
            _duplicateMessageOpen = false;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _closing = true;
            base.OnClosing(e);
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
