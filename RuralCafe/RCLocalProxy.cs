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

namespace RuralCafe
{
    /// <summary>
    /// Used for the requests without user. Like this we have both list and
    /// dictionary functionality. Both is required.
    /// </summary>
    public class LRHDict : KeyedCollection<int, KeyValuePair<int,LocalRequestHandler>>
    {
        public LRHDict() : base() { }
        protected override int GetKeyForItem(KeyValuePair<int, LocalRequestHandler> item)
        {
            return item.Key;
        }
    }

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
        private int _activeRequests;
        private int MAXIMUM_ACTIVE_REQUESTS;

        // remoteProxy
        private WebProxy _remoteProxy;

        // big queue for lining up requests to remote proxy
        private List<LocalRequestHandler> _globalRequestQueue;
        // dictionary of linked lists of requests made by each client
        private Dictionary<int, List<LocalRequestHandler>> _clientRequestQueueMap;
        // dictionary of last requested page by each client
        private Dictionary<int, LocalRequestHandler> _clientLastRequestMap;
        // dictionary of requests without a user. They await to be "added" via trotro by a specific user
        private LRHDict _requestsWithoutUser;
        private const int _requestsWithoutUserCapacity = 50;
        // Random for the keys of the above Dictionary
        private Random _random;

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
        /// <param name="maxRequests">Maximum active requests.</param>
        public RCLocalProxy(IPAddress listenAddress, int listenPort, string proxyPath, string indexPath,
            string cachePath, string wikiDumpPath, string packagesPath, int maxRequests)
            : base(LOCAL_PROXY_NAME, listenAddress, listenPort, proxyPath,
            cachePath, packagesPath)
        {
            _activeRequests = 0;
            _uiPagesPath = proxyPath + "RuralCafePages" + Path.DirectorySeparatorChar;
            _indexPath = indexPath;
            _wikiDumpPath = wikiDumpPath;
            _globalRequestQueue = new List<LocalRequestHandler>();
            _clientRequestQueueMap = new Dictionary<int, List<LocalRequestHandler>>();
            _clientLastRequestMap = new Dictionary<int, LocalRequestHandler>();
            _requestsWithoutUser = new LRHDict();
            _random = new Random();
            _newRequestEvent = new AutoResetEvent(false);
            _averageTimePerRequest = new TimeSpan(0);
            MAXIMUM_ACTIVE_REQUESTS = maxRequests;

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

            // FIXME remove
            // load previous state
            //success = ReadLog(proxyPath + logsPath);
            //if (!success)
            //{
            //    Console.WriteLine("Error reading log.");
            //}
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
            return IndexWrapper.EnsureIndexExists(indexPath);
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
        /// Checks to see if this proxy even has any Wiki indices
        /// </summary>
        public bool HasWikiIndices()
        {
            return (_wikiIndices.Count > 0);
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
                    //XXX: Debug Console Print
                    if (_activeRequests >= MAXIMUM_ACTIVE_REQUESTS)
                    {
                        Console.WriteLine("Waiting. Active Requests: " + _activeRequests);
                        while (_activeRequests >= MAXIMUM_ACTIVE_REQUESTS)
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
                throw new ArgumentException("localRequestHandler must be of type LocalRequestHandler");
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
                // XXX: Why only when slow!?
                if (NetworkStatus == NetworkStatusCode.Slow)
                {
                    LocalRequestHandler requestHandler = PopGlobalRequest();
                    if (requestHandler != null)
                    {
                        if (requestHandler.RequestStatus != RequestHandler.Status.Pending)
                        {
                            // skip requests in global queue that are not pending, probably requeued from log
                            continue;
                        }
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

                        requestHandler.LogServerResponse();
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

        // FIXME remove!
        /// <summary>
        /// Read log from directory and add to the requests queue, update the itemId.
        /// Called upon LocalProxy initialization.
        /// </summary>
        /// <param name="logPath">Relative or absolute path for the logs.</param>
        private bool ReadLog(string logPath)
        {
            int highestRequestId = 1;
            Dictionary<string, List<string>> loggedRequestQueueMap = new Dictionary<string, List<string>>();

            string debugFile = DateTime.Now.ToString("s") + "-debug.log";
            debugFile = debugFile.Replace(':', '.');

            try
            {
                if (!Directory.Exists(logPath))
                {
                    return false;
                }

                DirectoryInfo directory = new DirectoryInfo(logPath);
                var files = directory.GetFiles("*messages.log").OrderByDescending(f => f.LastWriteTime);
                int numFiles = files.Count();
                if (numFiles == 1)
                {
                    return true;
                }
                FileInfo currentFile = files.ElementAt(1);

                FileStream fs = currentFile.OpenRead();
                Console.WriteLine("Parsing log: " + currentFile);
                TextReader tr = new StreamReader(fs);

                uint linesParsed = 0;
                string line = tr.ReadLine();
                string[] lineTokens;

                while (line != null)
                {
                    linesParsed++;
                    //Console.WriteLine("Parsing line: " + line);
                    lineTokens = line.Split(' ');

                    string requestId = "";
                    if (lineTokens.Length > 0)
                    {
                        try
                        {
                            requestId = lineTokens[0];
                            int requestId_i = Int32.Parse(requestId);
                            if (requestId_i > highestRequestId)
                            {
                                highestRequestId = requestId_i;
                            }
                        }
                        catch (Exception)
                        {
                            // do nothing
                        }
                    }
                    // maximum number of tokens is 100
                    if (lineTokens.Length >= 100 || lineTokens.Length <= 5)
                    {
                        //Console.WriteLine("Error, tokens do not fit in array, line " + linesParsed);
                        // read the next line
                        line = tr.ReadLine();
                        continue;
                    }

                    if (lineTokens.Length >= 9)
                    {
                        // make sure that its actually a search query
                        string clientAddress = lineTokens[4];
                        string httpCommand = lineTokens[5];
                        string requestUriString = lineTokens[6];
                        Uri requestUri = new Uri(requestUriString);
                        string refererUri = lineTokens[8];
                        string startTime = lineTokens[1] + " " + lineTokens[2] + " " + lineTokens[3];

                        if ((httpCommand == "GET") && requestUri.AbsolutePath.StartsWith("http://www.ruralcafe.net/request/add"))
                        {
                            // Parse parameters
                            NameValueCollection qscoll = HttpUtility.ParseQueryString(requestUri.Query);
                            string targetUri = qscoll.Get("a");
                            if (targetUri == null)
                            {
                                // error
                                line = tr.ReadLine();
                                continue;
                            }
                            string fileName = RCRequest.UriToFilePath(targetUri);
                            string hashPath = RCRequest.GetHashPath(fileName);
                            string itemId = hashPath.Replace(Path.DirectorySeparatorChar.ToString(), "");

                            // add it to the queue
                            //Console.WriteLine("Adding to queue: " + targetUri);
                            List<string> logEntry = new List<string>();
                            logEntry.Add(requestId);
                            logEntry.Add(startTime);
                            logEntry.Add(clientAddress);
                            logEntry.Add(requestUriString);
                            logEntry.Add(refererUri);
                            if (!loggedRequestQueueMap.ContainsKey(itemId))
                            {
                                loggedRequestQueueMap.Add(itemId, logEntry);
                            }
                        }

                        if ((httpCommand == "GET") && requestUri.AbsolutePath.StartsWith("http://www.ruralcafe.net/request/remove"))
                        {
                            // Parse parameters
                            NameValueCollection qscoll = HttpUtility.ParseQueryString(requestUri.Query);
                            string itemId = qscoll.Get("i");
                            if (itemId == null)
                            {
                                // error
                                line = tr.ReadLine();
                                continue;
                            }
                            // remove it from the queue
                            //Console.WriteLine("Removing from queue: " + itemId);
                            if (loggedRequestQueueMap.ContainsKey(itemId))
                            {
                                loggedRequestQueueMap.Remove(itemId);
                            }
                        }
                    }
                    else if (lineTokens.Length >= 7)
                    {
                        requestId = lineTokens[0];
                        string httpCommand = lineTokens[4]; 
                        string requestUriString = lineTokens[6];
                        Uri requestUri = new Uri(requestUriString);
                        string status = lineTokens[6];

                        // Parse parameters
                        NameValueCollection qscoll = HttpUtility.ParseQueryString(requestUri.Query);
                        string targetUri = qscoll.Get("a");
                        if (targetUri == null)
                        {
                            // error
                            line = tr.ReadLine();
                            continue;
                        }
                        string fileName = RCRequest.UriToFilePath(targetUri);
                        string hashPath = RCRequest.GetHashPath(fileName);
                        string itemId = hashPath.Replace(Path.DirectorySeparatorChar.ToString(), "");

                        if ((httpCommand == "RSP") &&
                            loggedRequestQueueMap.ContainsKey(itemId) &&
                            (RequestHandler.Status) Enum.Parse(typeof(RequestHandler.Status), status) != RequestHandler.Status.Pending)
                        {
                            // parse the response
                            // check if its in the queue, if so, remove it
                            //Console.WriteLine("Removing from queue: " + targetUri);
                            loggedRequestQueueMap[itemId].Add(status);
                        }
                    }

                    // read the next line
                    line = tr.ReadLine();
                }

                tr.Close();


                // load the queued requests into the request queue
                foreach (string currentRequestId in loggedRequestQueueMap.Keys)
                {
                    LocalRequestHandler requestHandler = new LocalRequestHandler(this, null);
                    requestHandler.HandleLogRequest(loggedRequestQueueMap[currentRequestId]);
                }

                // update the nextId
                _nextRequestId = highestRequestId + 1;
            }
            catch (Exception)
            {
                Console.WriteLine("Could not read debug logs for saved state.");
                return false;
            }
            return true;
        }

        # region Request Queues Interface

        /// <summary>
        /// Adds the request to the global queue and client's queue.
        /// </summary>
        /// <param name="userId">The IP address of the client.</param>
        /// <param name="requestHandler">The request to queue.</param>
        public void QueueRequest(int userId, LocalRequestHandler requestHandler)
        {
            // if the request is already completed (due to HandleLogRequest) then don't add to global queue
            if (!(requestHandler.RequestStatus == RequestHandler.Status.Completed))
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
                        _newRequestEvent.Set();
                    }
                }
            }

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
            if (requestHandlers != null)
            {
                lock (requestHandlers)
                {
                    // add or replace
                    if (requestHandlers.Contains(requestHandler))
                    {
                        requestHandlers.Remove(requestHandler);
                        requestHandler.OutstandingRequests = requestHandler.OutstandingRequests - 1;
                    }
                    requestHandlers.Add(requestHandler);
                    requestHandler.OutstandingRequests = requestHandler.OutstandingRequests + 1;
                }
            }
        }

        /// <summary>
        /// Removes a single request from the queues.
        /// </summary>
        /// <param name="userId">The userId of the client.</param>
        /// <param name="requestHandler">The request to dequeue.</param>
        public void DequeueRequest(int userId, LocalRequestHandler requestHandler)
        {
            // remove the request from the global queue
            lock (_globalRequestQueue)
            {
                if (_globalRequestQueue.Contains(requestHandler))
                {
                    int existingRequestIndex = _globalRequestQueue.IndexOf(requestHandler);
                    requestHandler = _globalRequestQueue[existingRequestIndex];
                    // check to see if this URI is requested more than once
                    // if so, just decrement count
                    // if not, remove it
                    if (requestHandler.OutstandingRequests == 1)
                    {
                        _globalRequestQueue.Remove(requestHandler);
                    }
                    else
                    {
                        //requestHandler.OutstandingRequests = requestHandler.OutstandingRequests - 1;
                    }
                }
            }

            // remove the request from the client's queue
            // don't need to lock the _clientRequestQueueMap for reading
            if (_clientRequestQueueMap.ContainsKey(userId))
            {
                List<LocalRequestHandler> requestHandlers = _clientRequestQueueMap[userId];

                lock (requestHandlers)
                {
                    if (requestHandlers.Contains(requestHandler))
                    {
                        requestHandler.OutstandingRequests = requestHandler.OutstandingRequests - 1;
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
        /// Gets the request queue for a particular client.
        /// </summary>
        /// <param name="userId">The IP address of the client.</param>
        /// <returns>A list of the requests that belong to a client or null if they does not exist.</returns>
        public List<LocalRequestHandler> GetRequests(int userId)
        {
            List<LocalRequestHandler> requestHandlers = null;
            lock (_clientLastRequestMap)
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
        /// <param name="uri">The key.</param>
        /// <param name="handler">The value.</param>
        /// <returns>The id of the request.</returns>
        public int AddRequestWithoutUser(LocalRequestHandler handler)
        {
            int id = _random.Next();
            lock (_requestsWithoutUser)
            {
                if (_requestsWithoutUser.Count >= _requestsWithoutUserCapacity)
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
        /// <param name="uri">The uri.</param>
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
        /// <param name="requestHandler">The request for which we want the ETA.</param>
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
        /// <param name="requestHandler">The other request object's handler.</param>
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