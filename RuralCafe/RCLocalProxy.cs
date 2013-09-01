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
using System.Xml;

namespace RuralCafe
{
    /// <summary>
    /// Local proxy implementation, inherits from  the generic RCProxy.
    /// </summary>
    public class RCLocalProxy : RCProxy
    {
        // Constants
        private const int REQUESTS_WITHOUT_USER_CAPACITY = 50;
        private const string QUEUES_FILENAME = "Queues.json";
        private const int SPEED_ONLINE_THRESHOLD_BS = 32768; // 32 KB/s
        // .. for network speed detection
        /// <summary>
        /// Each time a new download is considered, the bytes used for calculation
        /// so far are multiplicated with this factor.
        /// </summary>
        private const double NETWORK_SPEED_REDUCTION_FACTOR = 0.9;
        private const double NETWORK_SPEED_TIME_WEIGHT = 0.7;
        // FIXME do some tests to find good default value! Customizable!?
        private const long BYTES_PER_SECOND_ONLINE_THRESHOLD = 10240; // 10 kb/s
        private static readonly TimeSpan NETWORK_DETECTION_INTERVAL = new TimeSpan(0, 5, 0);
        /// <summary>
        /// The status will only be changed if up, if we're above THRESHOLD * (1 + THRESHOLD_PERCENT_ANTI_FLAPPING)
        /// and only down if we're below THRESHOLD * (1 - THRESHOLD_PERCENT_ANTI_FLAPPING)
        /// </summary>
        private const double THRESHOLD_PERCENT_ANTI_FLAPPING = 0.1;
        // .. for clustering XXX customizable?

        // This is the interval in which certain periodic things are done:
        // * The time is being checked and if it's between 5 a.m. and 6 a.m. the clustering is run.
        // * The cache metrics are logged.
        private static readonly TimeSpan PERIODIC_INTERVAL = new TimeSpan(1, 0, 0);
        private const int CLUSTERING_K = 20;
        private const bool CLUSTERING_HIERARCHICAL = true;
        private const int CLUSTERING_CAT_NFEATURES = 2;
        private const int CLUSTERING_SUBCAT_NFEATURES = 4;
        private const int CLUSTERING_MAXCATEGORIES = 8;
        private const string CLUSTERS_FOLDER = "clusters";

        // RuralCafe pages path
        private string _uiPagesPath;
        private string _rcSearchPage;
        private string _wikiDumpPath;

        // remoteProxy
        private WebProxy _remoteProxy;

        /// <summary>
        /// Automatically detect the network status
        /// </summary>
        private bool _detectNetworkStatusAuto;
        /// <summary>
        /// The currently determined download speed in byte/s.
        /// </summary>
        private long _networkSpeedBS;
        /// <summary>
        /// The number of bytes that have been used to determine the network speed.
        /// </summary>
        private long _speedCalculationBytesUsed;
        /// <summary>
        /// The number of seconds that have been used to determine the network speed.
        /// </summary>
        private double _speedCalculationSecondsUsed;
        /// <summary>
        /// Object used to lock for network speed calculations.
        /// </summary>
        private object _speedLockObj = new object();
        /// <summary>
        /// The timer that prints the speed and, if (_detectNetworkStatusAuto)
        /// changes the network status accordingly.
        /// </summary>
        private Timer _changeNetworkStatusTimer;
        /// <summary>
        /// The timer that does the periodic stuff mentioned above.
        /// </summary>
        private Timer _periodicTimer;
        /// <summary>
        /// A variable telling whether the clustering is currently running.
        /// </summary>
        private bool clusteringRunning;

        // dictionary of lists of requests made by each client
        private Dictionary<int, List<LocalRequestHandler>> _clientRequestsMap;
        // dictionary of requests without a user. They await to be "added" via Trotro by a specific user
        private IntKeyedCollection<LocalRequestHandler> _requestsWithoutUser;
        // Random for the keys of the above Dictionary
        private Random _random;
        
        // state for maintaining the time for request/responses for measuring the ETA
        private TimeSpan _averageTimePerRequest;
        
        // the wrappers
        private WikiWrapper _wikiWrapper;
        public IndexWrapper _indexWrapper;

        // the session manager
        private SessionManager _sessionManager;

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
        /// <summary>The index wrapper.</summary>
        public IndexWrapper IndexWrapper
        {
            get { return _indexWrapper; }
            set { _indexWrapper = value; }
        }
        /// <summary>The session manager.</summary>
        public SessionManager SessionManager
        {
            get { return _sessionManager; }
        }
        /// <summary>Detect the network status automatically?
        /// If yes, the status is first set to online.</summary>
        public bool DetectNetworkStatusAuto
        {
            get { return _detectNetworkStatusAuto; }
            set 
            { 
                _detectNetworkStatusAuto = value;
                if (_detectNetworkStatusAuto)
                {
                    // Always start online if detect auto is on
                    NetworkStatus = NetworkStatusCode.Online;
                }
            }
        }

        #endregion

        /// <summary>
        /// Construtor for LocalProxy.
        /// </summary>
        /// <param name="listenAddress">Address to listen for requests on.</param>
        /// <param name="listenPort">Port to listen for requests on.</param>
        /// <param name="proxyPath">Path to the proxy's executable.</param>
        /// <param name="maxCacheSize">The max cache size in bytes.</param>
        /// <param name="indexPath">Path to the proxy's index.</param>
        /// <param name="cachePath">Path to the proxy's cache.</param>
        /// <param name="wikiDumpPath">Path to the wiki dump file.</param>
        /// <param name="packagesPath">Path to the downloaded packages.</param>
        public RCLocalProxy(IPAddress listenAddress, int listenPort, string proxyPath, string indexPath,
            long maxCacheSize, string cachePath, string wikiDumpPath, string packagesPath)
            : base(LOCAL_PROXY_NAME, listenAddress, listenPort, proxyPath,
            maxCacheSize, cachePath, packagesPath)
        {
            // The UI pages are not stored in the proxy path.
            _uiPagesPath = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar +
                 "LocalProxy" + Path.DirectorySeparatorChar +
                 "RuralCafePages" + Path.DirectorySeparatorChar;

            _wikiDumpPath = wikiDumpPath;
            _clientRequestsMap = new Dictionary<int, List<LocalRequestHandler>>();
            _requestsWithoutUser = new IntKeyedCollection<LocalRequestHandler>();
            _random = new Random();
            _averageTimePerRequest = new TimeSpan(0);

            // XXX: Should be defaulted to something then fluctuate based on connection management
            _maxInflightRequests = Properties.Settings.Default.LOCAL_MAX_INFLIGHT_REQUESTS;

            _sessionManager = new SessionManager();

            _wikiWrapper = new WikiWrapper(wikiDumpPath);

            // The index might have been initialized by the cache when creating a new DB
            if (_indexWrapper == null)
            {
                _indexWrapper = new IndexWrapper(indexPath);
                // initialize the index
                _indexWrapper.EnsureIndexExists();
            }
            // _indexWrapper.RemoveAllDeadLinks(this);

            bool success = false;
            // initialize the wiki index
            success = _wikiWrapper.InitializeWikiIndex();
            if (!success)
            {
                _logger.Warn("Error initializing the local proxy wiki index.");
            }

            // Deserialize the queue
            DeserializeQueue();
            // Tell the program to serialize the queue before shutdown
            Program.AddShutDownDelegate(SerializeQueue);

            // Start the timer that logs the network speed and if auto-change is enabled
            // also changes the network status accordingly.
            _changeNetworkStatusTimer
                           = new Timer(LogSpeedAndChangeNetworkStatusAccordingly,
                               null, NETWORK_DETECTION_INTERVAL, NETWORK_DETECTION_INTERVAL);
        }

        /// <summary>
        /// Initializes the cache by making sure that the directories exist.
        /// The cache manager is initialized with a clusters path.
        /// </summary>
        /// <param name="cachePath">Path of the cache.</param>
        /// <param name="maxCacheSize">The max cache size in bytes.</param>
        /// <returns>True or false for success or not.</returns>
        protected override bool InitializeCache(long maxCacheSize, string cachePath)
        {
            _cacheManager = new CacheManager(this, maxCacheSize, cachePath, 
                _proxyPath + CLUSTERS_FOLDER + Path.DirectorySeparatorChar);
            return _cacheManager.InitializeCache();
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
        /// Starts the periodic timer.
        /// </summary>
        public void StartPeriodicTimer()
        {
            // Every 1 hour do the stuff we have to do.
            _periodicTimer = new Timer(PeriodicTasks, null, PERIODIC_INTERVAL, PERIODIC_INTERVAL);
            // Start the clustering now and pass true. Like this, the method knows
            // it should check if the old file is older than one day. This MUST be done in an own thread,
            // as the operation is very costly and everything will block otherwise.
            (new Thread(() => PeriodicTasks(true))).Start();
        }

        /// <summary>
        /// This method is called periodically by a timer in a new thread.
        /// 
        /// Estimates the cache size each time  and logs the cache metrics once a day between 5 and 6.
        /// 
        /// Starts the clustering if it is between 5 and 6 or if the last creation is more than a day ago.
        /// Only starts if the clustering is not currently running.
        /// </summary>
        /// <param name="o">When this is null, the time must be between 5 and 6 to run the clustering.
        /// If this is not null, the old file must be older than one day.</param>
        private void PeriodicTasks(object o)
        {
            DateTime now = DateTime.Now;
            if (now.Hour == 5)
            {
                // Estimate the cache size.
                _cacheManager.EstimateCacheSize();

                // Log cache metrics
                _cacheManager.LogCacheMetrics();

                // Log number of registered users
                LogNumberOfUsers();
            }

            // if (false)
            if(o == null ? 
                (now.Hour == 5) : 
                (ProxyCacheManager.GetClusteringTimeStamp().CompareTo(now.Subtract(new TimeSpan(1, 0, 0, 0))) < 0))
            {
                bool doClustering = false;
                lock (_periodicTimer)
                {
                    if (!clusteringRunning)
                    {
                        clusteringRunning = doClustering = true;
                    }
                }
                if (doClustering)
                {
                    ProxyCacheManager.CreateClusters(CLUSTERING_K, CLUSTERING_CAT_NFEATURES, CLUSTERING_SUBCAT_NFEATURES,
                    CLUSTERING_HIERARCHICAL, CLUSTERING_MAXCATEGORIES);
                    lock (_periodicTimer)
                    {
                        clusteringRunning = false;
                    }
                }
            }
        }

        /// <summary>
        /// Logs the number of registered users.
        /// </summary>
        private void LogNumberOfUsers()
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(UIPagesPath + "users.xml");

            int num = doc.DocumentElement.ChildNodes.Count;
            Logger.Metric("Registered users: " + num);
        }
        
        #region Network status detection

        /// <summary>
        /// Includes a download into the network speed statistics.
        /// </summary>
        /// <param name="results">The speed results.</param>
        public void IncludeDownloadInCalculation(NetworkUsageDetector.NetworkUsageResults results)
        {
            Logger.Debug(String.Format("Speed: {0} for {1} bytes in {2:0.00} seconds.",
                results.SpeedBs, results.BytesDownloaded, results.ElapsedSeconds));
            lock (_speedLockObj)
            {
                // the bytes and seconds used so far are multiplicated with NETWORK_SPEED_REDUCTION_FACTOR
                // (exponential decay)
                _speedCalculationSecondsUsed = _speedCalculationSecondsUsed * NETWORK_SPEED_REDUCTION_FACTOR;
                _speedCalculationBytesUsed = (int)(_speedCalculationBytesUsed * NETWORK_SPEED_REDUCTION_FACTOR);

                // New values
                double newSpeedCalcSecondsUsed = _speedCalculationSecondsUsed + results.ElapsedSeconds;
                long newSpeedCalcBytesUsed = _speedCalculationBytesUsed + results.BytesDownloaded;

                // In percent of how much the already existing values are weighted
                double weightOfOldResults = 1;
                double weightOfNewResults;
                if(_speedCalculationBytesUsed == 0 || _speedCalculationSecondsUsed == 0)
                {
                    weightOfOldResults = 0;
                    weightOfNewResults = 1;
                } 
                else
                {
                    weightOfNewResults = NETWORK_SPEED_TIME_WEIGHT * (results.ElapsedSeconds / _speedCalculationSecondsUsed)
                        + (1 - NETWORK_SPEED_TIME_WEIGHT) * (results.BytesDownloaded / _speedCalculationBytesUsed);
                }

                // Save new speed value
                _networkSpeedBS = (long) ((_networkSpeedBS + results.SpeedBs * weightOfNewResults)
                    / (weightOfOldResults + weightOfNewResults));

                // Save new values
                _speedCalculationSecondsUsed = newSpeedCalcSecondsUsed;
                _speedCalculationBytesUsed = newSpeedCalcBytesUsed;

                Logger.Debug("Detected current overall speed: " + _networkSpeedBS + " bytes/s.");
            }
        }

        /// <summary>
        /// Logs the speed.
        /// 
        /// If _detectNetworkStatusAuto: Changes the network status
        /// if the speed is too low or too high for the current status.
        /// </summary>
        /// <param name="o">Ignored</param>
        public void LogSpeedAndChangeNetworkStatusAccordingly(object o)
        {
            Logger.Metric("Current network speed is: " + _networkSpeedBS + " bytes/s.");

            if (_detectNetworkStatusAuto)
            {
                long downThreshold = (long)(BYTES_PER_SECOND_ONLINE_THRESHOLD * (1 - THRESHOLD_PERCENT_ANTI_FLAPPING));
                long upThreshold = (long)(BYTES_PER_SECOND_ONLINE_THRESHOLD * (1 + THRESHOLD_PERCENT_ANTI_FLAPPING));

                if (NetworkStatus == NetworkStatusCode.Online
                    && _networkSpeedBS < downThreshold)
                {
                    Logger.Metric(String.Format("Speed is {0}, that is below {1}, switching to slow mode.",
                        _networkSpeedBS, downThreshold));
                    NetworkStatus = NetworkStatusCode.Slow;
                }
                else if (NetworkStatus == NetworkStatusCode.Slow
                    && _networkSpeedBS > upThreshold)
                {
                    Logger.Metric(String.Format("Speed is {0}, that is above {1}, switching to online mode.",
                        _networkSpeedBS, upThreshold));
                    NetworkStatus = NetworkStatusCode.Online;
                }
            }
        }

        #endregion
        # region Request queues interface

        /// <summary>
        /// Adds the request to the global queue and client's queue and wakes up the dispatcher.
        /// </summary>
        /// <param name="userId">The user's id.</param>
        /// <param name="requestHandler">The request handler to queue.</param>
        public void AddRequest(int userId, LocalRequestHandler requestHandler)
        {
            // Order is important!
            requestHandler = (LocalRequestHandler) AddRequestGlobalQueue(requestHandler);
            AddRequestUserQueue(userId, requestHandler);

            // Notify that a new request has been added. The Dispatcher will wake up if it was waiting.
            _requestEvent.Set();
        }

        /// <summary>
        /// Adds the request handler to the user's queue.
        /// </summary>
        /// <param name="userId">The user's id.</param>
        /// <param name="requestHandler">The request handler to queue.</param>
        private void AddRequestUserQueue(int userId, LocalRequestHandler requestHandler)
        {
            List<LocalRequestHandler> requestHandlers = null;
            // add client queue, if it does not exist yet
            lock (_clientRequestsMap)
            {
                if (_clientRequestsMap.ContainsKey(userId))
                {
                    // get the queue of client requests
                    requestHandlers = _clientRequestsMap[userId];
                }
                else
                {
                    // create the queue of client requests
                    requestHandlers = new List<LocalRequestHandler>();
                    _clientRequestsMap.Add(userId, requestHandlers);
                }
            }

            // add the request to the client's queue
            lock (requestHandlers)
            {
                // Just add
                requestHandlers.Add(requestHandler);
                requestHandler.OutstandingRequests++;
            }
        }

        /// <summary>
        /// Removes a single request from the queues.
        /// </summary>
        /// <param name="userId">The userId of the client.</param>
        /// <param name="requestHandlerItemId">The item id of the request handlers to dequeue.</param>
        /// <returns>The removed RequestHandler or null.</returns>
        public LocalRequestHandler RemoveRequest(int userId, string requestHandlerItemId)
        {
            // Order is important!
            RemoveRequestGlobalQueue(requestHandlerItemId);
            return RemoveRequestUserQueue(userId, requestHandlerItemId);
        }

        /// <summary>
        /// Removes a single request from user queue.
        /// </summary>
        /// <param name="userId">The userId of the client.</param>
        /// <param name="requestHandlerItemId">The item id of the request handlers to dequeue.</param>
        /// <returns>The removed RequestHandler or null.</returns>
        private LocalRequestHandler RemoveRequestUserQueue(int userId, string requestHandlerItemId)
        {
            // remove the request from the client's queue
            // don't need to lock the _clientRequestQueueMap for reading
            if (_clientRequestsMap.ContainsKey(userId))
            {
                List<LocalRequestHandler> requestHandlers = _clientRequestsMap[userId];
                lock (requestHandlers)
                {
                    // This gets the requestHandler with the same ID, if there is one
                    LocalRequestHandler requestHandler =
                        requestHandlers.FirstOrDefault(rh => rh.RequestId == requestHandlerItemId);
                    if (requestHandler != null)
                    {
                        requestHandler.OutstandingRequests--;
                        requestHandlers.Remove(requestHandler);
                    }
                    return requestHandler;
                }
            }
            return null;
        }

        /// <summary>
        /// Recreates the global queue from the client queues. Requests are ordered chronologically.
        /// </summary>
        private void FillGlobalQueueFromClientQueues()
        {
            lock (_globalRequests)
            {
                // Empty glocal Request queue first
                _globalRequests.Clear();
                foreach (List<LocalRequestHandler> requestHandlers in _clientRequestsMap.Values)
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
                                int index = _globalRequests.IndexOf(requestHandler);
                                // if it already exists..
                                if (index != -1)
                                {
                                    // ..replace in user queue
                                    requestHandlers[i] = (LocalRequestHandler) _globalRequests[index];
                                }
                                else
                                {
                                    // Set status to pending (if it was being downloaded while the shutdown)
                                    requestHandler.RequestStatus = RequestHandler.Status.Pending;
                                    // queue new request
                                    _globalRequests.Add(requestHandler);
                                }
                            }
                        }
                    }
                }
                // Sort the queue chronologically.
                _globalRequests.Sort((x, y) => 
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
            lock (_clientRequestsMap)
            {
                if (_clientRequestsMap.ContainsKey(userId))
                {
                    requestHandlers = _clientRequestsMap[userId];
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
                serializer.Serialize(writer, _clientRequestsMap);
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
                _clientRequestsMap = JsonConvert.
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
        public void UpdateTimePerRequest(DateTime startTime, DateTime finishTime)
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
            int requestPosition = _globalRequests.IndexOf(requestHandler);

            if (requestPosition == -1)
            {
                // The request might have been finished ms ago. Otherwise sth. is wrong.
                return 0;
            }

            // Determine the time the first request is already running
            double secondsFirstRequestRunning = _averageTimePerRequest.TotalSeconds;
            LocalRequestHandler firstHandler = (LocalRequestHandler) _globalRequests[0];
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
            lock (_clientRequestsMap)
            {
                if (_clientRequestsMap.ContainsKey(userId))
                {
                    // don't bother locking client requests since it can't be deleted while holding the previous lock
                    List<LocalRequestHandler> requestHandlers = _clientRequestsMap[userId];
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
            lock (_clientRequestsMap)
            {
                if (_clientRequestsMap.ContainsKey(userId))
                {
                    // don't bother locking client requests since it can't be deleted while holding the previous lock
                    List<LocalRequestHandler> requestHandlers = _clientRequestsMap[userId];
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