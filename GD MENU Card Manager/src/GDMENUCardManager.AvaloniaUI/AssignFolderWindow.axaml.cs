using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GDMENUCardManager
{
    public class AssignFolderWindow : Window, INotifyPropertyChanged
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

        private IEnumerable<string> _knownFolders;

        public IEnumerable<string> KnownFolders
        {
            get => _knownFolders;
            set
            {
                if (_knownFolders != value)
                {
                    _knownFolders = value;
                    OnPropertyChanged();
                }
            }
        }

        public AssignFolderWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        public AssignFolderWindow(int selectedCount, IEnumerable<string> knownFolders) : this()
        {
            SelectionInfo = $"Assign folder path to {selectedCount} selected item{(selectedCount == 1 ? "" : "s")}";
            KnownFolders = knownFolders;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            this.FindControl<ComboBox>("FolderInput")?.Focus(NavigationMethod.Tab);
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
        private void FolderInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (!(sender is ComboBox comboBox) || comboBox.IsDropDownOpen)
                return;

            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                _textBeforeEdit = comboBox.Text;
                // Autocomplete leaves the filled-in tail selected.
                if (e.Source is TextBox textBox)
                {
                    textBox.ClearSelection();
                    textBox.CaretIndex = textBox.Text?.Length ?? 0;
                }
            }
            else if (e.Key == Key.Escape && comboBox.Text != _textBeforeEdit)
            {
                e.Handled = true;
                comboBox.SetCurrentValue(ComboBox.TextProperty, _textBeforeEdit);
            }
        }

        private void FolderInput_LostFocus(object sender, RoutedEventArgs e)
        {
            // Opening or clicking the dropdown moves focus without leaving the ComboBox.
            if (sender is ComboBox comboBox && !comboBox.IsDropDownOpen && !comboBox.IsKeyboardFocusWithin)
                _editingCombo = null;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Close(true);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close(false);
        }

        public new event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
