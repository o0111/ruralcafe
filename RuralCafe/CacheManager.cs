using RuralCafe.Clusters;
using RuralCafe.Database;
using RuralCafe.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Collections.Specialized;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using RuralCafe.Json;
using System.Threading;
using System.Reflection;
using RuralCafe.Lucenenet;
using System.Collections.Concurrent;

namespace RuralCafe
{
    public class GlobalCacheItemToAdd
    {
        public string url;
        public string httpMethod;
        public NameValueCollection headers;
        public short statusCode;

        public override bool Equals(object obj)
        {
            if(!(obj is GlobalCacheItemToAdd) || obj == null)
            {
                return false;
            }
            GlobalCacheItemToAdd other = obj as GlobalCacheItemToAdd;

            return httpMethod.ToLower().Equals(other.httpMethod.ToLower()) &&
                url.ToLower().Equals(other.url.ToLower());
        }

        public override int GetHashCode()
        {
            return httpMethod.ToLower().GetHashCode() * url.ToLower().GetHashCode();
        }
    }

    /// <summary>
    /// Class that manages the cache.
    /// 
    /// Methods to add items should be shifted to this class,
    /// eviction should be implemented here.
    /// 
    /// Methods to analyse the cache should also be implemented here.
    /// </summary>
    public class CacheManager
    {
        // Constants
        private const string DOC_FILE_NAME = "docfile.txt";
        private const string MAT_FILE_NAME = "cache.mat";
        private const string CLUSTERS_FILE_NAME = "clusters";
        private const string TREE_FILE_NAME = "tree";
        public const string CLUSTERS_BT_XML_FILE_NAME = "clustersBT.xml";
        public const string CLUSTERS_XML_FILE_NAME = "clusters.xml";

        private const string DATABASE_FILE_NAME = "RCDatabase.sdf";
        private const int DATABASE_MAX_SIZE_MB = 4000;
        private const int DATABASE_BUFFER_MAX_SIZE_KB = 8192;
        private const int DATABASE_BULK_INSERT_THRESHOLD = 100;
        private const double CACHE_EVICTION_PERCENT = 0.05;
        private readonly string EMPTY_DATABASE_FILE_NAME = Directory.GetCurrentDirectory()
            + Path.DirectorySeparatorChar + "Database" + Path.DirectorySeparatorChar + DATABASE_FILE_NAME;
        // Adapt this if the Database schema changes
        private readonly Dictionary<string, string[]> DB_SCHEMA = new Dictionary<string, string[]>() 
            { 
                { "GlobalCacheItem", new string[] { "httpMethod", "url", "responseHeaders", "filename", "statusCode", "filesize" } },
                { "GlobalCacheRCData",  new string[] { "httpMethod", "url", "downloadTime", "lastRequestTime", "numberOfRequests" } },
                { "UserCacheDomain",  new string[] { "userID", "domain" } },
                { "UserCacheItem",  new string[] { "httpMethod", "url", "responseHeaders", "filename", "statusCode", "userID", "domain" } }
            };

        // Regex's for safe URI replacements
        private static readonly Regex unsafeChars1 = new Regex(@"[^a-z0-9\\\-\.]");
        private static readonly Regex unsafeChars2 = new Regex(@"[^a-z0-9/\-\.]");

        /// <summary>
        /// The path to the cache.
        /// </summary>
        private string _cachePath;

        /// <summary>
        /// The path to the clusters.
        /// </summary>
        private string _clustersPath;

        /// <summary>
        /// The maximum size of the cache in bytes.
        /// </summary>
        private long _maxCacheSize;

        /// <summary>
        /// The proxy.
        /// </summary>
        private RCProxy _proxy;

        /// <summary>
        /// The writes currently being executed
        /// </summary>
        private HashSet<string> _writesExecuted = new HashSet<string>();
        /// <summary>
        /// An event to wake up threads waiting to obtain their write lock.
        /// </summary>
        private ManualResetEvent _writesEvent = new ManualResetEvent(false);

        /// <summary>
        /// The path to the cache.
        /// </summary>
        public string CachePath
        {
            get { return _cachePath; }
        }
        /// <summary>
        /// The path to the cache.
        /// </summary>
        public string ClustersPath
        {
            get { return _clustersPath; }
        }

        /// <summary>
        /// Creates a new CacheManager without a clusters path.
        /// </summary>
        /// <param name="cachePath">The path to the cache.</param>
        /// <param name="proxy">The proxy.</param>
        /// <param name="maxCacheSize">The maximum cache size in bytes.</param>
        public CacheManager(RCProxy proxy, long maxCacheSize, string cachePath)
        {
            this._proxy = proxy;
            this._maxCacheSize = maxCacheSize;
            this._cachePath = cachePath;
        }

        /// <summary>
        /// Creates a new CacheManager with a clusters path.
        /// </summary>
        /// <param name="cachePath">The path to the cache.</param>
        /// <param name="clustersPath">The path to the clusters</param>
        /// <param name="proxy">The proxy.</param>
        /// <param name="maxCacheSize">The maximum cache size in bytes.</param>
        public CacheManager(RCProxy proxy, long maxCacheSize, string cachePath, string clustersPath)
            : this(proxy, maxCacheSize, cachePath)
        {
            this._clustersPath = clustersPath;
        }

        #region (static) filepath methods

        /// <summary>
        /// Gets the HTTP method from a relative file name. This is just the name of the first folder.
        /// </summary>
        /// <param name="relFileName">The relative file name.</param>
        /// <returns>The HTTP method.</returns>
        public static string GetHTTPMethodFromRelCacheFileName(string relFileName)
        {
            return relFileName.Substring(0, relFileName.IndexOf(Path.DirectorySeparatorChar));
        }

        /// <summary>
        /// Gets the path of a filename from an URI. Relative to the cache path.
        /// </summary>
        /// <param name="uri">The URI.</param>
        /// <param name="httpMethod">The HTTP method.</param>
        /// <returns>Retalive cache file name.</returns>
        public static string GetRelativeCacheFileName(string uri, string httpMethod)
        {
            string fileName = UriToFilePath(uri);
            string hashPath = GetHashPath(fileName);
            return httpMethod + Path.DirectorySeparatorChar + hashPath + fileName;
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
            if (length == 0)
            {
                return "0" + Path.DirectorySeparatorChar.ToString() + "0"
                    + Path.DirectorySeparatorChar.ToString() + fileName;
            }

            value1 = HashString(fileName.Substring(length / 2));
            value2 = HashString(fileName.Substring(0, length / 2));

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

            string hashedPath = value1.ToString() + Path.DirectorySeparatorChar.ToString()
                + value2.ToString() + Path.DirectorySeparatorChar.ToString();
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
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                value = (1000003 * value) ^ (int)c;
            }
            value = value ^ s.Length;
            if (value == -1)
            {
                value = -2;
            }
            return value;
        }

        /// <summary>
        /// Converts an absolute file name of a file in this cache to an URI.
        /// </summary>
        /// <param name="absfilepath">The absolute path.</param>
        /// <returns>The URI.</returns>
        public string AbsoluteFilePathToUri(string absfilepath)
        {
            // We assume the path to be valid and to be in _cachePath
            // No bounds chechking, etc.
            return FilePathToUri(absfilepath.Substring(_cachePath.Length));
        }

        /// <summary>
        /// Determines the URI for the cache file path.
        /// </summary>
        /// <param name="relfilepath">The path relaive to the cache path.</param>
        /// <returns>The URI.</returns>
        public static string FilePathToUri(string relfilepath)
        {
            string uri = relfilepath;
            // Remove the httpMethod dir and the 2 hash dirs from path
            for (int i = 0; i < 3; i++)
            {
                int startIndex = uri.IndexOf(Path.DirectorySeparatorChar);
                if (startIndex != -1)
                {
                    uri = uri.Substring(startIndex + 1);
                }
            }

            // replace possible backslahes with shlashes
            uri = uri.Replace(Path.DirectorySeparatorChar, '/');
            // Remove possible index.html at the end
            string indexhtml = "index.html";
            if (uri.EndsWith("/" + indexhtml))
            {
                uri = uri.Substring(0, uri.Length - indexhtml.Length);
            }
            // Add http://
            uri = HttpUtils.AddHttpPrefix(uri);
            return uri;
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
        /// Private helper for UriToFilePath.
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

        #endregion
        #region cache interface

        /// <summary>
        /// Initializes the cache by making sure that the directory/ies exist(s)
        /// and initializing the database.
        /// </summary>
        /// <returns>True or false for success or not.</returns>
        public bool InitializeCache()
        {
            try
            {
                if (!Directory.Exists(_cachePath))
                {
                    System.IO.Directory.CreateDirectory(_cachePath);
                }
                if (!String.IsNullOrEmpty(_clustersPath) && !Directory.Exists(_clustersPath))
                {
                    System.IO.Directory.CreateDirectory(_clustersPath);
                }
            }
            catch (Exception)
            {
                return false;
            }
            return InitializeDatabase();
        }

        /// <summary>
        /// Logs the cache metrics.
        /// </summary>
        public void LogCacheMetrics()
        {
            _proxy.Logger.Metric("Cache Items: " + AllFiles().Count);
            _proxy.Logger.Metric("Cache Items with text/html mimetype: " + TextFiles().Count);
        }

        /// <summary>
        /// Checks whether an item is cached. Only the DB is being used,
        /// the disk contens are not ebing looked at.
        /// </summary>
        /// <param name="request">The web request.</param>
        /// <returns>If the item is cached.</returns>
        public bool IsCached(HttpWebRequest request)
        {
            return IsCached(request.Method, request.RequestUri.ToString());
        }

        /// <summary>
        /// Checks whether an item is cached. Only the DB is being used,
        /// the disk contens are not ebing looked at.
        /// </summary>
        /// <param name="response">The web response.</param>
        /// <returns>If the item is cached.</returns>
        public bool IsCached(HttpWebResponse response)
        {
            return IsCached(response.Method, response.ResponseUri.ToString());
        }

        /// <summary>
        /// Checks whether an item is cached. Only the DB is being used,
        /// the disk contens are not ebing looked at.
        /// </summary>
        /// <param name="httpMethod">The used HTTP method.</param>
        /// <param name="uri">The URI.</param>
        /// <returns>If the item is cached.</returns>
        public bool IsCached(string httpMethod, string uri)
        {
            using (RCDatabaseEntities databaseContext = GetNewDatabaseContext())
            {
                return IsCached(httpMethod, uri, databaseContext);
            }
        }

        /// <summary>
        /// Checks if the cache item is a text file.
        /// </summary>
        /// <param name="httpMethod">The HTTP method</param>
        /// <param name="uri">The URI.</param>
        /// <returns>True, if the content-type is "text/html" and false otherwise or if there is no such cache item.</returns>
        public bool IsHTMLFile(string httpMethod, string uri)
        {
            using (RCDatabaseEntities databaseContext = GetNewDatabaseContext())
            {
                // Be sure to call the private method! Otherwise this would count as a request.
                GlobalCacheItem gci = GetGlobalCacheItem(httpMethod, uri, databaseContext);
                if (gci == null)
                {
                    // This hould not happen
                    return false;
                }
                return gci.responseHeaders.Contains("\"Content-Type\":[\"text/html");
            }
        }

        /// <summary>
        /// Gets the global cache item for the specified HTTP method and URI, if it exists,
        /// and null otherwise.
        /// 
        /// This does not count as a request and the last request time and number of requests are
        /// not modified!
        /// </summary>
        /// <param name="httpMethod">The HTTP method.</param>
        /// <param name="uri">The URI.</param>
        /// <returns>The global cache item or null.</returns>
        public GlobalCacheItem GetGlobalCacheItem(string httpMethod, string uri)
        {
            using (RCDatabaseEntities databaseContext = GetNewDatabaseContext())
            {
                return GetGlobalCacheItem(httpMethod, uri, databaseContext);
            }
        }

        /// <summary>
        /// Gets the global cache item for the specified HTTP method and URI, if it exists,
        /// and null otherwise.
        /// 
        /// Also, the lastRequestTime is set to now and the number of requests is incremented.
        /// </summary>
        /// <param name="httpMethod">The HTTP method.</param>
        /// <param name="uri">The URI.</param>
        /// <returns>The global cache item or null.</returns>
        public GlobalCacheItem GetGlobalCacheItemAsRequest(string httpMethod, string uri)
        {
            GetLockFor(httpMethod, uri);
            try
            {
                using (RCDatabaseEntities databaseContext = GetNewDatabaseContext())
                {
                    GlobalCacheItem result = GetGlobalCacheItem(httpMethod, uri, databaseContext);
                    if (result != null)
                    {
                        _proxy.Logger.Debug(String.Format("Updating request time and number of requests of {0} {1}",
                            httpMethod, uri));
                        // Modify the RC data and save
                        result.GlobalCacheRCData.lastRequestTime = DateTime.Now;
                        result.GlobalCacheRCData.numberOfRequests++;
                        databaseContext.SaveChanges();
                    }
                    return result;
                }
            }
            finally
            {
                ReleaseLockFor(httpMethod, uri);
            }
        }

        /// <summary>
        /// Gets the global cache RC data item for the specified HTTP method and URI, if it exists,
        /// and null otherwise.
        /// </summary>
        /// <param name="httpMethod">The HTTP method.</param>
        /// <param name="uri">The URI.</param>
        /// <returns>The global cache RC data item or null.</returns>
        public GlobalCacheRCData GetGlobalCacheRCData(string httpMethod, string uri)
        {
            using (RCDatabaseEntities databaseContext = GetNewDatabaseContext())
            {
                return GetGlobalCacheRCData(httpMethod, uri, databaseContext);
            }
        }

        /// <summary>
        /// Adds cache items to the database. The files should already exist. Existing items
        /// will be replaced, and eviction will be done if necessary.
        /// </summary>
        /// <param name="items">The items to add. We use a set, so we can't have duplicates.</param>
        /// <returns>True for success and false for failure.</returns>
        public bool AddCacheItemsForExistingFiles(HashSet<GlobalCacheItemToAdd> items)
        {
            bool returnValue = true;
            using (RCDatabaseEntities databaseContext = GetNewDatabaseContext())
            {
                // When we add, we potentially also evict.
                // We need to make sure no other additions take place at the same time.
                GetLockFor("ADD", "");
                GetLocksFor(items);
                try
                {
                    foreach (GlobalCacheItemToAdd item in items)
                    {
                        if (!AddCacheItemForExistingFile(item.url, item.httpMethod, item.headers, item.statusCode, databaseContext))
                        {
                            returnValue = false;
                            break;
                        }
                    }
                    databaseContext.SaveChanges();
                }
                catch (Exception e)
                {
                    _proxy.Logger.Warn("Could not add cache items to the database.", e);
                    returnValue = false;
                }
                finally
                {
                    ReleaseLocksFor(items);
                    ReleaseLockFor("ADD", "");
                }
            }
            return returnValue;
        }

        /// <summary>
        /// Adds a cache item to the database. The file is assumed to exist already. If that item exists
        /// already in the DB, it will be replaced with the new headers and statusCode, and the RC data will
        /// be updated.
        /// 
        /// If the item did not exist and it does not fit in the cache, other items will be evicted.
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <param name="httpMethod">The HTTP Method.</param>
        /// <param name="headers">The headers.</param>
        /// <param name="statusCode">The status code.</param>
        /// <returns>True for success and false for failure.</returns>
        private bool AddCacheItemForExistingFile(string url, string httpMethod,
            NameValueCollection headers, short statusCode, RCDatabaseEntities databaseContext)
        {
            string relFileName = GetRelativeCacheFileName(url, httpMethod);

            // If the headers do not contain "Content-Type", which should practically not happen,
            // (but servers are actually not required to send it) we set it to the default:
            // "application/octet-stream"
            if (headers["Content-Type"] == null)
            {
                headers["Content-Type"] = "application/octet-stream";
            }

            long cacheSize, itemSize;
            // Get the cache and the file size
            try
            {
                cacheSize = CacheSize(databaseContext);
                itemSize = CacheItemFileSize(relFileName);
            }
            catch (Exception e)
            {
                _proxy.Logger.Warn("Could not compute cache or file size.", e);
                return false;
            }

            GlobalCacheItem existingCacheItem = GetGlobalCacheItem(httpMethod, url, databaseContext);

            // Look if we have to evict cache items first.
            long cacheOversize = cacheSize + itemSize - _maxCacheSize;
            if (existingCacheItem != null)
            {
                cacheOversize -= -existingCacheItem.filesize;
            }
            if (cacheOversize > 0)
            {
                try
                {
                    // We have to evict. We do until 5 % is free again.
                    EvictCacheItems((long)(cacheOversize + CACHE_EVICTION_PERCENT * _maxCacheSize), databaseContext);
                }
                catch (Exception e)
                {
                    _proxy.Logger.Warn("Could not evict cache items.", e);
                    return false;
                }
            }

            if (existingCacheItem == null)
            {
                // Add database entry.
                try
                {
                    AddCacheItemToDatabase(url, httpMethod, headers, statusCode,
                        relFileName, itemSize, databaseContext);
                }
                catch (Exception e)
                {
                    _proxy.Logger.Warn("Could not add cache item to the database.", e);
                    return false;
                }
            }
            else
            {
                // Update database entry.
                try
                {
                    UpdateCacheItemInDatabase(existingCacheItem, headers, statusCode,
                        relFileName, itemSize, databaseContext);
                }
                catch (Exception e)
                {
                    _proxy.Logger.Warn("Could not add cache item to the database.", e);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Adds a cache item, also replacing if there is already one.
        /// </summary>
        /// <param name="webResponse">The web response.</param>
        /// <returns>True for success and false for failure.</returns>
        public bool AddCacheItem(HttpWebResponse webResponse)
        {
            GlobalCacheItemToAdd newItem = new GlobalCacheItemToAdd();

            newItem.url = webResponse.ResponseUri.ToString();
            newItem.httpMethod = webResponse.Method;
            newItem.headers = webResponse.Headers;
            newItem.statusCode = (short)webResponse.StatusCode;


            // Add file to the disk
            if (!AddCacheItemToDisk(webResponse))
            {
                return false;
            }
            // Add file to the database (using the method for multiple files that has a try-catch
            // and also saves the database.
            return AddCacheItemsForExistingFiles(new HashSet<GlobalCacheItemToAdd>() { newItem });
        }

        /// <summary>
        /// Removes a file from the cache.
        /// </summary>
        /// <param name="fileName">The absolute filename of the cache item.</param>
        /// <returns>True or false for success or failure.</returns>
        public bool RemoveCacheItemFromDisk(string fileName)
        {
            GetLockFor(fileName);
            try
            {
                return Utils.DeleteFile(fileName);
            }
            finally
            {
                ReleaseLockFor(fileName);
            }
        }

        /// <summary>
        /// Adds a file and writes content into it.
        /// 
        /// This method should be used, when a file a created prior to adding it to the
        /// database, e.g. for 301s or when Unpacking. Then, AddCacheItemForExistingFiles should
        /// be called later.
        /// 
        /// When a WebResponse for the content is available, this method should not be used!
        /// </summary>
        /// <param name="fileName">The absolute filename of the cache item.</param>
        /// <param name="content">The content to store.</param>
        public void CreateFileAndWrite(string fileName, string content)
        {
            GetLockFor(fileName);
            try
            {
                Utils.CreateDirectoryForFile(fileName);
                using (StreamWriter sw = new StreamWriter(File.Open(fileName, FileMode.Create), Encoding.UTF8))
                {
                    sw.Write(content);
                }
            }
            finally
            {
                ReleaseLockFor(fileName);
            }
        }

        /// <summary>
        /// Used for unpacking. Creates the file for the given HTTP method and URL (or overrides it),
        /// streaming bytesToRead bytes form the readStream into it.
        /// 
        /// Only throws an exception if readStream does not provide enough bytes.
        /// </summary>
        /// <param name="httpMethod">The HTTP method</param>
        /// <param name="uri">The URL.</param>
        /// <param name="bytesToRead">The amount of bytes to read from readStream.</param>
        /// <param name="readStream">The stream of data.</param>
        /// <returns>True for success and false for failure. In any case readStream's position
        /// will advance by bytesToRead bytes.</returns>
        public bool CreateOrUpdateFileAndWrite(string httpMethod, string uri, long bytesToRead, FileStream readStream)
        {
            long bytesRead = 0;
            byte[] buffer = new byte[1024];
            FileStream writeStream = null;

            GetLockFor(httpMethod, uri);
            try
            {
                try
                {
                    string cacheFileName = _cachePath +
                           CacheManager.GetRelativeCacheFileName(uri, httpMethod);
                    if (!Utils.IsNotTooLongFileName(cacheFileName))
                    {
                        // We can't save the file
                        _proxy.Logger.Warn("problem creating file, filename too long for uri: " + uri);
                        return false;
                    }
                    if (IsCached(httpMethod, uri))
                    {
                        // We override the file, if it exists
                        Utils.DeleteFile(cacheFileName);
                    }
                    // (re)create the file
                    writeStream = Utils.CreateFile(cacheFileName);
                    if (writeStream == null)
                    {
                        _proxy.Logger.Warn("problem creating file for uri: " + uri);
                        return false;
                    }
                    return true;
                }
                finally
                {
                    // This is in a finally clause, so we read the bytes, even if there was an error.
                    // Then for the next file, the stream will be at the right position.
                    while (bytesRead < bytesToRead)
                    {
                        // is always fine to convert to int.
                        int bytesToReadNow = (int)Math.Min(buffer.Length, bytesToRead - bytesRead);
                        int bytesReadNow = readStream.Read(buffer, 0, bytesToReadNow);
                        if (bytesReadNow == 0)
                        {
                            throw new Exception("Ran out of bytes to read for" + uri);
                        }

                        if (writeStream != null)
                        {
                            writeStream.Write(buffer, 0, bytesReadNow);
                        }
                        bytesRead += bytesReadNow;
                    }
                    if (writeStream != null)
                    {
                        writeStream.Close();
                    }
                }
            }
            finally
            {
                ReleaseLockFor(httpMethod, uri);
            }
        }

        #endregion
        #region private cache file methods

        /// <summary>
        /// Gets the file size of a cache item.
        /// 
        /// Throws an exception, if the file does not exist.
        /// </summary>
        /// <param name="relName">The relative cache file name.</param>
        /// <returns>The file size in bytes.</returns>
        private long CacheItemFileSize(string relName)
        {
            long result = Utils.GetFileSize(_cachePath + relName);
            if (result == -1)
            {
                throw new Exception("File does not exist.");
            }
            return result;
        }

        /// <summary>
        /// Adds a cache item to the disk from a web response's content.
        /// </summary>
        /// <param name="webResponse">The web response</param>
        /// <returns>True for success, false for failure.</returns>
        private bool AddCacheItemToDisk(HttpWebResponse webResponse)
        {
            GetLockFor(webResponse.Method, webResponse.ResponseUri.ToString());
            try
            {
                string fileName = _cachePath + GetRelativeCacheFileName(webResponse.ResponseUri.ToString(),
                    webResponse.Method);

                FileStream writeFile = Utils.CreateFile(fileName);
                if (writeFile == null)
                {
                    _proxy.Logger.Error("Could not create file: " + fileName);
                    return false;
                }

                Stream contentStream = webResponse.GetResponseStream();
                Byte[] readBuffer = new Byte[4096];
                long bytesDownloaded = 0;
                using (writeFile)
                {
                    if (webResponse.ContentType.Contains("text"))
                    {
                        //Text response, encode UTF-8, trim
                        using (StreamWriter writer = new StreamWriter(writeFile, Encoding.UTF8))
                        using (StreamReader reader = new StreamReader(contentStream))
                        {
                            // This can be -1, if header is missing
                            bytesDownloaded = webResponse.ContentLength;
                            string content = reader.ReadToEnd();

                            if (bytesDownloaded <= 0)
                            {
                                // XXX: for GZIP this will be wrong.
                                bytesDownloaded = content.Length;
                            }
                            writer.Write(content.Trim());
                        }
                    }
                    else
                    {
                        // No text. Read buffered.
                        int bytesRead = contentStream.Read(readBuffer, 0, readBuffer.Length);
                        while (bytesRead != 0)
                        {
                            writeFile.Write(readBuffer, 0, bytesRead);
                            bytesDownloaded += bytesRead;

                            // Read the next part of the response
                            bytesRead = contentStream.Read(readBuffer, 0, readBuffer.Length);
                        }
                    }
                }
                _proxy.Logger.Debug("received: " + webResponse.ResponseUri + " "
                        + bytesDownloaded + " bytes.");
                return true;
            }
            finally
            {
                ReleaseLockFor(webResponse.Method, webResponse.ResponseUri.ToString());
            }
        }

        #endregion
        #region private cache database methods

        /// <summary>
        /// Gets a new database context. This context must not be shared among threads!
        /// 
        /// The context will never detect changes (which improves performance), as we have
        /// our own synchronization.
        /// </summary>
        /// <returns>A new database context.</returns>
        private RCDatabaseEntities GetNewDatabaseContext()
        {
            // Create context and modify connection string to point to our DB file.
            RCDatabaseEntities result = new RCDatabaseEntities();
            result.Database.Connection.ConnectionString =
                String.Format("data source=\"{0}\";Max Database Size={1};Max Buffer Size={2}",
                _proxy.ProxyPath + DATABASE_FILE_NAME, DATABASE_MAX_SIZE_MB, DATABASE_BUFFER_MAX_SIZE_KB);
            result.Configuration.AutoDetectChangesEnabled = false;
            result.Configuration.ValidateOnSaveEnabled = false;
            return result;
        }

        /// <summary>
        /// Tries to create a new database by copying the empty one and filling it from the cache.
        /// Any exceptions are forewarded.
        /// </summary>
        private void CreateNewDatabase()
        {
            string dbFile = _proxy.ProxyPath + DATABASE_FILE_NAME;
            _proxy.Logger.Info("Creating a new database file. This may take up to several hours!");
            File.Copy(EMPTY_DATABASE_FILE_NAME, dbFile);
            // We must fill the database with the current cache content!
            FillDatabaseFromCache();
        }

        /// <summary>
        /// Checks if there is a database and creates one if there isn't.
        /// 
        /// If there was none, the database will be filled with the current cache content.
        /// </summary>
        /// <returns>True if successful, false otherwise.</returns>
        private bool InitializeDatabase()
        {
            string dbFile = _proxy.ProxyPath + DATABASE_FILE_NAME;
            if (!File.Exists(dbFile))
            {
                _proxy.Logger.Info("No database file found.");
                try
                {
                    CreateNewDatabase();
                    return true;
                }
                catch (Exception e)
                {
                    _proxy.Logger.Error("Could not create database file. Deleting what we got so far.", e);
                    try
                    {
                        File.Delete(dbFile);
                    }
                    catch (Exception) { }
                    return false;
                }
            }

            // If it exists, we must test whether it really contains a valid DB
            bool validDB;
            try
            {
                validDB = CheckDatabaseIntegrityAndRepair();
            }
            catch (Exception e)
            {
                _proxy.Logger.Warn("Error checking database integrity: ", e);
                try
                {
                    CreateNewDatabase();
                    return true;
                }
                catch (Exception e1)
                {
                    _proxy.Logger.Error("Could not create database file. Deleting what we got so far.", e1);
                    try
                    {
                        File.Delete(dbFile);
                    }
                    catch (Exception) { }
                    return false;
                }
            }

            if (!validDB)
            {
                // The database is invalid and we must create a new one
                try
                {
                    File.Delete(dbFile);
                    CreateNewDatabase();
                }
                catch (Exception e)
                {
                    _proxy.Logger.Error("Could not create database file. Deleting what we got so far.", e);
                    try
                    {
                        File.Delete(dbFile);
                    }
                    catch (Exception) { }
                    return false;
                }

            }
            return true;
        }

        /// <summary>
        /// Tests if the existing database file is valid, that means contains the tables and rows we need.
        /// 
        /// Repairs the database if there are columns missing. If that is not possible, false is returned.
        /// </summary>
        /// <returns>True for a valid DB file, false otherwise.</returns>
        private bool CheckDatabaseIntegrityAndRepair()
        {
            using (RCDatabaseEntities databaseContext = GetNewDatabaseContext())
            {
                // See if all tables exist
                IEnumerable<string> tableNames = databaseContext.Database.SqlQuery<string>(
                    "SELECT table_name FROM information_schema.tables WHERE table_type <> 'VIEW'");
                foreach (string tableName in DB_SCHEMA.Keys)
                {
                    if (!tableNames.Contains(tableName))
                    {
                        _proxy.Logger.Warn(tableName + " table is missing in the database. Will create a new database.");
                        return false;
                    }
                    // See if all columns exist
                    IEnumerable<string> colNames = databaseContext.Database.SqlQuery<string>(
                        String.Format("SELECT column_name FROM information_schema.columns WHERE table_name='{0}'",
                        tableName));
                    foreach (string colName in DB_SCHEMA[tableName])
                    {
                        if (!colNames.Contains(colName))
                        {
                            // XXX: If we add columns later, we might want to do an
                            // ALTER TABLE here, instead of returning false.
                            // Be aware that sqlite is different in regards of adding columns!
                            _proxy.Logger.Warn(tableName + " table is missing column " +
                                colName + " in the database. Will create a new database.");
                            return false;
                        }
                    }
                }
                return true;
            }
        }

        /// <summary>
        /// Fills the database from the disk contents in the cache.
        /// Of course, entries will be incomplete, (headers missing, etc.)
        /// 
        /// TODO rewrite this using threads, and using GetLockFor and ReleaseLockFor
        /// Locking not necessary, if we figure out where the dups come from.
        /// 
        /// Exceptions will be forwarded.
        /// </summary>
        private void FillDatabaseFromCache()
        {
            long currentCacheSize = 0;
            // We loop through each sub-sub-directory, so we don't have all fileInfos in
            // memory at a time
            DirectoryInfo cacheDirInfo = new DirectoryInfo(_cachePath);
            RCDatabaseEntities databaseContext = GetNewDatabaseContext();
            int counter = 0;

            foreach (DirectoryInfo subDirInfo in cacheDirInfo.EnumerateDirectories())
            {
                foreach (DirectoryInfo subsubDirInfo in subDirInfo.EnumerateDirectories())
                {
                    List<FileInfo> files = subsubDirInfo.EnumerateFiles("*", SearchOption.AllDirectories).ToList();
                    if (files.Count == 0)
                    {
                        continue;
                    }

                    currentCacheSize += files.Sum(file => file.Length);
                    if (currentCacheSize >= _maxCacheSize)
                    {
                        // We could find that out very late potentially, which is not so nice.
                        _proxy.Logger.Error(String.Format("The existing cache is more than {0} bytes in size, " +
                            "which is bigger than {1}, the max size allowed.", currentCacheSize, _maxCacheSize));
                        throw new Exception("Existing cache too big.");
                    }
                    
                    // Iterate through all files and fill the database
                    foreach (FileInfo fileInfo in files)
                    {
                        string fileName;
                        try
                        {
                            fileName = fileInfo.FullName;
                        }
                        catch (PathTooLongException)
                        {
                            // The file actually exists, but the path is too long.
                            // To have our database consistent, we add it anyway. We have to find out
                            // the path via reflection.
                            fileName = (string)typeof(FileInfo).GetField(
                                                "FullPath",
                                                 BindingFlags.Instance |
                                                 BindingFlags.NonPublic).GetValue(fileInfo);
                            _proxy.Logger.Warn(String.Format("Filename too long: {0}", fileName));
                        }
                        
                        
                        // We cannot recover the headers, but we look at the file to determine its content-type,
                        // as we always want this header!
                        NameValueCollection headers = new NameValueCollection()
                        {
                            { "Content-Type", Utils.GetContentTypeOfFile(fileName)}
                        };
                        // We assume it was a request with a 200 OK answer.
                        short statusCode = 200;
                        string uri = AbsoluteFilePathToUri(fileName);
                        string relFileName = fileName.Substring(_cachePath.Length);
                        string httpMethod = GetHTTPMethodFromRelCacheFileName(relFileName);
                        

                        GlobalCacheItem existingCacheItem = GetGlobalCacheItem(httpMethod, uri, databaseContext);
                        if (existingCacheItem == null)
                        {
                            // Add database entry.
                            AddCacheItemToDatabase(uri, httpMethod, headers, statusCode, relFileName, fileInfo.Length, databaseContext);
                        }
                        else
                        {
                            // Update database entry.
                            _proxy.Logger.Warn(String.Format("Duplicate entry in database: {0} {1}\nOld file: {2}\nNew file: {3}",
                                httpMethod, uri, existingCacheItem.filename, relFileName));
                            UpdateCacheItemInDatabase(existingCacheItem, headers, statusCode,
                                    relFileName, fileInfo.Length, databaseContext);
                        }

                        // To gain a better performace, we save and recreate the context after 100 inserts.
                        counter++;
                        if (counter == DATABASE_BULK_INSERT_THRESHOLD)
                        {
                            counter = 0;
                            _proxy.Logger.Info(DATABASE_BULK_INSERT_THRESHOLD + " new files added. Saving database changes made so far.");
                            databaseContext.SaveChanges();
                            databaseContext.Dispose();
                            databaseContext = GetNewDatabaseContext();
                        }
                         
                    }
                }
            }
            
            // Save
            _proxy.Logger.Info("Saving completed database changes.");
            databaseContext.SaveChanges();
            databaseContext.Dispose();
        }

        /// <summary>
        /// Updates a cache item in the database. Throws an exception if anything goes wrong.
        /// 
        /// databaseContext.SaveChanges() still needs to be called.
        /// </summary>
        /// <param name="existingCacheItem">The existing global cache item.</param>
        /// <param name="headers">the new headers</param>
        /// <param name="statusCode">The new status code.</param>
        /// <param name="relFileName">The relative file name.</param>
        /// <param name="fileSize">The file size.</param>
        /// <param name="databaseContext">The database context.</param>
        private void UpdateCacheItemInDatabase(GlobalCacheItem existingCacheItem, NameValueCollection headers,
            short statusCode, string relFileName, long fileSize, RCDatabaseEntities databaseContext)
        {
            string headersJson = JsonConvert.SerializeObject(headers,
                Formatting.None, new NameValueCollectionConverter());

            _proxy.Logger.Debug("Updating in database: " + existingCacheItem.httpMethod + " "
                    + existingCacheItem.url);

            // Update non-RC data
            existingCacheItem.responseHeaders = headersJson;
            existingCacheItem.statusCode = statusCode;
            existingCacheItem.filesize = fileSize;

            // Update RC data
            GlobalCacheRCData rcData = existingCacheItem.GlobalCacheRCData;
            // Although this is not really a request, we set the lastRequestTime to now
            rcData.lastRequestTime = DateTime.Now;
            // One request more
            rcData.numberOfRequests++;
            // Download time is the lastModified time of the file, if it already exists. Otherwise now
            rcData.downloadTime = File.Exists(_cachePath + relFileName) ?
                File.GetLastWriteTime(_cachePath + relFileName) : DateTime.Now;
        }

        /// <summary>
        /// Adds a new cache item to the database. Throws an exception if anything goes wrong.
        /// 
        /// databaseContext.SaveChanges() still needs to be called.
        /// </summary>
        /// <param name="url">The url.</param>
        /// <param name="httpMethod">The HTTP method.</param>
        /// <param name="headers">The headers.</param>
        /// <param name="statusCode">The status code.</param>
        /// <param name="relFileName">The relative file name.</param>
        /// <param name="fileSize">The file size.</param>
        /// <param name="databaseContext">The database context.</param>
        private void AddCacheItemToDatabase(string url, string httpMethod,
            NameValueCollection headers, short statusCode, string relFileName, long fileSize,
            RCDatabaseEntities databaseContext)
        {
            // We disable cookies for non-streamed requests
            headers.Remove("Set-Cookie");

            string headersJson = JsonConvert.SerializeObject(headers,
                Formatting.None, new NameValueCollectionConverter());

            _proxy.Logger.Debug("Adding to database: " + httpMethod + " "
                    + url);

            // Check if the RC data still exists (this means the file has been cached previsouly and was evicted)
            GlobalCacheRCData rcData = GetGlobalCacheRCData(httpMethod, url, databaseContext);
            if (rcData == null)
            {
                // create a new rc data item
                rcData = new GlobalCacheRCData();
                // Save the rc values
                rcData.url = url;
                rcData.httpMethod = httpMethod;
                // Although this is not really a request, we set the lastRequestTime to now
                rcData.lastRequestTime = DateTime.Now;
                // No requests so far
                rcData.numberOfRequests = 0;
                // Download time is the lastModified time of the file, if it already exists. Otherwise now
                rcData.downloadTime = File.Exists(_cachePath + relFileName) ?
                    File.GetLastWriteTime(_cachePath + relFileName) : DateTime.Now;
                // add item
                databaseContext.GlobalCacheRCData.Add(rcData);
            }
            else
            {
                // Although this is not really a request, we set the lastRequestTime to now
                rcData.lastRequestTime = DateTime.Now;
                // One request more
                rcData.numberOfRequests++;
                // Download time is the lastModified time of the file, if it already exists. Otherwise now
                rcData.downloadTime = File.Exists(_cachePath + relFileName) ?
                    File.GetLastWriteTime(_cachePath + relFileName) : DateTime.Now;
            }

            // Create item and save the values.
            GlobalCacheItem cacheItem = new GlobalCacheItem();
            cacheItem.url = url;
            cacheItem.httpMethod = httpMethod;
            cacheItem.responseHeaders = headersJson;
            cacheItem.statusCode = statusCode;
            cacheItem.filename = relFileName; // TODO sth. else?
            cacheItem.filesize = fileSize;
            // add item
            databaseContext.GlobalCacheItem.Add(cacheItem);

            // If we're on the local proxy, we want to add text documents to the Lucene index.
            if (_proxy is RCLocalProxy && httpMethod.Equals("GET") &&
                (headers["Content-Type"].Contains("text/html") || headers["Content-Type"].Contains("text/plain")))
            {
                RCLocalProxy proxy = ((RCLocalProxy)_proxy);
                // The index might not have been initialized...
                if (proxy.IndexWrapper == null)
                {
                    // FIXME We should not use the Program var here.
                    // But when we're creating the DB, this gets called in the RCProxy constructor
                    // before the RCLocalProxy constructor. We should find a way to have the index created
                    // before the cache for the LP
                    proxy.IndexWrapper = new IndexWrapper(Program.INDEX_PATH);
                    // initialize the index
                    proxy.IndexWrapper.EnsureIndexExists();
                }

                // add the file to Lucene, if it is a GET text or HTML file.
                // We have made sure the content-type header is always present in the DB!

                // XXX reading the file we just wrote. Not perfect.
                string document = Utils.ReadFileAsString(_cachePath + relFileName);
                string title = HtmlUtils.GetPageTitleFromHTML(document);

                // Use whole document, so we can also find results with tags, etc.
                try
                {
                    proxy.IndexWrapper.IndexDocument(url, title, document);
                }
                catch (Exception e)
                {
                    _proxy.Logger.Warn("Could not add document to index.", e);
                }
            }
        }

        /// <summary>
        /// Gets the current cache size by summing up all file sizes.
        /// </summary>
        /// <param name="databaseContext">The database context.</param>
        /// <returns>The current cache size.</returns>
        private long CacheSize(RCDatabaseEntities databaseContext)
        {
            IQueryable<long> fileSizes = from gci in databaseContext.GlobalCacheItem select gci.filesize;
            return fileSizes.Count() > 0 ? fileSizes.Sum() : 0;
        }

        /// <summary>
        /// Checks whether an item is cached. Only the DB is being used,
        /// the disk contens are not ebing looked at.
        /// </summary>
        /// <param name="httpMethod">The used HTTP method.</param>
        /// <param name="uri">The URI.</param>
        /// <param name="databaseContext">The database context.</param>
        /// <returns>If the item is cached.</returns>
        private bool IsCached(string httpMethod, string uri, RCDatabaseEntities databaseContext)
        {
            if (uri.Length > 2000)
            {
                return false;
            }
            return (from gci in databaseContext.GlobalCacheItem
                    where gci.httpMethod.Equals(httpMethod) && gci.url.Equals(uri)
                    select 1).Count() != 0;
        }

        /// <summary>
        /// Gets the global cache item for the specified HTTP method and URI, if it exists,
        /// and null otherwise.
        /// </summary>
        /// <param name="httpMethod">The HTTP method.</param>
        /// <param name="uri">The URI.</param>
        /// <param name="databaseContext">The database context.</param>
        /// <returns>The global cache item or null.</returns>
        private GlobalCacheItem GetGlobalCacheItem(string httpMethod, string uri, RCDatabaseEntities databaseContext)
        {
            if (uri.Length > 2000)
            {
                return null;
            }
            return (from gci in databaseContext.GlobalCacheItem
                    where gci.httpMethod.Equals(httpMethod) && gci.url.Equals(uri)
                    select gci).FirstOrDefault();
        }

        /// <summary>
        /// Gets the global cache RC data item for the specified HTTP method and URI, if it exists,
        /// and null otherwise.
        /// </summary>
        /// <param name="httpMethod">The HTTP method.</param>
        /// <param name="uri">The URI.</param>
        /// <param name="databaseContext">The database context.</param>
        /// <returns>The global cache RC data item or null.</returns>
        private GlobalCacheRCData GetGlobalCacheRCData(string httpMethod, string uri, RCDatabaseEntities databaseContext)
        {
            if (uri.Length > 2000)
            {
                return null;
            }
            return (from gcrc in databaseContext.GlobalCacheRCData
                    where gcrc.httpMethod.Equals(httpMethod) && gcrc.url.Equals(uri)
                    select gcrc).FirstOrDefault();
        }

        /// <summary>
        /// Removes a cache item and deletes the file. This method is used internally, when evicting.
        /// </summary>
        /// <param name="cacheItem">The cache item.</param>
        /// <param name="databaseContext">The database context.</param>
        private void RemoveCacheItem(GlobalCacheItem cacheItem, RCDatabaseEntities databaseContext)
        {
            _proxy.Logger.Debug(String.Format("Removing from the cache: {0} {1} Last request: {2}",
                cacheItem.httpMethod, cacheItem.url, cacheItem.GlobalCacheRCData.lastRequestTime));
            // Remove file
            Utils.DeleteFile(_cachePath + GetRelativeCacheFileName(cacheItem.url, cacheItem.httpMethod));
            // Remove cache item entry
            databaseContext.GlobalCacheItem.Remove(cacheItem);

            // If we're on the local proxy, we want to add text documents to the Lucene index.
            if (_proxy is RCLocalProxy && cacheItem.httpMethod.Equals("GET") &&
                (cacheItem.responseHeaders.Contains("\"Content-Type\":[\"text/html") ||
                cacheItem.responseHeaders.Contains("\"Content-Type\":[\"text/plain")))
            {
                try
                {
                    // remove the file from Lucene, if it is a GET text or HTML file.
                    // We have made sure the content-type header is always present in the DB!
                    ((RCLocalProxy)_proxy).IndexWrapper.DeleteDocument(cacheItem.url);
                }
                catch (Exception e)
                {
                    _proxy.Logger.Warn("Could not remove document from the index.", e);
                }
            }
        }

        /// <summary>
        /// Evict items from the cache (also deleting the files from disk), until
        /// a total of bytesToEvict is deleted.
        /// 
        /// LRU is the eviction strategy.
        /// </summary>
        /// <param name="bytesToEvict">The number of bytes to evict.</param>
        /// <param name="databaseContext">The database context.</param>
        private void EvictCacheItems(long bytesToEvict, RCDatabaseEntities databaseContext)
        {
            _proxy.Logger.Debug(String.Format("Evicting {0} bytes from the cache.", bytesToEvict));
            long evicted = 0;
            IOrderedQueryable<GlobalCacheItem> orderedCacheItems = (from gci in databaseContext.GlobalCacheItem select gci).
                OrderBy(gci => gci.GlobalCacheRCData.lastRequestTime);

            foreach (GlobalCacheItem gci in orderedCacheItems)
            {
                evicted += gci.filesize;
                RemoveCacheItem(gci, databaseContext);

                if (evicted >= bytesToEvict)
                {
                    break;
                }
            }
        }

        public void DeleteBZ2Entries()
        {
            RCDatabaseEntities databaseContext = GetNewDatabaseContext();
            IQueryable<GlobalCacheItem> bz2s = from gci in databaseContext.GlobalCacheItem
                    where
                        gci.filename.EndsWith(".html.bz2") ||
                        gci.filename.EndsWith(".htm.bz2") ||
                        gci.filename.EndsWith(".txt.bz2") ||
                        gci.filename.EndsWith(".xml.bz2") ||
                        gci.filename.EndsWith(".js.bz2")
                    select gci;

            int counter = 0;
            foreach (GlobalCacheItem bz2 in bz2s)
            {
                try
                {
                    string newFileName = _cachePath + bz2.filename.Substring(0, bz2.filename.Length - 4);
                    FileInfo newFileInfo = new FileInfo(newFileName);
                    if (newFileInfo.Exists)
                    {
                        // Update DB entry
                        bz2.GlobalCacheRCData.url = bz2.GlobalCacheRCData.url.Substring(0, bz2.filename.Length - 4);
                        bz2.url = bz2.url.Substring(0, bz2.filename.Length - 4);
                        bz2.filename = bz2.filename.Substring(0, bz2.filename.Length - 4);
                        bz2.filesize = newFileInfo.Length;
                    }
                    else
                    {
                        // Remove DB entry
                        databaseContext.GlobalCacheRCData.Remove(bz2.GlobalCacheRCData);
                        databaseContext.GlobalCacheItem.Remove(bz2);
                    }
                    // Save (just when debugging to see if it works)
                    // databaseContext.SaveChanges();

                    counter++;
                    if (counter == DATABASE_BULK_INSERT_THRESHOLD)
                    {
                        counter = 0;
                        _proxy.Logger.Info(DATABASE_BULK_INSERT_THRESHOLD + " files updated. Saving database changes made so far.");
                        databaseContext.SaveChanges();
                        databaseContext.Dispose();
                        databaseContext = GetNewDatabaseContext();
                    }
                         
                }
                catch (Exception e)
                {
                    _proxy.Logger.Warn("Error deleting/updating bz2 entries: ", e);
                }
            }

            try
            {
                // Save
                _proxy.Logger.Info("Saving completed database changes.");
                databaseContext.SaveChanges();
                databaseContext.Dispose();
            }
            catch (Exception e)
            {
                _proxy.Logger.Warn("Error deleting/updating bz2 entries: ", e);
            }
        }

        #endregion
        #region analysis

        /// <summary>
        /// Gets all files that exist in the cache directory.
        /// The database is NOT used.
        /// </summary>
        /// <returns>A list of filenames of all files in the cache.</returns>
        private List<string> AllFiles()
        {
            return Directory.EnumerateFiles(_cachePath, "*", SearchOption.AllDirectories).ToList();
        }

        /// <summary>
        /// Gets a list of all file infos of the files that exist in the cache directory.
        /// This method should be preferred over AllFiles(), if additional info (e.g. filesize)
        /// is needed.
        /// 
        /// The database is NOT used.
        /// </summary>
        /// <returns>A list of all file infos.</returns>
        private List<FileInfo> AllFileInfos()
        {
            DirectoryInfo dirInfo = new DirectoryInfo(_cachePath);
            return dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).ToList();
        }

        /// <summary>
        /// Gets all files that have Content-Type: text/html or text/plain
        /// The database IS used.
        /// </summary>
        /// <returns>A list of filenames of all text files in the cache.</returns>
        private List<string> TextFiles()
        {
            // Old version without DB
            //List<string> dirResults = Directory.EnumerateFiles(_cachePath, "*", SearchOption.AllDirectories)
            //    .Where(filename => new string[] { "text/html", "text/plain" }.
            //        Contains(Utils.GetContentTypeOfFile(filename))).ToList();

            // Request all filenames where the header has the specified content type. Contains looks dirty
            // as we save the headers in JSON format. This is faster as deserializing before testing,
            // as SQL can do it for as.
            using (RCDatabaseEntities databaseContext = GetNewDatabaseContext())
            {
                List<string> dbResults = (from gci in databaseContext.GlobalCacheItem
                                          where gci.responseHeaders.Contains("\"Content-Type\":[\"text/html") ||
                                            gci.responseHeaders.Contains("\"Content-Type\":[\"text/plain")
                                          select gci.filename).ToList();
                // Convert relative to global filenames (extra step as the above is converted into SQL, where this
                // cannot be done)
                return (from relFile in dbResults select _cachePath + relFile).ToList();
            }
        }

        /// <summary>
        /// Gets the timestamp of the current clusters.xml, if existent.
        /// </summary>
        /// <returns>The timestamp.</returns>
        public DateTime GetClusteringTimeStamp()
        {
            string xmlFileName = _clustersPath + CLUSTERS_XML_FILE_NAME;
            return Cluster.GetClusteringTimeStamp(xmlFileName);
        }

        /// <summary>
        /// Creates the clusters.
        /// 
        /// </summary>
        /// <param name="k">The number of clusters to create.</param>
        /// <param name="catNFeatures">The maximum number of features for a category.</param>
        /// <param name="subcatNFeatures">The maximum number of features for a subcategory.</param>
        /// <param name="hierarchical">If the clusters should be organized hierarchical.</param>
        /// <param name="maxCategories">The maximum number of categories.</param>
        public void CreateClusters(int k, int catNFeatures, int subcatNFeatures, bool hierarchical, int maxCategories)
        {
            _proxy.Logger.Info("Clustering: Creating clusters. This may take around an hour!");
            // Measure what part takes what time
            Stopwatch stopwatch = new Stopwatch();

            // Filenames
            string docFileName = _clustersPath + DOC_FILE_NAME;
            string matFileName = _clustersPath + MAT_FILE_NAME;
            string clustersFileName = _clustersPath + CLUSTERS_FILE_NAME;
            string xmlBTFileName = _clustersPath + CLUSTERS_BT_XML_FILE_NAME;
            string xmlFileName = _clustersPath + CLUSTERS_XML_FILE_NAME;

            List<string> textFiles;

            // get files
            _proxy.Logger.Debug("Clustering (1/6): Getting all text files.");
            stopwatch.Start();
            textFiles = TextFiles();
            stopwatch.Stop();
            _proxy.Logger.Debug("Custering (1/6): Getting all text files took: " + stopwatch.Elapsed.TotalSeconds + "s");

            // Abort if we're having less than 2 text files
            if (textFiles.Count < 2)
            {
                _proxy.Logger.Debug("Clustering: Less than 2 text files, aborting.");
                return;
            }
            // List number of text files
            _proxy.Logger.Debug(String.Format("Clustering (1/6): Using {0} text files.", textFiles.Count));
            // List all Text files XXX Debug
            //foreach(string textFile in textFiles)
            //{
            //    _proxy.Logger.Debug("Clustering uses file: " + textFile);
            //}

            List<string> titles;
            // files2doc
            _proxy.Logger.Debug("Clustering (2/6): Creating docfile.");
            stopwatch.Restart();
            try
            {
                titles = Cluster.CreateDocFile(textFiles, docFileName);
            }
            catch (IOException e)
            {
                _proxy.Logger.Warn("Clustering: DocFile creation failed.", e);
                return;
            }
            stopwatch.Stop();
            _proxy.Logger.Debug("Custering (2/6): Creating docfile took: " + stopwatch.Elapsed.TotalSeconds + "s");


            // doc2mat
            _proxy.Logger.Debug("Clustering (3/6): Doc2Mat.");
            stopwatch.Restart();
            try
            {
                Doc2Mat.DoDoc2Mat(docFileName, matFileName);
            }
            catch (Exception e)
            {
                _proxy.Logger.Warn("Clustering: Doc2Mat failed.", e);
                return;
            }
            stopwatch.Stop();
            _proxy.Logger.Debug("Custering (3/6): Doc2Mat took: " + stopwatch.Elapsed.TotalSeconds + "s");

            // ClutoClusters
            _proxy.Logger.Debug("Clustering (4/6): Cluto-Clustering.");
            string treeFileName = null;
            HashSet<string>[] features;
            stopwatch.Restart();
            try
            {
                if (hierarchical)
                {
                    treeFileName = _clustersPath + TREE_FILE_NAME;
                    features = Cluster.CreateClusters(matFileName, clustersFileName, k, true, treeFileName,
                        catNFeatures, subcatNFeatures);
                }
                else
                {
                    features = Cluster.CreateClusters(matFileName, clustersFileName, k, false, "",
                        catNFeatures, subcatNFeatures);
                }
            }
            catch (Exception e)
            {
                _proxy.Logger.Warn("Clustering: Cluto failed.", e);
                return;
            }
            stopwatch.Stop();
            _proxy.Logger.Debug("Custering (4/6): Cluto-Clustering took: " + stopwatch.Elapsed.TotalSeconds + "s");

            // Create binary tree XML file
            _proxy.Logger.Debug("Clustering (5/6): Creating clustersBT.xml.");
            stopwatch.Restart();
            try
            {
                Cluster.CreateClusterBTXMLFile(textFiles, features, clustersFileName, (hierarchical ? treeFileName : ""),
                    xmlBTFileName, k, _cachePath.Length, titles);
            }
            catch (Exception e)
            {
                _proxy.Logger.Warn("Clustering: Creating XML failed.", e);
                return;
            }
            stopwatch.Stop();
            _proxy.Logger.Debug("Clustering (5/6): Creating clustersBT.xml took " + stopwatch.Elapsed.TotalSeconds + " s");

            // Create XML file
            _proxy.Logger.Debug("Clustering (6/6): Creating clusters.xml.");
            stopwatch.Restart();
            try
            {
                Cluster.CreateClusterXMLFile(xmlFileName, xmlBTFileName, maxCategories);
            }
            catch (Exception e)
            {
                _proxy.Logger.Error("Clustering: Creating clusters.xml failed.", e);
                return;
            }
            stopwatch.Stop();
            _proxy.Logger.Debug("Custering (6/6): Creating clusters.xml took: " + stopwatch.Elapsed.TotalSeconds + "s");

            _proxy.Logger.Info("Clustering: Finished successfully.");
        }

        #endregion
        #region synchronization

        /// <summary>
        /// Gets locks for all items. In alphabetic order to prevent deadlocks.
        /// </summary>
        /// <param name="items">The items to get locks for.</param>
        private void GetLocksFor(IEnumerable<GlobalCacheItemToAdd> items)
        {
            foreach (GlobalCacheItemToAdd item in items.OrderBy(item => item.url).ThenBy(item => item.httpMethod))
            {
                GetLockFor(item.httpMethod, item.url);
            }
        }

        /// <summary>
        /// Gets a lock for the item with the given httpMethod and URL.
        /// 
        /// You can also lock on any string, passing that string as httpMethod and
        /// passing an empty string as URL.
        /// </summary>
        /// <param name="httpMethod">The HTTP method.</param>
        /// <param name="uri">The URL.</param>
        private void GetLockFor(string httpMethod, string uri)
        {
            while (true)
            {
                lock (_writesExecuted)
                {
                    if (!_writesExecuted.Contains(httpMethod + " " + uri))
                    {
                        _writesExecuted.Add(httpMethod + " " + uri);
                        break;
                    }
                    _writesEvent.Reset();
                }
                // Wait for event
                _writesEvent.WaitOne();
            }
        }

        /// <summary>
        /// Get the lock for a file by getting its http method and url from the file name.
        /// </summary>
        /// <param name="absoluteFileName">The absolute file name.</param>
        private void GetLockFor(string absoluteFileName)
        {
            string relFileName = absoluteFileName.Substring(_cachePath.Length);
            GetLockFor(GetHTTPMethodFromRelCacheFileName(relFileName),
                FilePathToUri(relFileName));
        }

        /// <summary>
        /// Releases locks for all items. In reverse alphabetic order to prevent deadlocks.
        /// </summary>
        /// <param name="items">The items to release locks for.</param>
        private void ReleaseLocksFor(IEnumerable<GlobalCacheItemToAdd> items)
        {
            foreach (GlobalCacheItemToAdd item in items.OrderByDescending(item => item.url).ThenByDescending(item => item.httpMethod))
            {
                ReleaseLockFor(item.httpMethod, item.url);
            }
        }

        /// <summary>
        /// Releases the lock for the item with the given httpMethod and URL.
        /// </summary>
        /// <param name="httpMethod">The HTTP method.</param>
        /// <param name="uri">The URL.</param>
        private void ReleaseLockFor(string httpMethod, string uri)
        {
            lock (_writesExecuted)
            {
                _writesExecuted.Remove(httpMethod + " " + uri);
                _writesEvent.Set();
            }
        }

        /// <summary>
        /// Releases the lock for a file by getting its http method and url from the file name.
        /// </summary>
        /// <param name="absoluteFileName">The absolute file name.</param>
        private void ReleaseLockFor(string absoluteFileName)
        {
            string relFileName = absoluteFileName.Substring(_cachePath.Length);
            ReleaseLockFor(GetHTTPMethodFromRelCacheFileName(relFileName),
                FilePathToUri(relFileName));
        }

        #endregion
    }
}
