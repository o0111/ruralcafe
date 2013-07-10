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

        /// <summary>
        /// The path to the cache.
        /// </summary>
        private string _cachePath;

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
        public CacheManager(string cachePath)
        {
            this._cachePath = cachePath;
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
        /// TODO customizable
        /// TODO use proxy's logger, do not write to console directly.
        /// TODO remove stopwatches
        /// </summary>
        public void CreateClusters()
        {
            Console.WriteLine("Creating clusters.");
            // Measure what part takes what time
            Stopwatch stopwatch = new Stopwatch();

            // Create directory, if it does not exist already
            string docFileName = _cachePath + CLUSTERS_FOLDER + Path.DirectorySeparatorChar + DOC_FILE_NAME;
            string matFileName = _cachePath + CLUSTERS_FOLDER + Path.DirectorySeparatorChar + MAT_FILE_NAME;
            if (!Utils.CreateDirectoryForFile(docFileName))
            {
                return;
            }

            // get files
            Console.Write("Getting all text files... ");
            stopwatch.Start();
            List<string> textFiles = TextFiles();
            stopwatch.Stop();
            Console.WriteLine(stopwatch.Elapsed.TotalSeconds + "s");

            // files2doc
            Console.Write("Creating docfile... ");
            stopwatch.Restart();
            Cluster.CreateDocFile(_cachePath.Length, textFiles, docFileName);
            stopwatch.Stop();
            Console.WriteLine(stopwatch.Elapsed.TotalSeconds + "s");

            // doc2mat
            Console.Write("Doc2Mat... ");
            stopwatch.Restart();
            Doc2Mat.DoDoc2Mat(docFileName, matFileName);
            stopwatch.Stop();
            Console.WriteLine(stopwatch.Elapsed.TotalSeconds + "s");

            // ClutoClusters
            Console.Write("Clustering... ");
            stopwatch.Restart();
            Cluster.CreateClusters(matFileName, 10);
            stopwatch.Stop();
            Console.WriteLine(stopwatch.Elapsed.TotalSeconds + "s");
        }

        #endregion
    }
}
