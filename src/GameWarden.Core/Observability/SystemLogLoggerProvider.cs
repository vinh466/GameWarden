using System.Collections;
using System.Text.Json;
using GameWarden.Core.Data;
using Microsoft.Extensions.Logging;

namespace GameWarden.Core.Observability;

/// <summary>
/// Feeds Core's normal <see cref="ILogger"/> output into <see cref="SystemLogWriter"/> (docs/19). Scopes provide the
/// instance/job/request fields; everything else is stored as redacted JSON. The file logger remains the boot copy.
/// </summary>
public sealed class SystemLogLoggerProvider(SystemLogWriter writer, IExternalScopeProvider scopes, TimeProvider time) : ILoggerProvider
{
    public const LogLevel Minimum = LogLevel.Information;

    public ILogger CreateLogger(string categoryName) => new SystemLogLogger(writer, scopes, time, categoryName);

    public void Dispose()
    {
    }

    private sealed class SystemLogLogger(
        SystemLogWriter writer,
        IExternalScopeProvider scopes,
        TimeProvider time,
        string category) : ILogger
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel >= SystemLogLoggerProvider.Minimum && logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            try
            {
                var values = new Dictionary<string, object?>(StringComparer.Ordinal);
                scopes.ForEachScope((scope, target) =>
                {
                    if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                    {
                        foreach (var (key, value) in pairs)
                        {
                            target[key] = value;
                        }
                    }
                }, values);

                static string? Text(Dictionary<string, object?> values, string key) =>
                    values.TryGetValue(key, out var value) ? value?.ToString() : null;

                var instanceId = Text(values, "instanceId");
                var jobId = Text(values, "jobId");
                var requestId = Text(values, "requestId");
                var agentClientId = Text(values, "agentClientId");
                values.Remove("instanceId");
                values.Remove("jobId");
                values.Remove("requestId");
                values.Remove("agentClientId");
                var data = values.Count == 0 ? null : JsonSerializer.Serialize(values, Json);
                writer.Write(new SystemLogRecord
                {
                    At = time.GetUtcNow().UtcDateTime,
                    Level = logLevel switch
                    {
                        LogLevel.Trace => "trace",
                        LogLevel.Debug => "debug",
                        LogLevel.Warning => "warn",
                        LogLevel.Error => "error",
                        LogLevel.Critical => "critical",
                        _ => "info",
                    },
                    Component = category,
                    Category = category,
                    EventId = eventId.Id == 0 ? null : eventId.Id,
                    Message = formatter(state, exception),
                    Exception = exception?.ToString(),
                    InstanceId = instanceId,
                    JobId = jobId,
                    RequestId = requestId,
                    AgentClientId = agentClientId,
                    Source = "core",
                    Data = data,
                });
            }
            catch
            {
                // The file log is the fallback; a malformed scope must never break the caller.
            }
        }
    }
}
