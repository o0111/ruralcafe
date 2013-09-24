using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using HtmlAgilityPack;
using System.Net;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Web;
using GehtSoft.Collections;
using PorterStemmerAlgorithm;
using SVMTrainClass;
using WindowsFormsApplication1;
using System.Threading;

namespace ProcessTopicTopLinks
{
    public class ProcessTopicTopLinksClass
    {
        public RichTextBox textWindow;
        public Int64 CollectionTermCount;
        public string topicDirectory;
        public string topicFileName;
        public string directory;
        public ACrawlerWin MainWindow;
        public Dictionary<string, collectionDictionaryStruct> collectionTerms = new Dictionary<string, collectionDictionaryStruct>();
        public Dictionary<string, int> stopWords = new Dictionary<string, int>();
        public Dictionary<string, ulong> LDC = new Dictionary<string, ulong>();
        public ulong TotalN;
        public int downloadC;
        public svm_predict svmPredict = new svm_predict();

        public List<docVector> relevantDocVectors = new List<docVector>();


        public struct docVector
        {
            public List<string> termID;
            public List<double> termWeight;

        };

        public struct termStruct
        {
            public string termName;
            public long termFreq;
            public ulong LDC_df;
        };
        public struct collectionDictionaryStruct
        {
            public string termName;
            public Int64 termID;
            public Int64 termDF;
            public Int64 termCF;

        };

        public string cleanToken(string token)
        {
            string returnS = "";

            for (int i = 0; i < token.Length; i++)
            {
                if ((token[i] >= 'a' && token[i] <= 'z') || (token[i] >= 'A' && token[i] <= 'Z'))
                    returnS += "" + token[i];
            }
            returnS = returnS.ToLower();




            return returnS;

        }


        public void makeTrainTest()
        {
            List<string> topLinksList = new List<string>();
            List<string> tempList = new List<string>();
            List<string> tempListPos = new List<string>();
            Dictionary<string, termStruct> docTerms = new Dictionary<string, termStruct>();
            relevantDocVectors.Clear();

            termStruct tempTerm;
            collectionDictionaryStruct tempCollectionTerm;
            PorterStemmer pStem = new PorterStemmer();
            long docLength = 0;
            System.IO.StreamWriter fileW, testfileW;

            fileW = new System.IO.StreamWriter(topicDirectory + directory + "//" + "trainSet.txt");
            testfileW = new System.IO.StreamWriter(topicDirectory + directory + "//" + "testSet.txt");


            CollectionTermCount = 0;

            string line = "";
            int rkey = 0;

            try
            {
                System.IO.StreamReader file2 = new System.IO.StreamReader("stopwords.txt"); //stop words

                while ((line = file2.ReadLine()) != null)
                {
                    rkey++;
                    line = line.ToLower();
                    line = pStem.stemTerm(line);

                    if (!stopWords.ContainsKey(line))
                        stopWords.Add(line, rkey);
                }
                file2.Close();
            }
            catch (System.Exception e)
            {
                MainWindow.ShowMessageBox("Either stopwords.txt file is missing or it has incorrect format: " + e.Message);
            }

            try
            {
                System.IO.StreamReader file3 = new System.IO.StreamReader("1gram_gt_500K.txt"); //stop words
                line = "";
                TotalN = 0;
                while ((line = file3.ReadLine()) != null)
                {
                    line = line.ToLower();
                    string[] words = line.Split('	');
                    //  MessageBox.Show(words[0] + "  " + words[1]);

                    if (!LDC.ContainsKey(words[0]))
                        LDC.Add(words[0], Convert.ToUInt64(words[1]));


                    if (Convert.ToUInt64(words[1]) > TotalN)
                        TotalN = Convert.ToUInt64(words[1]);

                }
                file3.Close();
            }
            catch (System.Exception e)
            {
                MainWindow.ShowMessageBox("Either 1gram_gt_500K.txt file is missing or it has incorrect format: " + e.Message);
            }

            collectionTerms.Clear();

            System.IO.StreamReader file = new System.IO.StreamReader(topicDirectory + topicFileName);
            int rr = 0;
            while ((line = file.ReadLine()) != null)
            {
                line = "" + rr;
                if ((rr >= 30 && rr < 60))
                    tempListPos.Add(line);
                //else


                tempList.Add(line);

                rr++;
            }
            file.Close();

            for (int i = 0; i < tempListPos.Count; i++)
            {
                topLinksList.Add(tempListPos[i]);
            }

            for (int i = 0; i < tempList.Count; i++)
            {
                if ((i >= 0 && i < 30))
                {
                    topLinksList.Add(tempList[i]);
                }
            }


            // MessageBox.Show("" + topLinksList.Count);




            for (int i = 0; i < topLinksList.Count; i++)
            {
                file = new System.IO.StreamReader(topicDirectory + directory + "//" + topLinksList[i] + ".txt");
                docTerms.Clear();
                docLength = 0;
                while ((line = file.ReadLine()) != null)
                {
                    string[] tokens = line.Split(' ');
                    for (int j = 0; j < tokens.Length; j++)
                    {
                        //textWindow.Text += "\n" + tokens[j];
                        string cleanTT = cleanToken(tokens[j]);


                        ulong ldcScore = 0;
                        if (LDC.ContainsKey(cleanTT))
                        {
                            ldcScore = LDC[cleanTT];
                        }
                        //MessageBox.Show("  " + ldcScore);

                        cleanTT = pStem.stemTerm(cleanTT);
                        if (!stopWords.ContainsKey(cleanTT))
                        {
                            if (cleanTT.Length > 1)
                            {
                                if (docTerms.ContainsKey(cleanTT))
                                {
                                    tempTerm = docTerms[cleanTT];
                                    tempTerm.termFreq++;
                                    tempTerm.LDC_df += ldcScore;
                                    docTerms.Remove(cleanTT);
                                    docTerms.Add(cleanTT, tempTerm);
                                    //docTerms.Add(tokens[j],
                                }
                                else
                                {
                                    tempTerm = new termStruct();
                                    tempTerm.termName = cleanTT;
                                    tempTerm.termFreq = 1;
                                    tempTerm.LDC_df = ldcScore;

                                    docTerms.Add(cleanTT, tempTerm);
                                }
                                docLength++;
                            }
                        }


                    }
                }
                foreach (var pair in docTerms)
                {
                    tempTerm = docTerms[pair.Key.ToString()];



                    //    textWindow.Text += tempTerm.termName + " " + tempTerm.termFreq;
                    //    textWindow.Text += "\n";

                    if (collectionTerms.ContainsKey(tempTerm.termName))
                    {
                        tempCollectionTerm = collectionTerms[tempTerm.termName];
                        tempCollectionTerm.termDF++;
                        tempCollectionTerm.termCF += tempTerm.termFreq;
                        collectionTerms.Remove(tempTerm.termName);
                        collectionTerms.Add(tempTerm.termName, tempCollectionTerm);
                    }
                    else
                    {
                        tempCollectionTerm = new collectionDictionaryStruct();
                        CollectionTermCount++;
                        tempCollectionTerm.termID = CollectionTermCount;
                        tempCollectionTerm.termDF = 1;
                        tempCollectionTerm.termCF = tempTerm.termFreq;
                        tempCollectionTerm.termName = tempTerm.termName;
                        collectionTerms.Add(tempTerm.termName, tempCollectionTerm);
                    }

                }


            }


            MainWindow.SetRichText(".................... Training Focused Crawler for Topic " + directory + "\n");

            //makingTrainSet and TestSet
            for (int i = 0; i < topLinksList.Count; i++)
            {
                file = new System.IO.StreamReader(topicDirectory + directory + "//" + topLinksList[i] + ".txt");
                docTerms.Clear();
                docLength = 0;
                while ((line = file.ReadLine()) != null)
                {
                    string[] tokens = line.Split(' ');
                    for (int j = 0; j < tokens.Length; j++)
                    {
                        //textWindow.Text += "\n" + tokens[j];
                        string cleanTT = cleanToken(tokens[j]);
                        ulong ldcScore = 0;
                        if (LDC.ContainsKey(cleanTT))
                        {
                            ldcScore = LDC[cleanTT];
                        }
                        cleanTT = pStem.stemTerm(cleanTT);

                        if (!stopWords.ContainsKey(cleanTT))
                        {
                            if (cleanTT.Length > 1)
                            {
                                if (docTerms.ContainsKey(cleanTT))
                                {
                                    tempTerm = docTerms[cleanTT];
                                    tempTerm.termFreq++;
                                    tempTerm.LDC_df += ldcScore;
                                    docTerms.Remove(cleanTT);
                                    docTerms.Add(cleanTT, tempTerm);
                                    //docTerms.Add(tokens[j],
                                }
                                else
                                {
                                    tempTerm = new termStruct();
                                    tempTerm.termName = cleanTT;
                                    tempTerm.termFreq = 1;
                                    tempTerm.LDC_df = ldcScore;
                                    docTerms.Add(cleanTT, tempTerm);
                                }
                                docLength++;
                            }
                        }

                    }
                }

                int numberTrainDocs = (int)(topLinksList.Count * (double)0.50);
                if (i < numberTrainDocs)
                {
                    fileW.Write("+1 ");
                    testfileW.Write("-1 ");
                }
                else
                {
                    fileW.Write("-1 ");
                    testfileW.Write("-1 ");
                }

                docLength = 0;
                foreach (var pair in docTerms)
                {
                    tempTerm = docTerms[pair.Key.ToString()];
                    docLength += (long)tempTerm.termFreq;
                }

                docVector newDocVector = new docVector();
                newDocVector.termID = new List<string>();
                newDocVector.termWeight = new List<double>();
                foreach (var pair in docTerms)
                {

                    tempTerm = docTerms[pair.Key.ToString()];
                    tempCollectionTerm = collectionTerms[pair.Key.ToString()];

                    if (i < numberTrainDocs)
                    {
                        double setWeight = 0, setWeight2 = 0;
                        double IDF = 0;
                        double df = (double)((double)tempTerm.LDC_df / (double)tempTerm.termFreq);
                        IDF = (double)((double)(TotalN) / (double)(df));
                        IDF = Math.Log(IDF) / Math.Log(2);
                        double TF = 0;
                        TF = (double)((double)tempTerm.termFreq / (double)docLength);
                        setWeight = (double)(TF * IDF);
                        TF = (double)((double)tempTerm.termFreq);
                        setWeight2 = (double)(TF * IDF);

                        if (df > 0 && ((setWeight2 > 4 && df < 100000000) || (setWeight2 > 0.2 && df < 1000)))
                        {
                            newDocVector.termID.Add(pair.Key.ToString());
                            newDocVector.termWeight.Add(setWeight);
                        }
                    }
                }
                if (i < numberTrainDocs)
                    relevantDocVectors.Add(newDocVector);

                fileW.WriteLine("");
                testfileW.WriteLine("");

            }

            fileW.Close();
            testfileW.Close();

            MainWindow.SetRichText(".................... Training Focused Cralwer completed succesfully for topic " + directory + "\n");


            System.String[] svm_parameters = new System.String[7];
            svm_parameters[0] = "-s";
            svm_parameters[1] = "0";
            svm_parameters[2] = "-t";
            svm_parameters[3] = "0";
            svm_parameters[4] = "-c";
            svm_parameters[5] = "1";
            svm_parameters[6] = topicDirectory + directory + "/" + "trainSet.txt";



            svm_train svmObject = new svm_train();
            svmObject.runSVM(svm_parameters, topicDirectory + directory + "/");
        }

        public int isWebLinkRelevant(string url2, int threadN, System.IO.StreamWriter logFile)
        {
            int relevant = 0;
            Dictionary<string, termStruct> docTerms = new Dictionary<string, termStruct>();

            termStruct tempTerm;
            collectionDictionaryStruct tempCollectionTerm;
            PorterStemmer pStem = new PorterStemmer();
            int docLength = 0;
            int binaryFile = 0;

            long binaryDigit = 0;
            long totalDigit = 0;


            logFile.Write("-> checking binary or text file = " + "\n");
            logFile.Flush();
            try
            {
                using (StreamReader sr = new StreamReader(topicDirectory + directory + "/tempPage" + threadN + ".htm"))
                {
                    string line;

                    while ((line = sr.ReadLine()) != null)
                    {

                        for (int i = 0; i < line.Length; i++)
                        {
                            char ch = line[i];
                            int ascii = ch;
                            if (ascii > 127)
                            {
                                binaryDigit++;
                            }
                            totalDigit++;

                        }
                    }
                }
            }
            catch (Exception d)
            {

            }
            logFile.Write("<- checking binary or text file = " + "\n");
            logFile.Flush();

            double ratioD = (double)((double)binaryDigit / (double)totalDigit);
            //MessageBox.Show(topicDirectory + directory + "/tempPage" + threadN + ".htm" + "   " + ratioD);
            if (ratioD > 0.30)
                binaryFile = 1;
            else
                binaryFile = 0;



            if (binaryFile == 0)
            {
                try
                {

                    HtmlAgilityPack.HtmlDocument document = new HtmlAgilityPack.HtmlDocument();

                    logFile.Write("-> extracting viewable text using html agility pack = " + "\n");
                    logFile.Flush();
                    document.Load(topicDirectory + directory + "/tempPage" + threadN + ".htm");
                    using (System.IO.StreamWriter fileW = new System.IO.StreamWriter(topicDirectory + directory + "//" + "focus" + threadN + ".txt"))
                    {
                        fileW.WriteLine(ExtractViewableTextCleaned(document.DocumentNode));
                        //     MessageBox.Show(ExtractViewableTextCleaned(document.DocumentNode));
                        fileW.Close();
                    }
                    logFile.Write("<- extracting viewable text using html agility pack = " + "\n");
                    logFile.Flush();


                }
                catch (SystemException ex)
                {
                    logFile.Write("-> extracting viewable text using html agility pack (tryCatch) = " + "\n");
                    logFile.Flush();
                    using (System.IO.StreamWriter fileW = new System.IO.StreamWriter(topicDirectory + directory + "//" + "focus" + threadN + ".txt"))
                    {
                        fileW.WriteLine("");
                        fileW.Close();
                    }
                    logFile.Write("<- extracting viewable text using html agility pack (tryCatch) = " + "\n");
                    logFile.Flush();

                }

                logFile.Write("-> extractng tokens of focus file for similarity checking = " + "\n");
                logFile.Flush();
                System.IO.StreamReader file;
                System.IO.StreamWriter testfileW;
                string line = "";
                file = new System.IO.StreamReader(topicDirectory + directory + "//" + "focus" + threadN + ".txt");
                testfileW = new System.IO.StreamWriter(topicDirectory + directory + "//" + "testSet.txt");
                docTerms.Clear();
                docLength = 0;
                while ((line = file.ReadLine()) != null)
                {
                    string[] tokens = line.Split(' ');
                    for (int j = 0; j < tokens.Length; j++)
                    {
                        //textWindow.Text += "\n" + tokens[j];
                        string cleanTT = cleanToken(tokens[j]);

                        ulong ldcScore = 0;
                        if (LDC.ContainsKey(cleanTT))
                        {
                            ldcScore = LDC[cleanTT];
                        }

                        cleanTT = pStem.stemTerm(cleanTT);
                        if (!stopWords.ContainsKey(cleanTT))
                        {
                            if (cleanTT.Length > 1)
                            {
                                if (docTerms.ContainsKey(cleanTT))
                                {
                                    tempTerm = docTerms[cleanTT];
                                    tempTerm.termFreq++;
                                    tempTerm.LDC_df += ldcScore;

                                    docTerms.Remove(cleanTT);
                                    docTerms.Add(cleanTT, tempTerm);
                                    //docTerms.Add(tokens[j],
                                }
                                else
                                {
                                    tempTerm = new termStruct();
                                    tempTerm.termName = cleanTT;
                                    tempTerm.termFreq = 1;
                                    tempTerm.LDC_df = ldcScore;
                                    docTerms.Add(cleanTT, tempTerm);
                                }
                                docLength++;
                            }
                        }

                    }
                }

                testfileW.Write("+1 ");

                docVector newDocVector = new docVector();
                newDocVector.termID = new List<string>();
                newDocVector.termWeight = new List<double>();
                foreach (var pair in docTerms)
                {

                    tempTerm = docTerms[pair.Key.ToString()];
                    if (collectionTerms.ContainsKey(pair.Key.ToString()))
                    {
                        tempCollectionTerm = collectionTerms[pair.Key.ToString()];


                        double setWeight = 0, setWeight2 = 0;
                        double IDF = 0;
                        double df = (double)((double)tempTerm.LDC_df / (double)tempTerm.termFreq);
                        IDF = (double)((double)(TotalN) / (double)(df));
                        IDF = Math.Log(IDF) / Math.Log(2);
                        double TF = 0;
                        TF = (double)((double)tempTerm.termFreq / (double)docLength);
                        setWeight = (double)(TF * IDF);

                        TF = (double)((double)tempTerm.termFreq);
                        setWeight2 = (double)(TF * IDF);

                        if (df > 0 && ((setWeight2 > 4 && df < 100000000) || (setWeight2 > 0.2 && df < 1000)))
                        {
                            newDocVector.termID.Add(pair.Key.ToString());
                            newDocVector.termWeight.Add(setWeight);
                        }
                    }
                }

                testfileW.WriteLine("");

                testfileW.Close();
                file.Close();
                logFile.Write("<- extractng tokens of focus file for similarity checking = " + "\n");
                logFile.Flush();

                logFile.Write("-> checking similarity of webdocument = " + "\n");
                logFile.Flush();

                double OverallSimilarity = 0;
                double relevCon = 0;

                for (int i = 0; i < relevantDocVectors.Count; i++)
                {
                    docVector oldDocVector = relevantDocVectors[i];
                    double upperPart = 0;
                    double sumtfidf1 = 0;
                    double sumtfidf2 = 0;
                    for (int j = 0; j < oldDocVector.termID.Count; j++)
                    {
                        string term1 = oldDocVector.termID[j];
                        double tfidf1 = oldDocVector.termWeight[j];
                        for (int z = 0; z < newDocVector.termID.Count; z++)
                        {
                            string term2 = newDocVector.termID[z];

                            if (term1 == term2)
                            {
                                double tfidf2 = newDocVector.termWeight[z];
                                upperPart += (tfidf1 * tfidf2);
                                //  MessageBox.Show(term1 + " " + term2);
                            }
                        }
                        sumtfidf1 += tfidf1;
                    }
                    sumtfidf1 = Math.Sqrt(sumtfidf1);
                    for (int z = 0; z < newDocVector.termID.Count; z++)
                    {
                        string term2 = newDocVector.termID[z];
                        double tfidf2 = newDocVector.termWeight[z];
                        sumtfidf2 += tfidf2;
                    }
                    sumtfidf2 = Math.Sqrt(sumtfidf2);


                    if (oldDocVector.termID.Count > 0 && newDocVector.termID.Count > 0)
                    {
                        upperPart = (double)((double)upperPart / (double)(sumtfidf1 * sumtfidf2));
                        relevCon++;
                    }
                    else
                        upperPart = 0;

                    OverallSimilarity += upperPart;

                    //MessageBox.Show("" + upperPart + " " + sumtfidf1 + " " + sumtfidf2 + " " + oldDocVector.termID.Count);


                    if (i > 15)
                        break;

                }
                OverallSimilarity = (double)((double)OverallSimilarity / (double)relevCon);

                //  MessageBox.Show("" + OverallSimilarity + " " + relevCon);
                logFile.Write("<- checking similarity of webdocument = " + "\n");
                logFile.Flush();
                if (OverallSimilarity > 0.058)
                    relevant = 1;
                else
                    relevant = -1;
            }


            if (binaryFile == 1)
                relevant = -1;

            return relevant;
        }

        private Regex _removeRepeatedWhitespaceRegex = new Regex(@"(\s|\n|\r){2,}", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        public string ExtractViewableTextCleaned(HtmlNode node)
        {
            string textWithLotsOfWhiteSpaces = ExtractViewableText(node);
            return _removeRepeatedWhitespaceRegex.Replace(textWithLotsOfWhiteSpaces, " ");
        }

        public string ExtractViewableText(HtmlNode node)
        {
            StringBuilder sb = new StringBuilder();
            ExtractViewableTextHelper(sb, node);
            return sb.ToString();
        }

        private void ExtractViewableTextHelper(StringBuilder sb, HtmlNode node)
        {
            if (node.Name != "script" && node.Name != "style")
            {
                if (node.NodeType == HtmlNodeType.Text)
                {
                    AppendNodeText(sb, node);
                }

                foreach (HtmlNode child in node.ChildNodes)
                {
                    ExtractViewableTextHelper(sb, child);
                }
            }
        }

        private void AppendNodeText(StringBuilder sb, HtmlNode node)
        {
            try
            {
                string text = ((HtmlTextNode)node).Text;
                if (string.IsNullOrWhiteSpace(text) == false)
                {
                    sb.Append(text);

                    // If the last char isn't a white-space, add a white space
                    // otherwise words will be added ontop of each other when they're only separated by
                    // tags

                    if (text.EndsWith("\t") || text.EndsWith("\n") || text.EndsWith(" ") || text.EndsWith("\r"))
                    {
                        // We're good!
                    }
                    else
                    {
                        sb.Append(" ");
                    }
                }
            }
            catch (System.StackOverflowException)
            {
            }
        }

        public void downloadSeedDocs()
        {
            List<string> topLinksList = new List<string>();


            System.IO.StreamReader file = new System.IO.StreamReader(topicDirectory + topicFileName);
            string line;

            while ((line = file.ReadLine()) != null)
            {
                topLinksList.Add(line);
            }

            file.Close();
            System.IO.Directory.CreateDirectory(topicDirectory + directory);
            System.IO.Directory.CreateDirectory(topicDirectory + directory + "//webdocs");

            HtmlAgilityPack.HtmlDocument document = new HtmlAgilityPack.HtmlDocument();

            MainWindow.SetRichText("..................starting downloading seed documents for topic " + directory + "\n");

            for (int j = 0; j < topLinksList.Count; j++)
            {
                //     WebClient client = new WebClient();
                //     client.DownloadFile(topLinksList[j], topicDirectory + directory + "/tempPage" + j + ".html");

                if ((j >= 0 && j < 70))
                {
                    try
                    {
                        if (topLinksList[j].IndexOf(".pdf") != -1 || topLinksList[j].IndexOf(".jpeg") != -1 || topLinksList[j].IndexOf(".jpg") != -1 || topLinksList[j].IndexOf(".gif") != -1 || topLinksList[j].IndexOf(".wav") != -1 || topLinksList[j].IndexOf(".swf") != -1 || topLinksList[j].IndexOf(".avi") != -1 || topLinksList[j].IndexOf(".rar") != -1)
                        {
                            using (System.IO.StreamWriter fileW = new System.IO.StreamWriter(topicDirectory + directory + "//" + j + ".txt"))
                            {
                                fileW.WriteLine("");
                            }
                        }
                        else
                        {
                            MainWindow.SetRichText("Topic" + directory + "= downloading [URL=" + topLinksList[j] + "]\n");

                            WebRequest request = WebRequest.Create(topLinksList[j]);
                            request.Method = "GET";
                            WebResponse response = request.GetResponse();
                            Stream stream = response.GetResponseStream();
                            StreamReader reader = new StreamReader(stream);
                            string content = reader.ReadToEnd();
                            // MessageBox.Show(content);

                            document.LoadHtml(content);
                            using (System.IO.StreamWriter fileW = new System.IO.StreamWriter(topicDirectory + directory + "//" + j + ".txt"))
                            {
                                fileW.WriteLine(ExtractViewableTextCleaned(document.DocumentNode));
                            }
                        }

                        //  MainWindow.SetRichText("................successfully done.......................\n");
                    }
                    catch (SystemException ex)
                    {
                        MainWindow.SetRichText("could not download [error = " + ex.ToString() + "]\n");
                        //listBox.Items.Add("error =  " + j);
                        using (System.IO.StreamWriter fileW = new System.IO.StreamWriter(topicDirectory + directory + "//" + j + ".txt"))
                        {
                            fileW.WriteLine("");
                        }

                    }
                }
                //  if (j >= 19)
                //    break;
            }
            MainWindow.SetRichText(".............seed downloading complete successfully for topic " + directory + "\n");

            MainWindow.showSeedDocsFinish(directory);
            downloadC = 1;
        }

        private static bool TrySetSuppressScriptErrors(WebBrowser webBrowser, bool value)
        {
            FieldInfo field = typeof(WebBrowser).GetField("_axIWebBrowser2", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null)
            {
                object axIWebBrowser2 = field.GetValue(webBrowser);
                if (axIWebBrowser2 != null)
                {
                    axIWebBrowser2.GetType().InvokeMember("Silent", BindingFlags.SetProperty, null, axIWebBrowser2, new object[] { value });
                    return true;
                }
            }

            return false;
        }
    }
}
