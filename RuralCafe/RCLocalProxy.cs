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
using BzReader;

namespace RuralCafe
{
    /// <summary>
    /// Local proxy implementation, inherits from GenericProxy.
    /// </summary>
    public class RCLocalProxy : RCProxy
    {
        // RuralCafe pages path
        private string _uiPagesPath;
        private string _rcSearchPage;
        private string _indexPath;
        private string _wikiDumpPath;
        //public int _activeRequests;
        //public int MAXIMUM_ACTIVE_REQUESTS = 50;

        // remoteProxy
        private WebProxy _remoteProxy;

        // big queue for lining up requests to remote proxy
        private List<LocalRequestHandler> _globalRequestQueue;
        // dictionary of linked lists of requests made by each client
        private Dictionary<IPAddress, List<LocalRequestHandler>> _clientRequestQueueMap;
        // dictionary of last requested page by each client
        private Dictionary<IPAddress, LocalRequestHandler> _clientLastRequestMap;

        // notifies that a new request has arrived
        private AutoResetEvent _newRequestEvent;

        // state for maintaining the time for request/responses for measuring the ETA
        private TimeSpan _averageTimePerRequest;
        
        // wiki indices (currently only wikipedia)
        private static Dictionary<string, Indexer> _wikiIndices = new Dictionary<string, Indexer>();


        #region Property accessors

        /// <summary>Path to the RC UI pages.</summary>
        public string UIPagesPath
        {
            get { return _uiPagesPath; }
        }
        /// <summary>Path to the RC search page.</summary>
        public string RCSearchPage
        {
            get { return _rcSearchPage; }
        }
        /// <summary>Path to the proxy's wiki dump.</summary>
        public string WikiDumpPath
        {
            get { return _wikiDumpPath; }
        }
        /// <summary>Path to the proxy's index.</summary>
        public string IndexPath
        {
            get { return _indexPath; }
        }
        /// <summary>WebProxy information for the remote proxy.</summary>
        public WebProxy RemoteProxy
        {
            get { return _remoteProxy; }
        }
        /// <summary>The indices for wiki dumps.</summary>
        public static Dictionary<string, Indexer> WikiIndices
        {
            get { return _wikiIndices; }
        }

        #endregion

        /// <summary>
        /// Construtor for LocalProxy.
        /// </summary>
        /// <param name="listenAddress">Address to listen for requests on.</param>
        /// <param name="listenPort">Port to listen for requests on.</param>
        /// <param name="proxyPath">Path to the proxy's executable.</param>
        /// <param name="indexPath">Path to the proxy's index.</param>
        /// <param name="cachePath">Path to the proxy's cache.</param>
        /// <param name="wikiDumpPath">Path to the wiki dump file.</param>
        /// <param name="packagePath">Path to the downloaded packages.</param>
        /// <param name="logsPath">Path to the proxy's logs.</param>
        public RCLocalProxy(IPAddress listenAddress, int listenPort, string proxyPath, string indexPath,
            string cachePath, string wikiDumpPath, string packagesPath, string logsPath)
            : base(LOCAL_PROXY_NAME, listenAddress, listenPort, proxyPath, 
            cachePath, packagesPath, logsPath)
        {
            //_activeRequests = 0;
            _uiPagesPath = proxyPath + "RuralCafePages" + Path.DirectorySeparatorChar;
            _indexPath = indexPath;
            _wikiDumpPath = wikiDumpPath;
            _globalRequestQueue = new List<LocalRequestHandler>();
            _clientRequestQueueMap = new Dictionary<IPAddress, List<LocalRequestHandler>>();
            _clientLastRequestMap = new Dictionary<IPAddress, LocalRequestHandler>();
            _newRequestEvent = new AutoResetEvent(false);
            _averageTimePerRequest = new TimeSpan(0);

            bool success = false;

            // initialize the index
            success = InitializeIndex(indexPath);
            if (!success)
            {
                Console.WriteLine("Error initializing the local proxy index.");
            }

            // initialize the wiki index
            success = InitializeWikiIndex(wikiDumpPath);
            if (!success)
            {
                Console.WriteLine("Error initializing the local proxy wiki index.");
            }
        }

        /// <summary>
        /// Sets the remote proxy address and port.
        /// </summary>
        /// <param name="proxyAddress">Remote proxy address.</param>
        /// <param name="proxyPort">Remote proxy port</param>
        public void SetRemoteProxy(IPAddress proxyAddress, int proxyPort)
        {
            if (proxyAddress == null)
            {
                _remoteProxy = null;
            }
            else
            {
                _remoteProxy = new WebProxy(proxyAddress.ToString(), proxyPort);
            }
        }

        /// <summary>
        /// Initializes the index.
        /// </summary>
        /// <param name="path">Path to the index.</param>
        /// <returns>True or false for success or not.</returns>
        private bool InitializeIndex(string indexPath)
        {
            bool created;
            created = IndexWrapper.EnsureIndexExists(indexPath);
            if (!created)
            {
                return false;
            }
            return true;
        }
        
        /// <summary>
        /// Initialize the wiki index.
        /// </summary>
        /// <param name="dumpFile">The name of the wiki dump file.</param>
        /// <returns>True or false for success or not.</returns>
        private bool InitializeWikiIndex(string dumpFile)
        {
            // check if the file exists
            if (!File.Exists(dumpFile))
            {
                return false;
            }

            // check if the index exists
            Indexer ixr = new Indexer(dumpFile);
            if (!ixr.IndexExists)
            {
                return false;
            }

            // load the index
            _wikiIndices.Add(dumpFile.ToLowerInvariant(), ixr);
			
            return true;
        }

        /// <summary>
        /// Sets the search page interface to present to clients.
        /// </summary>
        /// <param name="searchPage">Search page filename.</param>
        public void SetRCSearchPage(string searchPage)
        {
            _rcSearchPage = "http://www.ruralcafe.net/" + searchPage;
        }

        /// <summary>
        /// Starts the listener for connections from clients.
        /// </summary>
        public override void StartListener()
        {
            WriteDebug("Started Listener on " + _listenAddress + ":" + _listenPort);
            try
            {
                // create a listener for the proxy port
                TcpListener sockServer = new TcpListener(_listenAddress, _listenPort);
                sockServer.Start();

                // loop and listen for the next connection request
                while (true)
                {
                    /*
                    while (_activeRequests >= MAXIMUM_ACTIVE_REQUESTS)
                    {
                        Thread.Sleep(100);
                    }*/

                    // accept connections on the proxy port (blocks)
                    Socket socket = sockServer.AcceptSocket();
                    //_activeRequests++;

                    // handle the accepted connection in a separate thread
                    LocalRequestHandler requestHandler = new LocalRequestHandler(this, socket);
                    Thread proxyThread = new Thread(new ThreadStart(requestHandler.Go));
                    //proxyThread.Name = String.Format("LocalRequest" + socket.RemoteEndPoint.ToString());
                    proxyThread.Start();
                }
            }
            catch (SocketException ex)
            {
                WriteDebug("SocketException in StartLocalListener, errorcode: " + ex.NativeErrorCode);
            }
            catch (IOException e)
            {
                WriteDebug("Exception in StartLocalListener: " + e.StackTrace + " " + e.Message);
            }
        }

        /// <summary>
        /// Starts the dispatcher which requests pages from the remote proxy.
        /// Currently makes one request at a time via a single TCP connection.
        /// </summary>
        public void StartDispatcher()
        {
            WriteDebug("Started Requester");
            // go through the outstanding requests forever
            while (true)
            {
                LocalRequestHandler requestHandler = PopGlobalRequest();
                if (requestHandler != null)
                {
                    if (_gatewayProxy != null)
                    {
                        requestHandler.RCRequest.SetProxy(_gatewayProxy, RequestHandler.WEB_REQUEST_DEFAULT_TIMEOUT);
                    }
                    else
                    {
                        requestHandler.RCRequest.SetProxy(_remoteProxy, RequestHandler.WEB_REQUEST_DEFAULT_TIMEOUT);
                    }
                    // save the request file as a package
                    requestHandler.RCRequest.CacheFileName = requestHandler.PackageFileName;

                    requestHandler.RequestStatus = (int)RequestHandler.Status.Requested;

                    WriteDebug("dispatching to remote proxy: " + requestHandler.RequestUri);
                    long bytesDownloaded = requestHandler.RCRequest.DownloadToCache();

                    if (bytesDownloaded > -1)
                    {
                        WriteDebug("unpacking: " + requestHandler.RequestUri);
                        long unpackedBytes = Package.Unpack(requestHandler, _indexPath);
                        if (unpackedBytes > 0)
                        {
                            WriteDebug("unpacked: " + requestHandler.RequestUri);
                            requestHandler.RCRequest.FileSize = unpackedBytes;
                            requestHandler.RequestStatus = (int)RequestHandler.Status.Completed;
                        }
                        else
                        {
                            WriteDebug("failed to unpack: " + requestHandler.RequestUri);
                            requestHandler.RequestStatus = (int)RequestHandler.Status.Failed;
                        }

                        // XXX: for benchmarking only
                        //SaveBenchmarkTimes(totalProcessingTime);
                    }
                    else
                    {
                        requestHandler.RequestStatus = (int)RequestHandler.Status.Failed;
                    }

                    requestHandler.FinishTime = DateTime.Now;
                    UpdateTimePerRequest(requestHandler.StartTime, requestHandler.FinishTime);

                    requestHandler.LogResponse();
                }
                else
                {
                    // wait for an add event
                    _newRequestEvent.WaitOne();
                }
            }
        }


        # region Request Queues Interface

        /// <summary>
        /// Adds the request to the global queue and client's queue.
        /// </summary>
        /// <param name="clientAddress">The IP address of the client.</param>
        /// <param name="request">The request to queue.</param>
        public void QueueRequest(IPAddress clientAddress, LocalRequestHandler requestHandler)
        {
            List<LocalRequestHandler> requestHandlers = null;
            // add the request to the client's queue
            lock (_clientRequestQueueMap)
            {
                if (_clientRequestQueueMap.ContainsKey(clientAddress))
                {
                    // get the queue of client requests
                    requestHandlers = _clientRequestQueueMap[clientAddress];
                }
                else
                {
                    // create the queue of client requests
                    requestHandlers = new List<LocalRequestHandler>();
                    _clientRequestQueueMap.Add(clientAddress, requestHandlers);
                }
            }

            // add the request to the client's queue
            if (requestHandlers != null)
            {
                lock (requestHandlers)
                {
                    if (!requestHandlers.Contains(requestHandler))
                    {
                        requestHandlers.Add(requestHandler);
                    }
                }
            }

            // add the request to the global queue
            lock (_globalRequestQueue)
            {
                if (_globalRequestQueue.Contains(requestHandler))
                {
                    int existingRequestIndex = _globalRequestQueue.IndexOf(requestHandler);
                    LocalRequestHandler existingRequestHandler = _globalRequestQueue[existingRequestIndex];
                    if (existingRequestHandler.RequestStatus == (int)RequestHandler.Status.Failed)
                    {
                        // requeue failed request
                        _globalRequestQueue.Remove(existingRequestHandler);
                        _globalRequestQueue.Add(requestHandler);
                        _newRequestEvent.Set();
                    }
                    else
                    {
                        // request exists, do nothing
                    }
                }
                else
                {
                    // queue new request
                    _globalRequestQueue.Add(requestHandler);
                    _newRequestEvent.Set();
                }
            }
        }

        /// <summary>
        /// Removes all requests for a client from the queues.
        /// </summary>
        /// <param name="clientAddress">The IP address of the client.</param>
        public void ClearRequestQueues(IPAddress clientAddress)
        {
            // remove from the global queue
            lock (_globalRequestQueue)
            {
                if (_clientRequestQueueMap.ContainsKey(clientAddress))
                {
                    List<LocalRequestHandler> requestHandlers = _clientRequestQueueMap[clientAddress];

                    foreach (LocalRequestHandler request in requestHandlers)
                    {
                        _globalRequestQueue.Remove(request);
                    }
                }
            }

            // remove the client address' request queue map.
            lock (_clientRequestQueueMap)
            {
                if (_clientRequestQueueMap.ContainsKey(clientAddress))
                {
                    // lock to prevent any additions to the clientRequests
                    List<LocalRequestHandler> requestHandlers = _clientRequestQueueMap[clientAddress];
                    lock (requestHandlers)
                    {
                        _clientRequestQueueMap.Remove(clientAddress);
                    }
                }
            }
        }

        /// <summary>
        /// Removes a single request from the queues.
        /// </summary>
        /// <param name="clientAddress">The IP address of the client.</param>
        /// <param name="request">The request to dequeue.</param>
        public void DequeueRequest(IPAddress clientAddress, LocalRequestHandler requestHandler)
        {
            // remove the request from the global queue
            lock (_globalRequestQueue)
            {
                if (_globalRequestQueue.Contains(requestHandler))
                {
                    _globalRequestQueue.Remove(requestHandler);
                }
            }

            // remove the request from the client's queue
            // don't need to lock the _clientRequestQueueMap for reading
            if (_clientRequestQueueMap.ContainsKey(clientAddress))
            {
                List<LocalRequestHandler> requestHandlers = _clientRequestQueueMap[clientAddress];

                lock (requestHandlers)
                {
                    if (requestHandlers.Contains(requestHandler))
                    {
                        requestHandlers.Remove(requestHandler);
                    }
                }
            }
        }

        /// <summary>
        /// Returns the first global request and removes it from the queue.
        /// </summary>
        /// <returns>Returns the first global request in the queue or null if no request exists.</returns>
        public LocalRequestHandler PopGlobalRequest()
        {
            LocalRequestHandler requestHandler = null;

            // lock to make sure nothing is added or removed
            lock (_globalRequestQueue)
            {
                if (_globalRequestQueue.Count > 0)
                {
                    requestHandler = _globalRequestQueue[0];
                    _globalRequestQueue.RemoveAt(0);
                }
            }
            return requestHandler;
        }

        /// <summary>
        /// Gets the last request for a client.
        /// </summary>
        /// <param name="clientAddress">The IP address of the client.</param>
        /// <returns>The last request of the client or null if it does not exist.</returns>
        public LocalRequestHandler GetLatestRequest(IPAddress clientAddress)
        {
            LocalRequestHandler requestHandler = null;
            lock (_clientLastRequestMap)
            {
                if (_clientLastRequestMap.ContainsKey(clientAddress))
                {
                    requestHandler = _clientLastRequestMap[clientAddress];
                }
            }
            return requestHandler;
        }

        /// <summary>
        /// Sets the last request for a client.
        /// </summary>
        /// <param name="clientAddress">The IP address of the client.</param>
        /// <param name="request">The request to set as the last request.</param>
        public void SetLastRequest(IPAddress clientAddress, LocalRequestHandler requestHandler)
        {
            lock (_clientLastRequestMap)
            {
                if (!_clientLastRequestMap.ContainsKey(clientAddress))
                {
                    _clientLastRequestMap.Add(clientAddress, requestHandler);
                }
                else
                {
                    _clientLastRequestMap[clientAddress] = requestHandler;
                }
            }
            return;
        }

        /// <summary>
        /// Gets the request queue for a particular client.
        /// </summary>
        /// <param name="clientAddress">The IP address of the client.</param>
        /// <returns>A list of the requests that belong to a client or null if they does not exist.</returns>
        public List<LocalRequestHandler> GetRequests(IPAddress clientAddress)
        {
            List<LocalRequestHandler> requestHandlers = null;
            lock (_clientLastRequestMap)
            {
                if (_clientRequestQueueMap.ContainsKey(clientAddress))
                {
                    requestHandlers = _clientRequestQueueMap[clientAddress];
                }
            }
            return requestHandlers;
        }

        # endregion

        # region Timing and ETA

        /// <summary>
        /// updates the time per request given a timespan
        /// Exponential moving average alpha = 0.2
        /// </summary>
        private void UpdateTimePerRequest(DateTime startTime, DateTime finishTime) 
        {
            TimeSpan totalProcessingTime = finishTime - startTime;

            double alpha = 0.2;
            if (_averageTimePerRequest.CompareTo(new TimeSpan(0)) == 0)
            {
                _averageTimePerRequest = totalProcessingTime;
            }
            else
            {
                _averageTimePerRequest = new TimeSpan(0, 0, (int)(alpha * totalProcessingTime.TotalSeconds + (1 - alpha) * _averageTimePerRequest.TotalSeconds));
            }
        }

        // predicate object for ETA calculations
        private LocalRequestHandler _predicateObj = null;

        /// <summary>
        /// Returns the number of seconds until request is expected to be satisfied.
        /// Calculates the ETA by looking at the average satisfied time and the position of this request.
        /// </summary>
        /// <param name="request">The request for which we want the ETA.</param>
        /// <returns>ETA in seconds.</returns>
        public int ETA(LocalRequestHandler requestHandler)
        {
            // set the predicate object
            _predicateObj = requestHandler;
            int requestPosition = _globalRequestQueue.FindIndex(SameRCRequestPredicate);
            // +2 since -1 if the item doesn't exist (which means its being serviced now)
            return (int)((requestPosition + 2) * _averageTimePerRequest.TotalSeconds);
        }

        /// <summary>
        /// Predicate method for findindex in ETA.
        /// </summary>
        /// <param name="requestObj">The other request object.</param>
        /// <returns>True or false for match or no match.</returns>
        private bool SameRCRequestPredicate(LocalRequestHandler requestHandler)
        {
            if (_predicateObj == null || requestHandler == null)
            {
                return false;
            }

            if (_predicateObj.RequestUri == requestHandler.RequestUri)
            {
                return true;
            }
            return false;
        }

        # endregion

        # region Unused

        /// <summary>
        /// Gets the number of outstanding requests for a client.
        /// Unused at the moment.
        /// </summary>
        /// <param name="clientAddress">The IP address of the client.</param>
        /// <returns>The number of outstanding requests for the client.</returns>
        public int NumQueuedRequests(IPAddress clientAddress)
        {
            int count = 0;
            lock (_clientRequestQueueMap)
            {
                if (_clientRequestQueueMap.ContainsKey(clientAddress))
                {
                    // don't bother locking client requests since it can't be deleted while holding the previous lock
                    List<LocalRequestHandler> requestHandlers = _clientRequestQueueMap[clientAddress];
                    count = requestHandlers.Count;
                }
            }
            return count;
        }

        /// <summary>
        /// Returns the number of satisfied requests.
        /// Unused at the moment.
        /// </summary>
        /// <param name="clientAddress">The IP address of the client.</param>
        /// <returns>The number of satisfied requests for the client.</returns>
        public int NumFinishedRequests(IPAddress clientAddress)
        {
            int count = 0;
            lock (_clientRequestQueueMap)
            {
                if (_clientRequestQueueMap.ContainsKey(clientAddress))
                {
                    // don't bother locking client requests since it can't be deleted while holding the previous lock
                    List<LocalRequestHandler> requestHandlers = _clientRequestQueueMap[clientAddress];
                    foreach (LocalRequestHandler requestHandler in requestHandlers)
                    {
                        if (requestHandler.RequestStatus == (int)RequestHandler.Status.Completed ||
                            requestHandler.RequestStatus == (int)RequestHandler.Status.Failed)
                        {
                            count++;
                        }
                    }
                }
            }
            return count;
        }

        # endregion
    }
}