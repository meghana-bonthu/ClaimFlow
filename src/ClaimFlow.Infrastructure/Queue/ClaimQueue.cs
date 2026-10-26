using System.Threading.Channels;
using ClaimFlow.Application.Abstractions;
using ClaimFlow.Application.Services;
using ClaimFlow.Domain.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Infrastructure.Queue;

/// <summary>
/// In-process queue backed by System.Threading.Channels. The <see cref="IClaimQueue"/> abstraction means it can be
/// swapped for Azure Service Bus or Kafka without touching application code.
/// </summary>
public sealed class ChannelClaimQueue : IClaimQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<Guid> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(Guid claimId, CancellationToken ct) => _channel.Writer.WriteAsync(claimId, ct);
}

/// <summary>
/// Single-consumer adjudication worker. One reader keeps member accumulator updates serialized.
/// On startup it re-queues any claims left in Received state (e.g. after a crash/restart).
/// </summary>
public sealed class AdjudicationWorker(
    ChannelClaimQueue queue,
    IServiceScopeFactory scopes,
    ILogger<AdjudicationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverAsync(stoppingToken);

        await foreach (var claimId in queue.Reader.ReadAllAsync(stoppingToken))
        {
            using var scope = scopes.CreateScope();
            using var _ = logger.BeginScope(new Dictionary<string, object> { ["ClaimId"] = claimId });
            try
            {
                var service = scope.ServiceProvider.GetRequiredService<AdjudicationService>();
                var status = await service.AdjudicateAsync(claimId, stoppingToken);
                logger.LogInformation("Claim {ClaimId} adjudicated with status {Status}", claimId, status);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Claim stays in Received and is picked up again on next restart.
                logger.LogError(ex, "Adjudication failed for claim {ClaimId}", claimId);
            }
        }
    }

    private async Task RecoverAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var claims = scope.ServiceProvider.GetRequiredService<IClaimRepository>();
            var stranded = await claims.GetIdsByStatusAsync(ClaimStatus.Received, ct);
            foreach (var id in stranded) await queue.EnqueueAsync(id, ct);
            if (stranded.Count > 0) logger.LogInformation("Re-queued {Count} received claims on startup", stranded.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not recover received claims on startup");
        }
    }
}
