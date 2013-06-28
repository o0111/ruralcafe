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
        /// <pparam name="internalHandler">The internal handler to copy fields from.</pparam>
        public LocalRequestHandler(LocalInternalRequestHandler internalHandler)
            : base(internalHandler.Proxy, internalHandler.Context)
        {
            _requestId = _proxy.GetAndIncrementNextRequestID();
            _requestTimeout = LOCAL_REQUEST_PACKAGE_DEFAULT_TIMEOUT;
            // Copy fields from internalHandler
            _outstandingRequests = internalHandler.OutstandingRequests;
            _packageFileName = internalHandler.PackageFileName;
        }

        /// <summary>
        /// Constructor for a local proxy's request handler.
        /// </summary>
        /// <param name="proxy">Proxy this request handler belongs to.</param>
        /// <param name="context">Client context.</param>
        public LocalRequestHandler(RCLocalProxy proxy, HttpListenerContext context)
            : base(proxy, context)
        {
            _requestId = _proxy.GetAndIncrementNextRequestID();
            _requestTimeout = LOCAL_REQUEST_PACKAGE_DEFAULT_TIMEOUT;
        }
        /// <summary>
        /// DUMMY used for request matching.
        /// XXX: Not the cleanest implementation need to instantiate a whole object just to match
        /// </summary> 
        /// <param name="itemId">The item id of the request handler.</param>
        public LocalRequestHandler(string itemId)
        {
            _rcRequest = new RCRequest(itemId);
        }

        /// <summary>
        /// Main logic of RuralCafe LPRequestHandler.
        /// Called by Go() in the base RequestHandler class.
        /// </summary>
        public override Status HandleRequest()
        {
            if (IsBlacklisted(RequestUri))
            {
                Logger.Debug("ignoring blacklisted: " + RequestUri);
                SendErrorPage(HttpStatusCode.NotFound, "blacklisted: " + RequestUri);
                return Status.Failed;
            }

            // XXX: this function will return true if the domain is wikipedia even if the file isn't in the archive
            // XXX: ideally, this would return true or false and then if the wiki page is properly served it returns
            // XXX: Otherwise we can stream/fetch it from the cache
            if (IsInWikiCache())
            {
                return ServeWikiURI();
            }

            // XXX: not cacheable, ignore, and log it instead of streaming for now
            // XXX: we could pass through this stuff directly, but it would require bypassing all filtering
            if (!IsCacheable()
                && _proxy.NetworkStatus == RCProxy.NetworkStatusCode.Online
                )
            {
                Logger.Debug("streaming: " + RequestUri + " to client.");

                long bytesSent = StreamTransparently();
                _rcRequest.FileSize = bytesSent;

                return Status.Completed;
            }

            // Do only use cache for HEAD/GET
            if (IsGetOrHeadHeader() && IsCached(_rcRequest.CacheFileName))
            {
                // try getting the content type from the file extension
                string contentType = Utils.GetContentTypeOfFile(_rcRequest.CacheFileName);
                
                _clientHttpContext.Response.ContentType = contentType;

                _rcRequest.FileSize = StreamFromCacheToClient(_rcRequest.CacheFileName);
                if (_rcRequest.FileSize < 0)
                {
                    return Status.Failed;
                }
                return Status.Completed;
            }

            // XXX: not sure if this should even be here, technically for a cacheable file that's not cached, this is
            // XXX: behaving like a synchronous proxy
            // XXX: this is fine if we're online, fine if we're offline since it'll fail.. but doesn't degrade gradually

            // XXX: response time could be improved here if it downloads and streams to the client at the same time
            // XXX: basically, merge the DownloadtoCache() and StreamfromcachetoClient() methods into a new third method.
            // cacheable but not cached, cache it, then send to client if there is no remote proxy
            // if online, stream to cache, then stream to client.
            if (_proxy.NetworkStatus == RCProxy.NetworkStatusCode.Online)
            {
                Logger.Debug("streaming: " + _rcRequest.GenericWebRequest.RequestUri + " to cache and client.");
                _rcRequest.GenericWebRequest.Proxy = null;
                long bytesDownloaded = _rcRequest.DownloadToCache(true);
                try
                {
                    FileInfo f = new FileInfo(_rcRequest.CacheFileName);
                    if (bytesDownloaded > -1 && f.Exists)
                    {
                        _rcRequest.FileSize = StreamFromCacheToClient(_rcRequest.CacheFileName);
                        if (_rcRequest.FileSize < 0)
                        {
                            return Status.Failed;
                        }
                        return Status.Completed;
                    }
                    else
                    {
                        return Status.Failed;
                    }
                }
                catch
                {
                    // do nothing
                }
                return Status.Failed;
            }
            
            if (_proxy.NetworkStatus != RCProxy.NetworkStatusCode.Online)
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
                string id = "" + ((RCLocalProxy)_proxy).AddRequestWithoutUser(this);

                string redirectUrl = "http://www.ruralcafe.net/" +
                    //(IsGetOrHeadHeader() ? 
                    "trotro-user.html"
                    //: "request/add")
                    + "?t=" + title + "&a=" + id;
                _clientHttpContext.Response.Redirect(redirectUrl);
                //_clientHttpContext.Response.StatusCode = (int)HttpStatusCode.TemporaryRedirect;

                return Status.Completed;
            }
            return Status.Failed;
        }

        /// <summary>
        /// Helper method to get the ETA from the proxy this handler belongs to.
        /// </summary>
        /// <returns>ETA as a printable string.</returns>
        public string PrintableETA()
        {
            int eta = ((RCLocalProxy)_proxy).ETA(this);
            string etaString = "";
            if ((this.RequestStatus == Status.Completed) ||
                (this.RequestStatus == Status.Failed))
            {
                etaString = "0";
            }
            else if (eta < 60)
            {
                etaString = "< 1 min";
            }
            else
            {
                // now eta is in minutes
                eta = eta / 60;
                if (eta < 60)
                {
                    if (eta == 1)
                    {
                        etaString = "1 min";
                    }
                    else
                    {
                        etaString = eta.ToString() + " min";
                    }
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
            //LogDebug("eta sent: " + etaString);
            return etaString;
        }

        #region Serve Wikipedia Page Methods

        /// <summary>
        /// Checks if the request is in the wikipedia cache.
        /// </summary>
        /// <returns>True or false if the request is in the wiki cache or not.</returns>
        private bool IsInWikiCache()
        {
            if (!((RCLocalProxy)_proxy).HasWikiIndices())
            {
                return false;
            }
            if (RequestUri.StartsWith("http://en.wikipedia.org/wiki/"))
            {
                // images aren't currently cached, just return no
                if (RequestUri.StartsWith("http://en.wikipedia.org/wiki/File:"))
                {
                    return false;
                }

                if (((RCLocalProxy)_proxy).WikiDumpPath.Equals(""))
                {
                    return false;
                }

                // XXX: need to check whether the request is actually in the cache
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets called to render a URI.
        /// </summary>
        /// <param name="e">Request parameters.</param>
        private void ServeWikiURLRenderPage(UrlRequestedEventArgs e)
        {
            PageInfo page = null;

            if (page == null ||
                !e.Url.Equals(page.Name, StringComparison.InvariantCultureIgnoreCase))
            {
                HitCollection hits = Indexer.Search(e.Url, RCLocalProxy.WikiIndices.Values, 1);

                page = null;

                if (hits.Count > 0)
                {
                    page = hits[0];
                }
            }

            if (page != null)
            {
                e.Response = page.GetFormattedContent();
                e.RedirectTarget = page.RedirectToTopic;
                e.Redirect = !String.IsNullOrEmpty(e.RedirectTarget);
            }
        }

        /// <summary>
        /// Serves a Wikipedia page using the Wiki renderer.
        /// </summary>
        /// <returns>Status of the handler.</returns>
        private Status ServeWikiURI()
        {
            try
            {
                Uri uri = new Uri(RequestUri);
                // XXX: not sure if we need to Decode again
                UrlRequestedEventArgs urea = new UrlRequestedEventArgs(HttpUtility.UrlDecode(uri.AbsolutePath.Substring(6)));

                ServeWikiURLRenderPage(urea);
                if (urea.Redirect)
                {
                    // This sets Location header and status code!
                    _clientHttpContext.Response.Redirect(urea.RedirectTarget);
                }
                //SendWikiHeader("HTTP/1.1", sendBuf.Length, redirectUrl, _clientHttpContext);
                SendMessage(urea.Response);
            }
            catch (Exception)
            {
                return Status.Failed;
            }
            return Status.Completed;
        }

        /// <summary>
        /// Generates the URL for the given request term.
        /// </summary>
        /// <param name="term">The request term to generate the URL for.</param>
        /// <returns>The URL.</returns>
        public string GenerateUrl(string term)
        {
            return String.Format("http://en.wikipedia.org/wiki/{0}", HttpUtility.UrlEncode(term));
        }

        # endregion
    }
}
