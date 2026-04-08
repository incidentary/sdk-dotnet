using System.Data.Common;
using System.Diagnostics;
using Incidentary.Sdk.Context;
using Incidentary.Sdk.WireFormat;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Incidentary.Sdk.Integrations.EntityFrameworkCore;

/// <summary>EF Core command interceptor that records db_query events.</summary>
public sealed class IncidentaryDbCommandInterceptor : DbCommandInterceptor
{
    private readonly IIncidentaryClient _client;
    private readonly int _maxCommandTextLength;
    private readonly bool _includeSqlStatement;

    /// <param name="client">Incidentary client.</param>
    /// <param name="maxCommandTextLength">Maximum SQL text length before truncation.</param>
    /// <param name="includeSqlStatement">
    /// When true, captures the SQL command text in <c>db.statement</c>.
    /// Defaults to <see langword="false"/> because SQL text may contain schema
    /// structure that operators prefer not to transmit to third-party backends.
    /// Enable explicitly if you understand and accept this trade-off.
    /// </param>
    public IncidentaryDbCommandInterceptor(
        IIncidentaryClient client,
        int maxCommandTextLength = 1024,
        bool includeSqlStatement = false)
    {
        _client = client;
        _maxCommandTextLength = maxCommandTextLength;
        _includeSqlStatement = includeSqlStatement;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        // We record on completion, not start. Just return.
        return new ValueTask<InterceptionResult<DbDataReader>>(result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        RecordDbQuery(command, eventData.Duration);
        return new ValueTask<DbDataReader>(result);
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        RecordDbQuery(command, eventData.Duration, 500);
    }

    public override Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        RecordDbQuery(command, eventData.Duration, 500);
        return Task.CompletedTask;
    }

    private void RecordDbQuery(DbCommand command, TimeSpan duration, int status = 200)
    {
        var attrs = new Dictionary<string, object>
        {
            ["db.system"] = DetectDbSystem(command)
        };

        if (_includeSqlStatement)
        {
            var commandText = command.CommandText;
            if (commandText.Length > _maxCommandTextLength)
                commandText = commandText[.._maxCommandTextLength] + "...";
            attrs["db.statement"] = commandText;
        }

        var current = IncidentaryActivity.Current;

        _client.RecordEvent(EventTypes.DbQuery, new RecordEventOptions
        {
            Kind = CeKind.Internal,
            Status = status,
            DurationNs = duration.Ticks * 100,
            TraceId = current?.TraceId,
            ParentCeId = current?.CeId,
            EventAttrs = attrs
        });
    }

    private static string DetectDbSystem(DbCommand command)
    {
        var connectionType = command.Connection?.GetType().FullName ?? string.Empty;
        if (connectionType.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            return "postgresql";
        if (connectionType.Contains("SqlConnection", StringComparison.OrdinalIgnoreCase))
            return "mssql";
        if (connectionType.Contains("MySql", StringComparison.OrdinalIgnoreCase))
            return "mysql";
        if (connectionType.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            return "sqlite";
        return "other";
    }
}
