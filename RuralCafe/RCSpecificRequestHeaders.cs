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

        /// <summary>
        /// The user id.
        /// </summary>
        public int RCUserID;
    }
}
