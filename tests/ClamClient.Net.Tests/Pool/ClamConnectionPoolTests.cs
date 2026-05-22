using ClamClient.Net.Configuration;
using ClamClient.Net.Pool;

namespace ClamClient.Net.Tests.Pool;

/// <summary>
/// Unit tests for <see cref="ClamConnectionPool"/> pool lifecycle and semaphore accounting.
/// Uses a fake stream factory so no TCP server is required.
/// </summary>
public sealed class ClamConnectionPoolTests
{
    private static CancellationToken TestCt => TestContext.Current.CancellationToken;

    /// <summary>
    /// Verifies that disposing the pool sends the <c>zEND\0</c> command on every idle connection.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_WritesEndCommandToAllIdleConnections()
    {
        var trackingStream = new TrackingStream();
        var (pool, _) = BuildPool(streamFactory: _ => Task.FromResult<Stream>(trackingStream));
        var conn = await pool.RentAsync(TestCt);
        pool.Return(conn);

        await pool.DisposeAsync();

        // ClamConnection.DisposeAsync sends "zEND\0" on healthy connections.
        Assert.Contains("zEND\0", trackingStream.Written);
    }

    /// <summary>
    /// Verifies that renting from a disposed pool throws <see cref="ObjectDisposedException"/>.
    /// </summary>
    [Fact]
    public async Task RentAsync_AfterDisposeAsync_ThrowsObjectDisposedException()
    {
        var (pool, _) = BuildPool();
        await pool.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => pool.RentAsync(TestCt));
    }

    /// <summary>
    /// Verifies that a rent attempt at full capacity throws <see cref="OperationCanceledException"/> when the token expires before a slot is released.
    /// </summary>
    [Fact]
    public async Task RentAsync_AtCapacity_ThrowsOperationCanceledBeforeSlotFrees()
    {
        var (pool, _) = BuildPool(maxConnections: 1);
        await using (pool)
        {
            var held = await pool.RentAsync(TestCt);

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => pool.RentAsync(cts.Token));

            pool.Return(held);
        }
    }

    /// <summary>
    /// Verifies that a waiter blocked at capacity is unblocked and receives the recycled connection once the slot is returned.
    /// </summary>
    [Fact]
    public async Task RentAsync_AtCapacity_UnblocksWhenSlotIsReleased()
    {
        var (pool, factory) = BuildPool(maxConnections: 1);
        await using (pool)
        {
            var first = await pool.RentAsync(TestCt);

            // Start a waiter before releasing the slot.
            var waiter = pool.RentAsync(TestCt);
            Assert.False(waiter.IsCompleted);

            pool.Return(first);

            var second = await waiter;
            pool.Return(second);

            // Both rentals used the same recycled connection; factory called once.
            Assert.Same(first, second);
            Assert.Equal(1, factory.OpenCount);
        }
    }

    /// <summary>
    /// Verifies that a stale idle connection is discarded and a new stream is opened in its place.
    /// </summary>
    [Fact]
    public async Task RentAsync_EvictsStaleIdleConnection_AndOpensNewOne()
    {
        // IdleConnectionTimeout = TimeSpan.Zero → any elapsed time fails the < check, so every idle
        // connection is treated as stale when re-evaluated on the next Rent.
        var (pool, factory) = BuildPool(idleTimeout: TimeSpan.Zero);
        await using (pool)
        {
            var conn = await pool.RentAsync(TestCt);
            pool.Return(conn);

            // Brief delay so LastUsedAt is measurably in the past.
            await Task.Delay(1, TestCt);

            var conn2 = await pool.RentAsync(TestCt);
            pool.Return(conn2);

            Assert.NotSame(conn, conn2);
            Assert.Equal(2, factory.OpenCount);
        }
    }

    /// <summary>
    /// Verifies that a returned connection is dequeued on the next rent without opening a new stream.
    /// </summary>
    [Fact]
    public async Task RentAsync_WhenIdleConnectionAvailable_ReusesItWithoutOpeningNew()
    {
        var (pool, factory) = BuildPool();
        await using (pool)
        {
            var conn = await pool.RentAsync(TestCt);
            pool.Return(conn);

            var conn2 = await pool.RentAsync(TestCt);
            pool.Return(conn2);

            Assert.Same(conn, conn2);
            Assert.Equal(1, factory.OpenCount);
        }
    }

    /// <summary>
    /// Verifies that renting from an empty pool opens exactly one new stream.
    /// </summary>
    [Fact]
    public async Task RentAsync_WhenNoIdleConnection_OpensNewStream()
    {
        var (pool, factory) = BuildPool();
        await using (pool)
        {
            var conn = await pool.RentAsync(TestCt);
            pool.Return(conn);

            Assert.Equal(1, factory.OpenCount);
        }
    }

    /// <summary>
    /// Verifies that <see cref="ClamClientOptions.MaxConnections"/> equal to zero disables the semaphore cap and allows unlimited concurrent rentals.
    /// </summary>
    [Fact]
    public async Task RentAsync_WithMaxConnectionsZero_AllowsConcurrentRentals()
    {
        var (pool, factory) = BuildPool(maxConnections: 0); // unlimited
        await using (pool)
        {
            var conns = await Task.WhenAll(
                pool.RentAsync(TestCt),
                pool.RentAsync(TestCt),
                pool.RentAsync(TestCt));

            foreach (var c in conns)
            {
                pool.Return(c);
            }

            Assert.Equal(3, factory.OpenCount);
        }
    }

    /// <summary>
    /// Verifies that returning an unhealthy connection releases its semaphore slot so a subsequent rent does not block.
    /// </summary>
    [Fact]
    public async Task Return_UnhealthyConnection_ReleasesSlotSoSubsequentRentSucceeds()
    {
        var (pool, _) = BuildPool(maxConnections: 1);
        await using (pool)
        {
            var conn = await pool.RentAsync(TestCt);
            conn.Invalidate();
            pool.Return(conn); // slot must be released here

            // Without the release this would block and cancel via TestCt.
            var conn2 = await pool.RentAsync(TestCt);
            pool.Return(conn2);
        }
    }

    /// <summary>
    /// Builds a <see cref="ClamConnectionPool"/> wired to a <see cref="FakeStreamFactory"/> for use in tests.
    /// </summary>
    /// <param name="maxConnections">Semaphore cap; <c>0</c> means unlimited.</param>
    /// <param name="idleTimeout">Idle eviction threshold; defaults to 30 minutes.</param>
    /// <param name="streamFactory">Optional custom stream factory; falls back to the fake factory.</param>
    /// <returns>The constructed pool and the fake factory (for assertion purposes).</returns>
    private static (ClamConnectionPool pool, FakeStreamFactory factory) BuildPool(
        int maxConnections = 10,
        TimeSpan? idleTimeout = null,
        Func<CancellationToken, Task<Stream>>? streamFactory = null)
    {
        var factory = new FakeStreamFactory();
        var options = new ClamClientOptions
        {
            MaxConnections = maxConnections,
            IdleConnectionTimeout = idleTimeout ?? TimeSpan.FromMinutes(30),
            Timeout = TimeSpan.FromSeconds(5)
        };

        var pool = new ClamConnectionPool(
            options,
            streamFactory ?? factory.OpenAsync);

        return (pool, factory);
    }

    // ---------------------------------------------------------------------------
    // Inner fakes
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Tracks the number of streams opened and produces in-memory streams.
    /// </summary>
    private sealed class FakeStreamFactory
    {
        private int _openCount;

        /// <summary>
        /// Number of streams opened by <see cref="OpenAsync"/>.
        /// </summary>
        public int OpenCount => _openCount;

        /// <summary>
        /// Opens a new <see cref="TrackingStream"/> and increments <see cref="OpenCount"/>.
        /// </summary>
        public Task<Stream> OpenAsync(CancellationToken _)
        {
            Interlocked.Increment(ref _openCount);
            return Task.FromResult<Stream>(new TrackingStream());
        }
    }

    /// <summary>
    /// A write-only in-memory stream that records all written bytes as ASCII text.
    /// </summary>
    private sealed class TrackingStream : Stream
    {
        private readonly MemoryStream _ms = new();

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException(); set => throw new NotSupportedException();
        }

        /// <summary>
        /// All bytes written to this stream, decoded as ASCII.
        /// </summary>
        public string Written => System.Text.Encoding.ASCII.GetString(_ms.ToArray());

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) => 0; // simulates no data available

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            _ms.Write(buffer, offset, count);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            _ms.Write(buffer, offset, count);
            return Task.CompletedTask;
        }
    }
}