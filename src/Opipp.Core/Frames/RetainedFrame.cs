namespace Opipp.Core.Frames;

/// <summary>
/// A frame held for later output: either inline in memory, or in a <see cref="FrameStore"/> when the
/// run exceeds its retention budget. Accessing <see cref="Frame"/> yields a usable frame either way,
/// so the pipeline keeps one representation while small runs stay entirely in memory.
///
/// Deliberately a value type: it is stored inline in the per-frame tuple that selection and the
/// writers already pass around, so adopting it does not allocate a second list.
/// </summary>
public readonly struct RetainedFrame
{
    private readonly FrameStore? _store;
    private readonly Frame? _inline;
    private readonly int _index;

    private RetainedFrame(FrameStore? store, Frame? inline, int index)
    {
        _store = store;
        _inline = inline;
        _index = index;
    }

    /// <summary>Wraps a frame that is still resident in memory.</summary>
    public static RetainedFrame Inline(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return new RetainedFrame(null, frame, -1);
    }

    /// <summary>Points at a frame already written to <paramref name="store"/>.</summary>
    public static RetainedFrame Stored(FrameStore store, int index)
    {
        ArgumentNullException.ThrowIfNull(store);
        if ((uint)index >= (uint)store.Count)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"store holds {store.Count} frames");
        return new RetainedFrame(store, null, index);
    }

    /// <summary>Arrival order within the run; -1 for inline frames.</summary>
    public int Index => _index;

    /// <summary>True when this handle points at a usable frame.</summary>
    public bool IsValid => _inline is not null || _store is not null;

    /// <summary>True when the pixels live on disk rather than in managed memory.</summary>
    public bool IsStored => _store is not null;

    /// <summary>Materialises the frame. Inline frames are returned as-is; stored ones are re-read.</summary>
    public Frame Frame => _inline ?? (_store is null
        ? throw new InvalidOperationException("RetainedFrame is uninitialised")
        : _store.Read(_index));

    /// <summary>Pixel bytes still held in managed memory by this handle.</summary>
    public long ResidentBytes => _inline is null ? 0 : _inline.Samples.Length * sizeof(ushort);
}
