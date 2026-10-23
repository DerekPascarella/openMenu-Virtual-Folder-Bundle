using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GDMENUCardManager
{
    public partial class AssignFolderWindow : Window, INotifyPropertyChanged
    {
        private string _folderPath = string.Empty;
        private string _selectionInfo = string.Empty;
        private ComboBox _editingCombo;
        private string _textBeforeEdit;

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

        public string SelectionInfo
        {
            get => _selectionInfo;
            set
            {
                if (_selectionInfo != value)
                {
                    _selectionInfo = value;
                    OnPropertyChanged();
                }
            }
        }

        public IEnumerable<string> KnownFolders { get; set; }

        public AssignFolderWindow()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += (s, e) => FolderInput.Focus();
        }

        public AssignFolderWindow(int selectedCount, IEnumerable<string> knownFolders) : this()
        {
            SelectionInfo = $"Assign folder path to {selectedCount} selected item{(selectedCount == 1 ? "" : "s")}";
            KnownFolders = knownFolders;
        }

        private void FolderInput_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox != _editingCombo)
            {
                _editingCombo = comboBox;
                _textBeforeEdit = comboBox.Text;
            }
        }

        // Enter accepts the typed text and Escape undoes it. Escape with nothing to undo falls through to Cancel.
        private void FolderInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!(sender is ComboBox comboBox) || comboBox.IsDropDownOpen)
                return;

            if (e.Key == Key.Enter)
            {
                e.Handled = true;
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

        private void FolderInput_LostFocus(object sender, RoutedEventArgs e)
        {
            _editingCombo = null;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
