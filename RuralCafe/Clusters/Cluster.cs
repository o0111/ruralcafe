using RuralCafe.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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

            using (StreamWriter docFileWriter = new StreamWriter(docFileStream))
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
        /// <param name="k">The number of clusters to create.</param>
        public static void CreateClusters(string matFile, int k)
        {
            // vcluster.exe <matFile> k
            ProcessStartInfo clusterStartInfo = new ProcessStartInfo(VCLUSTERS_PATH);
            clusterStartInfo.Arguments = "\"" + matFile + "\" " + k; 

            clusterStartInfo.UseShellExecute = false;
            clusterStartInfo.RedirectStandardOutput = true;
            clusterStartInfo.RedirectStandardError = true;
            clusterStartInfo.CreateNoWindow = false;

            Process vcluster = new Process();
            vcluster.StartInfo = clusterStartInfo;
            vcluster.Start();
            vcluster.WaitForExit();
            if (vcluster.ExitCode != 0)
            {
                throw new Exception("PERL doc2mat failed with exitcode: " + vcluster.ExitCode);
            }
        }

        /// <summary>
        /// Creates an XML file containing all the cluster information.
        /// 
        /// Throws an Exception (e.g. FormatException or XmlException)
        /// if anything goes wrong.
        /// </summary>
        /// <param name="fileNames">A list of all files that have been clustered.</param>
        /// <param name="clusterFile">Cluto's result file.</param>
        /// <param name="xmlFile">The XML result will be stored in here.</param>
        /// <param name="k">The number of clusters</param>
        /// <param name="cachePathLength">The length of a string containing the cache path root folder.</param>
        public static void CreateClusterXMLFile(List<string> fileNames, string clusterFile, 
            string xmlFile, int k, int cachePathLength)
        {
            string clusterFileContent = Utils.ReadFileAsString(clusterFile);
            string[] clusterNumbers = newlineRegex.Split(clusterFileContent);

            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.AppendChild(xmlDoc.CreateXmlDeclaration("1.0", "UTF-8", ""));

            XmlElement clustersElement = xmlDoc.CreateElement("clusters");
            xmlDoc.AppendChild(clustersElement);

            // Create a node for each cluster
            XmlElement[] clusters = new XmlElement[k];
            int[] sizes = new int[k];
            for (int i = 0; i < k; i++)
            {
                clusters[i] = xmlDoc.CreateElement("cluster");
                clustersElement.AppendChild(clusters[i]);
                clusters[i].SetAttribute("number", "" + i);
            }
            // Save each file in the corresponsing cluster
            for (int i = 0; i < fileNames.Count; i++)
            {
                // Determine relative file path
                string uri = RCRequest.FilePathToUri(fileNames[i].Substring(cachePathLength));
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

            xmlDoc.Save(xmlFile);
        }
    }
}
