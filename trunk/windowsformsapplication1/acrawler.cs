using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
using System.Net;
using System.IO;
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
        // Constants
        public static readonly string[] BAD_URL_PARTS = new string[] {
            "youtube", "facebook", "twitter", ".pdf", ".jpg", ".jpeg", ".gif", ".ppt" };
        public const int NUMBER_OF_LINKS_HALF = 30;

        // Backreference to the MainWindow.
        private ACrawlerWin MainWindow;
        // TODO this is to be removed
        public ProcessTopicTopLinksClass pttlObject;

        [JsonProperty]
        public List<string> toCrawlList = new List<string>();
        [JsonProperty]
        public List<string> inCrawlList = new List<string>();
        [JsonProperty]
        public List<string> seedBank = new List<string>();
        [JsonProperty]
        public List<string> relevantDownload = new List<string>();
        
        [JsonProperty]
        public int count;
        [JsonProperty]
        public int totalDownload;
        [JsonProperty]
        public int threadActive;

        // The new classifier
        public Classifier classifier;

        // The thread number
        private int threadN;
        // The topic number
        private int topicN; // TODO redundant -> remove
        // The interrupted flag
        public volatile bool interrupted = false;


        public ACrawlerClass(int threadN, ACrawlerWin mainWindow)
        {
            this.threadN = threadN;
            this.topicN = threadN + 1;
            this.MainWindow = mainWindow;
            classifier = new Classifier(topicN, mainWindow.mainFolder);
        }

        /// <summary>
        /// Adds the the positive links (all except the first 30) from topicN.txt to the seedBank.
        /// </summary>
        public void AddSeedDocs()
        {
            seedBank.Clear();

            string[] lines = File.ReadAllLines(pttlObject.topicDirectory + pttlObject.topicFileName);
            for (int i = NUMBER_OF_LINKS_HALF; i < lines.Length; i++)
            {
                if (IsUsefulURL(lines[i]))
                {
                    seedBank.Add(lines[i]);
                    toCrawlList.Add(lines[i]);
                    inCrawlList.Add(lines[i]);
                }
            }
        }

        public void StartCrawling()
        {
            totalDownload = 0;
            count = 0;

            // Load the state from a previous run.
            LoadState();

            List<string> linksList = new List<string>();
            string parentUrl = "";

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
                        parentUrl = GetNextCrawlURL();
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
                                        if (!linksList.Contains(link.Attributes["href"].Value))
                                        {
                                            linksList.Add(link.Attributes["href"].Value.ToString());
                                        }
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

                                    if (!inCrawlList.Contains(checkD) && IsUsefulURL(checkD))
                                    {
                                        toCrawlList.Add(checkD);
                                        inCrawlList.Add(checkD);
                                    }
                                }


                                linksList.Clear();
                                logFile.Write("<- hyperlinks saving in data structures" + "\n");
                                logFile.Flush();

                            }
                            MainWindow.SetUrlText(pttlObject.directory);
                        }
                    }
                    catch (SystemException)
                    {
                    }

                    if (toCrawlList.Count == 0)
                    {
                        // We ran out of frontier links
                        break;
                    }

                    if (totalDownload > 100)
                    {
                        logFile.Write("-> switching threads" + "\n");
                        logFile.Flush();
                        // FIXME this is probably poorly implemented
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
                    MainWindow.runAnotherThread(threadN);
                    logFile.Write("<- thread finish but running another thread" + "\n");
                    logFile.Flush();
                }
            }

            // Notify the MainWindow that a topic is completed and save the state
            MainWindow.CrawlerTopicCompleted();
            SaveState();
        }

        /// <summary>
        /// Gets the next URL to crawl. TODO adapt implementation.
        /// </summary>
        /// <returns>The next URL to crawl.</returns>
        public string GetNextCrawlURL()
        {
            string result = toCrawlList[0];
            toCrawlList.RemoveAt(0);
            return result;
        }

        /// <summary>
        /// Checks if a URL is useful for crawling.
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <returns>True, if it is useful.</returns>
        private bool IsUsefulURL(string url)
        {
            foreach (string badURLPart in BAD_URL_PARTS)
            {
                if (url.Contains(badURLPart) || url.Contains(badURLPart.ToUpper()))
                {
                    return false;
                }
            }
            return true;
        }

        #region State (de)serialization

        /// <summary>
        /// Serializes the state. Called, when the thread is being stopped.
        /// </summary>
        public void SaveState()
        {
            string filename = MainWindow.mainFolder + "crawler" + this.threadN + ".txt";

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
