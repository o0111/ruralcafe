using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
using System.Net;
using System.IO;
using GehtSoft.Collections;
using System.Windows.Forms;
using WindowsFormsApplication1;
using ProcessTopicTopLinks;
using System.Threading;

namespace ACrawler
{
    class ACrawlerClass
    {
        public RichTextBox textWindow;
        public ACrawlerWin MainWindow;
        public ProcessTopicTopLinksClass pttlObject;
        public List<string> toCrawlList = new List<string>();
        public List<string> seedBank = new List<string>();
        public int seedBankPointer;
        public List<string> relevantDownload = new List<string>();
        public Dictionary<string, List<string>> inCrawList = new Dictionary<string, List<string>>();
        public int count;
        public int totalDownload;
        public int threadActive;
        //    public Mutex mutex = new Mutex();
        public System.IO.StreamWriter fileW;// = new System.IO.StreamWriter(topicDirectory + directory + "//" + j + ".txt")
        public System.IO.StreamWriter logFile, logTrueClassification, logFalseClassification;


        public void addSeedDocs()
        {
            // string parentUrl;
            // toCrawlList.Add("first");
            //  toCrawlList.Add("second");
            //  parentUrl = toCrawlList[0];
            //  MessageBox.Show(parentUrl);
            seedBank.Clear();


            System.IO.StreamReader file = new System.IO.StreamReader(pttlObject.topicDirectory + pttlObject.topicFileName);
            string line;
            int ccc = 0;


            while ((line = file.ReadLine()) != null)
            {
                ccc++;
                if (ccc >= 30)
                {
                    if ((line.Contains("facebook") == true) || (line.Contains("youtube") == true) || (line.Contains(".pdf") == true) || (line.Contains(".jpg") == true) || (line.Contains(".gif") == true) || (line.Contains(".jpeg") == true) || (line.Contains(".PDF") == true) || (line.Contains(".pdf") == true) || (line.Contains(".ppt") == true) || (line.Contains(".PPT") == true))
                    { }
                    else
                        seedBank.Add(line);
                }

            }
            file.Close();


            seedBankPointer = 0;
            for (int i = 0; i < seedBank.Count; i++)
            {
                line = seedBank[i];
                if (inCrawList.ContainsKey(line))
                {
                }
                else
                {
                    if ((line.Contains("facebook") == true) || (line.Contains("youtube") == true) || (line.Contains(".pdf") == true) || (line.Contains(".jpg") == true) || (line.Contains(".gif") == true) || (line.Contains(".jpeg") == true))
                    {
                    }
                    else
                    {
                        toCrawlList.Add(line);

                        List<string> backLinks = new List<string>();
                        backLinks.Add("#seedDoc");
                        inCrawList.Add(line, backLinks);
                        //  break;
                    }
                }
                seedBankPointer++;
                if (seedBankPointer >= 20)
                    break;

            }

        }

        public void startCrawling(object ob)
        {
            int threadN = (int)ob;
            totalDownload = 0;
            count = 0;

            RBTree<string> linksTree = new RBTree<string>();
            List<string> linksList = new List<string>();
            List<string> topLinksList = new List<string>();
            string parentUrl = "";
            string line;

            WebClient client = new WebClient();

            HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
            logFile = new System.IO.StreamWriter(pttlObject.topicDirectory + pttlObject.directory + "//webdocs//" + "systemLog.txt");
            logTrueClassification = new System.IO.StreamWriter(pttlObject.topicDirectory + pttlObject.directory + "//webdocs//" + "classificationTrueLog.txt");
            logFalseClassification = new System.IO.StreamWriter(pttlObject.topicDirectory + pttlObject.directory + "//webdocs//" + "classificationFalseLog.txt");

            while (true)
            {
                try
                {
                    parentUrl = toCrawlList[0];
                    toCrawlList.RemoveAt(0);

                    if (count % 4 == 0)
                        MainWindow.SetUrlText(pttlObject.directory);

                    logFile.Close();
                    logFile = new System.IO.StreamWriter(pttlObject.topicDirectory + pttlObject.directory + "//webdocs//" + "systemLog.txt");

                    MainWindow.SetUrlChecking(parentUrl);

                    logFile.Write("-> Download and saving file in tempPage = " + parentUrl + "\n");
                    logFile.Flush();
                    totalDownload++;
                    client.DownloadFile(parentUrl, pttlObject.topicDirectory + pttlObject.directory + "/tempPage" + threadN + ".htm");
                    logFile.Write("<- Download and saving file in tempPage = " + parentUrl + "\n");
                    logFile.Flush();

                    MainWindow.SetRichText("checking relevance of [url=" + parentUrl + "]\n");


                    logFile.Write("-> checking web page relevance = " + parentUrl + "\n");
                    logFile.Flush();
                    int relResult = pttlObject.isWebLinkRelevant(parentUrl, threadN, logFile);
                    logFile.Write("<- checking web page relevance = " + parentUrl + "\n");
                    logFile.Flush();

                    if (relResult == -1)
                    {
                        logFalseClassification.Write(parentUrl + "\n");
                        MainWindow.SetRichText("done...[" + "not relevant" + "]" + "\n");
                    }
                    else
                    {
                        logTrueClassification.Write(parentUrl + "\n");

                        count++;

                        relevantDownload.Add(parentUrl);

                        logFile.Write("-> Downloading relevant document in archieve " + parentUrl + "\n");
                        logFile.Flush();
                        client.DownloadFile(parentUrl, pttlObject.topicDirectory + pttlObject.directory + "//webdocs//" + count + ".html");
                        // Satia: run the delegate if it is not null.
                        if (MainWindow.processUriDelegate != null)
                        {
                            MainWindow.processUriDelegate(parentUrl);
                        }

                        logFile.Write("<- Downloading relevant document in archieve " + parentUrl + "\n");
                        logFile.Flush();

                        MainWindow.SetRichText("done...[" + "relevant" + "]" + "\n");

                        string mainDomain = "";
                        int cc = -1;

                        logFile.Write("-> domain name extraction" + "\n");
                        logFile.Flush();
                        if (parentUrl.Length > 2)
                        {
                            for (int k = parentUrl.Length - 1; k >= 0; k--)
                            {
                                if (parentUrl[k] == '/')
                                {
                                    cc = k;
                                    break;
                                }
                                // mainDomain += "" + parentUrl[k];
                            }

                            if (cc != -1)
                                mainDomain = "" + parentUrl.Substring(0, cc);
                        }

                        logFile.Write("<- domain name extraction" + "\n");
                        logFile.Flush();

                        logFile.Write("-> downloading document for hyperliks extraction (tempPage)" + "\n");
                        logFile.Flush();
                        doc.Load(pttlObject.topicDirectory + pttlObject.directory + "/tempPage" + threadN + ".htm");
                        logFile.Write("<- downloading document for hyperliks extraction (tempPage)" + "\n");
                        logFile.Flush();

                        logFile.Write("-> hyperlinks extraction (tempPage)" + "\n");
                        logFile.Flush();
                        foreach (HtmlNode link in doc.DocumentNode.SelectNodes("//a[@href]"))
                        {

                            int found = 0;
                            try
                            {
                                linksTree.Add(link.Attributes["href"].Value.ToString());
                            }
                            catch (System.ArgumentException)
                            {
                                found = 1;
                            }
                            if (found == 0)
                                linksList.Add(link.Attributes["href"].Value.ToString());
                        }
                        logFile.Write("<- hyperlinks extraction (tempPage)" + "\n");
                        logFile.Flush();

                        logFile.Write("-> hyperlinks saving in data structures" + "\n");
                        logFile.Flush();
                        for (int i = 0; i < linksList.Count; i++)
                        {

                            string checkD = "";
                            if (linksList[i].Contains("://") == false)
                                checkD = mainDomain + '/' + linksList[i];
                            else
                                checkD = linksList[i];

                            //   mutex.WaitOne();
                            if (inCrawList.ContainsKey(checkD) || (checkD.Contains("youtube") == true) || (checkD.Contains("facebook") == true) || (checkD.Contains("twitter") == true) || (checkD.Contains(".pdf") == true) || (checkD.Contains(".jpg") == true) || (checkD.Contains(".gif") == true) || (checkD.Contains(".jpeg") == true))
                            {
                                if (inCrawList.ContainsKey(checkD))
                                {
                                    List<string> backLinks = inCrawList[checkD];
                                    inCrawList.Remove(checkD);
                                    backLinks.Add(parentUrl);
                                    inCrawList.Add(checkD, backLinks);
                                }
                            }
                            else
                            {
                                toCrawlList.Add(checkD);
                                List<string> backLinks = new List<string>();
                                backLinks.Add(parentUrl);
                                inCrawList.Add(checkD, backLinks);

                                //  if ( toCrawlList.Count%30 == 0 )
                                //    MainWindow.SetUrlText();
                            }
                            //    mutex.ReleaseMutex();
                        }


                        linksList.Clear();
                        linksTree.Clear();
                        logFile.Write("<- hyperlinks saving in data structures" + "\n");
                        logFile.Flush();

                    }
                    // File.Delete("c:/crawler/temp/tempPage" + threadN + ".htm");
                }
                catch (SystemException ex)
                {

                    // MessageBox.Show(parentUrl + "   " + ex.ToString() + "  " + threadN);
                }

                if (toCrawlList.Count == 0)
                {
                    //    MessageBox.Show("Crawling completed successfully " + seedBankPointer + "  " + count + " " + seedBank.Count);
                    if (count < 500 && seedBankPointer < seedBank.Count)
                    {
                        logFile.Write("-> getting more seed documents" + "\n");
                        logFile.Flush();
                        int iii = 0;
                        while (true)
                        {
                            line = seedBank[seedBankPointer];
                            if (inCrawList.ContainsKey(line))
                            {
                            }
                            else
                            {
                                if ((line.Contains("facebook") == true) || (line.Contains("youtube") == true) || (line.Contains(".pdf") == true) || (line.Contains(".jpg") == true) || (line.Contains(".gif") == true) || (line.Contains(".jpeg") == true))
                                {
                                }
                                else
                                {
                                    toCrawlList.Add(line);

                                    List<string> backLinks = new List<string>();
                                    backLinks.Add("#seedDoc");
                                    inCrawList.Add(line, backLinks);
                                    //  break;
                                }
                            }
                            iii++;
                            if (iii > 20)
                                break;

                            seedBankPointer++;
                            if (seedBankPointer >= seedBank.Count)
                                break;
                        }
                        logFile.Write("<- getting more seed documents" + "\n");
                        logFile.Flush();
                    }
                    else
                        break;
                }

                if (totalDownload > 100)
                {
                    logFile.Write("-> switching threads" + "\n");
                    logFile.Flush();
                    fileW = new System.IO.StreamWriter(pttlObject.topicDirectory + pttlObject.directory + "//webdocs//" + "DocsUrlNames.txt");
                    for (int i = 0; i < relevantDownload.Count; i++)
                    {
                        string strUrl = relevantDownload[i];
                        List<string> backLinks = inCrawList[strUrl];
                        fileW.Write(strUrl + "\n");
                        for (int j = 0; j < backLinks.Count; j++)
                        {
                            string strBack = backLinks[j];
                            fileW.Write(strBack + "\n");
                        }
                        fileW.Write("-17\n");
                    }
                    fileW.Close();
                    MainWindow.suspendRunAnotherThread(threadN);
                    logFile.Write("<- switching threads" + "\n");
                    logFile.Flush();
                }


                if (count == 500)
                {
                    //  MessageBox.Show("Crawling counter completed successfully");
                    break;
                }
                //  MessageBox.Show("Hello");
            }

            logFile.Write("-> thread finish but running another thread" + "\n");
            logFile.Flush();
            fileW = new System.IO.StreamWriter(pttlObject.topicDirectory + pttlObject.directory + "//webdocs//" + "DocsUrlNames.txt");
            for (int i = 0; i < relevantDownload.Count; i++)
            {
                string strUrl = relevantDownload[i];
                List<string> backLinks = inCrawList[strUrl];
                fileW.Write(strUrl + "\n");
                for (int j = 0; j < backLinks.Count; j++)
                {
                    string strBack = backLinks[j];
                    fileW.Write(strBack + "\n");
                }
                fileW.Write("-17\n");
            }
            fileW.Close();
            logTrueClassification.Close();
            logFalseClassification.Close();
            MainWindow.runAnotherThread(threadN);
            logFile.Write("<- thread finish but running another thread" + "\n");
            logFile.Flush();
        }
    }
}
