namespace Incidentary.Sdk.Buffering;

/// <summary>
/// Thread-safe, fixed-capacity circular buffer. When full, the oldest item is
/// overwritten. Use <see cref="Flush"/> to drain all items in write order.
/// </summary>
internal sealed class RingBuffer<T>
{
    private readonly object _syncRoot = new();
    private readonly T[] _items;
    private int _head;   // next write position
    private int _count;
    private long _totalWritten;
    private long _totalDropped;

    /// <summary>
    /// Creates a new ring buffer with the specified capacity.
    /// </summary>
    /// <param name="capacity">Maximum number of items the buffer can hold. Must be greater than zero.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="capacity"/> is less than or equal to zero.</exception>
    public RingBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be greater than zero.");
        }

        Capacity = capacity;
        _items = new T[capacity];
    }

    /// <summary>Current number of items in the buffer (not capacity).</summary>
    public int Count
    {
        get
        {
            lock (_syncRoot)
            {
                return _count;
            }
        }
    }

    /// <summary>Maximum capacity of the buffer.</summary>
    public int Capacity { get; }

    /// <summary>Total items written since creation (monotonically increasing).</summary>
    public long TotalWritten
    {
        get
        {
            lock (_syncRoot)
            {
                return _totalWritten;
            }
        }
    }

    /// <summary>Total items overwritten due to a full buffer.</summary>
    public long TotalDropped
    {
        get
        {
            lock (_syncRoot)
            {
                return _totalDropped;
            }
        }
    }

    /// <summary>
    /// Writes an item to the next slot. If the buffer is full, the oldest item
    /// is overwritten and <see cref="TotalDropped"/> is incremented.
    /// </summary>
    public void Write(T item)
    {
        lock (_syncRoot)
        {
            if (_count == Capacity)
            {
                _totalDropped++;
            }

            _items[_head] = item;
            _head = (_head + 1) % Capacity;
            _count = Math.Min(_count + 1, Capacity);
            _totalWritten++;
        }
    }

    /// <summary>
    /// Returns all items in write order (oldest to newest) and clears the buffer.
    /// Returns an empty list if the buffer is empty.
    /// </summary>
    public IReadOnlyList<T> Flush()
    {
        lock (_syncRoot)
        {
            if (_count == 0)
            {
                return Array.Empty<T>();
            }

            var result = new T[_count];

            // Oldest item is at (_head - _count + Capacity) % Capacity
            var start = (_head - _count + Capacity) % Capacity;
            for (var i = 0; i < _count; i++)
            {
                result[i] = _items[(start + i) % Capacity];
            }

            // Reset state
            Array.Clear(_items, 0, Capacity);
            _head = 0;
            _count = 0;

            return result;
        }
    }
}
