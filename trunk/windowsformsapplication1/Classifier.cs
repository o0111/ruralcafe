using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using NClassifier;
using NClassifier.Bayesian;
using System.IO;
using ACrawler;

namespace WindowsFormsApplication1
{
    public class Classifier
    {
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
                string useableText = ExtractUseableText(fileContent);

                if (i < ACrawlerClass.NUMBER_OF_LINKS_HALF)
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
        /// Extracts useable text by getting only text HTML content, eleminating stopwords,
        /// and words occuring too often or too rare.
        /// </summary>
        /// <param name="text">The input text.</param>
        /// <returns>The useable text.</returns>
        public string ExtractUseableText(string text)
        {
            // TODO
            return text;
        }

        public void Bla()
        {
            IWordsDataSource wds = new SimpleWordsDataSource();
            BayesianClassifier classifier = new BayesianClassifier(wds);

            classifier.TeachMatch("This is a text about computer science");
            classifier.TeachNonMatch("This is an arbitrary useless text without meaning.");

            bool isMatch = classifier.IsMatch("Computer science has evolved a lot in the past.");
            double value = classifier.Classify("Computer science has evolved a lot in the past.");
        }

    }
}
