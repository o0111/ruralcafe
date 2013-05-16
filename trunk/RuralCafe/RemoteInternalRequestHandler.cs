using BzReader;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml;

namespace RuralCafe
{
    /// <summary>
    /// Handles internal requests. These are e.g. richness, remove, etc.
    /// </summary>
    public class RemoteInternalRequestHandler : InternalRequestHandler
    {
        private static Dictionary<String, RoutineMethod> routines = new Dictionary<String, RoutineMethod>();
        private static RoutineMethod defaultMethod = new RoutineMethod("Default");

        /// <summary>
        /// Static Constructor. Defines routines.
        /// </summary>
        static RemoteInternalRequestHandler()
        {
            routines.Add("/request/richness", new RoutineMethod("RichnessRequest",
                new string[] { "u", "r" }, new Type[] { typeof(int), typeof(string) }));
        }

        /// <summary>
        /// Constructor for a local internal proxy's request handler.
        /// </summary>
        /// <param name="proxy">Proxy this request handler belongs to.</param>
        /// <param name="socket">Client socket.</param>
        public RemoteInternalRequestHandler(RCRemoteProxy proxy, HttpListenerContext context)
            : base(proxy, context, routines, defaultMethod)
        {
            _requestId = _proxy.NextRequestId;
            _proxy.NextRequestId = _proxy.NextRequestId + 1;
            _requestTimeout = LOCAL_REQUEST_PACKAGE_DEFAULT_TIMEOUT;
        }

        #region Proxy Control Methods
        /// <summary>
        /// Client changes richness. TODO
        /// </summary>
        public Response RichnessRequest(int userid, string richnessString)
        {
            Richness richness;
            try
            {
                richness = (Richness)Enum.Parse(typeof(Richness), richnessString, true);
            }
            catch (Exception)
            {
                throw new HttpException(HttpStatusCode.BadRequest, "unknown richness setting: " + richnessString);
            }
            Console.WriteLine("Richness would have been set to: " + richness + " by user " + userid);
            return new Response("Richness set.");
        }

        #endregion
    }
}
