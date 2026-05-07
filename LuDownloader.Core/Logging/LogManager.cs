using System;

namespace BlankPlugin
{
    public static class CoreLogManager
    {
        private static volatile Func<ICoreLogger> _factory = () => new NullCoreLogger();

        public static void SetFactory(Func<ICoreLogger> factory)
            => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

        public static ICoreLogger GetLogger() => _factory();
    }
}
