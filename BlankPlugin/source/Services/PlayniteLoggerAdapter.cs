using System;

namespace BlankPlugin
{
    public class PlayniteLoggerAdapter : ICoreLogger
    {
        private readonly Playnite.SDK.ILogger _inner;
        public PlayniteLoggerAdapter(Playnite.SDK.ILogger inner) => _inner = inner;

        public void Info(string message)  => _inner.Info(message);
        public void Warn(string message)  => _inner.Warn(message);
        public void Error(string message) => _inner.Error(message);
        public void Error(Exception ex, string message) => _inner.Error(ex, message);
        public void Debug(string message) => _inner.Debug(message);
        public void Trace(string message) => _inner.Trace(message);
    }
}
