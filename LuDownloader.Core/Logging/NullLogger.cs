using System;

namespace BlankPlugin
{
    public sealed class NullLogger : ILogger
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
        public void Error(Exception ex, string message) { }
        public void Debug(string message) { }
        public void Trace(string message) { }
    }
}
