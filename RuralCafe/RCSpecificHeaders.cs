using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RuralCafe
{
    /// <summary>
    /// Data class for the RC specific request headers.
    /// </summary>
    public class RCSpecificRequestHeaders
    {
        /// <summary>
        /// The user id.
        /// </summary>
        public int RCUserID;

        /// <summary>
        /// Default Constructor.
        /// </summary>
        public RCSpecificRequestHeaders() { }

        /// <summary>
        /// </summary>
        /// <param name="userID">The user id.</param>
        public RCSpecificRequestHeaders(int userID)
        {
            RCUserID = userID;
        }
    }

    /// <summary>
    /// Data class for the RC specific response headers.
    /// </summary>
    public class RCSpecificResponseHeaders
    {
        /// <summary>
        /// The size of the response package index.
        /// </summary>
        public long RCPackageIndexSize;
        /// <summary>
        /// The size of the response package content.
        /// </summary>
        public long RCPackageContentSize;

        /// <summary>
        /// Default Constructor.
        /// </summary>
        public RCSpecificResponseHeaders() { }

        /// <summary>
        /// </summary>
        /// <param name="packageIndexSize">The size of the response package index.</param>
        /// <param name="packageContentSize">The size of the response package content.</param>
        public RCSpecificResponseHeaders(long packageIndexSize, long packageContentSize) 
        {
            RCPackageIndexSize = packageIndexSize;
            RCPackageContentSize = packageContentSize;
        }
    }
}
