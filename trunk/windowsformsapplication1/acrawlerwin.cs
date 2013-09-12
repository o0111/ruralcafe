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

namespace WindowsFormsApplication1
{


    public partial class ACrawlerWin : Form
    {
        private Thread[] thread = new Thread[200];
        private ACrawlerClass[] crawlerObject = new ACrawlerClass[200];
        private ProcessTopicTopLinksClass[] pttlObject = new ProcessTopicTopLinksClass[200];
        private int totalThread = 0;
        private int trackTopics = 0;
        public Mutex mutex = new Mutex();
        public string mainFolder;
        public int globalTotalTopics;
        public int countDead;

        /// <summary>
        /// Process an URI in any way.
        /// </summary>
        /// <param name="uri">The URI.</param>
        public delegate void ProcessURI(string uri);
        /// <summary>
        /// The actual delegate.
        /// </summary>
        public ProcessURI processUriDelegate;

        public ACrawlerWin()
        {
            InitializeComponent();
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
                    threadProgressText.Text += "Topic#" + i + ": Relevant Links:" + crawlerObject[i].count + "/Total Crawled:" + crawlerObject[i].totalDownload + "/Seed Links: " + crawlerObject[i].toCrawlList.Count + "\n";
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
                    MessageBox.Show("Downloading seed documents completed");
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

        private void LoadButton_Click(object sender, System.EventArgs e)
        {
            /*
            WebClient webClient = new WebClient();

            string apiKey = "";
            string cx = "";
            string query = "shariq bashir";

            string Json = webClient.DownloadString(String.Format("https://www.googleapis.com/customsearch/v1?key={0}&cx={1}&q={2}&alt=json", apiKey, cx, query));
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            GoogleSearchItem results = serializer.Deserialize<GoogleSearchItem>(Json);
            MessageBox.Show("complete  " + serializer.Serialize(results));*/

            startButton.Enabled = false;
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
                    if (crawlerObject[i].count < 500)
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
            downloadSeedButton.Enabled = false;

            // Satia: Determine number of topics by content of topics.txt, if it is 0
            string topicsFileName = this.mainFolder + "topics.txt";
            if (globalTotalTopics == 0 && File.Exists(topicsFileName))
            {
                globalTotalTopics = File.ReadAllLines(topicsFileName).Length;
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
                /*        while(true)
                        {
                            if (pttlObject[cc].downloadC == 1)
                                break;

                        //    MessageBox.Show("hello");

                        //    Thread.Sleep(500);
                        }*/
            }





        }

        private void editTopicsButton_Click(object sender, EventArgs e)
        {
            string topicsFileName = this.mainFolder + "topics.txt";
            // Create an empty topics.txt file if there is none
            if (!File.Exists(topicsFileName))
            {
                using (File.Create(topicsFileName)) { }
            }
            // Open in standard editor.
            try
            {
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
            string topicsFileName = this.mainFolder + "topics.txt";
            // Create an empty topics.txt file if there is none
            if (!File.Exists(topicsFileName))
            {
                using (File.Create(topicsFileName)) { }
            }

            string[] topics = File.ReadAllLines(topicsFileName);
            if (topics.Length == 0)
            {
                MessageBox.Show(this, "Please define some topics first.",
                        "Error", MessageBoxButtons.OK);
                return;
            }

            // Read the file for the negative seed links, which are equal for all topics at the moment
            string negativeSeedLinks = File.ReadAllText("negativeSeedLinks.txt");

            // Loop through all topics in topics.txt
            for (int i = 0; i < topics.Length; i++)
            {
                // Create the file, possibly overwriting
                string topicFileName = this.mainFolder + "topic" + (i + 1) + ".txt";
                using (StreamWriter topicFileWriter = new StreamWriter(File.Create(topicFileName)))
                {
                    // Write the negative seed links inti the file
                    topicFileWriter.WriteLine(negativeSeedLinks);
                    // TODO Google the topic for 30 positive links
                }
            }
        }
    }
}
