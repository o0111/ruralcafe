using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Util
{
    /// <summary>
    /// Taken from
    /// http://stackoverflow.com/questions/16839479/using-lambda-expression-in-place-of-icomparer-argument
    /// 
    /// You can use it to instantiate IComparers inline with lambda expressions.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class FunctionalComparer<T> : IComparer<T>
    {
        private Func<T, T, int> comparer;
        public FunctionalComparer(Func<T, T, int> comparer)
        {
            this.comparer = comparer;
        }
        public static IComparer<T> Create(Func<T, T, int> comparer)
        {
            return new FunctionalComparer<T>(comparer);
        }
        public int Compare(T x, T y)
        {
            return comparer(x, y);
        }
    }
}
