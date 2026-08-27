using Avalonia.Platform.Storage;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ByteSizeLib;
using GDMENUCardManager.Core.MenuOptions;
using MsBox.Avalonia;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace GDMENUCardManager
{
    public partial class MenuOptionsWindow : Window, INotifyPropertyChanged
    {
        private readonly MenuOptionsManager _menuOptions;
        private readonly MenuOptionsConfig _config;

        public ObservableCollection<ThemeEntry> Themes { get; } = new ObservableCollection<ThemeEntry>();

        private ThemeEntry _selectedTheme;
        public ThemeEntry SelectedTheme
        {
            get => _selectedTheme;
            set { _selectedTheme = value; RaisePropertyChanged(); }
        }

        private bool _forceStyleTheme;
        public bool ForceStyleTheme
        {
            get => _forceStyleTheme;
            set { _forceStyleTheme = value; RaisePropertyChanged(); }
        }

        private MenuStyle _style = MenuStyle.Folders;

        // Manage radio state in code, the group unchecking clobbers the IsChecked binding.
        private RadioButton _radioFolders;
        private RadioButton _radioScroll;
        private RadioButton _radioGrid3;
        private RadioButton _radioLineDesc;

        private bool _bgmEnabled;
        public bool BgmEnabled
        {
            get => _bgmEnabled;
            set
            {
                _bgmEnabled = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(CanApplyBgm));
                RaisePropertyChanged(nameof(ShowBgmHint));
            }
        }

        private string _selectedSourcePath;
        public string SelectedSourcePath
        {
            get => _selectedSourcePath;
            set
            {
                _selectedSourcePath = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(SelectedSourceDisplay));
                RaisePropertyChanged(nameof(CanApplyBgm));
                RaisePropertyChanged(nameof(ShowBgmHint));
            }
        }

        public string SelectedSourceDisplay =>
            string.IsNullOrEmpty(SelectedSourcePath) ? "(no new file selected)" : SelectedSourcePath;

        private bool _hasExistingBgm;
        public bool HasExistingBgm
        {
            get => _hasExistingBgm;
            private set { _hasExistingBgm = value; RaisePropertyChanged(); }
        }

        private string _bgmCurrentText;
        public string BgmCurrentText
        {
            get => _bgmCurrentText;
            private set { _bgmCurrentText = value; RaisePropertyChanged(); }
        }

        public bool CanApplyBgm => !BgmEnabled || HasExistingBgm || !string.IsNullOrEmpty(SelectedSourcePath);

        public bool ShowBgmHint => !CanApplyBgm;

        public new event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public MenuOptionsWindow()
        {
            InitializeComponent();
        }

        public MenuOptionsWindow(GDMENUCardManager.Core.Manager manager) : this()
        {
            _radioFolders = this.FindControl<RadioButton>("RadioStyleFolders");
            _radioScroll = this.FindControl<RadioButton>("RadioStyleScroll");
            _radioGrid3 = this.FindControl<RadioButton>("RadioStyleGrid3");
            _radioLineDesc = this.FindControl<RadioButton>("RadioStyleLineDesc");

            _menuOptions = manager.CreateMenuOptionsManager();
            _config = _menuOptions.Load();

            _forceStyleTheme = _config.ForceStyleTheme;
            _style = _config.Style;
            _bgmEnabled = _config.BgmEnabled;
            UpdateStyleRadios();
            RefreshBgmState(_config);

            RefreshThemes(_config.ThemeId);
            DataContext = this;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void StyleRadio_Click(object sender, RoutedEventArgs e)
        {
            if (sender == _radioScroll)
                SetStyle(MenuStyle.Scroll);
            else if (sender == _radioGrid3)
                SetStyle(MenuStyle.Grid3);
            else if (sender == _radioLineDesc)
                SetStyle(MenuStyle.LineDesc);
            else
                SetStyle(MenuStyle.Folders);
            UpdateStyleRadios();
        }

        private void UpdateStyleRadios()
        {
            _radioFolders.IsChecked = _style == MenuStyle.Folders;
            _radioScroll.IsChecked = _style == MenuStyle.Scroll;
            _radioGrid3.IsChecked = _style == MenuStyle.Grid3;
            _radioLineDesc.IsChecked = _style == MenuStyle.LineDesc;
        }

        private void SetStyle(MenuStyle style)
        {
            if (_style == style)
                return;
            _style = style;
            RefreshThemes(null);
        }

        private void RefreshThemes(string preferredThemeId)
        {
            var themes = _menuOptions.GetThemesForStyle(_style);
            Themes.Clear();
            foreach (var t in themes)
                Themes.Add(t);

            SelectedTheme = Themes.FirstOrDefault(t => t.Id == preferredThemeId) ?? Themes.FirstOrDefault();
        }

        private async void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var pickedFiles = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Music File",
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new FilePickerFileType("Audio Files")
                    {
                        Patterns = new List<string> { "*.wav", "*.mp3", "*.ogg", "*.flac" }
                    },
                    new FilePickerFileType("All Files")
                    {
                        // Use "*.*" here, a plain "*" ends up matching nothing on macOS.
                        Patterns = new List<string> { "*.*" },
                        AppleUniformTypeIdentifiers = new List<string> { "public.item" }
                    }
                }
            });
            var selectedFile = pickedFiles.Count > 0 ? pickedFiles[0].TryGetLocalPath() : null;
            if (selectedFile != null)
            {
                SelectedSourcePath = selectedFile;
            }
        }

        private void RefreshBgmState(MenuOptionsConfig cfg)
        {
            HasExistingBgm = cfg.BgmFileExists;
            BgmCurrentText = !cfg.BgmFileExists ? null
                : string.IsNullOrEmpty(cfg.BgmSourceFile)
                    ? "Current: BGM.ADP"
                    : $"Current: {cfg.BgmSourceFile} (converted {cfg.BgmConvertedDate})";
            RaisePropertyChanged(nameof(CanApplyBgm));
            RaisePropertyChanged(nameof(ShowBgmHint));
        }

        private async void ApplyStyleButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                IsEnabled = false;
                await _menuOptions.ApplyStyleThemeAsync(ForceStyleTheme, _style,
                    SelectedTheme?.Id ?? MenuOptionsManager.DefaultThemeId(_style));

                await MessageBoxManager.GetMessageBoxStandard("Menu Options",
                    "Default style and theme settings applied.\n\nChanges take effect after clicking \"Save Changes\" in the main window.",
                    icon: MsBox.Avalonia.Enums.Icon.None, windowStartupLocation: WindowStartupLocation.CenterOwner).ShowWindowDialogAsync(this);
            }
            catch (Exception ex)
            {
                await MessageBoxManager.GetMessageBoxStandard("Error", ex.Message,
                    icon: MsBox.Avalonia.Enums.Icon.None, windowStartupLocation: WindowStartupLocation.CenterOwner).ShowWindowDialogAsync(this);
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private async void ApplyBgmButton_Click(object sender, RoutedEventArgs e)
        {
            var newSource = BgmEnabled ? SelectedSourcePath : null;

            ProgressWindow progressWindow = null;
            if (!string.IsNullOrEmpty(newSource))
            {
                progressWindow = new ProgressWindow();
                progressWindow.Title = "Converting Music";
                progressWindow.TotalItems = 1;
                progressWindow.TextContent = $"Converting {Path.GetFileName(newSource)} to Dreamcast ADPCM...";
                progressWindow.Show(this);
            }

            try
            {
                IsEnabled = false;
                var result = await _menuOptions.ApplyBgmAsync(BgmEnabled, newSource);

                if (progressWindow != null)
                {
                    progressWindow.AllowClose();
                    progressWindow.Close();
                    progressWindow = null;
                }

                RefreshBgmState(_menuOptions.Load());
                SelectedSourcePath = null;

                var message = "Background music setting applied.";
                if (result != null)
                {
                    var size = ByteSize.FromBytes(result.FileSize);
                    var mode = result.Channels == 2 ? "stereo" : "mono";
                    var duration = $"{(int)result.Duration.TotalMinutes}:{result.Duration.Seconds:00}";
                    message = $"Music track converted ({duration}, {mode}, {size:0.#}).";
                    if (result.Duration.TotalMinutes > 10)
                        message += "\n\nNote: tracks over 10 minutes make the menu disc image noticeably larger.";
                }
                message += "\n\nChanges take effect after clicking \"Save Changes\" in the main window.";

                await MessageBoxManager.GetMessageBoxStandard("Menu Options", message,
                    icon: MsBox.Avalonia.Enums.Icon.None, windowStartupLocation: WindowStartupLocation.CenterOwner).ShowWindowDialogAsync(this);
            }
            catch (Exception ex)
            {
                await MessageBoxManager.GetMessageBoxStandard("Error", ex.Message,
                    icon: MsBox.Avalonia.Enums.Icon.None, windowStartupLocation: WindowStartupLocation.CenterOwner).ShowWindowDialogAsync(this);
            }
            finally
            {
                IsEnabled = true;
                if (progressWindow != null)
                {
                    progressWindow.AllowClose();
                    progressWindow.Close();
                }
            }
        }

    }
}
