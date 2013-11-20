using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
using System.Net;
using System.IO;
using System.Windows.Forms;
using Crawler;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Reflection;
using Util;

namespace Crawler
{
    /// <summary>
    /// A class extending the webclient where you can set a timeout.
    /// </summary>
    public class MyWebClient : WebClient
    {
        public int TimeoutMs
        {
            get;
            set;
        }

        public MyWebClient(int timeoutMs)
        {
            this.TimeoutMs = timeoutMs;
        }

        protected override WebRequest GetWebRequest(Uri uri)
        {
            WebRequest w = base.GetWebRequest(uri);
            w.Timeout = TimeoutMs;
            return w;
        }
    }

    /// <summary>
    /// An URI, together with its weight.
    /// </summary>
    public class WeightedUri : IComparable<WeightedUri>
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
        /// <summary>
        /// Implementing the CompareTo method.
        /// 
        /// We make sure entries with the same URL are "equal" by comparison,
        /// so that there won't be dups in the SortedSet. All other entries are
        /// sorted by weight and then by Uri.
        /// </summary>
        /// <param name="wu2">The other WeightedUri</param>
        /// <returns>A comparison value.</returns>
        public int CompareTo(WeightedUri wu2)
        {
            if (Uri.Equals(wu2.Uri))
            {
                return 0;
            }
            if (Weight > wu2.Weight)
            {
                return 1;
            }
            if (Weight < wu2.Weight)
            {
                return -1;
            }
            return Uri.CompareTo(wu2.Uri);
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
        public const int NUMBER_OF_LINKS = 10; // XXX maybe increasing this will increase accuracy
        public const int SWITCH_THREADS_DOWNLOAD_THRESHOLD = 100;
        public const int WEB_TIMEOUT = 1000 * 5; // seconds

        // The links still to crawl, in a PriorityQueue
        [JsonProperty]
        private SortedSet<WeightedUri> toCrawlList = new SortedSet<WeightedUri>();
        [JsonProperty]
        // All links to crawl and ever crawled
        private List<string> inCrawlList = new List<string>();
        // The number of relevant pages downloaded.
        [JsonProperty]
        private int count;
        // The number of documents downloaded in total (useful and not useful).
        [JsonProperty]
        private int totalDownload;

        // Backreference to the MainWindow.
        private ACrawlerWin MainWindow;
        // The new classifier
        private Classifier classifier;
        // private volatile bools for the properties.
        private volatile bool interrupted = false;
        private volatile bool finished = false;
        private volatile bool running = false;

        /// <summary>
        /// The number of relevant pages downloaded.
        /// </summary>
        public int Count
        {
            get { return count; }
        }
        /// <summary>
        /// The number of documents downloaded in total (useful and not useful).
        /// </summary>
        public int TotalDownload
        {
            get { return totalDownload; }
        }
        public int NumFrontierLinks
        {
            get { return toCrawlList.Count; }
        }
        /// <summary>
        /// The thread number.
        /// </summary>
        public int ThreadN
        {
            get;
            private set;
        }
        /// <summary>
        /// Whether the crawler has been interrupted.
        /// </summary>
        public bool Interrupted
        {
            get { return interrupted; }
        }
        /// <summary>
        /// Whether the crawler is finished download enough documents, or finished because he ran out of frontier links.
        /// </summary>
        public bool Finished
        {
            get { return finished; }
        }
        /// <summary>
        /// Whether the crawler is currently running.
        /// </summary>
        public bool Running
        {
            get { return running; }
        }

        /// <summary>
        /// Creates a new crawler.
        /// </summary>
        /// <param name="threadN">The thread number of this crawler.</param>
        /// <param name="mainWindow">The MainWindow.</param>
        public Crawler(int threadN, ACrawlerWin mainWindow)
        {
            this.ThreadN = threadN;
            this.MainWindow = mainWindow;
            AddSeedDocs();
        }

        /// <summary>
        /// Adds the the positive links from topicN.txt to the crawl lists.
        /// </summary>
        private void AddSeedDocs()
        {
            string[] lines = File.ReadAllLines(MainWindow.MainFolder + "topic" + ThreadN + ".txt");
            for (int i = 0; i < lines.Length; i++)
            {
                if (IsUsefulURL(lines[i]))
                {
                    // Seed links get the highest priority: 1
                    WeightedUri wu = new WeightedUri(lines[i], 1);
                    toCrawlList.Add(wu);
                    inCrawlList.Add(lines[i]);
                }
            }
        }

        /// <summary>
        /// Trains the classifier.
        /// </summary>
        public void Train()
        {
            classifier = new Classifier(ThreadN, MainWindow.MainFolder);
            classifier.Train();
        }

        /// <summary>
        /// Trains the classifier with half of the documents and tests it with the other half.
        /// </summary>
        public void TrainTest()
        {
            classifier = new Classifier(ThreadN, MainWindow.MainFolder);
            TestResults results = classifier.TrainTest();
            MainWindow.SetRichText("Test results for topic " + ThreadN + ":\n" + results + "\n");
            MainWindow.ShowTestFinish(ThreadN, results);
        }

        /// <summary>
        /// Interrupts the Crawler.
        /// </summary>
        public void Interrupt()
        {
            interrupted = true;
        }
        /// <summary>
        /// Sets running = true. Should ba called before StartCrawling is started in a new thread, to avoid conflicts.
        /// </summary>
        public void SetRunning()
        {
            running = true;
        }

        /// <summary>
        /// Starts crawling, until finished, interrupted, or running out of frontier links.
        /// Also is suspended (temporarily) after 100 downloaded links.
        /// </summary>
        public void StartCrawling()
        {
            running = true;
            //interrupted = false;
            finished = false;

            totalDownload = 0;
            count = 0;

            // Load the state from a previous run.
            LoadState();

            string parentUrl = "";
            WebClient client = new MyWebClient(WEB_TIMEOUT);

            string folder = MainWindow.MainFolder + classifier.TopicDir + Path.DirectorySeparatorChar;
            using (StreamWriter logFile = new StreamWriter(folder + "webdocs" + Path.DirectorySeparatorChar + "systemLog.txt"))
            using (StreamWriter logTrueClassification = new StreamWriter(folder + "webdocs" + Path.DirectorySeparatorChar + "classificationTrueLog.txt"))
            using (StreamWriter logFalseClassification = new StreamWriter(folder + "webdocs" + Path.DirectorySeparatorChar + "classificationFalseLog.txt"))
            {
                while (!Interrupted)
                {
                    try
                    {
                        if (toCrawlList.Count == 0 || count >= MainWindow.PagesToDownloadPerTopic)
                        {
                            // We ran out of frontier links or topic is completed
                            finished = true;
                            break;
                        }

                        // Get the next URL from the ProrityQueue
                        parentUrl = GetNextCrawlURL();
                        // Check if parentUrl (Full URL with "http://" and probably "www.") is blacklisted
                        if (!MainWindow.IsBlacklisted(parentUrl))
                        {
                            MainWindow.SetUrlChecking(parentUrl);

                            // Download the page
                            logFile.Write("-> Download = " + parentUrl + "\n");
                            logFile.Flush();
                            totalDownload++;

                            string pageContent = client.DownloadString(parentUrl);
                            string text = HtmlUtils.ExtractText(pageContent);
                            logFile.Write("<- Download = " + parentUrl + "\n");
                            logFile.Flush();

                            // Check, if it is relevant
                            MainWindow.SetRichText("checking relevance of [url=" + parentUrl + "]\n");
                            logFile.Write("-> checking web page relevance = " + parentUrl + "\n");
                            logFile.Flush();
                            //int relResult = pttlObject.isWebLinkRelevant(parentUrl, threadN, logFile);
                            bool isMatch = classifier.IsMatch(text);
                            logFile.Write("<- checking web page relevance = " + parentUrl + "\n");
                            logFile.Flush();

                            if (!isMatch)
                            {
                                // Not relevant
                                logFalseClassification.Write(parentUrl + "\n");
                                MainWindow.SetRichText("done...[" + "not relevant" + "]" + "\n");
                            }
                            else
                            {
                                // Relevant!
                                logTrueClassification.Write(parentUrl + "\n");
                                MainWindow.SetRichText("done...[" + "relevant" + "]" + "\n");
                                count++;

                                logFile.Write("-> Saving relevant document in archieve " + parentUrl + "\n");
                                logFile.Flush();
                                // Save page contents in file.
                                File.WriteAllText(folder + "webdocs" + Path.DirectorySeparatorChar + count + ".html", pageContent);
                                // Let the delegate process the URI
                                MainWindow.LetDelegateProcess(parentUrl);
                                logFile.Write("<- Saving relevant document in archieve " + parentUrl + "\n");
                                logFile.Flush();

                                logFile.Write("-> hyperlinks extraction" + "\n");
                                logFile.Flush();
                                AddLinksToCrawlLists(new Uri(parentUrl), pageContent);
                                logFile.Write("<- hyperlinks extraction" + "\n");
                                logFile.Flush();
                            }
                        }
                    }
                    catch (SystemException)
                    {
                    }
                    finally
                    {
                        // Do not do that too often.
                        if (totalDownload % 10 == 0)
                        {
                            MainWindow.SetUrlText(ThreadN);
                        }
                    }

                    // After a certain number of downloads, we want to switch threads,
                    // so another topic can procede
                    if (totalDownload % SWITCH_THREADS_DOWNLOAD_THRESHOLD == 0)
                    {
                        logFile.Write("-> switching threads" + "\n");
                        logFile.Flush();
                        // This will possibly trigger the MainWindow to interrupt us.
                        MainWindow.SuspendRunAnotherThread(ThreadN);
                        logFile.Write("<- switching threads" + "\n");
                        logFile.Flush();
                    }
                }

                // Refresh display a last time
                MainWindow.SetUrlText(ThreadN);
                // We're done. Start another topic's crawler, unless we have been interrupted.
                if (!Interrupted)
                {
                    logFile.Write("-> thread finish but running another thread" + "\n");
                    logFile.Flush();
                    MainWindow.RunAnotherThread();
                    logFile.Write("<- thread finish but running another thread" + "\n");
                    logFile.Flush();
                }
            }

            // Notify the MainWindow that a topic is completed (or interrupted) and save the state.
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
            string filename = MainWindow.MainFolder + "crawler" + this.ThreadN + ".txt";

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
            string filename = MainWindow.MainFolder + "crawler" + this.ThreadN + ".txt";

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
                    FieldInfo fi = this.GetType().GetField(varName, BindingFlags.NonPublic | BindingFlags.Instance);
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
