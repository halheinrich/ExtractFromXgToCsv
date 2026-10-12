using ExtractFromXgToCsv.Services;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The file system as a Local run's destination, scripted: the files are real
/// (<see cref="FileSystemDestination"/>), and a test can make finishing one —
/// its flush on close — fail. No real destination is known to fail there, and
/// the run's handling of it is pinned through this.
/// </summary>
internal sealed class ScriptedDestination : IOutputDestination
{
    /// <summary>The message a scripted flush failure carries.</summary>
    public const string FlushFailure = "The disk is full.";

    /// <summary>Whether flushing a file fails, with <see cref="FlushFailure"/>.</summary>
    public bool FailFlush { get; init; }

    public Stream Replace(string path) => new ScriptedStream(FileSystemDestination.Instance.Replace(path), this);

    /// <summary>A real file stream that fails where its destination is scripted to.</summary>
    private sealed class ScriptedStream(Stream inner, ScriptedDestination script) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => true;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush()
        {
            if (script.FailFlush)
                throw new IOException(FlushFailure);
            inner.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            if (script.FailFlush)
                throw new IOException(FlushFailure);
            return inner.FlushAsync(cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.WriteAsync(buffer, cancellationToken);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.WriteAsync(buffer, offset, count, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}
