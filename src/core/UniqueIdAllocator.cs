using System;

#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace astron.core
{
    public class UniqueIdAllocator
    {
        protected uint[] table;
        protected uint max;
        protected uint min;
        protected uint size;
        protected uint nextFree;
        protected uint lastFree;
        protected uint free;

        private static uint IndexEnd = uint.MaxValue;
        private static uint IndexAllocated = uint.MaxValue - 1;

        /// <summary>
        /// Create a free id pool in the range [min:max].
        /// </summary>
        /// <param name="min"></param>
        /// <param name="max"></param>
        public UniqueIdAllocator(uint lo, uint hi)
        {
            min = lo;
            max = hi;

            size = max - min + 1; // +1 because min and max are inclusive.
            table = new uint[size];

            for (uint i = 0; i < size; ++i)
            {
                table[i] = i + 1;
            }

            table[size - 1] = IndexEnd;
            nextFree = 0;
            lastFree = size - 1;
            free = size;
        }

        /// <summary>
        /// Cross platform log implementation.
        /// </summary>
        /// <param name="message"></param>
        public void Log(string message)
        {
#if UNITY_5_3_OR_NEWER
            Debug.Log(message);
#else
        Console.WriteLine(message);
#endif
        }

        /// <summary>
        /// Returns an id between min and max (that were passed to the constructor).
        /// IndexEnd is returned if no ids are available.
        /// </summary>
        /// <returns></returns>
        public uint Allocate()
        {
            if (nextFree == IndexEnd)
            {
                Log($"Allocate error: no more free ids.");
                return IndexEnd;
            }

            uint index = nextFree;

            nextFree = table[nextFree];
            table[index] = IndexAllocated;

            --free;

            return index + min;
        }

        /// <summary>
        /// Checks the allocated state of an index. Returns true for
        /// indices that are currently allocated and in use.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public bool IsAllocated(uint id)
        {
            if (id < min || id > max)
            {
                // This id is out of range, not allocated.
                return false;
            }

            uint index = id - min; // Convert to table index.
            return table[index] == IndexAllocated;
        }

        /// <summary>
        /// Free an allocated index (index must be between min and max that were
        /// passed to the constructor).
        /// </summary>
        /// <param name="id"></param>
        public void Free(uint id)
        {
            uint index = id - min; // Convert to table index.

            if (nextFree != IndexEnd)
            {
                table[lastFree] = index;
            }

            table[index] = IndexEnd;
            lastFree = index;

            if (nextFree == IndexEnd)
            {
                // ...the free list was empty.
                nextFree = index;
            }

            ++free;
        }

        /// <summary>
        /// Returns the decimal fraction of the pool that is used.  The range is 0 to
        /// 1.0 (e.g.  75% would be 0.75).
        /// </summary>
        /// <returns></returns>
        public float FractionUsed()
        {
            return (float)(size - free) / size;
        }
    }
}
