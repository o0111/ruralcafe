﻿/*
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
using RuralCafe.Util;

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
            : base(proxy, context)
        {
            _requestTimeout = REMOTE_REQUEST_PACKAGE_DEFAULT_TIMEOUT;

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
                Logger.Warn(errmsg, e);
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
            RequestHandler.Richness richness = Proxy.GetUserSettings(Context.Request.RemoteEndPoint,
                GetRCSpecificRequestHeaders().RCUserID).richness;
            if (richness == 0)
            {
                // Use default when nothing is set
                richness = Properties.Settings.Default.DEFAULT_RICHNESS;
            }

            // download the package and return it to local proxy
            Logger.Debug("dispatching to content servers: " + RequestUri);
            if (RecursivelyDownloadPage(RCRequest, richness, 0))
            {
                RCRequest.FileSize = SendResponsePackage();
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
        /// <returns></returns>
        public bool RecursivelyDownloadPage(RCRequest rcRequest, Richness richness, int depth)
        {
            if (_killYourself ||_quota < DEFAULT_LOW_WATERMARK)
            {
                return false;
            }

            if (depth == Properties.Settings.Default.DEFAULT_DEPTH)
            {
                return false;
            }

            /*
            // Check for admission control
            // XXX: its not clear whether this should be done at the remote proxy at all.
            // XXX: we're architecturally basically assuming that we're limited by threads and the remote proxy
            // XXX: if we do decide to do this, we'll have to decide do it either per incoming active request or actually count all spawned asset/recurisve requests
            while (_proxy.NumInflightRequests >= _proxy.MaxInflightRequests)
            {
                Thread.Sleep(100);
            }
            // add
            _proxy.AddActiveRequest(this);
             // remove
            _proxy.RemoveActiveRequest(this);
            */

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

            // download the page
            // replace for non GET/HEADs
            bool replace = !IsGetOrHeadHeader();

            bool success;
            if (IsGetOrHeadHeader())
            {
                // Download!
                success = rcRequest.DownloadToCache();
            }
            else
            {
                // We must lock, because there is a small chance another thread also works on
                // a POST for the same URI.
                lock (_proxy.ProxyCacheManager.ExternalLockObj)
                {
                    // Remove old things
                    _proxy.ProxyCacheManager.RemoveCacheItem(_rcRequest.GenericWebRequest.Method,
                        _rcRequest.GenericWebRequest.RequestUri.ToString());

                    // Download!
                    success = rcRequest.DownloadToCache();
                }
            }
            
            if (!success)
            {
                Logger.Warn("[depth = " + depth + "] error downloading: " + rcRequest.Uri);
                return false;
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
                RCRequest rc301 = new RCRequest(this, (HttpWebRequest)WebRequest.Create(rcRequest.UriBeforeRedirect));
                _package.Pack(this, rc301, ref _quota);
            }

            // get the embedded content of the search result page
            DownloadEmbeddedObjects(rcRequest, richness);

            // recurse if necessary
            LinkedList<Uri> resultLinkUris = ExtractLinks(rcRequest);
            foreach (Uri currUri in resultLinkUris)
            {
                RCRequest currRequest = new RCRequest(this, (HttpWebRequest)WebRequest.Create(currUri));

                RecursivelyDownloadPage(currRequest, richness, depth + 1);
            }
            return true;
        }

        /// <summary>
        /// Downloads embedded objects based on the richness.
        /// </summary>
        /// <param name="rcRequest">Request page to start from.</param>
        /// <param name="richness">Richness setting.</param>
        /// <returns>List of RCRequests of embedded objects downloaded</returns>
        private LinkedList<RCRequest> DownloadEmbeddedObjects(RCRequest rcRequest, Richness richness)
        {
            LinkedList<Uri> filteredEmbeddedObjects = new LinkedList<Uri>();

            if (_killYourself || _quota < DEFAULT_LOW_WATERMARK)
            {
                return new LinkedList<RCRequest>();
            }
            
            LinkedList<Uri> embeddedObjects = ExtractEmbeddedObjects(rcRequest);

            // XXX: refactor into filter class/method.
            // filter out based on richness
            foreach (Uri currUri in embeddedObjects)
            {
                // ignore blacklisted domains
                if (IsBlacklisted(currUri.ToString()))
                {
                    continue;
                }
                
                if (richness == Richness.Normal || (richness == Richness.Low && IsATextPage(currUri.ToString())))
                {
                    filteredEmbeddedObjects.AddLast(currUri);
                }
            }
            embeddedObjects = filteredEmbeddedObjects;

            return DownloadObjectsInParallel(rcRequest, embeddedObjects);
        }

        /// <summary>
        /// Downloads a set of URIs in series.
        /// 
        /// DEPRECATED
        /// </summary>
        /// <param name="parentRequest">Root request.</param>
        /// <param name="children">Children requests to be downloaded.</param>
        /// <returns>List of downloaded requests.</returns>
        private LinkedList<RCRequest> DownloadObjects(RCRequest parentRequest, LinkedList<RCRequest> children)
        {
            LinkedList<RCRequest> addedObjects = new LinkedList<RCRequest>();

            if (children.Count == 0)
            {
                return addedObjects;
            }

            parentRequest.ResetEvents = new ManualResetEvent[children.Count];

            try
            {
                // queue up worker threads to download URIs
                for (int i = 0; i < children.Count; i++)
                {
                    RCRequest currChild = children.ElementAt(i);
                    // make sure we haven't downloaded this before
                    if (_package.RCRequests.Contains(currChild))
                    {
                        // skip it
                        parentRequest.SetDone();
                        continue;
                    }

                    // reduce the timer
                    DateTime currTime = DateTime.Now;
                    DateTime endTime = parentRequest.StartTime.AddMilliseconds(RequestHandler.WEB_REQUEST_DEFAULT_TIMEOUT);
                    if (endTime.CompareTo(currTime) > 0)
                    {
                        currChild.GenericWebRequest.Timeout = (int)(endTime.Subtract(currTime)).TotalMilliseconds;
                    }
                    else
                    {
                        currChild.GenericWebRequest.Timeout = 0;
                    } 
                     
                    // download the page
                    currChild.DownloadToCache();

                    if (IsTimedOut())
                    {
                        break;
                    }
                }

                addedObjects = _package.Pack(this, children, ref _quota);
            }
            catch (Exception e)
            {
                Logger.Warn("unable to download embeddedObjects.", e);
            }

            return addedObjects;
        }

        /// <summary>
        /// Downloads a set of URIs in parallel using a ThreadPool.
        /// </summary>
        /// <param name="parentRequest">Root request.</param>
        /// <param name="children">Children requests to be downloaded.</param>
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
                    RCRequest currChildObject = new RCRequest(this, (HttpWebRequest)WebRequest.Create(childObjects.ElementAt(i)));
                    currChildObject.ChildNumber = i;
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
                WaitAll(parentRequest.ResetEvents);

                addedObjects = _package.Pack(this, addedObjects, ref _quota);
            }
            catch (Exception e)
            {
                Logger.Warn("unable to download embeddedObjects.", e);
            }

            return addedObjects;
        }

        /// <summary>
        /// Thread synchronization wait method.
        /// </summary>
        /// <remarks>Call this from parent thread after spawning children.</remarks>
        /// <param name="waitHandles">Thread handles.</param>
        private void WaitAll(WaitHandle[] waitHandles)
        {
            foreach (WaitHandle myWaitHandle in waitHandles)
            {
                WaitHandle.WaitAny(new WaitHandle[] { myWaitHandle });
            }
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
            if (!request.RootRequest.IsTimedOut())
            {                
                // reduce the timer
                DateTime currTime = DateTime.Now;
                DateTime endTime = request.RootRequest.StartTime.AddMilliseconds(RequestHandler.WEB_REQUEST_DEFAULT_TIMEOUT);
                if (endTime.CompareTo(currTime) > 0)
                {
                    request.GenericWebRequest.Timeout = (int)(endTime.Subtract(currTime)).TotalMilliseconds;

                    // download the page
                    request.DownloadToCache();
                }
                else
                {
                    request.GenericWebRequest.Timeout = 0;
                }
            }

            // mark this thread as done
            //LogDebug("Child Number: " + request.ChildNumber + " done.");
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
            if (!BuildPackageIndex())
            {
                return -1;
            }

            Logger.Debug("sending results package: " + (_package.IndexSize + _package.ContentSize) + " bytes at " + _proxy.MAXIMUM_DOWNLINK_BANDWIDTH + " bytes per second.");

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

        /// <summary>
        /// Combines all of the URIs in the package into a package index file.
        /// </summary>
        /// <returns>True or false for success or failure.</returns>
        bool BuildPackageIndex()
        {
            _package.ContentSize = 0;
            try
            {
                if (!Utils.CreateDirectoryForFile(PackageFileName))
                {
                    return false;
                }
                if (!Utils.DeleteFile(PackageFileName))
                {
                    return false;
                }

                TextWriter tw = new StreamWriter(PackageFileName);

                // create the package index file
                foreach (RCRequest rcRequest in _package.RCRequests)
                {
                    tw.WriteLine(rcRequest.Uri + " " + rcRequest.FileSize.ToString());
                    _package.ContentSize += rcRequest.FileSize;
                }

                tw.Close();

                // calculate the index size
                _package.IndexSize = Utils.GetFileSize(PackageFileName);
                if (_package.IndexSize < 0)
                {
                    Logger.Warn("problem getting file info: " + PackageFileName);
                }
            }
            catch (Exception e)
            {
                Logger.Error("problem creating package file: " + PackageFileName, e);
                return false;
            }

            return true;
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
                Logger.Debug("IsATextPage: " + e.Message);
                // probably 404
                return false;
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

        /// <summary>
        /// Extracts the embedded objects on a page.
        /// Wrapper for ExtractReferences()
        /// XXX: not completely implemented, need non HTML/"src=" references.
        /// </summary>
        LinkedList<Uri> ExtractEmbeddedObjects(RCRequest rcRequest)
        {
            return ExtractReferences(rcRequest, HtmlUtils.EmbeddedObjectTagAttributes);
        }

        /// <summary>
        /// Extracts the links on a page.
        /// Wrapper for ExtractReferences()
        /// XXX: not completely implemented, need non HTML/"a href=" references.
        /// </summary>
        LinkedList<Uri> ExtractLinks(RCRequest rcRequest)
        {
            return ExtractReferences(rcRequest, HtmlUtils.LinkTagAttributes);
        }

        /// <summary>
        /// Extracts the html references using a separator token and returns them.
        /// </summary>
        /// <param name="rcRequest">Page to parse.</param>
        /// <param name="tagAttributes">Seperator tokens.</param>
        /// <returns>List of references.</returns>
        LinkedList<Uri> ExtractReferences(RCRequest rcRequest, string[,] tagAttributes)
        {
            LinkedList<Uri> extractedReferences = new LinkedList<Uri>();

            string fileString = Utils.ReadFileAsString(rcRequest.CacheFileName).ToLower();
            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(fileString);
            Uri baseUri = new Uri(rcRequest.Uri);

            for (int i = 0; i < tagAttributes.GetLength(0); i++)
            {
                string tag = tagAttributes[i, 0];
                string attribute = tagAttributes[i, 1];

                HtmlNodeCollection results = doc.DocumentNode.SelectNodes("//" + tag + "[@" + attribute + "]");
                if (results == null)
                {
                    continue;
                }
                foreach(HtmlNode link in results)
                {
                    HtmlAttribute att = link.Attributes[attribute];
                    // Get the absolute URI
                    string currUriStr;
                    try
                    {
                        currUriStr = new Uri(baseUri, att.Value).AbsoluteUri;
                    }
                    catch(UriFormatException)
                    {
                        continue;
                    }

                    if (!HttpUtils.IsValidUri(currUriStr))
                    {
                        continue;
                    }

                    try
                    {
                        Uri currUri = new Uri(currUriStr.Trim());

                        if (!extractedReferences.Contains(currUri))
                        {
                            extractedReferences.AddLast(currUri);
                        }
                    }
                    catch (Exception)
                    {
                        // pass
                    }
                }
            }
            return extractedReferences;
        }
        #endregion
    }
}
