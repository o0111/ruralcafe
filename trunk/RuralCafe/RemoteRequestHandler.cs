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
using HtmlAgilityPack;
using Util;

namespace RuralCafe
{
    /// <summary>
    /// Handler for the remote proxy, handles requests from a local proxy.
    /// XXX: multi-threaded for multiple clients would be good.
    /// </summary>
    public class RemoteRequestHandler : RequestHandler
    {
        /// <summary>
        /// The Default low watermark.
        /// </summary>
        public static int DEFAULT_LOW_WATERMARK;

        //private static int _nextId = 1;
        private long _quota;

        // override GenericProxy
        //new private RCRemoteProxy _proxy;

        // ruralcafe specific stuff
        private Package _package;

        // abort
        private bool _killYourself;

        /*
        // benchmarking variables
        protected DateTime handleRequestStart;
        protected DateTime handleRequestEnd;
        protected DateTime downloadPagesStart;
        protected DateTime downloadPagesEnd;
        protected DateTime transmitStart;
        protected DateTime transmitEnd;
        */

        //public static List<int> linksOnResultsPage = new List<int>();
        //public static List<int> imagesOnResultsPage = new List<int>();
        //public static List<int> imagesOnTargetPage = new List<int>();

        /// <summary>
        /// Constructor for a remote proxy's request handler.
        /// </summary>
        /// <param name="proxy">Proxy this request handler belongs to.</param>
        /// <param name="context">Client context.</param>
        public RemoteRequestHandler(RCRemoteProxy proxy, HttpListenerContext context)
            : base(proxy, context, REMOTE_REQUEST_PACKAGE_DEFAULT_TIMEOUT)
        {
            _quota = Properties.Settings.Default.DEFAULT_QUOTA;
            _package = new Package();
            _killYourself = false;
        }

        /// <summary>The proxy that this request belongs to.</summary>
        public RCRemoteProxy Proxy
        {
            get { return (RCRemoteProxy)_proxy; }
        }

        /// <summary>Big red button to kill the thread.</summary>
        public void KillYourself()
        {
            _killYourself = true;
        }

        /// <summary>
        /// Main logic of RuralCafe RPRequestHandler.
        /// Called by Go() in the base RequestHandler class.
        /// </summary>
        public override void HandleRequest(object nullObj)
        {
            if (!CheckIfBlackListedOrInvalidUri())
            {
                SendErrorPage(HttpStatusCode.InternalServerError, "Blacklisted or invalid URL.");
                DisconnectSocket();
                return;
            }

            // ugly variable
            bool shouldDisconnect = false;
            try
            {
                // create the RCRequest object for this request handler
                CreateRequest(OriginalRequest);
                LogRequest();

                // check for streaming
                RCSpecificRequestHeaders rcHeaders = GetRCSpecificRequestHeaders();
                if (rcHeaders.IsStreamingTransparently)
                {
                    // stream the request
                    shouldDisconnect = true;
                    SelectMethodAndStream();
                }
                else
                {
                    // queue the request
                    ((RCRemoteProxy)_proxy).AddRequest(this);
                }
            }
            catch (Exception e)
            {
                RequestStatus = RequestHandler.Status.Failed;
                String errmsg = "error handling request: ";
                if (_originalRequest != null)
                {
                    errmsg += " " + _originalRequest.RawUrl.ToString();
                }
                errmsg += " " + e.Message;
                SendErrorPage(HttpStatusCode.InternalServerError, errmsg);
            }
            finally
            {
                if (shouldDisconnect)
                {
                    DisconnectSocket();
                }
                LogResponse();
            }
            // do NOT close the socket for queued items till dispatcher is done.
        }

        /// <summary>
        /// Dispatch Threads.
        /// </summary>
        public override void DispatchRequest(object nullObj)
        {
            // set proxy and timeouts
            if (_proxy.GatewayProxy != null)
            {
                _rcRequest.SetProxyAndTimeout(Proxy.GatewayProxy, _requestTimeout);
            }
            
            // check user richness setting
            RequestHandler.Richness richness = Proxy.GetProperties(Context.Request.RemoteEndPoint,
                GetRCSpecificRequestHeaders().RCUserID).richness;
            if (richness == 0)
            {
                // Use default when nothing is set
                richness = Properties.Settings.Default.DEFAULT_RICHNESS;
            }

            // wait for admission control
            _proxy.WaitForAdmissionControlAndAddActiveRequest(RequestId);
            // Tell the network usage detector we're downloading now
            _proxy.NetworkUsageDetector.DownloadStarted();
            
            // download the package and return it to local proxy
            Logger.Debug("dispatching to content servers: " + RequestUri);
            bool success = RecursivelyDownloadPage(RCRequest, richness, 0);

            // remove from active set of connections
            _proxy.RemoveActiveRequest();
            // Tell the network usage detector we're done downloading
            _proxy.NetworkUsageDetector.DownloadStopped();

            if (success)
            {
                try
                {
                    RCRequest.FileSize = SendResponsePackage();
                }
                catch (Exception e)
                {
                    RCRequest.FileSize = -1;
                    SendErrorPage(HttpStatusCode.InternalServerError, e.Message);
                }
            }

            DisconnectSocket();
            // thread dies upon return
        }

        #region benchmarking (unused)

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

        /*
        // benchmarking stuff
        public void PrefetchAnalysis(string richness, int depth)
        {
            LogDebug("Running Benchmarker");

            // XXX: should add a parameter to always download or just read from cache
            // convert to Uri format
            //string pageUri = _webRequestUri;
            LogRequest();

            long bytesDownloaded = _rcRequest.DownloadToCache();

            FileInfo f;
            try
            {
                f = new FileInfo(_rcRequest.CacheFileName);
                if (bytesDownloaded < 0 || !f.Exists)
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
            LinkedList<RCRequest> objectsFound = DownloadEmbeddedObjects(_rcRequest, richness);
            // benchmarking: store the number of images found
            //imagesOnResultsPage.Add(objectsFound.Count);

            // recursively download pages
            LinkedList<RCRequest> resultLinkUris = ExtractGoogleResults(_rcRequest);
            // benchmarking: store the number of links found
            //linksOnResultsPage.Add(resultLinkUris.Count);
            foreach (RCRequest linkObject in resultLinkUris)
            {
                bytesDownloaded = linkObject.DownloadToCache();
                if (bytesDownloaded > -1 && f.Exists)
                {
                    linkObject.RequestStatus = (int)Status.Completed;
                }
                try
                {
                    f = new FileInfo(linkObject.CacheFileName);
                }
                catch (Exception)
                {
                    linkObject.RequestStatus = (int)Status.Failed;
                    continue;
                }
                if (linkObject.RequestStatus == (int)Status.Failed || !f.Exists)
                {
                    linkObject.RequestStatus = (int)Status.Failed;
                    continue;
                }

                // XXX: hackery
                // make a copy of this file
                try
                {
                    // create directory if it doesn't exist
                    if (!Util.CreateDirectoryForFile(linkObject.CacheFileName))
                    {
                        return;
                    }
                    // create directory if it doesn't exist
                    if (!Util.CreateDirectoryForFile("ZZZZZZ\\" + linkObject.CacheFileName))
                    {
                        return;
                    }

                    File.Delete("ZZZZZZ\\" + linkObject.CacheFileName);
                    File.Copy(linkObject.CacheFileName, "ZZZZZZ\\" + linkObject.CacheFileName);

                    // skip parseable check
                    if (!Util.IsParseable(linkObject))
                    {
                        continue;
                    }

                    // get the embedded content of the search result page
                    objectsFound = DownloadEmbeddedObjects(linkObject, richness);
                    // benchmarking: store the number of images on the page
                    //imagesOnTargetPage.Add(objectsFound.Count);

                    File.Delete(linkObject.CacheFileName);
                }
                catch (Exception e)
                {
                    LogDebug("problem downloading a file or something " + e.StackTrace + " " + e.Message);
                }
            }
        }*/

        #endregion
        #region Search Algorithms

        /// <summary>
        /// Recursively downloads a page and its embedded objects, and its outlinks.
        /// </summary>
        /// <param name="rcRequest">Requested page to start from.</param>
        /// <param name="richness">Richness setting.</param>
        /// <param name="depth">Depth to download.</param>
        /// <returns>Wheter something was downloaded successfully.</returns>
        public bool RecursivelyDownloadPage(RCRequest rcRequest, Richness richness, int depth)
        {
            if (_killYourself || _quota < DEFAULT_LOW_WATERMARK)
            {
                // Send error page if we're on top level
                if (depth == 0)
                {
                    SendErrorPage(HttpStatusCode.InternalServerError, "Request aborted or it does not fit in quota.");
                }
                return false;
            }

            // reduce the timer
            DateTime currTime = DateTime.Now;
            DateTime endTime = StartTime.AddMilliseconds(RequestHandler.WEB_REQUEST_DEFAULT_TIMEOUT);
            if (endTime.CompareTo(currTime) > 0)
            {
                RCRequest.GenericWebRequest.Timeout = (int)(endTime.Subtract(currTime)).TotalMilliseconds;
            }
            else
            {
                RCRequest.GenericWebRequest.Timeout = 0;
            }

            // Only download for POST/... or not already existing items
            if (!IsGetOrHeadHeader() || !_proxy.ProxyCacheManager.IsCached(rcRequest.RelCacheFileName))
            {
                // Download!
                try
                {
                    // There is no index on the remote side anyway
                    rcRequest.DownloadToCache(false);
                }
                catch (Exception e)
                {
                    Logger.Warn("[depth = " + depth + "] error downloading: " + rcRequest.Uri + " " + e.Message);
                    // Send error page if we're on top level
                    if (depth == 0)
                    {
                        if (e is WebException)
                        {
                            WebException exp = e as WebException;
                            HttpWebResponse response = (e as WebException).Response as HttpWebResponse;
                            SendErrorPage(response != null ? response.StatusCode : HttpStatusCode.InternalServerError, e.Message);
                        }
                        else
                        {
                            SendErrorPage(HttpStatusCode.InternalServerError, e.Message);
                        }
                    }
                    return false;
                }
            }
            else
            {
                Logger.Debug("Already existed: " + rcRequest.Uri);
            }

            // add to the package
            if (_package.Pack(this, rcRequest, ref _quota))
            {
                Logger.Debug("[depth = " + depth + "] packed: " + rcRequest.Uri + " " + rcRequest.FileSize + " bytes, " + _quota + " left");
            }

            // add a new request for the old location if it was redirected. This will then
            // get the 301 file from the cache, so the local proxy does not need to send 
            // another request to the remote proxy to find that out.
            if (rcRequest.UriBeforeRedirect != null)
            {
                Logger.Debug("Redirected: Also packing old URI with a 301 file.");
                RCRequest rc301 = new RCRequest(_proxy, (HttpWebRequest)WebRequest.Create(rcRequest.UriBeforeRedirect));
                _package.Pack(this, rc301, ref _quota);
            }

            if(!_proxy.ProxyCacheManager.IsHTMLFile(rcRequest.RelCacheFileName))
            {
                return true;
            }
            // Getting embedded objects and recursing only makes sense for html pages.
            Uri baseUri = new Uri(rcRequest.Uri);
            string htmlContent = Utils.ReadFileAsString(rcRequest.CacheFileName).ToLower();

            // get the embedded content of the search result page
            DownloadEmbeddedObjects(rcRequest, baseUri, htmlContent, richness);

            // Don't recurse if we're on the deepest layer allowed
            if (depth == Properties.Settings.Default.DEFAULT_DEPTH - 1)
            {
                return true;
            }

            // recurse
            LinkedList<Uri> resultLinkUris = HtmlUtils.ExtractLinks(baseUri, htmlContent);
            foreach (Uri uri in resultLinkUris)
            {
                RCRequest currRequest = new RCRequest(_proxy, (HttpWebRequest)WebRequest.Create(uri));
                RecursivelyDownloadPage(currRequest, richness, depth + 1);
            }
            return true;
        }

        /// <summary>
        /// Downloads embedded objects based on the richness.
        /// </summary>
        /// <param name="rcRequest">Request page to start from.</param>
        /// <param name="richness">Richness setting.</param>
        /// <param name="baseUri">The Uri of the website where to download embedded objects.</param>
        /// <param name="htmlContent">The HTML content of the webiste.</param>
        /// <returns>List of RCRequests of embedded objects downloaded</returns>
        private LinkedList<RCRequest> DownloadEmbeddedObjects(RCRequest rcRequest, Uri baseUri, string htmlContent, Richness richness)
        {
            LinkedList<Uri> filteredEmbeddedObjects = new LinkedList<Uri>();

            if (_killYourself || _quota < DEFAULT_LOW_WATERMARK)
            {
                return new LinkedList<RCRequest>();
            }

            LinkedList<Uri> embeddedObjects = HtmlUtils.ExtractEmbeddedObjects(baseUri, htmlContent);

            // XXX: refactor into filter class/method.
            // filter out based on richness
            foreach (Uri uri in embeddedObjects)
            {
                string uriS = uri.ToString();
                // ignore blacklisted domains
                if (IsBlacklisted(uriS))
                {
                    continue;
                }

                if (richness == Richness.Normal || (richness == Richness.Low && IsATextPage(uriS)))
                {
                    filteredEmbeddedObjects.AddLast(uri);
                }
            }
            embeddedObjects = filteredEmbeddedObjects;

            return DownloadObjectsInParallel(rcRequest, embeddedObjects);
        }

        /// <summary>
        /// Downloads a set of URIs in parallel using a ThreadPool.
        /// </summary>
        /// <param name="parentRequest">Root request.</param>
        /// <param name="childObjects">Children URIs to be downloaded.</param>
        /// <returns>List of downloaded requests.</returns>
        private LinkedList<RCRequest> DownloadObjectsInParallel(RCRequest parentRequest, LinkedList<Uri> childObjects)
        {
            //ThreadPool.SetMaxThreads(4, 4);
            LinkedList<RCRequest> addedObjects = new LinkedList<RCRequest>();

            if (_killYourself || childObjects.Count == 0)
            {
                return addedObjects;
            }
            
            parentRequest.ResetEvents = new ManualResetEvent[childObjects.Count];

            try
            {
                // queue up worker threads to download URIs
                for (int i = 0; i < childObjects.Count; i++)
                {
                    // create the RCRequest for the object
                    RCRequest currChildObject = new RCRequest(_proxy, (HttpWebRequest)WebRequest.Create(childObjects.ElementAt(i)));
                    currChildObject.ChildNumber = i;
                    // Set the root request.
                    currChildObject.RootRequest = parentRequest;
                    addedObjects.AddLast(currChildObject);

                    // set the resetEvent
                    currChildObject.ResetEvents = parentRequest.ResetEvents;
                    parentRequest.ResetEvents[i] = new ManualResetEvent(false);

                    // make sure we haven't downloaded this before
                    if (_package.RCRequests.Contains(currChildObject))
                    {
                        // skip it
                        currChildObject.SetDone();
                        continue;
                    }                    
 
                    // download the page
                    //LogDebug("queueing: " + currChild.ChildNumber + " " + currChild.Uri);
                    ThreadPool.QueueUserWorkItem(new WaitCallback(DownloadPageWorkerThread), (object)currChildObject);
                }

                // wait for timeout
                Utils.WaitAll(parentRequest.ResetEvents);

                addedObjects = _package.Pack(this, addedObjects, ref _quota);
            }
            catch (Exception e)
            {
                Logger.Warn("unable to download embeddedObjects.", e);
            }

            return addedObjects;
        }

        /// <summary>
        /// Worker thread to download a webpage given the RequestWrapper's parameters.
        /// </summary>
        /// <param name="requestObj">The requested URI.</param>
        private void DownloadPageWorkerThread(object requestObj)
        {
            // cast the RCRequest
            RCRequest request = (RCRequest)requestObj;

            if (_killYourself)
            {
                return;
            }

            // make sure this root request is not timed out
            if (!IsTimedOut())
            {                
                // reduce the timer
                DateTime currTime = DateTime.Now;
                DateTime endTime = StartTime.AddMilliseconds(_requestTimeout);
                if (endTime.CompareTo(currTime) > 0)
                {
                    request.GenericWebRequest.Timeout = (int)(endTime.Subtract(currTime)).TotalMilliseconds;

                    // download the page, if it does not exist already
                    if (!_proxy.ProxyCacheManager.IsCached(request.RelCacheFileName))
                    {
                        // Download!
                        try
                        {
                            // add to active set of connections
                            _proxy.AddActiveRequest();
                            // There is no index on the remote side anyway
                            request.DownloadToCache(false);
                        }
                        catch { } // Ignore
                        finally
                        {
                            // remove from active set of connections
                            _proxy.RemoveActiveRequest();
                        }
                    }
                }
                else
                {
                    request.GenericWebRequest.Timeout = 0;
                }
            }

            // mark this thread as done
            request.SetDone();
        }

        #endregion
        #region Request and Response Methods

        /// <summary>
        /// Sends the request result package to the client
        /// </summary>
        /// <returns>The size of the package sent.</returns>
        public long SendResponsePackage()
        {
            // build the package index
            _package.BuildPackageIndex(PackageFileName, Proxy.ProxyCacheManager);

            Logger.Debug("sending results package: " + (_package.IndexSize + _package.ContentSize) +
                " bytes at " + _proxy.MAXIMUM_DOWNLINK_BANDWIDTH + " bytes per second.");

            // Add response headers
            RCSpecificResponseHeaders headers = new RCSpecificResponseHeaders(_package.IndexSize, _package.ContentSize);
            AddRCSpecificResponseHeaders(headers);

            // stream out the pages (w/compression)
            LinkedList<string> fileNames = new LinkedList<string>();
            fileNames.AddLast(PackageFileName);
            foreach (RCRequest rcRequest in _package.RCRequests)
            {
                fileNames.AddLast(rcRequest.CacheFileName);
            }
            MemoryStream ms = GZipWrapper.GZipCompress(fileNames);
            return StreamToClient(ms);
        }

        #endregion
        #region Remote Proxy Specific Helper Functions

        /// <summary>
        /// Determines if the URI is pointing to a text page.
        /// Used for downloading embedded objects.
        /// </summary>
        /// <param name="pageUri">URI.</param>
        /// <returns>True or false for is or is not.</returns>
        bool IsATextPage(string pageUri)
        {
            Logger.Debug("IsATextPage?: " + pageUri);
            // first check if file has an extension which we know
            // If so, we look into our map, and do not have to send any request
            string fileExtension = Utils.GetFileExtension(pageUri);
            string contentTypeA = Utils.GetContentType(fileExtension);
            Logger.Debug("IsATextPage? - File Extension Mapping: " + fileExtension + "->" + contentTypeA);
            if (!contentTypeA.Equals("content/unknown"))
            {
                return (!contentTypeA.Contains("image") && !contentTypeA.Contains("audio")
                    && !contentTypeA.Contains("video"));
            }

            WebRequest request = WebRequest.Create(pageUri);
            // Only send a HEAD request, if no file extension
            request.Method = "HEAD";
            request.Timeout = HEAD_REQUEST_DEFAULT_TIMEOUT;
            WebResponse response;
            try
            {
                response = request.GetResponse();
            }
            catch (WebException e)
            {
                response = e.Response;
            }

            string contentType = "";
            if (response != null)
            {
                contentType = response.ContentType;
                Logger.Debug("IsATextPage? - HEAD request response: " + contentType);
                return (!contentType.Contains("image") && !contentType.Contains("audio")
                    && !contentType.Contains("video"));
            }
            // any other error
            return false;
        }

        
        #endregion
    }
}
