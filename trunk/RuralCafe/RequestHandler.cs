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
using log4net;
using RuralCafe.Util;
using Newtonsoft.Json;

namespace RuralCafe
{
    /// <summary>
    /// Abstract class for local and remote request classes.
    /// Each instance of RequestHandler has at least one RCRequest object associated with it.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public abstract class RequestHandler
    {
        /// <summary>
        /// The status for a request.
        /// 
        /// XXX: kind of ugly since this is being used by both the Generic/Local/RemoteRequest and RCRequests
        /// TODO: downloading && offline -> waiting (new status) ???
        /// </summary>
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
        public const string RC_PAGE = "http://www.ruralcafe.net/";
        public const string RC_PAGE_WITHOUT_WWW = "http://ruralcafe.net/";
        private static readonly Regex redirRegex = new Regex(@"HTTP/1\.1 301 Moved PermanentlyLocation: (?<uri>\S+)");

        /// <summary>
        /// The Name of the cookie for the user id.
        /// </summary>
        private const string cookieUserID = "uid";

        #region static preparation methods

        /// <summary>
        /// Prepares a new RequestHandler. With one (local or remote/internal or not) is decided.
        /// </summary>
        /// <param name="proxy">The proxy.</param>
        /// <param name="context">The client's context.</param>
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
                temp.Logger.Warn(errmsg, e);
                // Erroneous request has been handled
                temp._validRequest = false;
                return temp;
            }
        }

        #endregion

        // timeouts
        public const int LOCAL_REQUEST_PACKAGE_DEFAULT_TIMEOUT = Timeout.Infinite; // in milliseconds
        public const int REMOTE_REQUEST_PACKAGE_DEFAULT_TIMEOUT = 180000; // in milliseconds
        public const int WEB_REQUEST_DEFAULT_TIMEOUT = 60000; // in milliseconds
        public const int HEAD_REQUEST_DEFAULT_TIMEOUT = 1000; // in milliseconds
        
        // ID
        [JsonProperty]
        protected int _requestId;
        // number of outstanding requests for this object
        [JsonProperty]
        protected int _outstandingRequests;

        // proxy this request belongs to
        protected RCProxy _proxy;

        // client info
        protected HttpListenerContext _clientHttpContext;

        // the actual request object variables
        protected HttpListenerRequest _originalRequest;
        [JsonProperty]
        protected RCRequest _rcRequest;
        // timeout in milliseconds
        [JsonProperty]
        protected int _requestTimeout;

        // filename for the package
        [JsonProperty]
        protected string _packageFileName;

        // If request is valid
        [JsonProperty]
        private bool _validRequest = true;

        // Time of creation in ticks since system start
        [JsonProperty]
        private long _creationTime;

        // benchmarking unused
        //protected DateTime requestReceived;
        //protected DateTime requestCompleted;

        /// <summary>
        /// Constructor for the request.
        /// </summary>
        /// <param name="proxy">Proxy that this request belongs to.</param>
        /// <param name="context">Client context.</param>
        protected RequestHandler(RCProxy proxy, HttpListenerContext context) : this(proxy)
        {
            _clientHttpContext = context;
            _originalRequest = context.Request;
        }

        /// <summary>
        /// Constructor used, when http context is not available any more. E.g. queue deserialization.
        /// </summary>
        /// <param name="proxy">Proxy that this request belongs to.</param>
        protected RequestHandler(RCProxy proxy)
        {
            _proxy = proxy;
            _requestId = _proxy.GetAndIncrementNextRequestID();
            _creationTime = Environment.TickCount;
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
        /// Overriding Equals() from base object.
        /// Instead of testing for equality of reference,
        /// just check if the request cache file names are equal.
        /// </summary>        
        public override bool Equals(object obj)
        {
            if (!(obj is RequestHandler))
            {
                return false;
            }
            return ItemId.Equals(((RequestHandler)obj).ItemId);
        }
        
        /// <summary>
        /// Overriding GetHashCode() from base object.
        /// Just use the hash code of the ItemId.
        /// </summary>        
        public override int GetHashCode()
        {
            return ItemId.GetHashCode();
        }

        /// <summary>Checks whether the request is blacklisted by the proxy.</summary>
        public bool IsBlacklisted(string uri)
        {
            return _proxy.IsBlacklisted(uri);
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
            set { _rcRequest.StartTime = value; }
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
        /// <summary>The time of creation.</summary>
        public long CreationTime
        {
            get { return _creationTime; }
        }
        /// <summary>
        /// The Proxy's Logger.
        /// </summary>
        public ILog Logger
        {
            get { return _proxy.Logger; }
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
                    Logger.Debug("streaming: " + _originalRequest.RawUrl.ToString() + " to client.");
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
                Logger.Warn(errmsg, e);
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
        /// Creates RCRequest object for the request.
        /// </summary>
        /// <param name="request">The HTTP request.</param>
        /// <param name="refererUri">The referrer.</param>
        /// <returns></returns>
        protected bool CreateRequest(HttpListenerRequest request, string refererUri)
        {
            if (HttpUtils.IsValidUri(request.RawUrl.ToString()))
            {
                // create the request object
                _rcRequest = new RCRequest(this, HttpUtils.CreateWebRequest(request), "", refererUri,
                HttpUtils.ReceiveBody(request));
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
            HttpUtils.SendBody(_rcRequest.GenericWebRequest, _rcRequest.Body);
            WebResponse serverResponse = _rcRequest.GenericWebRequest.GetResponse();
            return StreamToClient(serverResponse.GetResponseStream());
        }

        /// <summary>
        /// Stream the file from the cache to the client. Only used by local rh or local internal rh.
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
                    Logger.Warn("file doesn't exist: " + fileName);
                    return -1;
                }
            }
            catch (Exception e)
            {
                Logger.Warn("problem getting file info: " + fileName, e);
                return -1;
            }

            // XXX: We're reading the content so we can redirect if there is a 301 in the file.
            // As soon as metadata will be included somehow, this won't be necessary any more.
            // Then remove this! Reading the file twice is bad!
            string content = Utils.ReadFileAsString(fileName);
            Match match = redirRegex.Match(content);
            if (match.Success)
            {
                _clientHttpContext.Response.Redirect(match.Groups["uri"].Value);
                SendMessage(content);
                return content.Length;
            }

            // Stream file to client
            FileStream fs = null;
            try
            {
                fs = f.Open(FileMode.Open, FileAccess.Read);
            }
            catch (Exception e)
            {
                Logger.Warn("problem opening file: " + fileName, e);
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
                return Utils.Stream(ms, output);
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
            return uri.StartsWith(RC_PAGE) || uri.StartsWith(RC_PAGE_WITHOUT_WWW);
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
            return IsGetOrHeadHeader() && Utils.IsNotTooLongFileName(_rcRequest.CacheFileName);
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
                Logger.Warn("Error getting file info", e);
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
        ///  Write an error response to the client.
        /// </summary>
        /// <param name="status">Error status.</param>
        /// <param name="message">Any additional text.</param>
        protected void SendErrorPage(HttpStatusCode status, string message)
        {
            _clientHttpContext.Response.StatusCode = (int)status;
            _clientHttpContext.Response.ContentType = "text/plain";
            SendMessage(message);
            Logger.Debug("sending error page: " + status + " " + message);
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
            }
            catch (Exception e)
            {
                Logger.Error("socket closed for some reason.", e);
            }
        }

        #endregion
        #region Logging

        /// <summary>
        /// Log the request from the client.
        /// </summary>
        public void LogRequest()
        {
            string str = _requestId + " " + Context.Request.RemoteEndPoint.Address +
                         " " + _rcRequest.GenericWebRequest.Method + " " + RequestUri +
                         " REFERER " + RefererUri + " " + 
                         RequestStatus + " " + _rcRequest.FileSize;
            _proxy.Logger.Info(str);
        }

        /// <summary>
        /// Log our response to the client.
        /// </summary>
        public void LogResponse()
        {
            string str = _requestId +  " RSP " + RequestUri + " " + 
                        RequestStatus + " " + _rcRequest.FileSize;
            _proxy.Logger.Info(str);
        }

        #endregion
    }
}
