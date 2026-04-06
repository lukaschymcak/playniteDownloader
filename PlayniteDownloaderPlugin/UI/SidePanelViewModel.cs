using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using PlayniteDownloaderPlugin.Models;
using PlayniteDownloaderPlugin.Pipeline;
using PlayniteDownloaderPlugin.Source;

namespace PlayniteDownloaderPlugin.UI;

public class SidePanelViewModel : INotifyPropertyChanged
{
    private readonly DownloadQueue _queue;
    private readonly SourceManager _sourceManager;
    private readonly UserConfig _config;
    private readonly Action _saveConfig;
    private readonly DownloadPipelineRunner _runner;

    public ObservableCollection<QueueEntry> Entries { get; } = new();
    public ObservableCollection<SourceEntry> Sources { get; } = new();

    private string _rdToken = string.Empty;
    public string RdToken
    {
        get => _rdToken;
        set { _rdToken = value; OnPropertyChanged(); }
    }

    private string _downloadPath = string.Empty;
    public string DownloadPath
    {
        get => _downloadPath;
        set { _downloadPath = value; OnPropertyChanged(); }
    }

    private string _newSourceUrl = string.Empty;
    public string NewSourceUrl
    {
        get => _newSourceUrl;
        set { _newSourceUrl = value; OnPropertyChanged(); }
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public ICommand CancelDownloadCommand { get; }
    public ICommand AddSourceCommand { get; }
    public ICommand RemoveSourceCommand { get; }
    public ICommand SaveSettingsCommand { get; }

    public SidePanelViewModel(
        DownloadQueue queue,
        SourceManager sourceManager,
        UserConfig config,
        Action saveConfig,
        DownloadPipelineRunner runner)
    {
        _queue = queue;
        _sourceManager = sourceManager;
        _config = config;
        _saveConfig = saveConfig;
        _runner = runner;

        _rdToken = config.RealDebridApiToken;
        _downloadPath = config.DefaultDownloadPath;

        CancelDownloadCommand = new RelayCommand<string>(id =>
        {
            _queue.Cancel(id);
            Refresh();
        });

        AddSourceCommand = new RelayCommand(_ =>
        {
            if (string.IsNullOrWhiteSpace(NewSourceUrl)) return;
            try
            {
                _sourceManager.AddCustomSource("Custom Source", NewSourceUrl);
                NewSourceUrl = string.Empty;
                StatusMessage = "Source added.";
                RefreshSources();
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = ex.Message;
            }
        });

        RemoveSourceCommand = new RelayCommand<string>(id =>
        {
            _sourceManager.RemoveCustomSource(id);
            RefreshSources();
        });

        SaveSettingsCommand = new RelayCommand(_ =>
        {
            _config.RealDebridApiToken = RdToken;
            _config.DefaultDownloadPath = DownloadPath;
            _config.RealDebridEnabled = !string.IsNullOrEmpty(RdToken);
            _saveConfig();
            StatusMessage = "Settings saved.";
        });

        _runner.EntryUpdated += _ => App.Current?.Dispatcher.BeginInvoke(Refresh);
        Refresh();
        RefreshSources();
    }

    public void Refresh()
    {
        Entries.Clear();
        foreach (QueueEntry entry in _queue.GetAll())
            Entries.Add(entry);
    }

    private void RefreshSources()
    {
        Sources.Clear();
        foreach (SourceEntry source in _sourceManager.GetAllSources())
            Sources.Add(source);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    public RelayCommand(Action<object?> execute) => _execute = execute;
    public bool CanExecute(object? p) => true;
    public void Execute(object? p) => _execute(p);
    public event EventHandler? CanExecuteChanged;
}

public class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    public RelayCommand(Action<T?> execute) => _execute = execute;
    public bool CanExecute(object? p) => true;
    public void Execute(object? p) => _execute(p is T t ? t : default);
    public event EventHandler? CanExecuteChanged;
}
