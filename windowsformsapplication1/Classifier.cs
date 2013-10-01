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
        // Constants
        public const int NUMBER_OF_WORDS_PER_DOCUMENT = 600;
        // The stop words
        public static readonly string[] STOP_WORDS;

        // The word frequencies
        private static Dictionary<string, long> wordFrequencies; 

        /// <summary>
        /// Static Constructor
        /// </summary>
        static Classifier()
        {
            STOP_WORDS = File.ReadAllLines("stopwords.txt");
            wordFrequencies = new Dictionary<string, long>();
            try
            {
                string[] entries = File.ReadAllLines("1gram_gt_500k.txt");
                foreach (string entry in entries)
                {
                    string[] splitEntry = entry.Split('\t');
                    wordFrequencies.Add(splitEntry[0], Int64.Parse(splitEntry[1]));
                }
            }
            catch (Exception) { }
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
            // Train with a third and test with rest
            Train(Crawler.NUMBER_OF_LINKS / 3);
            return Test(Crawler.NUMBER_OF_LINKS / 3);
        }

        private TestResults Test(int whereToStart)
        {
            TestResults results = new TestResults();
            // Positive
            for (int i = whereToStart; true; i++)
            {
                string fileName = mainDirectory + TopicDir + Path.DirectorySeparatorChar + i + ".txt";
                if (!File.Exists(fileName))
                {
                    break;
                }
                string fileContent = File.ReadAllText(fileName);
                string useableText = ExtractUseableText(fileContent);

                bool isMatch = classifier.IsMatch(useableText);
                // it's a positive link
                results.numberOfTestForPos++;
                if (!isMatch)
                {
                    results.falseNeg++;
                }
            }
            // Negative
            for (int i = whereToStart; true; i++)
            {
                string fileName = mainDirectory + "neg" + Path.DirectorySeparatorChar + i + ".txt";
                if (!File.Exists(fileName))
                {
                    break;
                }
                string fileContent = File.ReadAllText(fileName);
                string useableText = ExtractUseableText(fileContent);

                bool isMatch = classifier.IsMatch(useableText);
                // it's a negative link
                results.numberOfTestsForNeg++;
                if (isMatch)
                {
                    results.falsePos++;
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
            // Positive
            for (int i = 0; true; i++)
            {
                string fileName = mainDirectory + TopicDir + Path.DirectorySeparatorChar + i + ".txt";
                if (!File.Exists(fileName) || i >= trainCountPositive)
                {
                    break;
                }
                string fileContent = File.ReadAllText(fileName);
                string useableText = ExtractUseableText(fileContent);

                classifier.TeachMatch(useableText);
            }

            // Negative
            for (int i = 0; true; i++)
            {
                string fileName = mainDirectory + "neg" + Path.DirectorySeparatorChar + i + ".txt";
                if (!File.Exists(fileName) || i >= trainCountPositive)
                {
                    break;
                }
                string fileContent = File.ReadAllText(fileName);
                string useableText = ExtractUseableText(fileContent);

                classifier.TeachNonMatch(useableText);
            }
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
            // Remove punctuation chars.
            //usefulText = RegExs.PUNCTUATION_REGEX.Replace(usefulText, "");

            // Remove stopwords
            foreach (string stopWord in STOP_WORDS)
            {
                usefulText = usefulText.Replace(" " + stopWord + " ", " ");
            }

            // Use only frequent words
            usefulText = ExtractOnlyFrequentWords(usefulText);
            return usefulText;
        }

        private string ExtractOnlyFrequentWords(string text)
        {
            string[] words = RegExs.SPACES_REGEX.Split(text);
            Dictionary<string, int> frequencies = new Dictionary<string, int>();

            // Count the frequencies of all words.
            foreach (string word in words)
            {
                if (!frequencies.ContainsKey(word))
                {
                    frequencies.Add(word, 1);
                }
                else
                {
                    frequencies[word]++;
                }
            }

            // Sort all words by TfIdf.
            List<string> sortedWords = new List<string>(words);
            sortedWords.Sort((word1, word2) =>
                {
                    double tfIdf1 = wordFrequencies.ContainsKey(word1) ?
                        ((double)frequencies[word1]) / (words.Count() * wordFrequencies[word1]) : 0;
                    double tfIdf2 = wordFrequencies.ContainsKey(word2) ?
                        ((double)frequencies[word2]) / (words.Count() * wordFrequencies[word2]) : 0;
                    return tfIdf1.CompareTo(tfIdf2);
                });

            // TODO dups or no dups?
            return String.Join(" ",sortedWords.Take(NUMBER_OF_WORDS_PER_DOCUMENT));
        }
    }
}
