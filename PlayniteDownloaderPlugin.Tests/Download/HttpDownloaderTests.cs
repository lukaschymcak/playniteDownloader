using System.Net;
using System.Net.Http;
using PlayniteDownloaderPlugin.Download;
using PlayniteDownloaderPlugin.Models;
using Xunit;

namespace PlayniteDownloaderPlugin.Tests.Download;

public class HttpDownloaderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public HttpDownloaderTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task StartAsync_DownloadsFileToDirectory()
    {
        byte[] content = new byte[] { 1, 2, 3, 4, 5 };
        StaticFileHandler handler = new StaticFileHandler(content, "game.zip");
        HttpDownloader downloader = new HttpDownloader(new HttpClient(handler));

        await downloader.StartAsync("http://example.com/game.zip", _tempDir, CancellationToken.None);

        string[] files = Directory.GetFiles(_tempDir);
        Assert.Single(files);
        Assert.Equal(content, await File.ReadAllBytesAsync(files[0]));
    }

    [Fact]
    public async Task StartAsync_UsesContentDispositionFilename()
    {
        byte[] content = new byte[] { 1, 2, 3 };
        StaticFileHandler handler = new StaticFileHandler(content, "actual-name.rar",
            contentDisposition: "attachment; filename=\"actual-name.rar\"");
        HttpDownloader downloader = new HttpDownloader(new HttpClient(handler));

        await downloader.StartAsync("http://example.com/randomtoken123", _tempDir, CancellationToken.None);

        string[] files = Directory.GetFiles(_tempDir);
        Assert.Contains("actual-name.rar", files[0]);
    }

    [Fact]
    public async Task StartAsync_ReportsProgressEvents()
    {
        byte[] content = new byte[1024 * 10];
        StaticFileHandler handler = new StaticFileHandler(content, "big.zip");
        HttpDownloader downloader = new HttpDownloader(new HttpClient(handler));
        List<DownloadProgress> progressEvents = new List<DownloadProgress>();
        downloader.ProgressChanged += p => progressEvents.Add(p);

        await downloader.StartAsync("http://example.com/big.zip", _tempDir, CancellationToken.None);

        Assert.NotEmpty(progressEvents);
        Assert.Equal(DownloaderStatus.Complete, downloader.GetStatus().Status);
    }

    [Fact]
    public async Task Cancel_DeletesPartialFile()
    {
        byte[] content = new byte[1024 * 64];
        SlowStreamHandler handler = new SlowStreamHandler(content, "partial.zip");
        HttpDownloader downloader = new HttpDownloader(new HttpClient(handler));
        string? downloadedFile = null;
        downloader.ProgressChanged += p =>
        {
            if (!string.IsNullOrEmpty(p.FileName))
                downloadedFile = Path.Combine(_tempDir, p.FileName);
            if (p.BytesDownloaded > 0 && downloadedFile != null)
                downloader.Cancel(deleteFile: true);
        };

        Task downloadTask = downloader.StartAsync("http://example.com/partial.zip",
            _tempDir, CancellationToken.None);
        await Task.WhenAny(downloadTask, Task.Delay(5000));

        if (downloadedFile != null)
            Assert.False(File.Exists(downloadedFile), "Partial file should be deleted on cancel");
        Assert.Equal(DownloaderStatus.Error, downloader.GetStatus().Status);
    }
}

public class SlowStreamHandler : HttpMessageHandler
{
    private readonly byte[] _content;
    private readonly string _filename;
    private readonly string? _contentDisposition;

    public SlowStreamHandler(byte[] content, string filename, string? contentDisposition = null)
    {
        _content = content;
        _filename = filename;
        _contentDisposition = contentDisposition;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        MemoryStream ms = new MemoryStream(_content);
        SlowHttpContent httpContent = new SlowHttpContent(ms, _content.Length);
        HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = httpContent
        };
        response.Content.Headers.ContentLength = _content.Length;
        if (_contentDisposition != null)
            response.Content.Headers.TryAddWithoutValidation("Content-Disposition", _contentDisposition);
        return Task.FromResult(response);
    }

    private class SlowHttpContent : System.Net.Http.HttpContent
    {
        private readonly MemoryStream _stream;
        private readonly long _length;

        public SlowHttpContent(MemoryStream stream, long length)
        {
            _stream = stream;
            _length = length;
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            return Task.FromResult<Stream>(new SlowStream(_stream, _length));
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return _stream.CopyToAsync(stream);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }
    }

    private class SlowStream : Stream
    {
        private readonly MemoryStream _inner;
        private readonly long _totalLength;
        private int _bytesReturned;

        public SlowStream(MemoryStream inner, long totalLength)
        {
            _inner = inner;
            _totalLength = totalLength;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _totalLength;
        public override long Position
        {
            get => _bytesReturned;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int toRead = Math.Min(count, (int)(_totalLength - _bytesReturned));
            if (toRead <= 0) return 0;
            int read = _inner.Read(buffer, offset, toRead);
            _bytesReturned += read;
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            await Task.Delay(50, ct);
            int toRead = Math.Min(count, (int)(_totalLength - _bytesReturned));
            if (toRead <= 0) return 0;
            int read = await _inner.ReadAsync(buffer, offset, toRead, ct);
            _bytesReturned += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

public class StaticFileHandler : HttpMessageHandler
{
    private readonly byte[] _content;
    private readonly string _filename;
    private readonly string? _contentDisposition;

    public StaticFileHandler(byte[] content, string filename, string? contentDisposition = null)
    {
        _content = content;
        _filename = filename;
        _contentDisposition = contentDisposition;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(_content)
        };
        response.Content.Headers.ContentLength = _content.Length;
        if (_contentDisposition != null)
            response.Content.Headers.TryAddWithoutValidation("Content-Disposition", _contentDisposition);
        return Task.FromResult(response);
    }
}
