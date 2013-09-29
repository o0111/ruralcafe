using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using NClassifier;
using NClassifier.Bayesian;
using System.IO;
using ACrawler;
using Util;

namespace WindowsFormsApplication1
{
    /// <summary>
    /// Wrapper for an actual Classifier.
    /// </summary>
    public class Classifier
    {
        // The stop words
        public static readonly string[] STOP_WORDS;

        /// <summary>
        /// Static Constructor
        /// </summary>
        static Classifier()
        {
            STOP_WORDS = File.ReadAllLines("stopwords.txt");
        }


        // One bigger than threadN
        private int topicN;
        // The main dir (from MainWindow)
        private string mainDirectory;
        // rel to main dir
        private string topicFileName;
        // rel to main dir
        private string topicDir;
        // The actual classifier
        private BayesianClassifier classifier;

        public Classifier(int topicN, string mainDirectory)
        {
            this.topicN = topicN;
            this.mainDirectory = mainDirectory;
            this.topicFileName = "topic" + topicN + ".txt";
            this.topicDir = "" + topicN;

            IWordsDataSource wds = new SimpleWordsDataSource();
            this.classifier = new BayesianClassifier(wds);
            
            Train();
        }

        /// <summary>
        /// Trains the Classifier with the Seed Documents.
        /// </summary>
        private void Train()
        {
            for (int i = 0; true; i++)
            {
                string fileName = mainDirectory + topicDir + Path.DirectorySeparatorChar + i + ".txt";
                if (!File.Exists(fileName))
                {
                    break;
                }
                string fileContent = File.ReadAllText(fileName);
                // TODO as soon as DownloadSeed is not in PTTL any more and seedDocs are HTML, adapt ExtractUseableText.
                string useableText = ExtractUseableText(fileContent);

                if (i < Crawler.NUMBER_OF_LINKS_HALF)
                {
                    // it's a negative link
                    classifier.TeachNonMatch(useableText);
                }
                else
                {
                    // it's a positive link
                    classifier.TeachMatch(useableText);
                }
            }
        }

        /// <summary>
        /// Extracts useable text by  eleminating stopwords,
        /// and words occuring too often or too rare.
        /// </summary>
        /// <param name="text">The input text.</param>
        /// <returns>The useable text.</returns>
        private string ExtractUseableText(string text)
        {
            // Extract text from HTML
            //string usefulText = HtmlUtils.ExtractText(text);
            string usefulText = text.ToLower();
            // Remove stopwords
            foreach (string stopWord in STOP_WORDS)
            {
                usefulText = usefulText.Replace(" " + stopWord + " ", " ");
            }
            // TODO
            return usefulText;
        }

        /// <summary>
        /// Checks, if the given webpage is a match.
        /// </summary>
        /// <param name="text">The webpage's text contents. (Not HTML)</param>
        /// <returns>True, if the website is a match.</returns>
        public bool IsMatch(string text)
        {
            return classifier.IsMatch(ExtractUseableText(text));
        }

        /// <summary>
        /// Classifies the website with a value between 0 and 1.
        /// </summary>
        /// <param name="text">The webpage's text contents. (Not HTML)</param>
        /// <returns>A value between 0 and 1 indicating how much of a match the webiste is.</returns>
        public double Classify(string text)
        {
            return classifier.Classify(ExtractUseableText(text));
        }
    }
}
