using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace RuralCafe.Util
{
    /// <summary>
    /// A Collection, keyed by ints, that provides both dictionary and list behaviour.
    /// You can access elements as in a dictionary by [], and as in a list by .ElementAt(), ...
    /// </summary>
    /// <typeparam name="T">The Value Type.</typeparam>
    public class IntKeyedCollection<T> : KeyedCollection<int, KeyValuePair<int, T>>
    {
        /// <summary>
        /// Create a new int keyed collection.
        /// </summary>
        public IntKeyedCollection() : base() { }
        /// <summary>
        /// Gets the key for an item.
        /// </summary>
        /// <param name="item">An dictionary item.</param>
        /// <returns>The key.</returns>
        protected override int GetKeyForItem(KeyValuePair<int, T> item)
        {
            return item.Key;
        }
    }
}
