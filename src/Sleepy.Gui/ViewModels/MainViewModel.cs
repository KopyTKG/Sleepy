using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sleepy.Gui.Models;
using Sleepy.Gui.Services;

namespace Sleepy.Gui.ViewModels;

/// <summary>
/// Drives the single page: load specifications, let the user tune the export,
/// convert, and hand the result back for saving.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly List<ApiModule> _loaded = [];

    // What the last Apply used. The text fields above are drafts until then:
    // re-converting on every keystroke rebuilt the whole preview, which is a lot
    // of work to do between two letters of a workspace name.
    private string _appliedWorkspaceName = string.Empty;
    private string _appliedBaseUrl = string.Empty;
    private string _appliedBearerToken = string.Empty;

    /// <summary>
    /// Shared so that re-navigating the page — which is how a language change is
    /// applied — does not throw away what the user has loaded.
    /// </summary>
    public static MainViewModel Instance { get; } = new();

    public MainViewModel()
    {
        var settings = Settings.Current;

        _formatIndex = Math.Max(0, CollectionBuilders.Ids
            .ToList()
            .FindIndex(id => string.Equals(id, settings.Format, StringComparison.OrdinalIgnoreCase)));
        _layoutIndex = settings.Layout switch
        {
            FolderLayout.Flat => 1,
            FolderLayout.ModuleOnly => 2,
            _ => 0,
        };
        _requestHeader = settings.RequestHeader;
        _ignoreCertificateErrors = settings.IgnoreCertificateErrors;
        _outputFileName = Format.DefaultOutputName;
    }

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private string _sourceLabel = string.Empty;

    [ObservableProperty]
    private string _workspaceName = string.Empty;

    [ObservableProperty]
    private string _baseUrl = string.Empty;

    [ObservableProperty]
    private string _bearerToken = string.Empty;

    [ObservableProperty]
    private string _requestHeader = string.Empty;

    [ObservableProperty]
    private bool _ignoreCertificateErrors;

    [ObservableProperty]
    private int _layoutIndex;

    [ObservableProperty]
    private int _formatIndex;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = Localizer.Get("Status_Idle");

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private bool _hasModules;

    [ObservableProperty]
    private bool _hasSeveralModules;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _preview = string.Empty;

    [ObservableProperty]
    private bool _hasResult;

    /// <summary>Shown next to the preview heading, and suggested when saving.</summary>
    [ObservableProperty]
    private string _outputFileName = CollectionBuilders.Default.DefaultOutputName;

    /// <summary>The output formats on offer, in registry order.</summary>
    public IReadOnlyList<string> Formats { get; } =
        CollectionBuilders.All.Select(builder => builder.DisplayName).ToList();

    /// <summary>The format currently chosen.</summary>
    public ICollectionBuilder Format =>
        CollectionBuilders.All[Math.Clamp(FormatIndex, 0, CollectionBuilders.All.Count - 1)];

    /// <summary>
    /// Whether the text fields hold something the preview has not been built
    /// from yet.
    /// </summary>
    public bool HasPendingChanges =>
        HasModules
        && (!string.Equals(WorkspaceName, _appliedWorkspaceName, StringComparison.Ordinal)
            || !string.Equals(BaseUrl, _appliedBaseUrl, StringComparison.Ordinal)
            || !string.Equals(BearerToken, _appliedBearerToken, StringComparison.Ordinal));

    /// <summary>The folder layout the picker is on.</summary>
    public FolderLayout Layout => LayoutIndex switch
    {
        1 => FolderLayout.Flat,
        2 => FolderLayout.ModuleOnly,
        _ => FolderLayout.Nested,
    };

    /// <summary>Whether the chosen format writes a directory rather than one file.</summary>
    public bool IsFolderFormat => Format.IsFolderFormat;

    /// <summary>What this build is, shown in the status bar and the About dialog.</summary>
    public string Version => SleepyInfo.BuildIdentity;

    /// <summary>The converted collection, ready to be written to disk.</summary>
    public BuiltCollection? Collection { get; private set; }

    /// <summary>The single-file export, for the clipboard. Empty for folder formats.</summary>
    public string ExportJson => Collection is { IsSingleFile: true } single ? single.Content : string.Empty;

    public ObservableCollection<ModuleEntry> Modules { get; } = [];

    public string SuggestedFileName => OutputFileName;

    /// <summary>
    /// Loads every module the input refers to: a local JSON/YAML file, a spec URL,
    /// or a Swagger UI URL serving several definitions.
    /// </summary>
    public async Task LoadAsync(string input)
    {
        input = input.Trim().Trim('"');
        if (input.Length == 0)
        {
            SetError(Localizer.Get("Status_NoInput"));
            return;
        }

        InputText = input;
        IsBusy = true;
        SetStatus(Localizer.Format("Status_Loading", input));

        try
        {
            var headers = string.IsNullOrWhiteSpace(RequestHeader) ? [] : new[] { RequestHeader };
            using var source = new SpecSource(headers, IgnoreCertificateErrors);
            var modules = await ModuleDiscovery
                .ExpandAsync(input, source, baseUrlOverride: null, CancellationToken.None)
                .ConfigureAwait(true);

            _loaded.Clear();
            _loaded.AddRange(modules);

            Modules.Clear();
            foreach (var module in modules)
            {
                Modules.Add(new ModuleEntry(module));
            }

            HasModules = Modules.Count > 0;
            HasSeveralModules = Modules.Count > 1;
            OnPropertyChanged(nameof(HasPendingChanges));
            SourceLabel = input;

            if (string.IsNullOrWhiteSpace(BaseUrl))
            {
                var origins = modules
                    .Select(module => module.BaseUrl)
                    .Where(baseUrl => baseUrl.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                BaseUrl = origins.Count == 1 ? origins[0] : string.Empty;
            }

            var requestCount = Modules.Sum(module => module.RequestCount);
            Apply();

            SetStatus(Modules.Count == 1
                ? Localizer.Format(
                    "Status_LoadedSingle", Modules[0].Name, Localizer.Count("Count_Requests", requestCount))
                : Localizer.Format(
                    "Status_LoadedSeveral",
                    Localizer.Count("Count_Modules", Modules.Count),
                    Localizer.Count("Count_Requests", requestCount)));

        }
        catch (SleepyException exception)
        {
            SetError(exception.Message);
        }
        catch (HttpRequestException exception)
        {
            SetError(exception.Message);
        }
        catch (TaskCanceledException)
        {
            SetError(Localizer.Get("Status_Timeout"));
        }
        catch (IOException exception)
        {
            SetError(exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Takes what is typed and rebuilds the collection from it.</summary>
    [RelayCommand]
    public void Apply()
    {
        _appliedWorkspaceName = WorkspaceName;
        _appliedBaseUrl = BaseUrl;
        _appliedBearerToken = BearerToken;

        OutputFileName = string.IsNullOrWhiteSpace(_appliedWorkspaceName)
            ? Format.DefaultOutputName
            : NameFor(_appliedWorkspaceName);

        OnPropertyChanged(nameof(HasPendingChanges));
        Convert();
    }

    [RelayCommand]
    public void Convert()
    {
        if (_loaded.Count == 0)
        {
            return;
        }

        var selected = Modules
            .Where(entry => entry.IsSelected)
            .Select(entry => ApplyBaseUrl(entry.Module))
            .ToList();

        if (selected.Count == 0)
        {
            HasResult = false;
            SetError(Localizer.Get("Status_NoModuleSelected"));
            return;
        }

        var collection = Format.Build(selected, new BuildOptions
        {
            SourceName = string.IsNullOrWhiteSpace(SourceLabel) ? "gui" : SourceLabel,
            WorkspaceName = string.IsNullOrWhiteSpace(_appliedWorkspaceName)
                ? null
                : _appliedWorkspaceName.Trim(),
            Layout = Layout,
            BearerToken = _appliedBearerToken,
        });

        Collection = collection;
        Preview = Truncate(RenderPreview(collection));

        var requests = selected.Sum(Operations.Count);
        Summary = selected.Count == 1
            ? Localizer.Count("Count_Requests", requests)
            : Localizer.Format(
                "Summary_Several",
                Localizer.Count("Count_Modules", selected.Count),
                Localizer.Count("Count_Requests", requests));
        HasResult = true;
        SetStatus(Localizer.Get("Status_Ready"));
    }

    /// <summary>
    /// A single-file format previews as itself; a folder format previews as its
    /// files, each under its path, which is the only way to see what will land on
    /// disk before it does.
    /// </summary>
    private static string RenderPreview(BuiltCollection collection)
    {
        if (collection.IsSingleFile)
        {
            return collection.Content;
        }

        var preview = new StringBuilder();
        foreach (var file in collection.Files)
        {
            preview.Append("── ").Append(file.RelativePath).Append(" ──\n\n");
            preview.Append(file.Content.TrimEnd()).Append("\n\n");
        }

        return preview.ToString().TrimEnd();
    }

    private static string Truncate(string text)
        => text.Length > 20000 ? text[..20000] + "\n" + Localizer.Get("Preview_Truncated") : text;

    /// <summary>Applies the base URL override, if the user typed one.</summary>
    private ApiModule ApplyBaseUrl(ApiModule module)
        => string.IsNullOrWhiteSpace(_appliedBaseUrl)
            ? module
            : module with { BaseUrl = _appliedBaseUrl.Trim() };

    public async Task SaveAsync(string path)
    {
        try
        {
            await File.WriteAllTextAsync(path, ExportJson).ConfigureAwait(true);
            OutputFileName = Path.GetFileName(path);
            SetStatus(Localizer.Format("Status_Saved", path));
        }
        catch (IOException exception)
        {
            SetError(exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            SetError(exception.Message);
        }
    }

    /// <summary>Writes a folder format into <paramref name="directory"/>.</summary>
    public async Task SaveToFolderAsync(string directory)
    {
        if (Collection is not { } collection)
        {
            return;
        }

        try
        {
            foreach (var file in collection.Files)
            {
                var path = Path.Combine(directory, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, file.Content).ConfigureAwait(true);
            }

            OutputFileName = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar));
            SetStatus(Localizer.Format("Status_SavedFiles", collection.Files.Count, directory));
        }
        catch (IOException exception)
        {
            SetError(exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            SetError(exception.Message);
        }
    }

    public void SelectAllModules(bool selected)
    {
        foreach (var module in Modules)
        {
            module.IsSelected = selected;
        }

        Convert();
    }

    /// <summary>Re-renders the strings this view model owns after a language change.</summary>
    public void Relocalize()
    {
        foreach (var module in Modules)
        {
            module.Relocalize();
        }

        if (HasResult)
        {
            Convert();
        }
        else if (!IsError)
        {
            SetStatus(Localizer.Get("Status_Idle"));
        }
    }

    public void SetStatus(string message)
    {
        IsError = false;
        Status = message;
    }

    public void SetError(string message)
    {
        IsError = true;
        Status = message;
    }

    partial void OnLayoutIndexChanged(int value)
    {
        Settings.Update(settings => settings with { Layout = Layout });
        Convert();
    }

    partial void OnRequestHeaderChanged(string value)
        => Settings.Update(settings => settings with { RequestHeader = value });

    partial void OnIgnoreCertificateErrorsChanged(bool value)
        => Settings.Update(settings => settings with { IgnoreCertificateErrors = value });

    partial void OnFormatIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsFolderFormat));
        Settings.Update(settings => settings with { Format = Format.FormatId });

        // Keep the name the user chose; only its extension belongs to the format.
        OutputFileName = string.IsNullOrWhiteSpace(_appliedWorkspaceName)
            ? Format.DefaultOutputName
            : NameFor(_appliedWorkspaceName);
        Convert();
    }

    partial void OnWorkspaceNameChanged(string value) => OnPropertyChanged(nameof(HasPendingChanges));

    /// <summary>The suggested output name for a named collection, in the chosen format.</summary>
    private string NameFor(string workspaceName)
    {
        var stem = Slugify(workspaceName);
        var extension = Path.GetExtension(Format.DefaultOutputName);
        return extension.Length == 0 ? stem : stem + extension;
    }

    partial void OnBearerTokenChanged(string value) => OnPropertyChanged(nameof(HasPendingChanges));

    partial void OnBaseUrlChanged(string value) => OnPropertyChanged(nameof(HasPendingChanges));

    private static string Slugify(string value)
    {
        var slug = new string(value
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')
            .ToArray());
        return slug.Trim('-');
    }
}
