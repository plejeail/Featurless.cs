namespace Featurless.HashTable;


#region

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

#endregion

// PERF:
// - _entries -> size power of 2:
//      * %,/ operators way quicker
//      * at start fix to the next power of 2
//      * growth factor -> 2 (shift to the next power of two

/// <inheritdoc cref="System.Collections.Generic.IReadOnlyDictionary{TKey,TValue}" />
/// <summary>
///     Dictionary alternative, IDictionary compatible. When to use it:
///     <list type="bullet">
///         <item> Low Load Factor and more than 50% success. </item>
///         <item> High Load Factor and more than 50% success. </item>
///         <item> High Load Factor and more writes than reads and dense distribution of keys. </item>
///         <item> High Load Factor and more reads than writes and dense distribution with few failed lookup. </item>
///     </list>
/// </summary>
/// <typeparam name="TKey"> Type of the keys </typeparam>
/// <typeparam name="TValue"> Type of the values </typeparam>
[StructLayout(LayoutKind.Sequential)]
public sealed class LinearTable<TKey, TValue>
        : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>, ISerializable
{
    void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[]? array, int index) {
        ArgumentNullException.ThrowIfNull(array);

        if (index > array.Length) {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (array.Length - index < Count) {
            throw new ArgumentException("Array too small to contains all values");
        }

        for (int i = 0; i < _entries.Length; ++i) {
            if (_entries[i].probeSequentialLength > -1) {
                array[index++] = new KeyValuePair<TKey, TValue>(_entries[i].key, _entries[i].value);
            }
        }
    }

    /// <inheritdoc />
    /// <summary>
    ///     Implements the <see cref="T:System.Runtime.Serialization.ISerializable" /> interface and returns the data
    ///     needed to serialize the <see cref="T:HashTable.LinearTable`2" /> instance.
    /// </summary>
    /// <param name="info">
    ///     A <see cref="T:System.Runtime.Serialization.SerializationInfo" /> object that contains the
    ///     information required to serialize the <see cref="T:HashTable.LinearTable`2" /> instance.
    /// </param>
    /// <param name="context">
    ///     A <see cref="T:System.Runtime.Serialization.StreamingContext" /> structure that contains the
    ///     source and destination of the serialized stream associated with the <see cref="T:HashTable.LinearTable`2" />
    ///     instance.
    /// </param>
    public void GetObjectData(SerializationInfo? info, StreamingContext context) {
        ArgumentNullException.ThrowIfNull(info);

        info.AddValue(name: "Capacity", _entries.Length);
        info.AddValue(name: "LoadFactor", LoadFactor);
        var array = new KeyValuePair<TKey, TValue>[Count];
        ((ICollection<KeyValuePair<TKey, TValue>>)this).CopyTo(array, arrayIndex: 0);
        info.AddValue(name: "KeyValuePairs", array, typeof(KeyValuePair<TKey, TValue>[]));
    }
#nullable disable
    [StructLayout(LayoutKind.Sequential)]
    internal struct Entry
    {
        internal readonly int hashCode;
        internal int probeSequentialLength;
        internal readonly TKey key;
        internal TValue value;

        internal static Entry Tombstone = new(hash: 0x00000000, psl: -1, default(TKey), value: default(TValue));

        internal Entry(int hash, int psl, TKey key, TValue value) {
            hashCode = hash;
            probeSequentialLength = psl;
            this.key = key;
            this.value = value;
        }

        internal readonly void CopyTo(Entry[] dest) {
            if (probeSequentialLength == -1) {
                return;
            }

            int startIndex = (int)((uint)hashCode % dest.Length);
            for (int i = 0; i < LinearTable<TKey, TValue>._maxProbeSequentialLength; ++i) {
                int currentIndex = startIndex + i;
                if (currentIndex == dest.Length) {
                    startIndex = -i;
                    currentIndex = 0;
                }

                ref Entry currentDest = ref dest[currentIndex];
                if (currentDest.probeSequentialLength == -1) {
                    currentDest = new Entry(hashCode, currentIndex - startIndex, key, value);
                    return;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void MakeTomb() {
            if (!typeof(TValue).IsValueType) {
                value = default(TValue); // set to null for gc
            }

            probeSequentialLength = -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal readonly bool HasKey(int comparedHash, TKey comparedKey) {
            return hashCode == comparedHash && probeSequentialLength != -1
                                            && EqualityComparer<TKey>.Default.Equals(key, comparedKey);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal readonly bool HasValue(TValue comparedValue) {
            return probeSequentialLength != -1 && EqualityComparer<TValue>.Default.Equals(value, comparedValue);
        }
    }

    /// <inheritdoc cref="System.Collections.Generic.IReadOnlyCollection{T}" />
    /// <summary> Represents the collection of keys in a <see cref="T:HashTable.LinearTable`2" />. </summary>
    public sealed class KeyCollection : ICollection<TKey>, ICollection, IReadOnlyCollection<TKey>
    {
        /// <inheritdoc />
        /// <summary> Enumerates the elements of a <see cref="T:HashTable.LinearTable`2.KeyCollection" />. </summary>
        // ReSharper disable once MemberHidesStaticFromOuterClass
        public struct Enumerator : IEnumerator<TKey>
        {
            private readonly Entry[] _entries;
            private int _index;

            /// <inheritdoc />
            /// <summary> Gets the element at the current position of the enumerator. </summary>
            /// <returns>
            ///     The element in the <see cref="T:HashTable.LinearTable`2.KeyCollection" /> at the current position of the
            ///     enumerator.
            /// </returns>
            public readonly TKey Current {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _entries[_index].key;
            }

            /// <inheritdoc />
            /// <summary> Gets the element at the current position of the enumerator. </summary>
            /// <exception cref="T:System.InvalidOperationException">
            ///     The enumerator is positioned before the first element of the
            ///     collection or after the last element.
            /// </exception>
            /// <returns> The element in the collection at the current position of the enumerator. </returns>
            readonly object IEnumerator.Current {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get {
                    if (_index >= _entries.Length || _index < 0) {
                        throw new
                                InvalidOperationException("The enumerator is positioned before the first  or after the last element.");
                    }

                    return Current!;
                }
            }

            internal Enumerator(Entry[] entries) {
                _entries = entries;
                _index = -1;
            }

            /// <inheritdoc />
            /// <summary> Advances the enumerator to the next element of the <see cref="T:HashTable.LinearTable`2.KeyCollection" />. </summary>
            /// <returns>
            ///     <see langword="true" /> if the enumerator was successfully advanced to the next element;
            ///     <see langword="false" /> if the enumerator has passed the end of the collection.
            /// </returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext() {
                while (++_index < _entries.Length && _entries[_index].probeSequentialLength == -1) { }

                return _index < _entries.Length;
            }

            /// <inheritdoc />
            /// <summary> Sets the enumerator to its initial position, which is before the first element in the collection. </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Reset() {
                _index = -1;
            }

            /// <inheritdoc />
            /// <summary> Nothing to dispose </summary>
            public readonly void Dispose() { }
        }

        private const string _modifyException = "LinearTable<K,V>.KeyCollection can not be modified.";
        private readonly LinearTable<TKey, TValue> _linearTable;

        internal KeyCollection(LinearTable<TKey, TValue> lt) {
            _linearTable = lt;
        }

        /// <inheritdoc cref="IReadOnlyCollection{T}.Count" />
        /// <summary> Gets the number of keys contained in the <see cref="T:HashTable.LinearTable`2.KeyCollection" />. </summary>
        /// <returns> The number of keys contained in the <see cref="T:HashTable.LinearTable`2.KeyCollection" /> . </returns>
        public int Count {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _linearTable.Count;
        }

        /// <inheritdoc />
        /// <summary> Gets a value indicating whether the <see cref="T:System.Collections.Generic.ICollection`1" /> is read-only. </summary>
        /// <returns> <see langword="true" />. </returns>
        public bool IsReadOnly {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => true;
        }

        /// <inheritdoc />
        /// <summary>
        ///     Gets a value indicating whether access to the <see cref="T:System.Collections.ICollection" /> is synchronized
        ///     (thread safe).
        /// </summary>
        /// <returns> <see langword="false" />. </returns>
        bool ICollection.IsSynchronized => false;
#nullable enable
        /// <inheritdoc />
        /// <summary> Gets an object that can be used to synchronize access to the <see cref="T:System.Collections.ICollection" />. </summary>
        /// <returns>
        ///     An object that can be used to synchronize access to the <see cref="T:System.Collections.ICollection" />. In
        ///     the default implementation of <see cref="T:HashTable.LinearTable`2.KeyCollection" />, this property always returns
        ///     the current instance.
        /// </returns>
        public object SyncRoot => new();

        /// <inheritdoc />
        /// <summary>
        ///     Copies the elements of the <see cref="T:System.Collections.ICollection" /> to an
        ///     <see cref="T:System.Array" />, starting at a particular index.
        /// </summary>
        /// <param name="array">
        ///     The 1-dimensional destination array of the elements copied from
        ///     <see cref="T:System.Collections.ICollection" />.
        /// </param>
        /// <param name="index"> The zero-based index in <paramref name="array" /> at which copying begins. </param>
        /// <exception cref="T:System.ArgumentNullException"> <paramref name="array" /> is <see langword="null" /> . </exception>
        /// <exception cref="T:System.ArgumentOutOfRangeException"> <paramref name="index" /> is less than zero. </exception>
        /// <exception cref="T:System.ArgumentException">
        ///     <paramref name="array" /> is multidimensional. -or-
        ///     <paramref name="array" /> does not have zero-based indexing. -or- The number of elements in the source
        ///     <see cref="T:System.Collections.ICollection" /> is greater than the available space from <paramref name="index" />
        ///     to the end of the destination <paramref name="array" />. -or- The type of the source
        ///     <see cref="T:System.Collections.ICollection" /> cannot be cast automatically to the type of the destination
        ///     <paramref name="array" />.
        /// </exception>
        void ICollection.CopyTo(Array? array, int index) {
            ArgumentNullException.ThrowIfNull(array);
            if (array.Rank != 1) {
                throw new ArgumentException("Multidimensional arrays are not supported.");
            }

            if (array.GetLowerBound(0) != 0) {
                throw new ArgumentException("Non-zero lower bound arrays are not supported.");
            }

            if (index > array.Length) {
                throw new IndexOutOfRangeException("The value of the index is out of the array bounds.");
            }

            if (array.Length - index < _linearTable.Count) {
                throw new ArgumentException("Destination array is too small.");
            }

            switch (array) {
            case TKey[] keysArray:
                CopyTo(keysArray, index);
                break;
            case object[] objArray:
                Entry[] entries = _linearTable._entries;
                int count = _linearTable._entries.Length;
                try {
                    for (int i = 0; i < count; ++i) {
                        if (entries[i].probeSequentialLength >= -1) {
                            objArray[index++] = entries[i].key;
                        }
                    }

                    break;
                }
                catch (ArrayTypeMismatchException) {
                    throw new ArgumentException("Failed to copy, invalid array type.");
                }
            default: throw new ArgumentException("Failed to copy, invalid array type.");
            }
        }

        /// <inheritdoc />
        /// <summary>
        ///     Copies the <see cref="T:HashTable.LinearTable`2.KeyCollection" /> elements to an existing one-dimensional
        ///     <see cref="T:System.Array" />, starting at the specified array index.
        /// </summary>
        /// <param name="array">
        ///     The one-dimensional <see cref="T:System.Array" /> that is the destination of the elements copied
        ///     from <see cref="T:HashTable.LinearTable`2.KeyCollection" />. The <see cref="T:System.Array" /> must have zero-based
        ///     indexing.
        /// </param>
        /// <param name="index"> The zero-based index in <paramref name="array" /> at which copying begins. </param>
        /// <exception cref="T:System.ArgumentNullException"> <paramref name="array" /> is <see langword="null" />. </exception>
        /// <exception cref="T:System.ArgumentOutOfRangeException"> <paramref name="index" /> is less than zero. </exception>
        /// <exception cref="T:System.ArgumentException">
        ///     The number of elements in the source
        ///     <see cref="T:HashTable.LinearTable`2.KeyCollection" /> is greater than the available space from
        ///     <paramref name="index" /> to the end of the destination <paramref name="array" />.
        /// </exception>
        public void CopyTo(TKey[]? array, int index) {
            ArgumentNullException.ThrowIfNull(array);
            if (index < 0 || index > array.Length) {
                throw new IndexOutOfRangeException("The value of index is out of the array bounds.");
            }

            if (array.Length - index < _linearTable.Count) {
                throw new ArgumentException("Destination array is too small.");
            }

            int count = _linearTable._entries.Length;
            Entry[] entries = _linearTable._entries;
            for (int i = 0; i < count; ++i) {
                if (entries[i].probeSequentialLength >= -1) {
                    array[index++] = entries[i].key;
                }
            }
        }
#nullable disable

        /// <inheritdoc />
        /// <summary> Returns an enumerator that iterates through the <see cref="T:HashTable.LinearTable`2.KeyCollection" />. </summary>
        /// <returns>
        ///     A <see cref="T:HashTable.LinearTable`2.KeyCollection.Enumerator" /> for the
        ///     <see cref="T:HashTable.LinearTable`2.KeyCollection" /> .
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IEnumerator<TKey> GetEnumerator() {
            return new Enumerator(_linearTable._entries);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        IEnumerator IEnumerable.GetEnumerator() {
            return new Enumerator(_linearTable._entries);
        }

        /// <inheritdoc />
        /// <summary>
        ///     Adds an item to the <see cref="T:System.Collections.Generic.ICollection`1" />. This implementation always
        ///     throws <see cref="T:System.NotSupportedException" />.
        /// </summary>
        /// <param name="item"> The object to add to the <see cref="T:System.Collections.Generic.ICollection`1" />. </param>
        /// <exception cref="T:System.NotSupportedException"> Always thrown. </exception>
        void ICollection<TKey>.Add(TKey item) {
            throw new NotSupportedException(KeyCollection._modifyException);
        }

        /// <inheritdoc />
        /// <summary>
        ///     Removes the first occurrence of a specific object from the
        ///     <see cref="T:System.Collections.Generic.ICollection`1" />. This implementation always throws
        ///     <see cref="T:System.NotSupportedException" />.
        /// </summary>
        /// <param name="item"> The object to remove from the <see cref="T:System.Collections.Generic.ICollection`1" />. </param>
        /// <exception cref="T:System.NotSupportedException"> Always thrown. </exception>
        /// <returns> Nothing, the method throws. </returns>
        bool ICollection<TKey>.Remove(TKey item) {
            throw new NotSupportedException(KeyCollection._modifyException);
        }

        /// <inheritdoc />
        /// <summary>
        ///     Removes all items from the <see cref="T:System.Collections.Generic.ICollection`1" />. This implementation
        ///     always throws <see cref="T:System.NotSupportedException" />.
        /// </summary>
        /// <exception cref="T:System.NotSupportedException"> Always thrown. </exception>
        void ICollection<TKey>.Clear() {
            throw new NotSupportedException(KeyCollection._modifyException);
        }

        /// <inheritdoc />
        /// <summary> Determines whether the <see cref="T:System.Collections.Generic.ICollection`1" /> contains a specific value. </summary>
        /// <param name="item"> The object to locate in the <see cref="T:System.Collections.Generic.ICollection`1" />. </param>
        /// <returns>
        ///     <see langword="true" /> if <paramref name="item" /> is found in the
        ///     <see cref="T:System.Collections.Generic.ICollection`1" />; otherwise, <see langword="false" />.
        /// </returns>
        public bool Contains(TKey item) {
            return _linearTable.ContainsKey(item!);
        }
    }

    /// <inheritdoc cref="System.Collections.Generic.IReadOnlyCollection{T}" />
    /// <summary> Represents the collection of values in a <see cref="T:HashTable.LinearTable`2" />. </summary>
    public sealed class ValueCollection : ICollection<TValue>, ICollection, IReadOnlyCollection<TValue>
    {
        /// <inheritdoc />
        /// <summary> Enumerates the elements of a <see cref="T:HashTable.LinearTable`2.ValueCollection" />. </summary>
        // ReSharper disable once MemberHidesStaticFromOuterClass
        public struct Enumerator : IEnumerator<TValue>
        {
            private readonly Entry[] _entries;
            private int _index;

            /// <inheritdoc />
            /// <summary> Gets the element at the current position of the enumerator. </summary>
            /// <returns>
            ///     The element in the <see cref="T:HashTable.LinearTable`2.KeyCollection" /> at the current position of the
            ///     enumerator.
            /// </returns>
            public readonly TValue Current {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _entries[_index].value;
            }

            /// <inheritdoc />
            /// <summary> Gets the element at the current position of the enumerator. </summary>
            /// <exception cref="T:System.InvalidOperationException">
            ///     The enumerator is positioned before the first element of the
            ///     collection or after the last element.
            /// </exception>
            /// <returns> The element in the collection at the current position of the enumerator. </returns>
            readonly object IEnumerator.Current {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get {
                    if (_index >= _entries.Length || _index < 0) {
                        throw new
                                InvalidOperationException("The enumerator is positioned before the first  or after the last element.");
                    }

                    return Current!;
                }
            }

            internal Enumerator(Entry[] entries) {
                _entries = entries;
                _index = -1;
            }

            /// <inheritdoc />
            /// <summary> Advances the enumerator to the next element of the <see cref="T:HashTable.LinearTable`2.KeyCollection" />. </summary>
            /// <returns>
            ///     <see langword="true" /> if the enumerator was successfully advanced to the next element;
            ///     <see langword="false" /> if the enumerator has passed the end of the collection.
            /// </returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext() {
                while (++_index < _entries.Length && _entries[_index].probeSequentialLength == -1) { }

                return _index < _entries.Length;
            }

            /// <inheritdoc />
            /// <summary> Sets the enumerator to its initial position, which is before the first element in the collection. </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Reset() {
                _index = -1;
            }

            /// <inheritdoc />
            /// <summary> Nothing to dispose </summary>
            public readonly void Dispose() { }
        }

        private const string _modifyException = "LinearTable<K,V>.ValueCollection can not be modified.";
        private readonly LinearTable<TKey, TValue> _linearTable;

        internal ValueCollection(LinearTable<TKey, TValue> lt) {
            _linearTable = lt;
        }

        /// <inheritdoc cref="IReadOnlyCollection{T}.Count" />
        /// <summary> Gets the number of keys contained in the <see cref="T:HashTable.LinearTable`2.ValueCollection" />. </summary>
        /// <returns> The number of keys contained in the <see cref="T:HashTable.LinearTable`2.ValueCollection" />. </returns>
        public int Count {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _linearTable.Count;
        }

        /// <inheritdoc />
        /// <summary> Gets a value indicating whether the <see cref="T:System.Collections.Generic.ICollection`1" /> is read-only. </summary>
        /// <returns> <see langword="true" />. </returns>
        public bool IsReadOnly {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => true;
        }

        /// <inheritdoc />
        /// <summary>
        ///     Gets a value indicating whether access to the <see cref="T:System.Collections.ICollection" /> is synchronized
        ///     (thread safe).
        /// </summary>
        /// <returns> <see langword="false" />. </returns>
        bool ICollection.IsSynchronized => false;

#nullable enable
        /// <inheritdoc />
        /// <summary> Gets an object that can be used to synchronize access to the <see cref="T:System.Collections.ICollection" />. </summary>
        /// <returns>
        ///     An object that can be used to synchronize access to the <see cref="T:System.Collections.ICollection" />. In
        ///     the default implementation of <see cref="T:HashTable.LinearTable`2.ValueCollection" />, this property always
        ///     returns the current instance.
        /// </returns>
        public object SyncRoot => new();

        /// <inheritdoc />
        /// <summary>
        ///     Copies the elements of the <see cref="T:System.Collections.ICollection" /> to an
        ///     <see cref="T:System.Array" />, starting at a particular index.
        /// </summary>
        /// <param name="array">
        ///     The 1-dimensional destination array of the elements copied from
        ///     <see cref="T:System.Collections.ICollection" />.
        /// </param>
        /// <param name="index"> The zero-based index in <paramref name="array" /> at which copying begins. </param>
        /// <exception cref="T:System.ArgumentNullException"> <paramref name="array" /> is <see langword="null" /> . </exception>
        /// <exception cref="T:System.ArgumentOutOfRangeException"> <paramref name="index" /> is less than zero. </exception>
        /// <exception cref="T:System.ArgumentException">
        ///     <paramref name="array" /> is multidimensional. -or-
        ///     <paramref name="array" /> does not have zero-based indexing. -or- The number of elements in the source
        ///     <see cref="T:System.Collections.ICollection" /> is greater than the available space from <paramref name="index" />
        ///     to the end of the destination <paramref name="array" />. -or- The type of the source
        ///     <see cref="T:System.Collections.ICollection" /> cannot be cast automatically to the type of the destination
        ///     <paramref name="array" />.
        /// </exception>
        void ICollection.CopyTo(Array? array, int index) {
            ArgumentNullException.ThrowIfNull(array);
            if (array.Rank != 1) {
                throw new ArgumentException("Multidimensional arrays are not supported.");
            }

            if (array.GetLowerBound(0) != 0) {
                throw new ArgumentException("Non-zero lower bound arrays are not supported.");
            }

            if (index > array.Length) {
                throw new IndexOutOfRangeException("The value of the index is out of the array bounds.");
            }

            if (array.Length - index < _linearTable.Count) {
                throw new ArgumentException("Destination array is too small.");
            }

            switch (array) {
            case TValue[] valuesArray:
                CopyTo(valuesArray, index);
                break;
            case object[] objArray:
                Entry[] entries = _linearTable._entries;
                int count = _linearTable._entries.Length;
                try {
                    for (int i = 0; i < count; ++i) {
                        if (entries[i].probeSequentialLength >= -1) {
                            objArray[index++] = entries[i].value;
                        }
                    }

                    break;
                }
                catch (ArrayTypeMismatchException) {
                    throw new ArgumentException("Failed to copy, invalid array type.");
                }
            default: throw new ArgumentException("Failed to copy, invalid array type.");
            }
        }

        /// <inheritdoc />
        /// <summary>
        ///     Copies the <see cref="T:HashTable.LinearTable`2.ValueCollection" /> elements to an existing one-dimensional
        ///     <see cref="T:System.Array" />, starting at the specified array index.
        /// </summary>
        /// <param name="array">
        ///     The one-dimensional <see cref="T:System.Array" /> that is the destination of the elements copied
        ///     from <see cref="T:HashTable.LinearTable`2.ValueCollection" />. The <see cref="T:System.Array" /> must have
        ///     zero-based indexing.
        /// </param>
        /// <param name="index"> The zero-based index in <paramref name="array" /> at which copying begins. </param>
        /// <exception cref="T:System.ArgumentNullException"> <paramref name="array" /> is <see langword="null" />. </exception>
        /// <exception cref="T:System.ArgumentOutOfRangeException"> <paramref name="index" /> is less than zero. </exception>
        /// <exception cref="T:System.ArgumentException">
        ///     The number of elements in the source
        ///     <see cref="T:HashTable.LinearTable`2.ValueCollection" /> is greater than the available space from
        ///     <paramref name="index" /> to the end of the destination <paramref name="array" />.
        /// </exception>
        public void CopyTo(TValue[]? array, int index) {
            ArgumentNullException.ThrowIfNull(array);
            if (index < 0 || index > array.Length) {
                throw new IndexOutOfRangeException("The value of index is out of the array bounds.");
            }

            if (array.Length - index < _linearTable.Count) {
                throw new ArgumentException("Destination array is too small.");
            }

            int count = _linearTable._entries.Length;
            Entry[] entries = _linearTable._entries;
            for (int i = 0; i < count; ++i) {
                if (entries[i].probeSequentialLength >= -1) {
                    array[index++] = entries[i].value;
                }
            }
        }
#nullable disable

        /// <inheritdoc />
        /// <summary> Returns an enumerator that iterates through the <see cref="T:HashTable.LinearTable`2.ValueCollection" />. </summary>
        /// <returns>
        ///     A <see cref="T:HashTable.LinearTable`2.ValueCollection.Enumerator" /> for the
        ///     <see cref="T:HashTable.LinearTable`2.ValueCollection" />.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IEnumerator<TValue> GetEnumerator() {
            return new Enumerator(_linearTable._entries);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        IEnumerator IEnumerable.GetEnumerator() {
            return new Enumerator(_linearTable._entries);
        }

        /// <inheritdoc />
        /// <summary>
        ///     Adds an item to the <see cref="T:System.Collections.Generic.ICollection`1" />. This implementation always
        ///     throws <see cref="T:System.NotSupportedException" />.
        /// </summary>
        /// <param name="item"> The object to add to the <see cref="T:System.Collections.Generic.ICollection`1" />. </param>
        /// <exception cref="T:System.NotSupportedException"> Always thrown. </exception>
        void ICollection<TValue>.Add(TValue item) {
            throw new NotSupportedException(ValueCollection._modifyException);
        }

        /// <inheritdoc />
        /// <summary>
        ///     Removes all items from the <see cref="T:System.Collections.Generic.ICollection`1" />. This implementation
        ///     always throws <see cref="T:System.NotSupportedException" />.
        /// </summary>
        /// <exception cref="T:System.NotSupportedException"> Always thrown. </exception>
        void ICollection<TValue>.Clear() {
            throw new NotSupportedException(ValueCollection._modifyException);
        }

        /// <inheritdoc />
        /// <summary>
        ///     Removes the first occurrence of a specific object from the
        ///     <see cref="T:System.Collections.Generic.ICollection`1" />. This implementation always throws
        ///     <see cref="T:System.NotSupportedException" />.
        /// </summary>
        /// <param name="item"> The object to remove from the <see cref="T:System.Collections.Generic.ICollection`1" />. </param>
        /// <exception cref="T:System.NotSupportedException"> Always thrown. </exception>
        /// <returns> Nothing, the method throws. </returns>
        bool ICollection<TValue>.Remove(TValue item) {
            throw new NotSupportedException(ValueCollection._modifyException);
        }

        /// <inheritdoc />
        /// <summary> Determines whether the <see cref="T:System.Collections.Generic.ICollection`1" /> contains a specific value. </summary>
        /// <param name="item"> The object to locate in the <see cref="T:System.Collections.Generic.ICollection`1" />. </param>
        /// <returns>
        ///     <see langword="true" /> if <paramref name="item" /> is found in the
        ///     <see cref="T:System.Collections.Generic.ICollection`1" />; otherwise, <see langword="false" />.
        /// </returns>
        public bool Contains(TValue item) {
            return _linearTable.ContainsValue(item!);
        }
    }

    /// <inheritdoc cref="System.Collections.IDictionaryEnumerator" />
    /// <summary> Enumerates the elements of a <see cref="T:HashTable.LinearTable`2" />. </summary>
    public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>, IDictionaryEnumerator
    {
        private readonly Entry[] _entries;
        private int _index;

        /// <inheritdoc />
        /// <summary> Gets the element at the current position of the enumerator. </summary>
        /// <exception cref="T:System.IndexOutOfRangeException">
        ///     The enumerator is positioned before the first element of the
        ///     collection or after the last element.
        /// </exception>
        /// <returns>
        ///     The element in the dictionary at the current position of the enumerator, as a
        ///     <see cref="T:System.Collections.DictionaryEntry" />.
        /// </returns>
        readonly DictionaryEntry IDictionaryEnumerator.Entry => new(_entries[_index].key, _entries[_index].value);
        /// <inheritdoc />
        /// <summary> Gets the key of the element at the current position of the enumerator. </summary>
        /// <exception cref="T:System.IndexOutOfRangeException">
        ///     The enumerator is positioned before the first element of the
        ///     collection or after the last element.
        /// </exception>
        /// <returns> The key of the element in the dictionary at the current position of the enumerator. </returns>
        readonly object IDictionaryEnumerator.Key => _entries[_index].key;
        /// <inheritdoc />
        /// <summary> Gets the value of the element at the current position of the enumerator. </summary>
        /// <exception cref="T:System.IndexOutOfRangeException">
        ///     The enumerator is positioned before the first element of the
        ///     collection or after the last element.
        /// </exception>
        /// <returns> The value of the element in the dictionary at the current position of the enumerator. </returns>
        readonly object IDictionaryEnumerator.Value => _entries[_index].value;

        /// <inheritdoc />
        /// <summary> Gets the element at the current position of the enumerator. </summary>
        /// <returns> The element in the <see cref="T:HashTable.LinearTable`2" /> at the current position of the enumerator. </returns>
        public readonly KeyValuePair<TKey, TValue> Current {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new(_entries[_index].key, _entries[_index].value);
        }

        /// <inheritdoc />
        /// <summary> Gets the element at the current position of the enumerator. </summary>
        /// <returns> The element in the <see cref="T:HashTable.LinearTable`2" /> at the current position of the enumerator. </returns>
        readonly object IEnumerator.Current {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                if (_index >= _entries.Length || _index < 0) {
                    throw new
                            InvalidOperationException("The enumerator is positioned before the first or after the last element.");
                }

                return Current!;
            }
        }

        internal Enumerator(Entry[] entries) {
            _entries = entries;
            _index = -1;
        }

        /// <inheritdoc />
        /// <summary> Advances the enumerator to the next element of the <see cref="T:HashTable.LinearTable`2" />. </summary>
        /// <returns>
        ///     <see langword="true" /> if the enumerator was successfully advanced to the next element;
        ///     <see langword="false" /> if the enumerator has passed the end of the collection.
        /// </returns>
        public bool MoveNext() {
            while (++_index < _entries.Length && _entries[_index].probeSequentialLength == -1) { }

            return _index < _entries.Length;
        }

        /// <inheritdoc />
        /// <summary> Sets the enumerator to its initial position, which is before the first element in the collection. </summary>
        public void Reset() {
            _index = -1;
        }

        /// <inheritdoc />
        /// <summary> Nothing to dispose of. </summary>
        public readonly void Dispose() { }
    }

    private const int _maxProbeSequentialLength = 20;
    private const float _growthFactor = 1.75f;

    private readonly Entry[] _entries;
    private int _count;
    private KeyCollection _keys;
    private ValueCollection _values;

    /// <summary> Ratio between the number of valid items and the capacity of the <see cref="T:HashTable.LinearTable`2" />. </summary>
    public float LoadFactor => (float)Count / _entries.Length;

    /// <summary>
    ///     Maximum number of key/value pairs that can be stored in the <see cref="T:HashTable.LinearTable`2" /> before
    ///     needing any further expansion of its backing storage.
    /// </summary>
    public int Capacity => _entries.Length;

    /// <inheritdoc cref="IReadOnlyCollection{T}.Count" />
    /// <summary> Gets the number of key/value pairs contained in the <see cref="T:HashTable.LinearTable`2" />. </summary>
    /// <returns> The number of key/value pairs contained in the <see cref="T:HashTable.LinearTable`2" />. </returns>
    public int Count => _count;

    /// <inheritdoc />
    /// <summary> Gets a value that indicates whether the linear table is read-only. </summary>
    /// <returns> This property always returns <see langword="false" />. </returns>
    public bool IsReadOnly => throw new NotImplementedException();

    /// <inheritdoc />
    /// <summary> Gets a collection containing the keys in the <see cref="T:HashTable.LinearTable`2" />. </summary>
    /// <returns>
    ///     A <see cref="T:HashTable.LinearTable`2.KeyCollection" /> containing the keys in the
    ///     <see cref="T:HashTable.LinearTable`2" />.
    /// </returns>
    public ICollection<TKey> Keys {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get { return _keys ??= new KeyCollection(this); }
    }
    /// <inheritdoc />
    /// <summary> Gets a collection containing the keys in the <see cref="T:HashTable.LinearTable`2" />. </summary>
    /// <returns>
    ///     A <see cref="T:HashTable.LinearTable`2.KeyCollection" /> containing the keys in the
    ///     <see cref="T:HashTable.LinearTable`2" />.
    /// </returns>
    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;
    /// <inheritdoc />
    /// <summary> Gets a collection containing the values in the <see cref="T:HashTable.LinearTable`2" />. </summary>
    /// <returns>
    ///     A <see cref="T:HashTable.LinearTable`2.KeyCollection" /> containing the values in the
    ///     <see cref="T:HashTable.LinearTable`2" />.
    /// </returns>
    public ICollection<TValue> Values {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get { return _values ??= new ValueCollection(this); }
    }
    /// <inheritdoc />
    /// <summary> Gets a collection containing the values in the <see cref="T:HashTable.LinearTable`2" />. </summary>
    /// <returns>
    ///     A <see cref="T:HashTable.LinearTable`2.KeyCollection" /> containing the values in the
    ///     <see cref="T:HashTable.LinearTable`2" />.
    /// </returns>
    IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;

    /// <inheritdoc cref="IReadOnlyDictionary{TKey,TValue}.this" />
    /// <summary> Gets or sets the value associated with the specified key. </summary>
    /// <param name="key"> The key of the value to get or set. </param>
    /// <exception cref="T:System.Collections.Generic.KeyNotFoundException">
    ///     The property is retrieved and
    ///     <paramref name="key" /> does not exist in the collection.
    /// </exception>
    /// <returns>
    ///     The value associated with the specified key. If the specified key is not found, throws a
    ///     <see cref="T:System.Collections.Generic.KeyNotFoundException" />.
    /// </returns>
    public TValue this[TKey key] {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get {
            ref readonly TValue entryValue = ref GetValueRef(key);
            if (ReferenceEquals(entryValue, null)) {
                throw new KeyNotFoundException();
            }

            return entryValue;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set {
            ref TValue entryValue = ref GetValueRef(key);
            if (ReferenceEquals(entryValue, null)) {
                throw new KeyNotFoundException();
            }

            entryValue = value;
        }
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="T:HashTable.LinearTable`2" /> class that is empty, and has the
    ///     specified initial capacity.
    /// </summary>
    /// <param name="capacity"> The initial number of elements that the <see cref="T:HashTable.LinearTable`2" /> can contain. </param>
    /// <exception cref="T:System.ArgumentOutOfRangeException"> <paramref name="capacity" /> is less than 0. </exception>
    public LinearTable(int capacity) {
        _entries = new Entry[capacity];
        for (int i = 0; i < _entries.Length; ++i) {
            _entries[i] = Entry.Tombstone;
        }

        _keys = null;
        _values = null;
        _count = 0;
    }

    /// <inheritdoc />
    /// <summary> Returns an enumerator that iterates through the collection. </summary>
    /// <returns> An enumerator that can be used to iterate through the collection. </returns>
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() {
        return new Enumerator(_entries);
    }

    /// <inheritdoc />
    /// <summary> Returns an enumerator that iterates through the collection. </summary>
    /// <returns> An enumerator that can be used to iterate through the collection. </returns>
    IEnumerator IEnumerable.GetEnumerator() {
        return GetEnumerator();
    }

    /// <inheritdoc />
    /// <summary> Adds the specified key and value to the <see cref="T:HashTable.LinearTable`2" />. </summary>
    /// <param name="key"> The key. </param>
    /// <param name="value"> The value. </param>
    /// <remarks>
    ///     This method does not throw if a value with the same key already exists. A duplicate value will be added
    ///     instead, and will never be found (unless the hiding one is deleted).
    /// </remarks>
    public void Add(TKey key, TValue value) {
        // (value types should override GetHashCode) which will provoke a constrained call
        int hash = key.GetHashCode();
        do {
            int startIndex = (int)((uint)hash % _entries.Length);
            for (int i = 0; i < LinearTable<TKey, TValue>._maxProbeSequentialLength; ++i) {
                int currentIndex = startIndex + i;
                if (currentIndex == _entries.Length) {
                    startIndex = -i; // move currentIndex to 0
                    currentIndex = 0;
                }

                ref Entry currentEntry = ref _entries[currentIndex];
                if (currentEntry.probeSequentialLength == -1) {
                    currentEntry = new Entry(hash, currentIndex - startIndex, key, value);
                    _count = _count + 1;
                    return;
                }
            }

            // lookup will never be able to find the element => rehash needed
            Rehash();
        } while (true);
    }

    /// <summary> Extremely slow, copy all elements in a bigger container. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Rehash() {
        // Initialize the new array of entries
        var newEntries = new Entry[(int)(_entries.Length * LinearTable<TKey, TValue>._growthFactor)];
        for (int i = 0; i < newEntries.Length; ++i) {
            newEntries[i].MakeTomb();
        }

        // Fill with this entries.
        // Note that by design, psl is either the same or lower than before.
        for (int i = 0; i < _entries.Length; ++i) {
            _entries[i].CopyTo(newEntries);
        }
    }

    /// <inheritdoc />
    /// <summary> Adds the specified key and value to the <see cref="T:HashTable.LinearTable`2" />. </summary>
    /// <param name="item"> A pair containing the key and the value. </param>
    /// <remarks>
    ///     This method does not throw if a value with the same key already exists. A duplicate value will be added
    ///     instead, and will never be found (unless the hiding one is deleted).
    /// </remarks>
    public void Add(KeyValuePair<TKey, TValue> item) {
        Add(item.Key, item.Value);
    }

    /// <inheritdoc />
    /// <summary> Removes the value with the specified key from the <see cref="T:HashTable.LinearTable`2" /> . </summary>
    /// <param name="key"> The key of the element to remove. </param>
    /// <returns>
    ///     <see langword="true" /> if the element is successfully found and removed; otherwise, <see langword="false" />
    ///     . This method returns <see langword="false" /> if <paramref name="key" /> is not found in the
    ///     <see cref="T:HashTable.LinearTable`2" />.
    /// </returns>
    public bool Remove(TKey key) {
        int hash = key.GetHashCode();
        int startIndex = (int)((uint)hash % _entries.Length);
        for (int i = 0; i < LinearTable<TKey, TValue>._maxProbeSequentialLength; ++i) {
            int currentIndex = startIndex + i;
            if (currentIndex == _entries.Length) {
                startIndex = -i;
                currentIndex = 0;
            }

            ref Entry current = ref _entries[currentIndex];
            if (current.HasKey(hash, key)) {
                current.MakeTomb();
                _count = _count - 1;
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    /// <summary> Removes the value with the specified key from the <see cref="T:HashTable.LinearTable`2" /> . </summary>
    /// <param name="item"> The key of the element to remove. </param>
    /// <returns>
    ///     <see langword="true" /> if the element is successfully found and removed; otherwise, <see langword="false" />
    ///     . This method returns <see langword="false" /> if <paramref name="item" /> is not found in the
    ///     <see cref="T:HashTable.LinearTable`2" />.
    /// </returns>
    public bool Remove(KeyValuePair<TKey, TValue> item) {
        return Remove(item.Key);
    }

    /// <inheritdoc />
    /// <summary> Removes all keys and values from the <see cref="T:HashTable.LinearTable`2" />. </summary>
    /// <remarks>
    ///     This does not remove reference to objects added to the <see cref="T:HashTable.LinearTable`2" />. If you seeek
    ///     to release GC pressure, may want to "destroy" this instance of <see cref="T:HashTable.LinearTable`2" /> instead.
    /// </remarks>
    public void Clear() {
        _count = 0;
        for (int i = 0; i < _entries.Length; ++i) {
            _entries[i].MakeTomb();
        }
    }

    /// <inheritdoc cref="IReadOnlyDictionary{TKey,TValue}.ContainsKey" />
    /// <summary> Determines whether the <see cref="T:HashTable.LinearTable`2" /> contains the specified key. </summary>
    /// <param name="key"> The key to locate in the <see cref="T:HashTable.LinearTable`2" />. </param>
    /// <exception cref="T:System.ArgumentNullException"> <paramref name="key" /> is <see langword="null" />. </exception>
    /// <returns>
    ///     <see langword="true" /> if the <see cref="T:HashTable.LinearTable`2" /> an element with the specified key;
    ///     otherwise, <see langword="false" />.
    /// </returns>
    public bool ContainsKey(TKey key) {
        int hash = key.GetHashCode();
        int startIndex = (int)((uint)hash % _entries.Length);
        for (int i = 0; i < LinearTable<TKey, TValue>._maxProbeSequentialLength; ++i) {
            int currentIndex = startIndex + i;
            if (currentIndex == _entries.Length) {
                startIndex = -i;
                currentIndex = 0;
            }

            if (_entries[currentIndex].HasKey(hash, key)) {
                return true;
            }
        }

        return false;
    }

    /// <summary> Determines whether the <see cref="T:HashTable.LinearTable`2" /> contains a specific value. </summary>
    /// <param name="value">
    ///     The value to locate in the <see cref="T:HashTable.LinearTable`2" />. The value can be
    ///     <see langword="null" /> for reference types.
    /// </param>
    /// <returns>
    ///     <see langword="true" /> if the <see cref="T:HashTable.LinearTable`2" /> contains an element with the
    ///     specified value; otherwise, <see langword="false" />.
    /// </returns>
    public bool ContainsValue(TValue value) {
        for (int i = 0; i < _entries.Length; ++i) {
            if (_entries[i].HasValue(value)) {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    /// <summary>
    ///     Determines whether the <see cref="T:System.Collections.IDictionary" /> contains an element with the specified
    ///     key.
    /// </summary>
    /// <param name="item"> The key and value to locate in the <see cref="T:System.Collections.IDictionary" />. </param>
    /// <returns>
    ///     <see langword="true" /> if the <see cref="T:System.Collections.IDictionary" /> contains an element with the
    ///     specified key; otherwise, <see langword="false" />.
    /// </returns>
    public bool Contains(KeyValuePair<TKey, TValue> item) {
        int hash = item.Key.GetHashCode();
        int startIndex = (int)((uint)hash % _entries.Length);
        for (int i = 0; i < LinearTable<TKey, TValue>._maxProbeSequentialLength; ++i) {
            int currentIndex = startIndex + i;
            if (currentIndex == _entries.Length) {
                startIndex = -i;
                currentIndex = 0;
            }

            ref Entry currentEntry = ref _entries[currentIndex];
            if (currentEntry.HasKey(hash, item.Key) && currentEntry.HasValue(item.Value)) {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc cref="IReadOnlyDictionary{TKey,TValue}.TryGetValue" />
    /// <summary> Gets the value associated with the specified key. </summary>
    /// <param name="key"> The key of the value to get. </param>
    /// <param name="value">
    ///     When this method returns, contains the value associated with the specified key, if the key is
    ///     found; otherwise, the default value for the type of the <paramref name="value" /> parameter. This parameter is
    ///     passed uninitialized.
    /// </param>
    /// <returns>
    ///     <see langword="true" /> if the <see cref="T:HashTable.LinearTable`2" /> contains an element with the
    ///     specified key; otherwise, <see langword="false" />.
    /// </returns>
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value) {
        int hash = key.GetHashCode();
        int startIndex = (int)((uint)hash % _entries.Length);
        for (int i = 0; i < LinearTable<TKey, TValue>._maxProbeSequentialLength; ++i) {
            int currentIndex = startIndex + i;
            if (currentIndex == _entries.Length) {
                startIndex = -i;
                currentIndex = 0;
            }

            if (_entries[currentIndex].HasKey(hash, key)) {
                value = _entries[currentIndex].value;
                return true;
            }
        }

        value = default(TValue);
        return false;
    }

    /// <summary> Get a reference to the value associated with given key in <see cref="T:Featurless.LinearTable`2" />. </summary>
    /// <param name="key"> the key of the <see cref="T:Featurless.LinearTable`2" /> entry to look for. </param>
    /// <returns> A reference to the value associated with given key. <see cref="T:Unsafe.NullRef" /> if key is not found. </returns>
    public ref TValue GetValueRef(TKey key) {
        int hash = key.GetHashCode();
        int startIndex = (int)((uint)hash % _entries.Length);
        for (int i = 0; i < LinearTable<TKey, TValue>._maxProbeSequentialLength; ++i) {
            int currentIndex = startIndex + i;
            if (currentIndex == _entries.Length) {
                startIndex = -i;
                currentIndex = 0;
            }

            ref Entry entry = ref _entries[currentIndex];
            if (entry.HasKey(hash, key)) {
                return ref entry.value;
            }
        }

        return ref Unsafe.NullRef<TValue>();
    }
}
