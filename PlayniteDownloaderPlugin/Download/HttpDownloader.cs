using PlayniteDownloaderPlugin.Models;
using System.Net.Http.Headers;

namespace PlayniteDownloaderPlugin.Download;

public class HttpDownloader : IDownloader
{
    private const int StallTimeoutMs = 8000;
    private const int MaxRetryAttempts = 10;
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;
    private bool _isPaused;
    private string _resolvedFileName = string.Empty;
    private string _currentUrl = string.Empty;
    private string _currentSavePath = string.Empty;

    private long _bytesDownloaded;
    private long _fileSize;
    private double _speed;
    private DownloaderStatus _status = DownloaderStatus.Paused;

    public event Action<DownloadProgress>? ProgressChanged;

    public HttpDownloader(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
    }

    public async Task StartAsync(string url, string savePath, CancellationToken ct)
    {
        _currentUrl = url;
        _currentSavePath = savePath;
        _isPaused = false;
        _bytesDownloaded = 0;
        _speed = 0;
        _status = DownloaderStatus.Active;

        await DownloadWithRetryAsync(url, savePath, ct);
    }

    private async Task DownloadWithRetryAsync(string url, string savePath, CancellationToken ct)
    {
        int retries = 0;
        while (!_isPaused)
        {
            try
            {
                await ExecuteDownloadAsync(url, savePath, ct);
                return;
            }
            catch (OperationCanceledException) when (_isPaused)
            {
                _status = DownloaderStatus.Paused;
                FireProgress();
                return;
            }
            catch (Exception) when (retries < MaxRetryAttempts && !_isPaused)
            {
                retries++;
                double delayMs = Math.Min(1000 * Math.Pow(2, retries - 1), MaxRetryDelay.TotalMilliseconds);
                await Task.Delay((int)delayMs, ct);
            }
        }
        _status = DownloaderStatus.Error;
        FireProgress();
    }

    private async Task ExecuteDownloadAsync(string url, string savePath, CancellationToken ct)
    {
        _status = DownloaderStatus.Active;
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);

        string? existingFile = FindExistingFile(savePath);
        if (existingFile != null)
        {
            _bytesDownloaded = new FileInfo(existingFile).Length;
            request.Headers.Range = new RangeHeaderValue(_bytesDownloaded, null);
        }

        using HttpResponseMessage response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        _resolvedFileName = ParseFilename(response, url);
        string filePath = Path.Combine(savePath, _resolvedFileName);
        _fileSize = (response.Content.Headers.ContentLength ?? 0) + _bytesDownloaded;

        Directory.CreateDirectory(savePath);
        FileMode fileMode = _bytesDownloaded > 0 ? FileMode.Append : FileMode.Create;
        using FileStream fileStream = new FileStream(filePath, fileMode, FileAccess.Write);
        using Stream stream = await response.Content.ReadAsStreamAsync(ct);

        byte[] buffer = new byte[81920];
        DateTime lastSpeedUpdate = DateTime.UtcNow;
        long bytesAtLastUpdate = _bytesDownloaded;

        while (true)
        {
            using CancellationTokenSource stallCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stallCts.CancelAfter(StallTimeoutMs);
            int bytesRead;
            try
            {
                bytesRead = await stream.ReadAsync(buffer, stallCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException("Download stalled — no data received for 8 seconds.");
            }

            if (bytesRead == 0) break;

            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            _bytesDownloaded += bytesRead;

            DateTime now = DateTime.UtcNow;
            double elapsed = (now - lastSpeedUpdate).TotalSeconds;
            if (elapsed >= 1.0)
            {
                _speed = (_bytesDownloaded - bytesAtLastUpdate) / elapsed;
                lastSpeedUpdate = now;
                bytesAtLastUpdate = _bytesDownloaded;
            }

            FireProgress();
        }

        _status = DownloaderStatus.Complete;
        FireProgress();
    }

    private string? FindExistingFile(string savePath)
    {
        if (!Directory.Exists(savePath) || string.IsNullOrEmpty(_resolvedFileName))
            return null;
        string candidate = Path.Combine(savePath, _resolvedFileName);
        return File.Exists(candidate) ? candidate : null;
    }

    private static string ParseFilename(HttpResponseMessage response, string url)
    {
        System.Net.Http.Headers.ContentDispositionHeaderValue? cd = response.Content.Headers.ContentDisposition;
        if (cd != null)
        {
            string? name = cd.FileNameStar ?? cd.FileName;
            if (!string.IsNullOrEmpty(name))
                return SanitizeFilename(name.Trim('"'));
        }
        string path = new Uri(url).AbsolutePath;
        string fromUrl = Path.GetFileName(path);
        return string.IsNullOrEmpty(fromUrl) ? "download" : fromUrl;
    }

    private static string SanitizeFilename(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }

    private void FireProgress() => ProgressChanged?.Invoke(new DownloadProgress
    {
        BytesDownloaded = _bytesDownloaded,
        FileSize = _fileSize,
        SpeedBytesPerSecond = _speed,
        FileName = _resolvedFileName,
        Status = _status
    });

    public void Pause()
    {
        _isPaused = true;
        _status = DownloaderStatus.Paused;
    }

    public void Resume()
    {
        if (_status != DownloaderStatus.Paused) return;
        _ = StartAsync(_currentUrl, _currentSavePath, CancellationToken.None);
    }

    public void Cancel(bool deleteFile = true)
    {
        _isPaused = true;
        _status = DownloaderStatus.Paused;
        if (deleteFile && !string.IsNullOrEmpty(_currentSavePath)
            && !string.IsNullOrEmpty(_resolvedFileName))
        {
            string file = Path.Combine(_currentSavePath, _resolvedFileName);
            if (File.Exists(file)) File.Delete(file);
        }
    }

    public DownloadProgress GetStatus() => new()
    {
        BytesDownloaded = _bytesDownloaded,
        FileSize = _fileSize,
        SpeedBytesPerSecond = _speed,
        FileName = _resolvedFileName,
        Status = _status
    };
}
