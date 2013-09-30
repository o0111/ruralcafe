using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using NClassifier;
using NClassifier.Bayesian;
using System.IO;
using Util;

namespace Crawler
{
    /// <summary>
    /// A  data struct for test results.
    /// </summary>
    public struct TestResults
    {
        public int numberOfTestForPos;
        public int numberOfTestsForNeg;
        public int falsePos;
        public int falseNeg;
        public double accuracy;

        public override string ToString()
        {
            return String.Format("\tPos Tests: {0}\n\tNeg Tests: {1}\n\tFalse Pos: {2}"
                + "\n\tFalse Neg: {3}\n\tAccuracy: {4:0.00}", numberOfTestForPos, numberOfTestsForNeg, falsePos,
                falseNeg, accuracy);
        }
    }

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

        // The topic number
        private int topicN;
        // The main dir (from MainWindow)
        private string mainDirectory;
        // The actual classifier
        private BayesianClassifier classifier;

        // rel to main dir
        public string TopicFileName
        {
            get;
            private set;
        }
        // rel to main dir
        public string TopicDir
        {
            get;
            private set;
        }

        public Classifier(int topicN, string mainDirectory)
        {
            this.topicN = topicN;
            this.mainDirectory = mainDirectory;
            this.TopicFileName = "topic" + topicN + ".txt";
            this.TopicDir = "" + topicN;

            IWordsDataSource wds = new SimpleWordsDataSource();
            this.classifier = new BayesianClassifier(wds);
        }

        /// <summary>
        /// Trains the Classifier with all Seed Documents.
        /// </summary>
        public void Train()
        {
            Train(0);
        }

        public TestResults TrainTest()
        {
            Train(Crawler.NUMBER_OF_LINKS_HALF / 2);
            return Test(Crawler.NUMBER_OF_LINKS_HALF / 2);
        }

        private TestResults Test(int whereToStart)
        {
            TestResults results = new TestResults();
            for (int i = 0; true; i++)
            {
                string fileName = mainDirectory + TopicDir + Path.DirectorySeparatorChar + i + ".txt";
                if (!File.Exists(fileName))
                {
                    break;
                }
                // Continue if this document is not to be used for testing.
                if (i % Crawler.NUMBER_OF_LINKS_HALF < whereToStart)
                {
                    continue;
                }
                // TODO this will possibly contain HTML now...
                string fileContent = File.ReadAllText(fileName);
                string useableText = ExtractUseableText(fileContent);

                bool isMatch = classifier.IsMatch(useableText);

                if (i < Crawler.NUMBER_OF_LINKS_HALF)
                {
                    // it's a negative link
                    results.numberOfTestsForNeg++;
                    if (isMatch)
                    {
                        results.falsePos++;
                    }
                }
                else
                {
                    // it's a positive link
                    results.numberOfTestForPos++;
                    if (!isMatch)
                    {
                        results.falseNeg++;
                    }
                }
            }
            // Calculate accuracy
            int testsTotal = results.numberOfTestForPos + results.numberOfTestsForNeg;
            results.accuracy = ((double)(testsTotal - results.falseNeg - results.falsePos))/testsTotal;

            return results;
        }

        /// <summary>
        /// Trains the Classifier with trainCountPositive positive and negative seed documents.
        /// </summary>
        /// <param name="trainCountPositive">The number of positive and negative documents to train the Classifier with.</param>
        private void Train(int trainCountPositive)
        {
            for (int i = 0; true; i++)
            {
                string fileName = mainDirectory + TopicDir + Path.DirectorySeparatorChar + i + ".txt";
                if (!File.Exists(fileName))
                {
                    break;
                }
                // Continue if this document is not to be used for training.
                if (trainCountPositive != 0 && i % Crawler.NUMBER_OF_LINKS_HALF >= trainCountPositive)
                {
                    continue;
                }
                string fileContent = File.ReadAllText(fileName);
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
            // Lower case
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
