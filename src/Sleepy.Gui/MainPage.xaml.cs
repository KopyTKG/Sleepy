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

    public MainPage()
    {
        this.InitializeComponent();
        DataContext = ViewModel;
        SelectCurrentLanguage();
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

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker
            {
                SuggestedFileName = ViewModel.SuggestedFileName,
                DefaultFileExtension = ".json",
            };
            picker.FileTypeChoices.Add("Insomnia export", [".json"]);
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

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
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
    private async void OnAboutClick(object sender, RoutedEventArgs e)
    {
        var body = new StackPanel { Spacing = 12 };

        body.Children.Add(new TextBlock
        {
            Text = $"sleepy {SleepyInfo.Version}",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });

        // The copyright and licence name are not repeated here: the embedded
        // licence text opens with both.
        var licence = new TextBlock
        {
            Text = ForDisplay($"{SleepyInfo.License}\n\n{SleepyInfo.ThirdPartyNotices}"),
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas, monospace"),
            FontSize = 11.5,
            LineHeight = 16,
        };

        body.Children.Add(new ScrollViewer
        {
            Content = licence,
            Height = 380,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Localizer.Get("About_Title"),
            CloseButtonText = Localizer.Get("About_Close"),
            Content = new Grid { Children = { body }, Width = 780 },
        };

        await dialog.ShowAsync();
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
