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
using System.Diagnostics;

namespace RuralCafe
{
    /// <summary>
    /// A Rural Cafe request.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class RCRequest
    {
        [JsonProperty]
        private string _anchorText;
        [JsonProperty]
        private string _contentSnippet;
        [JsonProperty]
        private string _refererUri;
        // hashPath without the directory seperators
        [JsonProperty]
        private string _requestId;
        [JsonProperty]
        private string _cacheFileName;
        [JsonProperty]
        private string _relCacheFileName;
        [JsonProperty]
        private string _packageFileName;

        [JsonProperty]
        private string _uriBeforeRedirect;

        [JsonProperty]
        private RequestHandler.Status _status;
        // These two are used only on the local proxa side, for requests that have been dispatched to the remote proxy.
        [JsonProperty]
        private HttpStatusCode _statusCode;
        [JsonProperty]
        private string _errorMessage;

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
        public string PackageFileName
        {
            set { _packageFileName = value; }
            get { return _packageFileName; }
        }
        /// <summary>The itemId of the object.</summary>
        public string RequestId
        {
            get { return _requestId; }
        }
        /// <summary>The file name of the object if it is cached ,absolute.</summary>
        public string CacheFileName
        {
            get { return _cacheFileName; }
        }
        /// <summary>The file name of the object if it is cached, relative.</summary>
        public string RelCacheFileName
        {
            get { return _relCacheFileName; }
        }
        /// <summary>The status of this request.</summary>
        public RequestHandler.Status RequestStatus
        {
            set { _status = value; }
            get { return _status; }
        }
        /// <summary>The status code for dispatched requests on the LP side.</summary>
        public HttpStatusCode StatusCode
        {
            set { _statusCode = value; }
            get { return _statusCode; }
        }
        /// <summary>The error message for failed dispatched requests on the LP side.</summary>
        public string ErrorMessage
        {
            set { _errorMessage = value; }
            get { return _errorMessage; }
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

            string fileName = CacheManager.UriToFilePath(_webRequest.RequestUri.ToString());
            string hashPath = CacheManager.GetHashPath(fileName);
            // Cache file name like ./GET/2876/627/...
            _relCacheFileName = request.Method + Path.DirectorySeparatorChar + hashPath + fileName;
            _requestId = _relCacheFileName.Replace(Path.DirectorySeparatorChar.ToString(), "");
            _cacheFileName = requestHandler.GenericProxy.CachePath + _relCacheFileName;

            _packageFileName = requestHandler.GenericProxy.PackagesPath + hashPath + fileName + ".gzip";
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
            int res = Uri.GetHashCode() * _webRequest.Method.GetHashCode();

            return Body == null ? res : res * Body.GetHashCode();
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
        /// Downloads a package from the remote proxy.
        /// </summary>
        /// <returns></returns>
        public bool DownloadPackage()
        {
            _requestHandler.Logger.Debug("downloading from remote proxy: " + _webRequest.RequestUri);
            // Stream parameters, if we have non GET/HEAD
            HttpUtils.SendBody(_webRequest, _body);

            // get the web response for the web request
            try
            {
                _webResponse = (HttpWebResponse)_webRequest.GetResponse();
            }
            catch (WebException e)
            {
                _statusCode = (e.Response as HttpWebResponse).StatusCode;
                _errorMessage = new StreamReader(e.Response.GetResponseStream()).ReadToEnd();
                return false;
            }
            _requestHandler.Logger.Debug("Received header: " + _webRequest.RequestUri);
            _statusCode = _webResponse.StatusCode;

            FileStream writeFile = Utils.CreateFile(PackageFileName);
            if (writeFile == null)
            {
                _statusCode = HttpStatusCode.InternalServerError;
                _errorMessage = "Could not create package file";
                _requestHandler.Logger.Warn(_errorMessage + ": " + PackageFileName);
                return false;
            }

            Stream contentStream = _webResponse.GetResponseStream();
            Byte[] readBuffer = new Byte[4096];
            long bytesDownloaded = 0;
            using (writeFile)
            {
                // Read buffered.
                int bytesRead = contentStream.Read(readBuffer, 0, readBuffer.Length);
                while (bytesRead != 0)
                {
                    writeFile.Write(readBuffer, 0, bytesRead);
                    bytesDownloaded += bytesRead;

                    // Read the next part of the response
                    bytesRead = contentStream.Read(readBuffer, 0, readBuffer.Length);
                }
            }
            _requestHandler.Logger.Debug("received: " + _webResponse.ResponseUri + " "
                    + bytesDownloaded + " bytes.");
            // Generelly we should receive more than 0 bytes, if there was no exception.
            // We keep this anyway, in case we overlooked something.
            return bytesDownloaded > 0;
        }

        /// <summary>
        /// Streams a request from the server into the cache.
        /// Used for both local and remote proxy requests.
        /// 
        /// Replaces existing files.
        /// 
        /// Cleans up, but rethrows any exceptions. Also throws own exceptions, if something goes wrong.
        /// </summary>
        public void DownloadToCache()
        {
            CacheManager cacheManager = _requestHandler.GenericProxy.ProxyCacheManager;

            try
            {
                _requestHandler.Logger.Debug("downloading: " + _webRequest.RequestUri);

                // Stream parameters, if we have non GET/HEAD
                HttpUtils.SendBody(_webRequest, _body);

                // get the web response for the web request
                _webResponse = (HttpWebResponse)_webRequest.GetResponse();
                _requestHandler.Logger.Debug("Received header: " + _webRequest.RequestUri);

                if (!_webResponse.ResponseUri.Equals(_webRequest.RequestUri))
                {
                    // redirected at some point
                    _uriBeforeRedirect = _webRequest.RequestUri.ToString();

                    // leave a 301 at the old cache file location
                    string str = "HTTP/1.1 301 Moved Permanently\r\n" +
                          "Location: " + _webResponse.ResponseUri.ToString() + "\r\n";

                    // Write the str in a file and save also a 301 entry in the database
                    // with corresponding header.
                    cacheManager.CreateFileAndWrite(_cacheFileName, str);
                    NameValueCollection redirHeaders = new NameValueCollection()
                    {
                        { "Location", _webResponse.ResponseUri.ToString() },
                        // We need to include content-type, as we always want that header!
                        { "Content-Type", "text/plain"}
                    };
                    // We won't index redir files.
                    GlobalCacheItemToAdd newItem = new GlobalCacheItemToAdd(_relCacheFileName, redirHeaders, 301, false);

                    // Add redir file to the database
                    cacheManager.AddCacheItemsForExistingFiles(new HashSet<GlobalCacheItemToAdd>() { newItem });

                    // have to save to the new cache file location
                    string uri = _webResponse.ResponseUri.ToString();
                    _relCacheFileName = CacheManager.GetRelativeCacheFileName(uri, _webResponse.Method);
                    _cacheFileName = _requestHandler.GenericProxy.CachePath + _relCacheFileName;

                    if (cacheManager.IsCached(_relCacheFileName))
                    {
                        _requestHandler.Logger.Debug("Already exists: " +
                            _webResponse.Method + " " + uri);
                        return;
                    }
                }

                // Add stream content to the cache. 
                if (!cacheManager.AddCacheItem(GenericWebResponse, _relCacheFileName))
                {
                    // clean up the (partial) download
                    cacheManager.RemoveCacheItemFromDisk(_cacheFileName);
                    _requestHandler.Logger.Debug("failed, could not add item to the database or cache: " + Uri);
                    throw new Exception("Could not add item to the database or cache.");
                }
            }
            catch (Exception e)
            {
                // timed out or some other error
                // clean up the (partial) download
                cacheManager.RemoveCacheItemFromDisk(_cacheFileName);
                _requestHandler.Logger.Debug("failed: " + Uri, e);

                // if this is a webexception, let's throw our own exception include the Remote Proxy's message (on the local side only!):
                if (_requestHandler.GenericProxy is RCLocalProxy && e is WebException)
                {
                    WebException exp = e as WebException;
                    if (exp.Response != null)
                    {
                        throw new WebException(exp.Message + ": " + new StreamReader(exp.Response.GetResponseStream()).ReadToEnd(),
                            exp, exp.Status, exp.Response);
                    }
                }

                throw;
            }
        }
    }
}