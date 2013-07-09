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
using System.Net;
using System.Threading;
using System.Web;
using System.Collections.Specialized;
using System.Text.RegularExpressions;
using RuralCafe.Util;
using Newtonsoft.Json;

namespace RuralCafe
{
    /// <summary>
    /// A Rural Cafe request.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class RCRequest
    {
        // Regex's for safe URI replacements
        private static readonly Regex unsafeChars1 = new Regex(@"[^a-z0-9\\\-\.]");
        private static readonly Regex unsafeChars2 = new Regex(@"[^a-z0-9/\-\.]");

        [JsonProperty]
        private string _anchorText;
        [JsonProperty]
        private string _contentSnippet;
        [JsonProperty]
        private string _refererUri;
        [JsonProperty]
        private string _fileName;
        [JsonProperty]
        private string _hashPath;
        // hashPath without the directory seperators
        [JsonProperty]
        private string _itemId;
        [JsonProperty]
        private string _cacheFileName;

        [JsonProperty]
        private string _uriBeforeRedirect;

        [JsonProperty]
        private RequestHandler.Status _status;
        /// <summary>
        /// The underlying web request.
        /// </summary>
        [JsonProperty]
        private HttpWebRequest _webRequest;
        // Body for POSTs, ...
        [JsonProperty]
        private byte[] _body;

        private HttpWebResponse _webResponse;
        [JsonProperty]
        private long _fileSize;

        private RequestHandler _requestHandler;

        private DateTime _startTime;
        private DateTime _finishTime;

        // threading support
        private ManualResetEvent[] _resetEvents;
        private int _childNumber;

        # region Accessors
        /// <summary> 
        /// Iff the request has been redirected, this is the old URI.
        /// Will be null, if the request was not made yet or there was no redirect.
        /// </summary>
        public string UriBeforeRedirect
        {
            get { return _uriBeforeRedirect; }
        }
        /// <summary>The requested URI OR the response uri if there is a response.</summary>
        public string Uri
        {
            get { return _webResponse != null ? _webResponse.ResponseUri.ToString() : _webRequest.RequestUri.ToString(); }
        }
        /// <summary>The content snippet of the link. Only used in conjuction with local/google search.</summary>
        public string ContentSnippet
        {
            set { _contentSnippet = value; }
            get { return _contentSnippet; }
        }
        /// <summary>The anchor Text of the requested Uri.</summary>
        public string AnchorText
        {
            set { _anchorText = value; }
            get { return _anchorText; }
        }
        /// <summary>The URI of the referer object.</summary>
        public string RefererUri
        {
            set { _refererUri = value; }
            get { return _refererUri; }
        }
        /// <summary>The file name of the object.</summary>
        public string FileName
        {
            set { _fileName = value; }
            get { return _fileName; }
        }
        /// <summary>The hashed file name of the object.</summary>
        public string HashPath
        {
            set { _hashPath = value; }
            get { return _hashPath; }
        }
        /// <summary>The itemId of the object.</summary>
        public string ItemId
        {
            get { return _itemId; }
        }
        /// <summary>The file name of the object if it is cached.</summary>
        public string CacheFileName
        {
            set { _cacheFileName = value; }
            get { return _cacheFileName; }
        }
        /// <summary>The status of this request.</summary>
        public RequestHandler.Status RequestStatus
        {
            set { _status = value; }
            get { return _status; }
        }
        /// <summary>The web request.</summary>
        public HttpWebRequest GenericWebRequest
        {
            get { return _webRequest; }
        }
        /// <summary>The web response.</summary>
        public HttpWebResponse GenericWebResponse
        {
            get { return _webResponse; }
        }
        /// <summary>The body for POSTs,... null otherwise.</summary>
        public byte[] Body
        {
            get { return _body; }
        }
        /// <summary>The size of the file if it is cached.</summary>
        public long FileSize
        {
            set { _fileSize = value; }
            get { return _fileSize; }
        }
        /// <summary>The root requesthandler of this request.</summary>
        public RequestHandler RootRequest
        {
            get { return _requestHandler; }
        }
        /// <summary>The array of events used to indicate whether the children requests in their own threads are completed.</summary>
        public ManualResetEvent[] ResetEvents
        {
            set { _resetEvents = value; }
            get { return _resetEvents; }
        }
        /// <summary>The child number if this is a child of another request.</summary>
        public int ChildNumber
        {
            set { _childNumber = value; }
            get { return _childNumber; }
        }
        /// <summary>The time this request was started.</summary>
        public DateTime StartTime
        {
            set { _startTime = value; }
            get { return _startTime; }
        }
        /// <summary>The time this request was finished.</summary>
        public DateTime FinishTime
        {
            set { _finishTime = value; }
            get { return _finishTime; }
        }

        # endregion

        /// <summary>
        /// Constructor for a RuralCafe Request.
        /// </summary>
        /// <param name="requestHandler">The handler for the request.</param>
        /// <param name="request">The request.</param>
        public RCRequest(RequestHandler requestHandler, HttpWebRequest request)
            :this(requestHandler, request, "", "", null)
        {
            // do nothing
        }

        /// <summary>
        /// Constructor for a RuralCafe Request.
        /// </summary>
        /// <param name="requestHandler">The handler for the request.</param>
        /// <param name="request">The request.</param>
        /// <param name="anchorText">Text of the anchor tag.</param>
        /// <param name="referrerUri">URI of the referer.</param>
        /// <param name="body">The body for POSTs, ...</param>
        public RCRequest(RequestHandler requestHandler, HttpWebRequest request, string anchorText,
            string referrerUri, byte[] body)
        {
            _anchorText = anchorText;
            _refererUri = referrerUri.Trim();

            _status = RequestHandler.Status.Pending;

            _webRequest = request;
            _webRequest.Timeout = RequestHandler.WEB_REQUEST_DEFAULT_TIMEOUT;
            _webRequest.Referer = _refererUri;
            _body = body;

            _fileName = UriToFilePath(_webRequest.RequestUri.ToString());
            _hashPath = GetHashPath(_fileName);
            _itemId = _hashPath.Replace(Path.DirectorySeparatorChar.ToString(), "");
            _cacheFileName = requestHandler.GenericProxy.CachePath + _hashPath + _fileName;
            _fileSize = 0;

            _requestHandler = requestHandler;

            _startTime = DateTime.Now;
            _finishTime = _startTime;
        }
        
        /// <summary>
        /// Sets the proxy to be used for this request.
        /// </summary>
        /// <param name="proxy">Proxy info.</param>
        /// <param name="timeout">Timeout.</param>
        public void SetProxyAndTimeout(WebProxy proxy, int timeout)
        {
             GenericWebRequest.Proxy = proxy;
             GenericWebRequest.Timeout = timeout;
        }

        /// <summary>Override object equality for request matching.</summary>
        public override bool Equals(object obj)
        {
            if (!(obj is RCRequest))
            {
                return false;
            }
            RCRequest other = (RCRequest)obj;
            return this.Uri.Equals(other.Uri) && this._webRequest.Method.Equals(other._webRequest.Method)
                && this.Body == other.Body;
        }
        /// <summary>Override object hash code for request matching.</summary>
        public override int GetHashCode()
        {
            return Uri.GetHashCode() * _webRequest.Method.GetHashCode() * Body.GetHashCode();
        }

        /// <summary>
        /// Gets the path of a filename from an URI. Relative to the cache path.
        /// </summary>
        /// <param name="uri">The URI.</param>
        /// <returns>Retalive cache file name.</returns>
        public static string GetRelativeCacheFileName(string uri)
        {
            string fileName = UriToFilePath(uri);
            string hashPath = GetHashPath(fileName);
            return hashPath + fileName;
        }

        /// <summary>
        /// Actually hashes the file name to a file path.
        /// </summary>
        /// <param name="fileName">File name to hash.</param>
        /// <returns>Hashed file path.</returns>
        public static string GetHashPath(string fileName)
        {
            // for compability with linux filepath delimeter
            fileName = fileName.Replace(Path.DirectorySeparatorChar.ToString(), "");
            int value1 = 0;
            int value2 = 0;

            int hashSpace = 5000;
            int length = fileName.Length;
            if (length == 0) {
                return "0" + Path.DirectorySeparatorChar.ToString() + "0" + Path.DirectorySeparatorChar.ToString() + fileName;
            }

            value1 = HashString(fileName.Substring(length/2));
            value2 = HashString(fileName.Substring(0, length/2));

            if (value1 < 0)
            {
                value1 = value1 * -1;
            }

            if (value2 < 0)
            {
                value2 = value2 * -1;
            }

            value1 = value1 % hashSpace;
            value2 = value2 % hashSpace;

            string hashedPath = value1.ToString() + Path.DirectorySeparatorChar.ToString() + value2.ToString() + Path.DirectorySeparatorChar.ToString(); // +fileName;
            return hashedPath;
        }
        /// <summary>
        /// Port of Python implementation of string hashing.
        /// </summary>
        /// <param name="s">String to hash.</param>
        /// <returns>Hashed value as an integer.</returns>
        private static int HashString(string s)
        {
            if (s == null)
                return 0;
            int value = (int)s[0] << 7;
            for (int i = 0; i < s.Length; i++) {
                char c = s[i];
                value = (1000003 * value) ^ (int)c;
            }
            value = value ^ s.Length;
            if (value == -1) {
                value = -2;
            }
            return value;
        }

        /// <summary>Set the reset event.</summary>
        public void SetDone()
        {
            if (_childNumber < _resetEvents.Length)
            {
                _resetEvents[_childNumber].Set();
            }
        }

        /// <summary>
        /// Translates a URI to a file path.
        /// Synchronized with CIP implementation in Python
        /// </summary>
        /// <param name="uri">The URI to translate.</param>
        /// <returns>Translated file path.</returns>
        public static string UriToFilePath(string uri)
        {
            // need to shrink the size of filenames for ruralcafe search prefs
            if (uri.Contains("ruralcafe.net") &&
                uri.Contains("textfield"))
            {
                int offset1 = uri.IndexOf("textfield");
                uri = uri.Substring(offset1 + "textfield".Length + 1);
                offset1 = uri.IndexOf('&');
                //offset1 = fileName.IndexOf("%26");
                if (offset1 > 0)
                {
                    uri = uri.Substring(0, offset1);
                }
            }

            // trim http
            uri = HttpUtils.RemoveHttpPrefix(uri);

            if (uri.IndexOf("/") == -1)
            {
                uri = uri + "/";
            }

            uri = uri.Replace("/", Path.DirectorySeparatorChar.ToString());

            uri = System.Web.HttpUtility.UrlDecode(uri);
            string fileName = MakeSafeUri(uri);

            // fix the filename extension
            if (fileName.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                fileName = fileName + "index.html";
            }

            return fileName;
        }
        /// <summary>
        /// Makes a URI safe for windows.
        /// PPrivate helper for UriToFilePath.
        /// </summary>
        /// <param name="uri">URI to make safe.</param>
        /// <returns>Safe URI.</returns>
        private static string MakeSafeUri(string uri)
        {
            // first trim the raw string
            string safe = uri.Trim();

            // replace spaces with hyphens
            safe = safe.Replace(" ", "-").ToLower();

            // replace any 'double spaces' with singles
            if (safe.IndexOf("--") > -1)
            {
                while (safe.IndexOf("--") > -1)
                {
                    safe = safe.Replace("--", "-");
                }
            }

            // trim out illegal characters
            if (Path.DirectorySeparatorChar == '\\')
            {
                safe = unsafeChars1.Replace(safe, "");
            }
            else
            {
                safe = unsafeChars2.Replace(safe, "");
            }

            // trim the length
            if (safe.Length > 220)
                safe = safe.Substring(0, 219);

            // clean the beginning and end of the filename
            char[] replace = { '-', '.' };
            safe = safe.TrimStart(replace);
            safe = safe.TrimEnd(replace);

            return safe;
        }

        /// <summary>
        /// Downloads a request and returns the result as a string.
        /// </summary>
        /// <returns>The requested webpage or <code>null</code> if it failed.</returns>
        public string DownloadAsString()
        {
            _requestHandler.Logger.Debug("downloading as string: " + _webRequest.RequestUri);
            // Stream parameters, if we have non GET/HEAD
            HttpUtils.SendBody(_webRequest, _body);
            try
            {
                // get the web response for the web request
                _webResponse = (HttpWebResponse)_webRequest.GetResponse();
                _requestHandler.Logger.Debug("downloading done: " + _webRequest.RequestUri);
                // Get response stream
                Stream responseStream = GenericWebResponse.GetResponseStream();
                return Utils.ReadStreamToEnd(responseStream);
            }
            catch (Exception e)
            {
                _requestHandler.Logger.Debug("failed: " + Uri, e);
                return null;
            }
        }

        /// <summary>
        /// Streams a request from the server into the cache.
        /// Used for both local and remote proxy requests.
        /// </summary>
        /// <returns>Length of the downloaded file.</returns>
        public long DownloadToCache(bool replace)
        {
            int bytesRead = 0;
            Byte[] readBuffer = new Byte[32];
            FileStream writeFile = null;
            long bytesDownloaded = 0;

            // create directory if it doesn't exist and delete the file so we can replace it
            if (!Utils.CreateDirectoryForFile(_cacheFileName))
            {
                return -1;
            }

            // XXX: should also check for cache expiration
            // check for 0 size file to re-download
            long fileSize = Utils.GetFileSize(_cacheFileName);
            if (fileSize > 0 && !replace)
            {
                //_requestHandler.LogDebug("exists: " + cacheFileName + " " + fileSize + " bytes");
                return fileSize;
            }
            else
            {
                if (!Utils.DeleteFile(_cacheFileName))
                {
                    return -1;
                }
            }
            try
            {
                _requestHandler.Logger.Debug("downloading: " + _webRequest.RequestUri);
                // Stream parameters, if we have non GET/HEAD
                HttpUtils.SendBody(_webRequest, _body);
                // get the web response for the web request
                _webResponse = (HttpWebResponse)_webRequest.GetResponse();
                _requestHandler.Logger.Debug("downloading done: " + _webRequest.RequestUri);
                if (!_webResponse.ResponseUri.Equals(_webRequest.RequestUri))
                {
                    // redirected at some point
                    _uriBeforeRedirect = _webRequest.RequestUri.ToString();
                    
                    // leave a 301 at the old cache file location
                    string str = "HTTP/1.1 301 Moved Permanently\r\n" +
                          "Location: " + _webResponse.ResponseUri.ToString() + "\r\n";
                    
                    using (StreamWriter sw = new StreamWriter(_cacheFileName))
                    {
                        sw.Write(str);
                    }

                    // have to save to the new cache file location
                    string uri = _webResponse.ResponseUri.ToString();
                    _fileName = UriToFilePath(uri);
                    _hashPath = GetHashPath(_fileName);
                    _cacheFileName = _requestHandler.GenericProxy.CachePath + _hashPath + _fileName;

                    // create directory if it doesn't exist and delete the file so we can replace it
                    if (!Utils.CreateDirectoryForFile(_cacheFileName))
                    {
                        return -1;
                    }

                    // XXX: should also check for cache expiration
                    // check for 0 size file to re-download
                    fileSize = Utils.GetFileSize(_cacheFileName);
                    if (fileSize > 0 && !replace)
                    {
                        return fileSize;
                    }
                    else
                    {
                        if (!Utils.DeleteFile(_cacheFileName))
                        {
                            return -1;
                        }
                    }
                }

                // XXX: Use a stream reader!?
                Stream responseStream = GenericWebResponse.GetResponseStream();

                writeFile = Utils.CreateFile(_cacheFileName);
                if (writeFile == null)
                {
                    return -1;
                }
                bytesRead = responseStream.Read(readBuffer, 0, 32);
                while (bytesRead != 0)
                {
                    // write the response to the cache
                    writeFile.Write(readBuffer, 0, bytesRead);
                    bytesDownloaded += bytesRead;

                    // Read the next part of the response
                    bytesRead = responseStream.Read(readBuffer, 0, 32);
                }
                _requestHandler.Logger.Debug("received: " + Uri + " " + bytesDownloaded + " bytes");
            }
            catch (Exception e)
            {
                // XXX: not handled well
                // timed out
                // incomplete, clean up the partial download
                Utils.DeleteFile(_cacheFileName);
                _requestHandler.Logger.Debug("failed: " + Uri, e);
                bytesDownloaded = -1; 
            }
            finally
            {
                if (writeFile != null)
                {
                    writeFile.Close();
                }
            }

            return bytesDownloaded;
        }
    }
}