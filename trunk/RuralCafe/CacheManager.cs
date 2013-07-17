using RuralCafe.Clusters;
using RuralCafe.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

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

        #region analysis

        /// <summary>
        /// </summary>
        /// <returns>The overall number of elements in the cache.</returns>
        public int CountElements()
        {
            return AllFiles().Count;
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
            stopwatch.Restart();
            try
            {
                if (hierarchical)
                {
                    treeFileName = _cachePath + CLUSTERS_FOLDER + Path.DirectorySeparatorChar + TREE_FILE_NAME;
                    Cluster.CreateClusters(matFileName, clustersFileName, k, true, treeFileName);
                }
                else
                {
                    Cluster.CreateClusters(matFileName, clustersFileName, k, false, "");
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
                Cluster.CreateClusterXMLFile(textFiles, clustersFileName, (hierarchical ? treeFileName : ""),
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
