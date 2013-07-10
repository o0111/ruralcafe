using RuralCafe.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

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
        private static readonly Regex newlineRegex = new Regex(@"\n|\r|\r\n");
        /// <summary>
        /// Path to vcluster.exe
        /// </summary>
        private static readonly string VCLUSTERS_PATH =
            Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar +
            "Clusters" + Path.DirectorySeparatorChar + "vcluster.exe";

        /// <summary>
        /// Creates one doc file containing all the given files. This file will be input for Doc2Mat.
        /// </summary>
        /// <param name="cachePathLength">The length of a string containing the cache path root folder.</param>
        /// <param name="files">The text files to merge.</param>
        /// <param name="docFile">The docfile. Contains one file per line, text only.</param>
        public static void CreateDocFile(int cachePathLength, List<string> files, string docFile)
        {
            StreamWriter docFileWriter = new StreamWriter(Utils.CreateFile(docFile));

            foreach (string file in files)
            {
                // Read file
                string content = Utils.ReadFileAsString(file);
                // Extract text from Html
                content = HtmlUtils.ExtractText(content);
                // Remove newlines
                content = newlineRegex.Replace(content, " ");
                // Determine relative file path
                string relFilePath = file.Substring(cachePathLength);
                // Write URI and space
                // FIXME original Doc2Mat eliminates dots first.
                // So this does not work. When we have own re-implementation
                // this could work.
                docFileWriter.Write(RCRequest.FilePathToUri(relFilePath) + " ");
                // Write content in a new line to docfile
                docFileWriter.WriteLine(content);
            }
            docFileWriter.Close();
        }

        /// <summary>
        /// Creates k clusters from a matfile using vcluster without any more parameters given.
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
            Console.WriteLine(vcluster.StandardOutput.ReadToEnd());
        }
    }
}
