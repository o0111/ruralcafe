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
using System.Collections.Specialized;
using System.Web;
using System.Collections.ObjectModel;
using RuralCafe.Lucenenet;
using Newtonsoft.Json;
using RuralCafe.Util;
using Microsoft.Win32;
using RuralCafe.Json;
using RuralCafe.Wiki;

namespace RuralCafe
{
    /// <summary>
    /// Local proxy implementation, inherits from GenericProxy.
    /// </summary>
    public class RCLocalProxy : RCProxy
    {
        /// <summary>
        /// Enum for the network status.
        /// </summary>
        public enum NetworkStatusCode
        {
            Online = 0,
            Slow = 1,
            Offline = 2
        };

        // Constants
        private const int REQUESTS_WITHOUT_USER_CAPACITY = 50;
        private const string QUEUES_FILENAME = "Queues.json";

        // RuralCafe pages path
        private string _uiPagesPath;
        private string _rcSearchPage;
        private string _indexPath;
        private string _wikiDumpPath;
        private int _activeRequests;

        // remoteProxy
        private WebProxy _remoteProxy;

        // Network status
        private NetworkStatusCode _networkStatus;

        // big queue for lining up requests to remote proxy
        private List<LocalRequestHandler> _globalRequestQueue;
        // dictionary of lists of requests made by each client
        private Dictionary<int, List<LocalRequestHandler>> _clientRequestQueueMap;
        // dictionary of requests without a user. They await to be "added" via trotro by a specific user
        private IntKeyedCollection<LocalRequestHandler> _requestsWithoutUser;
        // Random for the keys of the above Dictionary
        private Random _random;

        // notifies that a new request has arrived
        private AutoResetEvent _newRequestEvent;

        // state for maintaining the time for request/responses for measuring the ETA
        private TimeSpan _averageTimePerRequest;

        // the wiki wrapper
        private WikiWrapper _wikiWrapper;

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
        /// <summary>The wiki wrapper.</summary>
        public WikiWrapper WikiWrapper
        {
            get { return _wikiWrapper; }
        }
        /// <summary>The network status.</summary>
        public NetworkStatusCode NetworkStatus
        {
            get { return _networkStatus; }
            set { _networkStatus = value; }
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
        /// <param name="packagesPath">Path to the downloaded packages.</param>
        public RCLocalProxy(IPAddress listenAddress, int listenPort, string proxyPath, string indexPath,
            string cachePath, string wikiDumpPath, string packagesPath)
            : base(LOCAL_PROXY_NAME, listenAddress, listenPort, proxyPath,
            cachePath, packagesPath)
        {
            _activeRequests = 0;
            _uiPagesPath = proxyPath + "RuralCafePages" + Path.DirectorySeparatorChar;
            _indexPath = indexPath;
            _wikiDumpPath = wikiDumpPath;
            _globalRequestQueue = new List<LocalRequestHandler>();
            _clientRequestQueueMap = new Dictionary<int, List<LocalRequestHandler>>();
            _requestsWithoutUser = new IntKeyedCollection<LocalRequestHandler>();
            _random = new Random();
            _newRequestEvent = new AutoResetEvent(false);
            _averageTimePerRequest = new TimeSpan(0);

            _wikiWrapper = new WikiWrapper(wikiDumpPath);

            bool success = false;
            // initialize the index
            success = IndexWrapper.EnsureIndexExists(indexPath);
            if (!success)
            {
                _logger.Warn("Error initializing the local proxy index.");
            }

            // initialize the wiki index
            success = _wikiWrapper.InitializeWikiIndex();
            if (!success)
            {
                _logger.Warn("Error initializing the local proxy wiki index.");
            }

            // Deserialize the queue
            DeserializeQueue();
            // Tell the programm to serialize the queue before shutdown
            Program.AddShutDownDelegate(SerializeQueue);

            // Every x minutes, re-cluster the cachein an own thread.
            // TODO make timespan customizable.
            new Timer(StartClustering, null, TimeSpan.Zero, new TimeSpan(0, 30, 0));
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
        /// Sets the search page interface to present to clients.
        /// </summary>
        /// <param name="searchPage">Search page filename.</param>
        public void SetRCSearchPage(string searchPage)
        {
            _rcSearchPage = RequestHandler.RC_PAGE + searchPage;
        }

        /// <summary>
        /// Starts the clustering. This method is called periodically
        /// by a timer in a new thread.
        /// </summary>
        /// <param name="o">Ignored.</param>
        private void StartClustering(object o)
        {
            // TODO make field(s) for CreateClusters parameters
            ProxyCacheManager.CreateClusters(_uiPagesPath + "clusters.xml", 10);
        }

        /// <summary>
        /// Starts the listener for connections from clients.
        /// </summary>
        public override void StartListener()
        {
            _logger.Info("Started Listener on " + _listenAddress + ":" + _listenPort);
            try
            {
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

                    // handle the accepted connection in a separate thread
                    RequestHandler requestHandler = RequestHandler.PrepareNewRequestHandler(this, context);
                    // Start own method StartRequestHandler in the thread, which also in- and decreases _activeRequests
                    Thread proxyThread = new Thread(new ParameterizedThreadStart(this.StartRequestHandler));
                    proxyThread.Start(requestHandler);
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
        /// Invokes the <see cref="RequestHandler.Go"/> method. While it is running, the number of
        /// active requests is increased.
        /// </summary>
        /// <param name="requestHandler">The local or internal local request handler of type
        /// <see cref="RequestHandler"/></param>
        private void StartRequestHandler(Object requestHandler)
        {
            if (!(requestHandler is RequestHandler))
            {
                throw new ArgumentException("requestHandler must be of type RequestHandler");
            }
            // Increment number of active requests
            System.Threading.Interlocked.Increment(ref _activeRequests);
            // Start request handler
            ((RequestHandler)requestHandler).Go();
            // Decrement number of active requests
            System.Threading.Interlocked.Decrement(ref _activeRequests);
        }

        /// <summary>
        /// Starts the dispatcher which requests pages from the remote proxy.
        /// Currently makes one request at a time via a single TCP connection.
        /// </summary>
        public void StartDispatcher()
        {
            _logger.Info("Started Requester");
            // go through the outstanding requests forever
            while (true)
            {
                if (NetworkStatus == NetworkStatusCode.Slow)
                {
                    LocalRequestHandler requestHandler = GetFirstGlobalRequest();
                    if (requestHandler != null)
                    {
                        if (requestHandler.RequestStatus != RequestHandler.Status.Pending)
                        {
                            // skip requests in global queue that are not pending, probably requeued from log
                            lock (_globalRequestQueue)
                            {
                                _globalRequestQueue.RemoveAt(0);
                            }
                            continue;
                        }
                        // Save start time
                        requestHandler.StartTime = DateTime.Now;
                        if (_gatewayProxy != null)
                        {
                            requestHandler.RCRequest.SetProxyAndTimeout(_gatewayProxy, System.Threading.Timeout.Infinite);
                        }
                        else
                        {
                            requestHandler.RCRequest.SetProxyAndTimeout(_remoteProxy, System.Threading.Timeout.Infinite);
                        }
                        // save the request file as a package
                        requestHandler.RCRequest.CacheFileName = requestHandler.PackageFileName;

                        requestHandler.RequestStatus = RequestHandler.Status.Downloading;

                        _logger.Debug("dispatching to remote proxy: " + requestHandler.RequestUri);
                        long bytesDownloaded = requestHandler.RCRequest.DownloadToCache(true);

                        if (bytesDownloaded > 0)
                        {
                            // Get RC response headers
                            RCSpecificResponseHeaders headers = requestHandler.GetRCSpecificResponseHeaders();

                            long unpackedBytes = Package.Unpack(requestHandler, headers, _indexPath);
                            if (unpackedBytes > 0)
                            {
                                _logger.Debug("unpacked: " + requestHandler.RequestUri);
                                requestHandler.RCRequest.FileSize = unpackedBytes;
                                requestHandler.RequestStatus = RequestHandler.Status.Completed;
                            }
                            else
                            {
                                _logger.Warn("failed to unpack: " + requestHandler.RequestUri);
                                requestHandler.RequestStatus = RequestHandler.Status.Failed;
                            }

                            // XXX: for benchmarking only
                            //SaveBenchmarkTimes(totalProcessingTime);
                        }
                        else
                        {
                            requestHandler.RequestStatus = RequestHandler.Status.Failed;
                        }

                        requestHandler.FinishTime = DateTime.Now;
                        UpdateTimePerRequest(requestHandler.StartTime, requestHandler.FinishTime);

                        requestHandler.LogResponse();
                        // Remove from queue, as request is finished
                        lock (_globalRequestQueue)
                        {
                            _globalRequestQueue.RemoveAt(0);
                        }
                    }
                    else
                    {
                        // wait for an add event
                        _newRequestEvent.WaitOne();
                    }
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
        /// Adds the request to the global queue and client's queue and wakes up the dispatcher.
        /// </summary>
        /// <param name="userId">The user's id.</param>
        /// <param name="requestHandler">The request handler to queue.</param>
        public void QueueRequest(int userId, LocalRequestHandler requestHandler)
        {
            // Order is important!
            requestHandler = QueueRequestGlobalQueue(requestHandler);
            QueueRequestUserQueue(userId, requestHandler);

            // Notify that a new request has been added. The Dispatcher will wake up if it was waiting.
            _newRequestEvent.Set();
        }

        /// <summary>
        /// Adds the request handler to the global queue.
        /// </summary>
        /// <param name="requestHandler">The request handler to queue.</param>
        /// <returns>The request handler in the queue.
        /// Either the parameter or an already exiting equivalent RH in the queue.</returns>
        private LocalRequestHandler QueueRequestGlobalQueue(LocalRequestHandler requestHandler)
        {
            // add the request to the global queue
            lock (_globalRequestQueue)
            {
                if (_globalRequestQueue.Contains(requestHandler))
                {
                    // grab the existing handler instead of the new one
                    int existingRequestIndex = _globalRequestQueue.IndexOf(requestHandler);
                    requestHandler = _globalRequestQueue[existingRequestIndex];
                }
                else
                {
                    // queue new request
                    _globalRequestQueue.Add(requestHandler);
                }
                return requestHandler;
            }
        }

        /// <summary>
        /// Adds the request handler to the user's queue.
        /// </summary>
        /// <param name="userId">The user's id.</param>
        /// <param name="requestHandler">The request handler to queue.</param>
        private void QueueRequestUserQueue(int userId, LocalRequestHandler requestHandler)
        {
            List<LocalRequestHandler> requestHandlers = null;
            // add client queue, if it does not exist yet
            lock (_clientRequestQueueMap)
            {
                if (_clientRequestQueueMap.ContainsKey(userId))
                {
                    // get the queue of client requests
                    requestHandlers = _clientRequestQueueMap[userId];
                }
                else
                {
                    // create the queue of client requests
                    requestHandlers = new List<LocalRequestHandler>();
                    _clientRequestQueueMap.Add(userId, requestHandlers);
                }
            }

            // add the request to the client's queue
            lock (requestHandlers)
            {
                if (!requestHandlers.Contains(requestHandler))
                {
                    // Just add, if its not already contained.
                    requestHandlers.Add(requestHandler);
                    requestHandler.OutstandingRequests++;
                }
                
            }
        }

        /// <summary>
        /// Removes a single request from the queues.
        /// </summary>
        /// <param name="userId">The userId of the client.</param>
        /// <param name="requestHandlerItemId">The item id of the request handlers to dequeue.</param>
        public void DequeueRequest(int userId, string requestHandlerItemId)
        {
            // Order is important!
            DequeueRequestGlobalQueue(requestHandlerItemId);
            DequeueRequestUserQueue(userId, requestHandlerItemId);
        }

        /// <summary>
        /// Removes a single request from global queue.
        /// </summary>
        /// <param name="requestHandlerItemId">The item id of the request handlers to dequeue.</param>
        private void DequeueRequestGlobalQueue(string requestHandlerItemId)
        {
            // remove the request from the global queue
            lock (_globalRequestQueue)
            {
                // This gets the requestHandler with the same ID, if there is one
                LocalRequestHandler requestHandler =
                    _globalRequestQueue.FirstOrDefault(rh => rh.ItemId == requestHandlerItemId);
                if (requestHandler != null)
                {
                    // check to see if this URI is requested more than once
                    // if not, remove it
                    if (requestHandler.OutstandingRequests == 1)
                    {
                        _globalRequestQueue.Remove(requestHandler);
                    }
                }
            }
        }

        /// <summary>
        /// Removes a single request from user queue.
        /// </summary>
        /// <param name="userId">The userId of the client.</param>
        /// <param name="requestHandlerItemId">The item id of the request handlers to dequeue.</param>
        private void DequeueRequestUserQueue(int userId, string requestHandlerItemId)
        {
            // remove the request from the client's queue
            // don't need to lock the _clientRequestQueueMap for reading
            if (_clientRequestQueueMap.ContainsKey(userId))
            {
                List<LocalRequestHandler> requestHandlers = _clientRequestQueueMap[userId];
                lock (requestHandlers)
                {
                    // This gets the requestHandler with the same ID, if there is one
                    LocalRequestHandler requestHandler =
                        requestHandlers.FirstOrDefault(rh => rh.ItemId == requestHandlerItemId);
                    if (requestHandler != null)
                    {
                        requestHandler.OutstandingRequests--;
                        requestHandlers.Remove(requestHandler);
                    }
                }
            }
        }

        /// <summary>
        /// Returns the first global request in the queue or null if no request exists.
        /// </summary>
        /// <returns>The first unsatisfied request by the next user or null if no request exists.</returns>
        public LocalRequestHandler GetFirstGlobalRequest()
        {
            LocalRequestHandler requestHandler = null;

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

        /// <summary>
        /// Recreates the global queue from the client queues. Requests are ordered chronologically.
        /// </summary>
        private void FillGlobalQueueFromClientQueues()
        {
            lock (_globalRequestQueue)
            {
                // Empty glocal Request queue first
                _globalRequestQueue.Clear();
                foreach (List<LocalRequestHandler> requestHandlers in _clientRequestQueueMap.Values)
                {
                    lock (requestHandlers)
                    {
                        for (int i = 0; i < requestHandlers.Count; i++)
                        {
                            LocalRequestHandler requestHandler = requestHandlers[i];
                            // Only add requests not finished (or failed).
                            if (requestHandler.RequestStatus == RequestHandler.Status.Downloading ||
                                requestHandler.RequestStatus == RequestHandler.Status.Pending)
                            {
                                int index = _globalRequestQueue.IndexOf(requestHandler);
                                // if it already exists..
                                if (index != -1)
                                {
                                    // ..replace in user queue
                                    requestHandlers[i]= _globalRequestQueue[index];
                                }
                                else
                                {
                                    // Set status to pending (if it was being downloaded while the shutdown)
                                    requestHandler.RequestStatus = RequestHandler.Status.Pending;
                                    // queue new request
                                    _globalRequestQueue.Add(requestHandler);
                                }
                            }
                        }
                    }
                }
                // Sort the queue chronologically.
                _globalRequestQueue.Sort((x, y) => 
                    x.CreationTime > y.CreationTime ? 1 : 
                    (x.CreationTime == y.CreationTime ? 0 : -1));
            }
        }

        /// <summary>
        /// Gets the request queue for a particular client.
        /// </summary>
        /// <param name="userId">The id of the user.</param>
        /// <returns>A list of the requests that belong to a client or null if they do not exist.</returns>
        public List<LocalRequestHandler> GetRequests(int userId)
        {
            List<LocalRequestHandler> requestHandlers = null;
            lock (_clientRequestQueueMap)
            {
                if (_clientRequestQueueMap.ContainsKey(userId))
                {
                    requestHandlers = _clientRequestQueueMap[userId];
                }
            }
            return requestHandlers;
        }

        /// <summary>
        /// Adds a request, where the user is unknown yet, to the Dictionary
        /// </summary>
        /// <param name="handler">The value.</param>
        /// <returns>The id of the request.</returns>
        public int AddRequestWithoutUser(LocalRequestHandler handler)
        {
            int id = _random.Next();
            lock (_requestsWithoutUser)
            {
                if (_requestsWithoutUser.Count >= REQUESTS_WITHOUT_USER_CAPACITY)
                {
                    _requestsWithoutUser.RemoveAt(0);
                }
                _requestsWithoutUser.Add(new KeyValuePair<int,LocalRequestHandler>(id, handler));
            }
            return id;
        }

        /// <summary>
        /// Gets and removes the RequestHandler associated with the given URI. If the URI is no key
        /// in the Dictionary, an Exception is thrown.
        /// </summary>
        /// <param name="id">The id.</param>
        /// <returns>The LocalRequestHandler.</returns>
        public LocalRequestHandler PopRequestWithoutUser(int id)
        {
            lock (_requestsWithoutUser)
            {
                LocalRequestHandler result = _requestsWithoutUser[id].Value;
                _requestsWithoutUser.Remove(id);
                return result;
            }
        }

        # endregion
        #region Queue (de)serialization

        /// <summary>
        /// Serializes the queue(s). Called, when the proxy is being closed.
        /// </summary>
        public void SerializeQueue()
        {
            string filename = _proxyPath + STATE_DIRNAME + Path.DirectorySeparatorChar + QUEUES_FILENAME;
            Utils.CreateDirectoryForFile(filename);

            JsonSerializer serializer = new JsonSerializer();
            serializer.Converters.Add(new NameValueCollectionConverter());
            serializer.Converters.Add(new HttpWebRequestConverter());

            using (StreamWriter sw = new StreamWriter(filename))
            using (JsonWriter writer = new JsonTextWriter(sw))
            {
                serializer.Serialize(writer, _clientRequestQueueMap);
            }
            _logger.Info("Serialized queues.");
        }

        /// <summary>
        /// Deserializes the queues. Called when the program starts.
        /// </summary>
        public void DeserializeQueue()
        {
            string filename = _proxyPath + STATE_DIRNAME + Path.DirectorySeparatorChar + QUEUES_FILENAME;

            string fileContent = Utils.ReadFileAsString(filename);
            if (fileContent == "")
            {
                _logger.Info("No queue to deserialize.");
                // The file does not exist, we do not have to do anything.
                return;
            }
            // Deserialize
            try
            {
                _clientRequestQueueMap = JsonConvert.
                    DeserializeObject<Dictionary<int, List<LocalRequestHandler>>>(fileContent, 
                    new LocalRequestHandlerConverter(this));
                // Restore the global Queue
                FillGlobalQueueFromClientQueues();
                _logger.Info("Deserialized and restored queues.");
            }
            catch (Exception e)
            {
                _logger.Error("Queue deserialization failed: ", e);
            }
            
        }

        #endregion
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

        /// <summary>
        /// Returns the number of seconds until request is expected to be satisfied.
        /// Calculates the ETA by looking at the average satisfied time, the current running time of
        /// tfirst request and the position of this request.
        /// </summary>
        /// <param name="requestHandler">The request for which we want the ETA.</param>
        /// <returns>ETA in seconds.</returns>
        public int ETA(LocalRequestHandler requestHandler)
        {
            int requestPosition = _globalRequestQueue.IndexOf(requestHandler);

            if (requestPosition == -1)
            {
                // The request might have been finished ms ago. Otherwise sth. is wrong.
                return 0;
            }

            // Determine the time the first request is already running
            double secondsFirstRequestRunning = _averageTimePerRequest.TotalSeconds;
            LocalRequestHandler firstHandler = _globalRequestQueue[0];
            if(firstHandler != null)
            {
                secondsFirstRequestRunning = (DateTime.Now - firstHandler.StartTime).TotalSeconds;
            }
            if (secondsFirstRequestRunning > _averageTimePerRequest.TotalSeconds)
            {
                // This means, by avg. the first request should be ready, but it isn't
                secondsFirstRequestRunning = _averageTimePerRequest.TotalSeconds;
            }

            // Calculate remaining time
            return (int)(((requestPosition + 1) * _averageTimePerRequest.TotalSeconds) - secondsFirstRequestRunning);
        }

        # endregion
        # region Unused

        /// <summary>
        /// Gets the number of outstanding requests for a client.
        /// Unused at the moment.
        /// </summary>
        /// <param name="userId">The user's id.</param>
        /// <returns>The number of outstanding requests for the client.</returns>
        public int NumQueuedRequests(int userId)
        {
            int count = 0;
            lock (_clientRequestQueueMap)
            {
                if (_clientRequestQueueMap.ContainsKey(userId))
                {
                    // don't bother locking client requests since it can't be deleted while holding the previous lock
                    List<LocalRequestHandler> requestHandlers = _clientRequestQueueMap[userId];
                    count = requestHandlers.Count;
                }
            }
            return count;
        }

        /// <summary>
        /// Returns the number of satisfied requests.
        /// Unused at the moment.
        /// </summary>
        /// <param name="userId">The userId of the client.</param>
        /// <returns>The number of satisfied requests for the client.</returns>
        public int NumFinishedRequests(int userId)
        {
            int count = 0;
            lock (_clientRequestQueueMap)
            {
                if (_clientRequestQueueMap.ContainsKey(userId))
                {
                    // don't bother locking client requests since it can't be deleted while holding the previous lock
                    List<LocalRequestHandler> requestHandlers = _clientRequestQueueMap[userId];
                    foreach (LocalRequestHandler requestHandler in requestHandlers)
                    {
                        if (requestHandler.RequestStatus == RequestHandler.Status.Completed ||
                            requestHandler.RequestStatus == RequestHandler.Status.Failed)
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