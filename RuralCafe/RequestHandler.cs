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
            Normal = 0,
            Low = 1
        }

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
        protected Socket _clientSocket;
        protected IPAddress _clientAddress;

        // the actual request object variables
        protected HttpWebRequest _originalRequest;
        protected RCRequest _rcRequest;
        protected int _requestTimeout; // timeout in milliseconds

        // filename for the package
        protected string _packageFileName;

        // temporary variables
        private Byte[] _recvBuffer = new Byte[1024];
        // If request is valid
        private bool _validRequest = true;

        // Util consts
        /// <summary>
        /// Matches "localhost" or "127.0.0.1" followed by anything but a dot.
        /// </summary>
        private static Regex localAddressRegex = new Regex(@"(?<add1>(localhost|127\.0\.0\.1))(?<add2>[^\.])");

        // benchmarking unused
        //protected DateTime requestReceived;
        //protected DateTime requestCompleted;

        /// <summary>
        /// Constructor for the request.
        /// </summary>
        /// <param name="proxy">Proxy that this request belongs to.</param>
        /// <param name="socket">Socket on which the request came in on.</param>
        protected RequestHandler(RCProxy proxy, Socket socket)
        {
            _proxy = proxy;
            _clientSocket = socket;
            if (socket != null)
            {
                _clientAddress = ((IPEndPoint)(socket.RemoteEndPoint)).Address;
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
        public HttpWebRequest OriginalRequest
        {
            set { _originalRequest = value; }
            get { return _originalRequest; }
        }
        /// <summary>The Socket.</summary>
        public Socket Socket
        {
            get { return _clientSocket; }
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

        /// <summary>Checks whether the request is blacklisted by the proxy.</summary>
        public bool IsBlacklisted(string uri)
        {
            return _proxy.IsBlacklisted(uri);
        }

        #endregion

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

        public static RequestHandler PrepareNewRequestHandler(RCProxy proxy, Socket socket)
        {
            string recvString = "";
            HttpWebRequest originalRequest = null;
            try
            {
                // Read the incoming text on the socket into recvString
                int bytes = RecvMessage(new byte[1024], ref recvString, socket);
                if (bytes == 0 || recvString.Length == 0)
                {
                    // no bytes, it's an error just return.
                    throw (new IOException("empty request"));
                }

                // get the requested URI
                // the client browser sends a GET command followed by a space, then the URL, then an identifer for the HTTP version
                int index1 = recvString.IndexOf(' ');
                int index2 = recvString.IndexOf(' ', index1 + 1);
                if ((index1 < 0) || (index2 < 0))
                {
                    throw (new IOException("invalid request: " + recvString));
                }
                String method = recvString.Substring(0, index1);
                String uri = AddHttpPrefix(recvString.Substring(index1 + 1, index2 - index1).Trim());
                // Appends dot to local addresses so the remote proxy won't be bypassed
                uri = AppendDotToLocalAddress(uri);

                originalRequest = (HttpWebRequest)WebRequest.Create(uri);
                originalRequest.Method = method;

                RequestHandler result;
                if (proxy is RCLocalProxy)
                {
                    if (IsRCRequest(originalRequest))
                    {
                        result = new LocalInternalRequestHandler((RCLocalProxy)proxy, socket);
                    }
                    else
                    {
                        result = new LocalRequestHandler((RCLocalProxy)proxy, socket);
                    }
                }
                else
                {
                    // TODO also internal here
                    result = new RemoteRequestHandler((RCRemoteProxy)proxy, socket);
                }
                // Set original request.
                result._originalRequest = originalRequest;

                return result;
            }
            catch (Exception e)
            {
                // temp handler: Internal is not needed
                RequestHandler temp;
                if (proxy is RCLocalProxy)
                {
                    temp = new LocalRequestHandler((RCLocalProxy)proxy, socket);
                }
                else
                {
                    temp = new RemoteRequestHandler((RCRemoteProxy)proxy, socket);
                }
                String errmsg = "error handling request: ";
                if (originalRequest != null)
                {
                    errmsg += " " + originalRequest.RequestUri.ToString(); ;
                }
                errmsg += " " + e.GetType() + ": " + e.Message + "\n" + e.StackTrace;
                temp.LogDebug(errmsg);
                // Erroneous has been handled
                temp._validRequest = false;
                return temp;
            }
        }

        /// <summary>
        /// Main entry point for listener threads for a HttpWebRequest.
        /// </summary>
        public void Go()
        {
            string recvString = "";
            string refererUri = "";

            try
            {
                if (!_validRequest)
                {
                    // finally will still be executed!
                    return;
                }
                // get the referer URI
                refererUri = GetHeaderValue(recvString, "Referer");

                if (CreateRequest(_originalRequest, refererUri, recvString))
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
                    LogDebug("streaming: " + _originalRequest.RequestUri.ToString() + " to client.");
                    long bytesSent = StreamTransparently(recvString);
                    return;
                }
            }
            catch (Exception e)
            {
                String errmsg = "error handling request: ";
                if (_originalRequest != null)
                {
                    errmsg += " " +_originalRequest.RequestUri.ToString(); ;
                }
                errmsg += " " + e.GetType() + ": " + e.Message + "\n" + e.StackTrace;
                LogDebug(errmsg);
            }
            finally
            {
                // disconnect and close the socket
                if (_clientSocket != null)
                {
                    if (_clientSocket.Connected)
                    {
                        _clientSocket.Close();
                    }
                }
                // XXX: _rcRequest.FinishTime = DateTime.Now;
            }
            // returning from this method will terminate the thread
        }

        /// <summary>
        /// Creates and handles the logged request
        /// logEntry format: (requestId, startTime, clientAddress, requestedUri, refererUri, [status])
        /// 
        /// TODO no support for Method, POST parameters.
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
                _originalRequest = (HttpWebRequest) WebRequest.Create(logEntry[3]);
                string refererUri = logEntry[4];
                Status requestStatus = Status.Pending;
                if (logEntry.Count == 6)
                {
                    // Adapted to read the text of the status instead of the number.
                    // Therefore old logs won't work anymore.
                    requestStatus = (Status) Enum.Parse(typeof(Status),logEntry[5]);
                }

                if (CreateRequest(_originalRequest, refererUri, ""))
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
                    errmsg += " " + _originalRequest.RequestUri.ToString(); ;
                }
                errmsg += " " + e.GetType() + ": " + e.Message + "\n" + e.StackTrace;
                LogDebug(errmsg); LogDebug("error handling request: " + _originalRequest.RequestUri.ToString() + " " + e.Message + e.StackTrace);
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
        protected bool CreateRequest(HttpWebRequest request, string refererUri, string recvString)
        {
            if (Util.IsValidUri(request.RequestUri.ToString()))
            {
                // create the request object
                _rcRequest = new RCRequest(this, request, "", refererUri);
                // XXX: obsolete
                //_rcRequest.ParseRCSearchFields();
                _rcRequest.GenericWebRequest.Referer = refererUri;
                _rcRequest._recvString = recvString;
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
        protected long StreamTransparently(string recvString)
        {
            long bytesSent = 0;
            
            string clientRequest = "";
            if(recvString.Equals("")) {
                clientRequest = _rcRequest._recvString;
            }
            else {
                clientRequest = recvString;
            }
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
        /// Stream the file from the cache to the client.
        /// </summary>
        /// <param name="fileName">Name of the file to stream to the client.</param>
        /// <returns>Bytes streamed from the cache to the client.</returns>
        protected long StreamFromCacheToClient(string fileName)
        {
            long bytesSent = 0;

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
                int offset = 0;
                byte[] buffer = new byte[32]; // XXX: magic number 32
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
            catch (Exception)
            {
                return -1;
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
            return request.RequestUri.ToString().StartsWith("http://www.ruralcafe.net/");
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
        /// Currently, just based on the file name length.
        /// XXX: This should be changed so that even long file names can be cached.
        /// </summary>
        /// <returns>True if cacheable, false if not. </returns>
        protected bool IsCacheable()
        {
            return (_rcRequest.CacheFileName.Length <= 248);
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
        /// Gets the HTTP header value for a particular header.
        /// </summary>
        /// <param name="requestHeaders">The HTTP request headers received from the client.</param>
        /// <param name="header">The header to return the value of.</param>
        /// <returns>The header's value.</returns>
        public string GetHeaderValue(string requestHeaders, string header)
        {
            header = header + ":";
            int index1 = 0;
            int index2 = 0;
            string value = "";

            // find the header
            index1 = requestHeaders.IndexOf(header);
            if (index1 > 0)
            {
                value = requestHeaders.Substring(index1 + header.Length);
                index2 = value.IndexOf("\r\n");
                if (index2 > 0)
                {
                    // get the value
                    value = value.Substring(0, index2);
                }
            }
            return value.Trim();
        }

        /// <summary>
        /// Sends an HTTP OK response to the client.
        /// </summary>
        /// <param name="contentType">The Content-Type of the request to respond to.</param>
        protected void SendOkHeaders(string contentType)
        {
            SendOkHeaders(contentType, "");
        }

        /// <summary>
        /// Sends an HTTP OK response to the client.
        /// </summary>
        /// <param name="contentType">The Content-Type of the request to respond to.</param>
        protected void SendOkHeaders(string contentType, string additionalHeaders)
        {
            int statusCode = (int)HttpStatusCode.OK;
            string strReason = "";
            string str = "";

            str = "HTTP/1.1" + " " + statusCode + " " + strReason + "\r\n" +
            "Content-Type: " + contentType + "\r\n" +
            "Proxy-Connection: close" + "\r\n" +
            additionalHeaders +
            "\r\n";

            SendMessage(str);
        }

        /// <summary>
        ///  Write a redirect response to the client.
        /// </summary>
        /// <param name="url">Destination url.</param>
        protected void SendRedirect(string title, string url)
        {
            int statusCode = (int)HttpStatusCode.OK;
            string strReason = "";
            string str = "";
            title = HttpUtility.UrlEncode(title);
            url = HttpUtility.UrlEncode(url);

            str = "HTTP/1.1" + " " + statusCode + " " + strReason + "\r\n" +
            "Content-Type: " + "text/html" + "\r\n" +
            "Proxy-Connection: close" + "\r\n" +
            "\r\n";

            string fullUrl = "<meta HTTP-EQUIV=\"REFRESH\" content=\"0; url=http://www.ruralcafe.net/trotro-user.html?" + 
                "t=" + title + "&amp;" + "a=" + url + "\">";
            str = str + fullUrl;
            SendMessage(str);
        }

        // FIXME: Does apparently not create a proper header/html response. Same may be true for
        // SendOKHeaders
        /// <summary>
        ///  Write an error response to the client.
        /// </summary>
        /// <param name="status">Error status.</param>
        /// <param name="strReason">The reason for the status.</param>
        /// <param name="strText">Any additional text.</param>
        protected void SendErrorPage(HttpStatusCode status, string strReason, string strText)
        {
            int statusCode = (int)status;
            string str = "HTTP/1.1" + " " + statusCode + " " + strReason + "\r\n" +
                "Content-Type: text/plain" + "\r\n" +
                "Proxy-Connection: close" + "\r\n" +
                "\r\n" +
                statusCode + " " + strReason + " " + strText;
            SendMessage(str);

            LogDebug(status + " " + strReason + " " + strText);
        }

        /// <summary>
        /// Sends a string to the client socket.
        /// </summary>
        /// <param name="strMessage">The string message to send.</param>
        /// <returns>Returns the length of the message sent or -1 if failed.</returns>
        protected int SendMessage(string strMessage)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(strMessage);
            int len = buffer.Length;

            if ((_clientSocket == null) || !_clientSocket.Connected)
            {
                return -1;
            }
            try
            {
                _clientSocket.Send(buffer, len, 0);
            }
            catch (Exception e)
            {
                LogDebug("socket closed for some reason " + e.StackTrace + " " + e.Message);
                return -1;
            }
            return len;
        }

        /// <summary>
        /// Reads a string from the client socket.
        /// </summary>
        /// <param name="buf">Temporary buffer to store the bytes.</param>
        /// <param name="strMessage">Message read from the socket.</param>
        /// <returns>The length of the string read from the socket.</returns>
        protected int RecvMessage(byte[] buf, ref string strMessage)
        {
            return RecvMessage(buf, ref strMessage, _clientSocket);
        }

        /// <summary>
        /// Reads a string form the given socket.
        /// </summary>
        /// <param name="buf">Temporary buffer to store the bytes.</param>
        /// <param name="strMessage">Message read from the socket.</param>
        /// <param name="socket">The socket to read from.</param>
        /// <returns>The length of the string read from the socket.</returns>
        protected static int RecvMessage(byte[] buf, ref string strMessage, Socket socket)
        {
            int iBytes = socket.Receive(buf, 1024, 0);
            strMessage = Encoding.ASCII.GetString(buf).Replace("\0", "");
            return (iBytes);
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
            string str = _rcRequest.FinishTime + " RSP " + _originalRequest.RequestUri.ToString() + " " +
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
