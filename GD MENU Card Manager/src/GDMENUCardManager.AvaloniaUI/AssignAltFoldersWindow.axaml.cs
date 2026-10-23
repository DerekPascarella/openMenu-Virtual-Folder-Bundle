using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MsBox.Avalonia;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
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
        private Button _cancelButton;
        private bool _duplicateMessageOpen;
        private bool _closing;
        private ComboBox _editingCombo;
        private string _textBeforeEdit;

        public IEnumerable<string> KnownFolders { get; }

        public AssignAltFoldersWindow()
        {
            InitializeComponent();
        }

        public AssignAltFoldersWindow(GdItem item, IEnumerable<string> knownFolders) : this()
        {
            _primaryFolder = item.Folder;
            KnownFolders = knownFolders;

            var headerLabel = this.FindControl<TextBlock>("HeaderLabel");
            if (headerLabel != null)
                headerLabel.Text = "Assign additional folder paths for selected item";

            _listControl = this.FindControl<ItemsControl>("AltFoldersList");
            _addButton = this.FindControl<Button>("AddButton");
            _cancelButton = this.FindControl<Button>("CancelButton");

            for (int i = 0; i < item.AlternativeFolders.Count; i++)
            {
                _altFolders.Add(new AltFolderEntryAvalonia
                {
                    FolderPath = item.AlternativeFolders[i],
                    Label = $"Folder Path {i + 1}:"
                });
            }

            if (_listControl != null)
                _listControl.ItemsSource = _altFolders;

            UpdateAddButton();
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            Dispatcher.UIThread.Post(() =>
            {
                var first = _listControl?.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault();
                ((Control)first ?? _addButton)?.Focus(NavigationMethod.Tab);
            }, DispatcherPriority.Loaded);
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

        private void FolderPath_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox != _editingCombo)
            {
                _editingCombo = comboBox;
                _textBeforeEdit = comboBox.Text;
            }
        }

        // Enter accepts the typed text and Escape undoes it. Escape with nothing to undo falls through to Cancel.
        private async void FolderPath_KeyDown(object sender, KeyEventArgs e)
        {
            if (!(sender is ComboBox comboBox) || comboBox.IsDropDownOpen || !(comboBox.DataContext is AltFolderEntryAvalonia entry))
                return;

            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (IsDuplicate(entry))
                    await ClearDuplicate(entry);
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

        private async void FolderPath_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox leftCombo && !leftCombo.IsDropDownOpen && !leftCombo.IsKeyboardFocusWithin)
                _editingCombo = null;

            // Cancel, Escape and the title-bar X don't save, and OK has already checked.
            if (_closing || FocusManager?.GetFocusedElement() == _cancelButton)
                return;

            if (sender is ComboBox comboBox && comboBox.DataContext is AltFolderEntryAvalonia entry)
            {
                // Opening or clicking the dropdown moves focus without leaving the ComboBox.
                if (comboBox.IsDropDownOpen || comboBox.IsKeyboardFocusWithin)
                    return;

                // This row's X removes it, so there's nothing left to check.
                if (FocusManager?.GetFocusedElement() is Button removeButton && removeButton.DataContext == entry)
                    return;

                if (IsDuplicate(entry))
                    await ClearDuplicate(entry);
            }
        }

        private async void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var duplicate = _altFolders.FirstOrDefault(IsDuplicate);
            if (duplicate != null)
            {
                await ClearDuplicate(duplicate);
                return;
            }

            Close(true);
        }

        private bool IsDuplicate(AltFolderEntryAvalonia entry)
        {
            var path = GdItem.CleanFolderPath(entry.FolderPath) ?? string.Empty;
            if (string.IsNullOrEmpty(path)) return false;

            if (!string.IsNullOrEmpty(_primaryFolder) && path == _primaryFolder)
                return true;

            return _altFolders.Any(other => other != entry && GdItem.CleanFolderPath(other.FolderPath) == path);
        }

        private async Task ClearDuplicate(AltFolderEntryAvalonia entry)
        {
            // LostFocus and OK both find the same duplicate while its message is still open.
            if (_duplicateMessageOpen)
                return;

            _duplicateMessageOpen = true;
            await MessageBoxManager.GetMessageBoxStandard("Information",
                "This folder path is already assigned to this disc image.",
                icon: MsBox.Avalonia.Enums.Icon.None, windowStartupLocation: WindowStartupLocation.CenterOwner).ShowWindowDialogAsync(this);
            entry.FolderPath = string.Empty;
            _duplicateMessageOpen = false;
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            _closing = true;
            base.OnClosing(e);
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
