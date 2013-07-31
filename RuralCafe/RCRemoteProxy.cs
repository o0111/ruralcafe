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
using RuralCafe.Util;

namespace RuralCafe
{
    /// <summary>
    /// Remote proxy implementation, inherits from RCProxy.
    /// </summary>
    public class RCRemoteProxy : RCProxy
    {
        /// <summary>
        /// local proxy IP:Port -> (user id -> user settings)
        /// </summary>
        private Dictionary<IPEndPoint, Dictionary<int, RCUserSettings>> _userSettings;

        /// <summary>
        /// Constructor for remote proxy.
        /// </summary>
        /// <param name="listenAddress">Address to listen for requests on.</param>
        /// <param name="listenPort">Port to listen for requests on.</param>
        /// <param name="proxyPath">Path to the proxy's executable.</param>
        /// <param name="cachePath">Path to the proxy's cache.</param>
        /// <param name="packagesPath">Path to the proxy's packages</param>
        public RCRemoteProxy(IPAddress listenAddress, int listenPort, string proxyPath, 
            string cachePath, string packagesPath)
            : base(REMOTE_PROXY_NAME, listenAddress, listenPort, proxyPath, 
            cachePath, packagesPath)
        {
            _userSettings = new Dictionary<IPEndPoint, Dictionary<int, RCUserSettings>>();
            _maxInflightRequests = 50; // XXX: Should be defaulted to something then fluctuate based on connection management
        }

        /*
        /// <summary>
        /// Starts the listener for connections from local proxy.
        /// The remote proxy could potentially serve multiple local proxies.
        /// </summary>
        public override void StartListener()
        {
           _logger.Info("Started Listener on " + _listenAddress + ":" + _listenPort);
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
                    if (_activeRequests >= Properties.Settings.Default.LOCAL_MAXIMUM_ACTIVE_REQUESTS)
                    {
                        _logger.Debug("Waiting. Active Requests: " + _activeRequests);
                        while (_activeRequests >= Properties.Settings.Default.LOCAL_MAXIMUM_ACTIVE_REQUESTS)
                        {
                            Thread.Sleep(100);
                        }
                    }

                    // accept connections on the proxy port (blocks)
                    HttpListenerContext context = listener.GetContext();

                    // create the handler for the request and queue it up for processing
                    RequestHandler requestHandler = RequestHandler.PrepareNewRequestHandler(this, context);

                    // Start own method StartRequestHandler in the thread, which also in- and decreases _activeRequests
                    Thread proxyThread = new Thread(new ParameterizedThreadStart(requestHandler.Go));
                    proxyThread.Start(requestHandler);
                }
            }
            catch (SocketException e)
            {
                _logger.Fatal("SocketException in StartListener, errorcode: " + e.NativeErrorCode, e);
            }
            catch (Exception e)
            {
                _logger.Fatal("Exception in StartListener", e);
            }
        }
        */
        /*
        /// <summary>
        /// Returns the first global request in the queue or null if no request exists.
        /// </summary>
        /// <returns>The first unsatisfied request by the next user or null if no request exists.</returns>
        public RequestHandler GetFirstGlobalRequest()
        {
            RequestHandler requestHandler = null;

            // lock to make sure nothing is added or removed
            lock (_globalRequestQueue)
            {
                if (_globalRequestQueue.Count > 0)
                {
                    requestHandler = _globalRequestQueue[0];
                }
            }
            return requestHandler;
        }
        */

        /// <summary>
        /// Gets the setting of the user with the given id. Creates a new settings object
        /// if there wasn't one before.
        /// </summary>
        /// <param name="localProxyIP">The local proxy's IP address and port.</param>
        /// <param name="userID">The users id.</param>
        /// <returns>The user's settings.</returns>
        public RCUserSettings GetUserSettings(IPEndPoint localProxyIP, int userID)
        {
            lock (_userSettings)
            {
                // Add Dictionary for the IP, if not yet there
                if(!_userSettings.ContainsKey(localProxyIP))
                {
                    _userSettings[localProxyIP] = new Dictionary<int, RCUserSettings>();
                }
                // Add empty settings for the user, if settings not yet there
                if (!_userSettings[localProxyIP].ContainsKey(userID))
                {
                    _userSettings[localProxyIP][userID] = new RCUserSettings();
                }
                return _userSettings[localProxyIP][userID];
            }
        }
        # region Queue

        /// <summary>
        /// Adds the request to the global queue and wakes up the dispatcher.
        /// </summary>
        /// <param name="requestHandler">The request handler to queue.</param>
        public void QueueRequest(RemoteRequestHandler requestHandler)
        {
            // Order is important!
            requestHandler = (RemoteRequestHandler) QueueRequestGlobalQueue(requestHandler);

            // Notify that a new request has been added. The Dispatcher will wake up if it was waiting.
            _newRequestEvent.Set();
        }

        /// <summary>
        /// Removes a single request from the queues.
        /// </summary>
        /// <param name="requestHandlerItemId">The item id of the request handlers to dequeue.</param>
        public void DequeueRequest(string requestHandlerItemId)
        {
            // Order is important!
            RemoteRequestHandler requestHandler = (RemoteRequestHandler) DequeueRequestGlobalQueue(requestHandlerItemId);
            // abort! XXX: no interrupt handling, but better than nothing for now.
            requestHandler.KillYourself();
        }

        # endregion
    }
}
