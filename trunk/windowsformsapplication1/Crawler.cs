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
using Util;

namespace ACrawler
{
    /// <summary>
    /// An URI, together with its weight.
    /// </summary>
    public class WeightedUri
    {
        public string Uri
        {
            get;
            private set;
        }
        public double Weight
        {
            get;
            private set;
        }
        public WeightedUri(string uri, double weight)
        {
            this.Uri = uri;
            this.Weight = weight;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is WeightedUri))
            {
                return false;
            }
            return Uri.Equals((obj as WeightedUri).Uri);
        }
        public override int GetHashCode()
        {
            return Uri.GetHashCode();
        }
    }

    /// <summary>
    /// A crawler for one topic.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class Crawler
    {
        // Constants
        public static readonly string[] BAD_URL_PARTS = new string[] {
            "youtube", "facebook", "twitter", ".pdf", ".jpg", ".jpeg", ".gif", ".ppt" };
        public const int NUMBER_OF_LINKS_HALF = 30;
        public const int SWITCH_THREADS_DOWNLOAD_THRESHOLD = 20;

        // Backreference to the MainWindow.
        private ACrawlerWin MainWindow;
        // TODO this is to be removed
        public ProcessTopicTopLinksClass pttlObject;

        [JsonProperty]
        public SortedSet<WeightedUri> toCrawlList = new SortedSet<WeightedUri>(
            FunctionalComparer<WeightedUri>.Create((wu1, wu2) =>
                {
                    if(wu1.Uri.Equals(wu2.Uri))
                    {
                        return 0;
                    }
                    if(wu1.Weight > wu2.Weight)
                    {
                        return 1;
                    }
                    if(wu1.Weight < wu2.Weight)
                    {
                        return -1;
                    }
                    return wu1.Uri.CompareTo(wu2.Uri);
                }));
        [JsonProperty]
        public List<string> inCrawlList = new List<string>();
        [JsonProperty]
        public List<string> seedBank = new List<string>();
        [JsonProperty]
        public List<string> relevantDownload = new List<string>();
        
        /// <summary>
        /// The number of relevant pages downloaded.
        /// </summary>
        [JsonProperty]
        public int count;
        /// <summary>
        /// The number of documents downloaded in total (useful and not useful).
        /// </summary>
        [JsonProperty]
        public int totalDownload;

        // The new classifier
        public Classifier classifier;

        // The thread number
        private int threadN;

        // TODO make accessors and methods and make'em private!
        // The interrupted flag
        public volatile bool interrupted = false;
        // The finished flag
        public volatile bool finished = false;
        // The running flag
        public volatile bool running = false;


        public Crawler(int threadN, ACrawlerWin mainWindow)
        {
            this.threadN = threadN;
            this.MainWindow = mainWindow;
            classifier = new Classifier(threadN + 1, mainWindow.mainFolder);
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
                    // Seed links get the highest priority: 1
                    WeightedUri wu = new WeightedUri(lines[i], 1);
                    toCrawlList.Add(wu);
                    inCrawlList.Add(lines[i]);
                }
            }
        }

        /// <summary>
        /// Starts crawling, until finished, interrupted, or running out of frontier links.
        /// (Also is suspended currently after 100 downloaded links).
        /// 
        /// TODO use downloadString and remove dup Download, after PTTL is removed.
        /// </summary>
        public void StartCrawling()
        {
            running = true;
            finished = false;

            totalDownload = 0;
            count = 0;

            // Load the state from a previous run.
            LoadState();

            LinkedList<Uri> linksList = new LinkedList<Uri>();
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
                        if (toCrawlList.Count == 0 || count >= MainWindow.PagesToDownloadPerTopic)
                        {
                            // We ran out of frontier links or topic is completed
                            finished = true;
                            break;
                        }

                        parentUrl = GetNextCrawlURL();
                        // Check if parentUrl (Full URL with "http://" and probably "www.") is blacklisted
                        if (!MainWindow.IsBlacklisted(parentUrl))
                        {
                            MainWindow.SetUrlChecking(parentUrl);

                            logFile.Write("-> Download and saving file in tempPage = " + parentUrl + "\n");
                            logFile.Flush();
                            totalDownload++;
                            client.DownloadFile(parentUrl, pttlObject.topicDirectory + pttlObject.directory + "/tempPage" + threadN + ".htm");
                            // XXX No dup text extractions any more.
                            // XXX Not downloadFile + Read, but DownloadString instead
                            string pageContent = File.ReadAllText(pttlObject.topicDirectory + pttlObject.directory + "/tempPage" + threadN + ".htm");
                            string text = HtmlUtils.ExtractText(pageContent);
                            string[] textSplit = text.Split(' ');

                            logFile.Write("<- Download and saving file in tempPage = " + parentUrl + "\n");
                            logFile.Flush();

                            MainWindow.SetRichText("checking relevance of [url=" + parentUrl + "]\n");


                            logFile.Write("-> checking web page relevance = " + parentUrl + "\n");
                            logFile.Flush();
                            int relResult = pttlObject.isWebLinkRelevant(parentUrl, threadN, logFile);
                            double classify = classifier.Classify(text);
                            bool isMatch = classifier.IsMatch(text);
                            Console.WriteLine("Old: {0} New: {1} NewIsMatch: {2}", relResult, classify, isMatch);


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
                                // Let the delegate process the URI
                                MainWindow.LetDelegateProcess(parentUrl);

                                logFile.Write("<- Downloading relevant document in archieve " + parentUrl + "\n");
                                logFile.Flush();

                                MainWindow.SetRichText("done...[" + "relevant" + "]" + "\n");

                                logFile.Write("-> downloading document for hyperliks extraction (tempPage)" + "\n");
                                logFile.Flush();
                                doc.Load(pttlObject.topicDirectory + pttlObject.directory + "/tempPage" + threadN + ".htm");
                                logFile.Write("<- downloading document for hyperliks extraction (tempPage)" + "\n");
                                logFile.Flush();

                                logFile.Write("-> hyperlinks extraction (tempPage)" + "\n");
                                logFile.Flush();
                                AddLinksToCrawlLists(new Uri(parentUrl), pageContent);
                                logFile.Write("<- hyperlinks extraction (tempPage)" + "\n");
                                logFile.Flush();
                            }
                            MainWindow.SetUrlText(pttlObject.directory);
                        }
                    }
                    catch (SystemException)
                    {
                    }

                    if (totalDownload % SWITCH_THREADS_DOWNLOAD_THRESHOLD == 0)
                    {
                        logFile.Write("-> switching threads" + "\n");
                        logFile.Flush();
                        // This will possibly trigger the MainWindow to interrupt us.
                        MainWindow.SuspendRunAnotherThread(threadN);
                        logFile.Write("<- switching threads" + "\n");
                        logFile.Flush();
                    }
                }

                // Start another topic's crawler, unless we have been interrupted.
                if (!interrupted)
                {
                    logFile.Write("-> thread finish but running another thread" + "\n");
                    logFile.Flush();
                    MainWindow.RunAnotherThread();
                    logFile.Write("<- thread finish but running another thread" + "\n");
                    logFile.Flush();
                }
            }

            // Notify the MainWindow that a topic is completed (or interrupted) and save the state
            MainWindow.CrawlerTopicCompleted();
            SaveState();
            running = false;
        }

        /// <summary>
        /// Adds all links of a page in the crawl lists, unless they have already been added at some point.
        /// </summary>
        /// <param name="anchor">The anchor.</param>
        /// <param name="text">The website's text content, split into words.</param>
        public void AddLinksToCrawlLists(Uri baseUri, string html)
        {
            HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);

            HtmlNodeCollection results = doc.DocumentNode.SelectNodes("//a[@href]");
            if (results == null)
            {
                return;
            }
            foreach (HtmlNode link in results)
            {
                HtmlAttribute att = link.Attributes["href"];
                // Get the absolute URI
                Uri currUri;
                try
                {
                    currUri = new Uri(baseUri, att.Value);
                }
                catch (UriFormatException)
                {
                    continue;
                }
                string uriS = currUri.AbsoluteUri;

                if (!HttpUtils.IsValidUri(uriS))
                {
                    continue;
                }
                // Do not add links we have already crawled or that will already be crawled
                if (inCrawlList.Contains(uriS))
                {
                    continue;
                }
                // Get the snippet rurrounding the anchor.
                string snippet = HtmlUtils.GetSurroundingText(link, Classifier.STOP_WORDS);
                // And concatenate with the URI
                snippet += " " + uriS;
                // Get classification value and insert into list
                double value = classifier.Classify(snippet);

                toCrawlList.Add(new WeightedUri(uriS, value));
                inCrawlList.Add(uriS);
            }
        }

        /// <summary>
        /// Gets the next URL to crawl.
        /// </summary>
        /// <returns>The next URL to crawl.</returns>
        public string GetNextCrawlURL()
        {
            WeightedUri nextWU = toCrawlList.Max;
            toCrawlList.Remove(nextWU);
            return nextWU.Uri;
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
