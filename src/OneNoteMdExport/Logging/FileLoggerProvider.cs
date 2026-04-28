using System.Text;
using Microsoft.Extensions.Logging;

namespace OneNoteMdExport.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter _writer;
    private readonly Object _sync = new();

    public FileLoggerProvider(String path)
    {
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true,
        };
    }

    public ILogger CreateLogger(String categoryName) => new FileLogger(categoryName, _writer, _sync);

    public void Dispose() => _writer.Dispose();

    private sealed class FileLogger(String categoryName, StreamWriter writer, Object sync) : ILogger
    {
        private readonly String _categoryName = categoryName;
        private readonly StreamWriter _writer = writer;
        private readonly Object _sync = sync;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public Boolean IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, String> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (String.IsNullOrWhiteSpace(message) && exception is null)
            {
                return;
            }

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