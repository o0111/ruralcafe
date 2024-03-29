﻿using BzReader;
using HtmlAgilityPack;
using RuralCafe.Clusters;
using RuralCafe.Database;
using RuralCafe.Lucenenet;
using Util;
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
using System.Web;
using System.Xml;
using RuralCafe.LinkSuggestion;

namespace RuralCafe
{
    /// <summary>
    /// Handles internal requests. These are e.g. queue, richness, remove, etc.
    /// </summary>
    public class LocalInternalRequestHandler : InternalRequestHandler
    {
        // Constants
        private const string LINK_SUGGESTIONS_CACHED_TEXT = "cached";
        private readonly string[] REDIR_PAGES = new string[] { "", "trotro.html", "trotro-user.html" };
        private readonly TimeSpan MIN_SURVEY_INTERVAL = new TimeSpan(24, 0, 0); // 1 day

        private static Dictionary<String, RoutineMethod> routines = new Dictionary<String, RoutineMethod>();
        private static RoutineMethod defaultMethod = new RoutineMethod("DefaultPage");

        // The time when each user last did the satisfaction survey.
        private static Dictionary<int, DateTime> _timesOfLastSurvey = new Dictionary<int, DateTime>();

        /// <summary>
        /// Static Constructor. Defines routines.
        /// </summary>
        static LocalInternalRequestHandler()
        {
            routines.Add("/newrequest.html", new RoutineMethod("NewRequestHTML",
                new string[] { }, new Type[] { }));

            routines.Add("/request/index.xml", new RoutineMethod("ServeRCIndexPage", 		
 	            new string[] { "s" }, new Type[] { typeof(string) }));
            routines.Add("/request/search-live.xml", new RoutineMethod("ServeRCLiveResultPage",
                new string[] { "p", "s" }, new Type[] { typeof(int), typeof(string) }));
            routines.Add("/request/search-cache.xml", new RoutineMethod("ServeRCCacheResultPage",
                new string[] { "n", "p", "s" }, new Type[] { typeof(int), typeof(int), typeof(string) }));
            routines.Add("/request/queue.xml", new RoutineMethod("ServeRCQueuePage",
                new string[] { "v" }, new Type[] { typeof(string) }));
            routines.Add("/request/status.xml", new RoutineMethod("ServeNetworkStatus"));
            routines.Add("/request/linkSuggestions.xml", new RoutineMethod("LinkSuggestions",
                new string[] { "url", "anchor", "text", "amount" },
                new Type[] { typeof(string), typeof(string), typeof(string), typeof(int) }));

            routines.Add("/request/remove", new RoutineMethod("RemoveRequest",
                new string[] { "i" }, new Type[] { typeof(string) }));
            routines.Add("/request/add", new RoutineMethod("AddRequest",
                new string[] { "t", "a", }, new Type[] { typeof(string), typeof(int) }));
            routines.Add("/request/eta", new RoutineMethod("ServeETARequest",
                new string[] { "i" }, new Type[] { typeof(string) }));
            routines.Add("/request/signup", new RoutineMethod("SignupRequest",
                new string[] { "user", "pw" }, new Type[] { typeof(string), typeof(string) }, false));
            routines.Add("/request/login", new RoutineMethod("LoginRequest",
                new string[] { "user", "pw", "search" }, new Type[] { typeof(string), typeof(string), typeof(string) }, false));
            routines.Add("/request/logout", new RoutineMethod("LogoutRequest"));
            routines.Add("/request/isSurveyDue", new RoutineMethod("IsSurveyDue",
                new string[] { "endOfSession" }, new Type[] { typeof(bool) }));
            routines.Add("/request/userSatisfaction", new RoutineMethod("UserSatisfactionRequest",
                new string[] { "rating", "hadIssues", "problems", "comments" }, 
                new Type[] { typeof(int), typeof(bool), typeof(string), typeof(string) }));
            routines.Add("/", new RoutineMethod("HomePage"));

            // All delegated routines
            routines.Add("/request/richness", new RoutineMethod("DelegateToRemoteProxy"));
        }

        /// <summary>
        /// Constructor for a local internal proxy's request handler.
        /// </summary>
        /// <param name="proxy">Proxy this request handler belongs to.</param>
        /// <param name="context">Client context.</param>
        public LocalInternalRequestHandler(RCLocalProxy proxy, HttpListenerContext context)
            : base(proxy, context, routines, defaultMethod) { }

        /// <summary>The proxy that this request belongs to.</summary>
        public RCLocalProxy Proxy
        {
            get { return (RCLocalProxy)_proxy; }
        }

        #region helper methods

        /// <summary>
        /// Gets the time the user with the given id last did the survey.
        /// </summary>
        /// <param name="userId">The user ID.</param>
        /// <returns>The time when he last did the survey.</returns>
        private DateTime GetLastSurveyTime(int userId)
        {
            return _timesOfLastSurvey.ContainsKey(userId) ? _timesOfLastSurvey[userId] : DateTime.MinValue;
        }

        /// <summary>
        /// Sets the time when the user with given id last did the survey to now.
        /// </summary>
        /// <param name="userId">The user ID.</param>
        private void SetLastSurveyTime(int userId)
        {
            _timesOfLastSurvey[userId] = DateTime.Now;
        }

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
            Match match = RegExs.GOOGLE_RESULTS_NUM_REGEX.Match(googleResultsPage);
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

                    // If we're in slow mode, we don't want cached results within the live results
                    if (_proxy.NetworkStatus == RCProxy.NetworkStatusCode.Slow &&
                        _proxy.ProxyCacheManager.IsCached(CacheManager.GetRelativeCacheFileName(currUri, "GET")))
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
                            currSnippet = RegExs.MULTIPLE_SPACES_REGEX.Replace(currSnippet.Trim(), " ");
                        }
                    }

                    // Create request and save anchorText and snippet
                    RCRequest currRCRequest = new RCRequest(_proxy, (HttpWebRequest)WebRequest.Create(currUri));
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
        /// Some preprocessing.
        /// </summary>
        protected override Response PreProcess()
        {
            // Log the user out at the client side, if he is already logged out at the server side.
            if (UserIDCookieValue != Proxy.SessionManager.GetUserId(ClientIP))
            {
                SendLogoutCookies();
            }

            // Redirect to login if we force login and user is not logged in.
            if (Properties.Settings.Default.FORCE_LOGIN && UserIDCookieValue == -1
                && REDIR_PAGES.Contains(_originalRequest.Url.LocalPath.Substring(1)))
            {
                // We redirect to login.html
                _clientHttpContext.Response.Redirect("login.html");
                return new Response();
            }

            // Update time of last activity for logged in users
            if (UserIDCookieValue != -1)
            {
                Proxy.SessionManager.UpdateLastActivityTime(ClientIP);
            }

            return null;
        }

        /// <summary>
        /// Sends expired cookies, so they will be deleted at the client side.
        /// </summary>
        private void SendLogoutCookies()
        {
            // Set cookies.
            Cookie idCookie = new Cookie("uid", "", "/");
            Cookie nameCookie = new Cookie("uname", "", "/");
            Cookie statusCookie = new Cookie("status", "", "/");
            // Expiry date in the past.
            DateTime expiryDate = DateTime.Now.AddDays(-1);
            idCookie.Expires = expiryDate;
            nameCookie.Expires = expiryDate;
            statusCookie.Expires = expiryDate;

            _clientHttpContext.Response.Headers.Add("Set-Cookie", idCookie.ToCookieString());
            _clientHttpContext.Response.Headers.Add("Set-Cookie", nameCookie.ToCookieString());
            _clientHttpContext.Response.Headers.Add("Set-Cookie", statusCookie.ToCookieString());
        }

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

            return new Response(Proxy.UIPagesPath + fileName, true);
        }

        /// <summary>
        /// Returns the newrequest.html frame, but let's the back link point to the original
        /// referer, if available.
        /// </summary>
        public Response NewRequestHTML()
        {
            Uri referer = new Uri(RefererUri);
            NameValueCollection parameterCollection = HttpUtility.ParseQueryString(referer.Query);
            string originalReferer = parameterCollection["ref"];
            if (originalReferer == null)
            {
                return DefaultPage();
            }

            string fileName = Proxy.UIPagesPath + "newrequest.html";

            HtmlDocument doc = new HtmlDocument();
            doc.Load(fileName);
            HtmlNode node = doc.DocumentNode.SelectSingleNode("//a[@id='back_link']");
            node.SetAttributeValue("href", originalReferer);

            _clientHttpContext.Response.ContentType = "text/html";
            return new Response(doc.DocumentNode.OuterHtml, false);
        }

        /// <summary> 		
        /// Sends the RC Index page to the client.	
        /// </summary> 		
        /// <param name="searchString">The search string. "root" for level 1, the id for level 2,
        /// and two ids separated by a dot for level 3.</param>
        public Response ServeRCIndexPage(string searchString)
        {
            if (!File.Exists(Proxy.ProxyCacheManager.ClustersPath + IndexServer.CLUSTERS_XML_FILE_NAME))
            {
                // We haven't done clustering yet or there are no files in the cache.
                throw new HttpException(HttpStatusCode.NotFound, "No clusters computed.");
            }

            if (searchString == null)
            {
                throw new HttpException(HttpStatusCode.BadRequest, "Must provide search string.");
            }

            string xmlAnswer;
            // Determine the hierarchy-level (1, 2 or 3)
            if (searchString.Equals("root"))
            {
                // Level 1
                try
                {
                    xmlAnswer = IndexServer.Level1Index(Proxy.ProxyCacheManager.ClustersPath + IndexServer.CLUSTERS_XML_FILE_NAME);
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
                    xmlAnswer = IndexServer.Level2Index(Proxy.ProxyCacheManager.ClustersPath + IndexServer.CLUSTERS_XML_FILE_NAME,
                        searchString, Proxy);
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
                    xmlAnswer = IndexServer.Level3Index(Proxy.ProxyCacheManager.ClustersPath + IndexServer.CLUSTERS_XML_FILE_NAME,
                        catId, subCatId, Proxy);
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
        /// GET request will be sent to <![CDATA[request/queue.xml?u=a01&v=24-05-2012]]> where
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

            if (String.IsNullOrEmpty(date) || date.Equals("0"))
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

                        XmlElement statusCodeXml = xmlDoc.CreateElement("statusCode");
                        if(requestHandler.RCRequest.StatusCode != 0)
                        {
                            statusCodeXml.InnerText = requestHandler.RCRequest.StatusCode.ToString();
                        }
                        itemXml.AppendChild(statusCodeXml);
                        XmlElement errorMessageXml = xmlDoc.CreateElement("errorMessage");
                        errorMessageXml.InnerText = requestHandler.RCRequest.ErrorMessage;
                        itemXml.AppendChild(errorMessageXml);
                    }
                }
            }
            PrepareXMLRequestAnswer();
            return new Response(xmlDoc.InnerXml);
        }

        /// <summary>
        /// Sends the frame page to the client.
        /// GET request will be sent to <![CDATA[request/result.xml?n=5&p=1&s=searchstring]]> where
        /// n is the maximum number of items per page, the number of <![CDATA[<item>]]> allowed in this file
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
                Proxy.CachePath, offsets[0], resultAmounts[0], true, -1);
            // Query the Wiki index (this can throw timeout Exception)
            SearchResults wikiResults;
            try
            {
                wikiResults = Proxy.WikiWrapper.
                    Query(queryString, offsets[1], resultAmounts[1]);
            }
            catch (Exception e)
            {
                Logger.Debug("Wiki search failed: ", e);
                wikiResults = new SearchResults();
            }

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
        public static void AppendSearchResultsXMLElements(IEnumerable<SearchResult> results, XmlDocument doc, XmlElement elem)
        {
            foreach (SearchResult result in results)
            {
                elem.AppendChild(BuildSearchResultXmlElement(doc,
                    result.Title, result.URI, result.ContentSnippet));
            }
        }

        /// <summary>
        /// Sends the frame page to the client.
        /// GET request will be sent to <![CDATA[request/search.xml?p=1&s=searchstring]]> where
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
            _rcRequest = new RCRequest(_proxy, (HttpWebRequest)WebRequest.Create(googleSearchString));
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
                            linkObject.AnchorText, linkObject.Uri, linkObject.ContentSnippet));
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
        private static XmlElement BuildSearchResultXmlElement(XmlDocument doc, string title, string url, string snippet)
        {
            XmlElement itemXml = doc.CreateElement("item");

            XmlElement titleXml = doc.CreateElement("title");
            titleXml.InnerText = title;
            itemXml.AppendChild(titleXml);
            XmlElement urlXml = doc.CreateElement("url");
            urlXml.InnerText = url;
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
        /// <param name="amount">The max. number of suggestions to return.</param>
        public Response LinkSuggestions(string url, string anchorText, string surroundingText, int amount)
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
            string relFileName = CacheManager.GetRelativeCacheFileName(targetUri.ToString(), "GET");
            GlobalCacheItem gci = Proxy.ProxyCacheManager.GetGlobalCacheItem(relFileName);
            if (gci != null && !Properties.Network.Default.LS_DEBUG)
            {
                // non-debug, cached
                suggestionsXml.InnerText = LINK_SUGGESTIONS_CACHED_TEXT;
                suggestionsXml.SetAttribute("downloadTime",
                    Proxy.ProxyCacheManager.GetGlobalCacheRCData(relFileName).
                    downloadTime.ToString("dd'/'MM'/'yyyy"));
            }
            else if (gci == null && Properties.Network.Default.LS_DEBUG)
            {
                // debug, non-cached
                suggestionsXml.InnerText = LINK_SUGGESTIONS_CACHED_TEXT;
                suggestionsXml.SetAttribute("downloadTime", "not yet");
            }
            else
            {
                suggestionsXml.SetAttribute("status", Proxy.NetworkStatus.ToString().ToLower());
                // Add the results to the XML
                AppendSearchResultsXMLElements(LinkSuggestionHelper.GetLinkSuggestions(url, RefererUri, anchorText,
                    surroundingText, amount, Proxy), xmlDoc, suggestionsXml);
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
        /// Asks if the user satisfaction survey is due to be shown.
        /// </summary>
        /// <param name="endOfSession">True, if we know that this is the end of the session.</param>
        public Response IsSurveyDue(bool endOfSession)
        {
            _clientHttpContext.Response.ContentType = "text/plain";
            if ( // The setting must be true
                Properties.Settings.Default.SHOW_SURVEY
                // The survey must not have been shown alredy that day
                && GetLastSurveyTime(UserIDCookieValue).Add(MIN_SURVEY_INTERVAL).CompareTo(DateTime.Now) < 0
                // It must be at the end of the session or after the middle of an avg. session length of that user
                && (endOfSession || Proxy.SessionManager.GetTimeOfLogin(UserIDCookieValue).
                    Add(new TimeSpan(Proxy.SessionManager.GetAvgSessionLength(UserIDCookieValue).Ticks / 2)).
                    CompareTo(DateTime.Now) < 0))
            {
                // The client will show the survey.
                SetLastSurveyTime(UserIDCookieValue);
                return new Response("True");
            }
            return new Response("False");
        }

        /// <summary>
        /// Client signs up for a new account.
        /// </summary>
        /// <param name="username">The username.</param>
        /// <param name="password">The password</param>
        public Response SignupRequest(string username, string password)
        {
            int userID = Proxy.SessionManager.UserID(username);
            if (userID != -1)
            {
                return PageWithErrorMessage("signup.html", "wrong_username", "Username exists already.", username);
            }
            // Sign up
            int custid = Proxy.SessionManager.SignUpUser(username, password);
            // Log
            Logger.Info("A new user signed up with id " + custid + ": " + username);

            // Send log page (actually without error message), but with username already in login field.
            return PageWithErrorMessage("login.html", "wrong_username", "", username);
        }

        /// <summary>
        /// Tries to log a user in.
        /// </summary>
        /// <param name="username">The username.</param>
        /// <param name="password">The password.</param>
        /// <param name="search">The search part of the location where to redirect to on success.</param>
        /// <returns></returns>
        public Response LoginRequest(string username, string password, string search)
        {
            int userID = Proxy.SessionManager.UserID(username);
            if (userID == -1)
            {
                return PageWithErrorMessage("login.html", "wrong_username", "Username does not exist.", username);
            }
            else if (!Proxy.SessionManager.IsCorrectPW(username, password))
            {
                return PageWithErrorMessage("login.html", "wrong_password", "Password is incorrect.", username);
            }

            // User+PW correct
            Logger.Metric(userID, "Login.");
            Proxy.SessionManager.LogUserIn(ClientIP, userID);

            // Set cookies.
            Cookie idCookie = new Cookie("uid", "" + userID, "/");
            Cookie nameCookie = new Cookie("uname", username, "/");
            DateTime expiryDate = Proxy.SessionManager.GetSessionExpiryDate(userID);
            idCookie.Expires = expiryDate;
            nameCookie.Expires = expiryDate;

            // We must use Headers.Add (Not AddHeader, not SetCookie and not AppendCookie)
            // as in the other cases only a comma separated list in one Set-Cookie would be created,
            // which browsers do not understand. C# sucks.
            _clientHttpContext.Response.Headers.Add("Set-Cookie", idCookie.ToCookieString());
            _clientHttpContext.Response.Headers.Add("Set-Cookie", nameCookie.ToCookieString());
            
            // Redirect to the User Homepage.
            _clientHttpContext.Response.Redirect("/trotro-user.html" + search);
            return new Response();
        }

        /// <summary>
        /// Sends a page (login or signup), but embeds an error message and puts the username back into the field.
        /// </summary>
        /// <param name="pageName">The name of the page. Should be "login.html" or "signup.html".</param>
        /// <param name="spanId">The span id for the error message.</param>
        /// <param name="errorMessage">The error message.</param>
        /// <param name="username">The username.</param>
        private Response PageWithErrorMessage(string pageName, string spanId, string errorMessage, string username)
        {
            string fileName = Proxy.UIPagesPath + pageName;

            HtmlDocument doc = new HtmlDocument();
            doc.Load(fileName);
            HtmlNode errorNode = doc.DocumentNode.SelectSingleNode(String.Format("//span[@id='{0}']", spanId));
            errorNode.InnerHtml = errorMessage;
            HtmlNode usernameNode = doc.DocumentNode.SelectSingleNode("//input[@id='user']");
            usernameNode.SetAttributeValue("value", username);

            _clientHttpContext.Response.ContentType = "text/html";
            return new Response(doc.DocumentNode.OuterHtml, false);
        }

        /// <summary>
        /// Logs a user out.
        /// </summary>
        /// <returns></returns>
        public Response LogoutRequest()
        {
            Logger.Metric(UserIDCookieValue, "Logout.");
            Proxy.SessionManager.LogUserOut(UserIDCookieValue);
            SendLogoutCookies();

            return new Response();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="rating"></param>
        /// <param name="hadIssues"></param>
        /// <param name="problems"></param>
        /// <param name="comments"></param>
        /// <returns></returns>
        public Response UserSatisfactionRequest(int rating, bool hadIssues, string problems, string comments)
        {
            // Remove newlines from the strings, so that everything will be in one line.
            problems = RegExs.NEWLINE_REGEX.Replace(problems, " ");
            comments = RegExs.NEWLINE_REGEX.Replace(comments, " ");

            Logger.Metric(UserIDCookieValue,
                String.Format("User satisfaction. Rating: {0}, Had issues: {1}, Problems: {2}, Comments: {3}",
                rating, hadIssues, problems, comments));
            // Send a html page just closing the window.
            return new Response("<html><body><script>window.close();</script></body></html>");
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
            LocalRequestHandler removedLRH = Proxy.RemoveRequest(UserIDCookieValue, requestId);
            if (removedLRH == null)
            {
                return new Response("Request did not exist");
            }

            if (removedLRH.RequestStatus == Status.Downloading && removedLRH.OutstandingRequests == 0)
            {
                // delegate removal also to remote proxy for active requests that others haven't requested
                return DelegateToRemoteProxy();
            }
            return new Response("Removed request.");
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
            _rcRequest.SetProxyAndTimeout(Proxy.RemoteProxy, _requestTimeout);
            HttpWebResponse response = (HttpWebResponse)_rcRequest.GenericWebRequest.GetResponse();
            return createResponse(response);
        }
        #endregion
    }
}
