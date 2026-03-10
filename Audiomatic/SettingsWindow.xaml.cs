using Audiomatic.Services;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Audiomatic;

public sealed partial class SettingsWindow : Window
{
    private bool _suppressEvents = true;
    private readonly Action<BackdropSettings>? _onBackdropChanged;
    private readonly Action? _onLibraryChanged;
    private CancellationTokenSource? _scanCts;

    public SettingsWindow(SystemBackdrop? backdrop,
        Action<BackdropSettings>? onBackdropChanged = null,
        Action? onLibraryChanged = null)
    {
        _onBackdropChanged = onBackdropChanged;
        _onLibraryChanged = onLibraryChanged;

        this.InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(420, 550));
        SetTitleBar(CustomTitleBar);
        WindowShadow.Apply(this);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
        }

        if (backdrop != null)
            this.SystemBackdrop = backdrop;

        LoadSettings();
        LoadFolders();
        _suppressEvents = false;
    }

    private void LoadSettings()
    {
        var settings = SettingsManager.LoadBackdrop();
        switch (settings.Type)
        {
            case "mica": RadioMica.IsChecked = true; break;
            case "mica_alt": RadioMicaAlt.IsChecked = true; break;
            case "none": RadioNone.IsChecked = true; break;
            default: RadioAcrylic.IsChecked = true; break;
        }
    }

    private void LoadFolders()
    {
        FoldersList.Children.Clear();
        var folders = LibraryManager.GetFolders();
        foreach (var folder in folders)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var pathText = new TextBlock
            {
                Text = folder.Path,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(pathText, 0);

            var removeBtn = new Button
            {
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 2, 6, 2),
                Content = new FontIcon { Glyph = "\uE711", FontSize = 10 },
                Tag = folder.Id
            };
            removeBtn.Click += RemoveFolder_Click;
            Grid.SetColumn(removeBtn, 1);

            grid.Children.Add(pathText);
            grid.Children.Add(removeBtn);
            FoldersList.Children.Add(grid);
        }

        if (folders.Count == 0)
        {
            FoldersList.Children.Add(new TextBlock
            {
                Text = "No folders added yet.",
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
            });
        }
    }

    // ── Folder management ────────────────────────────────────

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary;
        picker.FileTypeFilter.Add("*");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder == null) return;

        var folderId = LibraryManager.AddFolder(folder.Path);
        LoadFolders();

        // Auto-scan the new folder
        ScanStatus.Text = "Scanning...";
        ScanProgress.Visibility = Visibility.Visible;
        ScanProgress.IsIndeterminate = true;

        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var progress = new Progress<(int scanned, int total)>(p =>
        {
            if (p.total > 0)
            {
                ScanProgress.IsIndeterminate = false;
                ScanProgress.Value = (double)p.scanned / p.total * 100;
            }
            ScanStatus.Text = $"Scanned {p.scanned}/{p.total} files...";
        });

        try
        {
            var added = await LibraryManager.ScanFolderAsync(folderId, folder.Path, progress, _scanCts.Token);
            ScanStatus.Text = $"Done. {added} tracks added.";
            _onLibraryChanged?.Invoke();
        }
        catch (OperationCanceledException)
        {
            ScanStatus.Text = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            ScanStatus.Text = $"Error: {ex.Message}";
        }
        finally
        {
            ScanProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is long folderId)
        {
            LibraryManager.RemoveFolder(folderId);
            LoadFolders();
            _onLibraryChanged?.Invoke();
        }
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        ScanStatus.Text = "Scanning all folders...";
        ScanProgress.Visibility = Visibility.Visible;
        ScanProgress.IsIndeterminate = true;
        ScanButton.IsEnabled = false;

        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var progress = new Progress<(int scanned, int total)>(p =>
        {
            if (p.total > 0)
            {
                ScanProgress.IsIndeterminate = false;
                ScanProgress.Value = (double)p.scanned / p.total * 100;
            }
            ScanStatus.Text = $"Scanned {p.scanned}/{p.total} files...";
        });

        try
        {
            var added = await LibraryManager.ScanAllFoldersAsync(progress, _scanCts.Token);
            ScanStatus.Text = $"Done. {added} new tracks found.";
            _onLibraryChanged?.Invoke();
        }
        catch (OperationCanceledException)
        {
            ScanStatus.Text = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            ScanStatus.Text = $"Error: {ex.Message}";
        }
        finally
        {
            ScanProgress.Visibility = Visibility.Collapsed;
            ScanButton.IsEnabled = true;
        }
    }

    // ── Appearance ───────────────────────────────────────────

    private void BackdropRadio_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (BackdropRadio.SelectedItem is not RadioButton rb || rb.Tag is not string type) return;

        var settings = new BackdropSettings(Type: type);
        SettingsManager.SaveBackdrop(settings);
        _onBackdropChanged?.Invoke(settings);

        // Apply to this window too
        SystemBackdrop = type switch
        {
            "mica" => new MicaBackdrop(),
            "mica_alt" => new MicaBackdrop { Kind = MicaKind.BaseAlt },
            "none" => null,
            _ => new DesktopAcrylicBackdrop()
        };
    }

    // ── Tab navigation ───────────────────────────────────────

    private void SettingsSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        FoldersPage.Visibility = sender.SelectedItem == SelectorFolders ? Visibility.Visible : Visibility.Collapsed;
        AppearancePage.Visibility = sender.SelectedItem == SelectorAppearance ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Window chrome ────────────────────────────────────────

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
