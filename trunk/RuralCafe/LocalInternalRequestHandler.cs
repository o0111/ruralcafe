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
    public class LocalInternalRequestHandler : InternalRequestHandler
    {
        private static Dictionary<String, RoutineMethod> routines = new Dictionary<String, RoutineMethod>();
        private static RoutineMethod defaultMethod = new RoutineMethod("DefaultPage");

        /// <summary>
        /// Static Constructor. Defines routines.
        /// </summary>
        static LocalInternalRequestHandler()
        {
            routines.Add("/request/index.xml", new RoutineMethod("ServeRCIndexPage",
                new string[] { "n", "c", "s" }, new Type[] { typeof(int), typeof(int), typeof(string) }));
            routines.Add("/request/search.xml", new RoutineMethod("ServeRCRemoteResultPage",
                new string[] { "n", "p", "s" }, new Type[] { typeof(int), typeof(int), typeof(string) }));
            routines.Add("/request/result.xml", new RoutineMethod("ServeRCResultPage",
                new string[] { "n", "p", "s" }, new Type[] { typeof(int), typeof(int), typeof(string) }));
            routines.Add("/request/queue.xml", new RoutineMethod("ServeRCQueuePage",
                new string[] { "v" }, new Type[] { typeof(string) }));
            routines.Add("/request/status.xml", new RoutineMethod("ServeNetworkStatus"));

            routines.Add("/request/remove", new RoutineMethod("RemoveRequest",
                new string[] { "i" }, new Type[] { typeof(string) }));
            routines.Add("/request/add", new RoutineMethod("AddRequest",
                new string[] { "t", "a", }, new Type[] { typeof(string), typeof(int) }));
            routines.Add("/request/eta", new RoutineMethod("ServeETARequest",
                new string[] { "i" }, new Type[] { typeof(string) }));
            routines.Add("/request/signup", new RoutineMethod("SignupRequest",
                new string[] { "u", "p", "i" }, new Type[] { typeof(string), typeof(string), typeof(int) }));
            routines.Add("/", new RoutineMethod("HomePage"));

            // All delegated routines
            routines.Add("/request/richness", new RoutineMethod("DelegateToRemoteProxy"));
        }

        /// <summary>
        /// The path to the UI pages.
        /// </summary>
        private string UIPagesPath;

        /// <summary>
        /// Constructor for a local internal proxy's request handler.
        /// </summary>
        /// <param name="proxy">Proxy this request handler belongs to.</param>
        /// <param name="socket">Client socket.</param>
        public LocalInternalRequestHandler(RCLocalProxy proxy, HttpListenerContext context)
            : base(proxy, context, routines, defaultMethod)
        {
            _requestTimeout = LOCAL_REQUEST_PACKAGE_DEFAULT_TIMEOUT;
            UIPagesPath = ((RCLocalProxy)_proxy).UIPagesPath;
        }

        #region helper methods

        private void PrepareXMLRequestAnswer()
        {
            _clientHttpContext.Response.ContentType = "text/xml";
            _clientHttpContext.Response.AddHeader("Cache-Control", "no-cache");
            _clientHttpContext.Response.AddHeader("Pragma", "no-cache");
            _clientHttpContext.Response.AddHeader("Expires", "-1");
        }

        /// <summary>
        /// Translates a RuralCafe search to a Google one.
        /// </summary>
        /// <returns>Google search query.</returns>
        private string ConstructGoogleSearch(string searchString)
        {
            //string searchTerms = GetRCSearchField("textfield");
            string googleWebRequestUri = "http://www.google.com/search?hl=en&q=" +
                                        searchString.Replace(' ', '+') +
                                        "&btnG=Google+Search&aq=f&oq=";
            return googleWebRequestUri;
        }

        #endregion
        #region RC display and handling Methods

        /// <summary>
        /// For everything else.
        /// </summary>
        public Response DefaultPage()
        {
            // FIXME why "result-", not "result-offline"?
            string fileName = _originalRequest.Url.LocalPath.Substring(1);
            fileName = fileName.Replace('/', Path.DirectorySeparatorChar);
            fileName = ((RCLocalProxy)_proxy).UIPagesPath + fileName;
            string contentType = Util.GetContentTypeOfFile(fileName);

            _clientHttpContext.Response.ContentType = contentType;
            return new Response(fileName, true);
        }

        /// <summary>
        /// Sends the RC Index page to the client.
        /// GET request will be sent to request/index.xml?c=6&n=4&s=root where
        /// c is the number of categories, the number of <category> required
        /// n is the maximum number of items in a category, the number of <item> allowed
        /// s is the upper level category which the user want to explore (the top level category is defined as 'root')
        /// </summary>
        public Response ServeRCIndexPage(int numItems, int numCategories, string searchString)
        {
			 // XXX: not building the xml file yet, just sending the dummy page for now since there's really no top categories query yet
            // for the cache path
            PrepareXMLRequestAnswer();
            return new Response(UIPagesPath + "index.xml", true);
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
        public Response ServeRCQueuePage(string date)
        {
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
            List<LocalRequestHandler> requestHandlers = ((RCLocalProxy)_proxy).GetRequests(UserIDCookieValue);

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
            PrepareXMLRequestAnswer();
            return new Response(queuePageString);
        }

        /// <summary>Serves the RuralCafe search page.</summary>
        public Response HomePage()
        {
            string pageUri = ((RCLocalProxy)_proxy).RCSearchPage;
            string fileName = pageUri;
            int offset = pageUri.LastIndexOf('/');
            if (offset >= 0 && offset < (pageUri.Length - 1))
            {
                fileName = pageUri.Substring(offset + 1);
            }

            return new Response(UIPagesPath + fileName, true);
        }

        /// <summary>
        /// Sends the frame page to the client.
        /// GET request will be sent to request/result.xml?n=5&p=1&s=searchstring where
        /// n is the maximum number of items per page, the number of <item> allowed in this file
        /// p is the current page number, if there are multipage pages, page number starts from 1, 2, 3...,
        /// s is the search query string
        /// </summary>
        public Response ServeRCResultPage(int numItemsPerPage, int pageNumber, string queryString)
        {
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
                if (uri.StartsWith("http://")) //laura: omit http://
                    uri = uri.Substring(7);
                resultsString = resultsString +
                                "<item>" +
                                "<title>" + title + "</title>" +
                                "<url>" + uri + "</url>" +
                                "<snippet>" + contentSnippet + "</snippet>" +
                                "</item>";
            }

            resultsString = resultsString + "</search>";

            PrepareXMLRequestAnswer();
            return new Response(resultsString);
        }

        //TODO: comment. Offline?
        public Response ServeRCRemoteResultPage(int numItemsPerPage, int pageNumber, string queryString)
        {
            if (_proxy.NetworkStatus == RCProxy.NetworkStatusCode.Offline)
            {
                return new Response();
            }
            // Google search
            string googleSearchString = ConstructGoogleSearch(queryString);
            _rcRequest = new RCRequest(this, (HttpWebRequest)WebRequest.Create(googleSearchString.Trim()));

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
                            string uri = System.Security.SecurityElement.Escape(linkObject.Uri);
                            string title = System.Security.SecurityElement.Escape(linkObject.AnchorText);
                            //string displayUri = uri;
                            string contentSnippet = "";

                            // XXX: find content snippet here
                            if (uri.StartsWith("http://")) //laura: omit http://
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

                    PrepareXMLRequestAnswer();
                    return new Response(resultsString);
                }
                else
                {
                    return new Response();
                }
            }
            catch
            {
                return new Response();
            }
        }

        #endregion
        #region Proxy Control Methods

        /// <summary>
        /// Client asks proxy whether the network is on.
        /// </summary>
        public Response ServeNetworkStatus()
        {
            string status;
            if (_proxy.NetworkStatus == RCProxy.NetworkStatusCode.Offline)
            {
                status = "offline";
            }
            else if (_proxy.NetworkStatus == RCProxy.NetworkStatusCode.Slow)
            {
                status = "cached";
            }
            else
            {
                status = "online";
            }
            return new Response(status);
        }

        /// <summary>
        /// Client signs up for a new account. Preconditions have already been checked in JS.
        /// 
        /// TODO Logging
        /// </summary>
        public Response SignupRequest(String username, String pw, int custid)
        {
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
            return new Response("Signup successful.");
        }

        #endregion
        #region Queue Management Methods

        /// <summary>
        /// Queues this request.
        /// </summary>
        public Response AddRequest(string targetName, int id)
        {
            int userId = UserIDCookieValue;
            string redirectUrl;
            if (userId == -1)
            {
                // Redirect to login.html referring to request id
                redirectUrl = "http://www.ruralcafe.net/login.html?t=" + targetName + "&a=" + id; ;
                _clientHttpContext.Response.Redirect(redirectUrl);
                return new Response();
            }
            if (targetName == null)
            {
                targetName = "fake title";
            }
            // Get original request from Dictionary
            LocalRequestHandler lrh;
            try
            {
                lrh = ((RCLocalProxy)_proxy).PopRequestWithoutUser(id);
            }
            catch (Exception)
            {
                throw new HttpException(HttpStatusCode.BadRequest, 
                    "malformed add request: ad not suppied or unknown");
            }
            // Set RC headers
            RCSpecificRequestHeaders headers = new RCSpecificRequestHeaders(userId);
            lrh.AddRCSpecificRequestHeaders(headers);

            ((RCLocalProxy)_proxy).QueueRequest(userId, lrh);
            // Redirect to homepage
            redirectUrl = "http://www.ruralcafe.net/";
            _clientHttpContext.Response.Redirect(redirectUrl);
            return new Response();
        }

        /// <summary>
        /// Removes the request from Ruralcafe's queue.
        /// </summary>
        public Response RemoveRequest(string itemId)
        {
            LocalRequestHandler matchingRequestHandler = new LocalRequestHandler(itemId);
            ((RCLocalProxy)_proxy).DequeueRequest(UserIDCookieValue, matchingRequestHandler);
            return new Response();
        }

        /// <summary>
        /// Gets the eta for a request in Ruralcafe's queue.
        /// </summary>
        public Response ServeETARequest(string itemId)
        {
            // find the indexer of the matching request
            List<LocalRequestHandler> requestHandlers = ((RCLocalProxy)_proxy).GetRequests(UserIDCookieValue);
            if (requestHandlers == null)
            {
                return new Response("0");
            }
            LocalRequestHandler matchingRequestHandler = new LocalRequestHandler(itemId);
            int requestIndex = requestHandlers.IndexOf(matchingRequestHandler);
            if (requestIndex < 0)
            {
                return new Response("0");
            }

            if (requestHandlers[requestIndex].RequestStatus == RequestHandler.Status.Pending)
            {
                return new Response("-1");
            }
            return new Response(requestHandlers[requestIndex].PrintableETA());
        }

        #endregion
        #region Remote Delegation

        /// <summary>
        /// Delegates a routine call to the remote proxy and responds with his response.
        /// 
        /// GET Parameters in the URI are passed. Other stuff ATM not, but this isn't even necessary.
        /// </summary>
        /// <returns>The remote proxy's response.</returns>
        public Response DelegateToRemoteProxy()
        {
            // Set Proxy for the request
            _rcRequest.SetProxyAndTimeout(((RCLocalProxy)_proxy).RemoteProxy, System.Threading.Timeout.Infinite);
            HttpWebResponse response = (HttpWebResponse)_rcRequest.GenericWebRequest.GetResponse();
            return createResponse(response);
        }
        #endregion
    }
}
