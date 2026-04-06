using System.Net.Http;
using PlayniteDownloaderPlugin.Download;
using PlayniteDownloaderPlugin.Integration;
using PlayniteDownloaderPlugin.Models;

namespace PlayniteDownloaderPlugin.Pipeline;

public class DownloadPipelineRunner
{
    private readonly DownloadQueue _queue;
    private readonly PlayniteIntegration _integration;
    private readonly UserConfig _config;
    private readonly HttpClient _http = new HttpClient();
    private CancellationTokenSource _cts = new();
    private Task? _workerTask;

    public event Action<QueueEntry>? EntryUpdated;

    public DownloadPipelineRunner(
        DownloadQueue queue, PlayniteIntegration integration, UserConfig config)
    {
        _queue = queue;
        _integration = integration;
        _config = config;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _workerTask = Task.Run(() => WorkerLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts.Cancel();
        _workerTask?.Wait(TimeSpan.FromSeconds(5));
    }

    private async Task WorkerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            QueueEntry? entry = _queue.Dequeue();
            if (entry == null)
            {
                try
                {
                    await Task.Delay(1000, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                continue;
            }

            await ProcessEntryAsync(entry, ct);
        }
    }

    private async Task ProcessEntryAsync(QueueEntry entry, CancellationToken ct)
    {
        try
        {
            if (entry.ResolvedUrls.Count == 0)
            {
                (List<string> urls, _) = await DownloaderFactory.ResolveUrlsAsync(
                    entry.OriginalUri, _config, ct);
                entry.ResolvedUrls = urls;
            }

            entry.Status = DownloadStatus.Active;
            Notify(entry);

            for (int i = entry.CurrentUrlIndex; i < entry.ResolvedUrls.Count; i++)
            {
                entry.CurrentUrlIndex = i;
                HttpDownloader downloader = new HttpDownloader(_http);
                downloader.ProgressChanged += progress =>
                {
                    entry.BytesDownloaded = progress.BytesDownloaded;
                    entry.FileSize = progress.FileSize;
                    entry.SpeedBytesPerSecond = progress.SpeedBytesPerSecond;
                    entry.Progress = entry.FileSize > 0
                        ? (float)entry.BytesDownloaded / entry.FileSize : 0;
                    _queue.UpdateEntry(entry);
                    Notify(entry);
                };

                await downloader.StartAsync(entry.ResolvedUrls[i], entry.DownloadPath, ct);
            }

            entry.Status = DownloadStatus.Extracting;
            entry.ExtractionProgress = 0;
            Notify(entry);

            string? archivePath = ExtractionService.FindArchiveEntryPoint(entry.DownloadPath);
            if (archivePath != null)
            {
                await ExtractionService.ExtractAsync(archivePath, entry.ExtractionPath,
                    progress =>
                    {
                        entry.ExtractionProgress = progress;
                        _queue.UpdateEntry(entry);
                        Notify(entry);
                    }, ct);
            }

            string? exePath = InstallResolver.FindExecutable(entry.ExtractionPath);
            _integration.MarkInstalled(entry.GameId, entry.ExtractionPath, exePath);

            entry.Status = DownloadStatus.Complete;
            _queue.UpdateEntry(entry);
            Notify(entry);
        }
        catch (OperationCanceledException)
        {
            entry.Status = DownloadStatus.Paused;
            _queue.UpdateEntry(entry);
            Notify(entry);
        }
        catch (Exception ex)
        {
            entry.Status = DownloadStatus.Error;
            entry.ErrorMessage = ex.Message;
            _queue.UpdateEntry(entry);
            Notify(entry);
        }
    }

    private void Notify(QueueEntry entry) => EntryUpdated?.Invoke(entry);
}
