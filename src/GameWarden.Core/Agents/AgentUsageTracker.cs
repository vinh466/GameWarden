using System.Threading.Channels;
using GameWarden.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace GameWarden.Core.Agents;

public sealed record AgentUsage(string ClientId, string TokenId, DateTimeOffset At, string? Ip, string? Version);

/// <summary>
/// Writes "last seen" without putting a database write on every Agent request. Bounded: under heavy log tailing an
/// old sample is dropped rather than delaying the call.
/// </summary>
public sealed class AgentUsageTracker : BackgroundService
{
    private readonly Channel<AgentUsage> _queue = Channel.CreateBounded<AgentUsage>(new BoundedChannelOptions(256)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });

    private readonly IDbContextFactory<GameWardenDb> _db;
    private readonly ILogger<AgentUsageTracker> _logger;

    public AgentUsageTracker(IDbContextFactory<GameWardenDb> db, ILogger<AgentUsageTracker> logger)
    {
        _db = db;
        _logger = logger;
    }

    public void Track(AgentUsage usage) => _queue.Writer.TryWrite(usage);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var usage in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var context = await _db.CreateDbContextAsync(stoppingToken);
                var token = await context.AgentTokens.SingleOrDefaultAsync(row => row.Id == usage.TokenId, stoppingToken);
                if (token is not null)
                {
                    token.LastUsedAt = usage.At;
                    token.LastUsedIp = usage.Ip;
                }

                var client = await context.AgentClients.SingleOrDefaultAsync(row => row.Id == usage.ClientId, stoppingToken);
                if (client is not null)
                {
                    client.LastSeenAt = usage.At;
                    client.LastSeenIp = usage.Ip;
                    client.LastSeenVersion = usage.Version;
                }

                await context.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not record Agent usage.");
            }
        }
    }
}
