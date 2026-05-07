using System;

namespace BlankPlugin
{
    public interface ICoreLogger
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
        void Error(Exception ex, string message);
        void Debug(string message);
        void Trace(string message);
    }
}
