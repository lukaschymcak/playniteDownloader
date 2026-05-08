using System;
using System.IO;

namespace LuDownloader.App.Logging
{
    public class FileLogger : BlankPlugin.ICoreLogger
    {
        private readonly string _path;
        private readonly object _lock = new object();

        public FileLogger(string path) => _path = path;

        private void Write(string level, string msg)
        {
            lock (_lock)
            {
                try
                {
                    File.AppendAllText(_path,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {msg}{Environment.NewLine}");
                }
                catch { /* suppress logging errors */ }
            }
        }

        public void Info(string msg)  => Write("INFO",  msg);
        public void Warn(string msg)  => Write("WARN",  msg);
        public void Error(string msg) => Write("ERROR", msg);
        public void Error(Exception ex, string msg) => Write("ERROR", msg + " | " + ex);
        public void Debug(string msg) => Write("DEBUG", msg);
        public void Trace(string msg) => Write("TRACE", msg);
    }
}
