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
using System.Collections.Specialized;
using BzReader;

namespace RuralCafe
{
    /// <summary>
    /// Request handler for requests coming in to the local proxy.
    /// </summary>
    public class LocalRequestHandler : RequestHandler
    {
        private static int _nextId = 1;

        // localProxy
        //new private RCLocalProxy _proxy;

        public static int DEFAULT_QUOTA;
        public static int DEFAULT_DEPTH; 

        /// <summary>
        /// Constructor for a local proxy's request handler.
        /// </summary>
        /// <param name="proxy">Proxy this request handler belongs to.</param>
        /// <param name="socket">Client socket.</param>
        public LocalRequestHandler(RCLocalProxy proxy, Socket socket)
            : base(proxy, socket)
        {
            _ID = _nextId++;
            _requestTimeout = REQUEST_PACKAGE_DEFAULT_TIMEOUT;
        }
        /// <summary>
        /// DUMMY used for request matching.
        /// Not the cleanest implementation need to instantiate a whole object just to match
        /// </summary> 
        private LocalRequestHandler(RCLocalProxy proxy, Socket socket, string uri)
            : this(proxy, socket)
        {
            if (!Util.IsValidUri(uri))
            {
                // XXX: do nothing
            }
            _rcRequest = new RCRequest(this, uri);

            /* XXX: don't think the dummy needs this
            // setup the header variables
            if (IsRuralCafeLocalSearch() || IsRuralCafeRemoteRequest())
            {
                ParseRuralCafeQuery();
            }
             */
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
        public override int HandleRequest()
        {
            if (IsRCRequest())
            {
                return ServeRCPage();
            }

            if (IsGoogleResultLink())
            {
                TranslateGoogleResultLink();
            }

            if (IsBlacklisted(RequestUri))
            {
                LogDebug("ignoring blacklisted: " + RequestUri);
                SendErrorPage(HTTP_NOT_FOUND, "ignoring blacklisted", RequestUri);
                return (int)Status.NotFound;
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
            if (!IsGetOrHeadHeader() || !IsCacheable())
            {
                LogDebug("streaming: " + RequestUri + " to client.");

                long bytesSent = StreamTransparently();
                _rcRequest.FileSize = bytesSent;

                return (int)Status.StreamedTransparently;
            }

            // set the last requested page for redirects
            ((RCLocalProxy)_proxy).SetLastRequest(_clientAddress, this);

            if (IsCached(_rcRequest.CacheFileName))
            {
                LogDebug("sending: " + _rcRequest.GenericWebRequest.RequestUri + " from cache.");
                
                // get the content type if its a Google proxied search request
                string contentType = "";
                if (RequestUri.Contains("http://www.google.com/search?"))
                {
                    contentType = "text/html";
                }
                else
                {
                    // Try getting the mime type from the search index
                    contentType = GetMimeType(RequestUri);
                }

                // try getting the content type from the file extension
                if (contentType.Equals("text/unknown"))
                    contentType = Util.GetContentTypeOfFile(_rcRequest.CacheFileName);

                SendOkHeaders(contentType);
                _rcRequest.FileSize = StreamFromCacheToClient(_rcRequest.CacheFileName, _rcRequest.IsCompressed());
                if (_rcRequest.FileSize < 0)
                {
                    return (int)Status.Failed;
                }
                return (int)Status.Cached;
            }

            // XXX: not sure if this should even be here, technically for a cacheable file that's not cached, this is
            // XXX: behaving like a synchronous proxy
            // XXX: this is fine if we're online, fine if we're offlin since it'll fail.. but doesn't degrade gradually

            // XXX: response time could be improved here if it downloads and streams to the client at the same time
            // XXX: basically, merge the DownloadtoCache() and StreamfromcachetoClient() methods into a new third method.
            // cacheable but not cached, cache it, then send to client if there is no remote proxy
            if (((RCLocalProxy)_proxy).RemoteProxy == null)
            {
                LogDebug("streaming: " + _rcRequest.GenericWebRequest.RequestUri + " to cache and client.");
                _rcRequest.GenericWebRequest.Proxy = null;
                long bytesDownloaded = _rcRequest.DownloadToCache();
                try
                {
                    FileInfo f = new FileInfo(_rcRequest.CacheFileName);
                    if (bytesDownloaded > -1 && f.Exists)
                    {
                        _rcRequest.FileSize = StreamFromCacheToClient(_rcRequest.CacheFileName, _rcRequest.IsCompressed());
                        if (_rcRequest.FileSize < 0)
                        {
                            return (int)Status.Failed;
                        }
                        return (int)Status.StreamedTransparently;
                    }
                }
                catch
                {
                    // do nothing
                }
            }

            SendErrorPage(HTTP_NOT_FOUND, "page not found", RequestUri);

            return (int)Status.NotFound;
        }


        #region RC display and handling Methods

        /// <summary>
        /// Serve the RuralCafe page.
        /// XXX: this entire group of RC display/handling methods will most likely all be obsoleted in the next major revision.
        /// </summary>
        /// <returns>Request status.</returns>
        private int ServeRCPage()
        {
            if (IsIndex())
            {
                try
                {
                    ServeRCIndexPage();
                }
                catch (Exception)
                {
                    return (int)Status.Failed;
                }
                return (int)Status.Completed;
            }

            if (IsResult())
            {
                try
                {
                    ServeRCResultPage();
                }
                catch (Exception)
                {
                    return (int)Status.Failed;
                }
                return (int)Status.Completed;
            }

            if (IsQueue())
            {
                try
                {
                    ServeRCQueuePage();
                }
                catch (Exception)
                {
                    return (int)Status.Failed;
                }
                return (int)Status.Completed;
            }

            if (IsRemovePage())
            {
                try
                {
                    //LogDebug("removing request from queues " + RequestUri);
                    DequeueRequest();
                    ServeRCRequestsRedirect();
                }
                catch (Exception)
                {
                    return (int)Status.Failed;
                }
                return (int)Status.Completed;
            }

            if (IsRemoveAllPage())
            {
                //LogDebug("removing all requests from queues");
                try
                {
                    ((RCLocalProxy)_proxy).ClearRequestQueues(_clientAddress);
                    ServeRCRequestsRedirect();
                }
                catch (Exception)
                {
                    return (int)Status.Failed;
                }
                return (int)Status.Completed;
            }

            // XXX: this code may be obsolete
            if (IsAddPage() || IsRetryPage())
            {
                // extract the actual requested URI
                int offset = RequestUri.IndexOf('=');
                string requestedUri = RequestUri.Substring(offset + 1);
                RequestUri = requestedUri;
                // rebuild the RCRequest and parse the parameters
                // (instead of making a new object)
                _rcRequest = new RCRequest(this, requestedUri, _rcRequest.RefererUri);
                _rcRequest.ParseRCSearchFields();

                // queue the request
                string queryString = _rcRequest.GetRCSearchField("textfield");
                if (queryString.Trim().Length > 0)
                {
                    QueueRequest();
                }

                // special case where we want to send the user back to the search results page
                RefererUri = _rcRequest.GetRCSearchField("referrer");
                if (RefererUri != null && !RefererUri.Equals(""))
                {
                    // can't serve the lucene results directly, since it wasn't requested it will
                    // screw up the refererUri we have to redirect back to the page via an intermediate
                    ServeRedirectPage();
                }
                else
                {
                    SendErrorPage(HTTP_NOT_FOUND, "no RefererUri found after adding page", "");
                }

                return (int)Status.Completed;
            }

            if (IsRCHomePage())
            {
                // JAY: disabling of any queuing for download... this is a bit messy, but the idea is that
                // JAY: the UI is streamlined for absolutely zero remote updates, will be changed with new UI overhaul.
                if (((RCLocalProxy)_proxy).RCSearchPage.Equals("http://www.ruralcafe.net/cip.html"))
                {
                    ServeRCSearchPage(((RCLocalProxy)_proxy).RCSearchPage);
                }
                else
                {
                    ServeRCFrames();
                }

                return (int)Status.Completed;
            }

            if (IsRCHeaderPage())
            {
                ServeRCHeader();

                return (int)Status.Completed;
            }

            if (IsRCRequestsPage())
            {
                ServeRCRequestsPage();

                return (int)Status.Completed;
            }

            if (IsRCImagePage())
            {
                ServeRCImage(RequestUri);

                return (int)Status.Completed;
            }

            if (IsRCLocalSearch())
            {
                LogDebug("serving RuralCafe results page");

                ((RCLocalProxy)_proxy).SetLastRequest(_clientAddress, this);

                string queryString = _rcRequest.GetRCSearchField("textfield");
                if (queryString.Trim().Length > 0)
                {
                    ServeRCResultsPage(queryString);
                }
                else
                {
                    ServeRCSearchPage(((RCLocalProxy)_proxy).RCSearchPage);
                }

                return (int)Status.Completed;
            }

            if (IsRCRemoteQuery())
            {
                LogDebug("queuing RuralCafe remote request");

                string requestString = _rcRequest.GetRCSearchField("textfield");
                if (requestString.Trim().Length > 0)
                {
                    QueueRequest();
                }

                /* XXX: not serving random stuff during a queue request
                LocalRequestHandler latestRequest = ((RCLocalProxy)_proxy).GetLatestRequest(_clientAddress);
                if (latestRequest != null)
                {
                    requestString = latestRequest.SearchTermsOrURI();
                    if (IsRCLocalSearch() || IsRCRemoteQuery())
                    {
                        ServeRCResultsPage(requestString);
                    }
                    else
                    {
                        StreamFromCacheToClient(latestRequest.CacheFileName, latestRequest.IsCompressed());
                    }
                }
                */
                return (int)Status.Completed;
            }

            SendErrorPage(HTTP_NOT_FOUND, "page does not exist", RequestUri);
            return (int)RequestHandler.Status.NotFound;
        }


        /// <summary>Checks if the request is for the RuralCafe command to get the index page.</summary>
        bool IsIndex()
        {
            if (RequestUri.StartsWith("http://www.ruralcafe.net/request/index.xml"))
            {
                return true;
            }
            return false;
        }
        /// <summary>Checks if the request is for the RuralCafe command to get the queue.</summary>
        bool IsQueue()
        {
            if (RequestUri.StartsWith("http://www.ruralcafe.net/request/queue.xml"))
            {
                return true;
            }
            return false;
        }
        /// <summary>Checks if the request is for the RuralCafe command to get the search results.</summary>
        bool IsResult()
        {
            if (RequestUri.StartsWith("http://www.ruralcafe.net/request/result.xml"))
            {
                return true;
            }
            return false;
        }


        /// <summary>Checks if the request is for the RuralCafe homepage.</summary>
        private bool IsRCHomePage()
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
        /// <summary>Checks if the request is for the RuralCafe header page.</summary>
        private bool IsRCHeaderPage()
        {
            if (RequestUri.Equals("http://www.ruralcafe.net/header.html") ||
                RequestUri.Equals("www.ruralcafe.net/header.html"))
            {
                return true;
            }
            return false;
        }
        /// <summary>Checks if the request is for the RuralCafe requests page.</summary>
        private bool IsRCRequestsPage()
        {
            if (RequestUri.Equals("http://www.ruralcafe.net/requests.html") ||
                RequestUri.Equals("www.ruralcafe.net/requests.html"))
            {
                return true;
            }
            return false;
        }
        /// <summary>Checks if the request is for the RuralCafe image page.</summary>
        bool IsRCImagePage()
        {
            if (RequestUri.Contains("www.ruralcafe.net") &&
                    RequestUri.EndsWith(".gif"))
            {
                return true;
            }
            return false;
        }
        /// <summary>Checks if the request is for the RuralCafe command to add a URI.</summary>
        bool IsAddPage()
        {
            if (RequestUri.StartsWith("http://www.ruralcafe.net/addpage="))
            {
                return true;
            } 
            return false;
        }
        /// <summary>Checks if the request is for the RuralCafe command to remove a URI.</summary>
        bool IsRemovePage()
        {
            if (RequestUri.StartsWith("http://www.ruralcafe.net/removepage="))
            {
                return true;
            }
            return false;
        } 
        /// <summary>Checks if the request is for the RuralCafe command to remove all URIs for a client.</summary>
        bool IsRemoveAllPage()
        {
            if (RequestUri.StartsWith("http://www.ruralcafe.net/removeall"))
            {
                return true;
            }
            return false;
        }
        /// <summary>Checks if the request is for the RuralCafe command to retry a URI.</summary>
        bool IsRetryPage()
        {
            if (RequestUri.StartsWith("http://www.ruralcafe.net/retrypage="))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Sends the RC Index page to the client.
        /// GET request will be sent to request/index.xml?c=6&n=4&s=root where
        /// c is the number of categories, the number of <category> required
        /// n is the maximum number of items in a category, the number of <item> allowed
        /// s is the upper level category which the user want to explore (the top level category is defined as 'root')
        /// </summary>
        void ServeRCIndexPage()
        {
            // Parse parameters
            NameValueCollection qscoll = HttpUtility.ParseQueryString(RequestUri);
            int numItems = Int32.Parse(qscoll.Get("n"));
            int pageNumber = Int32.Parse(qscoll.Get("p"));
            string searchString = qscoll.Get("s");

            SendOkHeaders("text/xml");
            // not building the xml file yet, just sending the dummy page for now since there's really no top categories query yet
            // for the cache path
            StreamFromCacheToClient(((RCLocalProxy)_proxy).UIPagesPath + "index.xml", false);
        }

        /// <summary>
        /// Sends the frame page to the client.
        /// xml file of reuqests in the queue, content will be displayed in frame-offline-login.html
        /// Please organize the item in chronological order (older items first)
        /// GET request will be sent to request/queue.xml?u=a01&v=24-05-2012 where
        /// u is the user id
        /// v is used to specify date (in format of dd-mm-yyyy), or month (in format of mm-yyyy), or all (v=0). 
        /// If t is set to dd-mm-yyyy or mm-yyyy, only the requests submitted during that day/month will be returned. 
        /// If v is set to 0, all requests submitted by the user should be returned.
        /// </summary>
        void ServeRCQueuePage()
        {
            // Parse parameters
            NameValueCollection qscoll = HttpUtility.ParseQueryString(RequestUri);
            int userId = Int32.Parse(qscoll.Get("u"));
            string date = qscoll.Get("v");

            List<LocalRequestHandler> requestHandlers = ((RCLocalProxy)_proxy).GetRequests(_clientAddress);

            string queuePageString = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + "<queue>";
            int i = 1;
            if (requestHandlers != null)
            {
                foreach (LocalRequestHandler requestHandler in requestHandlers)
                {
                    string itemId = "" + requestHandler.ID;
                    string linkAnchorText = requestHandler.SearchTermsOrURI();
                    string linkTarget = requestHandler.RequestUri;
                    string statusString = "";

                    // if its a search request, translate to the google version to get the remotely returned google results
                    if (linkAnchorText.StartsWith("http://"))
                    {
                        linkTarget = linkAnchorText;
                    }
                    else
                    {
                        linkTarget = requestHandler.RCRequest.TranslateRCSearchToGoogle();
                    }

                    /*
                    Failed = -1,
                    Received = 0,
                    Requested = 1,
                    Completed = 2,
                    Cached = 3,             
                    NotCacheable = 4,
                    NotFound = 5,
                    StreamedTransparently = 6,
                    Ignored = 7
                     */
                    if (requestHandler.RequestStatus == (int)Status.Failed)
                    {
                        statusString = "Failed";
                    }
                    else if (requestHandler.RequestStatus == (int)Status.Received)
                    {
                        statusString = "Received";
                    }
                    else if (requestHandler.RequestStatus == (int)Status.Requested)
                    {
                        statusString = "Requested";
                    }
                    else if (requestHandler.RequestStatus == (int)Status.Completed)
                    {
                        statusString = "Completed";
                    }
                    else if (requestHandler.RequestStatus == (int)Status.Cached)
                    {
                        statusString = "Cached";
                    }
                    else if (requestHandler.RequestStatus == (int)Status.NotCacheable)
                    {
                        statusString = "NotCacheable";
                    }
                    else if (requestHandler.RequestStatus == (int)Status.NotFound)
                    {
                        statusString = "NotFound";
                    }
                    else if (requestHandler.RequestStatus == (int)Status.StreamedTransparently)
                    {
                        statusString = "StreamedTransparently";
                    }
                    else if (requestHandler.RequestStatus == (int)Status.Ignored)
                    {
                        statusString = "Ignored";
                    }

                    // build the actual element
                    queuePageString = queuePageString +
                                    "<item id=\"" + itemId + "\">" +
                                        "<title>" + linkAnchorText + "</title>" +
                                        "<url>" + linkTarget + "</url>" +
                                        "<status>" + statusString + "</status>" +
                                        "<size>" + "unknown" + "</size>" +
                                    "</item>";
                    i++;
                }
            }

            queuePageString = queuePageString + "<queue>";
            SendOkHeaders("text/xml");
            SendMessage(queuePageString);
        }

        /// <summary>
        /// Sends the frame page to the client.
        /// GET request will be sent to request/result.xml?n=5&p=1&s=searchstring where
        /// n is the maximum number of items per page, the number of <item> allowed in this file
        /// p is the current page number, if there are multipage pages, page number starts from 1, 2, 3...,
        /// s is the search query string
        /// </summary>
        void ServeRCResultPage()
        {
            // Parse parameters
            NameValueCollection qscoll = HttpUtility.ParseQueryString(RequestUri);
            int numItemsPerPage = Int32.Parse(qscoll.Get("n"));
            int pageNumber = Int32.Parse(qscoll.Get("p"));
            string queryString = qscoll.Get("s");

            string resultsString = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>";

            List<Lucene.Net.Documents.Document> filteredLuceneResults = new List<Lucene.Net.Documents.Document>();
            HitCollection wikiResults = new HitCollection();
            int currentItemNumber = 1;
            if (queryString.Trim().Length > 0)
            {
                // Query the Wiki index
                wikiResults = Indexer.Search(queryString, RCLocalProxy.WikiIndices.Values, Indexer.MAX_SEARCH_HITS);

                // Query our RuralCafe index
                List<Lucene.Net.Documents.Document> luceneResults = IndexWrapper.Query(((RCLocalProxy)_proxy).IndexPath, queryString);

                //List<Lucene.Net.Documents.Document> luceneResults = new List<Lucene.Net.Documents.Document>();             
                // remove duplicates
                foreach (Lucene.Net.Documents.Document document in luceneResults)
                {
                    string documentUri = document.Get("uri");
                    string documentTitle = document.Get("title");

                    // ignore blacklisted domains
                    if (IsBlacklisted(documentUri))
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
                        if ((currentItemNumber > ((pageNumber - 1) * numItemsPerPage)) &&
                            currentItemNumber <= (pageNumber * numItemsPerPage))
                        {
                            filteredLuceneResults.Add(document);
                        }
                        currentItemNumber++;
                    }

                }
            }

            LogDebug(filteredLuceneResults.Count + " results");

            resultsString = resultsString + "<search total=\"" + filteredLuceneResults.Count + "\">";
            // Local Search Results
            for (int i = 0; i < filteredLuceneResults.Count; i++)
            {
                Lucene.Net.Documents.Document result = filteredLuceneResults.ElementAt(i);

                string uri = result.Get("uri");
                string title = result.Get("title");
                string displayUri = uri;
                string contentSnippet = "";

                /*
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
                }*/

                // JAY: find content snippet here
                //contentSnippet = 

                resultsString = resultsString +
                                "<item>" +
                                "<title>" + title + "</title>" +
                                "<url>" + uri + "</url>" +
                                "<snippet>" + contentSnippet + "</snippet>" +
                                "</item>";
            }

            resultsString = resultsString + "</search>";

            SendOkHeaders("text/xml");
            SendMessage(resultsString);
        }

        /// <summary>Sends the frame page to the client.</summary>
        void ServeRCFrames()
        {
            string latestRequest = GetLatestRequestAsString();

            string framesPage = "<html><head><title>RuralCafe Homepage</title></head><frameset rows=200,180,*>" +
                "<frame src=\"http://www.ruralcafe.net/requests.html\" hscrolling=\"no\" vscrolling=\"yes\" border=\"0\" name=\"links_frame\"/>" +
                "<frame src=\"http://www.ruralcafe.net/header.html\" scrolling=\"no\" border=\"0\" name=\"header_frame\"/>" +
                "<frame src=\"" + latestRequest + "\" border=\"0\" name=\"content_frame\"/>" +
                "</frameset><noframes><body><div>" +
                "Hi, your browser is really old. If you want to view this page, get a newer browser." +
                "</div></body></noframes></html>";

            SendOkHeaders("text/html");
            SendMessage(framesPage);
        }
        /// <summary>Sends the header page to the client.</summary>
        void ServeRCHeader()
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
        /// <summary>Sends the requests page to the client.</summary>
        private void ServeRCRequestsPage()
        {
            List<LocalRequestHandler> requestHandlers = ((RCLocalProxy)_proxy).GetRequests(_clientAddress);

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
            if (requestHandlers != null)
            {
                foreach (LocalRequestHandler requestHandler in requestHandlers)
                {
                    string linkAnchorText = requestHandler.SearchTermsOrURI();
                    string linkTarget = "";
                    /* JAY: XXX: Think this code was from a long time ago when we were converting google requests, depricated for now.
                    if (!(requestHandler.RequestUri.Contains("textfield=www.") ||
                        requestHandler.RequestUri.Contains("textfield=http://")))
                    {
                        //linkString = request.RequestUri.Replace("request", "search");
                    }
                    else
                    {*/
                    //linkAnchorText = requestHandler.SearchTermsOrURI();
                    linkTarget = requestHandler.RequestUri;

                    // if its a search request, translate to the google version to get the remotely returned google results
                    if (linkAnchorText.StartsWith("http://"))
                    {
                        linkTarget = linkAnchorText;
                    }
                    else
                    {
                        linkTarget = requestHandler.RCRequest.TranslateRCSearchToGoogle();
                    }
                    //    linkAnchorText = "http://" + linkAnchorText;
                    //}
                    //}
                    if (requestHandler.RequestStatus == (int)Status.Completed)
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
                            "<a href=\"http://www.ruralcafe.net/removepage=" + requestHandler.RequestUri + "\">" +
                            "[REMOVE]</a></td></tr>";
                    }
                    else if (requestHandler.RequestStatus == (int)Status.Failed)
                    {
                        requestString = "<tr><td>" + i + ".</td><td>" + linkAnchorText +
                            "</td><td>" +
                            "FAILED" +
                            "<td>" +
                            "<a href=\"http://www.ruralcafe.net/retrypage=" + requestHandler.RequestUri + "\">" +
                            "[RETRY]</a>" +
                            "<a href=\"http://www.ruralcafe.net/removepage=" + requestHandler.RequestUri + "\">" +
                            "[REMOVE]</a></td></tr>";
                    }
                    else
                    {
                        requestString = "<tr><td>" + i + ".</td><td>" + linkAnchorText +
                            "</td><td>" +
                            requestHandler.PrintableETA() +
                            "<td>" +
                            //"<a href=\"http://www.ruralcafe.net/modifypage=" + request.WebRequestUri + "\">" +
                            //"[MODIFY]</a>" +
                            "<a href=\"http://www.ruralcafe.net/removepage=" + requestHandler.RequestUri + "\">" +
                            "[REMOVE]</a></td></tr>";
                    }
                    SendMessage(requestString);
                    i++;
                }
            }

            string linksPageFooter = "</table>" +/*<a href=\"http://www.ruralcafe.net/requests.html\">[REFRESH]</a>*/ "</center></body></html>";
            SendMessage(linksPageFooter);
        }

        /// <summary>Serves the RuralCafe search page.</summary>
        private void ServeRCSearchPage(string pageUri)
        {
            string fileName = pageUri;
            int offset = pageUri.LastIndexOf('/');
            if (offset >= 0 && offset < (pageUri.Length - 1))
            {
                fileName = pageUri.Substring(offset + 1);
			}

            if (fileName.Equals(""))
            {
                ServeRCHeader();
            }
            else
            {
                SendOkHeaders("text/html");
                StreamFromCacheToClient(((RCLocalProxy)_proxy).UIPagesPath + fileName, false);
            }
        }
        /// <summary>Query Lucene index of local pages and serve the results.</summary>
        private void ServeRCResultsPage(string queryString)
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
            if (queryString.Trim().Length > 0)
            {
                // Query the Wiki index
                wikiResults = Indexer.Search(queryString, RCLocalProxy.WikiIndices.Values, Indexer.MAX_SEARCH_HITS);

                // Query our RuralCafe index
                List<Lucene.Net.Documents.Document> luceneResults = IndexWrapper.Query(((RCLocalProxy)_proxy).IndexPath, queryString);
				
  				//List<Lucene.Net.Documents.Document> luceneResults = new List<Lucene.Net.Documents.Document>();             
				// remove duplicates
                foreach (Lucene.Net.Documents.Document document in luceneResults)
                {
                    string documentUri = document.Get("uri");
                    string documentTitle = document.Get("title");

                    // ignore blacklisted domains
                    if (IsBlacklisted(documentUri))
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

            LocalRequestHandler latestRequestHandler = ((RCLocalProxy)_proxy).GetLatestRequest(_clientAddress);
            // last requested page
            if (latestRequestHandler != null)
            {
                SendMessage("<form action=\"http://www.ruralcafe.net/request\" method=\"GET\"><table width=\"80%\" cellpadding=5 border=0><tr><td><input type=\"text\" maxlength=2048 size=55 name=\"textfield\" value=\"" + latestRequestHandler.SearchTermsOrURI() + "\"><input type=\"submit\" name=\"button\" value=\"Search\"><br>" + (filteredLuceneResults.Count + wikiResults.Count) + " results" + "</td></tr></table></form>");

                //SendMessage();//: \"" + lastRequestedPage.GetSearchTerms() + "\"<br>");

                /* JAY: disabled queue remote request
                string lastUri = lastRequestedPage.RequestUri.Replace("search", "request");
                lastUri = lastUri + "&referrer=" + RequestUri;
                lastUri = lastUri + "&button=Queue+Request&specificity=normal&richness=low&depth=normal";
                string lastRequestedPageLink = "<a href=\"http://www.ruralcafe.net/addpage=" + 
                    lastUri + 
                    "\">[QUEUE REQUEST]</a><br>";
                SendMessage(_clientSocket, lastRequestedPageLink);
                 */

                /*
                // JAY: Disabled query suggestions
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
                        else if (title.Length == 0)
                        {
                            title = "No Title";
                        }

                        // JAY: find content snippet here
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

                    // JAY: find content snippet here
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
        /// <summary>Helper to translate RuralCafe to Lucene query format.</summary>
        private string TranslateRuralCafeToLuceneQuery()
        {
            string searchTerms = _rcRequest.GetRCSearchField("textfield");
            searchTerms = searchTerms.Replace('+', ' ');
            return searchTerms;
        }
        /// <summary>Sends image to the client.</summary>
        private void ServeRCImage(string pageUri)
        {
            string fileName = pageUri;
            int offset = pageUri.LastIndexOf('/');
            if (offset >= 0 && offset < (pageUri.Length - 1))
            {
                fileName = pageUri.Substring(offset + 1);
                fileName = "images"+Path.DirectorySeparatorChar + fileName;
            }

            SendOkHeaders("text/html");
            StreamFromCacheToClient(((RCLocalProxy)_proxy).UIPagesPath + fileName, false);
        }
        /// <summary>Sends the redirect page to the client.</summary>
        void ServeRedirectPage()
        {
            string str = "HTTP/1.1" + " " + HTTP_MOVED_TEMP + " " + "adding page redirection" + "\r\n" +
            "Content-Type: text/plain" + "\r\n" +
            "Proxy-Connection: close" + "\r\n" +
            "Location: " + RefererUri + "\r\n" +
            "\r\n" +
            HTTP_MOVED_TEMP + " " + "adding page redirection" +
            "";
            SendMessage(str);
        }
        /// <summary>Sends the redirect page to the client.</summary>
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
        /// <summary>Helper to get the latest request as a string.</summary>
        string GetLatestRequestAsString()
        {
            //int satisfiedRequests = _localProxy.SatisfiedRequests(_clientAddress);
            //int outstandingRequests = _localProxy.OutstandingRequests(_clientAddress);
            LocalRequestHandler latestRequestHandler = ((RCLocalProxy)_proxy).GetLatestRequest(_clientAddress);
            string latestRequestString;
            //string lastRequestedPageLink;
            if (latestRequestHandler == null ||
                latestRequestHandler.RequestUri == ((RCLocalProxy)_proxy).RCSearchPage)
            {
                latestRequestString = ((RCLocalProxy)_proxy).RCSearchPage;
            }
            else if (latestRequestHandler.IsRCLocalSearch())
            {
                latestRequestString = latestRequestHandler.SearchTermsOrURI().Trim();
            }
            else
            {
                latestRequestString = latestRequestHandler.RequestUri;
            }
            return latestRequestString;
        }
        /// <summary>Gets the search terms from the search URI for display</summary>
        private string SearchTermsOrURI()
        {
            if (IsRCLocalSearch() || IsRCRemoteQuery())
            {
                return _rcRequest.GetRCSearchField("textfield").Replace('+', ' ');
            }
            return RequestUri;
        }

        #endregion


        #region Queue Management Methods

        /// <summary>
        /// Queues this request.
        /// </summary>
        private void QueueRequest()
        {
            ((RCLocalProxy)_proxy).QueueRequest(_clientAddress, this);
        }

        /// <summary>
        /// Removes the request from Ruralcafe's queue.
        /// </summary>
        private void DequeueRequest()
        {
            // clean up the Ruralcafe info, remove it
            int index = RequestUri.IndexOf('=');
            string matchingUri = RequestUri.Substring(index + 1);
            LocalRequestHandler matchingRequestHandler = new LocalRequestHandler((RCLocalProxy)_proxy, _clientSocket, matchingUri);
            ((RCLocalProxy)_proxy).DequeueRequest(_clientAddress, matchingRequestHandler);
        }

        /// <summary>
        /// Helper method to get the ETA from the proxy this handler belongs to.
        /// </summary>
        /// <returns>ETA as a printable string.</returns>
        private string PrintableETA()
        {
            int eta = ((RCLocalProxy)_proxy).ETA(this);
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

        #endregion

        #region Google Translation Fun

        private bool IsGoogleResultLink()
        {
            if (RequestUri.StartsWith("http://www.google.com/url?q="))
            {
                return true;
            }
            return false;
        }

        private void TranslateGoogleResultLink()
        {
            string newLinkUri = RequestUri.Replace("http://www.google.com/url?q=", "");
            int stopIndex = newLinkUri.IndexOf("&");
            newLinkUri = newLinkUri.Substring(0, stopIndex);
            RequestUri = newLinkUri;

            string fileName = RCRequest.UriToFilePath(RequestUri);
            HashedFileName = RCRequest.HashedFilePath(fileName) + fileName;
            CacheFileName = Proxy.CachePath + HashedFileName;
        }

        #endregion

        #region Serve Wikipedia Page Methods

        /// <summary>
        /// Checks if the request is in the wikipedia cache.
        /// </summary>
        /// <returns>True or false if the request is in the wiki cache or not.</returns>
        private bool IsInWikiCache()
        {
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
        private int ServeWikiURI()
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
                return (int)Status.Failed;
            }
            return (int)Status.Completed;
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


        /// <summary>
        /// Stream the file from the cache to the client.
        /// </summary>
        /// <param name="decompress">Try to decompress then serve this file.</param>
        /// <param name="fileName">Name of the file to stream to the client.</param>
        /// <returns>Bytes streamed from the cache to the client.</returns>
        protected long StreamFromCacheToClient(string fileName, bool decompress)
        {
            long bytesSent = 0;

            // make sure the file exists.
            FileInfo f;
            try
            {
                f = new FileInfo(fileName);
                if (!f.Exists)
                {
                    LogDebug("error file doesn't exist: " + fileName);
                    return -1;
                }
            }
            catch (Exception e)
            {
                LogDebug("problem getting file info: " + fileName + " " + e.Message);
                return -1;
            }

            FileStream fs = null;
            try
            {
                // XXX: waste of computation, not a good tradeoff
                if (decompress)
                {
                    MemoryStream decompressedMs = Util.BZ2DecompressFile(fileName);
                    int streamLength = (int)decompressedMs.Length;
                    //byte[] decompressionBuf = new byte[length];
                    //decompressedMs.Read(decompressionBuf, 0, length);
                    _clientSocket.Send(decompressedMs.GetBuffer(), streamLength, 0); // XXX: this is an ugly hack, but max filesize is 32MB
                    bytesSent = streamLength;
                }
                else
                {
                    int offset = 0;
                    byte[] buffer = new byte[32]; // magic number 32
                    fs = f.Open(FileMode.Open, FileAccess.Read);
                    int bytesRead = fs.Read(buffer, 0, 32);
                    while (bytesRead > 0)
                    {
                        _clientSocket.Send(buffer, bytesRead, 0);
                        bytesSent += bytesRead;

                        bytesRead = fs.Read(buffer, 0, 32);

                        offset += bytesRead;
                    }
                }
            }
            catch (Exception e)
            {
                SendErrorPage(HTTP_NOT_FOUND, "problem serving from RuralCafe cache: ", e.Message);
                bytesSent = -1;
            }
            finally
            {
                if (fs != null)
                {
                    fs.Close();
                }

            }
            return bytesSent;
        }

        /// <summary>
        /// Stream the request to the server and the response back to the client transparently.
        /// XXX: does not have gateway support or tunnel to remote proxy support
        /// </summary>
        /// <returns>The length of the streamed result.</returns>
        protected long StreamTransparently()
        {
            long bytesSent = 0;
            string clientRequest = _rcRequest._recvString;
            Encoding ASCII = Encoding.ASCII;
            Byte[] byteGetString = ASCII.GetBytes(clientRequest);
            Byte[] receiveByte = new Byte[256];
            Socket socket = null;

            // establish the connection to the server
            try
            {
                string hostName = GetHeaderValue(clientRequest, "Host");
                IPHostEntry ipEntry = Dns.GetHostEntry(hostName);
                IPAddress[] addr = ipEntry.AddressList;

                IPEndPoint ip = new IPEndPoint(addr[0], 80);
                socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                socket.Connect(ip);
            }
            catch (SocketException)
            {
                // do nothing
                return -1;
            }

            // send the request, get the response, and transparently send it to the client
            socket.Send(byteGetString, byteGetString.Length, 0);
            Int32 bytesRead = socket.Receive(receiveByte, receiveByte.Length, 0);
            _clientSocket.Send(receiveByte, bytesRead, 0);
            bytesSent += bytesRead;

            // continue to stream the data
            while (bytesRead > 0)
            {
                bytesRead = socket.Receive(receiveByte, receiveByte.Length, 0);

                // check speed limit
                while (!((RCLocalProxy)_proxy).HasDownlinkBandwidth(bytesRead))
                {
                    Thread.Sleep(100);
                }
                _clientSocket.Send(receiveByte, bytesRead, 0);
                bytesSent += bytesRead;
            }
            socket.Close();

            return bytesSent;
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
    }
}
