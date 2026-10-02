using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Input;
using Sleepy.Gui.Services;
using Sleepy.Gui.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Sleepy.Gui;

public sealed partial class MainPage : Page
{
    private bool _languageReady;
    private bool _scaleReady;

    /// <summary>The scales offered; 0 means follow the display.</summary>
    private static readonly double[] Scales = [0, 1.0, 1.25, 1.5, 1.75, 2.0, 2.5];

    private readonly List<double> _scaleChoices = [];

    public MainPage()
    {
        this.InitializeComponent();
        DataContext = ViewModel;
        SelectCurrentLanguage();
        FillScalePicker();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        PreviewHighlighter.Render(PreviewText, ViewModel.Preview);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Preview))
        {
            PreviewHighlighter.Render(PreviewText, ViewModel.Preview);
        }
    }

    public MainViewModel ViewModel { get; } = MainViewModel.Instance;

    /// <summary>Points the picker at the language in force, without reacting to it.</summary>
    private void SelectCurrentLanguage()
    {
        var current = Localizer.CurrentOverride;
        var index = Localizer.SupportedLanguages
            .Select((language, position) => (language, position))
            .FirstOrDefault(entry => string.Equals(entry.language, current, StringComparison.OrdinalIgnoreCase))
            .position;

        LanguagePicker.SelectedIndex = current.Length == 0 ? 0 : index + 1;
        _languageReady = true;
    }

    /// <summary>
    /// Lists the scales, adding the saved one if it was set by hand to something
    /// in between, and selects it without treating that as a change.
    /// </summary>
    private void FillScalePicker()
    {
        // Only the X11 host reads a scale override; Windows follows its own
        // display settings, so offering one there would be a control that does nothing.
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var saved = UserSettings.NormalizeUiScale(Settings.Current.UiScale);
        _scaleChoices.AddRange(Scales);
        if (!_scaleChoices.Contains(saved))
        {
            _scaleChoices.Add(saved);
            _scaleChoices.Sort();
        }

        foreach (var scale in _scaleChoices)
        {
            ScalePicker.Items.Add(new ComboBoxItem
            {
                Content = scale == 0
                    ? Localizer.Get("Scale_Auto")
                    : $"{Math.Round(scale * 100).ToString(System.Globalization.CultureInfo.CurrentCulture)} %",
            });
        }

        ScalePicker.SelectedIndex = _scaleChoices.IndexOf(saved);
        ScalePicker.Visibility = Visibility.Visible;
        _scaleReady = true;
    }

    private async void OnScaleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_scaleReady || ScalePicker.SelectedIndex < 0)
        {
            return;
        }

        var scale = _scaleChoices[ScalePicker.SelectedIndex];
        if (scale == UserSettings.NormalizeUiScale(Settings.Current.UiScale))
        {
            return;
        }

        Settings.Update(settings => settings with { UiScale = scale });

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Localizer.Get("Scale_RestartTitle"),
            Content = new TextBlock
            {
                Text = Localizer.Get("Scale_RestartMessage"),
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = Localizer.Get("Scale_RestartNow"),
            CloseButtonText = Localizer.Get("Scale_Later"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !RestartWithScale(scale))
        {
            ViewModel.SetError(Localizer.Get("Status_RestartFailed"));
        }
    }

    private static bool RestartWithScale(double scale)
    {
#if WINDOWS
        return false;
#else
        return Program.Restart(scale);
#endif
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_languageReady)
        {
            return;
        }

        var index = LanguagePicker.SelectedIndex;
        var language = index <= 0 ? string.Empty : Localizer.SupportedLanguages[index - 1];
        if (string.Equals(language, Localizer.CurrentOverride, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Localizer.SetLanguage(language);
        ViewModel.Relocalize();

        // x:Uid strings are resolved while the page is built, so rebuild it. The
        // view model is shared, so nothing the user loaded is lost.
        if (Frame is { } frame)
        {
            frame.Navigate(typeof(MainPage));
            frame.BackStack.Clear();
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        if (e.DragUIOverride is { } overlay)
        {
            overlay.Caption = "Convert this spec";
            overlay.IsCaptionVisible = true;
            overlay.IsContentVisible = true;
        }

        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => e.Handled = true;

    private async void OnDrop(object sender, DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                if (items.FirstOrDefault() is StorageFile file)
                {
                    await ViewModel.LoadAsync(file.Path);
                    return;
                }
            }

            if (e.DataView.Contains(StandardDataFormats.Text))
            {
                var text = await e.DataView.GetTextAsync();
                await ViewModel.LoadAsync(text);
                return;
            }

            ViewModel.SetError("That drop had nothing sleepy can read.");
        }
        catch (Exception exception)
        {
            ViewModel.SetError($"Could not read the dropped item: {exception.Message}");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void OnLoadClick(object sender, RoutedEventArgs e)
        => await ViewModel.LoadAsync(ViewModel.InputText);

    private async void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            await ViewModel.LoadAsync(ViewModel.InputText);
        }
    }

    private async void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".json");
            picker.FileTypeFilter.Add(".yaml");
            picker.FileTypeFilter.Add(".yml");
            InitializeWithWindow(picker);

            if (await picker.PickSingleFileAsync() is { } file)
            {
                await ViewModel.LoadAsync(file.Path);
            }
        }
        catch (Exception exception)
        {
            ViewModel.SetError($"File dialog unavailable ({exception.Message}). Paste a path instead.");
        }
    }

    private void OnApplyClick(object sender, RoutedEventArgs e) => ViewModel.Apply();

    /// <summary>Enter in an output field applies it, the way Enter loads a source.</summary>
    private void OnOutputFieldKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            ViewModel.Apply();
        }
    }

    /// <summary>
    /// Asks what to do about text that was typed but never applied, so an export
    /// cannot quietly disagree with the fields above it.
    /// </summary>
    private async Task<bool> ResolvePendingChangesAsync()
    {
        if (!ViewModel.HasPendingChanges)
        {
            return true;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Localizer.Get("Pending_Title"),
            Content = new TextBlock
            {
                Text = Localizer.Get("Pending_Message"),
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = Localizer.Get("Pending_ApplyAndContinue"),
            SecondaryButtonText = Localizer.Get("Pending_ContinueAnyway"),
            CloseButtonText = Localizer.Get("Pending_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        switch (await dialog.ShowAsync())
        {
            case ContentDialogResult.Primary:
                ViewModel.Apply();
                return true;
            case ContentDialogResult.Secondary:
                return true;
            default:
                return false;
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!await ResolvePendingChangesAsync())
        {
            return;
        }

        if (ViewModel.IsFolderFormat)
        {
            await SaveToFolderAsync();
            return;
        }

        try
        {
            var picker = new FileSavePicker
            {
                SuggestedFileName = ViewModel.SuggestedFileName,
                DefaultFileExtension = ".json",
            };
            picker.FileTypeChoices.Add(ViewModel.Format.DisplayName, [".json"]);
            InitializeWithWindow(picker);

            if (await picker.PickSaveFileAsync() is { } file)
            {
                await ViewModel.SaveAsync(file.Path);
            }
        }
        catch (Exception exception)
        {
            ViewModel.SetError($"Save dialog unavailable ({exception.Message}).");
        }
    }

    /// <summary>
    /// Bruno and other folder formats are a directory of files, so the save flow
    /// asks for a folder rather than a file name.
    /// </summary>
    private async Task SaveToFolderAsync()
    {
        try
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow(picker);

            if (await picker.PickSingleFolderAsync() is { } folder)
            {
                await ViewModel.SaveToFolderAsync(folder.Path);
            }
        }
        catch (Exception exception)
        {
            ViewModel.SetError(Localizer.Format("Status_SavePickerUnavailable", exception.Message));
        }
    }

    private async void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (!await ResolvePendingChangesAsync())
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(ViewModel.ExportJson);
        Clipboard.SetContent(package);
        ViewModel.SetStatus("Export copied to the clipboard.");
    }

    /// <summary>
    /// Shows the licence the app is distributed under, plus the notices of the
    /// components that ship with it. Embedding them here is what carries the
    /// notice into a redistributed binary.
    /// </summary>
    /// <summary>
    /// Shows the licence the app is distributed under, plus the notices of the
    /// components that ship with it. Embedding them here is what carries the
    /// notice into a redistributed binary.
    /// </summary>
    /// <summary>
    /// Shows the licence the app is distributed under, plus the components that
    /// ship with it. Embedding them is what carries the notice into a
    /// redistributed binary; laying them out here is what makes them readable.
    /// </summary>
    private async void OnAboutClick(object sender, RoutedEventArgs e)
    {
        var body = new StackPanel { Spacing = 16 };

        body.Children.Add(new TextBlock
        {
            Text = $"sleepy {SleepyInfo.BuildIdentity}",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });

        body.Children.Add(new TextBlock
        {
            Text = Localizer.Format("About_SettingsPath", Settings.Path),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
        });

        // The copyright and licence name are not repeated: the licence opens with both.
        body.Children.Add(Section(Localizer.Get("About_License"), LicenceText()));
        body.Children.Add(Section(Localizer.Get("About_ThirdParty"), ComponentTable()));

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Localizer.Get("About_Title"),
            CloseButtonText = Localizer.Get("About_Close"),
            Content = new ScrollViewer
            {
                Content = new Grid { Children = { body }, Width = 780 },
                MaxHeight = 520,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
        };

        await dialog.ShowAsync();
    }

    /// <summary>A heading with its content underneath, the way the main page does it.</summary>
    private static StackPanel Section(string heading, UIElement content)
    {
        var section = new StackPanel { Spacing = 8 };
        section.Children.Add(new TextBlock
        {
            Text = heading,
            Style = (Style)Application.Current.Resources["SectionHeaderStyle"],
        });
        section.Children.Add(content);
        return section;
    }

    private static TextBlock LicenceText() => new()
    {
        Text = ForDisplay(SleepyInfo.License),
        IsTextSelectionEnabled = true,
        TextWrapping = TextWrapping.Wrap,
        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas, monospace"),
        FontSize = 11.5,
        LineHeight = 16,
    };

    /// <summary>The third-party components, one row each, plus the obligations note.</summary>
    private static StackPanel ComponentTable()
    {
        var table = new Grid { ColumnSpacing = 16, RowSpacing = 6 };
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var row = 0;
        foreach (var component in SleepyInfo.ThirdPartyComponents)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Add(table, Cell(component.Name), row, 0);
            Add(table, Cell(component.License, muted: true), row, 1);
            Add(table, Cell(component.UsedBy, muted: true), row, 2);
            row++;
        }

        var block = new StackPanel { Spacing = 12 };
        block.Children.Add(table);
        block.Children.Add(new TextBlock
        {
            Text = SleepyInfo.ThirdPartyNotes,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
        });

        return block;
    }

    private static TextBlock Cell(string text, bool muted = false) => new()
    {
        Text = text,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        IsTextSelectionEnabled = true,
        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
            muted ? "SecondaryTextBrush" : "PrimaryTextBrush"],
    };

    private static void Add(Grid grid, FrameworkElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    /// <summary>
    /// Normalises line endings to CRLF. The embedded texts use LF, which the
    /// text controls collapse into a single line.
    /// </summary>
    private static string ForDisplay(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", "\r\n", StringComparison.Ordinal);

    private void OnSelectAllClick(object sender, RoutedEventArgs e) => ViewModel.SelectAllModules(true);

    private void OnSelectNoneClick(object sender, RoutedEventArgs e) => ViewModel.SelectAllModules(false);

    private void OnModuleToggled(object sender, RoutedEventArgs e) => ViewModel.Convert();

    /// <summary>WinAppSDK pickers need to be told which window owns them.</summary>
    private static void InitializeWithWindow(object picker)
    {
#if WINDOWS
        if (App.MainWindowInstance is { } window)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker,
                WinRT.Interop.WindowNative.GetWindowHandle(window));
        }
#endif
    }
}
