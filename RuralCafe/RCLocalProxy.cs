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
using System.Windows.Forms;
using Microsoft.Win32;

namespace RuralCafe
{
    /// <summary>
    /// Local proxy implementation, inherits from GenericProxy.
    /// </summary>
    public class RCLocalProxy : RCProxy
    {
        // Constants
        private const int REQUESTS_WITHOUT_USER_CAPACITY = 50;
        private const string QUEUE_DIRNAME = "Queue";
        private const string GLOBAL_QUEUE_FILENAME = "GlobalQueue.json";

        // RuralCafe pages path
        private string _uiPagesPath;
        private string _rcSearchPage;
        private string _indexPath;
        private string _wikiDumpPath;
        private int _activeRequests;

        // remoteProxy
        private WebProxy _remoteProxy;

        // dictionary of lists of requests made by each client
        private IntKeyedCollection<List<LocalRequestHandler>> _clientRequestQueueMap;
        // The index of the client served last.
        private int _lastServedClientIndex = 0;
        // dictionary of requests without a user. They await to be "added" via trotro by a specific user
        private IntKeyedCollection<LocalRequestHandler> _requestsWithoutUser;
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
            _clientRequestQueueMap = new IntKeyedCollection<List<LocalRequestHandler>>();
            _requestsWithoutUser = new IntKeyedCollection<LocalRequestHandler>();
            _random = new Random();
            _newRequestEvent = new AutoResetEvent(false);
            _averageTimePerRequest = new TimeSpan(0);

            bool success = false;

            // initialize the index
            success = InitializeIndex(indexPath);
            if (!success)
            {
                _logger.Warn("Error initializing the local proxy index.");
            }

            // initialize the wiki index
            success = InitializeWikiIndex(wikiDumpPath);
            if (!success)
            {
                _logger.Warn("Error initializing the local proxy wiki index.");
            }

            // Tell the programm to serialize the queue before shutdown
            Program.AddShutDownDelegate(SerializeQueue);
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
        /// <param name="indexPath">Path to the index.</param>
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
                _logger.Debug("Parsing log: " + currentFile);
                TextReader tr = new StreamReader(fs);

                uint linesParsed = 0;
                string line = tr.ReadLine();
                string[] lineTokens;

                while (line != null)
                {
                    linesParsed++;
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
            catch (Exception e)
            {
                _logger.Warn("Could not read debug logs for saved state.", e);
                return false;
            }
            return true;
        }

        # region Request Queues Interface

        /// <summary>
        /// Adds the request to the client's queue.
        /// </summary>
        /// <param name="userId">The user's id.</param>
        /// <param name="requestHandler">The request handler to queue.</param>
        public void QueueRequest(int userId, LocalRequestHandler requestHandler)
        {
            List<LocalRequestHandler> requestHandlers = null;
            // add client queue, if it does not exist yet
            lock (_clientRequestQueueMap)
            {
                if (_clientRequestQueueMap.Contains(userId))
                {
                    // get the queue of client requests
                    requestHandlers = _clientRequestQueueMap[userId].Value;
                }
                else
                {
                    // create the queue of client requests
                    requestHandlers = new List<LocalRequestHandler>();
                    _clientRequestQueueMap.Add(new KeyValuePair<int, List<LocalRequestHandler>>(userId, requestHandlers));
                }
            }

            // add the request to the client's queue
            lock (requestHandlers)
            {
                // add or replace
                if (requestHandlers.Contains(requestHandler))
                {
                    requestHandlers.Remove(requestHandler);
                    requestHandler.OutstandingRequests = requestHandler.OutstandingRequests - 1;
                }
                requestHandlers.Add(requestHandler);
                requestHandler.OutstandingRequests++;
            }
            
            // Notify that a new request has been added. The Dispatcher will wake up if it was waiting.
            _newRequestEvent.Set();
        }

        /// <summary>
        /// Removes a single request from the queues.
        /// </summary>
        /// <param name="userId">The userId of the client.</param>
        /// <param name="requestHandler">The request to dequeue.</param>
        public void DequeueRequest(int userId, LocalRequestHandler requestHandler)
        {
            // remove the request from the client's queue
            // don't need to lock the _clientRequestQueueMap for reading
            if (_clientRequestQueueMap.Contains(userId))
            {
                List<LocalRequestHandler> requestHandlers = _clientRequestQueueMap[userId].Value;
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
        /// Returns the first RequestHandler in the list, whose request in not satisfied yet.
        /// This means his status is "Pending".
        /// </summary>
        /// <param name="clientList">A list of RequestHandlers.</param>
        /// <returns>The first unsatisfied RequestHandler or null if all requests are satisfied.</returns>
        private LocalRequestHandler GetFirstUnsatisfiedRequestHandler(List<LocalRequestHandler> clientList)
        {
            foreach(LocalRequestHandler lrh in clientList)
            {
                if (lrh.RequestStatus == RequestHandler.Status.Pending)
                {
                    // Unsatisfied
                    return lrh;
                }
            }
            return null;
        }

        /// <summary>
        /// Returns the first unsatisfied request by the next user.
        /// </summary>
        /// <returns>The first unsatisfied request by the next user or null if no request exists.</returns>
        public LocalRequestHandler PopGlobalRequest()
        {
            lock (_clientRequestQueueMap)
            {
                if (_clientRequestQueueMap.Count == 0)
                {
                    // If there are no clients, there is no next request.
                    return null;
                }
                // Loop through all clients, starting after the last served one.
                int index = (_lastServedClientIndex == _clientRequestQueueMap.Count - 1 ?
                    0 : _lastServedClientIndex + 1);
                int initialIndex = index;
                do
                {
                    List<LocalRequestHandler> clientList = _clientRequestQueueMap.ElementAt(index).Value;
                    LocalRequestHandler firstUnsatisfiedRequestHandler = GetFirstUnsatisfiedRequestHandler(clientList);
                    if (firstUnsatisfiedRequestHandler == null)
                    {
                        // All requests of this client have been satisfied
                        // Increment index or set to 0 if we're at the end.
                        index = (index == _clientRequestQueueMap.Count - 1 ? 0 : index + 1);
                        continue;
                    }
                    // This client will now be served
                    _lastServedClientIndex = index;
                    return firstUnsatisfiedRequestHandler;
                } while(index != initialIndex);
            }
            // There is no unsatisfied request.
            return null;
        }

        /// <summary>
        /// Gets the request queue for a particular client.
        /// </summary>
        /// <param name="userId">The IP address of the client.</param>
        /// <returns>A list of the requests that belong to a client or null if they do not exist.</returns>
        public List<LocalRequestHandler> GetRequests(int userId)
        {
            List<LocalRequestHandler> requestHandlers = null;
            lock (_clientRequestQueueMap)
            {
                if (_clientRequestQueueMap.Contains(userId))
                {
                    requestHandlers = _clientRequestQueueMap[userId].Value;
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
        /// TODO
        /// </summary>
        public void SerializeQueue()
        {
            string output = JsonConvert.SerializeObject(_clientRequestQueueMap, Formatting.Indented);
            _logger.Info("Saving queue, JSON:\n" + output);
            string filename = _proxyPath + QUEUE_DIRNAME + Path.DirectorySeparatorChar + GLOBAL_QUEUE_FILENAME;

            Utils.CreateDirectoryForFile(filename);
            FileStream stream = Utils.CreateFile(filename);
            if (stream != null)
            {
                StreamWriter writer = new StreamWriter(stream);
                writer.Write(output);
                writer.Close();
            }
            Thread.Sleep(500);
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
            // FIXME we don't habe global queue any more, do this with lastServedIndex and client queue
            //int requestPosition = _globalRequestQueue.FindIndex(SameRCRequestPredicate);
            int requestPosition = 0;
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
        /// <param name="userId">The user's id.</param>
        /// <returns>The number of outstanding requests for the client.</returns>
        public int NumQueuedRequests(int userId)
        {
            int count = 0;
            lock (_clientRequestQueueMap)
            {
                if (_clientRequestQueueMap.Contains(userId))
                {
                    // don't bother locking client requests since it can't be deleted while holding the previous lock
                    List<LocalRequestHandler> requestHandlers = _clientRequestQueueMap[userId].Value;
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
                if (_clientRequestQueueMap.Contains(userId))
                {
                    // don't bother locking client requests since it can't be deleted while holding the previous lock
                    List<LocalRequestHandler> requestHandlers = _clientRequestQueueMap[userId].Value;
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