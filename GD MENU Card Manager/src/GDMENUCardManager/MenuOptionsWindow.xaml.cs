using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using ByteSizeLib;
using GDMENUCardManager.Core.MenuOptions;
using Microsoft.Win32;

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

        public bool IsStyleFolders
        {
            get => _style == MenuStyle.Folders;
            set { if (value) SetStyle(MenuStyle.Folders); }
        }

        public bool IsStyleScroll
        {
            get => _style == MenuStyle.Scroll;
            set { if (value) SetStyle(MenuStyle.Scroll); }
        }

        public bool IsStyleGrid3
        {
            get => _style == MenuStyle.Grid3;
            set { if (value) SetStyle(MenuStyle.Grid3); }
        }

        public bool IsStyleLineDesc
        {
            get => _style == MenuStyle.LineDesc;
            set { if (value) SetStyle(MenuStyle.LineDesc); }
        }

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

        public event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public MenuOptionsWindow(GDMENUCardManager.Core.Manager manager)
        {
            InitializeComponent();

            _menuOptions = manager.CreateMenuOptionsManager();
            _config = _menuOptions.Load();

            _forceStyleTheme = _config.ForceStyleTheme;
            _style = _config.Style;
            _bgmEnabled = _config.BgmEnabled;
            RefreshBgmState(_config);

            RefreshThemes(_config.ThemeId);
            DataContext = this;
        }

        private void SetStyle(MenuStyle style)
        {
            if (_style == style)
                return;
            _style = style;
            RaisePropertyChanged(nameof(IsStyleFolders));
            RaisePropertyChanged(nameof(IsStyleScroll));
            RaisePropertyChanged(nameof(IsStyleGrid3));
            RaisePropertyChanged(nameof(IsStyleLineDesc));
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

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var fileDialog = new OpenFileDialog
            {
                Title = "Select Music File",
                Filter = "Audio Files|*.wav;*.mp3;*.ogg;*.flac|All Files|*.*"
            };
            if (fileDialog.ShowDialog(this) == true)
            {
                SelectedSourcePath = fileDialog.FileName;
            }
        }

        private void RefreshBgmState(GDMENUCardManager.Core.MenuOptions.MenuOptionsConfig cfg)
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

                MessageBox.Show(this,
                    "Default style and theme settings applied.\n\nChanges take effect after clicking \"Save Changes\" in the main window.",
                    "Menu Options", MessageBoxButton.OK, MessageBoxImage.None);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.None);
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
                progressWindow.Owner = this;
                progressWindow.Title = "Converting Music";
                progressWindow.TotalItems = 1;
                progressWindow.TextContent = $"Converting {Path.GetFileName(newSource)} to Dreamcast ADPCM...";
                progressWindow.Show();
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

                MessageBox.Show(this, message, "Menu Options", MessageBoxButton.OK, MessageBoxImage.None);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.None);
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
