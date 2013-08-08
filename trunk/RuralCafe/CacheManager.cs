﻿using RuralCafe.Clusters;
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

namespace RuralCafe
{
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
        public const string CLUSTERS_XML_FILE_NAME = "clusters.xml";
        private const string DATABASE_FILE_NAME = "RCDatabase.sdf";
        private readonly string EMPTY_DATABASE_FILE_NAME = Directory.GetCurrentDirectory()
            + Path.DirectorySeparatorChar + "Database" + Path.DirectorySeparatorChar + DATABASE_FILE_NAME;
        // Adapt this if the Database schema changes
        private readonly Dictionary<string, string[]> DB_SCHEMA = new Dictionary<string, string[]>() 
            { 
                { "GlobalCacheItem", new string[] { "httpMethod", "url", "responseHeaders", "filename", "statusCode" } },
                { "GlobalCacheRCData",  new string[] { "httpMethod", "url", "downloadTime", "lastRequestTime", "numberOfRequests" } },
                { "UserCacheDomain",  new string[] { "userID", "domain" } },
                { "UserCacheItem",  new string[] { "httpMethod", "url", "responseHeaders", "filename", "statusCode" } }
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
        /// The proxy.
        /// </summary>
        private RCProxy _proxy;

        /// <summary>
        /// An object to lock cache access internally.
        /// </summary>
        private object _lockObj = new object();
        /// <summary>
        /// An object to lock cache access externally.
        /// </summary>
        private object _extrenalLockObj = new object();

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
        /// An object to lock cache access externally in an additional layer.
        /// </summary>
        public object ExternalLockObj
        {
            get { return _extrenalLockObj; }
        }

        /// <summary>
        /// Creates a new CacheManager without a clusters path.
        /// </summary>
        /// <param name="cachePath">The path to the cache.</param>
        /// <param name="proxy">The proxy.</param>
        public CacheManager(string cachePath, RCProxy proxy)
        {
            this._cachePath = cachePath;
            this._proxy = proxy;
        }

        /// <summary>
        /// Creates a new CacheManager with a clusters path.
        /// </summary>
        /// <param name="cachePath">The path to the cache.</param>
        /// <param name="clustersPath">The path to the clusters</param>
        /// <param name="proxy">The proxy.</param>
        public CacheManager(string cachePath, string clustersPath, RCProxy proxy)
            : this(cachePath, proxy)
        {
            this._clustersPath = clustersPath;
        }

        #region (static) filepath methods

        /// <summary>
        /// Gets the path of a filename from an URI. Relative to the cache path.
        /// </summary>
        /// <param name="uri">The URI.</param>
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
            // XXX when this is done at the same time with Cluto, the clustering fails.

            _proxy.Logger.Metric("Cache Items: " + AllFiles().Count);
            _proxy.Logger.Metric("Cache Items with text/html mimetype: " + TextFiles().Count);
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
            return IsCached(httpMethod, uri, GetNewDatabaseContext());
        }

        /// <summary>
        /// Checks if the cache item is a text file.
        /// </summary>
        /// <param name="httpMethod">The HTTP method</param>
        /// <param name="uri">The URI.</param>
        /// <returns>True, if the content-type is "text/html" and false otherwise or if there is no such cache item.</returns>
        public bool IsHTMLFile(string httpMethod, string uri)
        {
            GlobalCacheItem gci = GetGlobalCacheItem(httpMethod, uri);
            if (gci == null)
            {
                // This hould not happen
                return false;
            }
            return gci.responseHeaders.Contains("\"Content-Type\":[\"text/html");
        }

        /// <summary>
        /// Gets the global cache item for the specified HTTP method and URI, if it exists,
        /// and null otherwise.
        /// </summary>
        /// <param name="httpMethod">The HTTP method.</param>
        /// <param name="uri">The URI.</param>
        /// <returns>The global cache item or null.</returns>
        public GlobalCacheItem GetGlobalCacheItem(string httpMethod, string uri)
        {
            RCDatabaseEntities databaseContext = GetNewDatabaseContext();
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
        /// <returns>The global cache RC data item or null.</returns>
        public GlobalCacheRCData GetGlobalCacheRCData(string httpMethod, string uri)
        {
            RCDatabaseEntities databaseContext = GetNewDatabaseContext();
            return (from gcrc in databaseContext.GlobalCacheRCData
                    where gcrc.httpMethod.Equals(httpMethod) && gcrc.url.Equals(uri)
                    select gcrc).FirstOrDefault();
        }

        /// <summary>
        /// Adds a cache item to the database. The file is assumed to exist already.
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <param name="httpMethod">The HTTP Method.</param>
        /// <param name="headers">The headers.</param>
        /// <param name="statusCode">The status code.</param>
        /// <returns>True for success and false for failure.</returns>
        public bool AddCacheItemForExistingFile(string url, string httpMethod,
            NameValueCollection headers, short statusCode)
        {
            string relFileName = GetRelativeCacheFileName(url, httpMethod);

            // locked: if exists return else create DB entry
            lock (_lockObj)
            {
                RCDatabaseEntities databaseContext = GetNewDatabaseContext();

                if (IsCached(httpMethod, url, databaseContext))
                {
                    _proxy.Logger.Debug("Already exists: " + httpMethod + " " + url);
                    return true;
                }

                // If the headers do not contain "Content-Type", which should practically not happen,
                // (but servers are actually not required to send it) we set it to the default:
                // "application/octet-stream"
                if (headers["Content-Type"] == null)
                {
                    headers["Content-Type"] = "application/octet-stream";
                }

                // Add database entry.
                try
                {
                    AddCacheItemToDatabase(url, httpMethod, headers, statusCode, relFileName, databaseContext);
                    databaseContext.SaveChanges();
                }
                catch (Exception e)
                {
                    _proxy.Logger.Error("Could not add cache item to the database.", e);
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Adds a cache item. If there is already an entry in the DB, nothing is done
        /// and true is returned.
        /// Otherwise a new DB entry will be created and a file will be created.
        /// </summary>
        /// <param name="webResponse">The web response.</param>
        /// <returns>True for success and false for failure.</returns>
        public bool AddCacheItem(HttpWebResponse webResponse)
        {
            string url = webResponse.ResponseUri.ToString();
            string httpMethod = webResponse.Method;
            NameValueCollection headers = webResponse.Headers;
            short statusCode = (short)webResponse.StatusCode;

            // Add file to the database
            // calling "for existing file" is a bit misleading, as the file
            // actually does not exist yet.
            if (!AddCacheItemForExistingFile(url, httpMethod, headers, statusCode))
            {
                return false;
            }
            // Add file to the disk
            return AddCacheItemToDisk(webResponse);
        }

        /// <summary>
        /// Removes an cache item from the DB and the disk.
        /// </summary>
        /// <param name="httpMethod">The used HTTP method.</param>
        /// <param name="uri">The URI.</param>
        public void RemoveCacheItem(string httpMethod, string uri)
        {
            lock(_lockObj)
            {
                RCDatabaseEntities databaseContext = GetNewDatabaseContext();
                try
                {
                    RemoveCacheItemFromDatabase(httpMethod, uri, databaseContext);
                    databaseContext.SaveChanges();
                }
                catch (Exception e)
                {
                    _proxy.Logger.Error("Could not remove cache item from the database.", e);
                    return;
                }
            }

            // Remove file
            Utils.DeleteFile(_cachePath + GetRelativeCacheFileName(uri, httpMethod));
        }

        #endregion
        #region cache file manipulation

        /// <summary>
        /// Adds a file and writes content into it.
        /// 
        /// This method should be used, when a file a created prior to adding it to the
        /// database, e.g. for 301s or when Unpacking. Then, AddCacheItemForExistingFile should
        /// be called later.
        /// 
        /// When a WebResponse for the content is available, this method should not be used!
        /// </summary>
        /// <param name="fileName">The absolute filename of the cache item.</param>
        /// <param name="content">The content to store.</param>
        public void CreateFileAndWrite(string fileName, string content)
        {
            Utils.CreateDirectoryForFile(fileName);
            using (StreamWriter sw = new StreamWriter(File.Open(fileName, FileMode.Create), Encoding.UTF8))
            {
                sw.Write(content);
            }
        }

        /// <summary>
        /// Adds a cache item to the disk from a web response's content.
        /// </summary>
        /// <param name="webResponse">The web response</param>
        /// <returns>True for success, false for failure.</returns>
        private bool AddCacheItemToDisk(HttpWebResponse webResponse)
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

        /// <summary>
        /// Removes a file from the cache.
        /// </summary>
        /// <param name="fileName">The absolute filename of the cache item.</param>
        /// <returns>True or false for success or failure.</returns>
        public bool RemoveCacheItemFromDisk(string fileName)
        {
            return Utils.DeleteFile(fileName);
        }

        #endregion
        #region private cache database methods

        private RCDatabaseEntities GetNewDatabaseContext()
        {
            // Create context and modify connection string to point to our DB file.
            RCDatabaseEntities result = new RCDatabaseEntities();
            result.Database.Connection.ConnectionString =
                String.Format("data source=\"{0}\"", _proxy.ProxyPath + DATABASE_FILE_NAME);
            return result;
        }

        /// <summary>
        /// Tries to create a new database by copying the empty one and filling it from the cache.
        /// Any exceptions are forewarded.
        /// </summary>
        private void CreateNewDatabase()
        {
            string dbFile = _proxy.ProxyPath + DATABASE_FILE_NAME;
            _proxy.Logger.Debug("Creating a new database file.");
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
                _proxy.Logger.Debug("No database file found.");
                try
                {
                    CreateNewDatabase();
                    return true;
                }
                catch (Exception e)
                {
                    _proxy.Logger.Error("Could not create database file.", e);
                    return false;
                }
            }

            bool validDB;
            try
            {
                validDB = CheckDatabaseIntegrityAndRepair();
            }
            catch (Exception e)
            {
                _proxy.Logger.Warn("Error checking database integrity: ", e);
                CreateNewDatabase();
                return true;
            }

            // If it exists, we must test whether it really contains a valid DB
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
                    _proxy.Logger.Error("Could not create database file.", e);
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
            RCDatabaseEntities databaseContext = GetNewDatabaseContext();
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
                foreach(string colName in DB_SCHEMA[tableName])
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

        /// <summary>
        /// Fills the database from the disk contents in the cache.
        /// Of course, entries will be incomplete, (headers missing, etc.)
        /// 
        /// Exceptions will be forwarded.
        /// </summary>
        private void FillDatabaseFromCache()
        {
            List<string> files = AllFiles();
            if (files.Count == 0)
            {
                // Cache is empty, as it should be.
                return;
            }

            _proxy.Logger.Warn("Creating new database, but the cache is not empty. Filling database with cache data...");
            RCDatabaseEntities databaseContext = GetNewDatabaseContext();
            // Iterate through all files and fill the database
            foreach (string file in files)
            {
                // We assume it was a GET request with a 200 OK answer.
                // We cannot recover the headers, but we look at the file to determine its content-type,
                // as we always want this header!
                NameValueCollection headers = new NameValueCollection()
                {
                    { "Content-Type", Utils.GetContentTypeOfFile(file)}
                };

                string uri = AbsoluteFilePathToUri(file);
                string relFileName = file.Substring(_cachePath.Length);
                AddCacheItemToDatabase(uri, "GET", headers, 200, relFileName, databaseContext);
                _proxy.Logger.Debug(String.Format("Adding {0} to the database.", file));
            }

            // Save
            _proxy.Logger.Debug("Saving database.");
            databaseContext.SaveChanges();
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
        /// <param name="databaseContext">The database context</param>
        private void AddCacheItemToDatabase(string url, string httpMethod,
            NameValueCollection headers, short statusCode, string relFileName, RCDatabaseEntities databaseContext)
        {
            string headersJson = JsonConvert.SerializeObject(headers,
                Formatting.None, new NameValueCollectionConverter());

            _proxy.Logger.Debug("Adding to database: " + httpMethod+ " "
                    + url);

            // Create item and save the values.
            GlobalCacheItem cacheItem = new GlobalCacheItem();
            cacheItem.url = url;
            cacheItem.httpMethod = httpMethod;
            cacheItem.responseHeaders = headersJson;
            cacheItem.statusCode = statusCode;
            cacheItem.filename = relFileName; // TODO sth. else?
            // add item
            databaseContext.GlobalCacheItem.Add(cacheItem);

            // create rc data item
            GlobalCacheRCData rcData = new GlobalCacheRCData();
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

        /// <summary>
        /// Removes an cache item from both DB tables.
        /// 
        /// databaseContext.SaveChanges() still needs to be called.
        /// </summary>
        /// <param name="httpMethod">The used HTTP method.</param>
        /// <param name="uri">The URI.</param>
        /// <param name="databaseContext">The database context</param>
        private void RemoveCacheItemFromDatabase(string httpMethod, string uri, RCDatabaseEntities databaseContext)
        {
            IQueryable<GlobalCacheItem> gciQuery = from gci in databaseContext.GlobalCacheItem 
                        where gci.httpMethod.Equals(httpMethod) && gci.url.Equals(uri)
                        select gci;
            foreach(GlobalCacheItem gci in gciQuery)
            {
                databaseContext.GlobalCacheItem.Remove(gci);
            }

            IQueryable<GlobalCacheRCData> rcQuery = from rci in databaseContext.GlobalCacheRCData
                                                  where rci.httpMethod.Equals(httpMethod) && rci.url.Equals(uri)
                                                  select rci;
            foreach (GlobalCacheRCData rci in rcQuery)
            {
                databaseContext.GlobalCacheRCData.Remove(rci);
            }
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
            return (from gci in databaseContext.GlobalCacheItem
                    where gci.httpMethod.Equals(httpMethod) && gci.url.Equals(uri)
                    select 1).Count() != 0;
        }

        #endregion
        #region analysis

        /// <summary>
        /// The file size of a cache item.
        /// </summary>
        /// <param name="fileName">The filename.</param>
        /// <returns>The filesize.</returns>
        public long CacheItemBytes(string fileName)
        {
            return Utils.GetFileSize(fileName);
        }

        /// <summary>
        /// Gets all files that exist in the directory.
        /// The database is NOT used.
        /// </summary>
        /// <returns>A list of filenames of all files in the cache.</returns>
        public List<string> AllFiles()
        {
            return Directory.EnumerateFiles(_cachePath, "*", SearchOption.AllDirectories).ToList();
        }

        /// <summary>
        /// Gets all files that have Content-Type: text/html or text/plain
        /// The database IS used.
        /// </summary>
        /// <returns>A list of filenames of all text files in the cache.</returns>
        public List<string> TextFiles()
        {
            // Old version without DB
            //List<string> dirResults = Directory.EnumerateFiles(_cachePath, "*", SearchOption.AllDirectories)
            //    .Where(filename => new string[] { "text/html", "text/plain" }.
            //        Contains(Utils.GetContentTypeOfFile(filename))).ToList();

            // Request all filenames where the header has the specified content type. Contains looks dirty
            // as we save the headers in JSON format. This is faster as deserializing before testing,
            // as SQL can do it for as.
            RCDatabaseEntities databaseContext = GetNewDatabaseContext();
            List<string> dbResults = (from gci in databaseContext.GlobalCacheItem
                                      where gci.responseHeaders.Contains("\"Content-Type\":[\"text/html") ||
                                        gci.responseHeaders.Contains("\"Content-Type\":[\"text/plain")
                                      select gci.filename).ToList();
            // Convert relative to global filenames (extra step as the above is converted into SQL, where this
            // cannot be done)
            return (from relFile in dbResults select _cachePath + relFile).ToList();
        }

        /// <summary>
        /// Creates the clusters.
        /// 
        /// TODO remove stopwatches
        /// </summary>
        /// <param name="k">The number of clusters to create.</param>
        /// <param name="catNFeatures">The maximum number of features for a category.</param>
        /// <param name="subcatNFeatures">The maximum number of features for a subcategory.</param>
        /// <param name="hierarchical">If the clusters should be organized hierarchical.</param>
        public void CreateClusters(int k, int catNFeatures, int subcatNFeatures, bool hierarchical)
        {
            _proxy.Logger.Info("Creating clusters.");
            // Measure what part takes what time
            Stopwatch stopwatch = new Stopwatch();

            // Filenames
            string docFileName = _clustersPath + DOC_FILE_NAME;
            string matFileName = _clustersPath + MAT_FILE_NAME;
            string clustersFileName = _clustersPath + CLUSTERS_FILE_NAME;
            string xmlFileName = _clustersPath + CLUSTERS_XML_FILE_NAME;

            // get files
            _proxy.Logger.Debug("Clustering: Getting all text files.");
            stopwatch.Start();
            List<string> textFiles = TextFiles();
            stopwatch.Stop();
            Console.WriteLine(stopwatch.Elapsed.TotalSeconds + "s");

            // Abort if we're having less than 2 text files
            if (textFiles.Count < 2)
            {
                _proxy.Logger.Debug("Clustering: Less than 2 text files, aborting.");
                return;
            }
            // List all Text files XXX Debug
            //foreach(string textFile in textFiles)
            //{
            //    _proxy.Logger.Debug("Clustering uses file: " + textFile);
            //}

            // files2doc
            _proxy.Logger.Debug("Clustering: Creating docfile.");
            stopwatch.Restart();
            try
            {
                Cluster.CreateDocFile(textFiles, docFileName);
            }
            catch (IOException e)
            {
                _proxy.Logger.Error("Clustering: DocFile creation failed.", e);
                return;
            }
            stopwatch.Stop();
            Console.WriteLine(stopwatch.Elapsed.TotalSeconds + "s");

            // doc2mat
            _proxy.Logger.Debug("Clustering: Doc2Mat.");
            stopwatch.Restart();
            try
            {
                Doc2Mat.DoDoc2Mat(docFileName, matFileName);
            }
            catch (Exception e)
            {
                _proxy.Logger.Error("Clustering: Doc2Mat failed.", e);
                return;
            }
            stopwatch.Stop();
            Console.WriteLine(stopwatch.Elapsed.TotalSeconds + "s");

            // ClutoClusters
            _proxy.Logger.Debug("Clustering: Cluto-Clustering.");
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
                _proxy.Logger.Error("Clustering: Cluto failed.", e);
                return;
            }
            stopwatch.Stop();
            Console.WriteLine(stopwatch.Elapsed.TotalSeconds + "s");

            // Create XML file
            _proxy.Logger.Debug("Clustering: Creating clusters.xml.");
            stopwatch.Restart();
            try
            {
                Cluster.CreateClusterXMLFile(textFiles, features, clustersFileName, (hierarchical ? treeFileName : ""),
                    xmlFileName, k, _cachePath.Length);
            }
            catch (Exception e)
            {
                _proxy.Logger.Error("Clustering: Creating XML failed.", e);
                return;
            }
            stopwatch.Stop();
            Console.WriteLine(stopwatch.Elapsed.TotalSeconds + "s");

            _proxy.Logger.Info("Clustering finished successfully.");
        }

        #endregion
    }
}
