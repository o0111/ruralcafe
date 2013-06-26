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
using System.IO;
using System.Web;
using System.Net;
using System.Collections.Specialized;
using System.Text.RegularExpressions;
using System.Net.Sockets;
using log4net;
using HtmlAgilityPack;

namespace RuralCafe.Util
{
    /// <summary>
    /// A set of utility functions for manipulating files and directories, 
    /// getting file extensions and mime types, and page contents.
    /// </summary>
    public class Utils
    {
        /// <summary>
        /// Map from file extension to HTTP MIME type
        /// </summary>
        private static Dictionary<string, string> _extMap = new Dictionary<string, string>();
        /// <summary>
        /// The buffer size for streaming.
        /// </summary>
        private static int _streamBufferSize = 32768;
        /// <summary>
        /// Matches "localhost" or "127.0.0.1" followed by anything but a dot.
        /// </summary>
        private static Regex localAddressRegex = new Regex(@"(?<add1>(localhost|127\.0\.0\.1))(?<add2>[^\.])");
        /// <summary>
        /// Regex for html tags
        /// </summary>
        private static Regex htmlTagRegex = new Regex(@"<[^<]+?>", RegexOptions.IgnoreCase);
        /// <summary>
        /// The local IP address.
        /// </summary>
        private static string localIPAdress = LocalIPAddress();
        /// <summary>
        /// A Lock object on the file system.
        /// </summary>
        private static Object filesystemLock = new Object();

        /// <summary>
        /// Creates a directory for a file (locked method).
        /// JAY: this lock is probably poorly implemented.
        /// </summary>
        /// <param name="fileName">Name of the file.</param>
        /// <returns>True or false for success or failure.</returns>
        public static bool CreateDirectoryForFile(string fileName)
        {
            if (fileName == null || fileName == "")
            {
                return false;
            }

            try
            {
                lock (filesystemLock)
                {
                    int offset = fileName.LastIndexOf(Path.DirectorySeparatorChar.ToString());
                    if (offset > 0)
                    {
                        string currPath = fileName.Substring(0, offset + 1);
                        if (!Directory.Exists(currPath) &&
                            !File.Exists(currPath))
                        {
                            System.IO.Directory.CreateDirectory(currPath);
                        }
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Deletes a file.
        /// </summary>
        /// <param name="fileName">Name of the file.</param>
        /// <returns>True or false for success or failure.</returns>
        public static bool DeleteFile(string fileName)
        {
            try
            {
                FileInfo f = new FileInfo(fileName);
                if (f.Exists)
                {
                    f.Delete();
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Creates a file.
        /// </summary>
        /// <param name="fileName">The name of the file.</param>
        /// <returns>Returns the FileStream for the created file.</returns>
        public static FileStream CreateFile(string fileName)
        {
            FileStream fs;
            try
            {
                fs = new FileStream(fileName, FileMode.Create);
                if (fs.CanSeek)
                {
                    fs.Seek(0, SeekOrigin.Begin);
                }
            }
            catch (Exception)
            {
                return null;
            }

            return fs;
        }        

        /// <summary>
        /// Helper function to get the file size on disk of a page.
        /// </summary>
        /// <param name="fileName">Name of the file.</param>
        /// <returns>-1 for non-existent file.</returns>
        public static long GetFileSize(string fileName)
        {
            FileInfo f;
            try
            {
                f = new FileInfo(fileName);

                if (!f.Exists)
                {
                    return -1;
                }

                if (f.Length >= 0)
                {
                    return f.Length;
                }
            }
            catch (Exception)
            {
                return -1;
            }

            return -1;
        }


        /// <summary>
        /// Gets the file extension from the file name.
        /// </summary>
        /// <param name="fileName">File name to parse.</param>
        /// <returns>File extension as a string.</returns>
        public static string GetFileExtensionFromFileName(string fileName)
        {
            string fileExtension = fileName;
            
            int offset1 = fileExtension.LastIndexOf("?");
            if (offset1 >= 0)
            {
                fileExtension = fileExtension.Substring(0, offset1);
            }

            int offset2 = fileExtension.LastIndexOf(".");
            if (offset2 > 0)
            {
                fileExtension = fileExtension.Substring(offset2);
            }
            return fileExtension;
        }
        /// <summary>
        /// Gets the file extension from a URI.
        /// </summary>
        /// <param name="stringUri">URI to parse.</param>
        /// <returns>File extension as a string.</returns>
        public static string GetFileExtension(string stringUri)
        {
            string fileExtension = stringUri;
            // throw away query terms
            fileExtension = fileExtension.Split('?')[0];
            fileExtension = fileExtension.Split('#')[0];

            int offset1 = fileExtension.LastIndexOf("/");
            int offset2 = fileExtension.LastIndexOf(".");
            if (offset2 > offset1)
            {
                return fileExtension.Substring(offset2);
            }
            
            return "";
        }

        /// <summary>
        /// Returns the HTTP "Content-Type" of an extension.
        /// </summary>
        /// <param name="extension">File extension.</param>
        /// <returns>Content type of the extension.</returns>
        public static string GetContentType(string extension)
        {
            if (_extMap.ContainsKey(extension))
            {
                return _extMap[extension];
            }
            // JAY: not sure how squid or whatever does this
            // content/unknown, but the problem is that after caching how do we know the content type without the extension in place
            return "content/unknown";
        }

        /// <summary>
        /// Tries to return the content type of a file based on its extension.
        /// Also tries to peek inside the file for HTML headers.
        /// </summary>
        /// <param name="fileName">Name of the file.</param>
        /// <returns>Content type of the file.</returns>
        public static string GetContentTypeOfFile(string fileName)
        {
            string fileExtension = GetFileExtensionFromFileName(fileName);

            if (!fileExtension.Equals(""))
            {
                return GetContentType(fileExtension);
            }

            // only try xhtml and html for pages without extensions
            try
            {
                // make sure file exists
                FileInfo f = new FileInfo(fileName);
                if (f.Exists)
                {
                    // read beginning of the file
                    string fileContents = ReadFileAsString(fileName).Trim();
                    if (fileContents != null)
                    {
                        if (fileContents.StartsWith("<!DOCTYPE html") ||
                            fileContents.StartsWith("<html>"))
                        {
                            return "text/html";
                        }
                    }
                }
            }
            catch (Exception)
            {
                // do nothing
            }

            return "content/unknown";
        }

        // XXX: Currently only based on file ending.
        // XXX: text content (xml, too) should be indexable
        /// <summary>
        /// Checks if the URI is parseable by RuralCafe.
        /// </summary>
        /// <param name="cacheFileName">The filename of the cached file.</param>
        /// <returns>True or false for parseable or not.</returns>
        public static bool IsParseable(string cacheFileName)
        {
            return GetContentTypeOfFile(cacheFileName).Contains("htm");
        }
        
        /// <summary>
        /// Checks if the URI is valid.
        /// </summary>
        /// <param name="Uri">URI.</param>
        /// <returns>True or false for valid or not.</returns>
        public static bool IsValidUri(string Uri)
        {
            // blank
            if (Uri.Trim().Length == 0)
            {
                return false;
            }

            // malformed
            try
            {
                HttpWebRequest tempRequest = (HttpWebRequest)WebRequest.Create(Uri);
            }
            catch (Exception)
            {
                return false;
            }

            // blank
            if (Uri.Equals("http://"))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Reads in a file as a string for ease of parsing.
        /// </summary>
        /// <param name="fileName">Name of the file.</param>
        /// <returns>String containing the file's contents. If there is a problem return an empty string.</returns>
        public static string ReadFileAsString(string fileName)
        {
            string str = "";

            try
            {
                FileInfo f = new FileInfo(fileName);
                if (!f.Exists)
                {
                    return str;
                }

                // open the file stream
                FileStream fs = f.Open(FileMode.Open, FileAccess.Read);

                // loop and get the bytes we need if we couldn't get it in one go
                StreamReader r = new StreamReader(fs);
                string t;
                while ((t = r.ReadLine()) != null)
                {
                    // append to the string
                    str += t;
                }

                r.Close();
                fs.Close();
            }
            catch (Exception)
            {
                // do nothing
            }

            return str;
        }

        /*
        /// <summary>
        /// Decompress a bz2 file and return the memorystream.
        /// </summary>
        /// <param name="fileName">Name of the file to decompress.</param>
        /// <returns>The file contents in a memorystream.</returns>
        public static MemoryStream BZ2DecompressFile(string fileName)
        {
            MemoryStream ms = new MemoryStream();
            FileStream bzipFileFs = new FileStream(fileName, FileMode.Open);
            ICSharpCode.SharpZipLib.BZip2.BZip2.Decompress(bzipFileFs, ms);
            return ms;
        }*/
        public static byte[] GetBytes(string str)
        {
            byte[] bytes = new byte[str.Length * sizeof(char)];
            System.Buffer.BlockCopy(str.ToCharArray(), 0, bytes, 0, bytes.Length);
            return bytes;
        }

        public static string GetString(byte[] bytes)
        {
            char[] chars = new char[bytes.Length / sizeof(char)];
            System.Buffer.BlockCopy(bytes, 0, chars, 0, bytes.Length);
            return new string(chars);
        }

        /// <summary>
        /// Gets the contents of the page. (dummy, output = input)
        /// </summary>
        /// <param name="pageContent">Page content.</param>
        /// <returns>String with the page content.</returns>
        public static string GetPageContent(string pageContent)
        {
            return pageContent;
        }
        
        /// <summary>
        /// Gets the title from a page.
        /// </summary>
        /// <param name="pageContent">Page content.</param>
        /// <returns>String containing the page title.</returns>
        public static string GetPageTitle(string pageContent)
        {
            int index1 = pageContent.IndexOf("<title>");
            int index2 = pageContent.IndexOf("</title>");
            // make sure the title tag exists
            if (index1 < 0 || index2 < 0)
            {
                return "";
            }
            // make sure there's something between the two indices
            index1 += "<title>".Length;
            if (index1 >= index2)
            {
                return "";
            }

            string title = pageContent.Substring(index1, index2 - index1);

            return title;
        }

        /// <summary>
        /// Strips a string of all HTML tags.
        /// </summary>
        /// <param name="source">Page content.</param>
        /// <returns>String containing the stripped text.</returns>
        public static string StripTagsCharArray(string source)
        {
            return StripTagsCharArray(source, true);
        }

        /// <summary>
        /// Strips a string of all HTML tags, except bold tags, if wished.
        /// </summary>
        /// <param name="source">Page content.</param>
        /// <param name="stripBoldTags">If true, bold tags are stripped, too.</param>
        /// <returns>String containing the stripped text.</returns>
        public static string StripTagsCharArray(string source, bool stripBoldTags)
        {
            if (stripBoldTags)
            {
                return htmlTagRegex.Replace(source, "");
            }
            else
            {
                string result = htmlTagRegex.Replace(source, delegate(Match match)
                {
                    string matchedString = match.ToString();
                    return matchedString.Equals("<b>") || matchedString.Equals("</b>") ?
                        matchedString : "";
                });

                // XXX: sometimes there is "<" or ">" between "<b>" and "</b>". Then the closing bold tag
                // is not recognized. This may also be an error of the Lucene highlighter or its usage.
                return result;
            }
        }

        /// <summary>
        /// Replaces all specified HTML tags and their content.
        /// </summary>
        /// <param name="input">The input string.</param>
        /// <param name="tag">The HTML tag to remove</param>
        /// <returns>The input string without the specified HTML tags and their content.</returns>
        public static string RemoveHTMLTagAndContent(string input, string tag)
        {
            return Regex.Replace(input, @"<" + tag + ".+?(>.+?</" + tag + ">|/>)", "");
        }

        /// <summary>
        /// Removes the Head of a HTML string, if there is any.
        /// </summary>
        /// <param name="input">An HMTL string.</param>
        /// <returns>The same string, with anything before the body removed.</returns>
        public static string RemoveHead(string input)
        {
            // Remove everything before <body>, if there is a body.
            int index = input.IndexOf("<body>");
            return index != -1 ? input.Substring(index) : input;
        }

        /// <summary>
        /// Extracts Text from HTML pages.
        /// Source: http://stackoverflow.com/questions/2113651/how-to-extract-text-from-resonably-sane-html (Modified)
        /// </summary>
        /// <param name="html">The HTML text.</param>
        /// <returns>The plain text.</returns>
        public static string ExtractText(string html)
        {
            if (html == null)
            {
                throw new ArgumentNullException("html");
            }

            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(html);
            List<string> chunks = new List<string>();

            // Tags that should be removed
            string[] badTags = new string[] { "script", "meta", "style" };

            foreach (HtmlNode item in doc.DocumentNode.DescendantsAndSelf())
            {
                if (item.NodeType == HtmlNodeType.Text)
                {
                    if(item.ParentNode != null && !badTags.Contains(item.ParentNode.Name))
                    {
                        if (item.InnerText.Trim() != "")
                        {
                            chunks.Add(item.InnerText.Trim());
                        }
                    }
                }
            }
            return String.Join(" ", chunks);
        }

        /// <summary>
        /// Adds a key value pair to the extension to content-type map.
        /// </summary>
        /// <param name="k">Key.</param>
        /// <param name="v">Value.</param>
        private static void SetSuffix(string k, string v)
        {
            _extMap.Add(k, v);
        }

        /// <summary>
        /// Streams the whole Body of a HttpWebResponse.
        /// </summary>
        /// <param name="response">The response.</param>
        /// <returns>The whole Body as a string.</returns>
        public static string StreamContent(HttpWebResponse response)
        {
            return ReadStreamToEnd(response.GetResponseStream());
        }

        /// <summary>
        /// Reads in incoming stream to the end and returns the result
        /// </summary>
        /// <param name="inStream">The incoming stream.</param>
        /// <returns>The content of the stream.</returns>
        public static string ReadStreamToEnd(Stream inStream)
        {
            return new StreamReader(inStream).ReadToEnd();
        }

        /// <summary>
        /// Streams all content from an input- to an output stream. Any exceptions are forwarded.
        /// </summary>
        /// <param name="inStream">The input stream.</param>
        /// <param name="outStream">The output stream.</param>
        /// <returns>The bytes written to the output stream.</returns>
        public static long Stream(Stream inStream, Stream outStream)
        {
            byte[] buffer = new byte[_streamBufferSize];
            long bytesWritten = 0;
            while (true)
            {
                int read = inStream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                    break;
                outStream.Write(buffer, 0, read);
                bytesWritten += read;
            }
            return bytesWritten;
        }

        /// <summary>
        /// Flattens a string[] into one string, comma separated.
        /// </summary>
        /// <param name="array">The array.</param>
        /// <returns>The string.</returns>
        public static string flattenStringArray(string[] array)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string content in array)
            {
                sb.Append(content);
                sb.Append(",");
            }
            sb.Remove(sb.Length - 1, 1);
            return sb.ToString();
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
        /// Creates an outgoing HttpWebRequest from an incoming HttpListenerRequest.
        /// </summary>
        /// <param name="listenerRequest">The HttpListenerRequest.</param>
        /// <returns>The HttpWebRequest.</returns>
        public static HttpWebRequest CreateWebRequest(HttpListenerRequest listenerRequest)
        {
            string url = UseLocalNetworkAdressForLocalAdress(listenerRequest.RawUrl);
            HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(url);
            webRequest.Method = listenerRequest.HttpMethod;

            foreach(string key in listenerRequest.Headers)
            {
                // FIXME ATM no Accept-Encoding due to GZIP failure
                // We handle these after the foreach loop 
                // (Do NOT set Host- or Proxy-Connection-header!)
                // Range may be ignored by Servers anyway, Expect will never be set by us.
                if (key.Equals("User-Agent") || key.Equals("Accept") || key.Equals("Referer")
                     || key.Equals("Content-Type") || key.Equals("Content-Length")
                     || key.Equals("Host") || key.Equals("Proxy-Connection")
                     || key.Equals("Range")|| key.Equals("Expect") || key.Equals("Accept-Encoding"))
                {
                    continue;
                }
                foreach (string value in listenerRequest.Headers.GetValues(key))
                {
                    // Headers that need special treatment
                    if (key.Equals("If-Modified-Since"))
                    {
                        webRequest.IfModifiedSince = DateTime.Parse(value);
                        continue;
                    }
                    if (key.Equals("Connection"))
                    {
                        if(value.Equals("keep-alive"))
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
                    catch(Exception e)
                    {
                        // XXX: remove the try-catch, when we are sure we did not forget any header field
                        // that needs "special treatment"
                        LogManager.GetLogger(typeof(Utils)).Error(e);
                    }
                }
            }
            // Copy headers where C# offers properties or methods (except Host!)
            webRequest.Accept = flattenStringArray(listenerRequest.AcceptTypes);
            webRequest.UserAgent = listenerRequest.UserAgent;
            webRequest.ContentLength = listenerRequest.ContentLength64;
            webRequest.ContentType = listenerRequest.ContentType;
            webRequest.Referer = listenerRequest.UrlReferrer == null ? null : listenerRequest.UrlReferrer.ToString();
            
            return webRequest;
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
                Stream(listenerRequest.InputStream, webRequest.GetRequestStream());
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
            if (webRequest.Method != "HEAD" && webRequest.Method != "GET")
            {
                // Never Expect 100 Continue!
                webRequest.ServicePoint.Expect100Continue = false;
                webRequest.GetRequestStream().Write(body, 0, body.Length);
                webRequest.GetRequestStream().Close();
            }
        }

        /// <summary>
        /// Checks if a filename is too long. A filename must be shorter than 260 chars and its
        /// path must be shorter than 248 chars.
        /// </summary>
        /// <param name="fileName">The filename.</param>
        /// <returns>true, if the filename is OK; false, if it is too long.</returns>
        public static bool IsNotTooLongFileName(string fileName)
        {
            string pathName = fileName.Substring(fileName.LastIndexOf(Path.DirectorySeparatorChar));
            return pathName.Length < 248 && fileName.Length < 260;
        }

        /// <summary>
        /// Determines the local IP address.
        /// </summary>
        /// <returns>The local IP address.</returns>
        public static string LocalIPAddress()
        {
            IPHostEntry host;
            string localIP = "";
            host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (IPAddress ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    localIP = ip.ToString();
                    break;
                }
            }
            return localIP;
        }

        /// <summary>
        /// Fills in the extension to content-type map with static mappings.
        /// </summary>
        public static void FillExtMap()
        {
            SetSuffix("", "content/unknown");
            SetSuffix(".uu", "application/octet-stream");
            SetSuffix(".exe", "application/octet-stream");
            SetSuffix(".ps", "application/postscript");
            SetSuffix(".zip", "application/zip");
            SetSuffix(".sh", "application/x-shar");
            SetSuffix(".tar", "application/x-tar");
            SetSuffix(".snd", "audio/basic");
            SetSuffix(".au", "audio/basic");
            SetSuffix(".wav", "audio/x-wav");
            SetSuffix(".gif", "image/gif");
            SetSuffix(".jpg", "image/jpeg");
            SetSuffix(".jpeg", "image/jpeg");
            SetSuffix(".png", "image/png");
            SetSuffix(".htm", "text/html");
            SetSuffix(".html", "text/html");
            SetSuffix(".text", "text/plain");
            SetSuffix(".c", "text/plain");
            SetSuffix(".cc", "text/plain");
            SetSuffix(".c++", "text/plain");
            SetSuffix(".h", "text/plain");
            SetSuffix(".pl", "text/plain");
            SetSuffix(".txt", "text/plain");
            SetSuffix(".java", "text/plain");
            SetSuffix(".xml", "text/xml");
            SetSuffix(".js", "application/javascript");
            SetSuffix(".css", "text/css");
            SetSuffix(".ico", "image/x-icon");
            SetSuffix(".asp", "text/html");
            SetSuffix(".php", "text/html");
        }
    }
}