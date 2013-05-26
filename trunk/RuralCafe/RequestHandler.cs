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
using System.Text.RegularExpressions;
using System.Collections.Specialized;
using System.Reflection;

namespace RuralCafe
{
    /// <summary>
    /// Abstract class for local and remote request classes.
    /// Each instance of RequestHandler has at least one RCRequest object associated with it.
    /// </summary>
    public abstract class RequestHandler
    {
        // response status
        // XXX: kind of ugly since this is being used by both the Generic/Local/RemoteRequest and RCRequests
        // TODO: downloading && offline -> waiting (new status) ???
        public enum Status
        {
            Failed = -1,
            Pending = 0,
            Downloading = 1,
            Completed = 2
        };
        /// <summary>
        /// Richness setting for requests.
        /// </summary>
        public enum Richness
        {
            // Do NOT give away value zero! Used to distinguish as there is no null for enums.
            Normal = 1,
            Low = 2
        }

        // Util consts
        /// <summary>
        /// The name of our homepage.
        /// </summary>
        private static string rcPage = "http://www.ruralcafe.net/";
        private static string rcPageWithoutWWW = "http://ruralcafe.net/";
        /// <summary>
        /// Matches "localhost" or "127.0.0.1" followed by anything but a dot.
        /// </summary>
        private static Regex localAddressRegex = new Regex(@"(?<add1>(localhost|127\.0\.0\.1))(?<add2>[^\.])");
        /// <summary>
        /// The Name of the cookie for the user id.
        /// </summary>
        private static string cookieUserID = "uid";

        // timeouts
        public const int LOCAL_REQUEST_PACKAGE_DEFAULT_TIMEOUT = Timeout.Infinite; // in milliseconds
        public const int REMOTE_REQUEST_PACKAGE_DEFAULT_TIMEOUT = 180000; // in milliseconds
        public const int WEB_REQUEST_DEFAULT_TIMEOUT = 60000; // in milliseconds
        public const int HEAD_REQUEST_DEFAULT_TIMEOUT = 1000; // in milliseconds
        
        // ID
        protected int _requestId;
        // number of outstanding requests for this object
        protected int _outstandingRequests;

        // proxy this request belongs to
        protected RCProxy _proxy;

        // client info
        protected HttpListenerContext _clientHttpContext;
        protected IPAddress _clientAddress;

        // the actual request object variables
        protected HttpListenerRequest _originalRequest;
        protected RCRequest _rcRequest;
        protected int _requestTimeout; // timeout in milliseconds

        // filename for the package
        protected string _packageFileName;

        // If request is valid
        private bool _validRequest = true;

        // benchmarking unused
        //protected DateTime requestReceived;
        //protected DateTime requestCompleted;

        /// <summary>
        /// Constructor for the request.
        /// </summary>
        /// <param name="proxy">Proxy that this request belongs to.</param>
        /// <param name="socket">Socket on which the request came in on.</param>
        protected RequestHandler(RCProxy proxy, HttpListenerContext context)
        {
            _proxy = proxy;
            _clientHttpContext = context;
            _originalRequest = context.Request;
            _requestId = _proxy.GetAndIncrementNextRequestID();
            if (context != null)
            {
                _clientAddress = ((IPEndPoint)(context.Request.RemoteEndPoint)).Address;
            }
        }
        /// <summary>
        /// DUMMY used for request matching.
        /// XXX: Not the cleanest implementation need to instantiate a whole object just to match
        /// </summary> 
        public RequestHandler()
        {
            _outstandingRequests = 1;
        }

        /// <summary>
        /// Destructor for the request.
        /// </summary>
        ~RequestHandler()
        {
            // cleanup stuff
        }

        /// <summary>
        /// Overriding Equals() from base object.
        /// Instead of testing for equality of reference,
        /// just check if the request URIs are equal
        /// </summary>        
        public override bool Equals(object obj)
        {
            return (ItemId.Equals(((RequestHandler)obj).ItemId));
        }
        
        /// <summary>
        /// Overriding GetHashCode() from base object.
        /// Just use the hash code of the RequestUri.
        /// </summary>        
        public override int GetHashCode()
        {
            return RequestUri.GetHashCode();
        }

        #region Property Accessors

        /// <summary>Request ID</summary>
        public int RequestId
        {
            get { return _requestId; }
        }
        /// <summary>Unique Item ID</summary>
        public string ItemId
        {
            get { return _rcRequest.ItemId; }
        }
        // accessors for the underlying RCRequest
        /// <summary>Outstanding requests for this URI, i.e. the total number of times it appears in the user queues.</summary>
        public int OutstandingRequests
        {
            set { _outstandingRequests = value; }
            get { return _outstandingRequests; }
        }

        /// <summary>The proxy that this request belongs to.</summary>
        public RCProxy Proxy
        {
            get { return _proxy; }
        }
        /// <summary>The request.</summary>
        public RCRequest RCRequest
        {
            set { _rcRequest = value; }
            get { return _rcRequest; }
        }
        /// <summary>The original request.</summary>
        public HttpListenerRequest OriginalRequest
        {
            set { _originalRequest = value; }
            get { return _originalRequest; }
        }
        /// <summary>The Socket.</summary>
        public HttpListenerContext Context
        {
            get { return _clientHttpContext; }
        }
        /// <summary>The name of the package if this is to be a package.</summary>
        public string PackageFileName
        {
            get { return _packageFileName; }
        }
        /// <summary>Time this request started.</summary>
        public DateTime StartTime
        {
            get { return _rcRequest.StartTime; }
        }
        /// <summary>Time this request finished.</summary>
        public DateTime FinishTime
        {
            set { _rcRequest.FinishTime = value; }
            get { return _rcRequest.FinishTime; }
        }
        
        // accessors for the underlying RCRequest
        /// <summary>Status of the request.</summary>
        public Status RequestStatus
        {
            set { _rcRequest.RequestStatus = value; }
            get { return _rcRequest.RequestStatus; }
        }
        /// <summary>URI of the request.</summary>
        public string RequestUri
        {
            get { return _rcRequest.Uri; }
        }
        /// <summary>Anchor text of the request.</summary>
        public string AnchorText
        {
            set { _rcRequest.AnchorText = value; }
            get { return _rcRequest.AnchorText; }
        }
        /// <summary>URI of the referrer.</summary>
        public string RefererUri
        {
            set { _rcRequest.RefererUri = value; }
            get { return _rcRequest.RefererUri; }
        }
        /// <summary>File name of the file if the RCRequest is stored in the cache.</summary>
        public string FileName
        {
            set { _rcRequest.FileName = value; }
            get { return _rcRequest.FileName; }
        }
        /// <summary>Hashed base name of the file if the RCRequest is stored in the cache.</summary>
        public string HashPath
        {
            set { _rcRequest.HashPath = value; }
            get { return _rcRequest.HashPath; }
        }
        /// <summary>Name of the file if the RCRequest is stored in the cache.</summary>
        public string CacheFileName
        {
            set { _rcRequest.CacheFileName = value; }
            get { return _rcRequest.CacheFileName; }
        }

        /// <summary>
        /// Gets the Value. -1 if cookie not set.
        /// </summary>
        public int UserIDCookieValue
        {
            get
            {
                return _originalRequest.Cookies[cookieUserID] == null ?
                    -1 : Int32.Parse(_originalRequest.Cookies[cookieUserID].Value);
            }
        }

        #endregion

        /// <summary>Checks whether the request is blacklisted by the proxy.</summary>
        public bool IsBlacklisted(string uri)
        {
            return _proxy.IsBlacklisted(uri);
        }

        /// <summary>
        /// Appends a dot to "localhost" or "127.0.0.1". This is done to prevent .NET from
        /// bypassing the remote proxy for local addresses.
        /// 
        /// If there is already a dot nothing is done.
        /// </summary>
        /// <param name="address">The address.</param>
        /// <returns>The new address.</returns>
        public static String AppendDotToLocalAddress(String address)
        {
            return localAddressRegex.Replace(address, "${add1}.${add2}");
        }

        /// <summary>
        /// Prepares a new RequestHandler. With one (local or remote/internal or not) is decided.
        /// </summary>
        /// <param name="proxy"></param>
        /// <param name="context"></param>
        /// <returns>The new request handler.</returns>
        public static RequestHandler PrepareNewRequestHandler(RCProxy proxy, HttpListenerContext context)
        {
            HttpListenerRequest originalRequest = context.Request;
            try
            {
                if (proxy is RCLocalProxy)
                {
                    if (IsRCRequest(originalRequest))
                    {
                        return new LocalInternalRequestHandler((RCLocalProxy)proxy, context);
                    }
                    else
                    {
                        return new LocalRequestHandler((RCLocalProxy)proxy, context);
                    }
                }
                else
                {
                    if (IsRCRequest(originalRequest))
                    {
                        return new RemoteInternalRequestHandler((RCRemoteProxy)proxy, context);
                    }
                    else
                    {
                        return new RemoteRequestHandler((RCRemoteProxy)proxy, context);
                    }
                }
            }
            catch (Exception e)
            {
                // temp handler: Internal is not needed
                RequestHandler temp;
                if (proxy is RCLocalProxy)
                {
                    temp = new LocalRequestHandler((RCLocalProxy)proxy, context);
                }
                else
                {
                    temp = new RemoteRequestHandler((RCRemoteProxy)proxy, context);
                }
                String errmsg = "error handling request: ";
                if (originalRequest != null)
                {
                    errmsg += " " + originalRequest.RawUrl.ToString(); ;
                }
                errmsg += " " + e.GetType() + ": " + e.Message + "\n" + e.StackTrace;
                temp.LogDebug(errmsg);
                // Erroneous request has been handled
                temp._validRequest = false;
                return temp;
            }
        }

        /// <summary>
        /// Main entry point for listener threads for a HttpWebRequest.
        /// </summary>
        public void Go()
        {
            string refererUri = "";

            try
            {
                if (!_validRequest)
                {
                    // finally will still be executed!
                    return;
                }
                // get the referer URI
                refererUri = _originalRequest.UrlReferrer != null ? _originalRequest.UrlReferrer.ToString() : "";

                if (CreateRequest(_originalRequest, refererUri))
                {
                    _packageFileName = _proxy.PackagesPath + _rcRequest.HashPath + _rcRequest.FileName + ".gzip";

                    // XXX: need to avoid duplicate request/response logging when redirecting e.g. after an add
                    // handle the request
                    if (IsRCRequest())
                    {
                        HandleRequest();
                    }
                    else {
                        LogRequest();
                        HandleRequest();
                        LogResponse();
                    }
                }
                else
                {
                    // XXX: was streaming these unparsable URIs, but this is disabled for now
                    // XXX: mangled version of the one in LocalRequestHandler, duplicate, and had to move the StreamTransparently() up to this parent class
                    LogDebug("streaming: " + _originalRequest.RawUrl.ToString() + " to client.");
                    long bytesSent = StreamTransparently();
                    return;
                }
            }
            catch (Exception e)
            {
                String errmsg = "error handling request: ";
                if (_originalRequest != null)
                {
                    errmsg += " " + _originalRequest.RawUrl.ToString(); ;
                }
                errmsg += " " + e.GetType() + ": " + e.Message + "\n" + e.StackTrace;
                LogDebug(errmsg);
            }
            finally
            {
                // disconnect and close the socket
                if (_clientHttpContext != null)
                {
                    _clientHttpContext.Response.Close();
                    
                }
                // XXX: _rcRequest.FinishTime = DateTime.Now;
            }
            // returning from this method will terminate the thread
        }

        /// <summary>
        /// Creates and handles the logged request
        /// logEntry format: (requestId, startTime, clientAddress, requestedUri, refererUri, [status])
        /// </summary>
        public bool HandleLogRequest(List<string> logEntry)
        {
            if (!(logEntry.Count >= 5))
            {
                return false;
            }

            try
            {
                int requestId = Int32.Parse(logEntry[0]);
                DateTime startTime = DateTime.Parse(logEntry[1]);
                IPAddress clientAddress = IPAddress.Parse(logEntry[2]);
                // FIXME no idea here!
                //_originalRequest = (HttpWebRequest) WebRequest.Create(logEntry[3]);
                string refererUri = logEntry[4];
                Status requestStatus = Status.Pending;
                if (logEntry.Count == 6)
                {
                    // Adapted to read the text of the status instead of the number.
                    // Therefore old logs won't work anymore.
                    requestStatus = (Status) Enum.Parse(typeof(Status),logEntry[5]);
                }

                if (CreateRequest(_originalRequest, refererUri))
                {
                    // from log book-keeping
                    _requestId = requestId;
                    _rcRequest.StartTime = startTime;
                    _clientAddress = clientAddress;
                    if (requestStatus == Status.Completed)
                    {
                        // Completed requests should not be added to the GLOBAL queue
                        _rcRequest.RequestStatus = requestStatus;
                    }
                    
                    _packageFileName = _proxy.PackagesPath + _rcRequest.HashPath + _rcRequest.FileName + ".gzip";

                    // XXX: need to avoid duplicate request/response logging when redirecting e.g. after an add
                    // handle the request
                    if (IsRCRequest())
                    {
                        HandleRequest();
                    }
                    else
                    {
                        LogRequest();
                        HandleRequest();

                        LogResponse();
                        if (requestStatus == Status.Completed)
                        {
                            // Completed requests should have a fake response in the log to indicate they're completed
                            LogServerResponse();
                        }
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                // do nothing
                String errmsg = "error handling request: ";
                if (_originalRequest != null)
                {
                    errmsg += " " + _originalRequest.RawUrl.ToString(); ;
                }
                errmsg += " " + e.GetType() + ": " + e.Message + "\n" + e.StackTrace;
                LogDebug(errmsg); LogDebug("error handling request: " + _originalRequest.RawUrl.ToString() + " " + e.Message + e.StackTrace);
            }
            return false;
        }

        /// <summary>
        /// Creates RCRequest object for the request.
        /// </summary>
        /// <param name="request">The HTTP request.</param>
        /// <param name="refererUri">The referrer.</param>
        /// <param name="recvString">The whole received string.</param>
        /// <returns></returns>
        protected bool CreateRequest(HttpListenerRequest request, string refererUri)
        {
            if (Util.IsValidUri(request.RawUrl.ToString()))
            {
                // create the request object
                _rcRequest = new RCRequest(this, Util.CreateWebRequest(request), "", refererUri,
                Util.ReceiveBody(request));
                _rcRequest.GenericWebRequest.Referer = refererUri;
                return true;
            }
            return false;
        }

        /// <summary>Abstract method for proxies to handle requests.</summary>
        public abstract Status HandleRequest();


        /// <summary>
        /// Stream the request to the server and the response back to the client transparently.
        /// XXX: does not have gateway support or tunnel to remote proxy support
        /// </summary>
        /// <returns>The length of the streamed result.</returns>
        protected long StreamTransparently()
        {
            //Util.StreamBody(_originalRequest, _rcRequest.GenericWebRequest);
            // Stream parameters, if we have non GET/HEAD
            Util.SendBody(_rcRequest.GenericWebRequest, _rcRequest.Body);
            WebResponse serverResponse = _rcRequest.GenericWebRequest.GetResponse();
            return StreamToClient(serverResponse.GetResponseStream());
        }

        /// <summary>
        /// Stream the file from the cache to the client.
        /// </summary>
        /// <param name="fileName">Name of the file to stream to the client.</param>
        /// <returns>Bytes streamed from the cache to the client.</returns>
        protected long StreamFromCacheToClient(string fileName)
        {
            // make sure the file exists.
            FileInfo f;
            try
            {
                int offset = fileName.LastIndexOf("?"); // laura: check get parameters
                string htmlQuery = "";
                if (offset >= 0)
                {
                    htmlQuery = fileName.Substring(offset + 1);
                    fileName = fileName.Substring(0, offset);
                }
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
                fs = f.Open(FileMode.Open, FileAccess.Read);

            }
            catch (Exception e)
            {
                LogDebug("problem opening file: " + fileName + " " + e.Message);
                return -1;
            }
            return StreamToClient(fs);
        }

        /// <summary>
        /// Streams data from a Stream to the client socket.
        /// </summary>
        /// <param name="ms">Data source.</param>
        /// <returns>Number of bytes streamed.</returns>
        protected long StreamToClient(Stream ms)
        {
            Stream output = _clientHttpContext.Response.OutputStream;
            try
            {
                return Util.Stream(ms, output);
            }
            catch (Exception e)
            {
                // XXX: don't think this is the way to handle such an error.
                SendErrorPage(HttpStatusCode.InternalServerError, "problem streaming the package from disk to client: " + e.StackTrace + " " + e.Message);
            }
            finally
            {
                if (ms != null)
                {
                    ms.Close();
                }
            }
            return -1;
        }

        /// <summary>
        /// Extracts the result links from a google results page.
        /// XXX: Probably broken all the time due to Google's constantly changing HTML format.
        /// </summary>
        /// <param name="rcRequest">Request to make.</param>
        /// <returns>List of links.</returns>
        public LinkedList<RCRequest> ExtractGoogleResults(RCRequest rcRequest)
        {
            string[] stringSeparator = new string[] { "<cite>" };
            LinkedList<RCRequest> resultLinks = new LinkedList<RCRequest>();
            string fileString = Util.ReadFileAsString(rcRequest.CacheFileName);
            string[] lines = fileString.Split(stringSeparator, StringSplitOptions.RemoveEmptyEntries);

            // get links
            int pos;
            string currLine;
            string currUri;
            string currTitle;
            // stagger starting index by 1 since first split can't be a link
            for (int i = 0; i < lines.Length - 1; i++)
            {
                currLine = (string)lines[i];
                currTitle = "";
                // get the title of the page as well
                if ((pos = currLine.LastIndexOf("<a href=")) >= 0)
                {
                    currTitle = currLine.Substring(pos);
                    if ((pos = currTitle.IndexOf(">")) >= 0)
                    {
                        currTitle = currTitle.Substring(pos + 1);
                        if ((pos = currTitle.IndexOf("</a>")) >= 0)
                        {
                            currTitle = currTitle.Substring(0, pos);
                            currTitle = Util.StripTagsCharArray(currTitle);
                            currTitle = currTitle.Trim();
                        }
                    }
                }

                currLine = (string)lines[i + 1];
                // to the next " symbol
                if ((pos = currLine.IndexOf("</cite>")) > 0)
                {
                    currUri = currLine.Substring(0, pos);

                    if ((pos = currUri.IndexOf(" - ")) > 0)
                    {
                        currUri = currUri.Substring(0, pos);
                    }

                    currUri = Util.StripTagsCharArray(currUri);
                    currUri = currUri.Trim();

                    // instead of translating to absolute, prepend http:// to make webrequest constructor happy
                    currUri = AddHttpPrefix(currUri);

                    if (!Util.IsValidUri(currUri))
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
                    RCRequest currRCRequest = new RCRequest(this, (HttpWebRequest)WebRequest.Create(currUri));
                    currRCRequest.AnchorText = currTitle;
                    //currRCRequest.ChildNumber = i - 1;
                    //currRCRequest.SetProxy(_proxy.GatewayProxy, WEB_REQUEST_DEFAULT_TIMEOUT);

                    resultLinks.AddLast(currRCRequest);
                }
            }

            return resultLinks;
        }

        #region RC Headers

        /// <summary>
        /// Adds all headers for the inter-proxy-protocol request. Used in local request handler.
        /// </summary>
        /// <param name="headers">The headers.</param>
        public void AddRCSpecificRequestHeaders(RCSpecificRequestHeaders headers)
        {
            // Iterate through all fields of the RCSpecificRequestHeaders class.
            foreach(FieldInfo field in headers.GetType().GetFields())
            {
                string name = field.Name;
                object value = field.GetValue(headers);
                if (value != null)
                {
                    RCRequest.GenericWebRequest.Headers.Add(name, value.ToString());
                }
            }
        }

        /// <summary>
        /// Used in remote request handler.
        /// </summary>
        /// <returns>Gets all RC specific request headers.</returns>
        public RCSpecificRequestHeaders GetRCSpecificRequestHeaders()
        {
            RCSpecificRequestHeaders result = new RCSpecificRequestHeaders();
            // Iterate through all fields of the RCSpecificRequestHeaders class.
            foreach (FieldInfo field in result.GetType().GetFields())
            {
                string name = field.Name;
                string value = _originalRequest.Headers[name];
                if (value != null)
                {
                    field.SetValue(result, Convert.ChangeType(value, field.FieldType));
                }
            }
            return result;
        }

        /// <summary>
        /// Adds all headers for the inter-proxy-protocol response. Used in remote request handler.
        /// </summary>
        /// <param name="headers">The headers.</param>
        public void AddRCSpecificResponseHeaders(RCSpecificResponseHeaders headers)
        {
            // Iterate through all fields of the RCSpecificRequestHeaders class.
            foreach (FieldInfo field in headers.GetType().GetFields())
            {
                string name = field.Name;
                object value = field.GetValue(headers);
                if (value != null)
                {
                    _clientHttpContext.Response.AddHeader(name, value.ToString());
                }
            }
        }

        /// <summary>
        /// Used in local request handler/Package.unpack(..)
        /// </summary>
        /// <returns>Gets all RC specific response headers.</returns>
        public RCSpecificResponseHeaders GetRCSpecificResponseHeaders()
        {
            RCSpecificResponseHeaders result = new RCSpecificResponseHeaders();
            // Iterate through all fields of the RCSpecificRequestHeaders class.
            foreach (FieldInfo field in result.GetType().GetFields())
            {
                string name = field.Name;
                string value = RCRequest.GenericWebResponse.Headers[name];
                if (value != null)
                {
                    field.SetValue(result, Convert.ChangeType(value, field.FieldType));
                }
            }
            return result;
        }

        #endregion
        #region Methods for Checking Requests

        /// <summary>
        /// Checks if the original request is a RC request.
        /// </summary>
        /// <returns>If it is or not.</returns>
        protected bool IsRCRequest()
        {
            return IsRCRequest(_originalRequest);
        }
        /// <summary>
        /// Checks if the request is a RC request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>If it is or not.</returns>
        protected static bool IsRCRequest(HttpWebRequest request)
        {
            return IsRCRequest(request.RequestUri.ToString());
        }
        /// <summary>
        /// Checks if the request is a RC request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>If it is or not.</returns>
        protected static bool IsRCRequest(HttpListenerRequest request)
        {
            return IsRCRequest(request.RawUrl);
        }

        /// <summary>
        /// Checks if the uri is a request to rural cafe
        /// </summary>
        /// <param name="uri">The URI.</param>
        /// <returns>If it is or not.</returns>
        protected static bool IsRCRequest(string uri)
        {
            return uri.StartsWith(rcPage) || uri.StartsWith(rcPageWithoutWWW);
        }

        /// <summary>
        /// Checks if the request is GET or HEAD.
        /// </summary>
        /// <returns>True if it is a GET or HEAD request, false if otherwise.</returns>
        protected bool IsGetOrHeadHeader()
        {
            return (_rcRequest.GenericWebRequest.Method == "GET" ||
                _rcRequest.GenericWebRequest.Method == "HEAD");
        }

        /// <summary>
        /// Checks if the page is cacheable.
        /// Currently, just based on the file name length and HTTP Method.
        /// XXX: This should be changed so that even long file names can be cached.
        /// </summary>
        /// <returns>True if cacheable, false if not. </returns>
        protected bool IsCacheable()
        {
            return IsGetOrHeadHeader() && Util.IsNotTooLongFileName(_rcRequest.CacheFileName);
        }

        /// <summary>
        /// Checks if the file is cached.
        /// </summary>
        /// <param name="fileName">Name of the file to check.</param>
        /// <returns>True if cached, false if not.</returns>
        protected bool IsCached(string fileName)
        {
            if (fileName == null || fileName.Equals("") || (fileName.Length > 248))
            {
                return false;
            }

            try
            {
                FileInfo f = new FileInfo(fileName);
                if (f.Exists)
                {
                    return true;
                }
            }
            catch (Exception e)
            {
                LogDebug("Error getting file info: " + e.StackTrace + " " + e.Message);
                return false;
            }

            return false;
        }

        /// <summary>
        /// Checks whether the request is timed out based on _timeout.
        /// </summary>
        /// <returns>True or false for timed out or not.</returns>
        public bool IsTimedOut()
        {
            if (_requestTimeout == Timeout.Infinite)
            {
                return false;
            }

            DateTime currTime = DateTime.Now;
            TimeSpan elapsed = currTime.Subtract(_rcRequest.StartTime);
            if (elapsed.TotalMilliseconds < _requestTimeout)
            {
                return false;
            }
            return true;
        }

        #endregion
        #region HTTP Helper Functions

        /// <summary>
        /// Adds "http://" to the given URI, if it does not start with it already.
        /// </summary>
        /// <param name="uri">The current URI.</param>
        /// <returns>The new URI.</returns>
        public static String AddHttpPrefix(String uri)
        {
            if (!uri.StartsWith("http://"))
            {
                return "http://" + uri;
            }
            return uri;
        }

        /// <summary>
        ///  Write an error response to the client.
        /// </summary>
        /// <param name="status">Error status.</param>
        /// <param name="message">Any additional text.</param>
        protected void SendErrorPage(HttpStatusCode status, string message)
        {
            _clientHttpContext.Response.StatusCode = (int)status;
            _clientHttpContext.Response.ContentType = "text/plain";
            SendMessage(message);
            LogDebug(status + " " + message);
        }

        /// <summary>
        /// Sends a string to the client socket. Must not be called more than once per request!
        /// </summary>
        /// <param name="strMessage">The string message to send.</param>
        /// <returns>Returns the length of the message sent or -1 if failed.</returns>
        protected void SendMessage(string strMessage)
        {
            try
            {
                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(strMessage);
                _clientHttpContext.Response.ContentLength64 = buffer.Length;
                _clientHttpContext.Response.OutputStream.Write(buffer, 0, buffer.Length);
                //StreamWriter writer = new StreamWriter(_clientHttpContext.Response.OutputStream);
                //writer.Write(strMessage);
            }
            catch (Exception e)
            {
                LogDebug("socket closed for some reason " + e.StackTrace + " " + e.Message);
            }
        }

        #endregion
        #region Logging

        /// <summary>
        /// Log the request from the client.
        /// </summary>
        public void LogRequest()
        {
            string str = _rcRequest.StartTime + " " + _clientAddress.ToString() +
                         " " + _rcRequest.GenericWebRequest.Method + " " + RequestUri +
                         " REFERER " + RefererUri + " " + 
                         RequestStatus + " " + _rcRequest.FileSize;
            _proxy.WriteMessage(_requestId, str);
        }

        /// <summary>
        /// Log our response to the client.
        /// </summary>
        public void LogResponse()
        {
            string str = _rcRequest.FinishTime + " RSP " + RequestUri + " " + 
                        RequestStatus + " " + _rcRequest.FileSize;
            _proxy.WriteMessage(_requestId, str);
        }

        /// <summary>
        /// Log our response to the client.
        /// </summary>
        public void LogServerResponse()
        {
            string str = _rcRequest.FinishTime + " RSP " + _originalRequest.RawUrl.ToString() + " " +
                        RequestStatus + " " + _rcRequest.FileSize;
            _proxy.WriteMessage(_requestId, str);
        }

        /// <summary>
        /// Logs any debug messages.
        /// </summary>
        public void LogDebug(string str)
        {
            _proxy.WriteDebug(_requestId, str);
        }

        #endregion
    }
}
