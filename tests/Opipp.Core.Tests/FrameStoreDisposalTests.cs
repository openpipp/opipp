using Opipp.Core.Frames;
using Xunit;

namespace Opipp.Core.Tests;

/// <summary>
/// The scratch store owns a file, and disposing it must close that file however the writer behaves:
/// a flush that throws (a full disk) must not leave the DeleteOnClose handle to the finalizer, or a
/// cancelled run keeps gigabytes of a large capture on disk. The tests supply the backing stream so
/// a close failure can be raised deliberately and observed exactly.
/// </summary>
public sealed class FrameStoreDisposalTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "opipp-dispose-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Dispose_WhenTheStreamCloseThrows_StillDisposesItOnceAndSurfacesTheFailure()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "store.bin");
            var stream = new ThrowingCloseStream(path);
            var store = new FrameStore(path, _ => stream);
            store.Append(new Frame(2, 2, 1, 16));

            var error = Assert.Throws<IOException>(() => store.Dispose());

            Assert.Equal("stream close failed", error.Message);
            Assert.Equal(1, stream.DisposeCount);

            // Disposal is one-shot: the failed close is not retried, and the store is already closed
            // for callers, so a retry cannot reach a half-closed stream.
            store.Dispose();
            Assert.Equal(1, stream.DisposeCount);
            Assert.Throws<ObjectDisposedException>(() => store.Append(new Frame(2, 2, 1, 16)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Dispose_WhenTheWriterFlushThrows_StillClosesTheStream()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "store.bin");
            var stream = new FailingFlushStream(path);
            var store = new FrameStore(path, _ => stream);
            store.Append(new Frame(2, 2, 1, 16));

            // BinaryWriter.Dispose flushes through the stream; that is the writer failure that used
            // to skip the stream close entirely.
            var error = Assert.Throws<IOException>(() => store.Dispose());

            Assert.Equal("flush failed", error.Message);
            Assert.Equal(1, stream.DisposeCount);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Dispose_WhenTheWriterAndTheStreamBothThrow_ReportsBothFailures()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "store.bin");
            var stream = new FailingFlushAndCloseStream(path);
            var store = new FrameStore(path, _ => stream);
            store.Append(new Frame(2, 2, 1, 16));

            var error = Assert.Throws<AggregateException>(() => store.Dispose());

            Assert.Equal(1, stream.DisposeCount);
            Assert.Collection(error.InnerExceptions,
                inner => Assert.Equal("flush failed", inner.Message),
                inner => Assert.Equal("stream close failed", inner.Message));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>A stream whose close fails after the handle itself has been released.</summary>
    private sealed class ThrowingCloseStream(string path)
        : FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.DeleteOnClose)
    {
        public int DisposeCount { get; private set; }

        protected override void Dispose(bool disposing)
        {
            DisposeCount++;
            base.Dispose(disposing);
            if (disposing) throw new IOException("stream close failed");
        }
    }

    /// <summary>A stream whose first flush fails, standing in for a writer flush against a full disk.</summary>
    private sealed class FailingFlushStream(string path)
        : FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.DeleteOnClose)
    {
        private int _failuresLeft = 1;

        public int DisposeCount { get; private set; }

        public override void Flush()
        {
            if (_failuresLeft-- > 0) throw new IOException("flush failed");
            base.Flush();
        }

        protected override void Dispose(bool disposing)
        {
            DisposeCount++;
            base.Dispose(disposing);
        }
    }

    /// <summary>A stream that fails on both the writer's flush and its own close.</summary>
    private sealed class FailingFlushAndCloseStream(string path)
        : FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.DeleteOnClose)
    {
        private int _failuresLeft = 1;

        public int DisposeCount { get; private set; }

        public override void Flush()
        {
            if (_failuresLeft-- > 0) throw new IOException("flush failed");
            base.Flush();
        }

        protected override void Dispose(bool disposing)
        {
            DisposeCount++;
            base.Dispose(disposing);
            if (disposing) throw new IOException("stream close failed");
        }
    }
}
