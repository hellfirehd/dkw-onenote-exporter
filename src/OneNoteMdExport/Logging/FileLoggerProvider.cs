using System.Text;
using Microsoft.Extensions.Logging;

namespace OneNoteMdExport.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter _writer;
    private readonly object _sync = new();

    public FileLoggerProvider(string path)
    {
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true,
        };
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _writer, _sync);

    public void Dispose() => _writer.Dispose();

    private sealed class FileLogger(string categoryName, StreamWriter writer, object sync) : ILogger
    {
        private readonly string _categoryName = categoryName;
        private readonly StreamWriter _writer = writer;
        private readonly object _sync = sync;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
                return;

            var entry = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
                .Append(" [")
                .Append(logLevel)
                .Append("] ")
                .Append(_categoryName)
                .Append(": ")
                .Append(message);

            if (exception is not null)
            {
                entry.AppendLine()
                     .Append(exception);
            }

            lock (_sync)
            {
                _writer.WriteLine(entry.ToString());
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}