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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using log4net;
using Util;
using RuralCafe.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Net.Security;

namespace RuralCafe
{
    /// <summary>
    /// An abstract proxy class for implementing the local and remote proxies.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public abstract class RCProxy
    {
        /// <summary>
        /// Enum for the network status.
        /// </summary>
        public enum NetworkStatusCode
        {
            /// <summary>
            /// Fast connection, the proxies will act transparantly.
            /// </summary>
            Online = 0,
            /// <summary>
            /// Slow connection, requests will be queued.
            /// </summary>
            Slow = 1,
            /// <summary>
            /// No connection, only queueing and offline browsing are possible.
            /// </summary>
            Offline = 2
        }

        // Constants
        /// <summary>
        /// Default local proxy name.
        /// </summary>
        public const string LOCAL_PROXY_NAME = "Local Proxy";
        /// <summary>
        /// Default remote proxy name
        /// </summary>
        public const string REMOTE_PROXY_NAME = "Remote Proxy";
        /// <summary>
        /// Constant for unlimited bandwidth.
        /// </summary>
        public const int UNLIMITED_BANDWIDTH = -1;
        /// <summary>
        /// Maximum bandwidth. At first unlimited.
        /// </summary>
        public int MAXIMUM_DOWNLINK_BANDWIDTH = UNLIMITED_BANDWIDTH;
        /// <summary>
        /// The name of the directory where the current state is saved.
        /// </summary>
        public const string STATE_DIRNAME = "State";
        /// <summary>
        /// The name of the directory where the current state is saved.
        /// </summary>
        public const string STATE_FILENAME = "State.json";
        // .. for network speed (detection)
        /// <summary>
        /// Each time a new download is considered, the bytes used for calculation
        /// so far are multiplicated with this factor.
        /// </summary>
        private const double NETWORK_SPEED_REDUCTION_FACTOR = 0.8;
        // The time will actually always be ~10s, so this is just to say how much the should be weighted different
        // when the bytes downloaded are dieffernt
        private const double NETWORK_SPEED_TIME_WEIGHT = 0.5;
        private static readonly TimeSpan NETWORK_DETECTION_INTERVAL = new TimeSpan(0, 5, 0);
        /// <summary>The maximum number of threads being used.</summary>
        public const int MAX_THREADS = 1000; 

        /// <summary>
        /// notifies that a new request has arrived or a request has completed
        /// </summary>
        protected AutoResetEvent _requestEvent;

        /// <summary>
        /// The gatewayProxy
        /// </summary>
        protected WebProxy _gatewayProxy;

        // proxy settings
        /// <summary>The IP address of the proxy.</summary>
        protected IPAddress _listenAddress;
        /// <summary>The port the proxy listens to.</summary>
        protected int _listenPort;
        /// <summary>The port the proxy listens to for HTTPS connections.</summary>
        protected int _httpsListenPort;
        /// <summary>The proxy's logger.</summary>
        protected readonly ILog _logger;
        /// <summary>The proxy's file path.</summary>
        protected string _proxyPath;
        /// <summary>The proxy's cache file path.</summary>
        protected string _packagesCachePath;
        /// <summary>The name of the proxy.</summary>
        protected string _name;
        /// <summary>The cache manager.</summary>
        protected CacheManager _cacheManager;

        // network speed stuff
        /// <summary>
        /// The network status
        /// </summary>
        protected NetworkStatusCode _networkStatus;
        /// <summary>
        /// The network usage detector.
        /// </summary>
        protected NetworkUsageDetector _networkUsageDetector;
        /// <summary>
        /// The currently determined download speed in byte/s.
        /// </summary>
        protected long _networkSpeedBS;
        /// <summary>
        /// The number of bytes that have been used to determine the network speed.
        /// </summary>
        protected long _speedCalculationBytesUsed;
        /// <summary>
        /// The number of seconds that have been used to determine the network speed.
        /// </summary>
        protected double _speedCalculationSecondsUsed;
        /// <summary>
        /// Object used to lock for network speed calculations.
        /// </summary>
        protected object _speedLockObj = new object();
        /// <summary>
        /// The timer that prints the speed and, does the periodic stuff depeding on 
        /// the current speed.
        /// </summary>
        protected Timer _changeNetworkStatusTimer;
        
        // bandwidth measurement
        // lock object
        private static Object _downlinkBWLockObject = new Object();
        private static DateTime _bwStartTime = DateTime.Now;
        private static int _bwDataSent = 0;

        /// <summary>
        /// maximum inflight requests
        /// </summary>
        [JsonProperty]
        protected int _maxInflightRequests;
        /// <summary>
        /// An event to wake up threads waiting for admission control.
        /// </summary>
        private ManualResetEvent _admissionEvent = new ManualResetEvent(false);
        /// <summary>
        /// All the waiting threads queue themselves up in here. The key is the Request ID.
        /// </summary>
        private List<string> _admissionQueue = new List<string>();

        /// <summary>
        /// Blacklist.
        /// </summary>
        protected List<string> _blacklistedDomains = new List<string>();

        /// <summary>
        /// The id of the next request.
        /// </summary>
        [JsonProperty]
        protected long _nextHandlerId = 1;

        /// <summary>
        /// A big queue for lining up requests
        /// </summary>
        protected List<RequestHandler> _globalRequests = new List<RequestHandler>();

        /// <summary>
        /// The number of active requests.
        /// </summary>
        protected int _activeRequests = 0;

        # region Property Accessors

        /// <summary>Path to the proxy folder.</summary>
        public string ProxyPath
        {
            get { return _proxyPath; }
        }
        /// <summary>Path to the proxy's cache.</summary>
        public string CachePath
        {
            get { return _cacheManager.CachePath; }
        }
        /// <summary>The cache manager.</summary>
        public CacheManager ProxyCacheManager
        {
            get { return _cacheManager; }
        }
        /// <summary>Path to the proxy's packages.</summary>
        public string PackagesPath
        {
            get { return _packagesCachePath; }
        }
        /// <summary>The gateway proxy used to connect to the Internet.</summary>
        public WebProxy GatewayProxy
        {
            get { return _gatewayProxy; }
        }
        /// <summary>The network status.</summary>
        public NetworkStatusCode NetworkStatus
        {
            get { return _networkStatus; }
            set { _networkStatus = value; }
        }
        /// <summary>The network status.</summary>
        public NetworkUsageDetector NetworkUsageDetector
        {
            get { return _networkUsageDetector; }
            set { _networkUsageDetector = value; }
        }
        /// <summary>The maximum number of inflight requests.</summary>
        public int MaxInflightRequests
        {
            get { return _maxInflightRequests; }
        }
        /// <summary>The current number of inflight requests.</summary>
        public int NumInflightRequests
        {
            get { return _activeRequests; }
        }

        /// <summary>The logger.</summary>
        public ILog Logger 
        {
            get { return _logger; }
        }
        # endregion

        /// <summary>
        /// Constructor for proxy base class.
        /// </summary>
        /// <param name="name">Name of the proxy.</param>
        /// <param name="listenAddress">Address the proxy listens on.</param>
        /// <param name="listenPort">Port the proxy listens on.</param>
        /// <param name="httpsListenPort">Port the proxy listens on for HTTPS</param>
        /// <param name="proxyPath">Directory path the proxy is running in.</param>
        /// <param name="maxCacheSize">The max cache size in bytes.</param>
        /// <param name="cachePath">Path to the proxy's cache</param>
        /// <param name="packageCachePath">Path to the proxy's packages</param>
        protected RCProxy(string name, IPAddress listenAddress, int listenPort, int httpsListenPort,
            string proxyPath, long maxCacheSize, string cachePath, string packageCachePath)
        {
            _name = name;
            // setup proxy listener variables
            _listenAddress = listenAddress;
            _listenPort = listenPort;
            _httpsListenPort = httpsListenPort;
            _proxyPath = proxyPath;

            // no pending requests
            _requestEvent = new AutoResetEvent(false);

            //create and initialize the logger
            _logger = LogManager.GetLogger(this.GetType());

            bool success = false;

            // initialize the cache directory
            success = InitializeCache(maxCacheSize, cachePath);
            if (!success)
            {
                _logger.Warn("Error initializing the " + name + " cache.");
            }

            // initialize the packages cache
            success = InitializePackagesCache(proxyPath + packageCachePath);
            if (!success)
            {
                _logger.Warn("Error initializing the " + name + " packages cache.");
            }

            // Load the blacklist
            LoadBlacklist();

            // initialize the network usage detector
            _networkUsageDetector = new NetworkUsageDetector(this);
            // Start the timer that logs the network speed and so on.
            _changeNetworkStatusTimer
                           = new Timer(LogSpeedAndApplyNetworkSpeedSettings,
                               null, NETWORK_DETECTION_INTERVAL, NETWORK_DETECTION_INTERVAL);

            // Restore old state
            LoadState();
            // Tell the programm to serialize state before shutdown
            Program.AddShutDownDelegate(SaveState);
        }

        /// <summary>
        /// Initializes the cache by making sure that the directory exists.
        /// </summary>
        /// <param name="cachePath">Path of the cache.</param>
        /// <param name="maxCacheSize">The max cache size in bytes.</param>
        /// <returns>True or false for success or not.</returns>
        protected virtual bool InitializeCache(long maxCacheSize, string cachePath)
        {
            _cacheManager = new CacheManager(this, maxCacheSize, cachePath);
            return _cacheManager.InitializeCache();
        }

        /// <summary>
        /// Initializes the packages by making sure that the directory exists.
        /// </summary>
        /// <param name="packagesCachePath">Path of the packages cache.</param>
        /// <returns>True or false for success or failure.</returns>
        protected bool InitializePackagesCache(string packagesCachePath)
        {
            _packagesCachePath = packagesCachePath;

            try
            {
                if (!Directory.Exists(_packagesCachePath))
                {
                    System.IO.Directory.CreateDirectory(_packagesCachePath);
                }
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Sets the gateway proxy for the remote proxy.
        /// </summary>
        /// <param name="proxyAddress">The IP address of the gateway.</param>
        /// <param name="proxyPort">The port of the gateway.</param>
        /// <param name="login">The login for the gateway.</param>
        /// <param name="password">The password for the gateway.</param>
        public void SetGatewayProxy(IPAddress proxyAddress, int proxyPort, string login, string password)
        {
            if (proxyAddress == null)
            {
                _gatewayProxy = null;
            }
            else
            {
                _gatewayProxy = new WebProxy(proxyAddress.ToString(), proxyPort);
                _gatewayProxy.Credentials = new NetworkCredential(login, password);
            }
        }

        /// <summary>
        /// Checks to see if the proxy still has free downlink bandwidth.
        /// Used to rate limit the downlink transfer speed.
        /// </summary>
        /// <param name="bytesToSend">Number of bytes the proxy wants to send.</param>
        /// <returns>True or false if the proxy has free bandwidth or not.</returns>
        public bool HasDownlinkBandwidth(int bytesToSend)
        {
            lock (_downlinkBWLockObject)
            {
                if (MAXIMUM_DOWNLINK_BANDWIDTH == UNLIMITED_BANDWIDTH)
                {
                    return true;
                }
                TimeSpan elapsed = DateTime.Now - _bwStartTime;
                if (elapsed.TotalMilliseconds > 1000)
                {
                    // reset
                    _bwStartTime = DateTime.Now;
                    _bwDataSent = bytesToSend;
                    return true;
                }
                else
                {
                    int bps = _bwDataSent / (Convert.ToInt32(elapsed.TotalSeconds) + 1);
                    if (bps > MAXIMUM_DOWNLINK_BANDWIDTH)
                    {
                        return false;
                    }
                    else
                    {
                        _bwDataSent += bytesToSend;
                        return true;
                    }
                }
            }
        }

        /// <summary>
        /// Loads the blacklist or the default blacklist, if there is none yet.
        /// </summary>
        private void LoadBlacklist()
        {
            // Copy the default file to the proxy folder, if there is no blacklist
            string blacklistFileName = _proxyPath + "blacklist.txt";
            if (!File.Exists(blacklistFileName))
            {
                try
                {
                    File.Copy("blacklist.txt", blacklistFileName);
                }
                catch (Exception)
                {
                    // do nothing
                }
            }
            LoadBlacklist(blacklistFileName);
        }

        /// <summary>
        /// Loads the blacklist from a file.
        /// </summary>
        /// <param name="fileName">The name of the blacklist file.</param>
        private void LoadBlacklist(string fileName)
        {
            try
            {
                string s = System.IO.File.ReadAllText(fileName);

                string[] domains = s.Split('\n');

                foreach (string domain in domains)
                {
                    if (domain.Trim().Length > 0)
                    {
                        _blacklistedDomains.Add(domain.Trim());
                    }
                }
            }
            catch (Exception)
            {
                // do nothing
            }
        }

        /// <summary>
        /// Checks whether a URI is blacklisted.
        /// </summary>
        /// <param name="requestUri">URI to check.</param>
        /// <returns>True or false for blacklisted or not.</returns>
        public bool IsBlacklisted(string requestUri)
        {
            // ignore all toolbar queries
            if (requestUri.Contains("toolbarqueries"))
            {
                return true;
            }

            // trim the "http://" and "www."
            requestUri = HttpUtils.RemoveWWWPrefix(HttpUtils.RemoveHttpPrefix(requestUri));

            // check against all domains in the blacklist
            foreach (string domainH in _blacklistedDomains)
            {
                // trim the "http://" and "www."
                string domain = HttpUtils.RemoveWWWPrefix(HttpUtils.RemoveHttpPrefix(domainH));
                if (requestUri.StartsWith(domain))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Increments the value of next request ID by one and returns the old value.
        /// </summary>
        /// <returns>The old value.</returns>
        public long GetAndIncrementNextHandlerID()
        {
            return System.Threading.Interlocked.Increment(ref _nextHandlerId) - 1;
        }

        /// <summary>
        /// Returns the first global request in the queue or null if no request exists.
        /// </summary>
        /// <returns>The first unsatisfied request by the next user or null if no request exists.</returns>
        public RequestHandler GetFirstGlobalRequest()
        {
            RequestHandler requestHandler = null;

            // lock to make sure nothing is added or removed
            lock (_globalRequests)
            {
                if (_globalRequests.Count > 0)
                {
                    requestHandler = _globalRequests[0];
                }
            }
            return requestHandler;
        }

        /// <summary>
        /// Starts the listener for HTTPS connections from clients.
        /// </summary>
        public void StartHttpsListener()
        {
            _logger.Info("Started HTTPS Listener on " + _listenAddress + ":" + _httpsListenPort);
            try
            {
                // create a listener for the proxy port
                TcpListener listener = new TcpListener(_listenAddress, _httpsListenPort);
                
                listener.Start();

                // loop and listen for the next connection request
                while (true)
                {
                    // accept connections on the proxy port (blocks)
                    TcpClient client = listener.AcceptTcpClient();

                    // Start own method HandleTCPRequest in the thread
                    ThreadPool.QueueUserWorkItem(new WaitCallback(HandleTCPRequest), client);
                }
            }
            catch (SocketException e)
            {
                _logger.Fatal("SocketException in StartHttpsListener, errorcode: " + e.NativeErrorCode, e);
            }
            catch (Exception e)
            {
                _logger.Fatal("Exception in StartHttpsListener", e);
            }
        }

        /// <summary>
        /// Starts the listener for connections from clients.
        /// </summary>
        public void StartListener()
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
                    // accept connections on the proxy port (blocks)
                    HttpListenerContext context = listener.GetContext();

                    // create the request handler
                    RequestHandler requestHandler = RequestHandler.PrepareNewRequestHandler(this, context);

                    // Start own method HandleRequest in the thread, which also in- and decreases _activeRequests
                    ThreadPool.QueueUserWorkItem(new WaitCallback(requestHandler.HandleRequest), null);
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

        /// <summary>
        /// Starts the dispatcher which requests pages from the remote proxy.
        /// Currently makes up to 20 requests at a time.
        /// </summary>
        public void StartDispatcher()
        {
            ThreadPool.SetMaxThreads(MAX_THREADS, MAX_THREADS);

            _logger.Info("Started Dispatcher");

            // go through the outstanding requests forever
            while (true)
            {
                // check whether we have another request and are allowed to handle another request
                RequestHandler requestHandler = GetFirstGlobalRequest();
                if (requestHandler != null)
                {
                    if (_name == REMOTE_PROXY_NAME || (_name == LOCAL_PROXY_NAME && NetworkStatus == NetworkStatusCode.Slow))
                    {
                        // RCRequest should never be null
                        if (requestHandler.RCRequest == null)
                        {
                            _logger.Error("RCRequest is null!");
                            continue;
                        }

                        // Going to process this, remove from incoming request queue
                        RemoveRequestGlobalQueue(requestHandler.RequestId);

                        // skip requests in global queue that are not pending, probably requeued from log
                        if (requestHandler.RequestStatus == RequestHandler.Status.Pending)
                        {
                            // Assign a dispatcher to the request
                            ThreadPool.QueueUserWorkItem(new WaitCallback(requestHandler.DispatchRequest), null);
                        }
                    }
                }
                else
                {
                    // wait for an add event
                    _requestEvent.WaitOne();
                }
            }
        }

        /// <summary>
        /// Add active request.
        /// </summary>
        public void AddActiveRequest()
        {
            lock (_admissionEvent)
            {
                _activeRequests++;
            }
        }

        /// <summary>
        /// Remove active request.
        /// </summary>
        public void RemoveActiveRequest()
        {
            lock (_admissionEvent)
            {
                _activeRequests--;
                // Wake up waiting threads
                _admissionEvent.Set();
            }
        }

        /// <summary>
        /// Checks if the thread is admitted. Blocks with a ManualResetEvent.
        /// 
        /// After it is admitted, the number of active requests is incremented.
        /// </summary>
        /// <param name="id">The request id.</param>
        public void WaitForAdmissionControlAndAddActiveRequest(string id)
        {
            // Queue yourself
            lock (_admissionEvent)
            {
                _admissionQueue.Add(id);
            }

            while (true)
            {
                lock (_admissionEvent)
                {
                    int diff = MaxInflightRequests - NumInflightRequests;
                    // We can go, if there is space and we're in the first diff positions of the queue
                    if (diff > 0 && _admissionQueue.Take(diff).Contains(id))
                    {
                        // Dequeue yourself
                        _admissionQueue.Remove(id);

                        _activeRequests++;
                        break;
                    }
                    else
                    {
                        // We'll have to wait (again)
                        _admissionEvent.Reset();
                    }
                }
                _admissionEvent.WaitOne();
            }
            
        }

        /// <summary>
        /// Adds the request handler to the global queue.
        /// </summary>
        /// <param name="requestHandler">The request handler to queue.</param>
        /// <returns>The request handler in the queue.
        /// Either the parameter or an already exiting equivalent RH in the queue.</returns>
        protected RequestHandler AddRequestGlobalQueue(RequestHandler requestHandler)
        {
            // add the request to the global queue
            lock (_globalRequests)
            {
                // queue new request
                _globalRequests.Add(requestHandler);

                return requestHandler;
            }
        }

        /// <summary>
        /// Removes a single request from global queue.
        /// </summary>
        /// <param name="requestId">The item id of the request handlers to dequeue.</param>
        protected RequestHandler RemoveRequestGlobalQueue(string requestId)
        {
            // remove the request from the global queue
            lock (_globalRequests)
            {
                // This gets the requestHandler with the same ID, if there is one
                RequestHandler requestHandler = _globalRequests.FirstOrDefault(rh => rh.RequestId == requestId);
                if (requestHandler != null)
                {
                    // if this URI is requested only once (or 0, which will be the case at the remote proxy), remove it
                    if (requestHandler.OutstandingRequests <= 1)
                    {
                        _globalRequests.Remove(requestHandler);
                        return requestHandler;
                    }
                }
            }
            return null;
        }

        #region HTTPS TCP Proxy

        /// <summary>
        /// Handles a TCP request.
        /// </summary>
        /// <param name="clientObject">The tcp client from the accepted connection.</param>
        protected abstract void HandleTCPRequest(object clientObject);

        /// <summary>
        /// Tunnels a TCP connection.
        /// </summary>
        /// <param name="inClient">The client to read from.</param>
        /// <param name="outClient">The client to write to.</param>
        public void TunnelTCP(TcpClient inClient, TcpClient outClient)
        {
            NetworkStream inStream = inClient.GetStream();
            NetworkStream outStream = outClient.GetStream();
            byte[] buffer = new byte[1024];
            int read;
            try
            {
                while (inClient.Connected && outClient.Connected)
                {
                    if (inStream.DataAvailable && (read = inStream.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        outStream.Write(buffer, 0, read);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Debug("TCP connection error: ", e);
            }
            finally
            {
                Logger.Debug("Closing TCP connection.");
                // Disconnent if connections still alive
                try
                {
                    if (inClient.Connected)
                    {
                        inClient.Close();
                    }
                    if (outClient.Connected)
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

        #endregion
        #region Network speed calculation detection

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
                if (_speedCalculationBytesUsed == 0 || _speedCalculationSecondsUsed == 0)
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
                _networkSpeedBS = (long)((_networkSpeedBS + results.SpeedBs * weightOfNewResults)
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
        /// Changes _maxInflightRequests according to the network speed.
        /// </summary>
        /// <param name="o">Ignored</param>
        public virtual void LogSpeedAndApplyNetworkSpeedSettings(object o)
        {
            Logger.Metric("Current network speed is: " + _networkSpeedBS + " bytes/s.");

            if (_networkSpeedBS > 0 && Properties.Network.Default.FLUCTUATE_MAX_REQUESTS)
            {
                // Change _maxInflightRequests accordingly.
                _maxInflightRequests = Math.Max((int)(_networkSpeedBS / Properties.Network.Default.MIN_BYTES_PER_SECOND_PER_REQUEST), 
                    Properties.Network.Default.MIN_MAX_INFLIGHT_REQUESTS);
                Logger.Info("Changing max inflight requests to: " + _maxInflightRequests);
            }
        }

        #endregion
        #region State (de)serialization

        /// <summary>
        /// Serializes the state. Called, when the proxy is being closed.
        /// </summary>
        public void SaveState()
        {
            string filename = _proxyPath + STATE_DIRNAME + Path.DirectorySeparatorChar + STATE_FILENAME;
            Utils.CreateDirectoryForFile(filename);

            JsonSerializer serializer = new JsonSerializer();
            using (StreamWriter sw = new StreamWriter(filename))
            using (JsonWriter writer = new JsonTextWriter(sw))
            {
                // All fields marked with [JsonProperty] will be serialized
                serializer.Serialize(writer, this);
            }
            _logger.Info("Serialized state.");
        }

        /// <summary>
        /// Deserializes the state. Called when the program starts.
        /// </summary>
        public void LoadState()
        {
            string filename = _proxyPath + STATE_DIRNAME + Path.DirectorySeparatorChar + STATE_FILENAME;

            string fileContent = Utils.ReadFileAsString(filename);
            if (fileContent == "")
            {
                _logger.Info("No state to deserialize.");
                // The file does not exist, we do not have to do anything.
                return;
            }
            // Deserialize
            try
            {
                JToken root = JObject.Parse(fileContent);
                foreach(JToken field in root.Children())
                {
                    string varName = (field as JProperty).Name;
                    
                    // Find accoridng field.
                    FieldInfo fi = this.GetType().GetField(varName, BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fi != null)
                    {
                        // and set value
                        object value = field.First.ToObject(fi.FieldType, new JsonSerializer());
                        fi.SetValue(this, value);
                    }
                }
                _logger.Info("Restored state.");
            }
            catch (Exception e)
            {
                _logger.Error("State deserialization failed: ", e);
            }

        }
        #endregion
    }
}
