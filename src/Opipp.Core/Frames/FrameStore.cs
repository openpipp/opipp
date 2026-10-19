namespace Opipp.Core.Frames;

/// <summary>
/// Append-only on-disk store for transformed frames. Long captures can retain far more pixel data
/// than fits in RAM (10,000 frames at 1920x1080 mono is ~41 GB), so accepted frames are written here
/// and materialised one at a time when a writer or the stacker needs them.
///
/// The store owns its file and deletes it on <see cref="Dispose"/>: it is scratch space, never an
/// output artifact. Layout is private to this class: a fixed-size metadata header per frame followed
/// by its raw <see cref="ushort"/> samples in little-endian order.
/// </summary>
public sealed class FrameStore : IDisposable
{
    private readonly string _path;
    private readonly FileStream _stream;
    private readonly BinaryWriter _writer;
    private readonly List<Entry> _index = new();
    private readonly byte[] _scratch = new byte[1024 * 1024];
    private long _storedBytes;
    private bool _disposed;

    private readonly record struct Entry(
        long Offset, int Width, int Height, int Channels, int BitDepth, BayerPattern Bayer, int SampleCount);

    public FrameStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        string? directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        _stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read,
            4096, FileOptions.DeleteOnClose);
        _writer = new BinaryWriter(_stream, System.Text.Encoding.UTF8, leaveOpen: true);
    }

    /// <summary>Number of frames written.</summary>
    public int Count => _index.Count;

    /// <summary>Pixel bytes held on disk (excludes per-frame headers).</summary>
    public long StoredBytes => _storedBytes;

    /// <summary>Path of the backing file, for diagnostics.</summary>
    public string Path => _path;

    /// <summary>Writes a frame and returns its handle index.</summary>
    public int Append(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ObjectDisposedException.ThrowIf(_disposed, this);

        long offset = _stream.Position;
        _writer.Write(frame.Width);
        _writer.Write(frame.Height);
        _writer.Write(frame.Channels);
        _writer.Write(frame.BitDepth);
        _writer.Write((byte)frame.Bayer);
        _writer.Write(frame.Index);
        _writer.Write(frame.Samples.Length);

        int byteCount = frame.Samples.Length * sizeof(ushort);
        int written = 0;
        while (written < byteCount)
        {
            int chunk = Math.Min(_scratch.Length, byteCount - written);
            Buffer.BlockCopy(frame.Samples, written, _scratch, 0, chunk);
            _writer.Write(_scratch, 0, chunk);
            written += chunk;
        }

        _index.Add(new Entry(offset, frame.Width, frame.Height, frame.Channels, frame.BitDepth,
            frame.Bayer, frame.Samples.Length));
        _storedBytes += byteCount;
        return _index.Count - 1;
    }

    /// <summary>Materialises a frame from the store. The returned frame is a fresh copy.</summary>
    public Frame Read(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)index >= (uint)_index.Count)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"store holds {_index.Count} frames");

        Entry e = _index[index];
        Flush();

        _stream.Position = e.Offset;

        // Reuse a reader over the same stream: the writer already flushed its buffer.
        using var reader = new BinaryReader(_stream, System.Text.Encoding.UTF8, leaveOpen: true);
        int width = reader.ReadInt32();
        int height = reader.ReadInt32();
        int channels = reader.ReadInt32();
        int bitDepth = reader.ReadInt32();
        var bayer = (BayerPattern)reader.ReadByte();
        long sourceIndex = reader.ReadInt64();
        int sampleCount = reader.ReadInt32();

        if (width != e.Width || height != e.Height || channels != e.Channels || bitDepth != e.BitDepth ||
            sampleCount != e.SampleCount)
        {
            throw new InvalidDataException(
                $"frame {index} header mismatch: expected {e.Width}x{e.Height}x{e.Channels}@{e.BitDepth}, " +
                $"found {width}x{height}x{channels}@{bitDepth}");
        }

        var samples = new ushort[sampleCount];
        int byteCount = sampleCount * sizeof(ushort);
        int read = 0;
        while (read < byteCount)
        {
            int chunk = Math.Min(_scratch.Length, byteCount - read);
            int got = reader.Read(_scratch, 0, chunk);
            if (got <= 0) throw new EndOfStreamException($"frame {index} is truncated");
            Buffer.BlockCopy(_scratch, 0, samples, read, got);
            read += got;
        }

        return new Frame(width, height, channels, bitDepth, samples) { Bayer = bayer, Index = sourceIndex };
    }

    public void Flush()
    {
        _writer.Flush();
        _stream.Flush();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _writer.Dispose();
            _stream.Dispose();
        }
        finally
        {
            try
            {
                if (File.Exists(_path)) File.Delete(_path);
            }
            catch (IOException)
            {
                // Scratch file left behind; the next run uses a fresh name and the OS cleans temp.
            }
        }
    }
}
