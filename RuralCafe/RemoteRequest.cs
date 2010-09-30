using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Threading;
//using System.Web;

namespace RuralCafe
{
    public class RemoteRequest : GenericRequest
    {
        public const int SEARCH_NOPREF = 0;
        public const int SEARCH_ALG1 = 1;
        public const int SEARCH_ALG2 = 2;
        public static int DEFAULT_QUOTA;
        public static int DEFAULT_DEPTH; 
        public static int DEFAULT_LOW_WATERMARK;
        // remote request timeout is how long the remote request gets to prefetch
        // the web request default should always be <= to it
        public const int REQUEST_PACKAGE_DEFAULT_TIMEOUT = 300000; // in milliseconds
        public const int WEB_REQUEST_DEFAULT_TIMEOUT = 30000; // in milliseconds
        private static int _nextId = 1;
        private long _quota;

        // multithreading event
        public LinkedList<RequestObject> embeddedObjects;

        /*
        // benchmarking stuff
        protected DateTime handleRequestStart;
        protected DateTime handleRequestEnd;
        protected DateTime downloadPagesStart;
        protected DateTime downloadPagesEnd;
        protected DateTime transmitStart;
        protected DateTime transmitEnd;
        */

        public static List<int> linksOnResultsPage = new List<int>();
        public static List<int> imagesOnResultsPage = new List<int>();

        public static List<int> imagesOnTargetPage = new List<int>();

        public RemoteRequest(RemoteProxy proxy, Socket socket) 
            : base(proxy, socket)
        {
            _requestId = _nextId++;
            _quota = 0;
            _timeout = REQUEST_PACKAGE_DEFAULT_TIMEOUT;
        }

        ~RemoteRequest()
        {
            // cleanup stuff
        }

        // instead of testing for equality of reference
        // just check if the actual requested pages are equivalent
        public override bool Equals(object obj)
        {
            return base.Equals(obj);
        }
        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        // this is the entry point and main logic of RuralCafe remote proxy
        public override void HandlePageRequest()
        {
            _package = new Package();
            // benchmarking
            //handleRequestStart = DateTime.Now;

            // ignore non-ruralcafe requests
            if (!IsRCRemoteQuery())
            {
                LogDebug("error not RuralCafe URL or search request: " + RequestUri);
                return;
            }

            LogRequest();
            
            // make sure headers all make sense
            string richness = _requestObject.GetRCSearchField("richness");
            if (!richness.Equals("high") &&
                !richness.Equals("medium") &&
                !richness.Equals("low"))
            {
                richness = "normal";
            }

            string depth_s = _requestObject.GetRCSearchField("depth");
            int depth = DEFAULT_DEPTH;
            if (depth_s.Equals("more"))
            {
                depth++;
            }

            _quota = DEFAULT_QUOTA;

            // XXX: static quota for now
            /* QUOTA parameterization in the UI
                // get the quota
                string quotaString = GetRuralCafeSearchField("quota");
                long remainingQuota = Int32.Parse(quotaString);
                if (quotaString.Equals(""))
                {
                    // no quota
                    remainingQuota = 1000000000; // XXX: very large number
                }
                else
                {
                    try
                    {
                        remainingQuota = Int32.Parse(quotaString);
                    }
                    catch (Exception e)
                    {
                        remainingQuota = 0;
                        LogException("Couldn't parse quota: " + e.StackTrace + " " + e.Message);
                    }                
                }*/
            
            if (IsRCURLRequest())
            {
                LogDebug("page request, downloading page as package");
                string requestUri = _requestObject.GetRCSearchField("textfield");

                if (requestUri.Trim().Length > 0)
                {
                    // since there's no results page we want an additional level of results
                    depth++;
                    string fileExtension = Util.GetFileExtension(requestUri);
                    if (!requestUri.StartsWith("http://"))
                    {
                        requestUri = "http://" + requestUri;
                    }

                    if (IsCacheable())
                    {
                        // reset the object to without the RuralCafe stuff
                        _requestObject = new RequestObject(_proxy, requestUri);

                        PrefetchURLPackage(richness, depth);
                        SendRequestResults();
                    }
                    else
                    {
                        // XXX: not streaming this for now since it would mess with the admission control stuff
                        LogDebug("not cacheable, returning nothing");
                        //_responseSize = StreamRequestFromProxyToClient();
                    }
                }
            }
            else
            {
                LogDebug("RuralCafe search request: " + RequestUri);

                /* XXX: disabled broad/deep for now
                // search type
                string searchType = GetRuralCafeSearchField("specificity");
                if (searchType.Equals("normal") ||
                    searchType.Equals("broad"))
                {
                    PrefetchBFS(richness, depth);
                }
                else if (searchType.Equals("deep"))
                {
                    PrefetchDFS(richness, depth);
                }
                else
                {
                    // shouldn't ever happen
                    LogDebug("error no specificity setting");
                    return;
                }
                 */
                PrefetchBFS(richness, depth);
                SendRequestResults();
            }

            // benchmarking
            //handleRequestEnd = DateTime.Now;
            //SaveBenchmarkTimes();

            return;
        }
        /*
        public void SaveBenchmarkTimes()
        {
            // benchmarking
            TimeSpan totalProcessingTime = handleRequestEnd.Subtract(handleRequestStart);
            TimeSpan downloadPagesTime = downloadPagesEnd.Subtract(downloadPagesStart);
            TimeSpan transmitDataTime = transmitEnd.Subtract(transmitStart);

            TextWriter tw = new StreamWriter("remote.totalProcessingTime.out", true);
            tw.Write(totalProcessingTime.TotalMilliseconds + "\n");
            tw.Close();
            tw = new StreamWriter("remote.downloadPagesTime.out", true);
            tw.Write(downloadPagesTime.TotalMilliseconds + "\n");
            tw.Close();
            tw = new StreamWriter("remote.transmitDataTime.out", true);
            tw.Write(transmitDataTime.TotalMilliseconds + "\n");
            tw.Close();
        }
        */
        #region Search Algorithms

        // ZZZ: benchmarking stuff
        public override void PrefetchBenchmarker(string richness, int depth)
        {
            LogDebug("Running Benchmarker");

            // XXX: should add a parameter to always download or just read from cache
            // convert to Uri format
            //string pageUri = _webRequestUri;
            LogRequest();

            int status = DownloadPage(_proxy, this, _requestObject);

            FileInfo f;
            try
            {
                f = new FileInfo(_requestObject._cacheFileName);
                if (status == STATUS_FAILED || !f.Exists)
                {
                    return;
                }
            }
            catch (Exception e)
            {
                LogDebug("problem getting file info " + e.StackTrace + " " + e.Message);
                return;
            }

            // get the embedded content of the search result page
            LinkedList<RequestObject> objectsFound = DownloadEmbeddedObjects(_requestObject, richness);
            // benchmarking: store the number of images found
            imagesOnResultsPage.Add(objectsFound.Count);

            // recursively download pages
            LinkedList<RequestObject> resultLinkUris = ExtractGoogleResults(_requestObject);
            // benchmarking: store the number of links found
            linksOnResultsPage.Add(resultLinkUris.Count);
            foreach (RequestObject linkObject in resultLinkUris)
            {
                //RequestObject requestObject = new RequestObject(linkObject);
                linkObject._status = DownloadPage(_proxy, this, linkObject);
                try
                {
                    f = new FileInfo(linkObject._cacheFileName);
                }
                catch (Exception)
                {
                    linkObject._status = STATUS_FAILED;
                    continue;
                }
                if (linkObject._status == STATUS_FAILED || !f.Exists)
                {
                    linkObject._status = STATUS_FAILED;
                    continue;
                }

                // XXX: hackery
                // make a copy of this file
                try
                {
                    // create directory if it doesn't exist
                    if (!Util.CreateDirectoryForFile(linkObject._cacheFileName))
                    {
                        return;
                    }
                    // create directory if it doesn't exist
                    if (!Util.CreateDirectoryForFile("ZZZZZZ\\" + linkObject._cacheFileName))
                    {
                        return;
                    }

                    File.Delete("ZZZZZZ\\" + linkObject._cacheFileName);
                    File.Copy(linkObject._cacheFileName, "ZZZZZZ\\" + linkObject._cacheFileName);

                    // skip parseable check
                    if (!Util.IsParseable(linkObject))
                    {
                        continue;
                    }

                    // get the embedded content of the search result page
                    objectsFound = DownloadEmbeddedObjects(linkObject, richness);
                    // benchmarking: store the number of images on the page
                    imagesOnTargetPage.Add(objectsFound.Count);

                    File.Delete(linkObject._cacheFileName);
                }
                catch (Exception e)
                {
                    LogDebug("problem downloading a file or something " + e.StackTrace + " " + e.Message);
                }
            }
        }
        
        // DFS
        void PrefetchDFS(string richness, int depth)
        {
            // benchmarking
            //downloadPagesStart = DateTime.Now;

            LogDebug("Running DFS");

            // reconstruct _requestObject
            string pageUri = _requestObject.TranslateRCSearchToGoogle();
            if (!Util.IsValidUri(pageUri))
            {
                return;
            }
            _requestObject = new RequestObject(_proxy, pageUri);

            int status = DownloadPage(_proxy, this, _requestObject);
            FileInfo f;
            try
            {
                f = new FileInfo(_requestObject._cacheFileName);
                if (status == STATUS_FAILED || !f.Exists)
                {
                    return;
                }
            }
            catch (Exception e)
            {
                LogDebug("Error getting file info: " + e.StackTrace + " " + e.Message);
                return;
            }

            // add to the package
            string errorMessage = _package.AddToPackage(_requestObject, _quota);
            if (!errorMessage.Equals(""))
            {
                LogDebug(errorMessage);
                return;
            }
            else
            {
                _quota -= _requestObject._fileSize;
                LogDebug("packed: " + RequestUri + " " + _requestObject._fileSize + " bytes" + _quota + " left");
            }

            // check quota
            if (_quota < DEFAULT_LOW_WATERMARK)
            {
                // benchmarking
                //downloadPagesEnd = DateTime.Now;

                return;
            }

            // get the embedded content of the search result page
            DownloadEmbeddedObjects(_requestObject, richness);
            if (_quota < DEFAULT_LOW_WATERMARK)
            {
                // benchmarking
                //downloadPagesEnd = DateTime.Now;

                return;
            }

            // recurse if necessary
            LinkedList<RequestObject> resultLinkUris = ExtractGoogleResults(_requestObject);
            foreach (RequestObject linkObject in resultLinkUris)
            {
                RecursivelyDownloadPage(linkObject, richness, depth);
                if (_quota < DEFAULT_LOW_WATERMARK)
                {
                    // benchmarking
                    //downloadPagesEnd = DateTime.Now;

                    return;
                }
            }
        }

        // BFS
        void PrefetchBFS(string richness, int depth)
        {
            // benchmarking
            //downloadPagesStart = DateTime.Now;

            LogDebug("Running BFS");

            // reconstruct _requestObject
            string pageUri = _requestObject.TranslateRCSearchToGoogle();
            if (!Util.IsValidUri(pageUri))
            {
                return;
            }
            _requestObject = new RequestObject(_proxy, pageUri);
            int status = DownloadPage(_proxy, this, _requestObject);
            FileInfo f;
            try
            {
                f = new FileInfo(_requestObject._cacheFileName);
                if (status == STATUS_FAILED || !f.Exists)
                {
                    return;
                }
            }
            catch (Exception e)
            {
                LogDebug("Error getting file info: " + e.StackTrace + " " + e.Message);
                return;
            }

            // add to the package
            string errorMessage = _package.AddToPackage(_requestObject, _quota);
            if (!errorMessage.Equals(""))
            {
                LogDebug(errorMessage);
                return;
            }
            else
            {
                _quota -= _requestObject._fileSize;
                LogDebug("packed: " + RequestUri + " " + _requestObject._fileSize + " bytes" + _quota + " left");
            }

            // check quota
            if (_quota < DEFAULT_LOW_WATERMARK)
            {
                // benchmarking
                //downloadPagesEnd = DateTime.Now;

                return;
            }

            // setup the initial frontier
            LinkedList<RequestObject> currentBFSFrontier = ExtractGoogleResults(_requestObject);
            LinkedList<RequestObject> nextBFSFrontier = new LinkedList<RequestObject>();
            int currentDepth = 0;
            while (currentDepth < depth)
            {
                // download objects in parallel
                currentBFSFrontier = DownloadObjectsInParallel(_requestObject, currentBFSFrontier);

                // download embedded objects for each downloaded object
                foreach (RequestObject currObject in currentBFSFrontier)
                {
                    // check quota
                    if (_quota < DEFAULT_LOW_WATERMARK)
                    {
                        // quota met
                        break;
                    }

                    // download embedded objects
                    DownloadEmbeddedObjects(currObject, richness);
                }

                if (_quota < DEFAULT_LOW_WATERMARK)
                {
                    // quota met
                    break;
                }

                // get the next frontier from the current ones
                nextBFSFrontier = GetNewBFSFrontier(currentBFSFrontier);
                currentBFSFrontier = nextBFSFrontier;
                currentDepth++;
            }

            //downloadPagesEnd = DateTime.Now;
        }

        LinkedList<RequestObject> GetNewBFSFrontier(LinkedList<RequestObject> currentBFSFrontier)
        {
            LinkedList<RequestObject> nextBFSFrontier = new LinkedList<RequestObject>();
            LinkedList<RequestObject> extractedLinks;

            // go through the current frontier and collect the links
            foreach (RequestObject requestObject in currentBFSFrontier)
            {
                // get all the links
                extractedLinks = ExtractLinks(requestObject);

                // add to the frontier if we haven't seen it recently
                foreach (RequestObject extractedLink in extractedLinks)
                {
                    // ignore blacklisted domains
                    if (_proxy.IsBlacklisted(extractedLink._uri))
                    {
                        continue;
                    }

                    if (!currentBFSFrontier.Contains(extractedLink) &&
                        !nextBFSFrontier.Contains(extractedLink))
                    {
                        nextBFSFrontier.AddLast(extractedLink);
                    }
                }

            }
            return nextBFSFrontier;
        }
        // starts the recursive download of a page
        void PrefetchURLPackage(string richness, int depth)
        {
            // benchmarking
            //downloadPagesStart = DateTime.Now;

            LogDebug("Downloading as a package: " + RequestUri);

            RecursivelyDownloadPage(_requestObject, richness, depth);

            // benchmarking
            //downloadPagesEnd = DateTime.Now;
        }

        // XXX: gets a page and its embedded contents, then recurses serially
        void RecursivelyDownloadPage(RequestObject requestObject, string richness, int depth)
        {
            if (depth == 0)
            {
                return;
            }

            // download the page
            int status = DownloadPage(_proxy, this, requestObject);
            FileInfo f;
            try
            {
                f = new FileInfo(requestObject._cacheFileName);
                if (status == STATUS_FAILED || !f.Exists)
                {
                    return;
                }
            }
            catch (Exception e)
            {
                LogDebug("Error getting file info: " + e.StackTrace + " " + e.Message);
                return;
            }

            // add to the package
            string errorMessage = _package.AddToPackage(requestObject, _quota);
            if (!errorMessage.Equals(""))
            {
                LogDebug(errorMessage);
                return;
            }
            else
            {
                _quota -= requestObject._fileSize;
                LogDebug("packed: " + requestObject._uri + " " + requestObject._fileSize + " bytes" + _quota + " left");
            }

            // check quota
            if (_quota < DEFAULT_LOW_WATERMARK)
            {
                return;
            }

            // check for parseable since its just some URL
            if (!Util.IsParseable(requestObject))
            {
                return;
            }

            // get the embedded content of the search result page
            DownloadEmbeddedObjects(requestObject, richness);

            // check quota
            if (_quota < DEFAULT_LOW_WATERMARK)
            {
                return;
            }

            // recurse if necessary
            LinkedList<RequestObject> resultLinkUris = ExtractLinks(requestObject);
            foreach (RequestObject currObject in resultLinkUris)
            {
                RecursivelyDownloadPage(currObject, richness, depth - 1);
                if (_quota < DEFAULT_LOW_WATERMARK)
                {
                    return;
                }
            }
        }

        // download embedded objects based on richness setting
        LinkedList<RequestObject> DownloadEmbeddedObjects(RequestObject requestObject, string richness)
        {
            embeddedObjects = ExtractEmbeddedObjects(requestObject);
            LinkedList<RequestObject> filteredEmbeddedObjects = new LinkedList<RequestObject>();

            // filter out based on richness
            foreach (RequestObject embeddedObject in embeddedObjects)
            {
                // ignore blacklisted domains
                if (_proxy.IsBlacklisted(embeddedObject._uri))
                {
                    continue;
                }

                if (richness.Equals("medium"))
                {
                    if (IsImagePage(embeddedObject._uri))
                    {
                        filteredEmbeddedObjects.AddLast(embeddedObject);
                    }
                }
                else if (richness.Equals("low"))
                {
                    // XXX: logic here is ugly, and not perfect 
                    // XXX: since the implementation of PossiblyATextPage is incomplete
                    // if its an image or couldn't possibly be a text page
                    if (!IsImagePage(embeddedObject._uri) && PossiblyATextPage(embeddedObject._uri))
                    {
                        filteredEmbeddedObjects.AddLast(embeddedObject);
                    }
                }
            }
            embeddedObjects = filteredEmbeddedObjects;

            return DownloadObjectsInParallel(requestObject, embeddedObjects);
        }

        LinkedList<RequestObject> DownloadObjectsInParallel(RequestObject requestObject, LinkedList<RequestObject> subRequestObjects)
        {
            LinkedList<RequestObject> addedObjects = new LinkedList<RequestObject>();

            if (subRequestObjects.Count == 0)
            {
                return addedObjects;
            }
            
            requestObject.resetEvents = new ManualResetEvent[subRequestObjects.Count];

            try
            {
                for (int i = 0; i < subRequestObjects.Count; i++)
                {
                    // set the resetEvent
                    requestObject.resetEvents[i] = new ManualResetEvent(false);

                    RequestObject embeddedObject;
                    try
                    {
                        embeddedObject = subRequestObjects.ElementAt(i);
                        embeddedObject._webRequest.Proxy = _proxy.RemoteWebProxy;
                        embeddedObject._webRequest.Timeout = _timeout;
                    }
                    catch (Exception e)
                    {
                        // skip it
                        LogDebug("unable to create WebRequest: " + e.StackTrace + " " + e.Message);
                        requestObject.SetEvent(i);
                        continue;
                    }

                    // make sure we haven't downloaded this before
                    if (_package.GetObjects().Contains(embeddedObject))
                    {
                        // skip it
                        requestObject.SetEvent(i);
                        continue;
                    }                    
 
                    // download the page
                    LogDebug("queueing: " + embeddedObject._uri);
                    RequestWrapper wrapperObject = new RequestWrapper(_proxy, this, requestObject, embeddedObject._uri, embeddedObject._cacheFileName, i);
                    ThreadPool.QueueUserWorkItem(new WaitCallback(DownloadPageWorkerThread), (object)wrapperObject);
                }

                // wait for timeout
                WaitAll(requestObject.resetEvents);

                // add files that were completed to the package
                foreach(RequestObject currentRequestObject in subRequestObjects)
                {
                    // check watermark
                    if (_quota > DEFAULT_LOW_WATERMARK)
                    {
                        // add to the package
                        string errorMessage = _package.AddToPackage(currentRequestObject, _quota);
                        if (!errorMessage.Equals(""))
                        {
                            LogDebug(errorMessage);
                        }
                        else
                        {
                            _quota -= currentRequestObject._fileSize;
                            addedObjects.AddLast(currentRequestObject);
                            LogDebug("packed: " + currentRequestObject._uri + " " + currentRequestObject._fileSize + " bytes, " + _quota + " left");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                LogDebug("unable to download embeddedObjects: " + e.StackTrace + " " + e.Message);
            }

            return addedObjects;
        }

        private void WaitAll(WaitHandle[] waitHandles)
        {
            /*
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
             
                // WaitAll for multiple handles on an STA thread is not supported.
                // ...so wait on each handle individually.
                foreach (WaitHandle myWaitHandle in waitHandles)
                {
                    WaitHandle.WaitAny(new WaitHandle[] { myWaitHandle });
                }
            }
            else
            {
                WaitHandle.WaitAll(waitHandles);
            }
             */
            foreach (WaitHandle myWaitHandle in waitHandles)
            {
                WaitHandle.WaitAny(new WaitHandle[] { myWaitHandle });
            }
        }

        protected int DownloadPage(GenericProxy proxy, RemoteRequest remoteRequest, RequestObject requestObject)
        {
            HttpWebRequest webRequest;
            try
            {
                // create the webRequest
                LogDebug("downloading: " + requestObject._uri);
                webRequest = (HttpWebRequest)WebRequest.Create(requestObject._uri);
                webRequest.Proxy = proxy.RemoteWebProxy;
                webRequest.Timeout = WEB_REQUEST_DEFAULT_TIMEOUT;
            }
            catch (Exception e)
            {
                LogDebug("unable to create WebRequest: " + e.StackTrace + " " + e.Message);
                return STATUS_FAILED;
            }

            // stream the page
            int status = StreamRequestFromServerToCache(proxy, remoteRequest, webRequest, requestObject._cacheFileName);
            return status;
        }
        // helper function to download a webpage given the page Uri and the destination fileName
        protected void DownloadPageWorkerThread(object tempObject)
        {
            RequestWrapper requestWrapper = (RequestWrapper)tempObject;

            // check to see if the time is up for this overall request object
            if (!requestWrapper.RootRequest.IsTimedOut())
            {
                HttpWebRequest webRequest;
                RemoteProxy proxy = (RemoteProxy)requestWrapper.Proxy;
                int requestId = requestWrapper.RootRequest.ID;
                try 
                {
                    // create the webRequest
                    LogDebug("downloading page: " + requestWrapper.PageUri);
                    webRequest = (HttpWebRequest)WebRequest.Create(requestWrapper.PageUri);
                    webRequest.Proxy = requestWrapper.Proxy.RemoteWebProxy;
                    webRequest.Timeout = WEB_REQUEST_DEFAULT_TIMEOUT;
                }
                catch (Exception e)
                {
                    LogDebug("unable to create WebRequest: " + e.StackTrace + " " + e.Message);
                    return;
                }

                // stream the page
                int status = StreamRequestFromServerToCache(requestWrapper.Proxy, requestWrapper.RootRequest, webRequest, requestWrapper.FileName);
            }

            // mark this thread as done
            RequestObject requestObject = requestWrapper.CurrentRequestObject;
            int objectNumber = requestWrapper.ObjectNum;
            requestObject.SetEvent(objectNumber);
        }

        #endregion

        #region Request and Response Methods

        // sends the request result package to the client
        void SendRequestResults()
        {
            // benchmarking
            //transmitStart = DateTime.Now;

            // build the package index
            if (!BuildPackageIndex())
            {
                return;
            }

            /*
            MemoryStream ms = GetGzipStreamOfPagesFromCache();
            if (ms == null)
            {
                SendErrorPage(404, "Problem streaming the package from disk to client", "null memory stream");
                return;
            }
            _package._compressedSize = ms.Length;
            */

            LogDebug("sending results package: " + (_package._indexSize + _package._contentSize) + " bytes at " + GenericProxy.DEFAULT_MAX_DOWNLOAD_SPEED + " bps" );
            SendPackageHeaders();
            //StreamToClient(ms);

            // stream out the pages (no compression)
            LinkedList<string> fileNames = new LinkedList<string>();
            fileNames.AddLast(_requestObject._packageIndexFileName);
            foreach (RequestObject requestObject in _package.GetObjects())
            {
                fileNames.AddLast(requestObject._cacheFileName);
            }
            MemoryStream ms = GZipWrapper.GZipCompress(fileNames);
            StreamToClient(ms);
//            StreamPackageToClient(fileNames);

            /*
            // XXX: uncompressed version, depricated
            // stream out the package file
            long packageSize = StreamPageFromDiskToClient(_requestObject._packageIndexFileName);
            // stream out the pages
            foreach (RequestObject requestObject in _package.GetObjects())
            {
                packageSize += StreamPageFromDiskToClient(requestObject._cacheFileName);
            }*/

            // benchmarking
            //transmitEnd = DateTime.Now;
        }

        long StreamPackageToClient(LinkedList<string> fileNames)
        {
            long bytesSent = 0;
            DateTime startTime = DateTime.Now;

            foreach (string fileName in fileNames)
            {
                FileInfo f;
                try
                {
                    f = new FileInfo(fileName);

                    if (!f.Exists)
                    {
                        LogDebug("error file doesn't exist: " + fileName);
                        return bytesSent;
                    }
                }
                catch (Exception e)
                {
                    LogDebug("problem getting file info: " + fileName + " " + e.StackTrace + " " + e.Message);
                    return bytesSent;
                }

                FileStream fs = null;
                try
                {
                    int offset = 0;
                    byte[] buffer = new byte[32]; // magic number 32
                    // open the file stream
                    fs = f.Open(FileMode.Open, FileAccess.Read);

                    // loop and get the bytes we need if we couldn't get it in one go
                    int bytesRead = fs.Read(buffer, 0, 32);
                    while (bytesRead > 0)
                    {
                        // send bytes
                        _clientSocket.Send(buffer, bytesRead, 0);
                        bytesSent += bytesRead;

                        // read bytes from cache
                        bytesRead = fs.Read(buffer, 0, 32);

                        offset += bytesRead;
                    }
                }
                catch (Exception e)
                {
                    SendErrorPage(404, "problem serving from RuralCafe cache", e.StackTrace + " " + e.Message);
                    //LogDebug("404 Problem serving from RuralCafe cache");
                }
                finally
                {
                    if (fs != null)
                    {
                        fs.Close();
                    }

                }
            }
            return bytesSent;
        }

        /*
        MemoryStream GetGzipStreamOfPagesFromCache()
        {
            LinkedList<string> fileNames = new LinkedList<string>();
            fileNames.AddLast(_requestObject._packageIndexFileName);
            foreach (RequestObject requestObject in _package.GetObjects())
            {
                fileNames.AddLast(requestObject._cacheFileName);
            }
            return GZipWrapper.GZipCompress(fileNames);
        }*/

        // combines all the Uri's in the package into the Package-Content header field
        void SendPackageHeaders()
        {
            int status = HTTP_OK;
            string strReason = "";
            string str = "";

            str = "HTTP/1.1" + " " + status + " " + strReason + "\r\n" +
            "Content-Type: ruralcafe-package" + "\r\n";

            // gzip stuff
            str += "Content-Encoding: gzip\r\n";
//            str += "Content-Length: ";
//            str += _package._indexSize + _package._contentSize;
//            str += "\r\n";

            str += "Package-IndexSize: ";
            str += _package._indexSize;
            str += "\r\n";

            str += "Package-ContentSize: ";
            str += _package._contentSize;
            str += "\r\n";

            str += "Proxy-Connection: close" + "\r\n" +
                "\r\n";

            //LogDebug("Sending package: " + str);
            SendMessage(str);
        }
        bool BuildPackageIndex()
        {
            _package._contentSize = 0;
            try
            {
                if (!Util.CreateDirectoryForFile(_requestObject._packageIndexFileName))
                {
                    return false;
                }
                if (!Util.DeleteFile(_requestObject._packageIndexFileName))
                {
                    return false;
                }

                TextWriter tw = new StreamWriter(_requestObject._packageIndexFileName);

                // create the package index file
                foreach (RequestObject requestObject in _package.GetObjects())
                {
                    tw.WriteLine(requestObject._uri + " " + requestObject._fileSize.ToString());
                    _package._contentSize += requestObject._fileSize;
                }

                tw.Close();

                // calculate the index size
                _package._indexSize = Util.GetFileSize(_requestObject._packageIndexFileName);
                if (_package._indexSize < 0)
                {
                    LogDebug("problem getting file info: " + _requestObject._packageIndexFileName);
                }
            }
            catch (Exception e)
            {
                LogDebug("problem creating package file: " + _requestObject._packageIndexFileName + " " + e.StackTrace + " " + e.Message);
                return false;
            }

            return true;
        }
        /* depricated, using a package file now
        // combines all the Uri's in the package into the Package-Content header field
        void SendPackageOkHeaders()
        {
            int status = HTTP_OK;
            string strReason = "";
            string str = "";
            string str2 = "";

            str = "HTTP/1.1" + " " + status + " " + strReason + "\r\n" +
            "Content-Type: ruralcafe-package" + "\r\n";
            str += "Package-Content: \"";

            foreach (RequestObject requestObject in _package.GetObjects())
            {
                // XXX: kind of kludgy to have this here...
                if (requestObject._fileSize == 0)
                {
                    continue;
                }
                str2 += "\"" + requestObject._fileSize.ToString() + "\"";

                str += "\"" + requestObject._uri + "\"";
            }
            str += "\"\r\n";

            str += "Package-ContentSize: ";
            str += "\"" + str2 + "\""; // bracket with " for splitting at localproxy, same for the Uris
            str += "\r\n";

            str += "Proxy-Connection: close" + "\r\n" +
                "\r\n";

            LogDebug("Sending package: " + str);
            SendMessage(_clientSocket, str);
        }
        */
        #endregion

        #region Remote Proxy Specific Helper Functions

        // checks if the file suffix looks like a text page
        bool PossiblyATextPage(string pageUri)
        {
            int offset = pageUri.LastIndexOf('.');
            if (offset > 0)
            {
                pageUri = pageUri.Substring(offset);
                string contentType = Util.GetContentType(pageUri);
                if (contentType.Contains("image") ||
                    contentType.Contains("audio"))
                {
                    return false;
                }
            }

            // no idea
            return true;
        }
        bool IsImagePage(string pageUri)
        {
            int offset = pageUri.LastIndexOf('.');
            if (offset > 0)
            {
                string suffix = pageUri.Substring(offset);
                if (Util.GetContentType(suffix).Contains("image"))
                {
                    // not an image
                    return true;
                }
            }
            return false;
        }

        /*
        // XXX: not working well, depricated
        bool IsProbablyATextPage(string pageUri)
        {
            // index page
            if(pageUri.EndsWith("/")) {
                return true;
            }

            // get page extension
            string suffix;
            int offset = pageUri.LastIndexOf('/');
            if (offset > 0)
            {
                suffix = pageUri.Substring(offset + 1);

                offset = suffix.LastIndexOf('.');
                if (offset > 0)
                {
                    suffix = suffix.Substring(offset);

                    if (GetContentType(suffix).Contains("text"))
                    {
                        return true;
                    }

                    if (IsParseable(pageUri))
                    {
                        return true;
                    }

                    if (suffix.Equals(".com") ||
                        suffix.Equals(".net") ||
                        suffix.Equals(".org"))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
         */

        // extract the links from a page
        LinkedList<RequestObject> ExtractEmbeddedObjects(RequestObject requestObject)
        {
            string[] stringSeparator = new string[] { "src=\"" };

            //string splitToken = "src=\"";
            return ExtractReferences(requestObject, stringSeparator);
        }
        // XXX: not completely implemented, need non href references
        // extracts the links from a page
        LinkedList<RequestObject> ExtractLinks(RequestObject requestObject)
        {
            string[] stringSeparator = new string[] { "a href=\"" };

            //string splitToken = "href=\"";
            return ExtractReferences(requestObject, stringSeparator);
        }

        // extracts the result links from a google results page
        LinkedList<RequestObject> ExtractGoogleResults(RequestObject requestObject)
        {
            string[] stringSeparator = new string[] { "<cite>" };
            LinkedList<RequestObject> resultLinks = new LinkedList<RequestObject>();
            string fileString = Util.ReadFileAsString(requestObject._cacheFileName);
            string[] lines = fileString.Split(stringSeparator, StringSplitOptions.RemoveEmptyEntries);

            // get links
            int pos;
            string currLine;
            string currUri;
            // stagger starting index by 1 since first split can't be a link
            for (int i = 1; i < lines.Length; i++)
            {
                currLine = (string)lines[i];
                // to the next " symbol
                if ((pos = currLine.IndexOf("</cite>")) > 0)
                {
                    currUri = currLine.Substring(0, pos);

                    if ((pos = currUri.IndexOf(" - ")) > 0)
                    {
                        currUri = currUri.Substring(0, pos);
                    }

                    currUri = currUri.Replace("<b>", "");
                    currUri = currUri.Replace("</b>", "");
                    currUri = currUri.Replace(" ", "");

                    // instead of translating to absolute, prepend http:// to make webrequest constructor happy
                    currUri = "http://" + currUri;

                    if (!Util.IsValidUri(currUri))
                    {
                        continue;
                    }

                    // check blacklist
                    if (_proxy.IsBlacklisted(currUri))
                    {
                        continue;
                    }

                    RequestObject currObject = new RequestObject(_proxy, currUri);
                    
                    resultLinks.AddLast(currObject);
                } 
            }

            return resultLinks;
        }

        // extracts the html references using a separator token and returns them
        LinkedList<RequestObject> ExtractReferences(RequestObject requestObject, string[] stringSeparator)
        {
            LinkedList<RequestObject> extractedReferences = new LinkedList<RequestObject>();

            string fileString = Util.ReadFileAsString(requestObject._cacheFileName);

            string[] lines = fileString.Split(stringSeparator, StringSplitOptions.RemoveEmptyEntries);

            // get links
            int pos;
            string currLine;
            string currUri;
            // stagger starting index by 1 since first split can't be a link
            for (int i = 1; i < lines.Length; i++)
            {
                currLine = (string)lines[i];
                // to the next " symbol
                if ((pos = currLine.IndexOf("\"")) > 0)
                {
                    currUri = currLine.Substring(0, pos);

                    // convert to absolute addresses before setting as a uri
                    currUri = TranslateToAbsoluteAddress(requestObject._uri, currUri);
                    // XXX: need to make sure the currUri isn't going to cause an exception to be thrown
                    if (!Util.IsValidUri(currUri))
                    {
                        continue;
                    }

                    RequestObject extractedReference = new RequestObject(_proxy, currUri);

                    if (!extractedReferences.Contains(extractedReference))
                    {
                        extractedReferences.AddLast(extractedReference);
                    }
                }
            }

            return extractedReferences;
        }

        // merges baseUri with currentUri to an absolute Uri
        string TranslateToAbsoluteAddress(string baseUri, string currUri)
        {
            // relative references with path e.g. "www.blah.com/xyz.html#thisarea"
            int index = currUri.IndexOf("#");
            if(index > 0)
            {
                currUri = currUri.Substring(index);
            }
            
            if (currUri.StartsWith("javascript"))
            {
                // ignore javascript references
                return baseUri;
            }
            else if (currUri.StartsWith("http://"))
            {
                // absolute uri
                return currUri;
            }
            else if (currUri.StartsWith("www."))
            {
                // absolute uri
                return "http://" + currUri;
            }
            else if (currUri.StartsWith("#"))
            {
                // local ref
                return baseUri;
            }
            else if (currUri.StartsWith("/"))
            {
                // weird case 1
                // trim off all but the domain from the baseUri

                // check for "http://" case
                string tempUri = "";
                if (baseUri.StartsWith("http://"))
                {
                    tempUri = "http://";
                    baseUri = baseUri.Substring(tempUri.Length);
                }

                int pos = baseUri.IndexOf("/");
                if (pos > 0)
                {
                    return tempUri + baseUri.Substring(0, pos) + currUri;
                }
                else
                {
                    // maybe the baseUri is something like www.blah.com with no trailing /
                    return tempUri + baseUri + currUri;
                }
            }
            else if (currUri.StartsWith("../"))
            {
                // case 2
                // trim off ../ off the currUri and blah/ off the baseUri

                // tokenize baseUri
                string[] tokens = baseUri.Split('/');
                if (tokens.Length <= 1)
                {
                    // error
                    return baseUri + currUri;
                }

                // count the number of "../"'s
                int count = 0;
                do
                {
                    count++;
                    currUri = currUri.Substring("../".Length);
                } while (currUri.StartsWith("../"));


                string httpShim = "";
                string newBaseUri = baseUri;
                if(baseUri.StartsWith("http://")) {
                    httpShim = "http://";
                    newBaseUri = newBaseUri.Substring(7);
                }

                // trim the current pageUri off
                int offset = newBaseUri.LastIndexOf('/');
                if (offset > 0)
                {
                    newBaseUri = newBaseUri.Substring(0, offset);
                }
                else
                {
                    // doesn't even have a / anywhere
                    // nothing to trim
                }

                // trim the baseUri by the count
                offset = newBaseUri.LastIndexOf('/');
                while(count > 0 && offset > 0) 
                {
                    newBaseUri = newBaseUri.Substring(0, offset);

                    // update the count
                    count--;

                    // get new offset
                    offset = newBaseUri.LastIndexOf('/');
                }

                if(count > 0) {
                    // degenerate case, nothing else to trim
                }

                return httpShim + newBaseUri + "/" + currUri;
            }
            else
            {
                // simple relative uri
                // trim current page
                string httpShim = "";
                string newBaseUri = baseUri;
                if (baseUri.StartsWith("http://"))
                {
                    httpShim = "http://";
                    newBaseUri = newBaseUri.Substring(7);
                }
                
                int offset = newBaseUri.LastIndexOf('/');

                /*
                // XXX: yet another nasty piece of code
                int offsetTwo = newBaseUri.LastIndexOf('.');
                if (offset > offsetTwo)
                {
                    newBaseUri = newBaseUri + "/";
                }

                offset = newBaseUri.LastIndexOf('/');
                 */
                if (offset > 0)
                {
                    newBaseUri = newBaseUri.Substring(0, offset);
                }
                else
                {
                    // XXX: unhandled, doesn't make sense
                    return baseUri + currUri;
                }
                return httpShim + newBaseUri + "/" + currUri;
            }
        }

        #endregion
    }
}
