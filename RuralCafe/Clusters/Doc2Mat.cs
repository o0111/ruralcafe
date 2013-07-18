using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace RuralCafe.Clusters
{
    /// <summary>
    /// Provides the functionality of creating a ClutoClustering-Matrix from a document.
    /// </summary>
    public static class Doc2Mat
    {
        /// <summary>
        /// Path to the doc2mat PERL script.
        /// </summary>
        private static readonly string DOC2MAT_PERL_SCRIPT =
            Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar +
            "Clusters" + Path.DirectorySeparatorChar + "doc2mat";

        /// <summary>
        /// Creates the matFile from a docfile. Currently uses PERL.
        /// 
        /// Can throw various Exceptions.
        /// </summary>
        /// <param name="docFile">The docfile.</param>
        /// <param name="matFile">The matfile.</param>
        public static void DoDoc2Mat(string docFile, string matFile)
        {
            // TODO re-implement doc2mat in c# ?

            int timeoutMS = 60000; // 1 minute

            // perl doc2mat -nostem <docFile> <matFile>
            ProcessStartInfo perlStartInfo = new ProcessStartInfo("perl");
            perlStartInfo.Arguments = "\"" + DOC2MAT_PERL_SCRIPT + "\"" + " -nostem " +
                "\"" + docFile + "\" \"" + matFile + "\"";
            
            perlStartInfo.UseShellExecute = false;
            perlStartInfo.RedirectStandardOutput = true;
            perlStartInfo.RedirectStandardError = true;
            perlStartInfo.CreateNoWindow = false;

            Process perl = new Process();
            perl.StartInfo = perlStartInfo;
            perl.Start();
            if (perl.WaitForExit(timeoutMS))
            {
                if (perl.ExitCode != 0)
                {
                    throw new Exception("PERL doc2mat failed with exitcode: " + perl.ExitCode);
                }
            }
            else
            {
                throw new Exception("PERL doc2mat timed out.");
            }
            
        }
    }
}
