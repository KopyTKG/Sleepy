using System.Collections.ObjectModel;
using System.Linq;
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

    /// <summary>
    /// Shared so that re-navigating the page — which is how a language change is
    /// applied — does not throw away what the user has loaded.
    /// </summary>
    public static MainViewModel Instance { get; } = new();

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
    private string _outputFileName = "insomnia.json";

    /// <summary>The converted export, ready to be written to disk.</summary>
    public string ExportJson { get; private set; } = string.Empty;

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
            var modules = await Discovery
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
            SetStatus(Modules.Count == 1
                ? Localizer.Format(
                    "Status_LoadedSingle", Modules[0].Name, Localizer.Count("Count_Requests", requestCount))
                : Localizer.Format(
                    "Status_LoadedSeveral",
                    Localizer.Count("Count_Modules", Modules.Count),
                    Localizer.Count("Count_Requests", requestCount)));

            Convert();
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

        var document = InsomniaBuilder.Build(selected, new BuildOptions
        {
            SourceName = string.IsNullOrWhiteSpace(SourceLabel) ? "gui" : SourceLabel,
            WorkspaceName = string.IsNullOrWhiteSpace(WorkspaceName) ? null : WorkspaceName.Trim(),
            Layout = LayoutIndex switch
            {
                1 => FolderLayout.Flat,
                2 => FolderLayout.ModuleOnly,
                _ => FolderLayout.Nested,
            },
            BearerToken = BearerToken,
        });

        ExportJson = JsonWriter.Write(document);
        Preview = ExportJson.Length > 20000
            ? ExportJson[..20000] + "\n" + Localizer.Get("Preview_Truncated")
            : ExportJson;

        var requests = selected.Sum(InsomniaBuilder.CountOperations);
        Summary = selected.Count == 1
            ? Localizer.Count("Count_Requests", requests)
            : Localizer.Format(
                "Summary_Several",
                Localizer.Count("Count_Modules", selected.Count),
                Localizer.Count("Count_Requests", requests));
        HasResult = true;
        SetStatus(Localizer.Get("Status_Ready"));
    }

    /// <summary>Applies the base URL override, if the user typed one.</summary>
    private ApiModule ApplyBaseUrl(ApiModule module)
        => string.IsNullOrWhiteSpace(BaseUrl) ? module : module with { BaseUrl = BaseUrl.Trim() };

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

    partial void OnLayoutIndexChanged(int value) => Convert();

    partial void OnWorkspaceNameChanged(string value)
    {
        OutputFileName = string.IsNullOrWhiteSpace(value) ? "insomnia.json" : $"{Slugify(value)}.json";
        Convert();
    }

    partial void OnBearerTokenChanged(string value) => Convert();

    partial void OnBaseUrlChanged(string value) => Convert();

    private static string Slugify(string value)
    {
        var slug = new string(value
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')
            .ToArray());
        return slug.Trim('-');
    }
}
