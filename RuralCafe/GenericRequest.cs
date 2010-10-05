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
    public abstract class GenericRequest
    {
        // ID
        protected int _requestId;

        // proxy
        public GenericProxy _proxy;

        // type of request
        protected string _cachePath;
        protected string _logPath;

        // client info
        protected Socket _clientSocket;
        public IPAddress _clientAddress;

        // httpwebrequest variables
        public DateTime _startTime;
        public DateTime _endTime;
        public RequestObject _requestObject;

        // httpwebresponse variables
        //public DateTime _responseTimestamp;
        public HttpWebResponse _webResponse;
        //protected long _responseSize;

        // response status
        // XXX: kind of ugly since this is being used by both the Generic/Local/RemoteRequest and RequestObjects
        public const int STATUS_RECEIVED = 0;
        public const int STATUS_REQUESTED = 1;
        public const int STATUS_FAILED = -1;
        public const int STATUS_SATISFIED = 2;
        public const int STATUS_CACHED = 3;
        public const int STATUS_NOTFOUND = 4;
        public const int STATUS_NOTCACHEABLE = 5;

        // ruralcafe specific stuff
        protected Package _package;

        // temporary stuff
        protected Byte[] _recvBuffer = new Byte[1024];
        public int _timeout; // timeout in milliseconds

        // benchmarking stuff
        //protected DateTime requestReceived;
        //protected DateTime requestCompleted;

        public GenericRequest(GenericProxy proxy, Socket socket)
        {
            _proxy = proxy;
            _clientSocket = socket;
            _clientAddress = ((IPEndPoint)(socket.RemoteEndPoint)).Address;

            // local proxy will wait forever for the remote proxy to respond
            _timeout = Timeout.Infinite;
            _startTime = DateTime.Now;
            _endTime = _startTime;
        }

        ~GenericRequest()
        {
            // cleanup stuff
        }

        // instead of testing for equality of reference
        // just check if the actual requested pages are equivalent
        public override bool Equals(object obj)
        {
            return (RequestUri.Equals(((GenericRequest)obj).RequestUri));
        }
        public override int GetHashCode()
        {
            return RequestUri.GetHashCode();
        }

        #region Property Accessors
                
        public int ID
        {
            get { return _requestId; }
        }
        public HttpWebResponse WResponse
        {
            get { return _webResponse; }
            set { _webResponse = value; }
        }

        public int Status
        {
            set { _requestObject._status = value; }
            get { return _requestObject._status; }
        }
        public bool Satisfied
        {
            get { return (Status == STATUS_SATISFIED); }
        }
        public bool Failed
        {
            get { return (Status == STATUS_FAILED); }
        }

        public string RequestUri
        {
            set { _requestObject._uri = value; }
            get { return _requestObject._uri; }
        }
        public string ReferrerUri
        {
            set { _requestObject._refererUri = value; }
            get { return _requestObject._refererUri; }
        }
        public int RequestTimeout
        {
            get { return _timeout; }
        }

        public bool IsTimedOut()
        {
            // XXX: not sure why the timeout would be zero
            if (_timeout == 0)
            {
                return true;
            }

            if (_timeout == Timeout.Infinite)
            {
                return false;
            }

            DateTime currTime = DateTime.Now;
            TimeSpan elapsed = currTime.Subtract(_startTime);
            if (elapsed.TotalMilliseconds >= _timeout)
            {
                return true;
            }
            return false;
        }
        #endregion

        // ZZZ: analysis and benchmarking entry point (backdoor)
        public void BackdoorGo()
        {
            // Call RuralCafe logic to handle page requests
            PrefetchBenchmarker("high", 1);
        }

        // main entry point for listener threads for a HttpWebRequest
        public void Go()
        {
            string recvString = "";
            string requestedUri = "";

            try
            {
                // Read the incoming text on the socket
                // If it's empty, it's an error, so just return.
                // This will termiate the thread.
                int bytes = RecvMessage(_recvBuffer, ref recvString);
                if (bytes == 0)
                {
                    throw (new IOException());
                }

                // Get the URL for the connection. The client browser sends a GET command
                // followed by a space, then the URL, then and identifer for the HTTP version.
                // Extract the URL as the string betweeen the spaces.
                int index1 = recvString.IndexOf(' ');
                int index2 = recvString.IndexOf(' ', index1 + 1);

                if ((index1 < 0) || (index2 < 0))
                {
                    throw (new IOException());
                }
                requestedUri = recvString.Substring(index1 + 1, index2 - index1).Trim();

                string refererUri = GetHeaderValue(recvString, "Referer");

                // https requests will fail this check
                if (!Util.IsValidUri(requestedUri))
                {
                    /*
                    LogDebug("streaming: " + requestedUri + " at " + GenericProxy.DEFAULT_MAX_DOWNLOAD_SPEED + "bps");
                    long bytesSent = StreamTransparently();

                    Status = GenericRequest.STATUS_NOTCACHEABLE;
                    //_endTime = DateTime.Now;
                    //_requestObject._fileSize = bytesSent;

                    LogDebug("unhandled request streamed: " + requestedUri);
                     * 
                     */
                    //LogDebug("unhandled request dropped: " + requestedUri);
                    return;
                    //throw (new IOException());
                }

                _requestObject = new RequestObject(_proxy, requestedUri, refererUri);
                _requestObject.ParseSearchFields();
                _requestObject._webRequest.Referer = refererUri;
                _requestObject._recvString = recvString;

                //_requestObject._recvString = recvString;
                HandlePageRequest();
            }
            catch (Exception e)
            {
                LogDebug("error parsing request: " + requestedUri + " " + e.Message + e.StackTrace);
            }
            finally
            {
                // Disconnect and close the socket.
                if (_clientSocket != null)
                {
                    if (_clientSocket.Connected)
                    {
                        _clientSocket.Close();
                    }
                }
            }
            // Returning from this method will terminate the thread.
        }

        public string GetHeaderValue(string recvString, string header) 
        {
            header = header + ":";
            int index1 = 0;
            int index2 = 0;

            // get referrer Uri
            string value = "";
            index1 = recvString.IndexOf(header);
            if (index1 > 0)
            {
                value = recvString.Substring(index1 + header.Length);
                index2 = value.IndexOf("\r\n");
                if (index2 > 0)
                {
                    value = value.Substring(0, index2);
                }
            }
            return value.Trim();
        }
        // abstract method for both proxies
        public abstract void HandlePageRequest();
        // ZZZ: benchmarking
        public abstract void PrefetchBenchmarker(string richness, int depth);

        #region Server Request Methods

        protected int StreamRequestFromServerToCache(GenericProxy proxy, GenericRequest request, HttpWebRequest webRequest, string fileName)
        {
            HttpWebResponse webResponse = null;
            long bytesDownloaded = 0;
            int returnStatus = STATUS_SATISFIED;

            if (fileName == null || fileName == "")
            {
                LogDebug("Filename is null");
                return STATUS_FAILED;
            }

            // create directory if it doesn't exist
            if (!Util.CreateDirectoryForFile(fileName))
            {
                return STATUS_FAILED;
            }

            // XXX: Should implement caching staleness check right here as per Firefox implementation maybe...?

            if (!Util.DeleteFile(fileName))
            {
                return STATUS_FAILED;
            }

            FileStream fs = null;
            try
            {
                fs = Util.CreateFile(fileName);
                if (fs == null)
                {
                    return STATUS_FAILED;
                }

                int bytesRead = 0;
                Byte[] buffer = new Byte[32];
                webResponse = (HttpWebResponse)webRequest.GetResponse();
                // XXX: hackery to pass the webresponse back to the LocalProxy because it needs this
                // XXX: in the unpack code to get the response headers
                if (proxy.Name.Equals(GenericProxy.LOCAL_PROXY_NAME))
                {
                    request.WResponse = webResponse;
                }

                // check to see if the time is up for the root request object
                if (request.IsTimedOut())
                {
                    LogDebug("Skipped, timed out: " + request.RequestUri);
                    return STATUS_FAILED;
                }

                // Create a response stream object.
                Stream responseStream = webResponse.GetResponseStream();
                // Handle compression
                /*
                if (webResponse.ContentEncoding.ToLower().Contains("gzip"))
                    responseStream = new GZipStream(responseStream, CompressionMode.Decompress);
                else if (webResponse.ContentEncoding.ToLower().Contains("deflate"))
                    responseStream = new DeflateStream(responseStream, CompressionMode.Decompress); 
                */
                // Read the response into a buffer.
                bytesRead = responseStream.Read(buffer, 0, 32);
                while (bytesRead != 0)
                {
                    // Write the response to the cache
                    fs.Write(buffer, 0, bytesRead);
                    bytesDownloaded += bytesRead;

                    // check to see if the time is up for this overall request object
                    if (request.IsTimedOut())
                    {
                        // incomplete, clean up the partial download
                        FileInfo f;
                        try
                        {
                            f = new FileInfo(fileName);
                            if (f.Exists)
                            {
                                f.Delete();
                            }
                        }
                        catch (Exception e)
                        {
                            LogDebug("Error deleting file: " + request.RequestUri + " " + e.StackTrace + " " + e.Message);
                        }
                        LogDebug("Failed, timed out: " + request.RequestUri);
                        return STATUS_FAILED;
                    }

                    // Read the next part of the response
                    bytesRead = responseStream.Read(buffer, 0, 32);
                }
            }
            catch (WebException e)
            {
                LogDebug("WebException, streaming failed: " + e.Message);
                returnStatus = STATUS_FAILED;
            }
            catch (Exception e)
            {
                // XXX: not handled well
                LogDebug("Exception, Stream from server to cache failed: " + e.Message);
                returnStatus = STATUS_FAILED;
            }
            finally
            {
                if (fs != null)
                {
                    fs.Close();
                }
            }

            LogDebug("received: " + webRequest.RequestUri + " " + bytesDownloaded + " bytes");
            return returnStatus;
        }

        // serve the page from the cache
        // assume that the _f is already instantiated
        protected long StreamFromDiskToClient(string fileName, bool decompress)
        {
            long bytesSent = 0;

            // JJJ: this check for existence bit is retarded, refactor to its own method.
            FileInfo f;
            try
            {
                f = new FileInfo(fileName);
                if (!f.Exists)
                {
                    LogDebug("error file doesn't exist: " + fileName);
                    return bytesSent;
                }
            }
            catch (Exception e)
            {
                LogDebug("problem getting file info: " + fileName + " " + e.Message);
                return bytesSent;
            }

            FileStream fs = null;
            try
            {
                // JJJ: this is where we do decompression 
                // we also need to do it when we unpack stuff from ruralcafe
                // open the file stream

                // Decompress
                if (decompress)
                {
                    // Decompress & stream to client
                    MemoryStream decompressedMs = BZ2DecompressFile(fileName);
                    int length = (int)decompressedMs.Length;
                    //byte[] decompressionBuf = new byte[length];
                    //decompressedMs.Read(decompressionBuf, 0, length);
                    _clientSocket.Send(decompressedMs.GetBuffer(), length, 0); // XXX: this is an ugly hack, but max filesize is 32MB
                    return length;
                }

                int offset = 0;
                byte[] buffer = new byte[32]; // magic number 32
                fs = f.Open(FileMode.Open, FileAccess.Read);

                // loop and get the bytes we need if we couldn't get it in one go
                int bytesRead = fs.Read(buffer, 0, 32);
                while (bytesRead > 0)
                {
                    // send bytes
                    _clientSocket.Send(buffer, bytesRead, 0);
                    bytesSent += bytesRead;

                    // read bytes from cache
                    bytesRead = fs.Read(buffer, 0, 32);

                    offset += bytesRead;
                }
            }
            catch (Exception e)
            {
                SendErrorPage(404, "problem serving from RuralCafe cache: ", e.Message);
                //LogDebug("404 Problem serving from RuralCafe cache");
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

        // XXX: not sure what the difference between this and the other streamfromservertoclient is
        protected long StreamTransparently()
        {
            long bytesSent = 0;
            string getString = _requestObject._recvString;
            Encoding ASCII = Encoding.ASCII;
            Byte[] byteGetString = ASCII.GetBytes(getString);
            Byte[] receiveByte = new Byte[256];
            Socket socket = null;
            //String strPage = null;
            try
            {
                string hostName = GetHeaderValue(getString, "Host");
                IPHostEntry ipEntry = Dns.GetHostEntry(hostName);
                IPAddress[] addr = ipEntry.AddressList;

                IPEndPoint ip = new IPEndPoint(addr[0], 80);
                socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                socket.Connect(ip);
            }
            catch (SocketException)
            {
                // do nothing
            }
            // send the request
            socket.Send(byteGetString, byteGetString.Length, 0);
            // recieve the bytes back from the server
            Int32 bytesRead = socket.Receive(receiveByte, receiveByte.Length, 0);
            _clientSocket.Send(receiveByte, bytesRead, 0);
            bytesSent += bytesRead;
            //strPage = strPage + ASCII.GetString(receiveByte, 0, bytesRead);
            while (bytesRead > 0)
            {
                // read the data in from the server
                bytesRead = socket.Receive(receiveByte, receiveByte.Length, 0);
                // check speed limit
                while (!_proxy.HasBandwidth(bytesRead))
                {
                    Thread.Sleep(100);
                }
                // send the data out to the client
                _clientSocket.Send(receiveByte, bytesRead, 0);
                bytesSent += bytesRead;
                //strPage = strPage + ASCII.GetString(receiveByte, 0, bytes);
            }
            socket.Close();

            //LogDebug(strPage);
            //SendMessage(_clientSocket, strPage);

            return bytesSent;
        }

        /*
        // XXX: not using this since the HttpWebRequest might not be identical to the client's request
        // Stream the page from the real server to the client
        // Called when we can't cache or don't know what to do with the page
        protected long StreamTransparentlyAmritaBaseline(HttpWebRequest webRequest)
        {
            long bytesSent = 0;
            Stream responseStream = null;
            try
            {
                //LogDebug("streaming request from server to client");
                // Get the response from the Web site.
                _webResponse = (HttpWebResponse)webRequest.GetResponse();

                int bytesRead = 0;
                Byte[] buffer = new Byte[32];

                // Create a response stream object.
                responseStream = _webResponse.GetResponseStream();

                // Read the response into a buffer.
                bytesRead = responseStream.Read(buffer, 0, 32);
                StringBuilder strResponse = new StringBuilder(""); 
                while (bytesRead != 0)
                {
                    // check speed limit
                    while (!_proxy.HasBandwidth(bytesRead))
                    {
                        Thread.Sleep(100);
                    }

                    // Pass the response back to the client
                    strResponse.Append(Encoding.ASCII.GetString(buffer,
                                        0, bytesRead));
                    _clientSocket.Send(buffer, bytesRead, 0);
                    bytesSent += bytesRead;

                    // Read the next part of the response
                    bytesRead = responseStream.Read(buffer, 0, 32);
                }
            }
            catch (FileNotFoundException e)
            {
                SendErrorPage(404, "file not found", e.StackTrace + " " + e.Message);
                //LogDebug("404 File not found");
            }
            catch (IOException e)
            {
                SendErrorPage(503, "service not available", e.StackTrace + " " + e.Message);
                //LogDebug("503 service not available");
            }
            catch (Exception e)
            {
                SendErrorPage(404, "File not found", e.StackTrace + " " + e.Message);
                //LogDebug("error streaming request from server to client: " + e.StackTrace + " " + e.Message);
            }
            finally
            {
                // Disconnect and close the socket.
                if (_clientSocket != null)
                {
                    if (_clientSocket.Connected)
                    {
                        _clientSocket.Close();
                    }
                }
            }

            return bytesSent;
        }
        */

        protected long StreamToClient(MemoryStream ms)
        {
            long bytesSent = 0;
            int offset = 0;
            byte[] buffer = new byte[32]; // magic number 32
            int bytesRead = 0;

            try
            {
                // loop and get the bytes we need if we couldn't get it in one go
                bytesRead = ms.Read(buffer, 0, 32);
                while (bytesRead > 0)
                {
                    // check speed limit
                    while (!_proxy.HasBandwidth(bytesRead))
                    {
                        Thread.Sleep(100);
                    }

                    // send bytes
                    _clientSocket.Send(buffer, bytesRead, 0);
                    bytesSent += bytesRead;

                    // read bytes from cache
                    bytesRead = ms.Read(buffer, 0, 32);

                    offset += bytesRead;
                }
            }
            catch (Exception e)
            {
                SendErrorPage(404, "Problem streaming the package from disk to client", e.StackTrace + " " + e.Message);
            }
            finally
            {
                if (ms != null)
                {
                    ms.Close();
                }

            }
            return bytesSent;
        }

        #endregion


        private MemoryStream BZ2DecompressFile(string bzipFile)
        {
            MemoryStream ms = new MemoryStream();
            FileStream bzipFileFs = new FileStream(bzipFile, FileMode.Open);
            ICSharpCode.SharpZipLib.BZip2.BZip2.Decompress(bzipFileFs, ms);
            //decompressionBuf = new byte[ms.Length];
            //ms.Read(decompressionBuf, 0, (int)ms.Length);
            //return ms.Length;
            return ms;
        }

        #region Check Request Methods

        // check if the httpwebrequest is a get or head
        protected bool IsGetOrHeadHeader()
        {
            if (_requestObject._webRequest.Method == "GET" ||
                _requestObject._webRequest.Method == "HEAD")
            {
                return true;
            }
            return false;
        }

        // check if the page is cacheable
        protected bool IsCacheable()
        {
            return IsCacheable(_requestObject._cacheFileName);
        }
        // private helper
        private bool IsCacheable(string fileName)
        {
            if (fileName.Length <= 255)
            {
                return true;
            }
            return false;
        }

        // private helper method
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

        protected bool IsRCRequest()
        {
            if (RequestUri.StartsWith("www.ruralcafe.net") ||
                RequestUri.StartsWith("http://www.ruralcafe.net"))
            {
                return true;
            }
            return false;
        }

        // checks to see if we're requesting a RuralCafe search
        public bool IsRCLocalSearch()
        {
            if (IsRCRequest() &&_requestObject.GetRCSearchField("button") == "Search")
            {
                return true;
            }
            return false;
        }

        public bool IsRCRemoteQuery()
        {
            if (IsRCRequest() && _requestObject.GetRCSearchField("button") == "Queue Request")
            {
                return true;
            }
            return false;
        }

        protected bool IsRCURLRequest()
        {
            if (RequestUri.Contains("URL+Request"))
            {
                return true;
            }
            return false;
        }

        #endregion

        #region Helper Functions

        // Write an OK response to the client
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

        // Write an error response to the client
        // with link to queue the page to RuralCafe
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

        // Send a string to a socket
        protected int SendMessage(string strMessage)
        {
            //Byte[] buffer = new Byte[strMessage.Length + 1];
            byte[] buffer = Encoding.UTF8.GetBytes(strMessage);
            int len = buffer.Length; //ASCII.GetBytes(strMessage.ToCharArray(), 0, strMessage.Length, buffer, 0);

            if (!_clientSocket.Connected)
            {
                LogDebug("socket closed for some reason");
                return 0;
            }
            try
            {
                _clientSocket.Send(buffer, len, 0);
            }
            catch (Exception e)
            {
                LogDebug("socket closed for some reason " + e.StackTrace + " " + e.Message);
                return 0;
            }
            return len;
        }

        // Read a string from a socket
        protected int RecvMessage(byte[] buf, ref string strMessage)
        {
            int iBytes = _clientSocket.Receive(buf, 1024, 0);
            strMessage = Encoding.ASCII.GetString(buf);
            return (iBytes);
        }

        #endregion

        #region Logging

        // log the request from the client
        public void LogRequest()
        {
            string str = _startTime + " " + _clientAddress.ToString() +
                         " GET " + RequestUri +
                         " REFERER " + ReferrerUri + " " + 
                         Status + " " + _requestObject._fileSize;
            _proxy.WriteMessage(_requestId, str);
        }

        // log the response from the server
        public void LogResponse()
        {
            string str = _endTime + " RSP " + RequestUri + " " + 
                        Status + " " + _requestObject._fileSize;
            _proxy.WriteMessage(_requestId, str);
        }

        // log exception
        public void LogDebug(string str)
        {
            _proxy.WriteDebug(_requestId, str);
        }

        #endregion

        #region Constants

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


        //removed since HttpUtil already handles this stuff
        /*
        public static string GetUnencodedUri(string encodedString)
        {
            foreach (KeyValuePair<string,string> symbolEncoding in _urlEncodingMap)
            {
                encodedString = encodedString.Replace(symbolEncoding.Key, symbolEncoding.Value);
            }

            return encodedString;
        }
        static void SetEncoding(string k, string v)
        {
            _urlEncodingMap.Add(k, v);
        }

        public static void FillURLEncodingMap()
        {
            SetEncoding("%24", "$");
            SetEncoding("%26", "&");
            SetEncoding("%2B", "+");
            SetEncoding("%2C", ",");
            SetEncoding("%2F", "/");
            SetEncoding("%3A", ":");
            SetEncoding("%3B", ";");
            SetEncoding("%3D", "=");
            SetEncoding("%3F", "?");
            SetEncoding("%40", "@");

            SetEncoding("%20", " ");
            SetEncoding("%22", "\"");
            SetEncoding("%3C", "<"); 
            SetEncoding("%3E", ">");
            SetEncoding("%23", "#");
            SetEncoding("%25", "%"); 
            SetEncoding("%7B", "{");
            SetEncoding("%7D", "}");
            SetEncoding("%7C", "|"); 
            SetEncoding("%5C", "\\");
            SetEncoding("%5E", "^");
            SetEncoding("%7E", "~");
            SetEncoding("%5B", "[");
            SetEncoding("%5D", "]");
            SetEncoding("%60", "`");

        }*/

        #endregion
    }
}
