using ExtractFromXgToCsv.Services;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// The file system as a Local run's destination, scripted: the files are real
/// (<see cref="FileSystemDestination"/>), and a test can make finishing a file
/// (its flush), a chosen file's write partway through, a chosen file's close,
/// or a removal fail. No real destination is known to fail there, and the
/// run's handling of each is pinned through this.
/// </summary>
internal sealed class ScriptedDestination : IOutputDestination
{
    /// <summary>The message a scripted flush failure carries.</summary>
    public const string FlushFailure = "The disk is full.";

    /// <summary>The message a scripted write failure carries.</summary>
    public const string WriteFailure = "The device is not ready.";

    /// <summary>The message a scripted close failure carries.</summary>
    public const string CloseFailure = "The network name is no longer available.";

    /// <summary>The message a scripted removal failure carries.</summary>
    public const string RemovalFailure = "The file is in use by another process.";

    /// <summary>Whether flushing any file fails, with <see cref="FlushFailure"/>.</summary>
    public bool FailFlush { get; init; }

    /// <summary>The file whose write fails partway through — after half its bytes — with <see cref="WriteFailure"/>.</summary>
    public string? FailWriteOf { get; init; }

    /// <summary>The file whose close fails, once its handle is released, with <see cref="CloseFailure"/>.</summary>
    public string? FailCloseOf { get; init; }

    /// <summary>Whether removing a file fails, with <see cref="RemovalFailure"/>, removing nothing.</summary>
    public bool FailRemoval { get; init; }

    /// <summary>The files removed, by name, in order.</summary>
    public List<string> Removed { get; } = [];

    public Stream Replace(string path) =>
        new ScriptedStream(FileSystemDestination.Instance.Replace(path), this, Path.GetFileName(path));

    public Stream CreateNew(string path) =>
        new ScriptedStream(FileSystemDestination.Instance.CreateNew(path), this, Path.GetFileName(path));

    public void Delete(string path)
    {
        if (FailRemoval)
            throw new IOException(RemovalFailure);
        FileSystemDestination.Instance.Delete(path);
        Removed.Add(Path.GetFileName(path));
    }

    /// <summary>A real file stream that fails where its destination is scripted to.</summary>
    private sealed class ScriptedStream(Stream inner, ScriptedDestination script, string name) : Stream
    {
        private bool _disposed;

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

        public override void Write(byte[] buffer, int offset, int count) =>
            WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (name == script.FailWriteOf)
            {
                await inner.WriteAsync(buffer[..(buffer.Length / 2)], cancellationToken);
                throw new IOException(WriteFailure);
            }
            await inner.WriteAsync(buffer, cancellationToken);
        }

        // The handle is released first, as a real file stream's failed close
        // releases it; then the scripted failure.
        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                inner.Dispose();
                if (name == script.FailCloseOf)
                    throw new IOException(CloseFailure);
            }
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }
    }
}
