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
        // XML name constants
        public const string CLUSTERS_XML_NAME = "clusters";
        public const string CLUSTERS_NUMBEROFCLUSTERS_XML_NAME = "numberOfClusters";
        public const string CLUSTERS_HIERARCHICAL_XML_NAME = "hierarchical";
        public const string PARENT_CLUSTER_XML_NAME = "category";
        public const string CLUSTER_XML_NAME = "subcategory";
        public const string CLUSTER_ID_XML_NAME = "id";
        public const string CLUSTER_FEATURES_XML_NAME = "title";
        public const string CLUSTER_SIZE_XML_NAME = "size";
        public const string CLUSTER_FEATURES_JOIN_STRING = ", ";
        public const string ITEM_XML_NAME = "item";
        public const string ITEM_URL_XML_NAME = "url";
        public const string ITEM_TITLE_XML_NAME = "title";
        public const string ITEM_SNIPPET_XML_NAME = "snippet";
        public const string INDEX_CATEGORIES_XML_NAME = "categories";
        public const string INDEX_LEVEL_XML_NAME = "level";
        public const string INDEX_ONLY_LEAF_CHILDS_TITLE = "Other";
        public const string INDEX_ONLY_LEAF_CHILDS_TROTRO_ID = "null";

        /// Regex's for docfile creation replacement
        private static readonly Regex newlineRegex = new Regex(@"\r\n|\n|\r");
        private static readonly Regex wordsStartingInvalidRegex = new Regex(@"\s\W\S*");
        private static readonly Regex httpStartingRegex = new Regex(@"(\shttp\S*)|(http\S*\s)", RegexOptions.IgnoreCase);
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
        /// A dictionary of the english language. Used as a whitelist for the clustering.
        /// </summary>
        private static HashSet<string> _dictionary = new HashSet<string>();

        /// <summary>
        /// Static Constructor. Fills the _dictionary.
        /// </summary>
        static Cluster()
        {
            FileInfo f = new FileInfo(DICT_PATH);
            using (FileStream fs = f.Open(FileMode.Open, FileAccess.Read))
            using (StreamReader r = new StreamReader(fs))
            {
                while (!r.EndOfStream)
                {
                    _dictionary.Add(r.ReadLine());
                }
            }
        }

        /// <summary>
        /// Creates one doc file containing all the given files. This file will be input for Doc2Mat.
        /// 
        /// Throws IOException if anything goes wrong.
        /// </summary>
        /// <param name="files">The text files to merge.</param>
        /// <param name="docFile">The docfile. Contains one file per line, text only.</param>
        public static void CreateDocFile(List<string> files, string docFile)
        {
            FileStream docFileStream = Utils.CreateFile(docFile);
            if (docFileStream == null)
            {
                throw new IOException("Could not create docFile.");
            }

            using (StreamWriter docFileWriter = new StreamWriter(docFileStream, Encoding.UTF8))
            {
                foreach (string file in files)
                {
                    // Read file
                    string content = Utils.ReadFileAsString(file);
                    // Format HTML
                    content = WebUtility.HtmlDecode(content);
                    // Extract text from Html
                    content = HtmlUtils.ExtractText(content);
                    // Remove newlines
                    content = newlineRegex.Replace(content, " ");
                    // Empty content if this is a redirect
                    if (RequestHandler.REDIR_REGEX.IsMatch(content))
                    {
                        content = String.Empty;
                    }
                    // Filter all words by our dictionary.
                    StringBuilder builder = new StringBuilder();
                    string[] words = content.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string word in words)
                    {
                        if (_dictionary.Contains(word.ToLower()))
                        {
                            builder.Append(word).Append(' ');
                        }
                    }
                    content = builder.ToString();
                    // Remove words not starting with a alphabetic char and URLs (not necessary if dict is used)
                    // content = wordsStartingInvalidRegex.Replace(content, "");
                    // content = httpStartingRegex.Replace(content, "");

                    // Write content in a new line to docfile
                    docFileWriter.WriteLine(content);
                }
            }
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
        public static HashSet<string>[] CreateClusters(string matFile, string clustersFile, int k,
            bool fulltree, string treefile, int catNFeatures, int subcatNFeatures)
        {
            int timeoutMS = 60000; // 1 minute

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

                if (vcluster.WaitForExit(timeoutMS) &&
                    outputWaitHandle.WaitOne(timeoutMS) &&
                    errorWaitHandle.WaitOne(timeoutMS))
                {
                    // Process completed. Check process.ExitCode here.
                    // Parse output
                    Console.Write(output);
                    if (vcluster.ExitCode != 0)
                    {
                        throw new Exception("vcluster failed with exitcode: " + vcluster.ExitCode);
                    }
                    string[] lines = newlineRegex.Split(output.ToString());
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
            for (int i = clusterlineIndex; i < clusterlineIndex + (2*k-1); i++)
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
                    if(!featuresPlusPercent[1].Equals("-1.$%"))
                    {
                        percent = Double.Parse(featuresPlusPercent[1].Substring(0, featuresPlusPercent[1].Length - 1));
                        sortedFeatures.Add(new KeyValuePair<double, string>(percent, feature));
                    }
                    
                }
                // Sort features by their percent value, descending
                sortedFeatures.Sort((pair1, pair2) => (int) (pair2.Key - pair1.Key));

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
        /// Creates an XML file containing all the cluster information.
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
        public static void CreateClusterXMLFile(List<string> fileNames, HashSet<string>[] features,
            string clusterFile, string treeFile,
            string xmlFile, int k, int cachePathLength)
        {
            // Read cluster file
            string clusterFileContent = Utils.ReadFileAsString(clusterFile);
            string[] clusterNumbers = newlineRegex.Split(clusterFileContent);

            bool hierarchical = !String.IsNullOrEmpty(treeFile);
            string[] parentNumbers = null;
            if (hierarchical)
            {
                // Read tree file
                string treeFileContent = Utils.ReadFileAsString(treeFile);
                parentNumbers = newlineRegex.Split(treeFileContent);
                // Remove additional info, only parent is relevant
                // Last line is empty and can be ignored
                for (int i = 0; i < parentNumbers.Length - 1; i++)
                {
                    parentNumbers[i] = parentNumbers[i].Substring(0, parentNumbers[i].IndexOf(' '));
                }
            }

            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.AppendChild(xmlDoc.CreateXmlDeclaration("1.0", "UTF-8", String.Empty));

            XmlElement rootXml = xmlDoc.CreateElement(CLUSTERS_XML_NAME);
            xmlDoc.AppendChild(rootXml);
            rootXml.SetAttribute(CLUSTERS_NUMBEROFCLUSTERS_XML_NAME, String.Empty + k);
            rootXml.SetAttribute(CLUSTERS_HIERARCHICAL_XML_NAME, String.Empty + hierarchical);

            // Create a node for each cluster
            XmlElement[] clusters = new XmlElement[k];
            int[] sizes = new int[k];
            for (int i = 0; i < k; i++)
            {
                clusters[i] = xmlDoc.CreateElement(CLUSTER_XML_NAME);
                clusters[i].SetAttribute(CLUSTER_ID_XML_NAME, String.Empty + i);
                // Save cluster features
                clusters[i].SetAttribute(CLUSTER_FEATURES_XML_NAME, 
                    String.Join(CLUSTER_FEATURES_JOIN_STRING, features[i]));
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
                XmlElement itemElement = xmlDoc.CreateElement(ITEM_XML_NAME);
                clusters[clusterNumber].AppendChild(itemElement);

                XmlElement titleElement = xmlDoc.CreateElement(ITEM_TITLE_XML_NAME);
                itemElement.AppendChild(titleElement);
                titleElement.InnerText = HtmlUtils.GetPageTitleFromFile(fileNames[i]);

                XmlElement uriElement = xmlDoc.CreateElement(ITEM_URL_XML_NAME);
                itemElement.AppendChild(uriElement);
                uriElement.InnerText = uri;

                XmlElement snippetElement = xmlDoc.CreateElement(ITEM_SNIPPET_XML_NAME);
                itemElement.AppendChild(snippetElement);
                snippetElement.InnerText = ""; // XXX: maybe we want to support this sometime

                // Increment size
                sizes[clusterNumber]++;
            }
            // Save sizes
            for (int i = 0; i < k; i++)
            {
                clusters[i].SetAttribute(CLUSTER_SIZE_XML_NAME, String.Empty + sizes[i]);
            }
            // Add clusters flat or hierarchical to the document
            if (hierarchical)
            {
                // There are k-1 parents
                XmlElement[] parents = new XmlElement[k - 1];
                for (int i = 0; i < parents.Length; i++)
                {
                    parents[i] = xmlDoc.CreateElement(PARENT_CLUSTER_XML_NAME);
                    // Parent numbers start at k
                    parents[i].SetAttribute(CLUSTER_ID_XML_NAME, String.Empty + (k + i));
                    // Save cluster features
                    parents[i].SetAttribute(CLUSTER_FEATURES_XML_NAME, 
                        String.Join(CLUSTER_FEATURES_JOIN_STRING, features[k + i]));
                }
                // Add leafs to the parents
                for(int i = 0; i < clusters.Length; i++)
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

        #region index serving

        /// <summary>
        /// Computes the 1st level in the hierarchy.
        /// </summary>
        /// <param name="clusterXMLFile">The path to clusters.xml</param>
        /// <param name="maxCategories">The maximum number of categories.</param>
        /// <param name="maxSubCategories">The maximum number of subcategories per category.</param>
        /// <returns>The index.xml string.</returns>
        public static string Level1Index(string clusterXMLFile, int maxCategories, int maxSubCategories)
        {
            XmlDocument clustersDoc = new XmlDocument();
            clustersDoc.Load(new XmlTextReader(clusterXMLFile));

            XmlDocument indexDoc = new XmlDocument();
            indexDoc.AppendChild(indexDoc.CreateXmlDeclaration("1.0", "UTF-8", String.Empty));

            XmlElement indexRootXml = indexDoc.CreateElement(INDEX_CATEGORIES_XML_NAME);
            indexDoc.AppendChild(indexRootXml);
            indexRootXml.SetAttribute(INDEX_LEVEL_XML_NAME, String.Empty + 1);

            // Check for root node
            if (clustersDoc.DocumentElement.ChildNodes.Count == 0)
            {
                throw new ArgumentException("No categories");
            }
            XmlElement rootNode = (XmlElement) clustersDoc.DocumentElement.ChildNodes[0];
            // Find up to maxCategories categories
            List<XmlElement> categories = FindCategories(clustersDoc, rootNode, maxCategories);
            foreach (XmlElement categoryElement in categories)
            {
                // Get all plain subcategories for each category
                List<XmlElement> subCategories = FindSubCategories(categoryElement);
                // Remove all childs from category
                categoryElement.RemoveAllChilds();
                // Add until maxSubCategories is reached (if it's != 0)
                for (int i = 0; i < subCategories.Count && (maxSubCategories == 0 || i < maxSubCategories); i++)
                {
                    categoryElement.AppendChild(subCategories[i]);
                }
                // For each subcategory we must remove all childs
                foreach (XmlElement subCategoryElement in categoryElement.ChildNodes)
                {
                    subCategoryElement.RemoveAllChilds();
                }
                // Add category to indexRootXml
                indexRootXml.AppendChild(indexDoc.ImportNode(categoryElement, true));
            }

            return indexDoc.InnerXml;
        }

        /// <summary>
        /// Computes the 2nd level in the hierarchy for a given category.
        /// </summary>
        /// <param name="clusterXMLFile">The path to clusters.xml</param>
        /// <param name="categoryId">The category id.</param>
        /// <param name="maxSubCategories">The maximum number of subcategories.</param>
        /// <param name="maxItems">The maximum number of items per subcategory.</param>
        /// <returns>The index.xml string.</returns>
        public static string Level2Index(string clusterXMLFile, string categoryId, int maxSubCategories, int maxItems)
        {
            XmlDocument clustersDoc = new XmlDocument();
            clustersDoc.Load(new XmlTextReader(clusterXMLFile));

            XmlDocument indexDoc = new XmlDocument();
            indexDoc.AppendChild(indexDoc.CreateXmlDeclaration("1.0", "UTF-8", String.Empty));

            XmlElement indexRootXml = indexDoc.CreateElement(INDEX_CATEGORIES_XML_NAME);
            indexDoc.AppendChild(indexRootXml);
            indexRootXml.SetAttribute(INDEX_LEVEL_XML_NAME, String.Empty + 2);

            XmlElement categoryElement = FindCategory(clustersDoc.DocumentElement, categoryId);
            if (categoryElement == null)
            {
                throw new ArgumentException("Could not find category with that id.");
            }
            // Get all plain subcategories.
            List<XmlElement> subCategories = FindSubCategories(categoryElement);
            // Remove all childs from category
            categoryElement.RemoveAllChilds();

            // Add until maxSubCategories is reached (if it's != 0)
            for (int i = 0; i < subCategories.Count && (maxSubCategories == 0 || i < maxSubCategories); i++)
            {
                categoryElement.AppendChild(subCategories[i]);
            }
            if (maxItems != 0)
            {
                // For each subcategory we might have to cut some elements
                foreach (XmlElement subCategoryElement in categoryElement.ChildNodes)
                {
                    for (int i = subCategoryElement.ChildNodes.Count - 1; i >= maxItems; i--)
                    {
                        subCategoryElement.RemoveChild(subCategoryElement.ChildNodes[i]);
                    }
                }
            }
            // Add category to indexRootXml
            indexRootXml.AppendChild(indexDoc.ImportNode(categoryElement, true));

            return indexDoc.InnerXml;
        }

        /// <summary>
        /// Computes the 3rd level in the hierarchy for a given category and subcategory.
        /// </summary>
        /// <param name="clusterXMLFile">The path to clusters.xml</param>
        /// <param name="categoryId">The category id.</param>
        /// <param name="subCategoryId">The subcategory id.</param>
        /// <param name="maxItems">The maximum number of items for the subcategory.</param>
        /// <returns>The index.xml string.</returns>
        public static string Level3Index(string clusterXMLFile, string categoryId, string subCategoryId, int maxItems)
        {
            XmlDocument clustersDoc = new XmlDocument();
            clustersDoc.Load(new XmlTextReader(clusterXMLFile));

            XmlDocument indexDoc = new XmlDocument();
            indexDoc.AppendChild(indexDoc.CreateXmlDeclaration("1.0", "UTF-8", String.Empty));

            XmlElement indexRootXml = indexDoc.CreateElement(INDEX_CATEGORIES_XML_NAME);
            indexDoc.AppendChild(indexRootXml);
            indexRootXml.SetAttribute(INDEX_LEVEL_XML_NAME, String.Empty + 3);

            XmlElement categoryElement, subCategoryElement;
            if (categoryId.Equals(INDEX_ONLY_LEAF_CHILDS_TROTRO_ID))
            {
                // This is an only leaf child subcategory
                categoryElement = clustersDoc.CreateElement(PARENT_CLUSTER_XML_NAME);
                // It will not have an id since it is not really a category.
                categoryElement.SetAttribute(ITEM_TITLE_XML_NAME, INDEX_ONLY_LEAF_CHILDS_TITLE);

                subCategoryElement = FindCategory(clustersDoc.DocumentElement, subCategoryId);
            }
            else
            {
                categoryElement = FindCategory(clustersDoc.DocumentElement, categoryId);
                if (categoryElement == null)
                {
                    throw new ArgumentException("Could not find category with that id.");
                }
                subCategoryElement = FindCategory(categoryElement, subCategoryId);
            }

            if (subCategoryElement == null)
            {
                throw new ArgumentException("Could not find subcategory with that id.");
            }
            if (maxItems != 0 && maxItems < subCategoryElement.ChildNodes.Count)
            {
                // We have to cut some items
                for (int i = subCategoryElement.ChildNodes.Count - 1; i >= maxItems; i--)
                {
                    subCategoryElement.RemoveChild(subCategoryElement.ChildNodes[i]);
                }
            }

            // Remove all childs
            categoryElement.RemoveAllChilds();
            // Add subcategory plain
            categoryElement.AppendChild(subCategoryElement);
            // Add category to indexRootXml
            indexRootXml.AppendChild(indexDoc.ImportNode(categoryElement, true));

            return indexDoc.InnerXml;
        }

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
                if (currentNode.Name.Equals(CLUSTER_XML_NAME))
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
        /// Finds the (sub)category with the given id in all children of the supplied parent.
        /// </summary>
        /// <param name="parent">The element to search.</param>
        /// <param name="id">The id to find.</param>
        /// <returns>The category with the given id or null.</returns>
        private static XmlElement FindCategory(XmlElement parent, string id)
        {
            if (parent.Name.Equals(CLUSTERS_XML_NAME) || parent.Name.Equals(PARENT_CLUSTER_XML_NAME)
                || parent.Name.Equals(CLUSTER_XML_NAME))
            {
                if (id.Equals(parent.GetAttribute(CLUSTER_ID_XML_NAME)))
                {
                    return parent;
                }
                // Recursively look through the children
                foreach (XmlElement child in parent.ChildNodes)
                {
                    XmlElement childResult = FindCategory(child, id);
                    if (childResult != null)
                    {
                        return childResult;
                    }
                }
            }
            return null;
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
                            if (child.Name.Equals(CLUSTER_XML_NAME))
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
                XmlElement onlyLeafChildsElement = doc.CreateElement(PARENT_CLUSTER_XML_NAME);
                // It will not have an id since it is not really a category.
                onlyLeafChildsElement.SetAttribute(ITEM_TITLE_XML_NAME, INDEX_ONLY_LEAF_CHILDS_TITLE);
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
                if (child.Name.Equals(CLUSTER_XML_NAME))
                {
                    num++;
                }
            }
            return num;
        }

        #endregion
    }
}
