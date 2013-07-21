using RuralCafe.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;

namespace RuralCafe.Clusters
{
    /// <summary>
    /// Provides functionality to create clusters.
    /// </summary>
    public static class Cluster
    {
        /// <summary>
        /// Regex for newlines.
        /// </summary>
        private static readonly Regex newlineRegex = new Regex(@"\r\n|\n|\r");
        /// <summary>
        /// Path to vcluster.exe
        /// </summary>
        private static readonly string VCLUSTERS_PATH =
            Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar +
            "Clusters" + Path.DirectorySeparatorChar + "vcluster.exe";

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
                    // Extract text from Html
                    content = HtmlUtils.ExtractText(content);
                    // Remove newlines
                    content = newlineRegex.Replace(content, " ");
                    // Write content in a new line to docfile
                    docFileWriter.WriteLine(content);
                }
            }
        }

        /// <summary>
        /// Creates k clusters from a matfile using vcluster without any more parameters given.
        /// 
        /// Can throw various Exceptions.
        /// </summary>
        /// <param name="matFile">The matfile.</param>
        /// <param name="clustersFile">The clusters file.</param>
        /// <param name="k">The number of clusters to create.</param>
        /// <param name="fulltree">Wether a full hierarchical tree should be computed.</param>
        /// <param name="treefile">Only used if fulltree. Path to the treefile.</param>
        /// <returns>An array with string-lists. Each list contains the labels for the cluster.</returns>
        public static HashSet<string>[] CreateClusters(string matFile, string clustersFile, int k,
            bool fulltree, string treefile)
        {
            int timeoutMS = 60000; // 1 minute

            // vcluster.exe -clmethod=rbr [-fulltree -showtree -labeltree -treefile=<treefile>]
            //  -showfeatures -clustfile=<clustFile> <matFile> k
            ProcessStartInfo clusterStartInfo = new ProcessStartInfo(VCLUSTERS_PATH);
            clusterStartInfo.Arguments = "-clmethod=rbr ";
            clusterStartInfo.Arguments += fulltree ? "-fulltree -showtree -labeltree -cltreefile=\"" + treefile + "\" " : "";
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
                        outputWaitHandle.Set();
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
                        errorWaitHandle.Set();
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
                    if (vcluster.ExitCode != 0)
                    {
                        throw new Exception("vcluster failed with exitcode: " + vcluster.ExitCode);
                    }
                    // Parse output
                    string[] lines = newlineRegex.Split(output.ToString());
                    // Create result array
                    HashSet<string>[] result = new HashSet<string>[fulltree ? 2 * k - 1 : k];
                    // Fill with empty lists
                    for (int i = 0; i < result.Length; i++)
                    {
                        result[i] = new HashSet<string>();
                    }

                    // Parse leaf cluster features
                    ParseLeafClusterFeatures(lines, result, k);

                    if (fulltree)
                    {
                        // Parse non-leaf cluster features
                        ParseNonLeafClusterFeatures(lines, result, k);
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
        private static void ParseLeafClusterFeatures(string[] lines, HashSet<string>[] features, int k)
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
                for (int lineIndex = index; lineIndex < index + 2; lineIndex++)
                {
                    // Cut off "Descriptive|Discriminating:"
                    string featuresString = lines[lineIndex].Substring(lines[lineIndex].IndexOf(':') + 1);
                    PutFeaturesIntoSet(featuresString, features[i]);
                }
            }
        }

        /// <summary>
        /// Parses the non-leaf cluster features from the vcluster output.
        /// </summary>
        /// <param name="lines">The vcluster output, split into lines.</param>
        /// <param name="features">The list of features, which will be filled.</param>
        /// <param name="k">The number of clusters.</param>
        private static void ParseNonLeafClusterFeatures(string[] lines, HashSet<string>[] features, int k)
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
                PutFeaturesIntoSet(featureString, features[clusterNumber]);
            }
        }

        /// <summary>
        /// Puts all the features from the feature string into the set.
        /// </summary>
        /// <param name="featuresString">The features string.</param>
        /// <param name="features">The feature set.</param>
        private static void PutFeaturesIntoSet(string featuresString, HashSet<string> features)
        {
            string[] featuresStringArray = featuresString.Split(',');
            foreach (string featurePlusPercent in featuresStringArray)
            {
                string feature = featurePlusPercent.Split(new string[] { " ", "\t" },
                    StringSplitOptions.RemoveEmptyEntries)[0];
                features.Add(feature);
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
            xmlDoc.AppendChild(xmlDoc.CreateXmlDeclaration("1.0", "UTF-8", ""));

            XmlElement rootXml = xmlDoc.CreateElement("clusters");
            xmlDoc.AppendChild(rootXml);
            rootXml.SetAttribute("numberOfClusters", "" + k);
            rootXml.SetAttribute("hierarchical", "" + hierarchical);

            // Create a node for each cluster
            XmlElement[] clusters = new XmlElement[k];
            int[] sizes = new int[k];
            for (int i = 0; i < k; i++)
            {
                clusters[i] = xmlDoc.CreateElement("cluster");
                clusters[i].SetAttribute("number", "" + i);
                // Save cluster features
                clusters[i].SetAttribute("features", String.Join(",", features[i]));
            }
            // Save each file in the corresponsing cluster
            for (int i = 0; i < fileNames.Count; i++)
            {
                // Determine relative file path
                string uri = CacheManager.FilePathToUri(fileNames[i].Substring(cachePathLength));
                int clusterNumber = Int32.Parse(clusterNumbers[i]);
                if (clusterNumber == -1)
                {
                    // this file was put in no cluster
                    continue;
                }

                XmlElement uriElement = xmlDoc.CreateElement("uri");
                clusters[clusterNumber].AppendChild(uriElement);
                uriElement.InnerText = uri;
                // Increment size
                sizes[clusterNumber]++;
            }
            // Save sizes
            for (int i = 0; i < k; i++)
            {
                clusters[i].SetAttribute("size", "" + sizes[i]);
            }
            // Add clusters flat or hierarchical to the document
            if (hierarchical)
            {
                // There are k-1 parents
                XmlElement[] parents = new XmlElement[k - 1];
                for (int i = 0; i < parents.Length; i++)
                {
                    parents[i] = xmlDoc.CreateElement("cluster");
                    // Parent numbers start at k
                    parents[i].SetAttribute("number", "" + (k + i));
                    // Save cluster features
                    parents[i].SetAttribute("features", String.Join(",", features[k + i]));
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
    }
}
