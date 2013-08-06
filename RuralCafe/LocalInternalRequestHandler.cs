using BzReader;
using RuralCafe.Clusters;
using RuralCafe.Lucenenet;
using RuralCafe.Util;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace RuralCafe
{
    /// <summary>
    /// Handles internal requests. These are e.g. queue, richness, remove, etc.
    /// </summary>
    public class LocalInternalRequestHandler : InternalRequestHandler
    {
        // Constants
        private const string LINK_SUGGESTIONS_CACHED_TEXT = "cached";

        private static Dictionary<String, RoutineMethod> routines = new Dictionary<String, RoutineMethod>();
        private static RoutineMethod defaultMethod = new RoutineMethod("DefaultPage");
        // Regex that matches two or more spaces. Useful for trimming them to one space.
        private static Regex multipleSpacesRegex = new Regex(@"\s\s+");
        // Regex that matches the number of search results in a google results page.
        private static Regex googleResultsNumRegex = new Regex("<div id=\"resultStats\">(Page \\d+ of a|A)bout (?<num>[\\d,]+) results");

        /// <summary>
        /// Static Constructor. Defines routines.
        /// </summary>
        static LocalInternalRequestHandler()
        {
            routines.Add("/request/index.xml", new RoutineMethod("ServeRCIndexPage", 		
 	            new string[] { "n", "c", "s" }, new Type[] { typeof(int), typeof(int), typeof(string) }));
            routines.Add("/request/search-live.xml", new RoutineMethod("ServeRCLiveResultPage",
                new string[] { "p", "s" }, new Type[] { typeof(int), typeof(string) }));
            routines.Add("/request/search-cache.xml", new RoutineMethod("ServeRCCacheResultPage",
                new string[] { "n", "p", "s" }, new Type[] { typeof(int), typeof(int), typeof(string) }));
            routines.Add("/request/queue.xml", new RoutineMethod("ServeRCQueuePage",
                new string[] { "v" }, new Type[] { typeof(string) }));
            routines.Add("/request/status.xml", new RoutineMethod("ServeNetworkStatus"));
            routines.Add("/request/linkSuggestions.xml", new RoutineMethod("LinkSuggestions",
                new string[] { "url", "anchor", "text" },
                new Type[] { typeof(string), typeof(string), typeof(string) }));

            routines.Add("/request/remove", new RoutineMethod("RemoveRequest",
                new string[] { "i" }, new Type[] { typeof(string) }));
            routines.Add("/request/add", new RoutineMethod("AddRequest",
                new string[] { "t", "a", }, new Type[] { typeof(string), typeof(int) }));
            routines.Add("/request/eta", new RoutineMethod("ServeETARequest",
                new string[] { "i" }, new Type[] { typeof(string) }));
            routines.Add("/request/signup", new RoutineMethod("SignupRequest",
                new string[] { "u", "p", "i" }, new Type[] { typeof(string), typeof(string), typeof(int) }));
            routines.Add("/request/login", new RoutineMethod("LoginRequest",
                new string[] { }, new Type[] { }));
            routines.Add("/request/logout", new RoutineMethod("LogoutRequest",
                new string[] { }, new Type[] { }));
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
        /// <param name="context">Client context.</param>
        public LocalInternalRequestHandler(RCLocalProxy proxy, HttpListenerContext context)
            : base(proxy, context, routines, defaultMethod)
        {
            _requestTimeout = LOCAL_REQUEST_PACKAGE_DEFAULT_TIMEOUT;
            UIPagesPath = Proxy.UIPagesPath;
        }

        /// <summary>The proxy that this request belongs to.</summary>
        public RCLocalProxy Proxy
        {
            get { return (RCLocalProxy)_proxy; }
        }

        /// <summary>Dummy.</summary>
        public override void DispatchRequest(object nullObj)
        {
            // dummy
        }

        #region helper methods

        /// <summary>
        /// Prepares an XML answer by setting content type and headers accordingly.
        /// </summary>
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
        /// <param name="searchString">The search string</param>
        /// <param name="pagenumber">The page number</param>
        /// <returns>Google search query.</returns>
        private string ConstructGoogleSearch(string searchString, int pagenumber)
        {
            string result = "http://www.google.com/search?hl=en&q=" +
                                        searchString.Replace(' ', '+') +
                                        "&btnG=Google+Search&aq=f&oq=";
            if (pagenumber > 1)
            {
                result += "&start=" + ((pagenumber - 1) * 10);
            }
            return result;
        }

        /// <summary>
        /// Gets the (approximate) number of google search results.
        /// </summary>
        /// <param name="googleResultsPage">The google results page.</param>
        /// <returns>The number of search results.</returns>
        private long GetGoogleResultsNumber(string googleResultsPage)
        {
            Match match = googleResultsNumRegex.Match(googleResultsPage);
            string numString = match.Groups["num"].Value;
            numString = numString.Replace(",", "");
            try
            {
                return Int64.Parse(numString);
            }
            catch (FormatException)
            {
                // We have at least one page, this is ugly.
                return 10;
            }
        }

        /// <summary>
        /// Extracts the result links from a google results page.
        /// </summary>
        /// <param name="googleResultPage">The Google results page.</param>
        /// <returns>List of links.</returns>
        private LinkedList<RCRequest> ExtractGoogleResults(string googleResultPage)
        {
            string[] stringSeparator = new string[] { "</cite>" };
            LinkedList<RCRequest> resultLinks = new LinkedList<RCRequest>();
            string[] lines = googleResultPage.Split(stringSeparator, StringSplitOptions.RemoveEmptyEntries);

            // get links
            int pos;
            string currLine;
            string currUri = "";
            string currTitle = "";
            string currSnippet = "";
            // Omitting last split, since there is no link any more.
            for (int i = 0; i < lines.Length - 1; i++)
            {
                currLine = lines[i];

                // get the title
                if ((pos = currLine.LastIndexOf("<a href=")) >= 0)
                {
                    // title
                    currTitle = currLine.Substring(pos);
                    // find start
                    if ((pos = currTitle.IndexOf(">")) >= 0)
                    {
                        // cut start
                        currTitle = currTitle.Substring(pos + 1);
                        if ((pos = currTitle.IndexOf("</a>")) >= 0)
                        {
                            // cut end
                            currTitle = currTitle.Substring(0, pos);
                            currTitle = HtmlUtils.StripTagsCharArray(currTitle);
                            currTitle = currTitle.Trim();
                        }
                    }
                }

                // get the uri
                if ((pos = currLine.LastIndexOf("<a href=\"/url?q=")) >= 0)
                {
                    // start right after
                    currUri = currLine.Substring(pos + "<a href=\"/url?q=".Length);
                    if ((pos = currUri.IndexOf("&amp")) >= 0)
                    {
                        // cut end
                        currUri = currUri.Substring(0, pos);
                        currUri = HtmlUtils.StripTagsCharArray(currUri);
                        currUri = currUri.Trim();
                    }

                    // instead of translating to absolute, prepend http:// to make webrequest constructor happy
                    currUri = HttpUtils.AddHttpPrefix(currUri);

                    if (!HttpUtils.IsValidUri(currUri))
                    {
                        continue;
                    }

                    // check blacklist
                    if (IsBlacklisted(currUri))
                    {
                        continue;
                    }

                    if (!currUri.Contains(".") || currTitle.Equals(""))
                    {
                        continue;
                    }

                    // get the content snippet (in next split)
                    currLine = lines[i + 1];
                    // find start
                    string snippetSplit = "<span class=\"st\">";
                    if ((pos = currLine.LastIndexOf(snippetSplit)) >= 0)
                    {
                        // cut start
                        currSnippet = currLine.Substring(pos + snippetSplit.Length);
                        if ((pos = currSnippet.IndexOf("</span>")) >= 0)
                        {
                            // cut end
                            currSnippet = currSnippet.Substring(0, pos);
                            currSnippet = HtmlUtils.StripTagsCharArray(currSnippet, false);
                            currSnippet = multipleSpacesRegex.Replace(currSnippet.Trim(), " ");
                        }
                    }

                    // Create request and save anchorText and snippet
                    RCRequest currRCRequest = new RCRequest(this, (HttpWebRequest)WebRequest.Create(currUri));
                    currRCRequest.AnchorText = currTitle;
                    currRCRequest.ContentSnippet = currSnippet;

                    resultLinks.AddLast(currRCRequest);
                }
            }

            return resultLinks;
        }

        #endregion
        #region RC display and handling Methods

        /// <summary>
        /// For everything else.
        /// </summary>
        public Response DefaultPage()
        {
            string fileName = _originalRequest.Url.LocalPath.Substring(1);
            fileName = fileName.Replace('/', Path.DirectorySeparatorChar);
            fileName = Proxy.UIPagesPath + fileName;
            string contentType = Utils.GetContentTypeOfFile(fileName);

            _clientHttpContext.Response.ContentType = contentType;
            return new Response(fileName, true);
        }

        /// <summary> 		
        /// Sends the RC Index page to the client. 		
        /// GET request will be sent to request/index.xml?c=6&n=4&s=root where 		
        /// n is the maximum number of items in a category, the number of <item>.
        /// c is the number of categories, the number of <category>. Only for level 1 and 2.
        /// s is the upper level category which the user want to explore (the top level category is defined as 'root') 		
        /// </summary> 		
        public Response ServeRCIndexPage(int numItems, int numCategories, string searchString)
        {
            string xmlAnswer;
            // Determine the hierarchy-level (1, 2 or 3)
            if (searchString.Equals("root"))
            {
                // Level 1
                try
                {
                    xmlAnswer = Cluster.Level1Index(Proxy.ProxyCacheManager.ClustersPath + CacheManager.CLUSTERS_XML_FILE_NAME, 
                        numCategories, numItems);
                }
                catch (ArgumentException e)
                {
                    throw new HttpException(HttpStatusCode.BadRequest, e.Message);
                }
            }
            else if (!searchString.Contains('.'))
            {
                // Level 2
                try
                {
                    xmlAnswer = Cluster.Level2Index(Proxy.ProxyCacheManager.ClustersPath + CacheManager.CLUSTERS_XML_FILE_NAME,
                        searchString, numCategories, numItems);
                }
                catch (ArgumentException e)
                {
                    throw new HttpException(HttpStatusCode.BadRequest, e.Message);
                }
            }
            else
            {
                // Level 3
                // Try to parse searchstring into categoryId and subCategoryId
                string[] catStrings = searchString.Split('.');
                if(catStrings.Length != 2)
                {
                    // Send error
                    throw new HttpException(HttpStatusCode.BadRequest, "searchstring malformed");
                }
                string catId = catStrings[0];
                string subCatId = catStrings[1];

                try
                {
                    xmlAnswer = Cluster.Level3Index(Proxy.ProxyCacheManager.ClustersPath + CacheManager.CLUSTERS_XML_FILE_NAME,
                        catId, subCatId, numItems);
                }
                catch (ArgumentException e)
                {
                    throw new HttpException(HttpStatusCode.BadRequest, e.Message);
                }
            }
            PrepareXMLRequestAnswer();
            return new Response(xmlAnswer, false);
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

            // get requests for this user
            List<LocalRequestHandler> requestHandlers = Proxy.GetRequests(UserIDCookieValue);

            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.AppendChild(xmlDoc.CreateXmlDeclaration("1.0", "UTF-8", String.Empty));
            XmlElement queueXml = xmlDoc.CreateElement("queue");
            xmlDoc.AppendChild(queueXml);

            if (requestHandlers != null)
            {
                foreach (LocalRequestHandler requestHandler in requestHandlers)
                {
                    string linkAnchorText = requestHandler.AnchorText;
                    string linkTarget = requestHandler.RequestUri;

                    // XXX: temporary hack to change the way the Title is being displayed
                    Uri tempUri = new Uri(linkTarget);
                    linkAnchorText = tempUri.Segments.Last();
                    if (linkAnchorText == "/")
                    {
                        if (tempUri.Segments.Length > 1)
                        {
                            linkAnchorText = tempUri.Segments[tempUri.Segments.Length - 2];
                        }
                        else
                        {
                            linkAnchorText = tempUri.Host;
                        }
                    }

                    DateTime requestDate = requestHandler.StartTime;
                    if (!queryOnDate || (
                        (requestDate.Year == queryDate.Year) &&
                        (requestDate.Month == queryDate.Month)) &&
                        ((queryOnDay && (requestDate.Day == queryDate.Day)) || !queryOnDay))
                    {
                        // build the actual element
                        XmlElement itemXml = xmlDoc.CreateElement("item");
                        itemXml.SetAttribute("id", requestHandler.RequestId);
                        queueXml.AppendChild(itemXml);

                        XmlElement titleXml = xmlDoc.CreateElement("title");
                        titleXml.InnerText = linkAnchorText;
                        itemXml.AppendChild(titleXml);
                        XmlElement urlXml = xmlDoc.CreateElement("url");
                        urlXml.InnerText = linkTarget;
                        itemXml.AppendChild(urlXml);
                        XmlElement statusXml = xmlDoc.CreateElement("status");
                        statusXml.InnerText = requestHandler.RequestStatus.ToString();
                        itemXml.AppendChild(statusXml);
                        XmlElement sizeXml = xmlDoc.CreateElement("size");
                        sizeXml.InnerText = "unknown";
                        itemXml.AppendChild(sizeXml);
                    }
                }
            }
            PrepareXMLRequestAnswer();
            return new Response(xmlDoc.InnerXml);
        }

        /// <summary>Serves the RuralCafe search page.</summary>
        public Response HomePage()
        {
            string pageUri = Proxy.RCSearchPage;
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
        /// 
        /// This gets the results from the cache.
        /// </summary>
        public Response ServeRCCacheResultPage(int numItemsPerPage, int pageNumber, string queryString)
        {
            if (pageNumber == 1)
            {
                // Log search term metric, but only for first-page-requests
                // We do this only in the cache search, since the online search is disabled when offline
                Logger.Metric(UserIDCookieValue, "Search term: " + queryString);
            }

            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.AppendChild(xmlDoc.CreateXmlDeclaration("1.0", "UTF-8", String.Empty));
            XmlElement searchXml = xmlDoc.CreateElement("search");
            xmlDoc.AppendChild(searchXml);

            if (queryString.Trim().Length == 0)
            {
                searchXml.SetAttribute("total", "0");
                return new Response(xmlDoc.InnerXml);
            }

            int[] resultAmounts = new int[2];
            int[] offsets = new int[2];
            if (Proxy.WikiWrapper.HasWikiIndices())
            {
                // We must include search results from both lucene and BzReader
                // In case we have odd numItemsPerPage
                // We ceil for Lucene and floor for BzReader
                resultAmounts[0] = (int)((numItemsPerPage / 2) + 0.5);
                resultAmounts[1] = (int)(numItemsPerPage / 2);
                offsets[0] = (pageNumber - 1) * resultAmounts[0];
                offsets[1] = (pageNumber - 1) * resultAmounts[1];
            }
            else
            {
                // We only need lucene search results.
                resultAmounts[0] = numItemsPerPage;
                resultAmounts[1] = 0;
                offsets[0] = (pageNumber - 1) * resultAmounts[0];
                offsets[1] = 0;
            }
           
            // Query our RuralCafe index
            SearchResults luceneResults = Proxy.IndexWrapper.Query(queryString, 
                Proxy.CachePath, offsets[0], resultAmounts[0]);
            // Query the Wiki index
            SearchResults wikiResults = Proxy.WikiWrapper.
                Query(queryString, offsets[1], resultAmounts[1]);

            // remove blacklisted lucene results
            for (int i = 0; i < luceneResults.Results.Count; i++ )
            {
                // ignore blacklisted domains
                if (IsBlacklisted(luceneResults.Results[i].URI))
                {
                    luceneResults.RemoveDocument(i);
                    // We will have to look at the same index again.
                    i--;
                }

            }
            // Log result num
            int numResults = luceneResults.NumResults + wikiResults.NumResults;
            Logger.Debug(numResults +  " results");

            searchXml.SetAttribute("total", "" + numResults);
            AppendSearchResultsXMLElements(luceneResults, xmlDoc, searchXml);
            AppendSearchResultsXMLElements(wikiResults, xmlDoc, searchXml);

            PrepareXMLRequestAnswer();
            return new Response(xmlDoc.InnerXml);
        }

        /// <summary>
        /// Adds children to elem for each search result.
        /// </summary>
        /// <param name="results">The search results.</param>
        /// <param name="doc">The XmlDocument.</param>
        /// <param name="elem">The XmlElement to append the childs.</param>
        private void AppendSearchResultsXMLElements(SearchResults results, XmlDocument doc, XmlElement elem)
        {
            foreach (SearchResult result in results.Results)
            {
                elem.AppendChild(BuildSearchResultXmlElement(doc,
                    result.Title, HttpUtils.RemoveHttpPrefix(result.URI), result.ContentSnippet));
            }
        }

        /// <summary>
        /// Sends the frame page to the client.
        /// GET request will be sent to request/search.xml?p=1&s=searchstring where
        /// p is the current page number, if there are multipage pages, page number starts from 1, 2, 3...,
        /// s is the search query string
        /// 
        /// This gets the results from google, always the same amount of results google gets, usually 10.
        /// </summary>
        public Response ServeRCLiveResultPage(int pageNumber, string queryString)
        {
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.AppendChild(xmlDoc.CreateXmlDeclaration("1.0", "UTF-8", String.Empty));
            XmlElement searchXml = xmlDoc.CreateElement("search");
            xmlDoc.AppendChild(searchXml);

            if (queryString.Trim().Length == 0 || Proxy.NetworkStatus == RCLocalProxy.NetworkStatusCode.Offline)
            {
                searchXml.SetAttribute("total", "0");
                return new Response(xmlDoc.InnerXml);
            }
            // Google search
            string googleSearchString = ConstructGoogleSearch(queryString, pageNumber);
            _rcRequest = new RCRequest(this, (HttpWebRequest)WebRequest.Create(googleSearchString));
            // Download result page
            string resultPage = _rcRequest.DownloadAsString();

            try
            {
                if (resultPage != null)
                {
                    LinkedList<RCRequest> resultLinkUris = ExtractGoogleResults(resultPage);
                    long numResults = GetGoogleResultsNumber(resultPage);
                    searchXml.SetAttribute("total", "" + numResults);

                    foreach (RCRequest linkObject in resultLinkUris)
                    {
                        searchXml.AppendChild(BuildSearchResultXmlElement(xmlDoc,
                            linkObject.AnchorText, HttpUtils.RemoveHttpPrefix(linkObject.Uri), linkObject.ContentSnippet));
                    }

                    PrepareXMLRequestAnswer();
                    return new Response(xmlDoc.InnerXml);
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

        /// <summary>
        /// Builds a search result XmlElement.
        /// </summary>
        /// <param name="doc">The XmlDocument</param>
        /// <param name="title">The title.</param>
        /// <param name="url">The URL.</param>
        /// <param name="snippet">The content snippet.</param>
        /// <returns>The XmlElement.</returns>
        private XmlElement BuildSearchResultXmlElement(XmlDocument doc, string title, string url, string snippet)
        {
            XmlElement itemXml = doc.CreateElement("item");

            XmlElement titleXml = doc.CreateElement("title");
            titleXml.InnerText = title;
            itemXml.AppendChild(titleXml);
            XmlElement urlXml = doc.CreateElement("url");
            urlXml.InnerText = HttpUtils.RemoveHttpPrefix(url);
            itemXml.AppendChild(urlXml);
            XmlElement snippetXml = doc.CreateElement("snippet");
            snippetXml.InnerText = snippet;
            itemXml.AppendChild(snippetXml);

            return itemXml;
        }

        /// <summary>
        /// Sends an XML with link suggestions. If the target URL is cached, that information is sent.
        /// </summary>
        /// <param name="url">The target URL.</param>
        /// <param name="anchorText">The anchor text.</param>
        /// <param name="surroundingText">The text surrounding the link.</param>
        public Response LinkSuggestions(string url, string anchorText, string surroundingText)
        {
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.AppendChild(xmlDoc.CreateXmlDeclaration("1.0", "UTF-8", String.Empty));
            XmlElement suggestionsXml = xmlDoc.CreateElement("suggestions");
            xmlDoc.AppendChild(suggestionsXml);
            // sent the anchorText right back
            suggestionsXml.SetAttribute("anchorText", anchorText);

            // Make global uri from relative url and referrer
            Uri refUri = new Uri(RefererUri);
            Uri targetUri = new Uri(refUri, url);

            // Test if url is cached.
            string filePath = Proxy.CachePath + CacheManager.GetRelativeCacheFileName(targetUri.AbsoluteUri);
            if (IsCached(filePath))
            {
                suggestionsXml.InnerText = LINK_SUGGESTIONS_CACHED_TEXT;
            }
            else
            {
                // XXX Mockup data
                for (int i = 0; i < 3; i++)
                {
                    XmlElement elem = xmlDoc.CreateElement("suggestion");
                    suggestionsXml.AppendChild(elem);
                    elem.InnerText = url + i;
                    elem.SetAttribute("downloadTime", "July 15, 2013");
                    elem.SetAttribute("title", "Link " + i);
                }
            }

            PrepareXMLRequestAnswer();
            // allow cross origin! (this request is being made from other domains.)
            _clientHttpContext.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            return new Response(xmlDoc.InnerXml);
        }

        #endregion
        #region Proxy Control Methods

        /// <summary>
        /// Client asks proxy whether the network is on.
        /// </summary>
        public Response ServeNetworkStatus()
        {
            string status;
            if (Proxy.NetworkStatus == RCLocalProxy.NetworkStatusCode.Offline)
            {
                status = "offline";
            }
            else if (Proxy.NetworkStatus == RCLocalProxy.NetworkStatusCode.Slow)
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
        /// </summary>
        /// <param name="username">The username.</param>
        /// <param name="pw">The password</param>
        /// <param name="custid">The new id of the user.</param>
        public Response SignupRequest(string username, string pw, int custid)
        {
            // Append zeros
            String custidStr = custid.ToString("D3");

            // Open users.xml
            String filename = Proxy.UIPagesPath + "users.xml";
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
            // Log
            Logger.Info("A new user signed up with id " + custid + ": " + username);
            return new Response("Signup successful.");
        }

        /// <summary>
        /// At the moment this just lets the server know someone logged in.
        /// The server than attached the IP to the user ID in the cookie.
        /// 
        /// Lates this should be changed so that the server actually logs the persons in 
        /// and performs the pw checks, etc.
        /// </summary>
        /// <returns></returns>
        public Response LoginRequest()
        {
            Logger.Info("User " + UserIDCookieValue + " logs in.");
            Proxy.SessionManager.LogUserIn(ClientIP, UserIDCookieValue);
            return new Response();
        }

        /// <summary>
        /// At the moment this just lets the server know someone logged out.
        /// The server than detaches the IP from the user ID in the cookie.
        /// 
        /// Later this should be changed so that the server actually logs the persons out.
        /// </summary>
        /// <returns></returns>
        public Response LogoutRequest()
        {
            Logger.Info("User " + UserIDCookieValue + " logs out.");
            Proxy.SessionManager.LogUserOut(UserIDCookieValue);
            return new Response();
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
                lrh = Proxy.PopRequestWithoutUser(id);
            }
            catch (Exception)
            {
                throw new HttpException(HttpStatusCode.BadRequest, 
                    "malformed add request: id not supplied or unknown");
            }
            // Set RC headers
            RCSpecificRequestHeaders headers = new RCSpecificRequestHeaders(userId);
            lrh.AddRCSpecificRequestHeaders(headers);

            Proxy.AddRequest(userId, lrh);
            // Redirect to homepage
            redirectUrl = RC_PAGE;
            _clientHttpContext.Response.Redirect(redirectUrl);
            return new Response();
        }

        /// <summary>
        /// Removes the request from Ruralcafe's queue.
        /// </summary>
        public Response RemoveRequest(string requestId)
        {
            // remove it locally
            Proxy.RemoveRequest(UserIDCookieValue, requestId);

            // delegate removal at remote proxy to remote proxy
            return DelegateToRemoteProxy();
        }

        /// <summary>
        /// Gets the eta for a request in Ruralcafe's queue.
        /// </summary>
        public Response ServeETARequest(string requestId)
        {
            // find the indexer of the matching request
            List<LocalRequestHandler> requestHandlers = Proxy.GetRequests(UserIDCookieValue);
            if (requestHandlers == null)
            {
                return new Response("0");
            }
            // This gets the requestHandler with the same requestID, if there is one
            LocalRequestHandler requestHandler = requestHandlers.FirstOrDefault(rh => rh.RequestId == requestId);
            if (requestHandler == null)
            {
                return new Response("0");
            }
            return new Response(requestHandler.PrintableETA());
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
            // We need to create the request as this is usually not done for the internal handler
            CreateRequest(OriginalRequest);
            // Set Proxy for the request
            _rcRequest.SetProxyAndTimeout(Proxy.RemoteProxy, System.Threading.Timeout.Infinite);
            HttpWebResponse response = (HttpWebResponse)_rcRequest.GenericWebRequest.GetResponse();
            return createResponse(response);
        }
        #endregion
    }
}
