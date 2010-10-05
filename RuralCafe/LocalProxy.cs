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
    public class LocalProxy : GenericProxy
    {
        // RuralCafe pages path
        public string _ruralCafePagesPath;
        public string _ruralCafeSearchPage;
        public string _indexPath;
        public string _wikiDumpPath;
        //public int _activeRequests;
        //public int MAXIMUM_ACTIVE_REQUESTS = 50;

        // big queue for lining up requests to remote proxy
        public List<LocalRequest> _globalRequestQueue;

        // dictionary of linked lists of requests made by each client
        public Dictionary<IPAddress, List<LocalRequest>> _clientRequestQueueMap;

        // dictionary of last requested page by each client
        public Dictionary<IPAddress, LocalRequest> _clientLastRequestMap;

        // dictionary of queries and results
        //public Dictionary<string, LinkedList<Result>> _searchResults;

        // notifies that a new request has arrived
        public AutoResetEvent _newRequestEvent;

        // seconds per request completed
        private DateTime _dispatchStartTime;
        private DateTime _dispatchEndTime;
        private TimeSpan _averageTimePerRequest;
        
        // Wiki index
        public static Dictionary<string, Indexer> indexes = new Dictionary<string, Indexer>();

        public LocalProxy(IPAddress listenAddress, int listenPort, string proxyPath, string indexPath,
            string cachePath, string wikiDumpPath, string packagePath, string logPath)
            : base(LOCAL_PROXY_NAME, listenAddress, listenPort, proxyPath, 
            cachePath, packagePath, logPath)
        {
            //_activeRequests = 0;
            _ruralCafePagesPath = proxyPath + @"RuralCafePages\";
            _indexPath = indexPath;// @"c:\cygwin\home\jchen\index-mathematics\"; //@"Lucene\";
            _wikiDumpPath = wikiDumpPath; // "d:\\wikipedia\\enwiki-20090520-pages-articles.xml.bz2";
            _globalRequestQueue = new List<LocalRequest>();
            _clientRequestQueueMap = new Dictionary<IPAddress, List<LocalRequest>>();
            _clientLastRequestMap = new Dictionary<IPAddress, LocalRequest>();
            //_searchResults = new Dictionary<string, LinkedList<Result>>();
            _newRequestEvent = new AutoResetEvent(false);
            _averageTimePerRequest = new TimeSpan(0);

            // initialize the cache
            InitializeCacheIndex(_indexPath);

            // initialize the cache
            InitializeCache(cachePath);

            LoadWikiIndexer(_wikiDumpPath);
        }
        // called once to make sure the package cache directory is intact
        private void InitializeCacheIndex(string luceneIndexPath)
        {
            bool created;
            created = CacheIndexer.EnsureIndexExists(luceneIndexPath);
            if (!created)
            {
                // XXX: got probs
            }

            // to add lucene cache index to the list of indexes
            //Indexer ixr = new Indexer(luceneIndexPath + "");
            //indexes.Add(luceneIndexPath.ToLowerInvariant(), ixr);
        }
        
        private void LoadWikiIndexer(string file)
        {
            if (!File.Exists(file))
            {
                return;
            }

            Indexer ixr = new Indexer(file);

            if (!ixr.IndexExists)
            {
                return;
            }

            indexes.Add(file.ToLowerInvariant(), ixr);
        }

        // XXX: later we may want to be able to use different interfaces on the fly
        // sets the search page interface to use
        public void SetRuralCafeSearchPage(string searchPage)
        {
            _ruralCafeSearchPage = "http://www.ruralcafe.net/" + searchPage;
        }

        #region Property accessors for LocalRequest objects

        public string RuralCafePagesPath
        {
            get { return _ruralCafePagesPath; }
        }
        public string RuralCafeSearchPage
        {
            get { return _ruralCafeSearchPage; }
        }

        #endregion

        // listens for connections from clients
        public void StartLocalListener()
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
                    /*
                    while (_activeRequests >= MAXIMUM_ACTIVE_REQUESTS)
                    {
                        Thread.Sleep(100);
                    }*/
                    // Accept connections on the proxy port.
                    Socket socket = sockServer.AcceptSocket();
                    //_activeRequests++;

                    // When AcceptSocket returns, it means there is a connection. Create
                    // an instance of the proxy handler class and start a thread running.
                    LocalRequest requestHandler = new LocalRequest(this, socket);
                    Thread proxyThread = new Thread(new ThreadStart(requestHandler.Go));
                    //proxyThread.Name = String.Format("LocalRequest" + socket.RemoteEndPoint.ToString());

                    proxyThread.Start();
                    // While the thread is running, the main program thread will loop around
                    // and listen for the next connection request.
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

        // Requests pages from the remote proxy
        // makes the requests one at a time via a single tcp socket
        public void StartDispatcher()
        {
            WriteDebug("Started Requester");
            while (true)
            {
                // goes through the outstanding requests
                if (_globalRequestQueue.Count > 0)
                {
                    DispatchStartTimestamp();

                    LocalRequest request = GetFirstRequestFromGlobalQueue();
                    RemoveFirstRequestFromGlobalQueue();
                    request.Status = GenericRequest.STATUS_REQUESTED;

                    WriteDebug("Dispatching to remote proxy: " + request.RequestUri);
                    int streamStatus = request.StreamRequestFromProxyToCache();
                    request._endTime = DateTime.Now;
                    if (streamStatus == GenericRequest.STATUS_SATISFIED)
                    {
                        WriteDebug("unpacking: " + request.RequestUri);
                        long unpackedBytes = Package.UnpackPackage(request);
                        if (unpackedBytes > 0)
                        {
                            WriteDebug("unpacked: " + request.RequestUri);
                            request.Status = GenericRequest.STATUS_SATISFIED;
                            request._requestObject._fileSize = unpackedBytes;
                        }
                        else
                        {
                            WriteDebug("failed to unpack: " + request.RequestUri);
                            request.Status = GenericRequest.STATUS_FAILED;
                        }

                        DispatchEndTimestamp();
                        UpdateTimePerRequest();
                        // XXX: benchmarking only
                        //SaveBenchmarkTimes(totalProcessingTime);
                    }
                    else
                    {
                        request.Status = GenericRequest.STATUS_FAILED;
                    }
                    request.LogResponse();
                }
                else
                {
                    // wait for an add event
                    _newRequestEvent.WaitOne();
                }
            }
        }

        // adds the request to the global queue and client's queue
        public void AddRequestToQueues(IPAddress clientAddress, LocalRequest request)
        {
            List<LocalRequest> clientRequests;
            // add the request to the client's queue
            lock (_clientRequestQueueMap)
            {
                if (_clientRequestQueueMap.ContainsKey(clientAddress))
                {
                    clientRequests = _clientRequestQueueMap[clientAddress];
                }
                else
                {
                    clientRequests = new List<LocalRequest>();
                    _clientRequestQueueMap.Add(clientAddress, clientRequests);
                }
            }

            lock (clientRequests)
            {
                if (!clientRequests.Contains(request))
                {
                    clientRequests.Add(request);
                }
            }

            // check to see if the request is already in the global queue
            lock (_globalRequestQueue)
            {
                if (_globalRequestQueue.Contains(request))
                {
                    int existingRequestIndex = _globalRequestQueue.IndexOf(request);
                    LocalRequest existingRequest = _globalRequestQueue[existingRequestIndex];
                    if (existingRequest.Status == GenericRequest.STATUS_FAILED)
                    {
                        _globalRequestQueue.Remove(existingRequest);
                        _globalRequestQueue.Add(request);
                        _newRequestEvent.Set();
                    }
                    else
                    {
                        // do nothing
                    }
                }
                else
                {
                    _globalRequestQueue.Add(request);
                    _newRequestEvent.Set();
                }
            }
        }

        public void RemoveAllRequestsFromQueues(IPAddress clientAddress)
        {
            lock (_globalRequestQueue)
            {
                if (_clientRequestQueueMap.ContainsKey(clientAddress))
                {
                    List<LocalRequest> clientRequests = _clientRequestQueueMap[clientAddress];

                    foreach (LocalRequest request in clientRequests)
                    {
                        _globalRequestQueue.Remove(request);
                    }
                }
            }

            // remove the request from the client's queue
            lock (_clientRequestQueueMap)
            {
                if (_clientRequestQueueMap.ContainsKey(clientAddress))
                {
                    List<LocalRequest> clientRequests = _clientRequestQueueMap[clientAddress];

                    lock (clientRequests)
                    {
                        _clientRequestQueueMap.Remove(clientAddress);
                    }
                }
            }
        }

        // removes the request from the global queue and client's queue
        public void RemoveRequestFromQueues(IPAddress clientAddress, LocalRequest request)
        {
            lock (_globalRequestQueue)
            {
                if (_globalRequestQueue.Contains(request))
                {
                    _globalRequestQueue.Remove(request);
                }
            }

            // remove the request from the client's queue
            lock (_clientRequestQueueMap)
            {
                if (_clientRequestQueueMap.ContainsKey(clientAddress))
                {
                    List<LocalRequest> clientRequests = _clientRequestQueueMap[clientAddress];

                    lock (clientRequests)
                    {
                        if (clientRequests.Contains(request))
                        {
                            clientRequests.Remove(request);
                        }
                    }
                }
            }
        }

        public LocalRequest GetFirstRequestFromGlobalQueue()
        {
            return _globalRequestQueue[0];
        }

        public void RemoveFirstRequestFromGlobalQueue()
        {
            lock (_globalRequestQueue)
            {
                _globalRequestQueue.RemoveAt(0);
            }
        }

        public int OutstandingRequests(IPAddress clientAddress)
        {
            // XXX: readerlock on _clientRequestQueueMap
            if (_clientRequestQueueMap.ContainsKey(clientAddress))
            {
                List<LocalRequest> clientRequests = _clientRequestQueueMap[clientAddress];
                return clientRequests.Count;
            }
            return 0;
        }

        public int SatisfiedRequests(IPAddress clientAddress)
        {
            // XXX: readerlock on _clientRequestQueueMap
            if (_clientRequestQueueMap.ContainsKey(clientAddress))
            {
                List<LocalRequest> clientRequests = _clientRequestQueueMap[clientAddress];
                int count = 0;
                foreach (LocalRequest request in clientRequests)
                {
                    if (request.Status == GenericRequest.STATUS_SATISFIED ||
                        request.Status == GenericRequest.STATUS_FAILED)
                    {
                        count++;
                    }
                }
                return count;
            }
            return 0;
        }

        public LocalRequest GetLastRequest(IPAddress clientAddress)
        {
            // XXX: readerlock on _clientRequestQueueMap
            if (_clientLastRequestMap.ContainsKey(clientAddress))
            {
                return _clientLastRequestMap[clientAddress];
            }
            return null;
        }
        public void SetLastRequest(IPAddress clientAddress, LocalRequest request)
        {
            // XXX: readerwriterlock on _clientLastRequestMap
            lock (_clientLastRequestMap)
            {
                if (!_clientLastRequestMap.ContainsKey(clientAddress))
                {
                    _clientLastRequestMap.Add(clientAddress, request);
                }
                else
                {
                    _clientLastRequestMap[clientAddress] = request;
                }
            }
            return;
        }

        public List<LocalRequest> GetClientRequestQueue(IPAddress clientAddress)
        {
            // XXX: readerlock on _clientRequestQueueMap
            if (_clientRequestQueueMap.ContainsKey(clientAddress))
            {
                return _clientRequestQueueMap[clientAddress];
            }
            return null;
        }

        // adds the results from a search response to our local state for faster access later
        // XXX: probably want to change this so that even individual terms are represented or something
        // XXX: the whole local search infrastructure needs to get rewritten, this is probably just going to 
        // XXX: get salvaged as a piece of that
        /*
        private void AddResultsToState(LocalRequest request) {
            if (request.IsRuralCafeSearchQuery())
            {
                string searchString = request.GetRuralCafeSearchString();
                LinkedList<Result> results = request.GetRuralCafeSearchResults();
                // if the query and results already exists replace it
                if (_searchResults.ContainsKey(searchString))
                {
                    _searchResults.Remove(searchString);
                }

                // add the key value back into the searchresults
                _searchResults.Add(searchString, results);
            }
        }*/

        // updates the time per request given a timespan
        // Exponential moving average alpha = 0.2
        private void DispatchStartTimestamp()
        {
            _dispatchStartTime = DateTime.Now;
        }
        private void DispatchEndTimestamp()
        {
            _dispatchEndTime = DateTime.Now;
        }
        private void UpdateTimePerRequest() 
        {
            TimeSpan totalProcessingTime = _dispatchEndTime - _dispatchStartTime;

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

        // returns the number of seconds until request is expected to be satisfied
        // gets the ETA by looking at the average satisfied time and the position of this request
        public int ETA(LocalRequest request)
        {
            // set the predicate object
            _predicateObj = request;
            int requestPosition = _globalRequestQueue.FindIndex(SameRequestObject);
            // +2 since -1 if the item doesn't exist (which means its being serviced now)
            return (int)((requestPosition + 2) * _averageTimePerRequest.TotalSeconds);
        }

        // predicate method for findindex in ETA()
        // XXX: ugly as sin
        private LocalRequest _predicateObj = null;
        private bool SameRequestObject(LocalRequest requestObj)
        {
            if(_predicateObj == null ||
                requestObj == null)
                return false;

            if (_predicateObj.RequestUri == requestObj.RequestUri)
            {
                return true;
            }
            return false;
        }
    }
}
