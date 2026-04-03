using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace OneNoteMdExport.Logging;

public sealed class MessageOnlyConsoleFormatter : ConsoleFormatter, IDisposable
{
    public const string FormatterName = "message-only";

    private readonly IDisposable? _optionsReloadToken;
    private SimpleConsoleFormatterOptions _options;

    public MessageOnlyConsoleFormatter(IOptionsMonitor<SimpleConsoleFormatterOptions> options)
        : base(FormatterName)
    {
        _options = options.CurrentValue;
        _optionsReloadToken = options.OnChange(updated => _options = updated);
    }

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var message = logEntry.Formatter?.Invoke(logEntry.State, null);
        if (string.IsNullOrWhiteSpace(message))
            return;

        if (!string.IsNullOrEmpty(_options.TimestampFormat))
            textWriter.Write(DateTimeOffset.Now.ToString(_options.TimestampFormat));

        textWriter.WriteLine(message);
    }

    public void Dispose() => _optionsReloadToken?.Dispose();
}