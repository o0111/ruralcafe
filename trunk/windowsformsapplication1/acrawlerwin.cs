using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;


using System.Runtime.InteropServices;
using System.Threading;

using System.Net;
using System.IO;
using PorterStemmerAlgorithm;
using System.Web.Script.Serialization;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Util;

namespace Crawler
{
    public partial class ACrawlerWin : Form
    {
        /// <summary>
        /// Process an URI in any way.
        /// </summary>
        /// <param name="uri">The URI.</param>
        /// <returns>An already started thread that will do the work.</returns>
        public delegate Thread ProcessURI(string uri);

        // Constants
        public const int TOTAL_THREAD = 20;
        public const string NEGATIVE_SEED_GOOGLE_TERM = "The";
        public const int GOOGLE_SLEEP_MS = 5000;

        // An array holding all the crawlers.
        private Crawler[] crawlers;
        // Number of crawlers started.
        public volatile int crawlersStarted = 0;
        // If "Pause" has been pressed.
        private volatile bool interrupted = false;
        // The number of topics is total
        private int globalTotalTopics;
        // Count of topics that are completed downloading seed documents
        private int seedsCompleted;
        // Count of tests that are completed
        private int testsCompleted;
        // Count of topics that are completed
        private int topicsCompleted;
        // Overall test accuracy
        private double overallTestAccuracy;
        // Test lock object
        private object testLockObject = new object();

        // The delegate threads processing the URLs
        private List<Thread> delegateThreads = new List<Thread>();
        // Blacklist
        private Regex[] blacklist;
        /// <summary>
        /// The actual delegate.
        /// </summary>
        private ProcessURI processUriDelegate;

        /// <summary>
        /// The main folder.
        /// </summary>
        public string MainFolder
        {
            get;
            private set;
        }
        /// <summary>
        /// The number of pages to download per topic, as indicated by the
        /// NumericUpDown.
        /// </summary>
        public int PagesToDownloadPerTopic
        {
            get { return (int) this.pagesPerTopicNUD.Value; }
        }
        /// <summary>
        /// The number of crawler threads running, that are not about to finish (interrupted).
        /// </summary>
        public int NumThreadsRunning
        {
            get { return crawlers.Where(c => c.Running && !c.Interrupted).Count(); }
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="topicsPath">The path to the topics seed documents.</param>
        /// <param name="processUriDelegate">A delegate to process downloaded URIs.</param>
        public ACrawlerWin(string topicsPath, ProcessURI processUriDelegate)
        {
            InitializeComponent();
            this.MainFolder = topicsPath;
            this.processUriDelegate = processUriDelegate;
            // Make sure the main folder exists
            Directory.CreateDirectory(this.MainFolder);
            threadProgressText.Text =  " ";
        }
        
        #region Buttons

        /// <summary>
        /// Invoked, when the edit topics button button is being clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void EditTopicsButton_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show(this, "Do you also want to delete training sets, seed documents and progress for the current topics?\n" +
                        "Everything RC downloaded will not be deleted. This is useful when changing the topics.",
                        "Continue?", MessageBoxButtons.YesNo);

            string topicsFileName = this.MainFolder + "topics.txt";
            // Create an empty topics.txt file if there is none
            if (!File.Exists(topicsFileName))
            {
                using (File.Create(topicsFileName)) { }
            }
            try
            {
                if (result == DialogResult.Yes)
                {
                    // Delete all files in this folder except topics.txt and blacklist.txt
                    foreach (string file in Directory.GetFiles(MainFolder).Where((file) =>
                        !new FileInfo(file).Name.Equals("topics.txt") && !new FileInfo(file).Name.Equals("blacklist.txt")))
                    {
                        File.Delete(file);
                    }
                    // Delete all directories
                    foreach (string dir in Directory.GetDirectories(MainFolder))
                    {
                        Directory.Delete(dir, true);
                    }
                }
               
                // Open in standard editor.
                Process.Start(topicsFileName);
            }
            catch (Exception exp)
            {
                ShowMessageBox("Could not open the topics file: " + exp.Message);
            }
        }

        /// <summary>
        /// Invoked, when the edit blacklist button button is being clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void EditBlacklistButton_Click(object sender, EventArgs e)
        {
            string blacklistFileName = this.MainFolder + "blacklist.txt";
            // Create an empty blacklist.txt file if there is none
            if (!File.Exists(blacklistFileName))
            {
                using (File.Create(blacklistFileName)) { }
            }
            try
            {
                // Open in standard editor.
                Process.Start(blacklistFileName);
            }
            catch (Exception exp)
            {
                ShowMessageBox("Could not open the blacklist file: " + exp.Message);
            }
        }

        /// <summary>
        /// Invoked, when the generate training set button is being clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void GenerateTrainingSetButton_Click(object sender, EventArgs e)
        {
            SetButtonsEnabled(false);
            new Thread(GenerateTrainingSets).Start();
        }

        /// <summary>
        /// Invoked, when the download seed button is being clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DownloadSeedButton_Click(object sender, System.EventArgs e)
        {
            SetButtonsEnabled(false);
            DownloadSeedDocs();
        }

        /// <summary>
        /// Invoked, when the train/test button is being clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TrainTestButton_Click(object sender, EventArgs e)
        {
            SetButtonsEnabled(false);
            // Determine number of topics by content of topics.txt
            string topicsFileName = this.MainFolder + "topics.txt";
            if (File.Exists(topicsFileName))
            {
                globalTotalTopics = File.ReadAllLines(topicsFileName).Length;
            }
            seedsCompleted = 0;

            StartAllCrawlers(true);
        }

        /// <summary>
        /// Invoked when the Start/Pause button is being clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LoadButton_Click(object sender, System.EventArgs e)
        {
            if (this.startButton.Text.Equals("Pause"))
            {
                Pause();
            }
            else
            {
                Start();
            }
        }

        #endregion
        #region Other UI controls

        /// <summary>
        /// Inserts a text into the big text box. Clears the text box after 6000 characters.
        /// </summary>
        /// <param name="text">The text to insert.</param>
        public void SetRichText(string text)
        {
            if (textWindow.InvokeRequired)
            {
                textWindow.Invoke(new MethodInvoker(delegate() { SetRichText(text); }));
            }
            else
            {
                textWindow.Text = textWindow.Text.Insert(0, text);
                if (textWindow.Text.Length > 6000)
                {
                    textWindow.Text = "";
                }
            }
        }

        /// <summary>
        /// Updates the numbers of relevant links, total pages crawled and frontier links
        /// for all topics in the box on the right.
        /// </summary>
        /// <param name="crawlerNumber">The number of the invoking crawler.</param>
        public void SetUrlText(int crawlerNumber)
        {
            if (textWindow.InvokeRequired)
            {
                threadProgressText.Invoke(new MethodInvoker(delegate() { SetUrlText(crawlerNumber); }));
            }
            else
            {
                string repText = "Topic#{0}: Relevant Links:{1}/Total Crawled:{2}/Frontier Links:{3}";
                string[] lines = threadProgressText.Lines;
                if (lines.Length <= crawlerNumber)
                {
                    for (int i = 0; i < crawlerNumber - lines.Length + 1; i++)
                    {
                        threadProgressText.Text += "\n";
                    }
                    threadProgressText.Text += String.Format(repText, crawlerNumber, crawlers[crawlerNumber].Count,
                            crawlers[crawlerNumber].TotalDownload, crawlers[crawlerNumber].NumFrontierLinks);
                }
                else
                {
                    lines[crawlerNumber] = String.Format(repText, crawlerNumber, crawlers[crawlerNumber].Count,
                            crawlers[crawlerNumber].TotalDownload, crawlers[crawlerNumber].NumFrontierLinks);
                    threadProgressText.Lines = lines;
                }
            }
        }

        /// <summary>
        /// Shows that a topic completed downloading its seed docs in the box on the right.
        /// </summary>
        /// <param name="topicNumber">The topic number or "neg".</param>
        public void ShowSeedDocsFinish(string topicNumber)
        {
            if (textWindow.InvokeRequired)
            {
                threadProgressText.Invoke(new MethodInvoker(delegate() { ShowSeedDocsFinish(topicNumber); }));
            }
            else
            {
                threadProgressText.Text += "Topic " + topicNumber + " finished downloading seed documents\n";
                seedsCompleted++;
                // We have to download seeds for all topics plus the negative seeds
                if (seedsCompleted == globalTotalTopics + 1)
                {
                    ShowMessageBox("Downloading seed documents completed");
                    SetButtonsEnabled(true);
                }
            }
        }

        /// <summary>
        /// Shows that a topic completed downloading its seed docs in the box on the right.
        /// </summary>
        /// <param name="topicNumber">The topic number.</param>
        public void ShowTestFinish(int topicNumber, TestResults results)
        {
            if (textWindow.InvokeRequired)
            {
                threadProgressText.Invoke(new MethodInvoker(delegate() { ShowTestFinish(topicNumber, results); }));
            }
            else
            {
                threadProgressText.Text += "Topic " + topicNumber + " finished testing\n";
                testsCompleted++;
                if (testsCompleted == globalTotalTopics)
                {
                    lock (testLockObject)
                    {
                        overallTestAccuracy += results.accuracy;
                        overallTestAccuracy /= globalTotalTopics;
                    }
                    string overallResult = String.Format("Tests completed. Overall accuracy: {0:0.00}\n", overallTestAccuracy);
                    SetRichText(overallResult);
                    ShowMessageBox(overallResult);
                    SetButtonsEnabled(true);
                }
                else
                {
                    lock (testLockObject)
                    {
                        overallTestAccuracy += results.accuracy;
                    }
                }
            }
        }

        /// <summary>
        /// Sets the label for the current URL being checked.
        /// </summary>
        /// <param name="text">The URL</param>
        public void SetUrlChecking(string text)
        {
            if (textWindow.InvokeRequired)
            {
                UrlChecking.Invoke(new MethodInvoker(delegate() { SetUrlChecking(text); }));
            }
            else
            {
                UrlChecking.Text = text;
            }
        }

        /// <summary>
        /// Shows a modal message box with the given text.
        /// </summary>
        /// <param name="text">The text to show.</param>
        public void ShowMessageBox(string text)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new MethodInvoker(delegate() { ShowMessageBox(text); }));
            }
            else
            {
                MessageBox.Show(this, text);
            }
        }

        /// <summary>
        /// Switches the text from the start button between
        /// "Start/Resume Crawling" and "Pause".
        /// </summary>
        /// <param name="start">If true, the text will be set to "Start/Resume Crawling"</param>
        public void SwitchStartButtonText(bool start)
        {
            if (startButton.InvokeRequired)
            {
                this.Invoke(new MethodInvoker(delegate() { SwitchStartButtonText(start); }));
            }
            else
            {
                if (start)
                {
                    this.startButton.Text = "Start/Resume Crawling";
                }
                else
                {
                    this.startButton.Text = "Pause";
                }
            }
        }

        /// <summary>
        /// Enables or disables a control.
        /// </summary>
        /// <param name="control">The control to enable or disable.</param>
        /// <param name="enabled">Enabled or disabled.</param>
        private void SetControlEnabled(Control control, bool enabled)
        {
            if (control.InvokeRequired)
            {
                this.Invoke(new MethodInvoker(delegate() { SetControlEnabled(control, enabled); }));
            }
            else
            {
                control.Enabled = enabled;
            }
        }

        /// <summary>
        /// Enables or disables all buttons.
        /// </summary>
        /// <param name="enabled">Enabled or disabled.</param>
        private void SetButtonsEnabled(bool enabled)
        {
            if (startButton.InvokeRequired)
            {
                this.Invoke(new MethodInvoker(delegate() { SetButtonsEnabled(enabled); }));
            }
            else
            {
                editTopicsButton.Enabled = enabled;
                editBlacklistButton.Enabled = enabled;
                generateTrainingSetButton.Enabled = enabled;
                downloadSeedButton.Enabled = enabled;
                trainTestButton.Enabled = enabled;
                startButton.Enabled = enabled;
            }
        }

        #endregion
        #region Control methods

        /// <summary>
        /// One topic has been completed. Shows a message box if all have been completed and waits for unfinished
        /// download threads.
        /// </summary>
        /// <param name="n">The number of the crawler.</param>
        public void CrawlerTopicCompleted(int n)
        {
            int topicsCompleted = Interlocked.Increment(ref this.topicsCompleted);
            SetRichText("Crawler " + n + " finished/paused. In total " + topicsCompleted + " crawlers are done.\n");
            if (topicsCompleted == globalTotalTopics || interrupted && topicsCompleted == crawlersStarted)
            {
                // Disable start button until RC finished, too
                SetControlEnabled(this.startButton, false);

                if (delegateThreads.Count == 0)
                {
                    ShowMessageBox("Crawling completed/paused.");
                }
                else
                {
                    ShowMessageBox("Crawling completed/paused. Press OK to wait for RC download threads to finish...");
                    // Join all RC threads.
                    foreach (Thread t in delegateThreads)
                    {
                        if (t != null)
                        {
                            t.Join();
                        }
                    }
                    ShowMessageBox("RC finished downloading.");
                }

                SwitchStartButtonText(true);
                SetButtonsEnabled(true);
            }
        }

        /// <summary>
        /// Lets the delegate process an URI in an own thread, if the delegate exists.
        /// </summary>
        /// <param name="uri">The URI.</param>
        public void LetDelegateProcess(string uri)
        {
            if (processUriDelegate != null)
            {
                Thread t = processUriDelegate(uri);
                lock (delegateThreads)
                {
                    delegateThreads.Add(t);
                }
            }
        }

        /// <summary>
        /// Creates a crawler, the nth.
        /// </summary>
        /// <param name="n">The number of the crawler to start.</param>
        /// <param name="trainTest">If true, the crawler will actually only train+test, not crawl.</param>
        private void CreateCrawler(int n, bool trainTest)
        {
            crawlers[n] = new Crawler(n, this);
            if (!trainTest)
            {
                // Train the crawlers here, if we're not testing
                crawlers[n].Train();
                SetRichText("Created and trained crawler " + n + "\n");
            }
        }

        /// <summary>
        /// Starts a crawler thread, the nth. Crawler must have been created before!
        /// </summary>
        /// <param name="n">The number of the crawler to start.</param>
        /// <param name="trainTest">If true, the crawler will actually only train+test, not crawl.</param>
        private void StartCrawler(int n, bool trainTest)
        {
            crawlers[n].SetRunning();
            new Thread(() =>
                {
                    if(trainTest)
                    {
                        crawlers[n].TrainTest();
                    }
                    else
                    {
                        crawlers[n].StartCrawling();
                    }
                }).Start();
        }

        /// <summary>
        /// Pause all crawler threads.
        /// </summary>
        private void Pause()
        {
            this.startButton.Enabled = false;
            this.interrupted = true;
            // Pause all threads
            foreach (Crawler crawler in crawlers)
            {
                if (crawler != null)
                {
                    crawler.Interrupt();
                }
            }
        }

        /// <summary>
        /// Starts all crawlers, if all necessary files exist.
        /// </summary>
        /// <param name="trainTest">If true, the crawler will actually only train+test, not crawl.</param>
        private void StartAllCrawlers(bool trainTest)
        {
            // Check if all necessary files exist
            if (!Directory.Exists(this.MainFolder + "neg"))
            {
                ShowMessageBox("Please download the seed documents first.");
                SwitchStartButtonText(true);
                SetButtonsEnabled(true);
                return;
            }
            for (int i = 0; i < globalTotalTopics; i++)
            {
                if (!Directory.Exists(this.MainFolder + i) || !Directory.Exists(this.MainFolder + i + Path.DirectorySeparatorChar + "webdocs"))
                {
                    ShowMessageBox("Please download the seed documents first.");
                    SwitchStartButtonText(true);
                    SetButtonsEnabled(true);
                    return;
                }
            }

            // Initialize crawler array.
            this.crawlers = new Crawler[globalTotalTopics];

            // Get number of crawlers to start.
            // All for train+test and up to 20 for crawl.
            crawlersStarted = trainTest ? globalTotalTopics : Math.Min(TOTAL_THREAD, globalTotalTopics);

            new Thread(() =>
            {
                for (int i = 0; i < crawlersStarted; i++)
                {
                    CreateCrawler(i, trainTest);
                }
                // Re-enable pause button
                SetControlEnabled(this.startButton, true);
                for (int i = 0; i < crawlersStarted; i++)
                {
                    StartCrawler(i, trainTest);
                }
            }).Start();
        }

        /// <summary>
        /// Starts a crawler for each topic. They potentially resume where they stopped.
        /// </summary>
        private void Start()
        {
            SetButtonsEnabled(false);
            this.interrupted = false;
            // Change text of this button.
            SwitchStartButtonText(false);

            this.topicsCompleted = 0;
            // Determine number of topics by content of topics.txt
            string topicsFileName = this.MainFolder + "topics.txt";
            if (File.Exists(topicsFileName))
            {
                globalTotalTopics = File.ReadAllLines(topicsFileName).Length;
            }

            string blacklistFileName = this.MainFolder + "blacklist.txt";
            // Create an empty blacklist.txt file if there is none
            if (!File.Exists(blacklistFileName))
            {
                using (File.Create(blacklistFileName)) { }
            }
            // Load blacklist
            string[] domains = File.ReadAllLines(blacklistFileName);
            this.blacklist = new Regex[domains.Length];
            for(int i = 0; i < domains.Length; i++)
            {
                string domain = HttpUtils.RemoveWWWPrefix(HttpUtils.RemoveHttpPrefix(domains[i])).Replace(".", @"\.");
                blacklist[i] = new Regex(@"https?://(\w+?\.)?" + domain);
            }

            StartAllCrawlers(false);
        }

        /// <summary>
        /// Looks if there is a topic for which a crawler has not been started yet or if there is a crawler interrupted.
        /// In these cases, the crawler is started/restarted.
        /// </summary>
        public void RunAnotherThread()
        {
            lock (crawlers)
            {
                if (interrupted)
                {
                    return;
                }
                if (crawlersStarted < globalTotalTopics)
                {
                    CreateCrawler(crawlersStarted, false);
                    StartCrawler(crawlersStarted, false);
                    crawlersStarted++;
                }
                else
                {
                    Crawler toStart = null;
                    for(int i = 0; i < globalTotalTopics; i++)
                    {
                        if (!crawlers[i].Running && !crawlers[i].Finished)
                        {
                            toStart = crawlers[i];
                            break;
                        }
                    }
                    if (toStart != null)
                    {
                        // Restart the other crawler
                        StartCrawler(toStart.ThreadN, false);
                    }
                }
            }
        }

        /// <summary>
        /// Looks if there is a topic for which a crawler has not been started yet or if there is a crawler interrupted.
        /// In these cases, the given crawler is interrupted and the other crawler is started/restarted.
        /// </summary>
        /// <param name="finishThread">The number of the thread to interrupt.</param>
        public void SuspendRunAnotherThread(int finishThread)
        {
            lock (crawlers)
            {
                if (interrupted)
                {
                    return;
                }
                if (crawlersStarted < globalTotalTopics)
                {
                    // We have to decrement the number of topics completed, when we interrupt a Crawler
                    Interlocked.Decrement(ref this.topicsCompleted);
                    // Interrupt the current crawler
                    crawlers[finishThread].Interrupt();
                    // Start a crawler for a new topic
                    CreateCrawler(crawlersStarted, false);
                    StartCrawler(crawlersStarted, false);
                    crawlersStarted++;
                }
                else
                {
                    Crawler toStart = null;
                    int i = finishThread;
                    i = (i == globalTotalTopics - 1 ? 0 : i + 1);
                    while (i != finishThread)
                    {
                        if (!crawlers[i].Running && !crawlers[i].Finished)
                        {
                            toStart = crawlers[i];
                            break;
                        }
                        i = (i == globalTotalTopics - 1 ? 0 : i + 1);
                    }

                    // Only interrupt the current, if we found another to start, or if too many are running.
                    if (toStart != null || NumThreadsRunning > TOTAL_THREAD)
                    {
                        // We have to decrement the number of topics completed, when we interrupt a Crawler
                        Interlocked.Decrement(ref this.topicsCompleted);
                        // Interrupt the current crawler
                        crawlers[finishThread].Interrupt();

                        if (toStart != null)
                        {
                            // Restart the other crawler
                            StartCrawler(toStart.ThreadN, false);
                        }
                    }
                }
            }
        }

        #endregion
        #region Helpers (Generate Training sets, Download Seed, ...)

        /// <summary>
        /// Generates the training sets.
        /// </summary>
        private void GenerateTrainingSets()
        {
            string topicsFileName = this.MainFolder + "topics.txt";
            // Create an empty topics.txt file if there is none
            if (!File.Exists(topicsFileName))
            {
                using (File.Create(topicsFileName)) { }
            }

            string[] topics = File.ReadAllLines(topicsFileName);
            if (topics.Length == 0)
            {
                ShowMessageBox("Please define some topics first.");
                SetButtonsEnabled(true);
                return;
            }

            // Get the negative seed links and write them into the file
            List<string> negativeSeedLinks = GetGoogleResults(NEGATIVE_SEED_GOOGLE_TERM, Crawler.NUMBER_OF_LINKS);
            File.WriteAllLines(this.MainFolder + "topicneg.txt", negativeSeedLinks);

            // Loop through all topics in topics.txt
            for (int i = 0; i < topics.Length; i++)
            {
                // Create the file, possibly overwriting
                string topicFileName = this.MainFolder + "topic" + i + ".txt";
                SetRichText("Generating training set for " + topics[i] + "\n");
                using (StreamWriter topicFileWriter = new StreamWriter(File.Create(topicFileName)))
                {
                    // Google 30 positive links for the topic
                    List<string> positiveSeedLinks = GetGoogleResults(topics[i], Crawler.NUMBER_OF_LINKS);
                    foreach (string positiveSeedLink in positiveSeedLinks)
                    {
                        topicFileWriter.WriteLine(positiveSeedLink);
                    }
                }
            }

            ShowMessageBox("Generated the training sets for " + topics.Length + " topics.");
            SetButtonsEnabled(true);
        }

        /// <summary>
        /// Downloads the seed documents.
        /// </summary>
        private void DownloadSeedDocs()
        {
            // Determine number of topics by content of topics.txt
            string topicsFileName = this.MainFolder + "topics.txt";
            if (File.Exists(topicsFileName))
            {
                globalTotalTopics = File.ReadAllLines(topicsFileName).Length;
            }

            // Check if all topicN.txt files and topicneg.txt exist
            if (!File.Exists(this.MainFolder + "topicneg.txt"))
            {
                ShowMessageBox("Please generate the training sets first.");
                SetButtonsEnabled(true);
                return;
            }
            for (int i = 0; i < globalTotalTopics; i++)
            {
                if (!File.Exists(this.MainFolder + "topic" + i + ".txt"))
                {
                    ShowMessageBox("Please generate the training sets first.");
                    SetButtonsEnabled(true);
                    return;
                }
            }

            seedsCompleted = 0;
            // Start a thread for each topic and one for negs
            new Thread(() => DownloadSeedDocsForTopic("neg")).Start();
            for (int cc = 0; cc < globalTotalTopics; cc++)
            {
                int num = cc;
                // Do NOT use cc directly!
                new Thread(() => DownloadSeedDocsForTopic("" + num)).Start();
            }
        }

        /// <summary>
        /// Downloads the seed documents for the topic with the given number or the negative seed docs.
        /// </summary>
        /// <param name="topicText">The number of the topic or "neg"</param>
        private void DownloadSeedDocsForTopic(string topicText)
        {
            SetRichText("..................starting downloading seed documents for topic " + topicText + "\n");
            // Read links to download
            string[] links = File.ReadAllLines(MainFolder + "topic" + topicText + ".txt");
            // Create directories
            Directory.CreateDirectory(MainFolder + topicText);
            string topicDir = MainFolder + topicText + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(topicDir + "webdocs");

            int iDiff = 0;
            //WebClient client = new MyWebClient(Crawler.WEB_TIMEOUT);
            for (int i = 0; i < links.Length; i++)
            {
                string file = topicDir + Path.DirectorySeparatorChar + (i - iDiff) + ".txt";
                try
                {
                    // Skip ftp and other protocols
                    if (!links[i].StartsWith("http"))
                    {
                        continue;
                    }
                    SetRichText("Topic" + topicText + "= downloading [URL=" + links[i] + "]\n");
                    // Download page
                    HttpWebRequest request = WebRequest.Create(links[i]) as HttpWebRequest;
                    WebResponse response = request.GetResponse();
                    // Skip non HTML links
                    if(!response.ContentType.StartsWith("text/html"))
                    {
                        continue;
                    }

                    string pageContent; // client.DownloadString(links[i]);
                    using(StreamReader sr = new StreamReader(response.GetResponseStream()))
                    {
                        pageContent = sr.ReadToEnd();
                    }

                    // Extract text
                    string text = HtmlUtils.ExtractText(pageContent);
                    // Save to file
                    File.WriteAllText(file, text);
                }
                catch (Exception e)
                {
                    // Jump one back, so we do not have empty files
                    iDiff++;
                    SetRichText("could not download [error = " + e.Message + "]\n");
                    
                }
            }

            SetRichText(".............seed downloading complete successfully for topic " + topicText + "\n");
            ShowSeedDocsFinish(topicText);
        }

        /// <summary>
        /// Checks whether a URI is blacklisted.
        /// </summary>
        /// <param name="requestUri">URI to check.</param>
        /// <returns>True or false for blacklisted or not.</returns>
        public bool IsBlacklisted(string requestUri)
        {
            // trim the "http://" and "www."
            //requestUri = HttpUtils.RemoveWWWPrefix(HttpUtils.RemoveHttpPrefix(requestUri));

            // check against all domains in the blacklist
            foreach (Regex domainH in blacklist)
            {
                // trim the "http://" and "www."
                if (domainH.IsMatch(requestUri))
                {
                    return true;
                }
            }
            return false;
        }

        #endregion
        #region Google search

        /// <summary>
        /// Gets up to maxAmount URLs from Google searching the searchString.
        /// </summary>
        /// <param name="searchString">The string to search for.</param>
        /// <param name="maxAmount">The maximum number of results.</param>
        /// <returns>The URLs.</returns>
        private List<string> GetGoogleResults(string searchString, int maxAmount)
        {
            WebClient client = new MyWebClient(Crawler.WEB_TIMEOUT);
            int pageNum = 1;
            List<string> result = new List<string>();

            try
            {
                while (result.Count < maxAmount)
                {
                    string query = ConstructGoogleSearch(searchString, pageNum);
                    // Sleep 5s before each request so that Google does not consider us as a bot.
                    // Note: This will stop working if you'd create a thread for each topic
                    Thread.Sleep(GOOGLE_SLEEP_MS);

                    string page = client.DownloadString(query);
                    List<string> results = ExtractGoogleResults(page);
                    if (results.Count == 0)
                    {
                        break;
                    }
                    result.AddRange(results.Take(maxAmount - result.Count));
                    pageNum++;
                }
            }
            catch (WebException e)
            {
                ShowMessageBox("Could not generate training sets for " + searchString + ": " + e.Message + "\n");
            }

            return result;
        }

        /// <summary>
        /// Constructs a Google search URL.
        /// </summary>
        /// <param name="searchString">The search string</param>
        /// <param name="pagenumber">The page number</param>
        /// <returns>Google search query.</returns>
        private string ConstructGoogleSearch(string searchString, int pagenumber)
        {
            // Only search for HTML pages
            searchString += " filetype:html";
            string result = "http://www.google.com/search?hl=en&q=" +
                                        searchString.Replace(' ', '+') +
                                        "&btnG=Google+Search&aq=f&oq=";
            if (pagenumber > 1)
            {
                result += "&start=" + ((pagenumber - 1) * 10);
            }
            return result;
        }

        /// <summary>
        /// Extracts the result links from a google results page.
        /// </summary>
        /// <param name="googleResultPage">The Google results page.</param>
        /// <returns>List of links.</returns>
        private List<string> ExtractGoogleResults(string googleResultPage)
        {
            string[] stringSeparator = new string[] { "</cite>" };
            List<string> results = new List<string>();
            string[] lines = googleResultPage.Split(stringSeparator, StringSplitOptions.RemoveEmptyEntries);

            // get links
            int pos;
            // Omitting last split, since there is no link any more.
            for (int i = 0; i < lines.Count() - 1; i++)
            {
                string currLine = lines[i];
                // get the uri
                if ((pos = currLine.LastIndexOf("<a href=\"/url?q=")) >= 0)
                {
                    // start right after
                    string currUri = currLine.Substring(pos + "<a href=\"/url?q=".Length);
                    if ((pos = currUri.IndexOf("&amp")) >= 0)
                    {
                        // cut end
                        currUri = currUri.Substring(0, pos);
                        // strip HTML tags
                        currUri = RegExs.HTML_TAG_REGEX.Replace(currUri, "");
                        currUri = currUri.Trim();
                    }

                    results.Add(currUri);
                }
            }

            return results;
        }

        #endregion
    }
}
