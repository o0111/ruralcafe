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

using System.Web;
using Lucene.Net.Search;
using BzReader;

namespace RuralCafe
{
    public class LocalRequest : GenericRequest
    {
        private static int _nextId = 1;

        // localProxy
        public LocalProxy _localProxy;
        private string _searchPageFileName;
        
        public static int DEFAULT_DEPTH; 

        public LocalRequest(LocalProxy proxy, Socket socket)
            : base(proxy, socket)
        {
            _requestId = _nextId++;
            // same as _proxy in GenericRequest, but its here so I don't have to cast it everywhere
            _localProxy = proxy;

            _searchPageFileName = _localProxy.RuralCafeSearchPage;
        }
        // XXX: not the cleanest implementation need to instantiate a whole object just to match
        // DUMMY used for request matching
        public LocalRequest(LocalProxy proxy, Socket socket, string requestedUri)
            : this(proxy, socket)
        {
            if (!Util.IsValidUri(requestedUri))
            {
                // XXX: do nothing
            }
            _requestObject = new RequestObject(_proxy, requestedUri);

            /* XXX: don't think the dummy needs this
            // setup the header variables
            if (IsRuralCafeLocalSearch() || IsRuralCafeRemoteRequest())
            {
                ParseRuralCafeQuery();
            }
             */
        }
        ~LocalRequest()
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

        #region Property Accessors

        public string ETA
        {
            get 
            {
                int eta = _localProxy.ETA(this);
                string etaString = "";
                if (eta == 0)
                {
                    etaString = "Unknown";
                }
                else if (eta < 60)
                {
                    etaString = "< 1 minute";
                }
                else
                {
                    // now eta is in minutes
                    eta = eta / 60;
                    if (eta < 60)
                    {
                        if (eta == 1)
                        {
                            etaString = "about a minute";// eta.ToString() + " minute";
                        }
                        else
                        {
                            etaString = eta.ToString() + " minutes";
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
                return etaString;
            }
        }

        #endregion
        // ZZZ: placeholder
        public override void PrefetchBenchmarker(string richness, int depth)
        {
        }

        // this is the main logic of RuralCafe local proxy
        public override void HandlePageRequest()
        {
            // is it not something we can handle
            if (!IsGetOrHeadHeader())
            {
                /*
                // XXX: we could pass this through directly, but it would require bypassing all
                // of the admission control/queuing stuff, not handling POST
                //LogRequest();
                LogDebug("not GET or HEAD method, returning nothing.");
                SendErrorPage(HTTP_BAD_METHOD, "not GET or HEAD method, returning nothing.", "");
                 */
                LogDebug("streaming: " + RequestUri + " at " + GenericProxy.DEFAULT_MAX_DOWNLOAD_SPEED + "bps");
                long bytesSent = StreamTransparently();

                Status = GenericRequest.STATUS_NOTCACHEABLE;
                _endTime = DateTime.Now;
                _requestObject._fileSize = bytesSent;

                LogRequest();
                return;
            }

            if (_localProxy.IsBlacklisted(RequestUri))
            {
                LogDebug("ignoring blacklisted: " + RequestUri);
                SendErrorPage(HTTP_NOT_FOUND, "ignoring blacklisted", RequestUri);
                return;
            }

            if (IsRCRequest())
            {
                ServeRCPage();
                return;
            }

            // XXX: this function will return true if the domain is wikipedia even if the file isn't in the archive...
            if (IsInWikiCache())
            {
                LogRequest();

                ServeWikiURL();
                return;
            }

            // XXX: not cacheable, ignore, and log it instead of streaming for now
            if (!IsCacheable())
            {
                /*
                LogDebug("not cacheable");
                SendErrorPage(HTTP_NOT_FOUND, "not cacheable, returning nothing.", "");
                 */
                LogDebug("streaming: " + _requestObject._webRequest.RequestUri + " at " + GenericProxy.DEFAULT_MAX_DOWNLOAD_SPEED + "bps");
                long bytesSent = StreamTransparently();

                Status = GenericRequest.STATUS_NOTCACHEABLE;
                _endTime = DateTime.Now;
                _requestObject._fileSize = bytesSent;

                LogRequest();
            }
            else
            {
                // set the last requested page since its not a ruralcafe or post page
                _localProxy.SetLastRequest(_clientAddress, this);

                if (IsCached(_requestObject._cacheFileName))
                {
                    // is cached, serve from cache
                    //LogDebug("cached, serving page from cache");

                    // Try getting the mime type from the index
                    string contentType = GetMimeType(RequestUri);
                    // Otherwise, try figuring out from the file extension
                    if (contentType.Equals("text/unknown"))
                        contentType = Util.GetContentTypeOfFile(_requestObject._cacheFileName);
                    SendOkHeaders(contentType);

                    _requestObject._fileSize = StreamFromDiskToClient(_requestObject._cacheFileName, _requestObject.IsCompressed());

                    Status = GenericRequest.STATUS_CACHED;
                    _endTime = DateTime.Now;

                    LogRequest();
                }
                else
                {
                    // XXX: hackery so that stuff that's cacheable but not cached is downloaded then sent out
                    if (_localProxy.RemoteWebProxy == null)
                    {
                        LogDebug("streaming to cache: " + _requestObject._webRequest.RequestUri + " at " + GenericProxy.DEFAULT_MAX_DOWNLOAD_SPEED + "bps");
                        _requestObject._webRequest.Proxy = null;
                        int bytesStreamed = StreamRequestFromServerToCache(_proxy, this, _requestObject._webRequest, _requestObject._cacheFileName);
                        try
                        {
                            FileInfo f = new FileInfo(_requestObject._cacheFileName);
                            if (bytesStreamed == 0 || !f.Exists)
                            {
                                // do nothing
                            }
                            else
                            {
                                LogDebug("streaming from cache: " + _requestObject._webRequest.RequestUri + " at " + GenericProxy.DEFAULT_MAX_DOWNLOAD_SPEED + "bps");

                                _requestObject._fileSize = StreamFromDiskToClient(_requestObject._cacheFileName, _requestObject.IsCompressed());
                            }
                        }
                        catch
                        {
                            // do nothing
                        }
                        finally
                        {
                            //Serve404PageWithAddLink();
                        }
                    }
                        /*
                    // XXX: Bypassing all page requests other than Ruralcafe ones for Amrita baseline
                    else if (_localProxy._amritaBaseline)
                    {
                        LogDebug("streaming: " + _requestObject._webRequest.RequestUri + " at " + GenericProxy.DEFAULT_MAX_DOWNLOAD_SPEED + "bps");

                        long bytesSent = StreamTransparently();

                        Status = GenericRequest.STATUS_NOTFOUND;
                        _endTime = DateTime.Now;
                        _requestObject._fileSize = bytesSent;
                    }
                    else
                    {*/
                        // not cached, allow the user to add the page requested to a URL request
                        //LogDebug("not cached, serving 404 page");

                        Serve404Page(false);

                        Status = GenericRequest.STATUS_NOTFOUND;
                        _endTime = DateTime.Now;
                    //}
                    // XXX: hackery to avoid duplicate request logging when redirecting after an add
                    if (!RequestUri.Equals(ReferrerUri))
                    {
                        LogRequest();
                    }
                }
            }
            //_localProxy._activeRequests--;
        }

        // determine the RuralCafe page and serve it
        void ServeRCPage()
        {
            if (IsRemovePage())
            {
                LogDebug("removing request from queues " + RequestUri);

                RemoveRequestFromRuralCafeQueues();

                ServeRCRequestsRedirect();
            }
            else if (IsRemoveAllPage())
            {
                LogDebug("removing all requests from queues");

                _localProxy.RemoveAllRequestsFromQueues(_clientAddress);

                ServeRCRequestsRedirect();
            }
            else if (IsAddPage() || IsRetryPage())
            {
                int offset = RequestUri.IndexOf('=');
                string requestedUri = RequestUri.Substring(offset + 1);
                RequestUri = requestedUri;
                // rebuild the requestObject and parse the parameters
                // (instead of making a new object)
                _requestObject = new RequestObject(_proxy, requestedUri, _requestObject._refererUri);
                _requestObject.ParseSearchFields();

                string queryString = _requestObject.GetRCSearchField("textfield");
                if (queryString.Trim().Length > 0)
                {
                    //LogDebug("adding page " + RequestUri);

                    QueueRequest();
                    LogRequest();
                }

                // special case where we want to send the user back to the search results page
                ReferrerUri = _requestObject.GetRCSearchField("referrer");
                if (ReferrerUri != null && !ReferrerUri.Equals(""))
                {
                    // can't serve the lucene results directly, since it wasn't requested it will
                    // screw up the refererUri... we have to redirect back to the page via an intermediate
                    ServeRedirectPage();
                    return;
                }
                SendErrorPage(404, "No RefererUri found after adding page", "");
            }
            else
            {
                if (IsRCHomePage())
                {
                    /*
                    //LogDebug("serving RuralCafe frames page " + RequestUri);
                    // XXX: bypassing for Amrita baseline
                    if (_localProxy._amritaBaseline)
                    {
                        ServeRuralCafeSearchPage(_searchPageFileName);
                    }
                    else
                    {*/
                    if (_localProxy.RuralCafeSearchPage.Equals("http://www.ruralcafe.net/cip.html"))
                    {
                        ServeRCSearchPage("searchpage.html");
                    }
                    else
                    {
                        ServeRCFramesPage();
                    }
                    //}
                }
                else if (IsRCHeaderPage())
                {
                    ServeRCHeaderPage();
                }
                else if (IsRCRequestsPage())
                {
                    ServeRCRequestsPage();
                }
                else if (RequestUri.Equals("http://www.ruralcafe.net/searchpage.html") ||
                    RequestUri.StartsWith("www.ruralcafe.net/searchpage.html"))
                {
                    ServeRCSearchPage(RequestUri);
                }
                /* broken, disabled for CIP
                // XXX: temporary to serve images of ruralcafe search page
                else if (RequestUri.StartsWith("http://www.ruralcafe.net/images") ||
                    RequestUri.StartsWith("www.ruralcafe.net/images"))
                {
                    ServeRuralCafeImagePage(RequestUri);
                }*/
                else if (IsRCImagesPage())
                {
                    ServeRCImagePage(RequestUri);
                }
                else if (IsRCLocalSearch())
                {
                    LogDebug("serving RuralCafe results page");

                    SetLastRequestedPage();

                    string queryString = _requestObject.GetRCSearchField("textfield");
                    if (queryString.Trim().Length > 0)
                    {
                        ServeRCResultsPage(queryString);
                        // XXX: hackery to get around logging the redirect result as if it were another request by the user
                        if (!RequestUri.Equals(ReferrerUri))
                        {
                            LogRequest();
                        }
                    }
                    else
                    {
                        ServeRCSearchPage(_searchPageFileName);
                    }
                }
                else if (IsRCRemoteQuery())
                {
                    /*
                    // XXX: bypassing for Amrita baseline
                    if (_localProxy._amritaBaseline)
                    {
                        string pageUri = _requestObject.TranslateRuralCafeToGoogleSearchUri();
                        _requestObject._webRequest = (HttpWebRequest)WebRequest.Create(pageUri);

                        StreamTransparentlyAmritaBaseline(_requestObject._webRequest);
                    }
                    else
                    {*/
                    /*
                        if (_requestObject.GetRCSearchField("button") == "Search")
                        {
                            LogDebug("serving RuralCafe local results page");

                            SetLastRequestedPage();

                            string queryString = _requestObject.GetRCSearchField("textfield");
                            if (queryString.Trim().Length > 0)
                            {
                                ServeSearchResultsPage(queryString);
                                // XXX: hackery to get around logging the redirect result as if it were another request by the user
                                if (!RequestUri.Equals(ReferrerUri))
                                {
                                    LogRequest();
                                }
                            }
                            else
                            {
                                ServeRuralCafeSearchPage(_searchPageFileName);
                            }
                        }
                        else
                        {*/
                            LogDebug("queuing RuralCafe remote request");

                            string requestString = _requestObject.GetRCSearchField("textfield");
                            if (requestString.Trim().Length > 0)
                            {
                                QueueRequest();
                                LogRequest();
                            }

                            LocalRequest lastRequestedPage = _localProxy.GetLastRequest(_clientAddress);
                            if (lastRequestedPage != null)
                            {
                                requestString = lastRequestedPage.SearchTermsOrURI();
                                if (IsRCLocalSearch() || IsRCRemoteQuery())
                                {
                                    ServeRCResultsPage(requestString);
                                }
                                else
                                {
                                    StreamFromDiskToClient(lastRequestedPage._requestObject._cacheFileName, lastRequestedPage._requestObject.IsCompressed());
                                }
                            }
                        //}
                    //}
                }
                else
                {
                    SendErrorPage(404, "page does not exist", RequestUri);
                    Status = GenericRequest.STATUS_NOTFOUND;
                    _endTime = DateTime.Now;
                }
            }
        }

        private void SetLastRequestedPage()
        {
            _localProxy.SetLastRequest(_clientAddress, this);
        }

        public bool IsInWikiCache()
        {
            if (RequestUri.StartsWith("http://en.wikipedia.org/wiki/"))
            {
                // images aren't currently cached, just return no
                if (RequestUri.StartsWith("http://en.wikipedia.org/wiki/File:"))
                {
                    return false;
                }

                if (_localProxy._wikiDumpPath.Equals(""))
                {
                    return false;
                }

                // XXX: need to check whether the request is actually in the cache
                return true;
            }
            return false;
        }

        #region Request and Response Methods

        /* XXX: depricated
        // accessors for streaming from server
        // adds the webproxy to all requests going through these
        public long StreamRequestFromProxyToClient()
        {
            // XXX: local proxy will wait forever for the remote proxy to respond
            Request.Timeout = Timeout.Infinite;
            Request.Proxy = _localProxy.RemoteWebProxy;

            return StreamRequestFromServerToClient();
        }
         */
        public int StreamRequestFromProxyToCache()
        {
            _requestObject._webRequest.Timeout = _timeout;
            _requestObject._webRequest.Proxy = _localProxy.RemoteWebProxy;
            
            // XXX: got this from msdn
            //var request = (HttpWebRequest)HttpWebRequest.Create(requestUri);
            //WRequest.Headers.Add(HttpRequestHeader.AcceptEncoding, "gzip,deflate");
            //WRequest.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            return StreamRequestFromServerToCache(_proxy, this, _requestObject._webRequest, _requestObject._packageIndexFileName);
        }

        /*
        // helper function to add a request object to lucene index
        void AddToLuceneIndex(RequestObject requestObject)
        {
            string pageContent = ReadFileAsString(requestObject._fileName);
            string pageTitle = GetPageTitle(pageContent);

            LuceneIndex.IndexDocument(_localProxy._luceneIndexPath, requestObject._uri, pageTitle, pageContent);
        }*/
        // needs to be able to parse HTML

        #endregion

        #region Check Request Methods

        // check if the URI request is actually for the RuralCafe homepage
        bool IsRCHomePage()
        {
            if (RequestUri.Equals("http://www.ruralcafe.net") ||
                RequestUri.Equals("http://www.ruralcafe.net/") ||
                RequestUri.Equals("www.ruralcafe.net") ||
                RequestUri.Equals("www.ruralcafe.net/") ||
                RequestUri.Equals("http://www.ruralcafe.net/index.html") ||
                RequestUri.Equals("www.ruralcafe.net/index.html"))
            {
                return true;
            }
            return false;
        }

        bool IsRCHeaderPage()
        {
            if (RequestUri.Equals("http://www.ruralcafe.net/header.html") ||
                RequestUri.Equals("www.ruralcafe.net/header.html"))
            {
                return true;
            }
            return false;
        }

        bool IsRCRequestsPage()
        {
            if (RequestUri.Equals("http://www.ruralcafe.net/requests.html") ||
                RequestUri.Equals("www.ruralcafe.net/requests.html"))
            {
                return true;
            }
            return false;
        }

        bool IsRCImagesPage()
        {
            if (RequestUri.Contains("www.ruralcafe.net") &&
                    RequestUri.EndsWith(".gif"))
            {
                return true;
            }
            return false;
        }
        
        // check if the URI request is actually a command to add the url to RuralCafe
        bool IsAddPage()
        {
            if (RequestUri.StartsWith("http://www.ruralcafe.net/addpage="))
            {
                return true;
            } 
            return false;
        }
        // check if the URI request is actually a command to add the url to RuralCafe
        bool IsRemovePage()
        {
            if (RequestUri.StartsWith("http://www.ruralcafe.net/removepage="))
            {
                return true;
            }
            return false;
        }
        bool IsRemoveAllPage()
        {
            if (RequestUri.StartsWith("http://www.ruralcafe.net/removeall"))
            {
                return true;
            }
            return false;
        }

        // check if the URI request is actually a command to retry the url
        bool IsRetryPage()
        {
            if (RequestUri.StartsWith("http://www.ruralcafe.net/retrypage="))
            {
                return true;
            }
            return false;
        }
        // check if the URI request is actually a command to modify the search to RuralCafe
        bool IsModifyPage()
        {
            if (RequestUri.StartsWith("http://www.ruralcafe.net/modifypage="))
            {
                return true;
            }
            return false;
        }
        // check if the URI request is actually a command to expand the search to RuralCafe
        bool IsExpandPage()
        {
            if (RequestUri.StartsWith("http://www.ruralcafe.net/expandpage="))
            {
                return true;
            }
            return false;
        }

        #endregion

        #region Request Management Methods

        // adds the request to ruralcafe's queue of requests
        void QueueRequest()
        {
            _localProxy.AddRequestToQueues(_clientAddress, this);
        }

        // Removes the request from ruralcafe's queue of requests
        void RemoveRequestFromRuralCafeQueues()
        {
            // clean up the Ruralcafe info, remove it
            int index = RequestUri.IndexOf('=');
            string removeRequestUri = RequestUri.Substring(index + 1);
            LocalRequest matchingRequest = new LocalRequest(_localProxy, _clientSocket, removeRequestUri);
            _localProxy.RemoveRequestFromQueues(_clientAddress, matchingRequest);
        }

        #endregion

        #region Serve Rural Cafe Page Methods

        // XXX: depricated
        /*
        // search locally for the exact match, and the lucene match
        bool ServeExactResultsPage()
        {
            // XXX: currently only getting exact google matches
            // compile all cached search results from various sources            
            //_cachedResults = new LinkedList<Result>();

            // first check for exact search matches
            string googleWebRequestUri = TranslateRuralCafeToGoogleSearchUri();
            if (!IsValidUri(googleWebRequestUri))
            {
                return false;
            }
            RequestObject googleRequestObject = new RequestObject(_proxy, googleWebRequestUri);
            if (IsCached(googleRequestObject._fileName))
            {
                // serve the page
                SendOkHeaders("text/html");
                StreamPageFromDiskToClient(googleRequestObject._fileName);
                return true;
            }

            return false;
        }*/


        /// <summary>
        /// Gets called whenever the browser control requests a URL from the web server
        /// </summary>
        /// <param name="sender">Web server instance</param>
        /// <param name="e">Request parameters</param>
        private void ServeWikiURLRenderPage(object sender, UrlRequestedEventArgs e)
        {
            string response = "Not found";
            string redirect = String.Empty;

            PageInfo page = null; // hitsBox.SelectedItem as PageInfo;

            if (page == null ||
                !e.Url.Equals(page.Name, StringComparison.InvariantCultureIgnoreCase))
            {
                HitCollection hits = Indexer.Search(e.Url, LocalProxy.indexes.Values, 1);

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

        void ServeWikiURL()
        {
            string response = String.Empty;
            string redirectUrl = String.Empty;

            Uri uri = new Uri(RequestUri);//new Uri(new Uri(GenerateUrl(String.Empty)), url);
            // XXX: probably buggy since we're doing URlDecode and such again... not sure if we need to.
            UrlRequestedEventArgs urea = new UrlRequestedEventArgs(HttpUtility.UrlDecode(uri.AbsolutePath.Substring(6)));

            // skip the events and such and just call the method
            ServeWikiURLRenderPage(this, urea);
            // event handler
            //UrlRequested(this, urea);

            redirectUrl = urea.Redirect ? urea.RedirectTarget : String.Empty;
            response = urea.Redirect ? "302 Moved" : urea.Response;

            // XXX: this needs to get integrated into the SendMessage method - done.
            byte[] sendBuf = Encoding.UTF8.GetBytes(response);
            SendWikiHeader("HTTP/1.1", sendBuf.Length, redirectUrl, _clientSocket);
            _clientSocket.Send(sendBuf);
            //SendOkHeaders();
            //SendMessage(_clientSocket, response);
        }

        /// <summary>
        /// Generates the URL for the given request term
        /// </summary>
        /// <param name="term">The request term to generate the URL for</param>
        /// <returns>The URL</returns>
        public string GenerateUrl(string term)
        {
            return String.Format("http://en.wikipedia.org/wiki/{0}", HttpUtility.UrlEncode(term));
        }

        /// <summary>
        /// Sends HTTP header to the client
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

        string GetMimeType(string url)
        {
            List<Lucene.Net.Documents.Document> filteredResults = new List<Lucene.Net.Documents.Document>();

            string queryString = url;
            if (queryString.Trim().Length > 0)
            {
                List<Lucene.Net.Documents.Document> results = CacheIndexer.Query(_localProxy._indexPath, queryString);

                string headerToken = "Content-Type:";
                // remove duplicates
                foreach (Lucene.Net.Documents.Document document in results)
                {
                    string documentUri = document.Get("uri");
                    if (documentUri.Equals(url)) {
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

        // Query Lucene index of local pages
        void ServeRCResultsPage(string queryString)
        {
            // RuralCafe local results
            int maxLength = 150;
            string tableHeader = "<table width=\"80%\" cellpadding=5 border=0>";
            string tableFooter = "</table>";
            string pageHeader = "<html><head></head><body><center>";
            string pageFooter = "</center></body></html>";
            string resultsString = "";

            List<Lucene.Net.Documents.Document> filteredLuceneResults = new List<Lucene.Net.Documents.Document>();
            HitCollection wikiResults = new HitCollection();
            if(queryString.Trim().Length > 0) {
                // Query the Wiki index
                wikiResults = Indexer.Search(queryString, LocalProxy.indexes.Values, Indexer.MAX_SEARCH_HITS); 
                
                // Query our RuralCafe index
                List<Lucene.Net.Documents.Document> luceneResults = CacheIndexer.Query(_localProxy._indexPath, queryString);

                // remove duplicates
                foreach (Lucene.Net.Documents.Document document in luceneResults)
                {
                    string documentUri = document.Get("uri");
                    string documentTitle = document.Get("title");

                    // ignore blacklisted domains
                    if (_localProxy.IsBlacklisted(documentUri))
                    {
                        continue;
                    }

                    bool exists = false;
                    foreach (Lucene.Net.Documents.Document filteredDocument in filteredLuceneResults)
                    {
                        string documentUri2 = filteredDocument.Get("uri");
                        string documentTitle2 = filteredDocument.Get("title");
                        if (documentUri.Equals(documentUri2) || documentTitle.Equals(documentTitle2))
                        {
                            exists = true;
                            break;
                        }
                    }
                    if (exists == false)
                    {
                        filteredLuceneResults.Add(document);
                    }

                }
            }
            LogDebug(filteredLuceneResults.Count + " results");

            SendOkHeaders("text/html");
            SendMessage(pageHeader);

            LocalRequest lastRequest = _localProxy.GetLastRequest(_clientAddress);
            // last requested page
            if (lastRequest != null)
            {
                SendMessage("<form action=\"http://www.ruralcafe.net/request\" method=\"GET\"><table width=\"80%\" cellpadding=5 border=0><tr><td><input type=\"text\" maxlength=2048 size=55 name=\"textfield\" value=\"" + lastRequest.SearchTermsOrURI() + "\"><input type=\"submit\" name=\"button\" value=\"Search\"><br>" + (filteredLuceneResults.Count + wikiResults.Count) + " results" + "</td></tr></table></form>");

                //SendMessage();//: \"" + lastRequestedPage.GetSearchTerms() + "\"<br>");

                /* JJJ: disabled queue remote request
                string lastUri = lastRequestedPage.RequestUri.Replace("search", "request");
                lastUri = lastUri + "&referrer=" + RequestUri;
                lastUri = lastUri + "&button=Queue+Request&specificity=normal&richness=low&depth=normal";
                string lastRequestedPageLink = "<a href=\"http://www.ruralcafe.net/addpage=" + 
                    lastUri + 
                    "\">[QUEUE REQUEST]</a><br>";
                SendMessage(_clientSocket, lastRequestedPageLink);
                 */

                /*
                // JJJ: Disabled query suggestions
                // suggested queries
                string relatedQueries = GetRelatedQueriesLinks(lastRequestedPage.GetSearchTerms());
                if (!relatedQueries.Contains("href")) 
                {
                    relatedQueries = "none";
                }
                SendMessage(_clientSocket, "Suggested queries:<br>" + relatedQueries);
                */

                // no local results
                if (wikiResults.Count == 0 && filteredLuceneResults.Count == 0)
                {
                    SendMessage(pageFooter);
                    return;
                }

                // wikipedia results
                if (wikiResults.Count > 0)
                {
                    SendMessage(tableHeader);

                    SendMessage("<tr><td>Wikipedia Results (Top 2 shown)</td></tr>");

                    int i = 0;
                    while (wikiResults.MoveNext() && i < 2)
                    {
                        PageInfo hit = (PageInfo)wikiResults.Current;
                        string uri = "http://en.wikipedia.org/wiki/" + hit.ToString().Replace(' ', '_');
                        string title = hit.ToString();
                        string displayUri = uri;
                        string contentSnippet = "";

                        if (uri.Length > maxLength)
                        {
                            displayUri = uri.Substring(0, maxLength) + "...";
                        }
                        if (title.Length > maxLength)
                        {
                            title = title.Substring(0, maxLength) + "...";
                        }
                        else if(title.Length == 0)
                        {
                            title = "No Title";
                        }

                        // JJJ: find content snippet here
                        //contentSnippet = 

                        resultsString = "<tr>" +
                                        "<td><a href=\"" + uri + "\">" + title + "</a><br><font color=\"green\">" + displayUri + "</font>" +
                                        "<br>" + contentSnippet + "</td>" + 
                            //"<td>" + headers + "</td>" +
                            //"<td>" + uri + "</td>" + 
                                        "</tr>";
                        SendMessage(resultsString);

                        i++;
                    }

                    SendMessage(tableFooter);

                    SendMessage("<hr>");
                }

                SendMessage(tableHeader);

                SendMessage("<tr><td>Search Results</td></tr>");

                // Local Search Results
                for (int i = 0; i < filteredLuceneResults.Count; i++)
                {
                    Lucene.Net.Documents.Document result = filteredLuceneResults.ElementAt(i);

                    string uri = result.Get("uri");
                    string title = result.Get("title");
                    string displayUri = uri;
                    string contentSnippet = "";

                    if (uri.Length > maxLength)
                    {
                        displayUri = uri.Substring(0, maxLength) + "...";
                    }
                    if (title.Length > maxLength)
                    {
                        title = title.Substring(0, maxLength) + "...";
                    }
                    else if (title.Length == 0)
                    {
                        title = "No Title";
                    }

                    // JJJ: find content snippet here
                    //contentSnippet = 

                    resultsString = "<tr>" +
                                    "<td><a href=\"" + uri + "\">" + title + "</a><br><font color=\"green\">" + displayUri + "</font>" +
                                    "<br>" + contentSnippet + "</td>" +
                        //"<td>" + headers + "</td>" +
                        //"<td>" + uri + "</td>" + 
                                    "</tr>";
                    SendMessage(resultsString);
                }

                SendMessage(tableFooter);

                SendMessage(pageFooter);
            }
        }
        string TranslateRuralCafeToLuceneQuery()
        {
            string searchTerms = _requestObject.GetRCSearchField("textfield");
            searchTerms = searchTerms.Replace('+', ' ');
            return searchTerms;
        }
        void ServeRCSearchPage(string pageUri)
        {
            string fileName = pageUri;
            int offset = pageUri.LastIndexOf('/');
            if (offset >= 0 && offset < (pageUri.Length - 1))
            {
                fileName = pageUri.Substring(offset + 1);
            }

            if (fileName.Equals(""))
            {
                ServeRCHeaderPage();
            }
            else
            {
                SendOkHeaders("text/html");
                StreamFromDiskToClient(_localProxy.RuralCafePagesPath + fileName, false);
            }
        }
        void ServeRCImagePage(string pageUri)
        {
            string fileName = pageUri;
            int offset = pageUri.LastIndexOf('/');
            if (offset >= 0 && offset < (pageUri.Length - 1))
            {
                fileName = pageUri.Substring(offset + 1);
                fileName = "images\\" + fileName;
            }

            SendOkHeaders("text/html");
            StreamFromDiskToClient(_localProxy.RuralCafePagesPath + fileName, false);
        }

        // send the page with the frameset
        void ServeRCFramesPage()
        {
            string lastRequestedPageString = GetLastRequestedPageString();

            string framesPage = "<html><head><title>RuralCafe Homepage</title></head><frameset rows=200,180,*>" +
                "<frame src=\"http://www.ruralcafe.net/requests.html\" hscrolling=\"no\" vscrolling=\"yes\" border=\"0\" name=\"links_frame\"/>" +
                "<frame src=\"http://www.ruralcafe.net/header.html\" scrolling=\"no\" border=\"0\" name=\"header_frame\"/>" +
                "<frame src=\"" + lastRequestedPageString + "\" border=\"0\" name=\"content_frame\"/>" +
                "</frameset><noframes><body><div>" +
                "Hi, your browser is really old. If you want to view this page, get a newer browser." +
                "</div></body></noframes></html>";

            SendOkHeaders("text/html");
            SendMessage(framesPage);
        }

        string GetLastRequestedPageString()
        {
            //int satisfiedRequests = _localProxy.SatisfiedRequests(_clientAddress);
            //int outstandingRequests = _localProxy.OutstandingRequests(_clientAddress);
            LocalRequest lastRequestedPage = _localProxy.GetLastRequest(_clientAddress);
            string lastRequestedPageString;
            //string lastRequestedPageLink;
            if (lastRequestedPage == null ||
                lastRequestedPage.RequestUri == _localProxy._ruralCafeSearchPage)
            {
                lastRequestedPageString = _localProxy._ruralCafeSearchPage;
            }
            else if (lastRequestedPage.IsRCLocalSearch())
            {
                lastRequestedPageString = lastRequestedPage.SearchTermsOrURI().Trim();
            }
            else
            {
                lastRequestedPageString = lastRequestedPage.RequestUri;
            }
            return lastRequestedPageString;
        }
        // send the header with the last requested page as the current url
        void ServeRCHeaderPage()
        {
            //request.GetSearchTerms()
            string headerPage = "<html><head></head>" +
                    "<body><center><br>" +
                    //"<body><center><a href=\"" + _searchPageFileName + "\" target=\"content_frame\">Ruralcafe Homepage</a>" +

                    "<form action=\"http://www.ruralcafe.net/request\" method=\"GET\" target=\"content_frame\"><table border=0 cellspacing=3 cellpadding=3>" +
                    "<tr><td colspan=2><center>" +
                    "<input type=\"text\" maxlength=2048 size=55 name=\"textfield\"><br>" +
                    "<input type=\"submit\" name=\"button\" value=\"Search\">" +
                    "<input type=\"submit\" name=\"button\" value=\"Queue Request\">" +
                    "</center></td></tr>" +

                    "<tr><td><b>Download:</b><br>" +
                    "<input type=\"radio\" name=\"richness\" value=\"low\" checked>text only<br>" +
                    "<input type=\"radio\" name=\"richness\" value=\"medium\">everything<br></td>";
            // XXX: hackery for Amrita so that the prefetch depth option is only available when the 
            // prefetching is actually available
            if (DEFAULT_DEPTH > 0)
            {
                headerPage +=
                    "<td><b>Prefetch:</b><br>" +
                    "<input type=\"radio\" name=\"depth\" value=\"normal\" checked>" + /*DEFAULT_DEPTH + */" less<br>" +
                    "<input type=\"radio\" name=\"depth\" value=\"more\">" + /*(DEFAULT_DEPTH + 1) + */" more<br></td></tr>";
            }
            else
            {
                headerPage += "</tr>";
            }
                    // XXX: disabled for study
                    //"<tr><td colspan=2><b>Confidence (search requests only):</b><br>" +
                    //"<input type=\"radio\" name=\"specificity\" value=\"normal\" checked>normal<br>" +
                    //"<input type=\"radio\" name=\"specificity\" value=\"high\" disabled>return a fewer more detailed results<br></td></tr>" +

                    headerPage += "</table></form></center></body></html>";

            SendOkHeaders("text/html");
            SendMessage(headerPage);
        }

        void ServeRCRequestsRedirect()
        {
            int status = HTTP_MOVED_PERM;
            string strReason = "";
            string str = "";

            str = "HTTP/1.1" + " " + status + " " + strReason + "\r\n" +
            "Location: http://www.ruralcafe.net/requests.html\r\n" + 
            "Content-Type: text/html\r\n" +
            "\r\n";

            SendMessage(str);
        }

        // iterate through requests and print them out in table format
        void ServeRCRequestsPage()
        {
            List<LocalRequest> requests = _localProxy.GetClientRequestQueue(_clientAddress);

            string linksPageHeader = "<html><head><meta http-equiv=\"refresh\" content=\"10\"></head>" +
                "<body><center>" +
            //<a href=\"http://www.ruralcafe.net/requests.html\">[REFRESH]</a>
                "<table width=\"80%\" border=1>" + 
                "<tr><td><b>#</b></td><td><b>Query/Page Request</b></td>" +
                "<td><b>Status/ETA</b></td><td><b><a href=\"http://www.ruralcafe.net/removeall\">[REMOVE ALL]</a></b></td></tr>";

            SendOkHeaders("text/html");
            SendMessage(linksPageHeader);
            
            string requestString = "";
            int i = 1;
            if (requests != null)
            {
                foreach (LocalRequest request in requests)
                {
                    string linkAnchorText = request.SearchTermsOrURI();
                    string linkTarget = "";
                    if (!(request.RequestUri.Contains("textfield=www.") || 
                        request.RequestUri.Contains("textfield=http://")))
                    {
                        //linkString = request.RequestUri.Replace("request", "search");
                    }
                    else
                    {
                        linkTarget = request.SearchTermsOrURI();
                        if (!linkTarget.StartsWith("http://"))
                        {
                            linkTarget = "http://" + linkTarget;
                            linkAnchorText = "http://" + linkAnchorText;
                        }
                    }
                    if (request.Satisfied)
                    {
                        
                        // XXX: if its a URL request then just drop all the ruralcafe shims
                        requestString = "<tr><td>" + i + ".</td><td><a href=\"" + 
                            linkTarget +
                            "\" target=\"content_frame\">" + linkAnchorText + "</a></td><td>" +
                            "COMPLETED" +
                            "<td>" + 
                            //"<a href=\"http://www.ruralcafe.net/expandpage=" + request.WebRequestUri + "\">" +
                            //"[EXPAND]</a>" +
                            //"<a href=\"http://www.ruralcafe.net/modifypage=" + request.WebRequestUri + "\">" +
                            //"[MODIFY]</a>" +
                            "<a href=\"http://www.ruralcafe.net/removepage=" + request.RequestUri + "\">" +
                            "[REMOVE]</a></td></tr>";
                    }
                    else if (request.Failed)
                    {
                        requestString = "<tr><td>" + i + ".</td><td>" + linkAnchorText +
                            "</td><td>" +
                            "FAILED" +
                            "<td>" + 
                            "<a href=\"http://www.ruralcafe.net/retrypage=" + request.RequestUri + "\">" +
                            "[RETRY]</a>" +
                            "<a href=\"http://www.ruralcafe.net/removepage=" + request.RequestUri + "\">" +
                            "[REMOVE]</a></td></tr>";
                    }
                    else
                    {
                        requestString = "<tr><td>" + i + ".</td><td>" + linkAnchorText +
                            "</td><td>" +
                            request.ETA +
                            "<td>" +
                            //"<a href=\"http://www.ruralcafe.net/modifypage=" + request.WebRequestUri + "\">" +
                            //"[MODIFY]</a>" +
                            "<a href=\"http://www.ruralcafe.net/removepage=" + request.RequestUri + "\">" +
                            "[REMOVE]</a></td></tr>";
                    }
                    SendMessage(requestString);
                    i++;
                }
            }

            string linksPageFooter = "</table>" +/*<a href=\"http://www.ruralcafe.net/requests.html\">[REFRESH]</a>*/ "</center></body></html>";
            SendMessage(linksPageFooter);
        }

        /*
        void ServeAddedPageRedirectToReferrer()
        {
            string str = "<html><head><meta http-equiv=\"Refresh\" content=\"0; url=\"" +
            ReferrerUri + "\"></head>" +
                "<body><p><a href=\"" + 
                ReferrerUri + "\">added requested page, redirecting back to referer page</a></p></body></html>";

            SendOkHeaders("text/html");
            SendMessage(_clientSocket, str);
        }*/

/*
        void ServeAddedRequestPage()
        {
            string str = "<html><head><meta http-equiv=\"Refresh\" content=\"0; url=http://www.ruralcafe.net/header.html\"></head>" +
                "<body><p><a href=\"http://www.ruralcafe.net/header.html\">adding requested page</a></p></body></html>";

            SendOkHeaders("text/html");
            SendMessage(_clientSocket, str);
        }

        void ServeRemovedRequestPage()
        {
            string str = "<html><head><meta http-equiv=\"Refresh\" content=\"0; url=http://www.ruralcafe.net/requests.html\"></head>" +
                "<body><p><a href=\"http://www.ruralcafe.net/requests.html\">Getting List of Requests</a></p></body></html>";

            SendOkHeaders("text/html");
            SendMessage(_clientSocket, str);
        }

        // XXX: not implemented
        void ServeModifyRequestPage()
        {
            string str = "<html><head><meta http-equiv=\"Refresh\" content=\"0; url=http://www.ruralcafe.net/requests.html\"></head>" +
                "<body><p><a href=\"http://www.ruralcafe.net/requests.html\">Getting List of Requests</a></p></body></html>";

            SendOkHeaders("text/html");
            SendMessage(_clientSocket, str);
        }

        // XXX: not implemented
        void ServeExpandRequestPage()
        {
            string str = "<html><head><meta http-equiv=\"Refresh\" content=\"0; url=requests.html\"></head>" +
                "<body><p><a href=\"requests.html\">Getting List of Requests</a></p></body></html>";

            SendOkHeaders("text/html");
            SendMessage(_clientSocket, str);
        }
        */
        #endregion

        #region Local Request Specific Helper Functions

        // gets the search terms from the search url
        public string SearchTermsOrURI()
        {
            if (IsRCLocalSearch() || IsRCRemoteQuery())
            {
                return _requestObject.GetRCSearchField("textfield").Replace('+', ' ');
            }
            return RequestUri;
        }

        void ServeRedirectPage()
        {
            string str = "HTTP/1.1" + " " + HTTP_MOVED_TEMP + " " + "adding page redirection" + "\r\n" +
            "Content-Type: text/plain" + "\r\n" +
            "Proxy-Connection: close" + "\r\n" +
            "Location: " + ReferrerUri + "\r\n" +
            "\r\n" +
            HTTP_MOVED_TEMP + " " + "adding page redirection" +
            "";
            SendMessage(str);
        }

        void Serve404Page(bool withQueueLink)
        {
            // page header
            SendOkHeaders("text/html");
            SendMessage("<html><head></head><body><center>");

            string errorMessage = "404 error - page not found";
            LocalRequest lastRequestedPage = _localProxy.GetLastRequest(_clientAddress);
            /*
            if (lastRequestedPage != null)
            {
                errorMessage = "404 error: \"" + lastRequestedPage.RequestUri + "\" not found<br>";
            }*/

            if (withQueueLink)
            {
                errorMessage += "<a href=\"http://www.ruralcafe.net/addpage=" +
                                "http://www.ruralcafe.net/request?textfield=" +
                                lastRequestedPage.RequestUri +
                                "&referrer=" + RequestUri +
                                "&button=Queue+URL+Request&richness=high" +
                                "\">[QUEUE PAGE]</a><br>";
            }
            SendMessage(errorMessage);

            // page footer
            SendMessage("</center></body></html>");
        }

        #endregion
    }
}
