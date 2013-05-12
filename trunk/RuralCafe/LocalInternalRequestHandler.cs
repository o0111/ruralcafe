using BzReader;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml;

namespace RuralCafe
{
    /// <summary>
    /// Handles internal requests. These are e.g. queue, richness, remove, etc.
    /// </summary>
    public class LocalInternalRequestHandler : RequestHandler
    {
        /// <summary>
        /// Constructor for a local internal proxy's request handler.
        /// </summary>
        /// <param name="proxy">Proxy this request handler belongs to.</param>
        /// <param name="socket">Client socket.</param>
        public LocalInternalRequestHandler(RCLocalProxy proxy, Socket socket)
            : base(proxy, socket)
        {
            _requestId = _proxy.NextRequestId;
            _proxy.NextRequestId = _proxy.NextRequestId + 1;
            _requestTimeout = LOCAL_REQUEST_PACKAGE_DEFAULT_TIMEOUT;
        }

        #region RC display and handling Methods

        /// <summary>
        /// Handles an RC internal request.
        /// </summary>
        /// <returns>The status.</returns>
        public override Status HandleRequest()
        {
            if (IsIndex())
            {
                try
                {
                    ServeRCIndexPage();
                }
                catch (Exception)
                {
                    return RequestHandler.Status.Failed;
                }
                return RequestHandler.Status.Completed;
            }

            if (IsRemoteResult())
            {
                try
                {
                    ServeRCRemoteResultPage();
                }
                catch (Exception)
                {
                    return RequestHandler.Status.Failed;
                }
                return RequestHandler.Status.Completed;
            }

            if (IsResult())
            {
                try
                {
                    ServeRCResultPage();
                }
                catch (Exception)
                {
                    return RequestHandler.Status.Failed;
                }
                return RequestHandler.Status.Completed;
            }

            if (IsQueue())
            {
                try
                {
                    ServeRCQueuePage();
                }
                catch (Exception)
                {
                    return RequestHandler.Status.Failed;
                }
                return RequestHandler.Status.Completed;
            }

            if (IsRemovePage())
            {
                try
                {
                    RemoveRequest();
                }
                catch (Exception)
                {
                    return RequestHandler.Status.Failed;
                }
                return RequestHandler.Status.Completed;
            }

            if (IsAddPage())
            {
                try
                {
                    AddRequest();
                }
                catch (Exception)
                {
                    return RequestHandler.Status.Failed;
                }
                return RequestHandler.Status.Pending;
            }

            if (IsRequestNetworkStatus())
            {
                try
                {
                    ServeNetworkStatus();
                }
                catch (Exception)
                {
                    return RequestHandler.Status.Failed;
                }
                return RequestHandler.Status.Completed;
            }

            if (IsEtaRequest())
            {
                try
                {
                    ServeETARequest();
                }
                catch (Exception)
                {
                    return RequestHandler.Status.Failed;
                }
                return RequestHandler.Status.Completed;
            }

            if (IsRichnessRequest())
            {
                try
                {
                    RichnessRequest();
                }
                catch (Exception)
                {
                    return RequestHandler.Status.Failed;
                }
                return RequestHandler.Status.Pending;
            }

            if (IsSignupRequest())
            {
                try
                {
                    SignupRequest();
                }
                catch (Exception)
                {
                    return RequestHandler.Status.Failed;
                }
                return RequestHandler.Status.Pending;
            }

            if (IsRCHomePage())
            {
                try
                {
                    ServeRCSearchPage(((RCLocalProxy)_proxy).RCSearchPage);
                }
                catch (Exception)
                {
                    return RequestHandler.Status.Failed;
                }
                return RequestHandler.Status.Completed;
            }

            // everything else
            string fileName = RequestUri.Replace("http://www.ruralcafe.net/", "");
            fileName = fileName.Replace('/', Path.DirectorySeparatorChar);
            fileName = ((RCLocalProxy)_proxy).UIPagesPath + fileName;
            string contentType = Util.GetContentTypeOfFile(fileName);
            SendOkHeaders(contentType);
            //LogDebug(contentType);
            long bytesSent = StreamFromCacheToClient(fileName);
            if (bytesSent > 0)
            {
                return RequestHandler.Status.Completed;
            }

            SendErrorPage(HTTP_NOT_FOUND, "page does not exist", RequestUri);
            return RequestHandler.Status.Failed;
        }

        /// <summary>Checks if the request is for the RuralCafe command to get the index page.</summary>
        bool IsIndex()
        {
            return RequestUri.StartsWith("http://www.ruralcafe.net/request/index.xml");
        }

        /// <summary>Checks if the request is for the RuralCafe command to get the queue.</summary>
        bool IsQueue()
        {
            return RequestUri.StartsWith("http://www.ruralcafe.net/request/queue.xml");
        }
        bool IsRemoteResult()
        {
            return RequestUri.StartsWith("http://www.ruralcafe.net/request/search.xml");
        }
        /// <summary>Checks if the request is for the RuralCafe command to get the search results.</summary>
        bool IsResult()
        {
            return RequestUri.StartsWith("http://www.ruralcafe.net/request/result.xml");
        }

        /// <summary>Checks if the request is for the RuralCafe homepage.</summary>
        private bool IsRCHomePage()
        {
            return RequestUri.Equals("http://www.ruralcafe.net") ||
                RequestUri.Equals("http://www.ruralcafe.net/") ||
                RequestUri.Equals("www.ruralcafe.net") ||
                RequestUri.Equals("www.ruralcafe.net/");
            //    RequestUri.Equals("http://www.ruralcafe.net/index.html") ||
            //    RequestUri.Equals("www.ruralcafe.net/index.html"))

        }

        /// <summary>Checks if the request is for the RuralCafe command to check whether the network is up.</summary>
        bool IsRequestNetworkStatus()
        {
            return RequestUri.StartsWith("http://www.ruralcafe.net/request/status");
        }
        /// <summary>Checks if the request is for the RuralCafe command to add a URI.</summary>
        bool IsAddPage()
        {
            // if (RequestUri.StartsWith("http://www.ruralcafe.net/addpage="))
            return RequestUri.StartsWith("http://www.ruralcafe.net/request/add?");
        }
        /// <summary>Checks if the request is for the RuralCafe command to remove a URI.</summary>
        bool IsRemovePage()
        {
            // if (RequestUri.StartsWith("http://www.ruralcafe.net/removepage="))
            return RequestUri.StartsWith("http://www.ruralcafe.net/request/remove?");
        }
        /// <summary>Checks if the request is for the RuralCafe command to remove all URIs for a client.</summary>
        bool IsRemoveAllPage()
        {
            return RequestUri.StartsWith("http://www.ruralcafe.net/removeall");
        }
        /// <summary>Checks if the request is for the RuralCafe command to add a URI.</summary>
        bool IsEtaRequest()
        {
            // if (RequestUri.StartsWith("http://www.ruralcafe.net/addpage="))
            return RequestUri.StartsWith("http://www.ruralcafe.net/request/eta");
        }
        /// <summary>Checks if the request is for the RuralCafe command to change richness.</summary>
        bool IsRichnessRequest()
        {
            return RequestUri.StartsWith("http://www.ruralcafe.net/request/richness");
        }
        /// <summary>Checks if the request is for the RuralCafe command to change richness.</summary>
        bool IsSignupRequest()
        {
            return RequestUri.StartsWith("http://www.ruralcafe.net/request/signup");
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
            NameValueCollection qscoll = Util.ParseHtmlQuery(RequestUri);
            int numItems = Int32.Parse(qscoll.Get("n"));
            int numCategories = Int32.Parse(qscoll.Get("c"));
            string searchString = qscoll.Get("s");

            SendOkHeaders("text/xml", "Cache-Control: no-cache" + "\r\n" +
                                      "Pragma: no-cache" + "\r\n" +
                                      "Expires: -1" + "\r\n");
            // not building the xml file yet, just sending the dummy page for now since there's really no top categories query yet
            // for the cache path
            StreamFromCacheToClient(((RCLocalProxy)_proxy).UIPagesPath + "index.xml");
        }

        /// <summary>
        /// Sends the frame page to the client.
        /// xml file of requests in the queue, content will be displayed in frame-offline-login.html
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
            NameValueCollection qscoll = Util.ParseHtmlQuery(RequestUri);
            int userId = Int32.Parse(qscoll.Get("u"));
            string date = qscoll.Get("v");

            // parse date
            bool queryOnDate = true;
            bool queryOnDay = false;
            int day = 1;
            int month = 1;
            int year = 1;
            DateTime queryDate = new DateTime();

            if (date.Equals("0"))
            {
                queryOnDate = false;
            }
            else if ((date.Length == 7) || (date.Length == 10))
            {
                if (date.Length <= 7)
                {
                    month = Int32.Parse(date.Substring(0, 2));
                    year = Int32.Parse(date.Substring(3, 4));
                    queryDate = new DateTime(year, month, day);
                }
                else
                {
                    queryOnDay = true;
                    day = Int32.Parse(date.Substring(0, 2));
                    month = Int32.Parse(date.Substring(3, 2));
                    year = Int32.Parse(date.Substring(6, 4));
                    queryDate = new DateTime(year, month, day);
                }
            }
            else
            {
                // malformed date
            }

            // get requests for this user
            List<LocalRequestHandler> requestHandlers = ((RCLocalProxy)_proxy).GetRequests(userId);
            // in reverse chronological order
            // requestHandlers.Reverse();

            string queuePageString = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + "<queue>";
            int i = 1;
            if (requestHandlers != null)
            {
                foreach (LocalRequestHandler requestHandler in requestHandlers)
                {
                    string itemId = System.Security.SecurityElement.Escape("" + requestHandler.ItemId);
                    string linkAnchorText = System.Security.SecurityElement.Escape(requestHandler.AnchorText);
                    string linkTarget = System.Security.SecurityElement.Escape(requestHandler.RequestUri);

                    // XXX: temporary hack to change the way the Title is being displayed
                    Uri tempUri = new Uri(linkTarget);
                    linkAnchorText = tempUri.Segments.Last();
                    if (linkAnchorText == "/")
                        if (tempUri.Segments.Length > 1)
                            linkAnchorText = tempUri.Segments[tempUri.Segments.Length - 2];
                        else
                            linkAnchorText = tempUri.Host;

                    string statusString = "";
                    DateTime requestDate = requestHandler.StartTime;

                    if (!queryOnDate || (
                        (requestDate.Year == queryDate.Year) &&
                        (requestDate.Month == queryDate.Month)) &&
                        ((queryOnDay && (requestDate.Day == queryDate.Day)) || !queryOnDay))
                    {

                        statusString = System.Security.SecurityElement.Escape(requestHandler.RequestStatus.ToString());

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
            }

            queuePageString = queuePageString + "</queue>"; //end tag
            SendOkHeaders("text/xml", "Cache-Control: no-cache" + "\r\n" +
                                      "Pragma: no-cache" + "\r\n" +
                                      "Expires: -1" + "\r\n");
            SendMessage(queuePageString);
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

            SendOkHeaders("text/html");
            StreamFromCacheToClient(((RCLocalProxy)_proxy).UIPagesPath + fileName);
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
            NameValueCollection qscoll = Util.ParseHtmlQuery(RequestUri);
            int numItemsPerPage = Int32.Parse(qscoll.Get("n"));
            int pageNumber = Int32.Parse(qscoll.Get("p"));
            string queryString = qscoll.Get("s");

            string resultsString = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>";

            List<Lucene.Net.Documents.Document> tempLuceneResults = new List<Lucene.Net.Documents.Document>();
            List<Lucene.Net.Documents.Document> filteredLuceneResults = new List<Lucene.Net.Documents.Document>();
            HitCollection wikiResults = new HitCollection();
            int currentItemNumber = 0;
            if (queryString.Trim().Length > 0)
            {
                // Query the Wiki index
                wikiResults = Indexer.Search(queryString, RCLocalProxy.WikiIndices.Values, Indexer.MAX_SEARCH_HITS);

                // Query our RuralCafe index
                List<Lucene.Net.Documents.Document> luceneResults = IndexWrapper.Query(((RCLocalProxy)_proxy).IndexPath, queryString);

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
                    foreach (Lucene.Net.Documents.Document tempDocument in tempLuceneResults)
                    {
                        string documentUri2 = tempDocument.Get("uri");
                        string documentTitle2 = tempDocument.Get("title");
                        if (String.Compare(documentUri, documentUri2) == 0 || String.Compare(documentTitle, documentTitle2) == 0)
                        {
                            exists = true;
                            break;
                        }
                    }
                    if (exists == false)
                    {
                        currentItemNumber++;
                        tempLuceneResults.Add(document);
                        if ((currentItemNumber > ((pageNumber - 1) * numItemsPerPage)) &&
                            (currentItemNumber < (pageNumber * numItemsPerPage) + 1))
                        {
                            filteredLuceneResults.Add(document);
                        }
                    }

                }
            }

            LogDebug(filteredLuceneResults.Count + " results");

            resultsString = resultsString + "<search total=\"" + currentItemNumber + "\">"; //laura: total should be the total number of results
            // Local Search Results
            for (int i = 0; i < filteredLuceneResults.Count; i++)
            {
                Lucene.Net.Documents.Document result = filteredLuceneResults.ElementAt(i);

                string uri = System.Security.SecurityElement.Escape(result.Get("uri")); // escape xml string
                string title = System.Security.SecurityElement.Escape(result.Get("title")); //escape xml string
                string displayUri = uri;
                string contentSnippet = "";

                // JAY: find content snippet here
                //contentSnippet = 
                if (uri.StartsWith("http://")) //laura: obmit http://
                    uri = uri.Substring(7);
                resultsString = resultsString +
                                "<item>" +
                                "<title>" + title + "</title>" +
                                "<url>" + uri + "</url>" +
                                "<snippet>" + contentSnippet + "</snippet>" +
                                "</item>";
            }

            resultsString = resultsString + "</search>";

            SendOkHeaders("text/xml", "Cache-Control: no-cache" + "\r\n" +
                                      "Pragma: no-cache" + "\r\n" +
                                      "Expires: -1" + "\r\n");
            SendMessage(resultsString);
        }


        void ServeRCRemoteResultPage()
        {
            if (_proxy.NetworkStatus == RCProxy.NetworkStatusCode.Offline)
            {
                return;
            }

            // Parse parameters
            NameValueCollection qscoll = Util.ParseHtmlQuery(RequestUri);
            int numItemsPerPage = Int32.Parse(qscoll.Get("n"));
            int pageNumber = Int32.Parse(qscoll.Get("p"));
            string queryString = qscoll.Get("s");

            // Google search
            string googleSearchString = ConstructGoogleSearch(queryString);
            _rcRequest = new RCRequest(this, (HttpWebRequest)WebRequest.Create(googleSearchString.Trim()));

            //LogDebug("streaming: " + _rcRequest.GenericWebRequest.RequestUri + " to cache and client.");
            //_rcRequest.GenericWebRequest.Proxy = null;
            long bytesDownloaded = _rcRequest.DownloadToCache(true);
            try
            {
                FileInfo f = new FileInfo(_rcRequest.CacheFileName);
                if (bytesDownloaded > -1 && f.Exists)
                {
                    LinkedList<RCRequest> resultLinkUris = ExtractGoogleResults(_rcRequest);
                    string resultsString = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>";
                    resultsString = resultsString + "<search total=\"" + resultLinkUris.Count.ToString() + "\">";
                    int currentItemNumber = 0;
                    foreach (RCRequest linkObject in resultLinkUris)
                    {
                        currentItemNumber++;
                        if ((currentItemNumber > ((pageNumber - 1) * numItemsPerPage)) &&
                            (currentItemNumber < (pageNumber * numItemsPerPage) + 1))
                        {
                            string uri = System.Security.SecurityElement.Escape(linkObject.Uri); //System.Security.SecurityElement.Escape(result.Get("uri")); // escape xml string
                            string title = System.Security.SecurityElement.Escape(linkObject.AnchorText); //System.Security.SecurityElement.Escape(result.Get("title")); //escape xml string
                            //string displayUri = uri;
                            string contentSnippet = "";

                            // XXX: find content snippet here
                            if (uri.StartsWith("http://")) //laura: obmit http://
                                uri = uri.Substring(7);
                            resultsString = resultsString +
                                            "<item>" +
                                            "<title>" + title + "</title>" +
                                            "<url>" + uri + "</url>" +
                                            "<snippet>" + contentSnippet + "</snippet>" +
                                            "</item>";
                        }
                    }

                    resultsString = resultsString + "</search>";

                    SendOkHeaders("text/xml", "Cache-Control: no-cache" + "\r\n" +
                                              "Pragma: no-cache" + "\r\n" +
                                              "Expires: -1" + "\r\n");
                    SendMessage(resultsString);
                }
                else
                {
                    // do nothing
                }
            }
            catch
            {
                // do nothing
            }
        }

        /// <summary>
        /// Translates a RuralCafe search to a Google one.
        /// </summary>
        /// <returns>Google search query.</returns>
        public string ConstructGoogleSearch(string searchString)
        {
            //string searchTerms = GetRCSearchField("textfield");
            string googleWebRequestUri = "http://www.google.com/search?hl=en&q=" +
                                        searchString.Replace(' ', '+') +
                                        "&btnG=Google+Search&aq=f&oq=";

            return googleWebRequestUri;
        }

        #endregion
        #region Proxy Control Methods

        /// <summary>
        /// Client asks proxy whether the network is on.
        /// </summary>
        private void ServeNetworkStatus()
        {
            SendOkHeaders("text/html");
            if (_proxy.NetworkStatus == RCProxy.NetworkStatusCode.Offline)
            {
                SendMessage("offline");
            }
            else if (_proxy.NetworkStatus == RCProxy.NetworkStatusCode.Slow)
            {
                SendMessage("cached");
            }
            else
            {
                SendMessage("online");
            }
        }

        /// <summary>
        /// Client changes richness. TODO
        /// 
        /// TODO Logging
        /// </summary>
        private void RichnessRequest()
        {
            // Parse parameters
            NameValueCollection qscoll = Util.ParseHtmlQuery(RequestUri);
            Richness richness;
            try
            {
                richness = (Richness)Enum.Parse(typeof(Richness), qscoll.Get("r"), true);
            }
            catch (Exception)
            {
                return;
            }
            Console.WriteLine("Richness would have been set to: " + richness);
            SendOkHeaders("text/html");
            SendMessage("Richness set.");
        }

        /// <summary>
        /// Client signs up for a new account. Preconditions have already been checked in JS.
        /// 
        /// TODO Logging
        /// </summary>
        private void SignupRequest()
        {
            // Parse parameters
            NameValueCollection qscoll = Util.ParseHtmlQuery(RequestUri);
            String username = qscoll.Get("u");
            String pw = qscoll.Get("p");
            int custid = Int32.Parse(qscoll.Get("i"));
            // Append zeros
            String custidStr = custid.ToString("D3");

            // Open users.xml
            String filename = "LocalProxy" + Path.DirectorySeparatorChar + "RuralCafePages"
                + Path.DirectorySeparatorChar + "users.xml";
            XmlDocument doc = new XmlDocument();
            doc.Load(filename);
            XmlNode custsNode = doc.SelectSingleNode("customers");
            // Add new user
            custsNode.AppendChild(custsNode.LastChild.CloneNode(true));
            custsNode.LastChild.Attributes["custid"].Value = custidStr;
            custsNode.LastChild.SelectSingleNode("user").InnerText = username;
            custsNode.LastChild.SelectSingleNode("pwd").InnerText = pw;
            //Save
            doc.Save(filename);

            SendOkHeaders("text/html");
            SendMessage("Signup successful.");
        }

        #endregion
        #region Queue Management Methods

        /// <summary>
        /// Queues this request.
        /// 
        /// TODO Method missing. Parameters (POST) missing.
        /// </summary>
        private void AddRequest()
        {
            // Parse parameters
            NameValueCollection qscoll = Util.ParseHtmlQuery(RequestUri);
            int userId = Int32.Parse(qscoll.Get("u"));
            string targetName = qscoll.Get("t");
            string targetUri = qscoll.Get("a");
            string refererUri = qscoll.Get("r");
            if (targetName == null)
            {
                targetName = "fake title";
            }
            if (targetUri == null)
            {
                // error
                targetUri = "";
                SendErrorPage(HTTP_NOT_FOUND, "malformed add request", "");
                return;
            }
            if (refererUri == null)
            {
                refererUri = targetUri;
            }

            // New local request handler (the request being added is not internal)
            LocalRequestHandler lrh = new LocalRequestHandler(this);
            lrh.OriginalRequest = (HttpWebRequest)WebRequest.Create(RequestUri.Trim());
            // preserve the original request status (for HandleLogRequest)
            Status originalRequestStatus = _rcRequest.RequestStatus;
            lrh.RCRequest = new RCRequest(lrh, (HttpWebRequest)WebRequest.Create(targetUri.Trim()), targetName, refererUri);
            lrh.RCRequest.RequestStatus = originalRequestStatus;

            ((RCLocalProxy)_proxy).QueueRequest(userId, lrh);
            SendOkHeaders("text/html");
            SendMessage(refererUri);
        }

        /// <summary>
        /// Removes the request from Ruralcafe's queue.
        /// </summary>
        private void RemoveRequest()
        {
            // Parse parameters
            NameValueCollection qscoll = Util.ParseHtmlQuery(RequestUri);
            int userId = Int32.Parse(qscoll.Get("u"));
            string itemId = qscoll.Get("i");

            LocalRequestHandler matchingRequestHandler = new LocalRequestHandler(itemId);
            ((RCLocalProxy)_proxy).DequeueRequest(userId, matchingRequestHandler);
            SendOkHeaders("text/html");
        }

        /// <summary>
        /// Gets the eta for a request in Ruralcafe's queue.
        /// </summary>
        private void ServeETARequest()
        {
            // Parse parameters
            NameValueCollection qscoll = Util.ParseHtmlQuery(RequestUri);
            int userId = Int32.Parse(qscoll.Get("u"));
            string itemId = qscoll.Get("i");

            // find the indexer of the matching request
            List<LocalRequestHandler> requestHandlers = ((RCLocalProxy)_proxy).GetRequests(userId);
            if (requestHandlers == null)
            {
                SendOkHeaders("text/html");
                SendMessage("0");
                return;
            }
            LocalRequestHandler matchingRequestHandler = new LocalRequestHandler(itemId);
            int requestIndex = requestHandlers.IndexOf(matchingRequestHandler);
            if (requestIndex < 0)
            {
                SendOkHeaders("text/html");
                SendMessage("0");
                return;
            }

            if (requestHandlers[requestIndex].RequestStatus == RequestHandler.Status.Pending)
            {
                SendOkHeaders("text/html");
                SendMessage("-1");
                return;
            }
            string printableEta = requestHandlers[requestIndex].PrintableETA();

            SendOkHeaders("text/html");
            SendMessage(printableEta);
        }

        #endregion
    }
}
