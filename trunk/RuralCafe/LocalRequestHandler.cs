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
using System.Security;

using System.Web;
using Lucene.Net.Search;
using System.Collections.Specialized;
using BzReader;
using System.Xml;
using RuralCafe.Util;
using Newtonsoft.Json;
using RuralCafe.LinkSuggestion;
using RuralCafe.Database;

namespace RuralCafe
{
    /// <summary>
    /// Request handler for requests coming in to the local proxy.
    /// </summary>
    public class LocalRequestHandler : RequestHandler
    {
        /// <summary>
        /// Constructor for a local proxy's request handler.
        /// </summary>
        /// <param name="internalHandler">The internal handler to copy fields from.</pparam>
        public LocalRequestHandler(LocalInternalRequestHandler internalHandler)
            : base(internalHandler.Proxy, internalHandler.Context)
        {
            _requestTimeout = LOCAL_REQUEST_PACKAGE_DEFAULT_TIMEOUT;
            // Copy fields from internalHandler
            _outstandingRequests = internalHandler.OutstandingRequests;
            PackageFileName = internalHandler.PackageFileName;
        }

        /// <summary>
        /// Constructor for a local proxy's request handler.
        /// </summary>
        /// <param name="proxy">Proxy this request handler belongs to.</param>
        /// <param name="context">Client context.</param>
        public LocalRequestHandler(RCLocalProxy proxy, HttpListenerContext context)
            : base(proxy, context)
        {
            _requestTimeout = LOCAL_REQUEST_PACKAGE_DEFAULT_TIMEOUT;
        }

        /// <summary>
        /// Constructor used, when http context is not available any more. E.g. queue deserialization.
        /// </summary>
        /// <param name="proxy">Proxy this request handler belongs to.</param>
        public LocalRequestHandler(RCLocalProxy proxy)
            : base(proxy)
        {
            _requestTimeout = LOCAL_REQUEST_PACKAGE_DEFAULT_TIMEOUT;
        }

        /// <summary>
        /// Default constructor for JSON.
        /// </summary>
        public LocalRequestHandler() { }

        /// <summary>The proxy that this request belongs to.</summary>
        public RCLocalProxy Proxy
        {
            get { return (RCLocalProxy)_proxy; }
        }

        /// <summary>
        /// Main logic of RuralCafe LPRequestHandler.
        /// Called by Go() in the base RequestHandler class.
        /// </summary>
        public override void HandleRequest(object nullObj)
        {
            if (!CheckIfBlackListedOrInvalidUri())
            {
                DisconnectSocket();
                return;
            }

            try
            {
                // create the RCRequest object for this request handler
                CreateRequest(OriginalRequest);
                LogRequest();

                // Try to get content from the wiki, if available.
                string redir;
                string wikiContent = Proxy.WikiWrapper.GetWikiContentIfAvailable(RequestUri, out redir);
                if (wikiContent != null)
                {
                    // Log query metric
                    Proxy.Logger.QueryMetric(Proxy.SessionManager.GetUserId(ClientIP),
                        true, RefererUri, RequestUri);

                    if (!redir.Equals(""))
                    {
                        _clientHttpContext.Response.Redirect(redir);
                    }
                    // Set content type.
                    _clientHttpContext.Response.ContentType = "text/html";
                    // Include link suggestions if we're offline. XXX do we want that for wiki?
                    if (Proxy.NetworkStatus == RCLocalProxy.NetworkStatusCode.Offline)
                    {
                        wikiContent = LinkSuggestionHtmlModifier.IncludeTooltips(wikiContent);
                    }
                    SendMessage(wikiContent);

                    return;// Status.Completed;
                }

                // Check if this request is cacheable
                if (IsCacheable() && Proxy.ProxyCacheManager.IsCached(_rcRequest.GenericWebRequest))
                {
                    // Log query metric
                    Proxy.Logger.QueryMetric(Proxy.SessionManager.GetUserId(ClientIP),
                        true, RefererUri, RequestUri);

                    // Include link suggestions if we're not online for html pages
                    if (Proxy.NetworkStatus != RCLocalProxy.NetworkStatusCode.Online
                        && Proxy.ProxyCacheManager.IsHTMLFile(_rcRequest.GenericWebRequest.Method,
                        _rcRequest.Uri))
                    {
                        string content = Utils.ReadFileAsString(_rcRequest.CacheFileName);
                        if (String.IsNullOrEmpty(content))
                        {
                            return;// Status.Failed;
                        }
                        content = LinkSuggestionHtmlModifier.IncludeTooltips(content);

                        // Modify the webresponse
                        GlobalCacheItem gci = _proxy.ProxyCacheManager.GetGlobalCacheItem(_originalRequest.HttpMethod,
                            _originalRequest.RawUrl);
                        if (gci == null)
                        {
                            string message = "problem getting db info: " + _rcRequest.CacheFileName;
                            Logger.Warn(message);
                            SendErrorPage(HttpStatusCode.InternalServerError, message);
                            return;
                        }
                        ModifyWebResponse(gci);

                        SendMessage(content);
                        return;
                    }

                    _rcRequest.FileSize = StreamFromCacheToClient(_rcRequest.CacheFileName, true);
                    return;
                }

                // Log query metric for uncached items
                Proxy.Logger.QueryMetric(Proxy.SessionManager.GetUserId(ClientIP), false, RefererUri, RequestUri);

                // cacheable but not cached, cache it, then send to client if there is no remote proxy
                // if online, stream to cache, then stream to client.
                if (Proxy.NetworkStatus == RCLocalProxy.NetworkStatusCode.Online)
                {
                    // We're streaming through the remote proxy.
                    SetStreamToRemoteProxy();
                    // Try to start measuring the speed
                    bool measuring = NetworkUsageDetector.
                        StartMeasuringIfNotRunningWithCallback(Proxy.IncludeDownloadInCalculation);

                    Status result = SelectMethodAndStream();

                    // Only get the results if this thread was measuring.
                    if (measuring)
                    {
                        // Take speed into calculation, if successful
                        if (result == Status.Completed)
                        {
                            NetworkUsageDetector.MarkReadyForCallback();
                        }
                        else
                        {
                            // Abort. Speed will not be considered.
                            NetworkUsageDetector.AbortCallback();
                        }
                    }

                    return;
                }

                // if we're not online, let's check if the package file name is not too long
                if (!Utils.IsNotTooLongFileName(PackageFileName))
                {
                    Logger.Debug("package filename for " + RequestUri + " is too long. Aborting.");
                    SendErrorPage(HttpStatusCode.InternalServerError, "package filename for " + RequestUri + " is too long.");

                    return;// Status.Failed;
                }

                if (Proxy.NetworkStatus != RCLocalProxy.NetworkStatusCode.Online)
                {
                    // Uncached links should be redirected to
                    // /trotro-user.html?t=title&a=id (GET/HEAD) or (because they should have been prefetched)
                    // /request/add?t=title&a=id (POST/...) (because prefetching POSTs is impossible) (XXX: Not necessary!?)
                    // when the system mode is slow or offline
                    // Parse parameters to get title
                    NameValueCollection qscoll = HttpUtility.ParseQueryString(_originalRequest.Url.Query);
                    string title = qscoll.Get("trotro");
                    if (title == null)
                    {
                        title = RequestUri;
                        int pos = title.LastIndexOf("/");
                        while (pos > 20)
                        {
                            title = title.Substring(0, pos);
                            pos = title.LastIndexOf("/");
                        }
                    }

                    // Save the request in the "without user" queue
                    string id = "" + Proxy.AddRequestWithoutUser(this);

                    string redirectUrl = "http://www.ruralcafe.net/" +
                        "trotro-user.html"
                        + "?t=" + title + "&a=" + id;
                    _clientHttpContext.Response.Redirect(redirectUrl);
                }
            }
            catch (Exception e)
            {
                RequestStatus = RequestHandler.Status.Failed;
                String errmsg = "error handling request: ";
                if (_originalRequest != null)
                {
                    errmsg += " " + _originalRequest.RawUrl;
                }
                Logger.Warn(errmsg, e);
                SendErrorPage(HttpStatusCode.InternalServerError, errmsg);
            }
            finally
            {
                DisconnectSocket();
                LogResponse();
            }
        }
        
        /// <summary>
        /// Dispatch Threads.
        /// </summary>
        public override void DispatchRequest(object nullObj)
        {
            // set proxy and timeouts
            if (_proxy.GatewayProxy == null)
            {
                // connect directly to remote proxy
                _rcRequest.SetProxyAndTimeout(Proxy.RemoteProxy, System.Threading.Timeout.Infinite);
            }
            else
            {
                // connect through gateway proxy
                _rcRequest.SetProxyAndTimeout(Proxy.GatewayProxy, System.Threading.Timeout.Infinite);
            }

            // wait for admission control
            while (_proxy.NumInflightRequests >= _proxy.MaxInflightRequests)
            {
                Thread.Sleep(100);
            }

            // Try to start measuring the speed
            bool measuring = NetworkUsageDetector.StartMeasuringIfNotRunning();

            // add to active set of connections
            _proxy.AddActiveRequest(this);

            // download the request file as a package
            RequestStatus = RequestHandler.Status.Downloading;
            bool downloadSuccessful = RCRequest.DownloadPackage();
            
            // remove from active set of connections
            _proxy.RemoveActiveRequest(this);

            // Only get the results if this thread was measuring.
            if (measuring)
            {
                NetworkUsageDetector.NetworkUsageResults results = NetworkUsageDetector.GetMeasuringResults();
                if (downloadSuccessful)
                {
                    // If request successful, we save the results
                    Proxy.IncludeDownloadInCalculation(results);
                }
            }

            // check results and unpack
            if (downloadSuccessful)
            {
                RCSpecificResponseHeaders headers = GetRCSpecificResponseHeaders();

                long unpackedBytes = Package.Unpack(this, headers);
                if (unpackedBytes > 0)
                {
                    Logger.Debug("unpacked: " + RequestUri);
                    RCRequest.FileSize = unpackedBytes;
                    RequestStatus = RequestHandler.Status.Completed;
                }
                else
                {
                    Logger.Warn("failed to unpack: " + RequestUri);
                    RequestStatus = RequestHandler.Status.Failed;
                }
            }
            else
            {
                RequestStatus = RequestHandler.Status.Failed;
            }

            // save finish time
            FinishTime = DateTime.Now;
            ((RCLocalProxy)_proxy).UpdateTimePerRequest(StartTime, FinishTime);

            LogResponse();

            // thread dies upon return
        }

        /// <summary>
        /// Configures the request handler to stream the request trough the remote proxy.
        /// </summary>
        /// <returns>The length of the streamed result.</returns>
        private void SetStreamToRemoteProxy()
        {
            // Set Remote Proxy as Proxy
            RCRequest.SetProxyAndTimeout(Proxy.RemoteProxy, System.Threading.Timeout.Infinite);
            // Set flag to indicate we're streaming
            AddRCSpecificRequestHeaders(new RCSpecificRequestHeaders(true));
        }

        /// <summary>
        /// Helper method to get the ETA from the proxy this handler belongs to.
        /// </summary>
        /// <returns>ETA as a printable string.</returns>
        public string PrintableETA()
        {
            if ((this.RequestStatus == Status.Completed) ||
                (this.RequestStatus == Status.Failed))
            {
                return "0";
            }

            string etaString;
            int eta = Proxy.ETA(this);
            if (eta < 60)
            {
                // This includes negative ETA (by avg. the request should be ready, but it isn't)
                etaString = "< 1 min";
            }
            else
            {
                // now eta is in minutes
                eta = eta / 60;
                if (eta < 60)
                {
                    etaString = eta.ToString() + " min";
                }
                else
                {
                    // now eta is in hours
                    eta = eta / 60;
                    if (eta < 24)
                    {
                        etaString = eta.ToString() + " hours";
                    }
                    else
                    {
                        etaString = "> 1 day";
                    }
                }
            }
            return etaString;
        }
    }
}
