using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Data.Common;

namespace Blog.Infrastructure.Persistence.Interceptors
{
    /// <summary>
    /// 慢查询拦截器：EF Core 命令执行超过阈值（默认 500ms）时记录警告日志，包含 SQL 文本。
    /// </summary>
    public class SlowQueryInterceptor : DbCommandInterceptor
    {
        private static readonly TimeSpan Threshold = TimeSpan.FromMilliseconds(500);

        private readonly ILogger<SlowQueryInterceptor> _logger;

        public SlowQueryInterceptor(ILogger<SlowQueryInterceptor> logger)
        {
            _logger = logger;
        }

        public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
        {
            LogIfSlow(command, eventData);
            return base.ReaderExecuted(command, eventData, result);
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            LogIfSlow(command, eventData);
            return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }

        public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
        {
            LogIfSlow(command, eventData);
            return base.NonQueryExecuted(command, eventData, result);
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            LogIfSlow(command, eventData);
            return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
        }

        public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
        {
            LogIfSlow(command, eventData);
            return base.ScalarExecuted(command, eventData, result);
        }

        public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
        {
            LogIfSlow(command, eventData);
            return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
        }

        private void LogIfSlow(DbCommand command, CommandExecutedEventData eventData)
        {
            if (eventData.Duration < Threshold) return;

            _logger.LogWarning("慢查询 {Duration}ms: {Sql}",
                eventData.Duration.TotalMilliseconds, command.CommandText);
        }
    }
}
