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
using RuralCafe.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Reflection;

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

        // notifies that a new request has arrived
        protected AutoResetEvent _newRequestEvent;

        /// <summary>
        /// The gatewayProxy
        /// </summary>
        protected WebProxy _gatewayProxy;

        // proxy settings
        protected IPAddress _listenAddress;
        protected int _listenPort;
        protected readonly ILog _logger;
        protected string _proxyPath;
        protected string _packagesCachePath;
        protected string _name;
        protected CacheManager _cacheManager;

        /// <summary>
        /// The network status
        /// </summary>
        private NetworkStatusCode _networkStatus;
        protected int _activeRequests;
        
        // bandwidth measurement
        // lock object
        private static Object _downlinkBWLockObject = new Object();
        private static DateTime _bwStartTime = DateTime.Now;
        private static int _bwDataSent = 0;

        // maximum inflight requests
        protected int _maxInflightRequests;

        /// <summary>
        /// Blacklist.
        /// </summary>
        protected List<string> _blacklistedDomains = new List<string>();

        /// <summary>
        /// The id of the next request.
        /// </summary>
        [JsonProperty]
        protected long _nextRequestId = 1;

        /// <summary>
        /// A big queue for lining up requests
        /// </summary>
        protected List<RequestHandler> _globalRequestQueue = new List<RequestHandler>();

        # region Property Accessors

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
        /// <summary>The maximum number of inflight requests.</summary>
        public int MaxInflightRequests
        {
            get { return _maxInflightRequests; }
            set { _maxInflightRequests = value; }
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
        /// <param name="proxyPath">Directory path the proxy is running in.</param>
        /// <param name="cachePath">Path to the proxy's cache</param>
        /// <param name="packageCachePath">Path to the proxy's packages</param>
        protected RCProxy(string name, IPAddress listenAddress, int listenPort, 
            string proxyPath, string cachePath, string packageCachePath)
        {
            _name = name;
            // setup proxy listener variables
            _listenAddress = listenAddress;
            _listenPort = listenPort;
            _proxyPath = proxyPath;

            // no pending requests
            _newRequestEvent = new AutoResetEvent(false);

            //create and initialize the logger
            _logger = LogManager.GetLogger(this.GetType());

            bool success = false;

            // initialize the cache directory
            success = InitializeCache(cachePath);
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

            // Restore old state
            LoadState();
            // Tell the programm to serialize state before shutdown
            Program.AddShutDownDelegate(SaveState);
        }

        /// <summary>
        /// Initializes the cache by making sure that the directory exists.
        /// </summary>
        /// <param name="cachePath">Path of the cache.</param>
        /// <returns>True or false for success or not.</returns>
        protected virtual bool InitializeCache(string cachePath)
        {
            _cacheManager = new CacheManager(cachePath, this);
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
        /// Loads the blacklist from a file.
        /// </summary>
        /// <param name="fileName">The name of the blacklist file.</param>
        public void LoadBlacklist(string fileName)
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

            // trim the "http://"
            requestUri = HttpUtils.RemoveHttpPrefix(requestUri);

            // check against all domains in the blacklist
            foreach (string domain in _blacklistedDomains)
            {
                if (requestUri.StartsWith(domain) || HttpUtils.AddOrRemoveWWW(requestUri).StartsWith(domain))
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
        public long GetAndIncrementNextRequestID()
        {
            return System.Threading.Interlocked.Increment(ref _nextRequestId) - 1;
        }

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

                    // create the request handler
                    RequestHandler requestHandler = RequestHandler.PrepareNewRequestHandler(this, context);

                    // Start own method StartRequestHandler in the thread, which also in- and decreases _activeRequests
                    ThreadPool.QueueUserWorkItem(new WaitCallback(StartRequestHandler), requestHandler);
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
        /// Invokes the <see cref="RequestHandler.HandleRequest"/> method. While it is running, the number of
        /// active requests is increased.
        /// </summary>
        /// <param name="requestHandler">The request handler of type
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
            ((RequestHandler)requestHandler).HandleRequest();
            // Decrement number of active requests
            System.Threading.Interlocked.Decrement(ref _activeRequests);
        }


        /// <summary>
        /// Starts the dispatcher which requests pages from the remote proxy.
        /// Currently makes up to 20 requests at a time.
        /// </summary>
        public void StartDispatcher()
        {
            int workerThreads;
            int portThreads;

            // XXX: probably don't want to hard-code this as the number of port threads should be proportional to line speed.
            ThreadPool.SetMaxThreads(MaxInflightRequests, MaxInflightRequests);
            ThreadPool.GetMaxThreads(out workerThreads, out portThreads);
            Console.WriteLine("\nMaximum worker threads: \t{0}" +
                "\nMaximum completion port threads: {1}",
                workerThreads, portThreads);
            ThreadPool.GetAvailableThreads(out workerThreads, out portThreads);
            Console.WriteLine("\nAvailable worker threads: \t{0}" +
                "\nAvailable completion port threads: {1}\n",
                workerThreads, portThreads);

            _logger.Info("Started Dispatcher");

            // go through the outstanding requests forever
            while (true)
            {
                RequestHandler requestHandler = GetFirstGlobalRequest();
                if (_name == REMOTE_PROXY_NAME && requestHandler != null ||
                    (NetworkStatus == NetworkStatusCode.Slow && requestHandler != null))
                {
                    if (requestHandler.RCRequest != null && requestHandler.RequestStatus != RequestHandler.Status.Pending)
                    {
                        // skip requests in global queue that are not pending, probably requeued from log
                        lock (_globalRequestQueue)
                        {
                            _globalRequestQueue.RemoveAt(0);
                        }
                        continue;
                    }
                    // Queue the request as a thread
                    ThreadPool.QueueUserWorkItem(new WaitCallback(requestHandler.DispatchRequest), null);

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
        }

        /// <summary>
        /// Adds the request handler to the global queue.
        /// </summary>
        /// <param name="requestHandler">The request handler to queue.</param>
        /// <returns>The request handler in the queue.
        /// Either the parameter or an already exiting equivalent RH in the queue.</returns>
        protected RequestHandler QueueRequestGlobalQueue(RequestHandler requestHandler)
        {
            // add the request to the global queue
            lock (_globalRequestQueue)
            {
                // queue new request
                _globalRequestQueue.Add(requestHandler);

                return requestHandler;
            }
        }

        /// <summary>
        /// Removes a single request from global queue.
        /// </summary>
        /// <param name="requestHandlerItemId">The item id of the request handlers to dequeue.</param>
        protected RequestHandler DequeueRequestGlobalQueue(string requestHandlerItemId)
        {
            // remove the request from the global queue
            lock (_globalRequestQueue)
            {
                // This gets the requestHandler with the same ID, if there is one
                RequestHandler requestHandler =
                    _globalRequestQueue.FirstOrDefault(rh => rh.ItemId == requestHandlerItemId);
                if (requestHandler != null)
                {
                    // check to see if this URI is requested more than once
                    // if not, remove it
                    if (requestHandler.OutstandingRequests == 1)
                    {
                        _globalRequestQueue.Remove(requestHandler);
                        return requestHandler;
                    }
                }
            }
            return null;
        }

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
                // All fiels marked with [JsonProperty] will be serialized
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
