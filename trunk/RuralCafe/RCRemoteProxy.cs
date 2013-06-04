/*
   Copyright 2010 Jay Chen

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.

*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Threading;
using System.Collections;

namespace RuralCafe
{
    /// <summary>
    /// Remote proxy implementation, inherits from RCProxy.
    /// </summary>
    public class RCRemoteProxy : RCProxy
    {
        /// <summary>
        /// user id -> user settings.
        /// </summary>
        private Dictionary<int, RCUserSettings> _userSettings;

        /// <summary>
        /// Constructor for remote proxy.
        /// </summary>
        /// <param name="listenAddress">Address to listen for requests on.</param>
        /// <param name="listenPort">Port to listen for requests on.</param>
        /// <param name="proxyPath">Path to the proxy's executable.</param>
        /// <param name="cachePath">Path to the proxy's cache.</param>
        /// <param name="packagesPath">Path to the proxy's packages</param>
        /// <param name="logsPath">Path to the proxy's logs</param>
        public RCRemoteProxy(IPAddress listenAddress, int listenPort, string proxyPath, 
            string cachePath, string packagesPath)
            : base(REMOTE_PROXY_NAME, listenAddress, listenPort, proxyPath, 
            cachePath, packagesPath)
        {
            _requestQueue = new List<string>();
            _userSettings = new Dictionary<int, RCUserSettings>();
        }

        /// <summary>
        /// Starts the listener for connections from local proxy.
        /// The remote proxy could potentially serve multiple local proxies.
        /// </summary>
        public override void StartListener()
        {
           _logger.Info("Started Listener on " +
                _listenAddress + ":" + _listenPort);
            try
            {
                // create a listener for the proxy port
                HttpListener listener = new HttpListener();
                // prefix URL at which the listener will listen
                listener.Prefixes.Add("http://*:" + _listenPort + "/");
                listener.Start();

                // loop and listen for the next connection request
                while (true)
                {
                    // accept connections on the proxy port (blocks)
                    HttpListenerContext context = listener.GetContext();

                    // handle the accepted connection in a separate thread
                    RequestHandler requestHandler = RequestHandler.PrepareNewRequestHandler(this, context);
                    Thread proxyThread = new Thread(new ThreadStart(requestHandler.Go));
                    proxyThread.Start();
                }
            }
            catch (SocketException e)
            {
                _logger.Fatal("SocketException in StartRemoteListener, errorcode: " + e.NativeErrorCode, e);
            }
            catch (Exception e)
            {
                _logger.Fatal("Exception in StartRemoteListener", e);
            }
        }

        /// <summary>
        /// Gets the setting of the user with the given id. Creates a new settings object
        /// if there wasn't one before.
        /// </summary>
        /// <param name="userID">The users id.</param>
        /// <returns>The users settings.</returns>
        public RCUserSettings GetUserSettings(int userID)
        {
            lock (_userSettings)
            {
                if (!_userSettings.ContainsKey(userID))
                {
                    _userSettings[userID] = new RCUserSettings();
                }
                return _userSettings[userID];
            }
        }

        # region Unused

        // requests from the local proxy
        public List<string> _requestQueue;

        /// <summary>
        /// Add a request to the queue.
        /// Unused and untested at the moment.
        /// </summary>
        /// <param name="requestUri">The request URI.</param>
        /// <returns>True if the request is added, and false if the URI is already in the queue.</returns>
        public bool AddRequest(string requestUri)
        {
            if (!_requestQueue.Contains(requestUri))
            {
                _requestQueue.Add(requestUri);
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// Removes a request from the queue.
        /// Unused and untested at the moment.
        /// </summary>
        /// <param name="requestUri">The URI of the request.</param>
        /// <returns>True if the request is removed, and false if the URI is not in the queue.</returns>
        public bool RemoveRequest(string requestUri)
        {
            if (!_requestQueue.Contains(requestUri))
            {
                return false;
            }

            _requestQueue.Remove(requestUri);

            return true;
        }

        # endregion
    }
}
