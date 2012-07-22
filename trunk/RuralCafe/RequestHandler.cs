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
        public enum Status
        {
            Failed = -1,
            Received = 0,
            Requested = 1,
            Completed = 2,
            Cached = 3,             
            NotCacheable = 4,
            NotFound = 5,
            StreamedTransparently = 6,
            Ignored = 7
        };

        // timeouts
        public const int REQUEST_PACKAGE_DEFAULT_TIMEOUT = Timeout.Infinite; // in milliseconds
        public const int WEB_REQUEST_DEFAULT_TIMEOUT = 30000; // in milliseconds

        // ID
        protected int _ID;

        // proxy this request belongs to
        protected RCProxy _proxy;

        /*
        // type of request
        protected string _cachePath;
        protected string _logPath;
         */

        // client info
        protected Socket _clientSocket;
        protected IPAddress _clientAddress;

        // the actual request object variables
        protected RCRequest _rcRequest;
        protected int _requestTimeout; // timeout in milliseconds

        // filename for the package
        protected string _packageFileName;

        // temporary variables
        private Byte[] _recvBuffer = new Byte[1024];

        // benchmarking unused
        //protected DateTime requestReceived;
        //protected DateTime requestCompleted;

        /// <summary>
        /// Constructor for the request.
        /// </summary>
        /// <param name="proxy">Proxy that this request belongs to.</param>
        /// <param name="socket">Socket on which the request came in on.</param>
        public RequestHandler(RCProxy proxy, Socket socket)
        {
            _proxy = proxy;
            _clientSocket = socket;
            if (socket != null)
            {
                _clientAddress = ((IPEndPoint)(socket.RemoteEndPoint)).Address;
            }
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
        /// just check if the request IDs are equal
        /// </summary>        
        public override bool Equals(object obj)
        {
            return (ID.Equals(((RequestHandler)obj).ID));
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
        public int ID
        {
            get { return _ID; }
        }
        /// <summary>The proxy that this request belongs to.</summary>
        public RCProxy Proxy
        {
            get { return _proxy; }
        }
        /// <summary>Time this request started.</summary>
        public RCRequest RCRequest
        {
            set { _rcRequest = value; }
            get { return _rcRequest; }
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
        public int RequestStatus
        {
            set { _rcRequest.RequestStatus = value; }
            get { return _rcRequest.RequestStatus; }
        }
        /// <summary>URI of the request.</summary>
        public string RequestUri
        {
            set { _rcRequest.Uri = value; }
            get { return _rcRequest.Uri; }
        }
        /// <summary>URI of the referrer.</summary>
        public string RefererUri
        {
            set { _rcRequest.RefererUri = value; }
            get { return _rcRequest.RefererUri; }
        }
        /// <summary>Hashed base name of the file if the RCRequest is stored in the cache.</summary>
        public string HashedFileName
        {
            set { _rcRequest.HashedFileName = value; }
            get { return _rcRequest.HashedFileName; }
        }
        /// <summary>Name of the file if the RCRequest is stored in the cache.</summary>
        public string CacheFileName
        {
            set { _rcRequest.CacheFileName = value; }
            get { return _rcRequest.CacheFileName; }
        }
        /// <summary>Checks whether the RCRequest is stored in the cache.</summary>
        public bool IsCompressed()
        {
            return _rcRequest.IsCompressed();
        }

        /// <summary>Checks whether the request is blacklisted by the proxy.</summary>
        public bool IsBlacklisted(string uri)
        {
            return _proxy.IsBlacklisted(uri);
        }

        #endregion

        /// <summary>
        /// main entry point for listener threads for a HttpWebRequest.
        /// </summary>
        public void Go()
        {
            string clientRequest = "";
            string requestedUri = "";
            string refererUri = "";

            try
            {
                // Read the incoming text on the socket into recvString
                int bytes = RecvMessage(_recvBuffer, ref clientRequest);
                if (bytes == 0)
                {
                    // no bytes, it's an error just return.
                    throw (new IOException());
                }

                // get the requested URI
                // the client browser sends a GET command followed by a space, then the URL, then and identifer for the HTTP version
                int index1 = clientRequest.IndexOf(' ');
                int index2 = clientRequest.IndexOf(' ', index1 + 1);
                if ((index1 < 0) || (index2 < 0))
                {
                    throw (new IOException());
                }
                requestedUri = clientRequest.Substring(index1 + 1, index2 - index1).Trim();

                // https requests will fail this check
                if (Util.IsValidUri (requestedUri))
                {
                    // get the referer URI
                    refererUri = GetHeaderValue(clientRequest, "Referer");

                    // create the request object
                    _rcRequest = new RCRequest(this, requestedUri, refererUri);
                    _rcRequest.ParseRCSearchFields();
                    _rcRequest.GenericWebRequest.Referer = refererUri;
                    _rcRequest._recvString = clientRequest;

                    _packageFileName = _proxy.PackagesPath + _rcRequest.HashedFileName + ".gzip";

                    // XXX: need to avoid duplicate request/response logging when redirecting e.g. after an add
                    // handle the request
                    if (requestedUri != "http://www.ruralcafe.net/requests.html")
                    {
                        LogRequest();
                        RequestStatus = HandleRequest();
                        LogResponse();
                    }
                    else
                    {
                        RequestStatus = HandleRequest();
                    }
                }
                else
                {
                    /* XXX: was streaming these unparsable URIs, but this is disabled for now
                    LogDebug("streaming: " + requestedUri);
                    long bytesSent = StreamTransparently();

                    Status = GenericRequest.STATUS_NOTCACHEABLE;
                    //_rcRequest._fileSize = bytesSent;

                    LogDebug("unhandled request streamed: " + requestedUri);
                     */
                    throw (new IOException());
                }

            }
            catch (Exception e)
            {
                LogDebug("error parsing request: " + requestedUri + " " + e.Message + e.StackTrace);
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

        /// <summary>Abstract method for proxies to handle requests.</summary>
        public abstract int HandleRequest();
       
 
        #region Methods for Checking Requests

        /// <summary>
        /// Checks if the request is GET or HEAD.
        /// </summary>
        /// <returns>True if it is a GET or HEAD request, false if otherwise.</returns>
        protected bool IsGetOrHeadHeader()
        {
            if (_rcRequest.GenericWebRequest.Method == "GET" ||
                _rcRequest.GenericWebRequest.Method == "HEAD")
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if the page is cacheable.
        /// Currently, just based on the file length.
        /// XXX: This should be changed so that even long file names can be cached.
        /// </summary>
        /// <returns>True if cacheable, false if not. </returns>
        protected bool IsCacheable()
        {
            if (_rcRequest.CacheFileName.Length <= 255)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if the file is cached.
        /// </summary>
        /// <param name="fileName">Name of the file to check.</param>
        /// <returns>True if cached, false if not.</returns>
        protected bool IsCached(string fileName)
        {
            if (fileName == null || fileName.Equals(""))
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

        /// <summary>
        /// Checks if the request is a RuralCafe specific request.
        /// </summary>
        /// <returns>True if it is, false if not.</returns>
        protected bool IsRCRequest()
        {
            if (RequestUri.StartsWith("www.ruralcafe.net") ||
                RequestUri.StartsWith("http://www.ruralcafe.net"))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Checks to see if the request is a RuralCafe search.
        /// </summary>
        /// <returns>True if yes, false if not.</returns>
        public bool IsRCLocalSearch()
        {
            if (IsRCRequest() &&_rcRequest.GetRCSearchField("button") == "Search")
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if the request is a RuralCafe remote query.
        /// </summary>
        /// <returns>True if yes, false if not.</returns>
        public bool IsRCRemoteQuery()
        {
            if (IsRCRequest() && _rcRequest.GetRCSearchField("button") == "Queue Request")
            {
                return true;
            }
            return false;
        }

        #endregion


        #region HTTP Helper Functions

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
            int status = HTTP_OK;
            string strReason = "";
            string str = "";

            str = "HTTP/1.1" + " " + status + " " + strReason + "\r\n" +
            "Content-Type: " + contentType + "\r\n" +
            "Proxy-Connection: close" + "\r\n" +
            "\r\n";

            SendMessage(str);
        }

        /// <summary>
        ///  Write an error response to the client.
        /// </summary>
        /// <param name="status">Error status.</param>
        /// <param name="strReason">The reason for the status.</param>
        /// <param name="strText">Any additional text.</param>
        protected void SendErrorPage(int status, string strReason, string strText)
        {
            string str = "HTTP/1.1" + " " + status + " " + strReason + "\r\n" +
                "Content-Type: text/plain" + "\r\n" +
                "Proxy-Connection: close" + "\r\n" +
                "\r\n" +
                status + " " + strReason + " " + strText;
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

            if (!_clientSocket.Connected)
            {
                LogDebug("socket closed for some reason");
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
            int iBytes = _clientSocket.Receive(buf, 1024, 0);
            strMessage = Encoding.ASCII.GetString(buf);
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
                         " GET " + RequestUri +
                         " REFERER " + RefererUri + " " + 
                         RequestStatus + " " + _rcRequest.FileSize;
            _proxy.WriteMessage(_ID, str);
        }

        /// <summary>
        /// Log the response from the server.
        /// </summary>
        public void LogResponse()
        {
            string str = _rcRequest.FinishTime + " RSP " + RequestUri + " " + 
                        RequestStatus + " " + _rcRequest.FileSize;
            _proxy.WriteMessage(_ID, str);
        }

        /// <summary>
        /// Logs any debug messages.
        /// </summary>
        public void LogDebug(string str)
        {
            _proxy.WriteDebug(_ID, str);
        }

        #endregion


        #region HTTP Response Codes

        public static byte[] EOL = { (byte)'\r', (byte)'\n' };

        /** 2XX: generally "OK" */
        public const int HTTP_OK = 200;
        public const int HTTP_CREATED = 201;
        public const int HTTP_ACCEPTED = 202;
        public const int HTTP_NOT_AUTHORITATIVE = 203;
        public const int HTTP_NO_CONTENT = 204;
        public const int HTTP_RESET = 205;
        public const int HTTP_PARTIAL = 206;

        /** 3XX: relocation/redirect */
        public const int HTTP_MULT_CHOICE = 300;
        public const int HTTP_MOVED_PERM = 301;
        public const int HTTP_MOVED_TEMP = 302;
        public const int HTTP_SEE_OTHER = 303;
        public const int HTTP_NOT_MODIFIED = 304;
        public const int HTTP_USE_PROXY = 305;

        /** 4XX: client error */
        public const int HTTP_BAD_REQUEST = 400;
        public const int HTTP_UNAUTHORIZED = 401;
        public const int HTTP_PAYMENT_REQUIRED = 402;
        public const int HTTP_FORBIDDEN = 403;
        public const int HTTP_NOT_FOUND = 404;
        public const int HTTP_BAD_METHOD = 405;
        public const int HTTP_NOT_ACCEPTABLE = 406;
        public const int HTTP_PROXY_AUTH = 407;
        public const int HTTP_CLIENT_TIMEOUT = 408;
        public const int HTTP_CONFLICT = 409;
        public const int HTTP_GONE = 410;
        public const int HTTP_LENGTH_REQUIRED = 411;
        public const int HTTP_PRECON_FAILED = 412;
        public const int HTTP_ENTITY_TOO_LARGE = 413;
        public const int HTTP_REQ_TOO_LONG = 414;
        public const int HTTP_UNSUPPORTED_TYPE = 415;

        /** 5XX: server error */
        public const int HTTP_SERVER_ERROR = 500;
        public const int HTTP_INTERNAL_ERROR = 501;
        public const int HTTP_BAD_GATEWAY = 502;
        public const int HTTP_UNAVAILABLE = 503;
        public const int HTTP_GATEWAY_TIMEOUT = 504;
        public const int HTTP_VERSION = 505;

        #endregion
    }
}
