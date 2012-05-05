﻿/*
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

namespace RuralCafe
{
    public class RCRequest
    {
        private string _uri;
        private string _refererUri;
        private string _hashedFileName;
        private string _cacheFileName;

        private int _status;
        private HttpWebRequest _webRequest;

        private HttpWebResponse _webResponse;
        private long _fileSize;

        private RequestHandler _rootRequest;
        private Dictionary<string, string> _searchFields;

        private DateTime _startTime;
        private DateTime _finishTime;

        // threading support
        private ManualResetEvent[] _resetEvents;
        private int _childNumber;

        // only used by the transparent part of the proxy
        public string _recvString;


        # region Accessors

        /// <summary>The URI of the request object.</summary>
        public string Uri
        {
            set { _uri = value; }
            get { return _uri; }
        }
        /// <summary>The URI of the referer object.</summary>
        public string RefererUri
        {
            set { _refererUri = value; }
            get { return _refererUri; }
        }
        /// <summary>The hashed file name of the object.</summary>
        public string HashedFileName
        {
            set { _hashedFileName = value; }
            get { return _hashedFileName; }
        }
        /// <summary>The file name of the object if it is cached.</summary>
        public string CacheFileName
        {
            set { _cacheFileName = value; }
            get { return _cacheFileName; }
        }
        /// <summary>The status of this request.</summary>
        public int RequestStatus
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
        /// <summary>The size of the file if it is cached.</summary>
        public long FileSize
        {
            set { _fileSize = value; }
            get { return _fileSize; }
        }
        /// <summary>The root requesthandler of this request.</summary>
        public RequestHandler RootRequest
        {
            get { return _rootRequest; }
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
        /// <param name="uri">URI requested.</param>
        public RCRequest(RequestHandler requestHandler, string uri) 
            : this(requestHandler, uri, "")
        {
            // do nothing
        }

        /// <summary>
        /// Constructor for a RuralCafe Request.
        /// </summary>
        /// <param name="request">The handler for the request.</param>
        /// <param name="uri">URI requested.</param>
        /// <param name="referrerUri">URI of the referer.</param>
        public RCRequest(RequestHandler requestHandler, string uri, string referrerUri)
        {
            _uri = uri.Trim();
            _refererUri = referrerUri.Trim();

            string fileName = UriToFilePath(_uri);
            _hashedFileName = HashedFilePath(fileName) + fileName;
            _cacheFileName = requestHandler.Proxy.CachePath + _hashedFileName;
            if (IsCompressed())
            {
                _cacheFileName = _cacheFileName + ".bz2";
            }

            _status = (int)RequestHandler.Status.Received;
            _webRequest = (HttpWebRequest)WebRequest.Create(_uri);

            _fileSize = 0;

            _rootRequest = requestHandler;

            _startTime = DateTime.Now;
            _finishTime = _startTime;
        }
        
        /// <summary>
        /// Sets the proxy to be used for this request.
        /// </summary>
        /// <param name="proxy">Proxy info.</param>
        /// <param name="timeout">Timeout.</param>
        public void SetProxy(WebProxy proxy, int timeout) {
             GenericWebRequest.Proxy = proxy;
             GenericWebRequest.Timeout = timeout;
        }

        /// <summary>
        /// Parses the RuralCafe search fields for later use.
        /// </summary>
        public void ParseRCSearchFields()
        {
            _searchFields = new Dictionary<string, string>();

            string queryString = "";
            int offset = Uri.IndexOf('?');
            if (offset >= 0)
            {
                queryString = (offset < Uri.Length - 1) ? Uri.Substring(offset + 1) : String.Empty;

                // Parse the query string variables into a NameValueCollection.
                try
                {
                    NameValueCollection qscoll = HttpUtility.ParseQueryString(queryString);

                    foreach (String key in qscoll.AllKeys)
                    {
                        if (!_searchFields.ContainsKey(key))
                        {
                            _searchFields.Add(key, qscoll[key]);
                        }
                    }
                }
                catch (Exception)
                {
                    // nothing to parse
                    return;
                }
            }
        }

        /// <summary>Override object equality for request matching.</summary>
        public override bool Equals(object obj)
        {
            if (Uri.Equals(((RCRequest)obj).Uri))
            {
                return true;
            }
            return false;
        }
        /// <summary>Override object hash code for request matching.</summary>
        public override int GetHashCode()
        {
            return Uri.GetHashCode();
        }

        /*
        /// <summary>
        /// Helper that returns the hashed file path given a file name.
        /// </summary>
        /// <param name="fileName">File name.</param>
        /// <returns>Hashed file path.</returns>
        private static string HashedFileName(string fileName)
        {
            string hashedFileName = HashedFilePath(fileName) + fileName;
            return hashedFileName;
        }
         */
        /// <summary>
        /// Actually hashes the file name to a file path.
        /// </summary>
        /// <param name="fileName">File name to hash.</param>
        /// <returns>Hashed file path.</returns>
        public static string HashedFilePath(string fileName)
        {
            fileName = fileName.Replace(Path.DirectorySeparatorChar.ToString(), ""); // for compability with linux filepath delimeter
            int value1 = 0;
            int value2 = 0;

            int hashSpace = 5000;
            int length = fileName.Length;
            if (length == 0) {
                return "0\\0\\" + fileName;
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
        /// <param name="objectNumber">The object number of this request.</param>
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

            // trim http header junk
            if (uri.StartsWith("http://"))
            {
                uri = uri.Substring(7);
            }

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
                while (safe.IndexOf("--") > -1)
                    safe = safe.Replace("--", "-");

            // trim out illegal characters

            safe = Regex.Replace(safe, "[^a-z0-9/\\-\\.]", "");

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
        /// Checks whether the request should be stored in compressed format.
        /// Synchronized with CIP implementation.
        /// XXX: Should probably remove this, it saves HD space at the cost of processing.
        /// </summary>
        /// <returns>True or false if the request should be compressed or not.</returns>
        public bool IsCompressed()
        {
            if (Uri.EndsWith(".html") ||
                Uri.EndsWith(".htm") ||
                Uri.EndsWith(".txt") ||
                Uri.EndsWith(".xml") ||
                Uri.EndsWith(".js"))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets the value for a particular RuralCafe search field.
        /// </summary>
        /// <param name="key">Key of the field.</param>
        /// <returns>The value for the field.</returns>
        public string GetRCSearchField(string key)
        {
            if (_searchFields.ContainsKey(key))
            {
                return _searchFields[key];
            }

            return "";
        }
        /// <summary>
        /// Translates a RuralCafe search to a Google one.
        /// </summary>
        /// <returns>Google search query.</returns>
        public string TranslateRCSearchToGoogle()
        {
            string searchTerms = GetRCSearchField("textfield");
            string googleWebRequestUri = "http://www.google.com/search?hl=en&q=" +
                                        searchTerms.Replace(' ', '+') +
                                        "&btnG=Google+Search&aq=f&oq=";

            return googleWebRequestUri;
        }

        /// <summary>
        /// Streams a request from the server into the cache.
        /// Used for both local and remote proxy requests.
        /// </summary>
        /// <returns>Length of the downloaded file.</returns>
        public long DownloadToCache()
        {
            int bytesRead = 0;
            Byte[] readBuffer = new Byte[32];
            FileStream writeFile = null;
            long bytesDownloaded = 0;

            // create directory if it doesn't exist and delete the file so we can replace it
            if (!Util.CreateDirectoryForFile(CacheFileName) ||
                !Util.DeleteFile(CacheFileName))
            {
                return -1;
            }

            try
            {
                writeFile = Util.CreateFile(CacheFileName);
                if (writeFile == null)
                {
                    return -1;
                }

                // get the web response for the web request
                _webResponse = (HttpWebResponse)_webRequest.GetResponse();
                Stream responseStream = GenericWebResponse.GetResponseStream();

                // Read the response into a buffer.
                bytesRead = responseStream.Read(readBuffer, 0, 32);
                while (bytesRead != 0)
                {
                    // write the response to the cache
                    writeFile.Write(readBuffer, 0, bytesRead);
                    bytesDownloaded += bytesRead;

                    // check to see if the time is up for this overall request object
                    if (_rootRequest.IsTimedOut())
                    {
                        // incomplete, clean up the partial download
                        Util.DeleteFile(CacheFileName);
                        _rootRequest.LogDebug("Failed, timed out: " + Uri);
                        return -1;
                    }

                    // Read the next part of the response
                    bytesRead = responseStream.Read(readBuffer, 0, 32);
                }
            }
            catch (WebException e)
            {
                _rootRequest.LogDebug("WebException, streaming failed: " + e.Message);
                return -1;
            }
            catch (Exception e)
            {
                // XXX: not handled well
                _rootRequest.LogDebug("Exception, Stream from server to cache failed: " + e.Message);
                return -1;
            }
            finally
            {
                if (writeFile != null)
                {
                    writeFile.Close();
                }
            }

            _rootRequest.LogDebug("received: " + Uri + " " + bytesDownloaded + " bytes");
            return bytesDownloaded;
        }
    }
}