using System;

namespace BlankPlugin
{
    public static class LogManager
    {
        private static Func<ILogger> _factory = () => new NullLogger();

        public static void SetFactory(Func<ILogger> factory)
            => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

        public static ILogger GetLogger() => _factory();
    }
}
