using RuralCafe.Lucenenet;
using RuralCafe.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;
using System.Xml;

namespace RuralCafe.Clusters
{
    /// <summary>
    /// Provides functionality to create clusters.
    /// </summary>
    public static class Cluster
    {
        // Constants
        private const int VCLUSTER_TIMEOUT_MS = 1000 * 60 * 60 * 2; // 2 hours
        private const string DOC_FILE_NAME = "docfile.txt";
        private const string MAT_FILE_NAME = "cache.mat";
        private const string CLUSTERS_FILE_NAME = "clusters";
        private const string TREE_FILE_NAME = "tree";
        /// <summary>The file name of the binary tree clusters xml file.</summary>
        public const string CLUSTERS_BT_XML_FILE_NAME = "clustersBT.xml";
        public const string CLUSTERS_BT_XML_NAME = "clusters";
        public const string CLUSTERS_BT_NUMBEROFCLUSTERS_XML_NAME = "numberOfClusters";
        public const string CLUSTERS_BT_HIERARCHICAL_XML_NAME = "hierarchical";

        /// <summary>
        /// Path to vcluster.exe
        /// </summary>
        private static readonly string VCLUSTERS_PATH =
            Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar +
            "Clusters" + Path.DirectorySeparatorChar + "vcluster.exe";

        /// <summary>
        /// Path to dict.txt
        /// </summary>
        private static readonly string DICT_PATH =
            Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar +
            "Clusters" + Path.DirectorySeparatorChar + "dict.txt";
        /// <summary>
        /// Path to blacklist.txt
        /// </summary>
        private static readonly string BLACKLIST_PATH =
            Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar +
            "Clusters" + Path.DirectorySeparatorChar + "blacklist.txt";
        /// <summary>
        /// A dictionary of the english language. Used as a whitelist for the clustering.
        /// </summary>
        private static HashSet<string> _dictionary = new HashSet<string>();
        /// <summary>
        /// The blacklist words for clustering.
        /// </summary>
        private static HashSet<string> _blacklist = new HashSet<string>();


        /// <summary>
        /// Static Constructor. Fills _dictionary and _blacklist.
        /// </summary>
        static Cluster()
        {
            FileInfo dict = new FileInfo(DICT_PATH);
            using (FileStream fs = dict.Open(FileMode.Open, FileAccess.Read))
            using (StreamReader r = new StreamReader(fs))
            {
                while (!r.EndOfStream)
                {
                    _dictionary.Add(r.ReadLine());
                }
            }
            FileInfo bl = new FileInfo(BLACKLIST_PATH);
            using (FileStream fs = bl.Open(FileMode.Open, FileAccess.Read))
            using (StreamReader r = new StreamReader(fs))
            {
                while (!r.EndOfStream)
                {
                    _blacklist.Add(r.ReadLine());
                }
            }
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
        /// <param name="clustersPath">The path to the clusters folder.</param>
        /// <param name="proxy">The proxy.</param>
        public static void CreateClusters(int k, int catNFeatures, int subcatNFeatures, bool hierarchical,
            int maxCategories, string clustersPath, RCLocalProxy proxy)
        {
            proxy.Logger.Info("Clustering: Creating clusters. This may take around an hour!");
            // Measure what part takes what time
            Stopwatch stopwatch = new Stopwatch();

            // Filenames
            string docFileName = clustersPath + DOC_FILE_NAME;
            string matFileName = clustersPath + MAT_FILE_NAME;
            string clustersFileName = clustersPath + CLUSTERS_FILE_NAME;
            string xmlBTFileName = clustersPath + CLUSTERS_BT_XML_FILE_NAME;
            string xmlFileName = clustersPath + IndexServer.CLUSTERS_XML_FILE_NAME;

            // get files
            proxy.Logger.Debug("Clustering (1/6): Getting all text files.");
            stopwatch.Start();
            List<string> textFiles = proxy.ProxyCacheManager.TextFiles();
            stopwatch.Stop();
            proxy.Logger.Debug("Custering (1/6): Getting all text files took: " + stopwatch.Elapsed.TotalSeconds + "s");

            // Abort if we're having less than 2 text files
            if (textFiles.Count < 2)
            {
                proxy.Logger.Debug("Clustering: Less than 2 text files, aborting.");
                return;
            }
            // List number of text files
            proxy.Logger.Debug(String.Format("Clustering (1/6): Using {0} text files.", textFiles.Count));

            List<string> titles;
            // files2doc
            proxy.Logger.Debug("Clustering (2/6): Creating docfile.");
            stopwatch.Restart();
            try
            {
                titles = Cluster.CreateDocFile(textFiles, docFileName);
            }
            catch (IOException e)
            {
                proxy.Logger.Warn("Clustering: DocFile creation failed.", e);
                return;
            }
            stopwatch.Stop();
            proxy.Logger.Debug("Custering (2/6): Creating docfile took: " + stopwatch.Elapsed.TotalSeconds + "s");


            // doc2mat
            proxy.Logger.Debug("Clustering (3/6): Doc2Mat.");
            stopwatch.Restart();
            try
            {
                Doc2Mat.DoDoc2Mat(docFileName, matFileName);
            }
            catch (Exception e)
            {
                proxy.Logger.Warn("Clustering: Doc2Mat failed.", e);
                return;
            }
            stopwatch.Stop();
            proxy.Logger.Debug("Custering (3/6): Doc2Mat took: " + stopwatch.Elapsed.TotalSeconds + "s");

            // ClutoClusters
            proxy.Logger.Debug("Clustering (4/6): Cluto-Clustering.");
            string treeFileName = null;
            HashSet<string>[] features;
            stopwatch.Restart();
            try
            {
                if (hierarchical)
                {
                    treeFileName = clustersPath + TREE_FILE_NAME;
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
                proxy.Logger.Warn("Clustering: Cluto failed.", e);
                return;
            }
            stopwatch.Stop();
            proxy.Logger.Debug("Custering (4/6): Cluto-Clustering took: " + stopwatch.Elapsed.TotalSeconds + "s");

            // Create binary tree XML file
            proxy.Logger.Debug("Clustering (5/6): Creating clustersBT.xml.");
            stopwatch.Restart();
            try
            {
                Cluster.CreateClusterBTXMLFile(textFiles, features, clustersFileName, (hierarchical ? treeFileName : ""),
                    xmlBTFileName, k, proxy.CachePath.Length, titles);
            }
            catch (Exception e)
            {
                proxy.Logger.Warn("Clustering: Creating XML failed.", e);
                return;
            }
            stopwatch.Stop();
            proxy.Logger.Debug("Clustering (5/6): Creating clustersBT.xml took " + stopwatch.Elapsed.TotalSeconds + " s");

            // Create XML file
            proxy.Logger.Debug("Clustering (6/6): Creating clusters.xml.");
            stopwatch.Restart();
            try
            {
                Cluster.CreateClusterXMLFile(xmlFileName, xmlBTFileName, maxCategories);
            }
            catch (Exception e)
            {
                proxy.Logger.Error("Clustering: Creating clusters.xml failed.", e);
                return;
            }
            stopwatch.Stop();
            proxy.Logger.Debug("Custering (6/6): Creating clusters.xml took: " + stopwatch.Elapsed.TotalSeconds + "s");

            proxy.Logger.Info("Clustering: Finished successfully.");
        }

        /// <summary>
        /// Creates one doc file containing all the given files. This file will be input for Doc2Mat.
        /// 
        /// Throws IOException if anything goes wrong.
        /// </summary>
        /// <param name="files">The text files to merge.</param>
        /// <param name="docFile">The docfile. Contains one file per line, text only.</param>
        /// <returns>The titles of all documents added to the docfile.</returns>
        private static List<string> CreateDocFile(List<string> files, string docFile)
        {
            FileStream docFileStream = Utils.CreateFile(docFile);
            if (docFileStream == null)
            {
                throw new IOException("Could not create docFile.");
            }

            // We save all HTML titles of the files in here.
            List<string> titles = new List<string>();

            using (StreamWriter docFileWriter = new StreamWriter(docFileStream, Encoding.UTF8))
            {
                for (int i = 0; i < files.Count; i++)
                {
                    string file = files[i];
                    // Read file
                    string content = Utils.ReadFileAsString(file);
                    // Remove empty or nonexisting files from the list
                    if (String.IsNullOrEmpty(content))
                    {
                        files.RemoveAt(i);
                        i--;
                        continue;
                    }
                    // Format HTML
                    content = WebUtility.HtmlDecode(content);
                    // Save page title
                    titles.Add(HtmlUtils.GetPageTitleFromHTML(content));
                    // Extract text from Html
                    content = HtmlUtils.ExtractText(content);
                    // Remove newlines
                    content = RegExs.NEWLINE_REGEX.Replace(content, " ");
                    // Empty content if this is a redirect
                    if (RegExs.REDIR_REGEX.IsMatch(content))
                    {
                        content = String.Empty;
                    }
                    // Filter all words by our dictionary.
                    StringBuilder builder = new StringBuilder();
                    string[] words = content.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string word in words)
                    {
                        // Use all words in the dictionary that are not blacklisted
                        if (_dictionary.Contains(word.ToLower()) && !_blacklist.Contains(word.ToLower()))
                        {
                            builder.Append(word).Append(' ');
                        }
                    }
                    content = builder.ToString();

                    // Write content in a new line to docfile
                    docFileWriter.WriteLine(content);
                }
            }
            return titles;
        }

        /// <summary>
        /// Creates k clusters from a matfile using vcluster.
        /// 
        /// Can throw various Exceptions.
        /// </summary>
        /// <param name="matFile">The matfile.</param>
        /// <param name="clustersFile">The clusters file.</param>
        /// <param name="k">The number of clusters to create.</param>
        /// <param name="fulltree">Wether a full hierarchical tree should be computed.</param>
        /// <param name="treefile">Only used if fulltree. Path to the treefile.</param>
        /// <param name="catNFeatures">The maximum number of features for a category.</param>
        /// <param name="subcatNFeatures">The maximum number of features for a subcategory.</param>
        /// <returns>A set of string-lists. Each list contains the labels for the cluster.</returns>
        private static HashSet<string>[] CreateClusters(string matFile, string clustersFile, int k,
            bool fulltree, string treefile, int catNFeatures, int subcatNFeatures)
        {
            // vcluster.exe -clmethod=rbr -nfeatures=<n> [-showtree -labeltree -treefile=<treefile>]
            //  -showfeatures -clustfile=<clustFile> <matFile> k
            ProcessStartInfo clusterStartInfo = new ProcessStartInfo(VCLUSTERS_PATH);
            clusterStartInfo.Arguments = "-clmethod=rbr -nfeatures=" + subcatNFeatures + " ";
            clusterStartInfo.Arguments += fulltree ? "-showtree -labeltree -cltreefile=\"" + treefile + "\" " : "";
            clusterStartInfo.Arguments += "-showfeatures -clustfile=\"" + clustersFile + "\" \"" + matFile + "\" " + k;

            clusterStartInfo.UseShellExecute = false;
            clusterStartInfo.RedirectStandardOutput = true;
            clusterStartInfo.RedirectStandardError = true;
            clusterStartInfo.CreateNoWindow = false;

            Process vcluster = new Process();
            vcluster.StartInfo = clusterStartInfo;

            // vcluster can produce large outputs, so we need to read asynchronouly or the thread will block.
            StringBuilder output = new StringBuilder();
            StringBuilder error = new StringBuilder();

            using (AutoResetEvent outputWaitHandle = new AutoResetEvent(false))
            using (AutoResetEvent errorWaitHandle = new AutoResetEvent(false))
            {
                vcluster.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data == null)
                    {
                        try
                        {
                            outputWaitHandle.Set();
                        }
                        catch { }

                    }
                    else
                    {
                        output.AppendLine(e.Data);
                    }
                };
                vcluster.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data == null)
                    {
                        try
                        {
                            errorWaitHandle.Set();
                        }
                        catch { }
                    }
                    else
                    {
                        error.AppendLine(e.Data);
                    }
                };

                // Start the process
                vcluster.Start();
                vcluster.BeginOutputReadLine();
                vcluster.BeginErrorReadLine();

                if (vcluster.WaitForExit(VCLUSTER_TIMEOUT_MS) &&
                    outputWaitHandle.WaitOne(VCLUSTER_TIMEOUT_MS) &&
                    errorWaitHandle.WaitOne(VCLUSTER_TIMEOUT_MS))
                {
                    // Process completed. Check process.ExitCode here.
                    // Parse output
                    Console.Write(output);
                    if (vcluster.ExitCode != 0)
                    {
                        throw new Exception("vcluster failed with exitcode: " + vcluster.ExitCode);
                    }
                    string[] lines = RegExs.NEWLINE_REGEX.Split(output.ToString());
                    // Create result array
                    HashSet<string>[] result = new HashSet<string>[fulltree ? 2 * k - 1 : k];
                    // Fill with empty sets
                    for (int i = 0; i < result.Length; i++)
                    {
                        result[i] = new HashSet<string>();
                    }

                    // Parse leaf cluster features
                    ParseLeafClusterFeatures(lines, result, k, subcatNFeatures);

                    if (fulltree)
                    {
                        // Parse non-leaf cluster features
                        ParseNonLeafClusterFeatures(lines, result, k, catNFeatures);
                    }

                    return result;
                }
                else
                {
                    // Timed out.
                    throw new Exception("vcluster timed out.");
                }
            }
        }

        /// <summary>
        /// Parses the leaf cluster features from the vcluster output.
        /// </summary>
        /// <param name="lines">The vcluster output, split into lines.</param>
        /// <param name="features">The list of features, which will be filled.</param>
        /// <param name="k">The number of clusters.</param>
        /// <param name="subcatNFeatures">The maximum number of features to be displayed for a subcategory.</param>
        private static void ParseLeafClusterFeatures(string[] lines, HashSet<string>[] features, int k,
            int subcatNFeatures)
        {
            // Get Index of line that contains "Descriptive & Discriminating Features"
            int cluster0lineIndex = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("Descriptive & Discriminating Features"))
                {
                    cluster0lineIndex = i + 3;
                    break;
                }
            }
            // Loop through all clusters
            for (int i = 0, index = cluster0lineIndex; i < k; i++, index += 4)
            {
                // For both descriptive and discriminating features
                string featuresString = lines[index].Substring(lines[index].IndexOf(':') + 1)
                    + ", " + lines[index + 1].Substring(lines[index + 1].IndexOf(':') + 1);
                PutFeaturesIntoSet(featuresString, features[i], subcatNFeatures);
            }
        }

        /// <summary>
        /// Parses the non-leaf cluster features from the vcluster output.
        /// </summary>
        /// <param name="lines">The vcluster output, split into lines.</param>
        /// <param name="features">The list of features, which will be filled.</param>
        /// <param name="k">The number of clusters.</param>
        /// <param name="catNFeatures">The maximum number of features to be displayed for a category.</param>
        private static void ParseNonLeafClusterFeatures(string[] lines, HashSet<string>[] features, int k,
            int catNFeatures)
        {
            // Get Index of line that contains "Hierarchical Tree"
            int clusterlineIndex = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("Hierarchical Tree"))
                {
                    clusterlineIndex = i + 3;
                    break;
                }
            }
            // Loop through all clusters
            for (int i = clusterlineIndex; i < clusterlineIndex + (2 * k - 1); i++)
            {
                // Get the cluster number
                string nums = "0123456789";
                int numStartIndex = lines[i].IndexOfAny(nums.ToCharArray());
                int numDelemitingIndex = lines[i].IndexOf('[');
                int clusterNumber = Int32.Parse(lines[i].Substring(numStartIndex, numDelemitingIndex - numStartIndex).Trim());

                // If the cluster number is smaller than k, it is a leaf and here we won't have to do anything
                if (clusterNumber < k)
                {
                    continue;
                }

                // Get the featuresString
                int featuresStartIndex = lines[i].IndexOf('[', numDelemitingIndex + 1) + 1;
                int featuresEndIndex = lines[i].LastIndexOf(',');
                string featureString = lines[i].Substring(featuresStartIndex, featuresEndIndex - featuresStartIndex);
                PutFeaturesIntoSet(featureString, features[clusterNumber], catNFeatures);
            }
        }

        /// <summary>
        /// Puts all the features from the feature string into the set, but not more than max.
        /// </summary>
        /// <param name="featuresString">The features string.</param>
        /// <param name="features">The feature set.</param>
        /// <param name="max">The maximum number of features to put in the set.</param>
        private static void PutFeaturesIntoSet(string featuresString, HashSet<string> features, int max)
        {
            string[] featuresStringArray = featuresString.Split(',');
            if (featuresStringArray.Length > max)
            {
                // We have to filter
                List<KeyValuePair<double, string>> sortedFeatures = new List<KeyValuePair<double, string>>(featuresStringArray.Length);
                // Add all features and their percent value to a list
                for (int i = 0; i < featuresStringArray.Length; i++)
                {
                    string[] featuresPlusPercent = featuresStringArray[i].Split(new string[] { " ", "\t" },
                        StringSplitOptions.RemoveEmptyEntries);
                    string feature = featuresPlusPercent[0];

                    double percent = 0;
                    // This is displayed for empty clusters. For empty we clusters we don't extract the features.
                    if (!featuresPlusPercent[1].Equals("-1.$%"))
                    {
                        percent = Double.Parse(featuresPlusPercent[1].Substring(0, featuresPlusPercent[1].Length - 1));
                        sortedFeatures.Add(new KeyValuePair<double, string>(percent, feature));
                    }

                }
                // Sort features by their percent value, descending
                sortedFeatures.Sort((pair1, pair2) => (int)(pair2.Key - pair1.Key));

                // Put into features until max is reached
                // (We cannot just count until max, since there may be dups)
                for (int i = 0; i < sortedFeatures.Count && features.Count < max; i++)
                {
                    features.Add(sortedFeatures.ElementAt(i).Value);
                }
            }
            else
            {
                // We don't need to filter
                for (int i = 0; i < featuresStringArray.Length; i++)
                {
                    string feature = featuresStringArray[i].Split(new string[] { " ", "\t" },
                        StringSplitOptions.RemoveEmptyEntries)[0];
                    features.Add(feature);
                }
            }
        }

        /// <summary>
        /// Creates an XML file containing all the cluster information in a binary tree.
        /// 
        /// Throws an Exception (e.g. FormatException or XmlException)
        /// if anything goes wrong.
        /// </summary>
        /// <param name="fileNames">A list of all files that have been clustered.</param>
        /// <param name="features">A list of features for each cluster.</param>
        /// <param name="clusterFile">Cluto's result file.</param>
        /// <param name="treeFile">The tree file from cluto. If null or Empty, a flat XML will be produced.</param>
        /// <param name="xmlFile">The XML result will be stored in here.</param>
        /// <param name="k">The number of clusters</param>
        /// <param name="cachePathLength">The length of a string containing the cache path root folder.</param>
        /// <param name="titles">The titles of the files in fileNames.</param>
        private static void CreateClusterBTXMLFile(List<string> fileNames, HashSet<string>[] features,
            string clusterFile, string treeFile,
            string xmlFile, int k, int cachePathLength, List<string> titles)
        {
            // Read cluster file
            string clusterFileContent = Utils.ReadFileAsString(clusterFile);
            string[] clusterNumbers = RegExs.NEWLINE_REGEX.Split(clusterFileContent);

            bool hierarchical = !String.IsNullOrEmpty(treeFile);
            string[] parentNumbers = null;
            if (hierarchical)
            {
                // Read tree file
                string treeFileContent = Utils.ReadFileAsString(treeFile);
                parentNumbers = RegExs.NEWLINE_REGEX.Split(treeFileContent);
                // Remove additional info, only parent is relevant
                // Last line is empty and can be ignored
                for (int i = 0; i < parentNumbers.Length - 1; i++)
                {
                    parentNumbers[i] = parentNumbers[i].Substring(0, parentNumbers[i].IndexOf(' '));
                }
            }

            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.AppendChild(xmlDoc.CreateXmlDeclaration("1.0", "UTF-8", String.Empty));

            XmlElement rootXml = xmlDoc.CreateElement(CLUSTERS_BT_XML_NAME);
            xmlDoc.AppendChild(rootXml);
            rootXml.SetAttribute(CLUSTERS_BT_NUMBEROFCLUSTERS_XML_NAME, String.Empty + k);
            rootXml.SetAttribute(CLUSTERS_BT_HIERARCHICAL_XML_NAME, String.Empty + hierarchical);

            // Create a node for each cluster
            XmlElement[] clusters = new XmlElement[k];
            int[] sizes = new int[k];
            for (int i = 0; i < k; i++)
            {
                clusters[i] = xmlDoc.CreateElement(IndexServer.INDEX_SUBCATEGORY_XML_NAME);
                clusters[i].SetAttribute(IndexServer.INDEX_ID_XML_ATTR, String.Empty + i);
                // Save cluster features
                clusters[i].SetAttribute(IndexServer.INDEX_FEATURES_XML_ATTR,
                    String.Join(IndexServer.INDEX_FEATURES_JOIN_STRING, features[i]));
            }
            // Save each file in the corresponsing cluster
            for (int i = 0; i < fileNames.Count; i++)
            {
                // Determine relative file path and URI
                string uri = CacheManager.FilePathToUri(fileNames[i].Substring(cachePathLength));
                int clusterNumber = Int32.Parse(clusterNumbers[i]);
                if (clusterNumber == -1)
                {
                    // this file was put in no cluster
                    continue;
                }

                // Create an item with title, uri and snippet
                XmlElement itemElement = xmlDoc.CreateElement(IndexServer.ITEM_XML_NAME);
                clusters[clusterNumber].AppendChild(itemElement);

                XmlElement titleElement = xmlDoc.CreateElement(IndexServer.ITEM_TITLE_XML_NAME);
                itemElement.AppendChild(titleElement);
                titleElement.InnerText = titles[i];

                XmlElement uriElement = xmlDoc.CreateElement(IndexServer.ITEM_URL_XML_NAME);
                itemElement.AppendChild(uriElement);
                uriElement.InnerText = uri;

                XmlElement snippetElement = xmlDoc.CreateElement(IndexServer.ITEM_SNIPPET_XML_NAME);
                itemElement.AppendChild(snippetElement);
                snippetElement.InnerText = ""; // XXX: maybe we want to support this sometime

                // Increment size
                sizes[clusterNumber]++;
            }
            // Save sizes
            for (int i = 0; i < k; i++)
            {
                clusters[i].SetAttribute(IndexServer.INDEX_SIZE_XML_ATTR, String.Empty + sizes[i]);
            }
            // Add clusters flat or hierarchical to the document
            if (hierarchical)
            {
                // There are k-1 parents
                XmlElement[] parents = new XmlElement[k - 1];
                for (int i = 0; i < parents.Length; i++)
                {
                    parents[i] = xmlDoc.CreateElement(IndexServer.INDEX_CATEGORY_XML_NAME);
                    // Parent numbers start at k
                    parents[i].SetAttribute(IndexServer.INDEX_ID_XML_ATTR, String.Empty + (k + i));
                    // Save cluster features
                    parents[i].SetAttribute(IndexServer.INDEX_FEATURES_XML_ATTR,
                        String.Join(IndexServer.INDEX_FEATURES_JOIN_STRING, features[k + i]));
                }
                // Add leafs to the parents
                for (int i = 0; i < clusters.Length; i++)
                {
                    XmlElement elem = clusters[i];
                    int parentNum = Int32.Parse(parentNumbers[i]);
                    if (parentNum == -1)
                    {
                        // this is the/a root cluster
                        rootXml.AppendChild(elem);
                    }
                    else
                    {
                        // This is not a root cluster
                        parents[parentNum - k].AppendChild(elem);
                    }

                }
                // Arrange parents hierarchical
                for (int i = 0; i < parents.Length; i++)
                {
                    XmlElement elem = parents[i];
                    int parentNum = Int32.Parse(parentNumbers[i + k]);
                    if (parentNum == -1)
                    {
                        // this is the/a root cluster
                        rootXml.AppendChild(elem);
                    }
                    else
                    {
                        // This is not a root cluster
                        parents[parentNum - k].AppendChild(elem);
                    }
                }
            }
            else
            {
                foreach (XmlElement elem in clusters)
                {
                    rootXml.AppendChild(elem);
                }
            }
            xmlDoc.Save(xmlFile);
        }

        /// <summary>
        /// Creates a 3-level hiererchy xml file from the binary tree xml file.
        /// </summary>
        /// <param name="xmlFileName">The file where to store the result</param>
        /// <param name="xmlBTFileName">The file with the binary tree</param>
        /// <param name="maxCategories">The maximum number of categories.</param>
        private static void CreateClusterXMLFile(string xmlFileName, string xmlBTFileName, int maxCategories)
        {
            XmlDocument btDoc = new XmlDocument();
            btDoc.Load(new XmlTextReader(xmlBTFileName));

            XmlDocument newDoc = IndexServer.GetClustersXMLDocument(xmlFileName);
            lock (newDoc)
            {
                // Reset
                if (newDoc.DocumentElement != null)
                {
                    newDoc.DocumentElement.RemoveAll();
                }
                else
                {
                    newDoc.AppendChild(newDoc.CreateXmlDeclaration("1.0", "UTF-8", String.Empty));
                    newDoc.AppendChild(newDoc.CreateElement(IndexServer.INDEX_CATEGORIES_XML_NAME));
                }
                XmlElement newRootXml = newDoc.DocumentElement;

                XmlElement rootNode = (XmlElement)btDoc.DocumentElement.ChildNodes[0];
                // Find up to maxCategories categories
                List<XmlElement> categories = FindCategories(btDoc, rootNode, maxCategories);

                foreach (XmlElement categoryElement in categories)
                {
                    // Get all plain subcategories for each category
                    List<XmlElement> subCategories = FindSubCategories(categoryElement);
                    // Remove all childs from category
                    categoryElement.RemoveAllChilds();

                    // Add all subcategories
                    for (int i = 0; i < subCategories.Count; i++)
                    {
                        categoryElement.AppendChild(subCategories[i]);
                    }

                    // Add category to newRootXml
                    newRootXml.AppendChild(newDoc.ImportNode(categoryElement, true));
                }

                // Set timestamp for the new clusters.xml
                newRootXml.SetAttribute("time", "" + DateTime.Now.ToFileTime());

                // Save new xml
                newDoc.Save(xmlFileName);
            }
        }

        #region XML helpers

        /// <summary>
        /// Finds all plain subcategories in this category.
        /// </summary>
        /// <param name="parent">The category.</param>
        /// <returns>A list of all subcategories.</returns>
        private static List<XmlElement> FindSubCategories(XmlElement parent)
        {
            List<XmlElement> result = new List<XmlElement>();
            List<XmlElement> nodesToLookAt = new List<XmlElement>();
            nodesToLookAt.Add(parent);
            while (nodesToLookAt.Count != 0)
            {
                XmlElement currentNode = nodesToLookAt[0];
                nodesToLookAt.RemoveAt(0);
                if (currentNode.Name.Equals(IndexServer.INDEX_SUBCATEGORY_XML_NAME))
                {
                    result.Add(currentNode);
                }
                else
                {
                    foreach (XmlElement child in currentNode.ChildNodes)
                    {
                        nodesToLookAt.Add(child);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Traverses the binary tree and finds up to maxCategories categories.
        /// </summary>
        /// <param name="doc">The XmlDocument where the XmlElement originated. Will be used to create a new
        /// category for only leaf childs, if necessary.</param>
        /// <param name="xmlElement">The root xml element.</param>
        /// <param name="maxCategories">The maximum number of categories to find.</param>
        /// <returns>The list of category xml elements.</returns>
        private static List<XmlElement> FindCategories(XmlDocument doc, XmlElement xmlElement, int maxCategories)
        {
            List<XmlElement> categories = new List<XmlElement>();
            List<XmlElement> onlyLeafChilds = new List<XmlElement>();
            List<XmlElement> nodesToLookAt = new List<XmlElement>();
            nodesToLookAt.Add(xmlElement);

            // As long as we have nodes to look at and we have still space for categories or the
            // number of categories is not limited.
            while (nodesToLookAt.Count != 0 &&
                (maxCategories == 0 || nodesToLookAt.Count + categories.Count < maxCategories))
            {
                XmlElement node = nodesToLookAt[0];
                nodesToLookAt.RemoveAt(0);

                switch (NumberOfLeafChilds(node))
                {
                    case 0:
                        // Look at all childs (2)
                        foreach (XmlElement child in node.ChildNodes)
                        {
                            nodesToLookAt.Add(child);
                        }
                        break;
                    case 1:
                        // We have an only leaf child. We will make a category for all only leaf childs.
                        // If it is the first, we will have to decrement maxCategories.
                        if (onlyLeafChilds.Count == 0 && maxCategories != 0)
                        {
                            maxCategories--;
                        }
                        foreach (XmlElement child in node.ChildNodes)
                        {
                            if (child.Name.Equals(IndexServer.INDEX_SUBCATEGORY_XML_NAME))
                            {
                                // This is the only leaf child
                                onlyLeafChilds.Add(child);
                            }
                            else
                            {
                                // This is the child with other children. We will go further down here
                                nodesToLookAt.Add(child);
                            }
                        }

                        break;
                    default:
                        // We cannot go further down here
                        categories.Add(node);
                        break;
                }
            }
            // All remaining nodes to look at will be categories
            foreach (XmlElement node in nodesToLookAt)
            {
                categories.Add(node);
            }

            if (onlyLeafChilds.Count != 0)
            {
                // We will have to create a new category for the only leaf childs.
                XmlElement onlyLeafChildsElement = doc.CreateElement(IndexServer.INDEX_CATEGORY_XML_NAME);
                // It will id=-1 since it is not really a category.
                onlyLeafChildsElement.SetAttribute(IndexServer.INDEX_ID_XML_ATTR, IndexServer.INDEX_ONLY_LEAF_CHILDS_ID);
                onlyLeafChildsElement.SetAttribute(IndexServer.ITEM_TITLE_XML_NAME, IndexServer.INDEX_ONLY_LEAF_CHILDS_TITLE);
                // Add all only leaf childs
                foreach (XmlElement child in onlyLeafChilds)
                {
                    onlyLeafChildsElement.AppendChild(child);
                }
                // Add onlyLeafChildsElement to the categories
                categories.Add(onlyLeafChildsElement);
            }

            return categories;
        }

        /// <summary>
        /// Determines the number of children of the given node, which are leaves.
        /// </summary>
        /// <param name="node">The node</param>
        /// <returns>The number of leaf childs.</returns>
        private static int NumberOfLeafChilds(XmlElement node)
        {
            int num = 0;
            foreach (XmlElement child in node.ChildNodes)
            {
                if (child.Name.Equals(IndexServer.INDEX_SUBCATEGORY_XML_NAME))
                {
                    num++;
                }
            }
            return num;
        }

        #endregion
    }
}
