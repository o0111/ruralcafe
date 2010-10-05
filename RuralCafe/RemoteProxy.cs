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
    public class RemoteProxy : GenericProxy
    {
        public List<string> _requests;

        public bool AddRequest(string requestUri)
        {
            if (_requests.Contains(requestUri))
            {
                return false;
            }

            _requests.Add(requestUri);
            return true;
        }

        /* XXX: not implemented
        public void RemoveRequest(string requestUri)
        {
            if (_requests.Contains(requestUri))
            {
                _requests.Remove(requestUri);
            }
        }*/

        public RemoteProxy(IPAddress listenAddress, int listenPort, string proxyPath, 
            string cachePath, string packagePath, string logPath)
            : base(REMOTE_PROXY_NAME, listenAddress, listenPort, proxyPath, 
            cachePath, packagePath, logPath)
        {
            _requests = new List<string>();

            // initialize the cache
            InitializeCache(cachePath);
        }

        public void StartRemoteListener()
        {
            WriteDebug("Started Listener on " +
                _listenAddress + ":" + _listenPort);
            try
            {
                // Create a listener for the proxy port
                TcpListener sockServer = new TcpListener(_listenAddress, _listenPort);
                sockServer.Start();
                while (true)
                {
                    // Accept connections on the proxy port.
                    Socket socket = sockServer.AcceptSocket();

                    //Console.WriteLine("Remote Proxy: Got something");

                    // When AcceptSocket returns, it means there is a connection. Create
                    // an instance of the proxy server class and start a thread running.
                    RemoteRequest requestHandler = new RemoteRequest(this, socket);
                    Thread proxyThread = new Thread(new ThreadStart(requestHandler.Go));
                    proxyThread.Start();
                    // While the thread is running, the main program thread will loop around
                    // and listen for the next connection request.
                }
            }
            catch (SocketException ex)
            {
                WriteDebug("SocketException in StartRemoteListener, errorcode: " + ex.NativeErrorCode);
            }
            catch (Exception e)
            {
                WriteDebug("Exception in StartRemoteListener: " + e.StackTrace + " " + e.Message);
            }
        }
    }
}
