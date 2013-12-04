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
using Util;
using Newtonsoft.Json;
using System.Diagnostics;
using RuralCafe.Database;
using RuralCafe.Json;

namespace RuralCafe
{
    /// <summary>
    /// Abstract class for local and remote request classes.
    /// Each instance of RequestHandler has at least one RCRequest object associated with it.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public abstract class RequestHandler : IComparable<RequestHandler>
    {
        /// <summary>
        /// The status for a request.
        /// </summary>
        public enum Status
        {
            /// <summary>Request has failed.</summary>
            Failed = -1,
            /// <summary>Request waits to be downloaded.</summary>
            Pending = 0,
            /// <summary>Request is being downloaded.</summary>
            Downloading = 1,
            /// <summary>Request completed successfully.</summary>
            Completed = 2
        };
        /// <summary>
        /// Richness setting for requests.
        /// </summary>
        public enum Richness
        {
            // Do NOT use value zero! Used to distinguish as there is no null for enums.
            /// <summary>Normal richness: everything is downloaded.</summary>
            Normal = 1,
            /// <summary>Low richness: no images, videos or sounds.</summary>
            Low = 2
        }

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

        // Util consts
        /// <summary>The name of our homepage.</summary>
        public const string RC_PAGE = "http://www.ruralcafe.net/";
        /// <summary>The name of our homepage without preceding www.</summary>
        public const string RC_PAGE_WITHOUT_WWW = "http://ruralcafe.net/";

        /// <summary>The Name of the cookie for the user id.</summary>
        private const string cookieUserID = "uid";

        // timeouts, all in milliseconds
        /// <summary>Time until a local request is aborted.</summary>
        public const int LOCAL_REQUEST_PACKAGE_DEFAULT_TIMEOUT = Timeout.Infinite;
        /// <summary>Time until a remote request is aborted.
        /// FIXME this value is actually only used if there's a gateway proxy after the remote. Is this correct?</summary>
        public const int REMOTE_REQUEST_PACKAGE_DEFAULT_TIMEOUT = 180000;
        /// <summary>Time until a web request (from remote proxy) is aborted.</summary>
        public const int WEB_REQUEST_DEFAULT_TIMEOUT = 60000;
        /// <summary>Time until a HTTP HEAD request is aborted.</summary>
        public const int HEAD_REQUEST_DEFAULT_TIMEOUT = 1000;

        /// <summary>The ID.</summary>
        [JsonProperty]
        protected long _handlerId;
        /// <summary>Number of outstanding requests for this object.</summary>
        [JsonProperty]
        protected int _outstandingRequests;

        /// <summary>Proxy this request belongs to.</summary>/
        protected RCProxy _proxy;

        /// <summary>Client info.</summary>
        protected HttpListenerContext _clientHttpContext;

        /// <summary>The original request object.</summary>
        protected HttpListenerRequest _originalRequest;
        /// <summary>The RC request that is potentially being made.</summary>
        [JsonProperty]
        protected RCRequest _rcRequest;
        /// <summary>Timeout in milliseconds.</summary>
        [JsonProperty]
        protected int _requestTimeout;

        /// <summary>If request is valid.</summary>
        [JsonProperty]
        private bool _validRequest = true;

        /// <summary>Time of creation in ticks since system start.</summary>
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
        /// <param name="requestTimeout">The timeout for the request.</param>
        protected RequestHandler(RCProxy proxy, HttpListenerContext context, int requestTimeout) : this(proxy, requestTimeout)
        {
            _clientHttpContext = context;
            _originalRequest = context.Request;
        }

        /// <summary>
        /// Constructor used, when http context is not available any more. E.g. queue deserialization.
        /// </summary>
        /// <param name="proxy">Proxy that this request belongs to.</param>
        /// <param name="requestTimeout">The timeout for the request.</param>
        protected RequestHandler(RCProxy proxy, int requestTimeout)
        {
            _requestTimeout = requestTimeout;
            _proxy = proxy;
            _handlerId = _proxy.GetAndIncrementNextHandlerID();
            _creationTime = Environment.TickCount;
        }

        /// <summary>
        /// Default constructor for JSON.
        /// </summary>
        public RequestHandler() { }

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
            return RequestId.Equals(((RequestHandler)obj).RequestId);
        }
        
        /// <summary>
        /// Overriding GetHashCode() from base object.
        /// Just use the hash code of the ItemId.
        /// </summary>        
        public override int GetHashCode()
        {
            return RequestId.GetHashCode();
        }

        /// <summary>Checks whether the request is blacklisted by the proxy.</summary>
        public bool IsBlacklisted(string uri)
        {
            return _proxy.IsBlacklisted(uri);
        }

        /// <summary>
        /// Used to organize RHs in a Priority Queue. Ordered by number of requests, descending
        /// and creation time, ascending.
        /// </summary>
        /// <param name="other">The oterh RH.</param>
        /// <returns>The usual.</returns>
        int IComparable<RequestHandler>.CompareTo(RequestHandler other)
        {
            int outstandingRequestsDiff = other._outstandingRequests - this._outstandingRequests;
            if (outstandingRequestsDiff != 0)
            {
                return outstandingRequestsDiff;
            }
            return this._creationTime.CompareTo(other._creationTime);
        }

        #region Property Accessors
        /// <summary>Handler ID</summary>
        public long HandlerId
        {
            get { return _handlerId; }
        }
        /// <summary>Unique Request ID</summary>
        public string RequestId
        {
            get { return _rcRequest.RequestId; }
        }
        // accessors for the underlying RCRequest
        /// <summary>Outstanding requests for this URI, i.e. the total number of times it appears in the user queues.</summary>
        public int OutstandingRequests
        {
            set { _outstandingRequests = value; }
            get { return _outstandingRequests; }
        }
        /// <summary>The proxy that this request belongs to, abtract base class.</summary>
        public RCProxy GenericProxy
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
            set { _rcRequest.PackageFileName = value; }
            get { return _rcRequest.PackageFileName; }
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
        /// <summary>The time of creation.</summary>
        public long CreationTime
        {
            get { return _creationTime; }
        }
        /// <summary>The Proxy's Logger.</summary>
        public ILog Logger
        {
            get { return _proxy.Logger; }
        }
        /// <summary>Gets the Value. -1 if cookie not set.</summary>
        public int UserIDCookieValue
        {
            get
            {
                return _originalRequest.Cookies[cookieUserID] == null ?
                    -1 : Int32.Parse(_originalRequest.Cookies[cookieUserID].Value);
            }
        }
        /// <summary>The client's IP address.</summary>
        public IPAddress ClientIP
        {
            get { return _clientHttpContext.Request.RemoteEndPoint.Address; }
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
            // If we failed to create a request, _rcRequest will be null
            // If we have been deserializing from JSON, the context will be null.
            get { return _rcRequest != null ? _rcRequest.Uri : Context.Request.Url.AbsoluteUri; }
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
            // If we failed to create a request, _rcRequest will be null
            // If we have been deserializing from JSON, the context will be null.
            set { _rcRequest.RefererUri = value; }
            get { return _rcRequest != null ? _rcRequest.RefererUri : Context.Request.UrlReferrer.AbsoluteUri; }
        }

        #endregion

        /// <summary>
        /// Checks if the URI is blacklisted or invalid.
        /// </summary>
        /// <returns>True, if the request is OK.</returns>
        public bool CheckIfBlackListedOrInvalidUri()
        {
            if (!_validRequest)
            {
                return false;
            }

            // check for blacklist
            if (IsBlacklisted(OriginalRequest.RawUrl))
            {
                // For requests being repeated over and over this logging would be bad.
                // XXX SendErrorPage Logs. As this is also for items requested too often,
                // there shouldn't be any log message.
                SendErrorPage(HttpStatusCode.NotFound, "blacklisted: " + OriginalRequest.RawUrl);
                return false;
            }

            // check if the URI is valid
            if (!HttpUtils.IsValidUri(OriginalRequest.RawUrl))
            {
                Logger.Debug("URI invalid: " + OriginalRequest.RawUrl);
                SendErrorPage(HttpStatusCode.BadRequest, "URI invalid: " + OriginalRequest.RawUrl);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Disconnects the socket.
        /// </summary>
        public void DisconnectSocket()
        {
            // disconnect and close the socket
            if (_clientHttpContext != null)
            {
                try
                {
                    _clientHttpContext.Response.Close();
                }
                catch (Exception e)
                {
                    Logger.Warn("Could not close the response context: ", e);
                }
            }
        }

        /// <summary>
        /// Creates RCRequest object for the request. Entry point for new RequestHandler objects.
        /// </summary>
        /// <param name="request">The HTTP request.</param>
        protected void CreateRequest(HttpListenerRequest request)
        {
            // create the request object
            string refererUri = _originalRequest.UrlReferrer != null ? _originalRequest.UrlReferrer.ToString() : "";
            _rcRequest = new RCRequest(_proxy, HttpUtils.CreateWebRequest(request), "", refererUri, HttpUtils.ReceiveBody(request));
        }

        /// <summary>Abstract method for proxies to handle requests.</summary>
        public abstract void HandleRequest(object nullObj);

        /// <summary>Abstract method for handlers to issue requests.</summary>
        public abstract void DispatchRequest(object nullObj);

        /// <summary>
        /// Gets the time since start of the request. Does only make sence to call
        /// if status is downloading.
        /// </summary>
        /// <returns></returns>
        public TimeSpan TimeRunning()
        {
            return DateTime.Now - StartTime;
        }


        #region streaming

        /// <summary>
        /// If the request is cacheable, it is streamed to cache and then to client.
        /// If not it is streamed to the client directly.
        /// </summary>
        /// <returns>The status of the request.</returns>
        public Status SelectMethodAndStream()
        {
            Status result = Status.Failed;
            
            // Check for admission control, but on the local side not if we're not online
            if ((_proxy is RCRemoteProxy || _proxy.NetworkStatus != RCProxy.NetworkStatusCode.Online) &&
                _proxy.NumInflightRequests >= _proxy.MaxInflightRequests)
            {
                // Instead of keeping the client waiting, just send him a page to retry in a couple of minutes.
                SendErrorPage(HttpStatusCode.ServiceUnavailable, "No connection slots available, please try again in a couple of minutes.");
                return result;
            }

            
            // add to active set of connections
            _proxy.AddActiveRequest();
            // Tell the network usage detector we're downloading now
            _proxy.NetworkUsageDetector.DownloadStarted();

            if (IsCacheable())
            {
                // Stream to cache and client
                result = StreamToCacheAndClient();
            }
            else
            {
                // Otherwise just stream to the client.
                _rcRequest.FileSize = StreamTransparently();
                result = Status.Completed;
            }

            //remove from active set of connections
            _proxy.RemoveActiveRequest();
            // Tell the network usage detector we're done downloading
            _proxy.NetworkUsageDetector.DownloadStopped();

            return result;
        }

        /// <summary>
        /// Stream the request to the server and the response back to the client transparently.
        /// Measures speed.
        /// XXX: does not have gateway support
        /// </summary>
        /// <returns></returns>
        protected long StreamTransparently()
        {
            Logger.Debug("streaming: " + RequestUri + " to client and measuring speed.");
            // Stream parameters, if we have non GET/HEAD
            HttpUtils.SendBody(_rcRequest.GenericWebRequest, _rcRequest.Body);

            // Get response
            HttpWebResponse serverResponse;
            try
            {
                serverResponse = (HttpWebResponse)_rcRequest.GenericWebRequest.GetResponse();
            }
            catch (WebException e)
            {
                // Even if we have other status than 200 we just stream
                serverResponse = (HttpWebResponse)e.Response;
            }

            // Copy headers and status code.
            HttpUtils.CopyWebResponse(_clientHttpContext.Response, serverResponse);

            // Stream 
            long length;
            try
            {
                length = StreamToClient(serverResponse.GetResponseStream());
            }
            catch (Exception e)
            {
                length = -1;
                SendErrorPage(HttpStatusCode.InternalServerError, e.Message);
            }

            if (serverResponse.ContentLength != -1)
            {
                // If header is set, this can be more accurate (if GZIP was used).
                length = serverResponse.ContentLength;
            }
            return length;
        }

        /// <summary>
        /// Modifies the web response to include a global cache item's headers and status code.
        /// </summary>
        /// <param name="gci"></param>
        protected void ModifyWebResponse(GlobalCacheItem gci)
        {
            _clientHttpContext.Response.StatusCode = gci.statusCode;
            NameValueCollection headers = JsonConvert.DeserializeObject<NameValueCollection>(gci.responseHeaders,
                    new NameValueCollectionConverter());
            HttpUtils.IntegrateHeadersIntoWebResponse(_clientHttpContext.Response, headers);
            // Set content-type (not done by method above)
            _clientHttpContext.Response.ContentType = headers["Content-Type"];
            // Do not set content length or content encoding here
        }

        /// <summary>
        /// Stream the file from the cache to the client. Also modifies the response
        /// according to the DB data.
        /// 
        /// Throws an FileNotFoundException if the file does not exist.
        /// Throws an Exception if the DB info could not be retrieved.
        /// 
        /// Other exceptions are also forwarded.
        /// </summary>
        /// <param name="absFileName">Name of the file to stream to the client.</param>
        /// <param name="takeDBData">If the headers and status code from the DB should be used.
        /// This should be true for cache files and false for RC page files (e.g. trotro.html)</param>
        /// <returns>Bytes streamed from the cache to the client.</returns>
        protected long StreamFromCacheToClient(string absFileName, bool takeDBData)
        {
            // make sure the file exists.
            FileInfo f = new FileInfo(absFileName);
            if (!f.Exists)
            {
                Logger.Warn("File doesn't exist: " + absFileName);
                throw new FileNotFoundException("File doesn't exist: )");
            }

            if (takeDBData)
            {
                string relFileName = absFileName.Substring(_proxy.ProxyCacheManager.CachePath.Length);
                // Modify the webresponse
                GlobalCacheItem gci = _proxy.ProxyCacheManager.GetGlobalCacheItemAsRequest(relFileName);
                if (gci == null)
                {
                    Logger.Warn("Problem getting DB info: " + absFileName);
                    throw new Exception("Problem getting DB info.");
                }
                ModifyWebResponse(gci);
            }

            // Stream file to client
            FileStream fs = f.Open(FileMode.Open, FileAccess.Read);
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
                long bytes = Utils.Stream(ms, output);
                return bytes;
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                if (ms != null)
                {
                    ms.Close();
                }
            }
        }

        /// <summary>
        /// Streams a request first to the cache and then from the cache back to the client.
        /// 
        /// XXX: response time could be improved here if it downloads and streams to the client at the same time.
        /// Basically, somehow merge the DownloadtoCache() and StreamfromcachetoClient() methods into this method.
        /// </summary>
        /// <returns>The Status of the request.</returns>
        protected Status StreamToCacheAndClient()
        {
            Logger.Debug("streaming: " + _rcRequest.GenericWebRequest.RequestUri + " to cache and client.");
            try
            {
                // Download, which potentially also replaces
                // When streaming, we index all html pages!
                _rcRequest.DownloadToCache(true);
                FileInfo f = new FileInfo(_rcRequest.CacheFileName);
                if (f.Exists)
                {
                    try
                    {
                        _rcRequest.FileSize = StreamFromCacheToClient(_rcRequest.CacheFileName, true);
                        return Status.Completed;
                    }
                    catch (Exception e)
                    {
                        Logger.Warn(e.Message);
                        SendErrorPage(HttpStatusCode.InternalServerError, e.Message);
                        return Status.Failed;
                    }
                }
                else
                {
                    SendErrorPage(HttpStatusCode.InternalServerError, "File does not exist.");
                    return Status.Failed;
                }
            }
            catch (Exception e)
            {
                SendErrorPage(HttpStatusCode.InternalServerError, e.Message);
                return Status.Failed;
            }
        }

        #endregion
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
        /// <returns>True if it is a GET or HEAD request, false otherwise.</returns>
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
        /// Checks whether the request is timed out based on _timeout.
        /// </summary>
        /// <returns>True or false for timed out or not.</returns>
        public bool IsTimedOut()
        {
            return _rcRequest.IsTimedOut(_requestTimeout);
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
            string str = "ID " + _handlerId + " " + Context.Request.RemoteEndPoint.Address +
                         " " + _rcRequest.GenericWebRequest.Method + " " + RequestUri +
                         " REFERER " + RefererUri + " " + 
                         RequestStatus + " " + _rcRequest.FileSize;
            Logger.Info(str);
        }

        /// <summary>
        /// Log our response to the client.
        /// </summary>
        public void LogResponse()
        {
            string str = "ID " + _handlerId + " RSP " + RequestUri + " " + 
                        RequestStatus + " " + _rcRequest.FileSize;
            Logger.Info(str);
        }

        #endregion
    }
}
