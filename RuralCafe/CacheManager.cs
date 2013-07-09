using RuralCafe.Util;
using System;
using System.Collections.Generic;
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

        #endregion
    }
}
