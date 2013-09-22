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
using Google.API.Search;
using Google.Apis.Customsearch.v1;
using HtmlAgilityPack;
using GehtSoft.Collections;
using System.Threading;

using System.Net;
using System.IO;
using ACrawler;
using ProcessTopicTopLinks;
using PorterStemmerAlgorithm;
using System.Web.Script.Serialization;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace WindowsFormsApplication1
{
    public partial class ACrawlerWin : Form
    {
        /// <summary>Regex for html tags.</summary>
        public static readonly Regex HTML_TAG_REGEX = new Regex(@"<[^<]+?>", RegexOptions.IgnoreCase);

        private Thread[] thread = new Thread[200];
        private ACrawlerClass[] crawlerObject = new ACrawlerClass[200];
        private ProcessTopicTopLinksClass[] pttlObject = new ProcessTopicTopLinksClass[200];
        private int totalThread = 0;
        private int trackTopics = 0;
        public Mutex mutex = new Mutex();
        public string mainFolder;
        public int globalTotalTopics;
        public int countDead;
        // Count of topics that are completed
        private int topicsCompleted;
        // The delegate threads processing the URLs
        private List<Thread> delegateThreads = new List<Thread>();

        public int PagesToDownloadPerTopic
        {
            get { return (int) this.pagesPerTopicNUD.Value; }
        }

        /// <summary>
        /// Process an URI in any way.
        /// </summary>
        /// <param name="uri">The URI.</param>
        public delegate void ProcessURI(string uri);
        /// <summary>
        /// The actual delegate.
        /// </summary>
        public ProcessURI processUriDelegate;

        /// <summary>
        /// Pause button is disabled by default.
        /// </summary>
        public ACrawlerWin()
        {
            InitializeComponent();
            this.pauseButton.Enabled = false;
        }

        /// <summary>
        /// Constructor to set the settings programatically without Settings.txt
        /// </summary>
        /// <param name="topicsPath">The path to the topics seed documents.</param>
        /// <param name="processUriDelegate">A delegate to process downloaded URIs.</param>
        public ACrawlerWin(string topicsPath, ProcessURI processUriDelegate)
            : this()
        {
            this.mainFolder = topicsPath;
            this.processUriDelegate = processUriDelegate;
            // Make sure the main folder exists
            Directory.CreateDirectory(this.mainFolder);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            if (this.mainFolder != null)
            {
                // The settings have been set by Constructor already.
                return;
            }

            string line;
            int ccc = 0;
            System.IO.StreamReader file = new System.IO.StreamReader("Setting.txt");
            while ((line = file.ReadLine()) != null)
            {
                ccc++;


                if (ccc == 1)
                    mainFolder = line;
                if (ccc == 2)
                    globalTotalTopics = Int32.Parse(line);
            }
            file.Close();

            /*
            // Search 32 results of keyword : "Google APIs for .NET"
            GwebSearchClient client = new GwebSearchClient("");
            IList<IWebResult> results = client.Search("data mining", 500);

            using (System.IO.StreamWriter file = new System.IO.StreamWriter(@"c:\\Crawler\\topResults.txt"))
            {

                //file.WriteLine(line);



                foreach (IWebResult result in results)
                {
                  //  Console.WriteLine("[{0}] {1} => {2}", result.Title, result.Content, result.Url);
                  //  MessageBox.Show(result.Url);
                    file.WriteLine(result.Url);
                }

            }*/
        }

        public void WorkThreadFunction()
        {
            try
            {
                // do any background work
                //  ACrawlerClass crawlerObject = new ACrawlerClass();
                //  crawlerObject.listBox = listBox;
                //  crawlerObject.startCrawling("http://ming.org.pk/shariq.htm");
                MessageBox.Show("hdddello");
            }
            catch (Exception)
            {

                // log errors
            }
        }

        public void SetRichText(string text)
        {
            if (textWindow.InvokeRequired)
            {
                textWindow.Invoke(new MethodInvoker(delegate() { SetRichText(text); }));
            }
            else
            {
                //textWindow.Text = "Start Time: " + "";
                textWindow.Text = textWindow.Text.Insert(0, text);
                //totalURLs.Text = "" + crawlerObject.toCrawlList.Count;
                //textWindow.Text += text;
                if (textWindow.Text.Length > 6000)
                    textWindow.Text = "";
            }
        }
        public void SetUrlText(string topic)
        {
            if (textWindow.InvokeRequired)
            {
                threadProgressText.Invoke(new MethodInvoker(delegate() { SetUrlText(topic); }));
            }
            else
            {

                threadProgressText.Text = "";
                for (int i = 0; i < trackTopics; i++)
                {
                    threadProgressText.Text += "Topic#" + i + ": Relevant Links:" + crawlerObject[i].count + "/Total Crawled:" + crawlerObject[i].totalDownload + "/Frontier Links: " + crawlerObject[i].toCrawlList.Count + "\n";
                }

            }
        }
        public void showSeedDocsFinish(string topic)
        {
            if (textWindow.InvokeRequired)
            {
                threadProgressText.Invoke(new MethodInvoker(delegate() { showSeedDocsFinish(topic); }));
            }
            else
            {
                threadProgressText.Text += "Topic " + topic + " finished downloading seed documents\n";
                countDead++;
                if (countDead == globalTotalTopics)
                {
                    MessageBox.Show("Downloading seed documents completed");
                    SetButtonsEnabled(true);
                }
            }
        }
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
        /// One topic has been completed. Shows a message box if all have been completed and waits for unfinished
        /// download threads.
        /// </summary>
        public void CrawlerTopicCompleted()
        {
            int topicsCompleted = Interlocked.Increment(ref this.topicsCompleted);
            if (topicsCompleted == globalTotalTopics)
            {
                if (delegateThreads.Count == 0)
                {
                    MessageBox.Show("Crawling completed successfully.");
                }
                else
                {
                    MessageBox.Show("Crawling completed successfully. Press OK to wait for RC download threads to finish...");
                    // Join all RC threads.
                    foreach (Thread t in delegateThreads)
                    {
                        t.Join();
                    }
                    MessageBox.Show("RC finished downloading.");
                }
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
                Thread t = new Thread(() =>
                {
                    processUriDelegate(uri);
                });
                t.Start();
                lock (delegateThreads)
                {
                    delegateThreads.Add(t);
                }
            }
        }

        private void LoadButton_Click(object sender, System.EventArgs e)
        {
            SetButtonsEnabled(false);
            this.pauseButton.Enabled = true;

            this.topicsCompleted = 0;
            // Satia: Determine number of topics by content of topics.txt, if it is 0
            string topicsFileName = this.mainFolder + "topics.txt";
            if (globalTotalTopics == 0 && File.Exists(topicsFileName))
            {
                globalTotalTopics = File.ReadAllLines(topicsFileName).Length;
            }

            // Check if all necessary files exist
            for (int i = 1; i <= globalTotalTopics; i++)
            {
                if (!Directory.Exists(this.mainFolder + i) || !Directory.Exists(this.mainFolder + i + Path.DirectorySeparatorChar + "webdocs"))
                {
                    MessageBox.Show(this, "Please download the seed documents first.",
                        "Error", MessageBoxButtons.OK);
                    SetButtonsEnabled(true);
                    this.pauseButton.Enabled = false;
                    return;
                }
                // 0.txt to 29.txt have to exist
                for (int j = 0; j < 30; j++)
                {
                    if (!File.Exists(this.mainFolder + i + Path.DirectorySeparatorChar + j + ".txt"))
                    {
                        MessageBox.Show(this, "Please download the seed documents first.",
                            "Error", MessageBoxButtons.OK);
                        SetButtonsEnabled(true);
                        this.pauseButton.Enabled = false;
                        return;
                    }
                }
            }
            
            int cc = 0;
            totalThread = 20;

            trackTopics = 0;

            for (cc = 0; cc < totalThread; cc++)
            {

                pttlObject[cc] = new ProcessTopicTopLinksClass();
                pttlObject[cc].textWindow = textWindow;
                pttlObject[cc].MainWindow = this;
                pttlObject[cc].topicDirectory = mainFolder;
                pttlObject[cc].topicFileName = "topic" + (cc + 1) + ".txt";
                pttlObject[cc].directory = "" + (cc + 1);


                crawlerObject[cc] = new ACrawlerClass();
                crawlerObject[cc].textWindow = textWindow;

                crawlerObject[cc].MainWindow = this;
                crawlerObject[cc].pttlObject = pttlObject[cc];
                crawlerObject[cc].addSeedDocs();

                pttlObject[cc].makeTrainTest();
                crawlerObject[cc].threadActive = 1;


                // for (int i = 0; i < 100; i++)
                {
                    thread[cc] = new Thread(new ParameterizedThreadStart(crawlerObject[cc].startCrawling));
                }
                // for (int i = 0; i < 100; i++)
                thread[cc].Start(cc);

                trackTopics++;

                if (trackTopics >= globalTotalTopics)
                    break;

            }
        }
        public void runAnotherThread(int finishThread)
        {
            if (trackTopics < globalTotalTopics)
            {

                pttlObject[trackTopics] = new ProcessTopicTopLinksClass();
                pttlObject[trackTopics].textWindow = textWindow;
                pttlObject[trackTopics].MainWindow = this;
                pttlObject[trackTopics].topicDirectory = mainFolder;
                pttlObject[trackTopics].topicFileName = "topic" + (trackTopics + 1) + ".txt";
                pttlObject[trackTopics].directory = "" + (trackTopics + 1);


                crawlerObject[trackTopics] = new ACrawlerClass();
                crawlerObject[trackTopics].textWindow = textWindow;

                crawlerObject[trackTopics].MainWindow = this;
                crawlerObject[trackTopics].pttlObject = pttlObject[trackTopics];
                crawlerObject[trackTopics].addSeedDocs();

                pttlObject[trackTopics].makeTrainTest();
                crawlerObject[trackTopics].threadActive = 1;


                // for (int i = 0; i < 100; i++)
                {
                    thread[trackTopics] = new Thread(new ParameterizedThreadStart(crawlerObject[trackTopics].startCrawling));
                }
                // for (int i = 0; i < 100; i++)
                thread[trackTopics].Start(trackTopics);

                trackTopics++;
            }

            thread[finishThread].Abort();


        }
        public void suspendRunAnotherThread(int finishThread)
        {

            mutex.WaitOne();

            if (trackTopics < globalTotalTopics)
            {

                pttlObject[trackTopics] = new ProcessTopicTopLinksClass();
                pttlObject[trackTopics].textWindow = textWindow;
                pttlObject[trackTopics].MainWindow = this;
                pttlObject[trackTopics].topicDirectory = mainFolder;
                pttlObject[trackTopics].topicFileName = "topic" + (trackTopics + 1) + ".txt";
                pttlObject[trackTopics].directory = "" + (trackTopics + 1);


                crawlerObject[trackTopics] = new ACrawlerClass();
                crawlerObject[trackTopics].textWindow = textWindow;

                crawlerObject[trackTopics].MainWindow = this;
                crawlerObject[trackTopics].pttlObject = pttlObject[trackTopics];
                crawlerObject[trackTopics].addSeedDocs();

                pttlObject[trackTopics].makeTrainTest();
                crawlerObject[trackTopics].threadActive = 1;


                // for (int i = 0; i < 100; i++)
                {
                    thread[trackTopics] = new Thread(new ParameterizedThreadStart(crawlerObject[trackTopics].startCrawling));
                }
                // for (int i = 0; i < 100; i++)
                thread[trackTopics].Start(trackTopics);

                trackTopics++;
                mutex.ReleaseMutex();
                thread[finishThread].Suspend();
            }
            else
            {
                int minCount = 1000;
                int threadN = -1;
                for (int i = 0; i < trackTopics; i++)
                {
                    if (crawlerObject[i].count < PagesToDownloadPerTopic)
                    {
                        if (crawlerObject[i].count < minCount && i != finishThread)
                        {
                            if (thread[i].ThreadState == System.Threading.ThreadState.Suspended)
                            {
                                minCount = crawlerObject[i].count;
                                threadN = i;
                            }
                        }
                    }
                }
                if (threadN != -1)
                {
                    crawlerObject[threadN].totalDownload = 0;
                    if (thread[threadN].ThreadState == System.Threading.ThreadState.Suspended)
                        thread[threadN].Resume();

                    mutex.ReleaseMutex();

                    thread[finishThread].Suspend();
                }
                else
                    mutex.ReleaseMutex();

            }



            //MessageBox.Show("ss" + finishThread);
        }


        private void button1_Click(object sender, System.EventArgs e)
        {

            if (pauseButton.Text == "Pause")
            {
                for (int i = 0; i < trackTopics; i++)
                    thread[i].Suspend();

                pauseButton.Text = "Resume";
            }
            else
            {
                for (int i = 0; i < trackTopics; i++)
                    thread[i].Resume();

                pauseButton.Text = "Pause";
            }

        }

        private void label1_Click(object sender, System.EventArgs e)
        {

        }

        private void button1_Click_1(object sender, System.EventArgs e)
        {
            SetButtonsEnabled(false);

            // Satia: Determine number of topics by content of topics.txt, if it is 0
            string topicsFileName = this.mainFolder + "topics.txt";
            if (globalTotalTopics == 0 && File.Exists(topicsFileName))
            {
                globalTotalTopics = File.ReadAllLines(topicsFileName).Length;
            }

            // Check if all topicN.txt files exist
            for (int i = 1; i <= globalTotalTopics; i++)
            {
                if (!File.Exists(this.mainFolder + "topic" + i + ".txt"))
                {
                    MessageBox.Show(this, "Please generate the training sets first.",
                        "Error", MessageBoxButtons.OK);
                    SetButtonsEnabled(true);
                    return;
                }
            }

            totalThread = globalTotalTopics;

            for (int cc = 0; cc < totalThread; cc++)
            {
                pttlObject[cc] = new ProcessTopicTopLinksClass();

                pttlObject[cc].textWindow = textWindow;
                pttlObject[cc].MainWindow = this;
                pttlObject[cc].topicDirectory = mainFolder;
                pttlObject[cc].topicFileName = "topic" + (cc + 1) + ".txt";
                pttlObject[cc].directory = "" + (cc + 1);
                pttlObject[cc].downloadC = 0;
                thread[cc] = new Thread(new ThreadStart(pttlObject[cc].downloadSeedDocs));
                thread[cc].Start();
            }
        }

        private void editTopicsButton_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show(this, "This will delete training sets, seed documents and progress for the current topics.\n" +
                        "Everything RC downloaded will not be deleted. Continue?",
                        "Continue?", MessageBoxButtons.OKCancel);
            if (result == DialogResult.Cancel)
            {
                return;
            }

            string topicsFileName = this.mainFolder + "topics.txt";
            // Create an empty topics.txt file if there is none
            if (!File.Exists(topicsFileName))
            {
                using (File.Create(topicsFileName)) { }
            }
            try
            {
                // Delete all files in this folder expect topics.txt
                foreach (string file in Directory.GetFiles(mainFolder).Where((file) => !new FileInfo(file).Name.Equals("topics.txt")))
                {
                    File.Delete(file);
                }
                // Delete all directories
                foreach (string dir in Directory.GetDirectories(mainFolder))
                {
                    Directory.Delete(dir, true);
                }
                // Open in standard editor.
                Process.Start(topicsFileName);
            }
            catch (Exception exp)
            {
                MessageBox.Show(this, "Could not open the topics file: " + exp.Message,
                        "Error", MessageBoxButtons.OK);
            }
        }

        private void generateTrainingSetButton_Click(object sender, EventArgs e)
        {
            SetButtonsEnabled(false);
            new Thread(GenerateTrainingSets).Start();
        }

        /// <summary>
        /// Generates the training sets.
        /// </summary>
        private void GenerateTrainingSets()
        {
            string topicsFileName = this.mainFolder + "topics.txt";
            // Create an empty topics.txt file if there is none
            if (!File.Exists(topicsFileName))
            {
                using (File.Create(topicsFileName)) { }
            }

            string[] topics = File.ReadAllLines(topicsFileName);
            if (topics.Length == 0)
            {
                MessageBox.Show("Please define some topics first.",
                        "Error", MessageBoxButtons.OK);
                SetButtonsEnabled(true);
                return;
            }

            // Read the file for the negative seed links, which are equal for all topics at the moment
            string negativeSeedLinks = File.ReadAllText("negativeSeedLinks.txt");

            // Loop through all topics in topics.txt
            for (int i = 0; i < topics.Length; i++)
            {
                // Create the file, possibly overwriting
                string topicFileName = this.mainFolder + "topic" + (i + 1) + ".txt";
                SetRichText("Generating training set for " + topics[i] + "\n");
                using (StreamWriter topicFileWriter = new StreamWriter(File.Create(topicFileName)))
                {
                    // Write the negative seed links into the file
                    topicFileWriter.WriteLine(negativeSeedLinks);
                    // Google 30 positive links for the topic
                    List<string> positiveSeedLinks = GetGoogleResults(topics[i], 30);
                    foreach (string positiveSeedLink in positiveSeedLinks)
                    {
                        topicFileWriter.WriteLine(positiveSeedLink);
                    }
                }
            }

            MessageBox.Show("Generated the training sets for " + topics.Length + " topics.",
                        "Success", MessageBoxButtons.OK);
            SetButtonsEnabled(true);
        }

        /// <summary>
        /// Enables or disables a control in athread safe way.
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
        /// Enables or disables all buttons except the Pause/Resume button.
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
                startButton.Enabled = enabled;
                downloadSeedButton.Enabled = enabled;
                editTopicsButton.Enabled = enabled;
                generateTrainingSetButton.Enabled = enabled;
            }
        }

        #region Google search

        /// <summary>
        /// Gets up to maxAmount URLs from Google searching the searchString.
        /// </summary>
        /// <param name="searchString">The string to search for.</param>
        /// <param name="maxAmount">The maximum number of results.</param>
        /// <returns>The URLs.</returns>
        private List<string> GetGoogleResults(string searchString, int maxAmount)
        {
            WebClient client = new WebClient();
            int pageNum = 1;
            List<string> result = new List<string>();

            while (result.Count < maxAmount)
            {
                string query = ConstructGoogleSearch(searchString, pageNum);
                string page = client.DownloadString(query);
                List<string> results = ExtractGoogleResults(page);
                if (results.Count == 0)
                {
                    break;
                }
                result.AddRange(results);
                pageNum++;
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
                        currUri = HTML_TAG_REGEX.Replace(currUri, "");
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
