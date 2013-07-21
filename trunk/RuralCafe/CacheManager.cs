using RuralCafe.Clusters;
using RuralCafe.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

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
        private const string CLUSTERS_FOLDER = "clusters";
        private const string DOC_FILE_NAME = "docfile.txt";
        private const string MAT_FILE_NAME = "cache.mat";
        private const string CLUSTERS_FILE_NAME = "clusters";
        private const string TREE_FILE_NAME = "tree";

        // Regex's for safe URI replacements
        private static readonly Regex unsafeChars1 = new Regex(@"[^a-z0-9\\\-\.]");
        private static readonly Regex unsafeChars2 = new Regex(@"[^a-z0-9/\-\.]");

        /// <summary>
        /// The path to the cache.
        /// </summary>
        private string _cachePath;

        /// <summary>
        /// The proxy.
        /// </summary>
        private RCProxy _proxy;

        /// <summary>
        /// The path to the cache.
        /// </summary>
        public string CachePath
        {
            get { return _cachePath; }
        }

        /// <summary>
        /// Creates a new CacheManager
        /// </summary>
        /// <param name="cachePath">The path to the cache.</param>
        public CacheManager(string cachePath, RCProxy proxy)
        {
            this._cachePath = cachePath;
            this._proxy = proxy;
        }

        /// <summary>
        /// Initializes the cache by making sure that the directory exists.
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
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Logs the cache metrics.
        /// </summary>
        public void LogCacheMetrics()
        {
            // TODO when this is done at the same time with Cluto, the clustering fails.

            //_proxy.Logger.Metric("Cache Items: " + AllFiles().Count);
            //_proxy.Logger.Metric("Cache Items with text/html mimetype: " + TextFiles().Count);
        }

        #region static filepath methods

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
                return "0" + Path.DirectorySeparatorChar.ToString() + "0" + Path.DirectorySeparatorChar.ToString() + fileName;
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
        /// <param name="speedBS">The streaming (download) speed will be stored in here.</param>
        /// <returns>The streamed bytes or -1 if failed.</returns>
        public long AddCacheItem(string fileName, HttpWebResponse webResponse, out long speedBS)
        {
            // Speed = 0 at first
            speedBS = 0;
            FileStream writeFile = Utils.CreateFile(fileName);
            if (writeFile == null)
            {
                return -1;
            }

            Stream contentStream = webResponse.GetResponseStream();
            Byte[] readBuffer = new Byte[4096];
            Stopwatch stopwatch = new Stopwatch();
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

                        // Start measuring speed
                        stopwatch.Start();
                        string content = reader.ReadToEnd();
                        stopwatch.Stop();

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
                    stopwatch.Start();
                    // No text. Read buffered.
                    int bytesRead = contentStream.Read(readBuffer, 0, readBuffer.Length);
                    while (bytesRead != 0)
                    {
                        writeFile.Write(readBuffer, 0, bytesRead);
                        bytesDownloaded += bytesRead;

                        // Read the next part of the response
                        bytesRead = contentStream.Read(readBuffer, 0, readBuffer.Length);
                    }
                    stopwatch.Stop();
                }
            }
            // Calculate speed
            speedBS = (long)(bytesDownloaded / stopwatch.Elapsed.TotalSeconds);

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
        #region analysis

        // TODO make fileName relative, not absolute
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
        /// <param name="xmlFile">The file where to store the XML result.</param>
        /// <param name="k">The number of clusters to create.</param>
        /// <param name="hierarchical">If the clusters should be organized hierarchical.</param>
        public void CreateClusters(string xmlFile, int k, bool hierarchical)
        {
            _proxy.Logger.Info("Creating clusters.");
            // Measure what part takes what time
            Stopwatch stopwatch = new Stopwatch();

            // Create directory, if it does not exist already
            string docFileName = _cachePath + CLUSTERS_FOLDER + Path.DirectorySeparatorChar + DOC_FILE_NAME;
            string matFileName = _cachePath + CLUSTERS_FOLDER + Path.DirectorySeparatorChar + MAT_FILE_NAME;
            string clustersFileName = _cachePath + CLUSTERS_FOLDER + Path.DirectorySeparatorChar + CLUSTERS_FILE_NAME;
            if (!Utils.CreateDirectoryForFile(docFileName))
            {
                _proxy.Logger.Error("Clustering: Could not create directory for docFile.");
                return;
            }

            // get files
            _proxy.Logger.Debug("Clustering: Getting all text files.");
            stopwatch.Start();
            List<string> textFiles = TextFiles();
            stopwatch.Stop();
            Console.WriteLine(stopwatch.Elapsed.TotalSeconds + "s");

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
                    treeFileName = _cachePath + CLUSTERS_FOLDER + Path.DirectorySeparatorChar + TREE_FILE_NAME;
                    features = Cluster.CreateClusters(matFileName, clustersFileName, k, true, treeFileName);
                }
                else
                {
                    features = Cluster.CreateClusters(matFileName, clustersFileName, k, false, "");
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
                    xmlFile, k, _cachePath.Length);
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
