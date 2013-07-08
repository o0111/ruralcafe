using log4net;
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

namespace RuralCafe.Util
{
    /// <summary>
    /// Util methods for anything connected to HTTP: creating requests, checking URIs, Reading or Sending Request Bodies...
    /// </summary>
    public static class HttpUtils
    {
        //Constants
        private const string HTTP = "http://";
        /// <summary>
        /// Matches "localhost" or "127.0.0.1" followed by anything but a dot.
        /// </summary>
        private static readonly Regex localAddressRegex = new Regex(@"(?<add1>(localhost|127\.0\.0\.1))(?<add2>[^\.])");

        /// <summary>
        /// The local IP address.
        /// </summary>
        private static readonly IPAddress localIPAdress = LocalIPAddress();

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
                // (Do NOT set Host- or Proxy-Connection-header!)
                // Range may be ignored by Servers anyway, Expect will never be set by us.
                // Accept-Encoding can be set manually, but we're ignoring it here
                if (key.Equals("User-Agent") || key.Equals("Accept") || key.Equals("Referer")
                     || key.Equals("Content-Type") || key.Equals("Content-Length")
                     || key.Equals("Host") || key.Equals("Proxy-Connection")
                     || key.Equals("Range") || key.Equals("Expect") || key.Equals("Accept-Encoding"))
                {
                    continue;
                }
                foreach (string value in headers.GetValues(key))
                {
                    // Headers that need special treatment
                    if (key.Equals("If-Modified-Since"))
                    {
                        webRequest.IfModifiedSince = DateTime.Parse(value);
                        continue;
                    }
                    if (key.Equals("Connection"))
                    {
                        if (value.Equals("keep-alive"))
                        {
                            webRequest.KeepAlive = true;
                            continue;
                        }
                        if (value.Equals("close"))
                        {
                            webRequest.KeepAlive = false;
                            continue;
                        }
                        // else:
                        webRequest.Connection = value;
                    }
                    try
                    {
                        webRequest.Headers.Add(key, value);
                    }
                    catch (Exception e)
                    {
                        // This should ideally not happen!
                        LogManager.GetLogger(typeof(HttpUtils)).Error(e);
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
            return localAddressRegex.Replace(address, localIPAdress + "${add2}");
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
        /// Determines the network speed. FIXME this gets current network speed, not max possible network speed.
        /// 
        /// try http://stackoverflow.com/questions/16498558/internet-speed-and-bandwidth-usage
        /// 
        /// Source: http://stackoverflow.com/questions/13600604/how-to-get-accurate-download-upload-speed-in-c-net
        /// </summary>
        /// <returns></returns>
        public static void DetermineNetworkSpeed()
        {
            IPAddress localAddr = localIPAdress;

            NetworkInterface[] nics = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            NetworkInterface nic = null;
            foreach (NetworkInterface n in nics)
            {
                IPInterfaceProperties ipProps = n.GetIPProperties();
                // check if localAddr is in ipProps.UnicastAddresses
                foreach (UnicastIPAddressInformation unicastAddr in ipProps.UnicastAddresses)
                {
                    if (unicastAddr.Address.Equals(localAddr))
                    {
                        nic = n;
                        break;
                    }
                }
            }

            if (nic == null)
            {
                // TODO
                return;
            }
            var reads = Enumerable.Empty<double>();
            Stopwatch sw = new Stopwatch();
            long lastBr = nic.GetIPv4Statistics().BytesReceived;
            for (int i = 0; i < 1000; i++)
            {

                sw.Restart();
                Thread.Sleep(100);
                double elapsed = sw.Elapsed.TotalSeconds;
                long br = nic.GetIPv4Statistics().BytesReceived;

                double local = (br - lastBr) / elapsed;
                lastBr = br;

                // Keep last 20, ~2 seconds
                reads = new[] { local }.Concat(reads).Take(20);

                if (i % 10 == 0)
                { // ~1 second
                    double bSec = reads.Sum() / reads.Count();
                    double kbs = (bSec * 8) / 1024;
                    Console.WriteLine("Kb/s ~ " + kbs);
                }
            }
        }

        /// <summary>
        /// Adds "http://" to the given URI, if it does not start with it already.
        /// </summary>
        /// <param name="uri">The current URI.</param>
        /// <returns>The new URI.</returns>
        public static String AddHttpPrefix(String uri)
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
        public static String RemoveHttpPrefix(String uri)
        {
            if (uri.StartsWith(HTTP))
            {
                return uri.Substring(HTTP.Length);
            }
            return uri;
        }
    }
}
