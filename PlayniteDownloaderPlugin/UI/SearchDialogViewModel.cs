using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Playnite.SDK;
using PlayniteDownloaderPlugin.Download;
using PlayniteDownloaderPlugin.Models;
using PlayniteDownloaderPlugin.Pipeline;
using PlayniteDownloaderPlugin.Source;

namespace PlayniteDownloaderPlugin.UI;

public class SearchDialogViewModel : INotifyPropertyChanged
{
    private readonly SourceManager _sourceManager;
    private readonly DownloadQueue _queue;
    private readonly UserConfig _config;
    private readonly string _gameId;
    private CancellationTokenSource _searchCts = new();

    public ObservableCollection<DownloadResult> Results { get; } = new();
    public ObservableCollection<string> SelectedUris { get; } = new();

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set { _searchText = value; OnPropertyChanged(); }
    }

    private string _downloadPath = string.Empty;
    public string DownloadPath
    {
        get => _downloadPath;
        set { _downloadPath = value; OnPropertyChanged(); }
    }

    private DownloadResult? _selectedResult;
    public DownloadResult? SelectedResult
    {
        get => _selectedResult;
        set
        {
            _selectedResult = value;
            OnPropertyChanged();
            SelectedUris.Clear();
            if (value != null)
                foreach (string uri in value.Uris)
                    SelectedUris.Add(uri);
            SelectedUri = SelectedUris.FirstOrDefault();
        }
    }

    private string? _selectedUri;
    public string? SelectedUri
    {
        get => _selectedUri;
        set { _selectedUri = value; OnPropertyChanged(); }
    }

    private bool _isSearching;
    public bool IsSearching
    {
        get => _isSearching;
        set { _isSearching = value; OnPropertyChanged(); }
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public ICommand SearchCommand { get; }
    public ICommand DownloadCommand { get; }
    public ICommand BrowseCommand { get; }

    public Action? CloseDialog { get; set; }

    private readonly IPlayniteAPI _playniteApi;

    public SearchDialogViewModel(
        string gameId,
        string gameName,
        SourceManager sourceManager,
        DownloadQueue queue,
        UserConfig config,
        IPlayniteAPI playniteApi)
    {
        _gameId = gameId;
        _sourceManager = sourceManager;
        _queue = queue;
        _config = config;
        _searchText = gameName;
        _downloadPath = config.DefaultDownloadPath;
        _playniteApi = playniteApi;

        SearchCommand = new RelayCommand(async _ =>
        {
            _searchCts.Cancel();
            _searchCts = new CancellationTokenSource();
            IsSearching = true;
            Results.Clear();
            StatusMessage = string.Empty;
            try
            {
                List<DownloadResult> results = await _sourceManager.SearchAllAsync(SearchText, _searchCts.Token);
                foreach (DownloadResult r in results) Results.Add(r);
                if (!results.Any()) StatusMessage = "No results found.";
            }
            catch (OperationCanceledException) { }
            finally { IsSearching = false; }
        });

        DownloadCommand = new RelayCommand(async _ =>
        {
            if (SelectedUri == null)
            {
                StatusMessage = "Please select a download link.";
                return;
            }
            if (string.IsNullOrEmpty(DownloadPath))
            {
                StatusMessage = "Please set a download path.";
                return;
            }

            try
            {
                (List<string> urls, _) = await DownloaderFactory.ResolveUrlsAsync(
                    SelectedUri, _config, CancellationToken.None);

                string gameName = SelectedResult?.Title ?? SearchText;
                string extractionPath = Path.Combine(DownloadPath, SanitizePath(gameName));

                QueueEntry entry = new QueueEntry
                {
                    GameId = _gameId,
                    GameName = gameName,
                    OriginalUri = SelectedUri,
                    ResolvedUrls = urls,
                    DownloadPath = extractionPath,
                    ExtractionPath = extractionPath
                };

                _queue.Enqueue(entry);
                CloseDialog?.Invoke();
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = ex.Message;
            }
        });

        BrowseCommand = new RelayCommand(_ =>
        {
            string selected = _playniteApi.Dialogs.SelectFolder();
            if (!string.IsNullOrEmpty(selected))
                DownloadPath = selected;
        });
    }

    private static string SanitizePath(string name)
        => string.Concat(name.Select(c =>
            Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
