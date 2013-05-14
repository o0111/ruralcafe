﻿using BzReader;
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
                new string[] { "u", "v" }, new Type[] { typeof(int),  typeof(string) }));
            routines.Add("/request/status.xml", new RoutineMethod("ServeNetworkStatus"));
            routines.Add("/request/remove", new RoutineMethod("RemoveRequest",
                new string[] { "u", "i" }, new Type[] { typeof(int), typeof(string) }));
            routines.Add("/request/add", new RoutineMethod("AddRequest",
                new string[] { "u", "t", "a", "r" }, new Type[] { typeof(int), typeof(string), typeof(string), typeof(string) }));
            routines.Add("/request/eta", new RoutineMethod("ServeETARequest",
                new string[] { "u", "i" }, new Type[] { typeof(int), typeof(string) }));
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
        public LocalInternalRequestHandler(RCLocalProxy proxy, Socket socket)
            : base(proxy, socket, routines, defaultMethod)
        {
            _requestId = _proxy.NextRequestId;
            _proxy.NextRequestId = _proxy.NextRequestId + 1;
            _requestTimeout = LOCAL_REQUEST_PACKAGE_DEFAULT_TIMEOUT;
            UIPagesPath = ((RCLocalProxy)_proxy).UIPagesPath;
        }

        #region RC display and handling Methods

        /// <summary>
        /// For everything else.
        /// </summary>
        public Response DefaultPage()
        {
            string fileName = _originalRequest.RequestUri.LocalPath.Substring(1);
            fileName = fileName.Replace('/', Path.DirectorySeparatorChar);
            fileName = ((RCLocalProxy)_proxy).UIPagesPath + fileName;
            string contentType = Util.GetContentTypeOfFile(fileName);

            return new Response(contentType, "", fileName, true);
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
            return new Response("text/xml", "Cache-Control: no-cache" + "\r\n" +
                                      "Pragma: no-cache" + "\r\n" +
                                      "Expires: -1" + "\r\n",
                                      UIPagesPath + "index.xml", true);
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
        public Response ServeRCQueuePage(int userId, string date)
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
            List<LocalRequestHandler> requestHandlers = ((RCLocalProxy)_proxy).GetRequests(userId);

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
            return new Response("text/xml", "Cache-Control: no-cache" + "\r\n" +
                                      "Pragma: no-cache" + "\r\n" +
                                      "Expires: -1" + "\r\n",
                                      queuePageString);
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

            return new Response("text/html", "", UIPagesPath + fileName, true);
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

            return new Response("text/xml", "Cache-Control: no-cache" + "\r\n" +
                                      "Pragma: no-cache" + "\r\n" +
                                      "Expires: -1" + "\r\n",
                                      resultsString);
        }

        //TODO: comment. Offline?
        public Response ServeRCRemoteResultPage(int numItemsPerPage, int pageNumber, string queryString)
        {
            if (_proxy.NetworkStatus == RCProxy.NetworkStatusCode.Offline)
            {
                return new Response("text/html");
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

                    return new Response("text/xml", "Cache-Control: no-cache" + "\r\n" +
                                              "Pragma: no-cache" + "\r\n" +
                                              "Expires: -1" + "\r\n",
                                              resultsString);
                }
                else
                {
                    return new Response("text/html");
                }
            }
            catch
            {
                return new Response("text/html");
            }
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
            return new Response("text/html", status);
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
            return new Response("text/html", "Signup successful.");
        }

        #endregion
        #region Queue Management Methods

        /// <summary>
        /// Queues this request.
        /// 
        /// TODO Method missing. Parameters (POST) missing.
        /// TODO remove the whole AddRequest logic.
        /// FIXME if URI contains "?" -> error
        /// </summary>
        public Response AddRequest(int userId, string targetName, string targetUri, string refererUri)
        {
            if (targetName == null)
            {
                targetName = "fake title";
            }
            if (targetUri == null)
            {
                throw new HttpException(HttpStatusCode.BadRequest, "malformed add request: no targetUri");
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
            return new Response("text/html", refererUri);
        }

        /// <summary>
        /// Removes the request from Ruralcafe's queue.
        /// </summary>
        public Response RemoveRequest(int userId, string itemId)
        {
            LocalRequestHandler matchingRequestHandler = new LocalRequestHandler(itemId);
            ((RCLocalProxy)_proxy).DequeueRequest(userId, matchingRequestHandler);
            return new Response("text/html");
        }

        /// <summary>
        /// Gets the eta for a request in Ruralcafe's queue.
        /// </summary>
        public Response ServeETARequest(int userId, string itemId)
        {
            // find the indexer of the matching request
            List<LocalRequestHandler> requestHandlers = ((RCLocalProxy)_proxy).GetRequests(userId);
            if (requestHandlers == null)
            {
                return new Response("text/html", "0");
            }
            LocalRequestHandler matchingRequestHandler = new LocalRequestHandler(itemId);
            int requestIndex = requestHandlers.IndexOf(matchingRequestHandler);
            if (requestIndex < 0)
            {
                return new Response("text/html", "0");
            }

            if (requestHandlers[requestIndex].RequestStatus == RequestHandler.Status.Pending)
            {
                return new Response("text/html", "-1");
            }
            return new Response("text/html", requestHandlers[requestIndex].PrintableETA());
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
