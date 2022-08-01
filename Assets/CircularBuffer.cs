using System;
using System.Threading;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// Represents a circular buffer of up to Capacity of type T unmanaged items (e.g. floats)
/// </summary>
/// <typeparam name="T">The type of item</typeparam>
public class CircularBuffer<T> where T : unmanaged
{
    protected T[] Buffer;
    protected int Head;
    protected int Tail;
    protected int InternalCapacity;
    protected int InternalSize;

    public CircularBuffer(int capacity)
    {
        Buffer = new T[capacity];
        InternalCapacity = capacity;
        InternalSize = 0;
        Head = 0;
        Tail = 0;
    }

    /// <summary>
    /// Gets the total capacity of allocated buffer
    /// </summary>
    public int Capacity => InternalCapacity;

    /// <summary>
    /// Gets the current size used in the buffer, 0 <= CurrentSize <= Capacity
    /// </summary>
    public int CurrentSize => InternalSize;

    /// <summary>
    /// Gets or sets the value by logical index
    /// </summary>
    /// <param name="index">The logical index of stored value</param>
    /// <returns>Returns the value stored via logical index</returns>
    public T this[int index]
    {
        get { return GetByIndex(index); }
        set { SetByIndex(index, value); }
    }

    /// <summary>
    /// Gets the value by logical index
    /// </summary>
    /// <param name="index">The logical index of stored value</param>
    /// <returns>Returns the value stored via index</returns>
    protected T GetByIndex(int index)
    {
        if (index < 0)
            throw new IndexOutOfRangeException();

        int tillEndCount = GetTillEndCount();

        T item = Head - Tail > index || tillEndCount > index
            ? Buffer[Tail + index]
            : Buffer[index - tillEndCount];

        return item;
    }

    /// <summary>
    /// Sets the value by index
    /// </summary>
    /// <param name="index">The logical index of stored value</param>
    /// <param name="value">The value to store via index</param>
    protected void SetByIndex(int index, T value)
    {
        if (index < 0)
            throw new IndexOutOfRangeException();

        int tillEndCount = GetTillEndCount();
        int bufferIndex = Head - Tail > index || tillEndCount > index
            ? Tail + index
            : index - tillEndCount;

        Buffer[bufferIndex] = value;
    }

    /// <summary>
    /// Gets the size till the end of buffer
    /// </summary>
    /// <returns>The integer value</returns>
    protected int GetTillEndCount() => Tail < Head
                ? InternalCapacity - Head
                : InternalCapacity - Tail;

    /// <summary>
    /// Reads the specified amount of data from buffer
    /// </summary>
    /// <param name="buffer">The destination buffer to write data to</param>
    /// <param name="offset">The offset in the destination buffer to start reading from</param>
    /// <param name="count">The amount of data to be read</param>
    /// <returns>Return amount of data read and -1 if no data was read</returns>
    public virtual int Read(T[] buffer, int offset, int count)
    {
        // caller shouldn't ask for more elements than the whole buffer's capacity, that's an error
        if (InternalCapacity < count)
            throw new InvalidOperationException("The destination count is larget than the capacity.");

        // if there is buffer underrun then we can't provide count elements
        // since we return the count of copied elements, the caller can pad out the 
        // rest with zeroes if needed
        // TODO remove after testing, caller can watch for this and log if required
        if (InternalSize < count)
        {
            Debug.Log("Buffer underrun");
        }

        // # - used space
        // _ - free space
        // h - head marker
        // t - tail marker
        // 0 - buffer start
        // 1 - buffer end
        // case 1: 0#####h________t#####1
        // case 2: 0t##########h________1
        // case 3: 0_____t########h_____1
        int tillEndCount = GetTillEndCount();
        if (Tail < Head || tillEndCount > count)
        {
            // case 2, 3, or 1
            Array.Copy(Buffer, Tail, buffer, offset, count);
            Interlocked.Add(ref Tail, count);
        }
        else
        {
            // case 1
            Array.Copy(Buffer, Tail, buffer, offset, tillEndCount);
            Array.Copy(Buffer, 0, buffer, offset + tillEndCount, count - tillEndCount);
            Interlocked.Exchange(ref Tail, count - tillEndCount);
        }

        // subtract the count read from the size of buffer
        Interlocked.Add(ref InternalSize, -count);
        return count;
    }

    /// <summary>
    /// Writes the specified amount of data to buffer
    /// </summary>
    /// <param name="buffer">The source buffer to read data from</param>
    /// <param name="offset">The offset in the source buffer to start reading from</param>
    /// <param name="count">The amount of data to be written</param>
    public virtual void Write(NativeArray<T> buffer, int offset, int count)
    {
        // we need to check if there is enough space left for new buffer
        if (InternalCapacity - InternalSize < count)
        {
            throw new Exception("No free space. Extension is not allowed.");
        }

        // we should append buffer if there is enough space between _head and tail or _head and end of array
        // # - used space
        // _ - free space
        // h - head marker
        // t - tail marker
        // 0 - buffer start
        // 1 - buffer end
        // case 1: 0#####h________t#####1
        // case 2: 0t##########h________1
        // case 3: 0_____t########h_____1
        int tillEndCount = GetTillEndCount();
        if (Head < Tail || tillEndCount > count)
        {
            // case 1 or 2
            // TODO PH - BUG: Out of bounds on copy.
            Array.Copy(buffer, offset, Buffer, Head, count);
            Interlocked.Add(ref Head, count);
        }
        else
        {
            // case 3
            Array.Copy(buffer, offset, Buffer, Head, tillEndCount);
            Array.Copy(buffer, offset + tillEndCount, Buffer, 0, count - tillEndCount);
            Interlocked.Exchange(ref Head, count - tillEndCount);
        }

        // add the count written to the size of buffer
        Interlocked.Add(ref InternalSize, count);
    }

    /// <summary>
    /// Copies the buffer to new buffer without clearing what was read
    /// </summary>
    /// <returns>The copy of current buffer</returns>
    public virtual T[] ToArray()
    {
        var buffer = new T[InternalCapacity];

        if (Tail < Head)
        {
            Array.Copy(Buffer, Tail, buffer, Tail, Head - Tail);
        }
        else
        {
            Array.Copy(Buffer, Tail, buffer, Tail, InternalCapacity - Tail);
            Array.Copy(Buffer, 0, buffer, 0, Head);
        }

        return buffer;
    }
}
