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
        private const string DATABASE_FILE_NAME = "RCDatabase.db";
        private readonly string EMPTY_DATABASE_FILE_NAME = Directory.GetCurrentDirectory()
            + Path.DirectorySeparatorChar + "Database" + Path.DirectorySeparatorChar + DATABASE_FILE_NAME;

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

        private RCDatabaseEntities _databaseContext;

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
        {
            this._cachePath = cachePath;
            this._clustersPath = clustersPath;
            this._proxy = proxy;
        }

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

        #region (static) filepath methods

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
            // Remove the 2 hash dirs from path
            for (int i = 0; i < 2; i++)
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
        #region cache manipulation

        /// <summary>
        /// Creates a directory for a cache item.
        /// </summary>
        /// <param name="fileName">The absolute filename of the cache item.</param>
        /// <returns>True or false for success or failure.</returns>
        public bool CreateDirectoryForCacheItem(string fileName)
        {
            return Utils.CreateDirectoryForFile(fileName);
        }

        /// <summary>
        /// Adds a cache item, and saves a string in it.
        /// </summary>
        /// <param name="fileName">The absolute filename of the cache item.</param>
        /// <param name="content">The content to store.</param>
        public void AddCacheItem(string fileName, string content)
        {
            using (StreamWriter sw = new StreamWriter(File.Open(fileName, FileMode.Create), Encoding.UTF8))
            {
                sw.Write(content);
            }
        }

        /// <summary>
        /// Adds a cache item from a web response's content.
        /// </summary>
        /// <param name="fileName">The absolute filename of the cache item.</param>
        /// <param name="webResponse">The web response</param>
        /// <returns>The streamed bytes or -1 if failed.</returns>
        public long AddCacheItem(string fileName, HttpWebResponse webResponse)
        {
            FileStream writeFile = Utils.CreateFile(fileName);
            if (writeFile == null)
            {
                return -1;
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
            return bytesDownloaded;
        }

        /// <summary>
        /// Removes a file from the cache.
        /// </summary>
        /// <param name="fileName">The absolute filename of the cache item.</param>
        /// <returns>True or false for success or failure.</returns>
        public bool RemoveCacheItem(string fileName)
        {
            return Utils.DeleteFile(fileName);
        }

        #endregion
        #region cache database

        /// <summary>
        /// Checks if there is a database and creates one if there isn't.
        /// 
        /// If there was none, the database will be filled with the current cache content.
        /// </summary>
        /// <returns>True if successful, false otherwise.</returns>
        private bool InitializeDatabase()
        {
            string dbFile = _proxy.ProxyPath + DATABASE_FILE_NAME;
            // Create context and modify connection string to point to our DB file.
            _databaseContext = new RCDatabaseEntities();
            _databaseContext.Database.Connection.ConnectionString =
                String.Format("data source=\"{0}\"", dbFile);

            if (!File.Exists(dbFile))
            {
                try
                {
                    _proxy.Logger.Debug("No database file found, creating a new one.");
                    File.Copy(EMPTY_DATABASE_FILE_NAME, dbFile);
                    // We must fill the database with the current cache content!
                    FillDatabaseFromCache();
                    return true;
                }
                catch (Exception e)
                {
                    _proxy.Logger.Error("Could not create database file.", e);
                    return false;
                }
            }
            // If it exists, we must test whether it really contains a valid DB
            return CheckDatabaseIntegrityAndRepair();
        }

        /// <summary>
        /// Tests if the existing database file is valid, that means contains the tables and rows we need.
        /// 
        /// Repairs the database if there are rows missing. If that is not possible, false is returned.
        /// </summary>
        /// <returns>True for a valid DB file, false otherwise.</returns>
        private bool CheckDatabaseIntegrityAndRepair()
        {
            // http://stackoverflow.com/questions/3528361/is-there-an-way-using-ado-net-to-determine-if-a-table-exists-in-a-database-that

            // Loop through all tables and rows and check if they exist.
            // If a table is missing, delete file and and run return false
            // If a row is missing, add and fill with default values?
            var gcis = _databaseContext.Database.SqlQuery<string>(
                "SELECT name FROM sqlite_master WHERE type='table' AND name='GlobalCacheItem'");
            // TODO
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

            _proxy.Logger.Warn("No database file was found, but the cache is not empty. Filling database with cache data...");
            // Iterate through all files and fill the database
            foreach (string file in files)
            {
                // We assume it was a GET request with a 200 OK answer.
                // We cannot recover the headers
                AddCacheItemToDatabase(AbsoluteFilePathToUri(file), "GET", new NameValueCollection(), 200);
                _proxy.Logger.Debug(String.Format("Adding {0} to the database.", file));
            }

            // Save
            _proxy.Logger.Debug("Saving database.");
            _databaseContext.SaveChanges();

            // Debug: print DB contents with banana
            //var query = from gc in _databaseContext.GlobalCacheItem where gc.url.Contains("banana") select gc;
            //foreach (var gci in query)
            //{
            //    Console.WriteLine(String.Format("{0} {1} {2}", gci.httpMethod, gci.url, gci.responseHeaders));
            //}
            //var query2 = from rc in _databaseContext.GlobalCacheRCData where rc.url.Contains("banana") select rc;
            //foreach (var rci in query2)
            //{
            //    Console.WriteLine(String.Format("{0} {1} {2}", rci.httpMethod, rci.url, rci.downloadTime));
            //}
        }

        /// <summary>
        /// Adds a new cache item to the database. Throws an exception if anything goes wrong.
        /// 
        /// _databaseContext.SaveChanges() still needs to be called.
        /// </summary>
        /// <param name="url">The url.</param>
        /// <param name="httpMethod">The HTTP method.</param>
        /// <param name="headers">The headers.</param>
        /// <param name="statusCode">The status code.</param>
        public void AddCacheItemToDatabase(string url, string httpMethod,
            NameValueCollection headers, short statusCode)
        {
            string fileName = GetRelativeCacheFileName(url);
            string headersJson = JsonConvert.SerializeObject(headers,
                Formatting.None, new NameValueCollectionConverter());

            // Create item and save the values.
            GlobalCacheItem cacheItem = new GlobalCacheItem();
            cacheItem.url = url;
            cacheItem.httpMethod = httpMethod;
            cacheItem.responseHeaders = headersJson;
            cacheItem.statusCode = statusCode;
            cacheItem.filename = fileName; // TODO sth. else?
            // add item
            _databaseContext.GlobalCacheItem.Add(cacheItem);

            // create rc data item
            GlobalCacheRCData rcData = new GlobalCacheRCData();
            rcData.url = url;
            rcData.httpMethod = httpMethod;
            // Although this is not really a request, we set the lastRequestTime to now
            rcData.lastRequestTime = DateTime.Now;
            // No requests so far
            rcData.numberOfRequests = 0;
            // Download time is the lastModified time of the file.
            rcData.downloadTime = File.GetLastWriteTime(_cachePath + fileName);
            // add item
            _databaseContext.GlobalCacheRCData.Add(rcData);
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
        /// </summary>
        /// <returns>A list of filenames of all files in the cache.</returns>
        public List<string> AllFiles()
        {
            return Directory.EnumerateFiles(_cachePath, "*", SearchOption.AllDirectories).ToList();
        }

        /// <summary>
        /// </summary>
        /// <returns>A list of filenames of files with content type="text/html" in the cache.</returns>
        public List<string> TextFiles()
        {
            return Directory.EnumerateFiles(_cachePath, "*", SearchOption.AllDirectories)
                .Where(filename => Utils.GetContentTypeOfFile(filename).Equals("text/html")).ToList();
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
            foreach(string textFile in textFiles)
            {
                _proxy.Logger.Debug("Clustering uses file: " + textFile);
            }

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
