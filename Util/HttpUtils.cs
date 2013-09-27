using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Util
{
    /// <summary>
    /// Util methods for anything connected to HTTP: creating requests, checking URIs, Reading or Sending Request Bodies...
    /// </summary>
    public static class HttpUtils
    {
        //Constants
        private const string HTTP = "http://";
        private const string WWW = "www.";

        /// <summary>
        /// The local IP address.
        /// </summary>
        public static readonly IPAddress LOCAL_IP_ADDRESS = LocalIPAddress();

        /// <summary>
        /// Creates an outgoing HttpWebRequest from an incoming HttpListenerRequest.
        /// </summary>
        /// <param name="listenerRequest">The HttpListenerRequest.</param>
        /// <returns>The HttpWebRequest.</returns>
        public static HttpWebRequest CreateWebRequest(HttpListenerRequest listenerRequest)
        {
            string url = UseLocalNetworkAdressForLocalAdress(listenerRequest.RawUrl);
            HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(url);
            webRequest.Method = listenerRequest.HttpMethod;
            // Integrate Headers
            IntegrateHeadersIntoWebRequest(webRequest, listenerRequest.Headers);
            // Copy headers where C# offers properties or methods (except Host!)
            if (listenerRequest.AcceptTypes != null)
            {
                webRequest.Accept = String.Join(",", listenerRequest.AcceptTypes);
            }
            webRequest.UserAgent = listenerRequest.UserAgent;
            webRequest.ContentLength = listenerRequest.ContentLength64;
            webRequest.ContentType = listenerRequest.ContentType;
            webRequest.Referer = listenerRequest.UrlReferrer == null ? null : listenerRequest.UrlReferrer.ToString();
            // Always accept gzip or deflate encoding!
            // (the remote proxy will ignore this, but it can be useful for actual websites)
            // With this setting the response will be decompressed automatically.
            webRequest.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            return webRequest;
        }

        /// <summary>
        /// Integrates headers into a HttpWebRequest.
        /// </summary>
        /// <param name="webRequest">The web request.</param>
        /// <param name="headers">The headers.</param>
        public static void IntegrateHeadersIntoWebRequest(HttpWebRequest webRequest, NameValueCollection headers)
        {
            foreach (string key in headers)
            {
                string keyLower = key.ToLower();
                // (Do NOT set Host- or Proxy-Connection-header!)
                // Range may be ignored by Servers anyway, Expect will never be set by us.
                // Accept-Encoding can be set manually, but we're ignoring it here
                if (keyLower.Equals("user-agent") || keyLower.Equals("accept") || keyLower.Equals("referer")
                     || keyLower.Equals("content-type") || keyLower.Equals("content-length")
                     || keyLower.Equals("host") || keyLower.Equals("proxy-connection")
                     || keyLower.Equals("range") || keyLower.Equals("expect") || keyLower.Equals("accept-encoding"))
                {
                    continue;
                }
                foreach (string value in headers.GetValues(key))
                {
                    string valueLower = value.ToLower();
                    // Headers that need special treatment
                    if (keyLower.Equals("if-modified-since"))
                    {
                        webRequest.IfModifiedSince = DateTime.Parse(value);
                        continue;
                    }
                    else if (keyLower.Equals("connection"))
                    {
                        if (valueLower.Equals("keep-alive"))
                        {
                            webRequest.KeepAlive = true;
                            continue;
                        }
                        else if (valueLower.Equals("close"))
                        {
                            webRequest.KeepAlive = false;
                            continue;
                        }
                        // else:
                        webRequest.Connection = value;
                        continue;
                    }
                    try
                    {
                        webRequest.Headers.Add(key, value);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        /// <summary>
        /// Copies everything from an incoming WebResponse to an outgoing HttpListenerResponse.
        /// </summary>
        /// <param name="outgoingResponse">The outgoing response.</param>
        /// <param name="originalResponse">The incoming response.</param>
        public static void CopyWebResponse(HttpListenerResponse outgoingResponse, HttpWebResponse originalResponse)
        {
            IntegrateHeadersIntoWebResponse(outgoingResponse, originalResponse.Headers);
            // Do not set content length or content encoding
            outgoingResponse.ContentType = originalResponse.ContentType;
            outgoingResponse.StatusCode = (int)originalResponse.StatusCode;
            outgoingResponse.Cookies = originalResponse.Cookies;
        }

        /// <summary>
        /// Integrates headers into a HttpListenerResponse.
        /// </summary>
        /// <param name="response">The response.</param>
        /// <param name="headers">The headers.</param>
        public static void IntegrateHeadersIntoWebResponse(HttpListenerResponse response, NameValueCollection headers)
        {
            foreach (string key in headers)
            {
                string keyLower = key.ToLower();
                if (keyLower.Equals("content-type") || keyLower.Equals("content-length") || keyLower.Equals("content-encoding")
                    || keyLower.Equals("keep-alive"))
                {
                    continue;
                }
                foreach (string value in headers.GetValues(key))
                {
                    string valueLower = value.ToLower();
                    // Headers that need special treatment
                    if (keyLower.Equals("transfer-encoding"))
                    {
                        response.SendChunked = valueLower.Equals("chunked");
                        continue;
                    }
                    else if (keyLower.Equals("connection"))
                    {
                        if (valueLower.Equals("keep-alive"))
                        {
                            response.KeepAlive = true;
                            continue;
                        }
                        else if (valueLower.Equals("close"))
                        {
                            response.KeepAlive = false;
                            continue;
                        }
                    }

                    try
                    {
                        response.Headers.Add(key, value);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }
        
        /// <summary>
        /// Streams the body for a request.
        /// </summary>
        /// <param name="listenerRequest">The incoming request.</param>
        /// <param name="webRequest">The outgoing request.</param>
        public static void StreamBody(HttpListenerRequest listenerRequest, HttpWebRequest webRequest)
        {
            // Stream body for non HEAD/GET requests
            if (webRequest.Method != "HEAD" && webRequest.Method != "GET")
            {
                // Never Expect 100 Continue!
                webRequest.ServicePoint.Expect100Continue = false;
                Utils.Stream(listenerRequest.InputStream, webRequest.GetRequestStream());
                webRequest.GetRequestStream().Close();
            }
        }

        /// <summary>
        /// Receives the body for an incoming request.
        /// </summary>
        /// <param name="listenerRequest">The incoming request.</param>
        /// <returns>The body for POST/... or null for GET/HEAD.</returns>
        public static byte[] ReceiveBody(HttpListenerRequest listenerRequest)
        {
            // Stream body for non HEAD/GET requests
            if (listenerRequest.HttpMethod != "HEAD" && listenerRequest.HttpMethod != "GET")
            {
                byte[] buffer = new byte[listenerRequest.ContentLength64];
                var memoryStream = new MemoryStream(buffer);
                listenerRequest.InputStream.CopyTo(memoryStream);
                return memoryStream.ToArray();
            }
            return null;
        }

        /// <summary>
        /// Sends the body for an outgoing request.
        /// </summary>
        /// <param name="webRequest">The outgoing request.</param>
        /// <param name="body">The body to send.</param>
        public static void SendBody(HttpWebRequest webRequest, byte[] body)
        {
            // Stream body for non HEAD/GET requests
            if (webRequest.Method != "HEAD" && webRequest.Method != "GET" && body != null)
            {
                // Never Expect 100 Continue!
                webRequest.ServicePoint.Expect100Continue = false;
                webRequest.GetRequestStream().Write(body, 0, body.Length);
                webRequest.GetRequestStream().Close();
            }
        }

        /// <summary>
        /// Streams the whole Body of a HttpWebResponse.
        /// </summary>
        /// <param name="response">The response.</param>
        /// <returns>The whole Body as a string.</returns>
        public static string StreamContent(HttpWebResponse response)
        {
            return Utils.ReadStreamToEnd(response.GetResponseStream());
        }

        /// <summary>
        /// Checks if the URI is valid.
        /// </summary>
        /// <param name="uri">URI.</param>
        /// <returns>True or false for valid or not.</returns>
        public static bool IsValidUri(string uri)
        {
            try
            {
                HttpWebRequest tempRequest = (HttpWebRequest)WebRequest.Create(uri);
            }
            catch (Exception)
            {
                // malformed
                return false;
            }

            return uri.Trim().Length != 0 && !uri.Equals("http://");
        }

        /// <summary>
        /// Replaces "localhost" or "127.0.0.1" with the local network address.
        /// Otherwise the remote proxy would be bypassed due to a hardcoded error
        /// in .NET framework.
        /// </summary>
        /// <param name="address">The address.</param>
        /// <returns>The new address.</returns>
        public static string UseLocalNetworkAdressForLocalAdress(string address)
        {
            return RegExs.LOCAL_ADDRESS_REGEX.Replace(address, LOCAL_IP_ADDRESS + "${add2}");
        }

        /// <summary>
        /// Determines the local IP address.
        /// </summary>
        /// <returns>The local IP address (or null).</returns>
        private static IPAddress LocalIPAddress()
        {
            IPHostEntry host;
            IPAddress localIP = null;
            host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (IPAddress ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    localIP = ip;
                    break;
                }
            }
            return localIP;
        }

        /// <summary>
        /// Adds "http://" to the given URI, if it does not start with it already.
        /// </summary>
        /// <param name="uri">The current URI.</param>
        /// <returns>The new URI.</returns>
        public static string AddHttpPrefix(string uri)
        {
            if (!uri.StartsWith(HTTP))
            {
                return HTTP + uri;
            }
            return uri;
        }

        /// <summary>
        /// Removes "http://" from the given URI, if it does start with it.
        /// </summary>
        /// <param name="uri">The current URI.</param>
        /// <returns>The new URI.</returns>
        public static string RemoveHttpPrefix(string uri)
        {
            if (uri.StartsWith(HTTP))
            {
                return uri.Substring(HTTP.Length);
            }
            return uri;
        }

        /// <summary>
        /// Removes "www." from the given URI, if it does start with it.
        /// </summary>
        /// <param name="uri">The current URI.</param>
        /// <returns>The new URI.</returns>
        public static string RemoveWWWPrefix(string uri)
        {
            if (uri.StartsWith(WWW))
            {
                return uri.Substring(WWW.Length);
            }
            return uri;
        }
    }
}
