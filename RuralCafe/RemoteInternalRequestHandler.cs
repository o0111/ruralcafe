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
                new string[] { "r" }, new Type[] { typeof(string) }));
            routines.Add("/request/remove", new RoutineMethod("RemoveRequest",
                new string[] { "i" }, new Type[] { typeof(string) }));
            /*
            routines.Add("/request/add", new RoutineMethod("AddRequest",
                new string[] { "t", "a", }, new Type[] { typeof(string), typeof(int) }));
             */
        }

        /// <summary>
        /// Constructor for a local internal proxy's request handler.
        /// </summary>
        /// <param name="proxy">Proxy this request handler belongs to.</param>
        /// <param name="context">Client context.</param>
        public RemoteInternalRequestHandler(RCRemoteProxy proxy, HttpListenerContext context)
            : base(proxy, context, routines, defaultMethod)
        {
            _requestTimeout = LOCAL_REQUEST_PACKAGE_DEFAULT_TIMEOUT;
        }

        /// <summary>The proxy that this request belongs to.</summary>
        public RCRemoteProxy Proxy
        {
            get { return (RCRemoteProxy)_proxy; }
        }

        #region Proxy Control Methods
        /// <summary>
        /// Client changes richness.
        /// </summary>
        public Response RichnessRequest(string richnessString)
        {
            int userid = UserIDCookieValue;
            Richness richness;
            try
            {
                richness = (Richness)Enum.Parse(typeof(Richness), richnessString, true);
            }
            catch (Exception)
            {
                throw new HttpException(HttpStatusCode.BadRequest, "unknown richness setting: " + richnessString);
            }
            // Get the user settings
            RCUserSettings settings = Proxy.GetUserSettings(Context.Request.RemoteEndPoint, userid);
            // and change richness
            settings.richness = richness;
            Logger.Debug("Richness of user " + userid + " from Local Proxy " + 
                Context.Request.RemoteEndPoint + " was set to: " + richness);
            return new Response("Richness set.");
        }

        /// <summary>Dummy.</summary>
        public override void DispatchRequest(object nullObj)
        {
            // dummy
        }

        /// <summary>
        /// Removes the request from Ruralcafe's queue.
        /// </summary>
        public Response RemoveRequest(string itemId)
        {
            Proxy.DequeueRequest(itemId);
            return new Response("Removed request.");
        }

        #endregion
    }
}
