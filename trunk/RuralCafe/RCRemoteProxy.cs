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
        private Dictionary<IPEndPoint, Dictionary<int, RCProperties>> _Properties;

        /// <summary>
        /// Constructor for remote proxy.
        /// </summary>
        /// <param name="listenAddress">Address to listen for requests on.</param>
        /// <param name="listenPort">Port to listen for requests on.</param>
        /// <param name="httpsListenPort">Port the proxy listens on for HTTPS</param>
        /// <param name="proxyPath">Path to the proxy's executable.</param>
        /// <param name="maxCacheSize">The max cache size in bytes.</param>
        /// <param name="cachePath">Path to the proxy's cache.</param>
        /// <param name="packagesPath">Path to the proxy's packages</param>
        public RCRemoteProxy(IPAddress listenAddress, int listenPort, int httpsListenPort, string proxyPath,
            long maxCacheSize, string cachePath, string packagesPath)
            : base(REMOTE_PROXY_NAME, listenAddress, listenPort, httpsListenPort, proxyPath, 
            maxCacheSize, cachePath, packagesPath)
        {
            _Properties = new Dictionary<IPEndPoint, Dictionary<int, RCProperties>>();
            _maxInflightRequests = Properties.Network.Default.REMOTE_MAX_INFLIGHT_REQUESTS;
        }

        /// <summary>
        /// Gets the setting of the user with the given id. Creates a new settings object
        /// if there wasn't one before.
        /// </summary>
        /// <param name="localProxyIP">The local proxy's IP address and port.</param>
        /// <param name="userID">The users id.</param>
        /// <returns>The user's settings.</returns>
        public RCProperties GetProperties(IPEndPoint localProxyIP, int userID)
        {
            lock (_Properties)
            {
                // Add Dictionary for the IP, if not yet there
                if(!_Properties.ContainsKey(localProxyIP))
                {
                    _Properties[localProxyIP] = new Dictionary<int, RCProperties>();
                }
                // Add empty settings for the user, if settings not yet there
                if (!_Properties[localProxyIP].ContainsKey(userID))
                {
                    _Properties[localProxyIP][userID] = new RCProperties();
                }
                return _Properties[localProxyIP][userID];
            }
        }

        /// <summary>
        /// Handles a TCP request. The remote proxy behaves like a normal HTTPS proxy.
        /// </summary>
        /// <param name="clientObject">The tcp client from the accepted connection.</param>
        protected override void HandleTCPRequest(object clientObject)
        {
            TcpClient inClient = clientObject as TcpClient;
            TcpClient outClient = null;

            try
            {
                NetworkStream clientStream = inClient.GetStream();
                StreamReader clientReader = new StreamReader(clientStream);
                StreamWriter clientWriter = new StreamWriter(clientStream);

                // Read initial request.
                List<String> connectRequest = new List<string>();
                string line;
                while (!String.IsNullOrEmpty(line = clientReader.ReadLine()))
                {
                    connectRequest.Add(line);
                }
                if (connectRequest.Count == 0)
                {
                    throw new Exception("Invalid or no CONNECT request.");
                }

                string[] requestLine0Split = connectRequest[0].Split(' ');
                if (requestLine0Split.Length < 3)
                {
                    throw new Exception("Invalid or no CONNECT request.");
                }
                // Check if it is CONNECT
                string method = requestLine0Split[0];
                if (!method.Equals("CONNECT"))
                {
                    throw new Exception("Invalid or no CONNECT request.");
                }
                // Get host and port
                string requestUri = requestLine0Split[1];
                string[] uriSplit = requestUri.Split(new char[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
                if (uriSplit.Length < 2)
                {
                    throw new Exception("Invalid or no CONNECT request.");
                }
                string host = uriSplit[0];
                int port = Int32.Parse(uriSplit[1]);

                // Connect to server
                outClient = new TcpClient(host, port);

                // Send 200 Connection Established to Client
                clientWriter.WriteLine("HTTP/1.0 200 Connection established\r\n\r\n");
                clientWriter.Flush();

                Logger.Debug("Established TCP connection for " + host + ":" + port);

                Thread clientThread = new Thread(() => TunnelTCP(inClient, outClient));
                Thread serverThread = new Thread(() => TunnelTCP(outClient, inClient));

                clientThread.Start();
                serverThread.Start();
            }
            catch (Exception e)
            {
                // Disconnent if connections still alive
                Logger.Debug("Closing TCP connection: ", e);
                try
                {
                    if (inClient.Connected)
                    {
                        inClient.Close();
                    }
                    if (outClient != null && outClient.Connected)
                    {
                        outClient.Close();
                    }
                }
                catch (Exception e1)
                {
                    Logger.Warn("Could not close the tcp connection: ", e1);
                }
            }
        }

        # region Queue

        /// <summary>
        /// Adds the request to the global queue and wakes up the dispatcher.
        /// </summary>
        /// <param name="requestHandler">The request handler to queue.</param>
        public void AddRequest(RemoteRequestHandler requestHandler)
        {
            // Order is important!
            requestHandler = (RemoteRequestHandler) AddRequestGlobalQueue(requestHandler);

            // Notify that a new request has been added. The Dispatcher will wake up if it was waiting.
            _requestEvent.Set();
        }

        /// <summary>
        /// Removes a single request from the queues.
        /// </summary>
        /// <param name="requestHandlerItemId">The item id of the request handlers to dequeue.</param>
        public void RemoveRequest(string requestHandlerItemId)
        {
            // Order is important!
            RemoteRequestHandler requestHandler = (RemoteRequestHandler) RemoveRequestGlobalQueue(requestHandlerItemId);
            // abort! XXX: no interrupt handling, but better than nothing for now.
            // XXX: if the request is removed from the queue already, we can't do much to it since we're not storing active requests somewhere right now
            // XXX: since this interruption doesn't work well anyway we might as well not do anything to requests that are already downloading until it works in its entirety.
            // requestHandler.KillYourself(); // XXX: beware - requestHandler can be null!
        }

        # endregion
    }
}
