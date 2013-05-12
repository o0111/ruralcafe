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

namespace RuralCafe
{
    /// <summary>
    /// Request handler for requests coming in to the local proxy.
    /// </summary>
    public class LocalRequestHandler : RequestHandler
    {
        public static int DEFAULT_QUOTA;
        public static int DEFAULT_DEPTH;
        public static Richness DEFAULT_RICHNESS;

        /// <summary>
        /// Constructor for a local proxy's request handler.
        /// </summary>
        /// <param name="proxy">Proxy this request handler belongs to.</param>
        /// <param name="socket">Client socket.</param>
        public LocalRequestHandler(LocalInternalRequestHandler internalHandler)
            : base(internalHandler.Proxy, internalHandler.Socket)
        {
            _requestId = _proxy.NextRequestId;
            _proxy.NextRequestId = _proxy.NextRequestId + 1;
            _requestTimeout = LOCAL_REQUEST_PACKAGE_DEFAULT_TIMEOUT;
            // Copy fields from internalHandler
            _outstandingRequests = internalHandler.OutstandingRequests;
            _packageFileName = internalHandler.PackageFileName;
        }

        /// <summary>
        /// Constructor for a local proxy's request handler.
        /// </summary>
        /// <param name="proxy">Proxy this request handler belongs to.</param>
        /// <param name="socket">Client socket.</param>
        public LocalRequestHandler(RCLocalProxy proxy, Socket socket)
            : base(proxy, socket)
        {
            _requestId = _proxy.NextRequestId;
            _proxy.NextRequestId = _proxy.NextRequestId + 1;
            _requestTimeout = LOCAL_REQUEST_PACKAGE_DEFAULT_TIMEOUT;
        }
        /// <summary>
        /// DUMMY used for request matching.
        /// XXX: Not the cleanest implementation need to instantiate a whole object just to match
        /// </summary> 
        public LocalRequestHandler(string itemId)
        {
            _rcRequest = new RCRequest(itemId);
        }
        /// <summary>Destructor.</summary>
        ~LocalRequestHandler()
        {
            // cleanup stuff
        }

        /// <summary>
        /// Override Equals() for handler matching.
        /// Just calls the base class.
        /// </summary>
        public override bool Equals(object obj)
        {
            return base.Equals(obj);
        }

        /// <summary>
        /// Overriding GetHashCode() from base object.
        /// Just use the hash code of the RequestUri.
        /// </summary>        
        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        /// <summary>
        /// Main logic of RuralCafe LPRequestHandler.
        /// Called by Go() in the base RequestHandler class.
        /// </summary>
        public override Status HandleRequest()
        {
            if (IsBlacklisted(RequestUri))
            {
                LogDebug("ignoring blacklisted: " + RequestUri);
                SendErrorPage(HTTP_NOT_FOUND, "ignoring blacklisted", RequestUri);
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
            // XXX: we could pass through this stuff directly, but it would require bypassing all blacklist/filtering
            if ((!IsGetOrHeadHeader() || !IsCacheable()) && 
                _proxy.NetworkStatus == RCProxy.NetworkStatusCode.Online)
            {
                LogDebug("streaming: " + RequestUri + " to client.");

                long bytesSent = StreamTransparently("");
                _rcRequest.FileSize = bytesSent;

                return Status.Completed;
            }

            if (IsCached(_rcRequest.CacheFileName))
            {
                // Try getting the mime type from the search index
                string contentType = GetMimeType(RequestUri);
                
                // try getting the content type from the file extension
                if (contentType.Equals("text/unknown"))
                {
                    contentType = Util.GetContentTypeOfFile(_rcRequest.CacheFileName);
                }
                // peek at the file, major hackery...
                string peekFile = System.IO.File.ReadAllText(_rcRequest.CacheFileName);
                if (peekFile.StartsWith("HTTP/1.1 301 Moved Permanently"))
                {
                    // don't bother sending HTTP OK headers
                }
                else
                {
                    SendOkHeaders(contentType);
                }

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
                LogDebug("streaming: " + _rcRequest.GenericWebRequest.RequestUri + " to cache and client.");
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
                // Uncached links should be redirected to trotro-user.html?t=title&a=url when the system mode is slow or offline
                // Parse parameters to get title
                NameValueCollection qscoll = Util.ParseHtmlQuery(RequestUri);
                string redirectUri = qscoll.Get("trotro");
                if (redirectUri == null)
                {
                    // XXX: temporary title
                    redirectUri = RequestUri;
                    int pos = redirectUri.LastIndexOf("/");
                    while (pos > 20)
                    {
                        redirectUri = redirectUri.Substring(0, pos);
                        pos = redirectUri.LastIndexOf("/");
                    }
                }
                SendRedirect(redirectUri, RequestUri);

                return Status.Completed;
            }
            return Status.Failed;
        }

        /// <summary>
        /// Helper to get the mime type from the URI
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        string GetMimeType(string uri)
        {
            List<Lucene.Net.Documents.Document> filteredResults = new List<Lucene.Net.Documents.Document>();

            string queryString = uri;
            if (queryString.Trim().Length > 0)
            {
                List<Lucene.Net.Documents.Document> results = IndexWrapper.Query(((RCLocalProxy)_proxy).IndexPath, queryString);

                string headerToken = "Content-Type:";
                // remove duplicates
                foreach (Lucene.Net.Documents.Document document in results)
                {
                    string documentUri = document.Get("uri");
                    if (documentUri.Equals(uri)) {
                        string headers = document.Get("headers");
                        // extract mime type
                        int i = headers.IndexOf(headerToken);
                        if (i < 0)
                            continue;
                        string chunk = headers.Substring(i + headerToken.Length);
                        int i2 = chunk.IndexOf("\r\n");
                        if (i2 < 0)
                            continue;
                        string mimeType = chunk.Substring(0, i2).Trim();
                        return mimeType;
                    }
                }
            }
            return "text/unknown";
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
            string response = "Not found";
            string redirect = String.Empty;

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
                response = page.GetFormattedContent();
                redirect = page.RedirectToTopic;
            }

            e.Redirect = !String.IsNullOrEmpty(redirect);
            e.RedirectTarget = redirect;
            e.Response = response;
        }

        /// <summary>
        /// Serves a Wikipedia page using the Wiki renderer.
        /// </summary>
        /// <returns>Status of the handler.</returns>
        private Status ServeWikiURI()
        {
            string response = String.Empty;
            string redirectUrl = String.Empty;

            try
            {
                Uri uri = new Uri(RequestUri);
                // XXX: not sure if we need to Decode again
                UrlRequestedEventArgs urea = new UrlRequestedEventArgs(HttpUtility.UrlDecode(uri.AbsolutePath.Substring(6)));

                ServeWikiURLRenderPage(urea);

                redirectUrl = urea.Redirect ? urea.RedirectTarget : String.Empty;
                response = urea.Redirect ? "302 Moved" : urea.Response;

                byte[] sendBuf = Encoding.UTF8.GetBytes(response);
                SendWikiHeader("HTTP/1.1", sendBuf.Length, redirectUrl, _clientSocket);
                _clientSocket.Send(sendBuf);
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

        /// <summary>
        /// Sends HTTP header to the client for the wiki URI.
        /// </summary>
        /// <param name="httpVersion">Http version string</param>
        /// <param name="bytesCount">The number of bytes in the response stream</param>
        /// <param name="statusCode">HTTP status code</param>
        /// <param name="socket">The socket where to write to</param>
        public void SendWikiHeader(string httpVersion, int bytesCount, string redirectLocation, Socket socket)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append(httpVersion);
            sb.Append(" ");
            sb.Append(String.IsNullOrEmpty(redirectLocation) ? "200" : "302");
            sb.AppendLine();
            sb.AppendLine("Content-Type: text/html");

            if (!String.IsNullOrEmpty(redirectLocation))
            {
                sb.Append("Location: ");
                sb.AppendLine(GenerateUrl(redirectLocation));
            }

            sb.AppendLine("Accept-Ranges: bytes");
            sb.Append("Content-Length: ");
            sb.Append(bytesCount);
            sb.AppendLine();
            sb.AppendLine();

            socket.Send(Encoding.ASCII.GetBytes(sb.ToString()));
        }

        # endregion
    }
}
