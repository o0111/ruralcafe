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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Reflection;

namespace ACrawler
{
    [JsonObject(MemberSerialization.OptIn)]
    public class ACrawlerClass
    {
        public RichTextBox textWindow;
        public ACrawlerWin MainWindow;
        public ProcessTopicTopLinksClass pttlObject;
        [JsonProperty]
        public List<string> toCrawlList = new List<string>();
        [JsonProperty]
        public List<string> seedBank = new List<string>();
        [JsonProperty]
        public int seedBankPointer;
        [JsonProperty]
        public List<string> relevantDownload = new List<string>();
        [JsonProperty]
        public Dictionary<string, List<string>> inCrawList = new Dictionary<string, List<string>>();
        [JsonProperty]
        public int count;
        [JsonProperty]
        public int totalDownload;
        [JsonProperty]
        public int threadActive;

        // The thread number
        private int threadN;
        // The interrupted flag
        public volatile bool interrupted = false;

        public void addSeedDocs()
        {
            seedBank.Clear();
            string line;

            using (StreamReader file = new StreamReader(pttlObject.topicDirectory + pttlObject.topicFileName))
            {
                int ccc = 0;

                while ((line = file.ReadLine()) != null)
                {
                    ccc++;
                    if (ccc >= 30 && !((line.Contains("facebook")) || (line.Contains("youtube")) || (line.Contains(".pdf")) || (line.Contains(".jpg")) ||
                            (line.Contains(".gif")) || (line.Contains(".jpeg")) || (line.Contains(".PDF")) || (line.Contains(".pdf")) ||
                            (line.Contains(".ppt")) || (line.Contains(".PPT"))))
                    {
                        seedBank.Add(line);
                    }

                }
            }

            seedBankPointer = 0;
            for (int i = 0; i < seedBank.Count; i++)
            {
                line = seedBank[i];
                if (!inCrawList.ContainsKey(line) && !((line.Contains("facebook")) || (line.Contains("youtube")) || (line.Contains(".pdf"))
                    || (line.Contains(".jpg")) || (line.Contains(".gif")) || (line.Contains(".jpeg"))))
                {
                    toCrawlList.Add(line);

                    List<string> backLinks = new List<string>();
                    backLinks.Add("#seedDoc");
                    inCrawList.Add(line, backLinks);
                    //  break;
                }
                
                seedBankPointer++;
                if (seedBankPointer >= 20)
                {
                    break;
                }
            }
        }

        public void startCrawling(object ob)
        {
            this.threadN = (int)ob;
            totalDownload = 0;
            count = 0;

            // Load the state from a previous run.
            LoadState();

            RBTree<string> linksTree = new RBTree<string>();
            List<string> linksList = new List<string>();
            string parentUrl = "";
            string line;

            WebClient client = new WebClient();

            HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();

            using (StreamWriter logFile = new StreamWriter(pttlObject.topicDirectory + pttlObject.directory + "//webdocs//" + "systemLog.txt"))
            using (StreamWriter logTrueClassification = new StreamWriter(pttlObject.topicDirectory + pttlObject.directory + "//webdocs//" + "classificationTrueLog.txt"))
            using (StreamWriter logFalseClassification = new StreamWriter(pttlObject.topicDirectory + pttlObject.directory + "//webdocs//" + "classificationFalseLog.txt"))
            {
                while (!interrupted)
                {
                    try
                    {
                        parentUrl = toCrawlList[0];
                        toCrawlList.RemoveAt(0);

                        // Check if parentUrl (Full URL with "http://" and probably "www.") is blacklisted
                        if (!MainWindow.IsBlacklisted(parentUrl))
                        {
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
                                // Satia: Let the delegate process the URI
                                MainWindow.LetDelegateProcess(parentUrl);

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

                                HtmlNodeCollection links = doc.DocumentNode.SelectNodes("//a[@href]");
                                if (links != null)
                                {
                                    foreach (HtmlNode link in links)
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
                                }
                                logFile.Write("<- hyperlinks extraction (tempPage)" + "\n");
                                logFile.Flush();

                                logFile.Write("-> hyperlinks saving in data structures" + "\n");
                                logFile.Flush();
                                for (int i = 0; i < linksList.Count; i++)
                                {

                                    string checkD = "";
                                    if (!linksList[i].Contains("://"))
                                        checkD = mainDomain + '/' + linksList[i];
                                    else
                                        checkD = linksList[i];

                                    //   mutex.WaitOne();
                                    if (inCrawList.ContainsKey(checkD) || (checkD.Contains("youtube")) || (checkD.Contains("facebook")) ||
                                        (checkD.Contains("twitter")) || (checkD.Contains(".pdf")) || (checkD.Contains(".jpg")) ||
                                        (checkD.Contains(".gif")) || (checkD.Contains(".jpeg")))
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
                            MainWindow.SetUrlText(pttlObject.directory);
                        }
                    }
                    catch (SystemException ex)
                    {
                        // MessageBox.Show(parentUrl + "   " + ex.ToString() + "  " + threadN);
                    }

                    if (toCrawlList.Count == 0)
                    {
                        if (count < MainWindow.PagesToDownloadPerTopic && seedBankPointer < seedBank.Count)
                        {
                            logFile.Write("-> getting more seed documents" + "\n");
                            logFile.Flush();
                            int iii = 0;
                            while (true)
                            {
                                line = seedBank[seedBankPointer];
                                if (!inCrawList.ContainsKey(line) && !((line.Contains("facebook")) || (line.Contains("youtube")) || (line.Contains(".pdf")) ||
                                    (line.Contains(".jpg")) || (line.Contains(".gif")) || (line.Contains(".jpeg"))))
                                {
                                    toCrawlList.Add(line);

                                    List<string> backLinks = new List<string>();
                                    backLinks.Add("#seedDoc");
                                    inCrawList.Add(line, backLinks);
                                    //  break;
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
                        {
                            // Either we downloaded enough or we ran out of frontier (seed) links
                            break;
                        }
                    }

                    if (totalDownload > 100)
                    {
                        logFile.Write("-> switching threads" + "\n");
                        logFile.Flush();

                        using (StreamWriter fileW = new StreamWriter(pttlObject.topicDirectory + pttlObject.directory + "//webdocs//" + "DocsUrlNames.txt"))
                        {
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
                        }
                        MainWindow.suspendRunAnotherThread(threadN);
                        logFile.Write("<- switching threads" + "\n");
                        logFile.Flush();
                    }

                    if (count >= MainWindow.PagesToDownloadPerTopic)
                    {
                        // Topic is completed
                        break;
                    }
                }

                if (!interrupted)
                {
                    logFile.Write("-> thread finish but running another thread" + "\n");
                    logFile.Flush();

                    using (StreamWriter fileW = new StreamWriter(pttlObject.topicDirectory + pttlObject.directory + "//webdocs//" + "DocsUrlNames.txt"))
                    {
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
                    }
                    MainWindow.runAnotherThread(threadN);
                    logFile.Write("<- thread finish but running another thread" + "\n");
                    logFile.Flush();
                }
            }

            // Notify the MainWindow that a topic is completed and save the state
            MainWindow.CrawlerTopicCompleted();
            SaveState();
        }

        #region State (de)serialization

        /// <summary>
        /// Serializes the state. Called, when the thread is being stopped.
        /// </summary>
        public void SaveState()
        {
            string filename = MainWindow.mainFolder + "crawler" + this.threadN + ".txt";
            if (!File.Exists(filename))
            {
                //using (File.Create(filename)) { }
            }

            JsonSerializer serializer = new JsonSerializer();
            using (StreamWriter sw = new StreamWriter(filename))
            using (JsonWriter writer = new JsonTextWriter(sw))
            {
                // All fields marked with [JsonProperty] will be serialized
                serializer.Serialize(writer, this);
            }
        }

        /// <summary>
        /// Deserializes the state. Called when the thread is being started.
        /// </summary>
        public void LoadState()
        {
            string filename = MainWindow.mainFolder + "crawler" + this.threadN + ".txt";

            if (!File.Exists(filename))
            {
                // The file does not exist, we do not have to do anything.
                return;
            }
            string fileContent = File.ReadAllText(filename);
            // Deserialize
            try
            {
                JToken root = JObject.Parse(fileContent);
                foreach (JToken field in root.Children())
                {
                    string varName = (field as JProperty).Name;

                    // Find accoridng field.
                    FieldInfo fi = this.GetType().GetField(varName, BindingFlags.Public | BindingFlags.Instance);
                    if (fi != null)
                    {
                        // and set value
                        object value = field.First.ToObject(fi.FieldType, new JsonSerializer());
                        fi.SetValue(this, value);
                    }
                }
            }
            catch (Exception)
            {
            }

        }
        #endregion
    }
}
