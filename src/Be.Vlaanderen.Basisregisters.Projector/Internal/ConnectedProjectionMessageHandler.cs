namespace Be.Vlaanderen.Basisregisters.Projector.Internal
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Autofac.Features.OwnedInstances;
    using ConnectedProjections;
    using Exceptions;
    using Extensions;
    using Microsoft.Extensions.Logging;
    using ProjectionHandling.Connector;
    using ProjectionHandling.Runner;
    using ProjectionHandling.SqlStreamStore;
    using SqlStreamStore.Streams;

    internal class ConnectedProjectionMessageHandler<TContext> where TContext : RunnerDbContext<TContext>
    {
        private readonly ConnectedProjectionName _runnerName;
        private readonly ConnectedProjector<TContext> _projector;
        private readonly EnvelopeFactory _envelopeFactory;
        private readonly ILogger _logger;

        public ConnectedProjectionMessageHandler(
            ConnectedProjectionName runnerName,
            ConnectedProjectionHandler<TContext>[] handlers,
            EnvelopeFactory envelopeFactory,
            ILoggerFactory loggerFactory)
        {
            _runnerName = runnerName;
            _projector = new ConnectedProjector<TContext>(Resolve.WhenEqualToHandlerMessageType(handlers));
            _envelopeFactory = envelopeFactory ?? throw  new ArgumentNullException(nameof(envelopeFactory));
            _logger = loggerFactory?.CreateLogger<ConnectedProjectionMessageHandler<TContext>>() ?? throw new ArgumentNullException(nameof(loggerFactory));
        }

        public async Task HandleAsync(
            IEnumerable<StreamMessage> messages,
            Func<Owned<TContext>> contextFactory,
            CancellationToken cancellationToken)
        {
            long? lastProcessedMessagePosition = null;
            using (var context = contextFactory())
            {
                try
                {
                    var completeMessageInProcess = CancellationToken.None;
                    var runnerPosition = await context.Value.GetRunnerPositionAsync(_runnerName, completeMessageInProcess);
                    foreach (var message in messages)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        if (runnerPosition.HasValue && message.Position <= runnerPosition)
                            continue;

                        var expectedPositionToProcess = (lastProcessedMessagePosition ?? runnerPosition ?? -1L) + 1;
                        if (message.Position > expectedPositionToProcess)
                            throw new Exception($"Messages skipped, expected messages for position: {expectedPositionToProcess}");

                        _logger.LogInformation(
                            "{RunnerName} handling {MessageType} at {Position}",
                            _runnerName,
                            message.Type,
                            message.Position);

                        _logger.LogTrace(
                            "[{Latency}] [POS {Position}] [{StreamId}] [{Type}]",
                            CalculateNotVeryPreciseLatency(message),
                            message.Position,
                            message.StreamId,
                            message.Type);

                        lastProcessedMessagePosition = message.Position;
                        await _projector.ProjectAsync(context.Value, _envelopeFactory.Create(message), completeMessageInProcess);
                    }

                    if (lastProcessedMessagePosition.HasValue)
                        await context.Value.UpdateProjectionStateAsync(
                            _runnerName,
                            lastProcessedMessagePosition.Value,
                            completeMessageInProcess);

                    await context.Value.SaveChangesAsync(completeMessageInProcess);
                }
                catch (TaskCanceledException){ }
                catch (Exception exception)
                {
                    throw new ConnectedProjectionMessageHandlingException(exception, _runnerName, lastProcessedMessagePosition);
                }
            }
        }

        public async Task HandleAsync(
            StreamMessage message,
            Func<Owned<TContext>> contextFactory,
            CancellationToken cancellationToken)
        {
            await HandleAsync(new[] {message}, contextFactory, cancellationToken);
        }

        private static TimeSpan CalculateNotVeryPreciseLatency(StreamMessage message)
        {
            // This is not very precise since we could have differing clocks, and should be seen as merely informational
            return DateTime.UtcNow - message.CreatedUtc;
        }
    }
}