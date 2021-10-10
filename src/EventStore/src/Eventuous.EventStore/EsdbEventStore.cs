using Microsoft.Extensions.Logging;

namespace Eventuous.EventStore;

[PublicAPI]
public class EsdbEventStore : IEventStore {
    readonly ILogger<EsdbEventStore>? _logger;
    readonly EventStoreClient         _client;

    public EsdbEventStore(EventStoreClient client, ILogger<EsdbEventStore>? logger) {
        _logger = logger;
        _client = Ensure.NotNull(client, nameof(client));
    }

    public EsdbEventStore(EventStoreClientSettings clientSettings, ILogger<EsdbEventStore>? logger)
        : this(new EventStoreClient(Ensure.NotNull(clientSettings, nameof(clientSettings))), logger) { }

    public Task<AppendEventsResult> AppendEvents(
        StreamName                       stream,
        ExpectedStreamVersion            expectedVersion,
        IReadOnlyCollection<StreamEvent> events,
        CancellationToken                cancellationToken
    ) {
        var proposedEvents = events.Select(ToEventData);

        var resultTask = expectedVersion == ExpectedStreamVersion.NoStream
            ? _client.AppendToStreamAsync(
                stream,
                StreamState.NoStream,
                proposedEvents,
                cancellationToken: cancellationToken
            ) : AnyOrNot(
                expectedVersion,
                () => _client.AppendToStreamAsync(
                    stream,
                    StreamState.Any,
                    proposedEvents,
                    cancellationToken: cancellationToken
                ),
                () => _client.AppendToStreamAsync(
                    stream,
                    expectedVersion.AsStreamRevision(),
                    proposedEvents,
                    cancellationToken: cancellationToken
                )
            );

        return TryExecute(
            async () => {
                var result = await resultTask.NoContext();

                return new AppendEventsResult(
                    result.LogPosition.CommitPosition,
                    result.NextExpectedStreamRevision.ToInt64()
                );
            }
        );

        static EventData ToEventData(StreamEvent streamEvent)
            => new(
                Uuid.NewUuid(),
                streamEvent.EventType,
                streamEvent.Data,
                streamEvent.Metadata
            );
    }

    public Task<StreamEvent[]> ReadEvents(
        StreamName         stream,
        StreamReadPosition start,
        int                count,
        CancellationToken  cancellationToken
    ) {
        var read = _client.ReadStreamAsync(
            Direction.Forwards,
            stream,
            start.AsStreamPosition(),
            count,
            cancellationToken: cancellationToken
        );

        return TryExecute(
            async () => {
                var resolvedEvents = await read.ToArrayAsync(cancellationToken).NoContext();
                return ToStreamEvents(resolvedEvents);
            }
        );
    }

    public Task<StreamEvent[]> ReadEventsBackwards(
        StreamName        stream,
        int               count,
        CancellationToken cancellationToken
    ) {
        var read = _client.ReadStreamAsync(
            Direction.Backwards,
            stream,
            StreamPosition.End,
            count,
            cancellationToken: cancellationToken
        );

        return TryExecute(
            async () => {
                var resolvedEvents = await read.ToArrayAsync(cancellationToken).NoContext();
                return ToStreamEvents(resolvedEvents);
            }
        );
    }

    public async Task ReadStream(
        StreamName          stream,
        StreamReadPosition  start,
        Action<StreamEvent> callback,
        CancellationToken   cancellationToken
    ) {
        var read = _client.ReadStreamAsync(
            Direction.Forwards,
            stream,
            start.AsStreamPosition(),
            cancellationToken: cancellationToken
        );

        await TryExecute(
            async () => {
                await foreach (var re in read.IgnoreWithCancellation(cancellationToken)) {
                    callback(ToStreamEvent(re));
                }

                return 1;
            }
        );
    }

    public Task TruncateStream(
        StreamName             stream,
        StreamTruncatePosition truncatePosition,
        ExpectedStreamVersion  expectedVersion,
        CancellationToken      cancellationToken
    ) {
        var meta = new StreamMetadata(truncateBefore: truncatePosition.AsStreamPosition());

        return TryExecute(
            () => AnyOrNot(
                expectedVersion,
                () => _client.SetStreamMetadataAsync(
                    stream,
                    StreamState.Any,
                    meta,
                    cancellationToken: cancellationToken
                ),
                () => _client.SetStreamMetadataAsync(
                    stream,
                    expectedVersion.AsStreamRevision(),
                    meta,
                    cancellationToken: cancellationToken
                )
            )
        );
    }

    public Task DeleteStream(
        StreamName            stream,
        ExpectedStreamVersion expectedVersion,
        CancellationToken     cancellationToken
    ) => TryExecute(
        () => AnyOrNot(
            expectedVersion,
            () => _client.SoftDeleteAsync(
                stream,
                StreamState.Any,
                cancellationToken: cancellationToken
            ),
            () => _client.SoftDeleteAsync(
                stream,
                expectedVersion.AsStreamRevision(),
                cancellationToken: cancellationToken
            )
        )
    );

    async Task<T> TryExecute<T>(Func<Task<T>> func) {
        try {
            return await func();
        }
        catch (StreamNotFoundException) {
            _logger?.LogError("Stream {Stream} not found", stream);
            throw new Exceptions.StreamNotFound(stream);
        }
        catch (Exception ex) {
            _logger?.LogError(ex, "Unable to read {Count} events from {Stream} backwards", count, stream);
            throw;
        }
    }

    static Task<T> AnyOrNot<T>(
        ExpectedStreamVersion version,
        Func<Task<T>>         whenAny,
        Func<Task<T>>         otherwise
    )
        => version == ExpectedStreamVersion.Any ? whenAny() : otherwise();

    static StreamEvent ToStreamEvent(ResolvedEvent resolvedEvent)
        => new(
            resolvedEvent.Event.EventType,
            resolvedEvent.Event.Data.ToArray(),
            resolvedEvent.Event.Metadata.ToArray(),
            resolvedEvent.Event.ContentType,
            resolvedEvent.OriginalEventNumber.ToInt64()
        );

    static StreamEvent[] ToStreamEvents(ResolvedEvent[] resolvedEvents)
        => resolvedEvents.Select(ToStreamEvent).ToArray();
}