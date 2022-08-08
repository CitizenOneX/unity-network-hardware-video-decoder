using System;
using Unity.Collections;

/// <summary>
/// A circular buffer of arrays of unmanaged values, e.g. circular buffer of 20 float[256] arrays
/// </summary>
/// <typeparam name="T"></typeparam>
public class CircularBuffer<T> where T : unmanaged
{
    private T[][] _buffer { get; set; }
    private int _writeIndex, _readIndex, _content, _capacity;

    public bool IsEmpty { get => _content == 0;  }
    public bool IsFull { get => _content == _capacity; }

    public int QueueLength { get => _content; }

    public CircularBuffer(int capacity, int bufSize)
    {
        _capacity = capacity;
        _buffer = new T[capacity][];
        for (int i=0; i<capacity; i++)
        {
            _buffer[i] = new T[bufSize];
        }
        Clear();
    }

    public T[] Read()
    {
        if (_content == 0) throw new InvalidOperationException("Cannot read from empty buffer");
        int return_readIndex = _readIndex++;
        _readIndex %= _capacity;
        _content--;
        return _buffer[return_readIndex];
    }

    public T[] Peek()
    {
        if (_content == 0) throw new InvalidOperationException("Cannot read from empty buffer");
        return _buffer[_readIndex];
    }

    public void Write(NativeArray<T> value)
    {
        if (_content == _capacity) throw new InvalidOperationException("Cannot write to full buffer, use Overwrite instead");
        value.CopyTo(_buffer[_writeIndex++]);
        _writeIndex %= _capacity;
        _content++;
    }

    public void Overwrite(NativeArray<T> value)
    {
        if (_content == _capacity) Read();
        Write(value);
    }

    // TODO could probably loop through and zero out all the little bufs
    public void Clear() => _writeIndex = _readIndex = _content = 0;
}