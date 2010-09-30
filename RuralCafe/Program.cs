using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.IO;
using System.Threading;
//using BzReader;

namespace RuralCafe
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            //string test = RequestObject.hashedFileName("www.msnbc.com");
            StartRuralCafe();
            //CacheIndexer.IndexSquidLog("urls.txt");
            //StartBenchmarking();
        }

        private static void StartRuralCafe()
        {
            //Application.EnableVisualStyles();
            //Application.SetCompatibleTextRenderingDefault(false);
            //Application.Run(new Form1());
            Console.WindowWidth = Console.LargestWindowWidth;
            Console.WindowHeight = Console.LargestWindowHeight;
            Console.SetWindowPosition(0, 0);
            // fill extension map
            Util.FillExtMap();
            // fill the URL encoding map
            //GenericRequest.FillURLEncodingMap();

            // load Configuration Settings
            LoadConfigFile();

            /* JJJ: Disabled query suggestions
            // load LDC data
            NGrams.Load1Grams();
            NGrams.Load2Grams();
            NGrams.Load3Grams();
            NGrams.Load4Grams();
            */

            // start the local proxy
            StartLocalProxy();

            // start the remote proxy
            if (REMOTE_PROXY_IP_ADDRESS != null)
            {
                StartRemoteProxy();
            }
        }

        // Local Proxy Settings
        public static IPAddress LOCAL_PROXY_IP_ADDRESS;
        public static int LOCAL_PROXY_LISTEN_PORT;
        public static string DEFAULT_SEARCH_PAGE;

        // Remote Proxy Settings
        public static IPAddress REMOTE_PROXY_IP_ADDRESS;
        public static int REMOTE_PROXY_LISTEN_PORT;

        // Remote Remote Proxy Settings (behind Amrita firewall)
        public static IPAddress EXTERNAL_PROXY_IP_ADDRESS;
        public static int EXTERNAL_PROXY_LISTEN_PORT;
        public static string EXTERNAL_PROXY_LOGIN;
        public static string EXTERNAL_PROXY_PASS;

        public static int DEFAULT_QUOTA;
        public static int DEFAULT_DEPTH;
        public static int DEFAULT_LOW_WATERMARK;
        public static int DEFAULT_MAX_DOWNLOAD_SPEED;

        // Path Settings
        public static string LOCAL_PROXY_PATH = Directory.GetCurrentDirectory() + @"\LocalProxy\";
        public static string REMOTE_PROXY_PATH = Directory.GetCurrentDirectory() + @"\RemoteProxy\";
        public static string PACKAGE_PATH = @"Packages\";
        public static string LOGS_PATH = @"Logs\";
        public static string INDEX_PATH;
        public static string LOCAL_CACHE_PATH; // = @"c:\cygwin\home\jchen\files-mathematics\"; //LOCAL_PROXY_PATH + @"Cache\"; //"d:\\mathematics-61\\";
        public static string REMOTE_CACHE_PATH;// = REMOTE_PROXY_PATH + @"Cache\";
        public static string WIKI_DUMP_FILE;

        // load the configuration file for all the proxies
        static void LoadConfigFile()
        {
            // create reader & open file
            string s = System.IO.File.ReadAllText("config.txt");

            string[] fields = s.Split('\n');

            Dictionary<string, string> configFields = new Dictionary<string, string>();
            foreach (string entry in fields)
            {
                // ignore whitespace lines
                if (entry.Trim().Equals(""))
                {
                    continue;
                }
                string[] mapEntry = entry.Split('=');
                configFields.Add(mapEntry[0].Trim(), mapEntry[1].Trim());
            }

            try
            {
                // Local Proxy Settings
                LOCAL_PROXY_IP_ADDRESS = IPAddress.Parse(configFields["LOCAL_PROXY_IP_ADDRESS"]);
                LOCAL_PROXY_LISTEN_PORT = Int32.Parse(configFields["LOCAL_PROXY_LISTEN_PORT"]);

                INDEX_PATH = configFields["INDEX_PATH"];
                LOCAL_CACHE_PATH = configFields["LOCAL_CACHE_PATH"];
                if (!LOCAL_CACHE_PATH.Contains(":\\"))
                {
                    LOCAL_CACHE_PATH = LOCAL_PROXY_PATH + LOCAL_CACHE_PATH;
                }
                WIKI_DUMP_FILE = configFields["WIKI_DUMP_FILE"]; //d:\wikipedia\enwiki-20090520-pages-articles.xml.bz2

                // Remote Proxy Settings
                if (configFields["REMOTE_PROXY_IP_ADDRESS"].Equals(""))
                {
                    REMOTE_PROXY_IP_ADDRESS = null;
                }
                else
                {
                    REMOTE_PROXY_IP_ADDRESS = IPAddress.Parse(configFields["REMOTE_PROXY_IP_ADDRESS"]);
                }
                if (configFields["REMOTE_PROXY_LISTEN_PORT"].Equals(""))
                {
                    REMOTE_PROXY_LISTEN_PORT = 0;
                }
                else
                {
                    REMOTE_PROXY_LISTEN_PORT = Int32.Parse(configFields["REMOTE_PROXY_LISTEN_PORT"]);
                }

                REMOTE_CACHE_PATH = configFields["REMOTE_CACHE_PATH"];
                if (!REMOTE_CACHE_PATH.Contains(":\\")) {
                    REMOTE_CACHE_PATH = REMOTE_PROXY_PATH + REMOTE_CACHE_PATH;
                }

                // External Proxy Settings (behind Amrita firewall)
                if (configFields["EXTERNAL_PROXY_IP_ADDRESS"].Equals(""))
                {
                    EXTERNAL_PROXY_IP_ADDRESS = null;
                    EXTERNAL_PROXY_LISTEN_PORT = 0;
                    EXTERNAL_PROXY_LOGIN = "";
                    EXTERNAL_PROXY_PASS = "";
                }
                else
                {
                    EXTERNAL_PROXY_IP_ADDRESS = IPAddress.Parse(configFields["EXTERNAL_PROXY_IP_ADDRESS"]);
                    EXTERNAL_PROXY_LISTEN_PORT = Int32.Parse(configFields["EXTERNAL_PROXY_LISTEN_PORT"]);
                    EXTERNAL_PROXY_LOGIN = configFields["EXTERNAL_PROXY_LOGIN"];
                    EXTERNAL_PROXY_PASS = configFields["EXTERNAL_PROXY_PASS"];
                }
                DEFAULT_SEARCH_PAGE = configFields["DEFAULT_SEARCH_PAGE"];
                DEFAULT_QUOTA = Int32.Parse(configFields["DEFAULT_QUOTA"]);
                DEFAULT_DEPTH = Int32.Parse(configFields["DEFAULT_DEPTH"]);
                DEFAULT_LOW_WATERMARK = DEFAULT_QUOTA / 20;
                DEFAULT_MAX_DOWNLOAD_SPEED = Int32.Parse(configFields["MAXIMUM_DOWNLOAD_SPEED"]);

                Console.WriteLine("LOCAL_PROXY_IP_ADDRESS: " + LOCAL_PROXY_IP_ADDRESS);
                Console.WriteLine("LOCAL_PROXY_LISTEN_PORT: " + LOCAL_PROXY_LISTEN_PORT);
                Console.WriteLine("REMOTE_PROXY_IP_ADDRESS: " + REMOTE_PROXY_IP_ADDRESS);
                Console.WriteLine("REMOTE_PROXY_LISTEN_PORT: " + REMOTE_PROXY_LISTEN_PORT);
                Console.WriteLine("EXTERNAL_PROXY_IP_ADDRESS: " + EXTERNAL_PROXY_IP_ADDRESS);
                Console.WriteLine("EXTERNAL_PROXY_LISTEN_PORT: " + EXTERNAL_PROXY_LISTEN_PORT);
                Console.WriteLine("EXTERNAL_PROXY_LOGIN: " + EXTERNAL_PROXY_LOGIN);
            }
            catch (Exception)
            {
                Console.WriteLine("Error parsing config.txt");
                Application.Exit();
            }
        }

        // start the local proxy
        public static void StartLocalProxy() {
            // Create the proxy
            LocalProxy localProxy = new LocalProxy(LOCAL_PROXY_IP_ADDRESS, LOCAL_PROXY_LISTEN_PORT, 
                LOCAL_PROXY_PATH, INDEX_PATH, LOCAL_CACHE_PATH, WIKI_DUMP_FILE, PACKAGE_PATH, LOGS_PATH);

            // set the remote proxy
            localProxy.SetRemoteProxy(REMOTE_PROXY_IP_ADDRESS, REMOTE_PROXY_LISTEN_PORT);
            localProxy.SetRuralCafeSearchPage(DEFAULT_SEARCH_PAGE);
            
            // load the blacklisted domains
            localProxy.LoadBlacklist("blacklist.txt");

            LocalRequest.DEFAULT_DEPTH = DEFAULT_DEPTH;
            // Start remote requester thread
            Thread localListenerThread = new Thread(new ThreadStart(localProxy.StartLocalListener));
            localListenerThread.Name = String.Format("localListenerThread");
            localListenerThread.Start();

            Thread requesterThread = new Thread(new ThreadStart(localProxy.StartDispatcher));
            requesterThread.Name = String.Format("requesterThread");
            requesterThread.Start();

            // listen for cc connection
        }

        // start the remote proxy
        public static RemoteProxy StartRemoteProxy()
        {
            RemoteRequest.DEFAULT_QUOTA = DEFAULT_QUOTA;
            RemoteRequest.DEFAULT_DEPTH = DEFAULT_DEPTH;
            RemoteRequest.DEFAULT_LOW_WATERMARK = DEFAULT_LOW_WATERMARK;
            //RemoteRequest.DEFAULT_MAX_DOWNLOAD_SPEED = DEFAULT_MAX_DOWNLOAD_SPEED;

            // Create the proxy
            RemoteProxy remoteProxy = new RemoteProxy(REMOTE_PROXY_IP_ADDRESS, REMOTE_PROXY_LISTEN_PORT, 
                REMOTE_PROXY_PATH, REMOTE_CACHE_PATH, PACKAGE_PATH, LOGS_PATH);

            // set the gateway proxy info and login for the remote proxy
            if (EXTERNAL_PROXY_IP_ADDRESS != null)
            {
                remoteProxy.SetRemoteProxy(EXTERNAL_PROXY_IP_ADDRESS, EXTERNAL_PROXY_LISTEN_PORT,
                    EXTERNAL_PROXY_LOGIN, EXTERNAL_PROXY_PASS);
            }
            // XXX: move this into the constructor or something
            RemoteProxy.DEFAULT_MAX_DOWNLOAD_SPEED = DEFAULT_MAX_DOWNLOAD_SPEED;

            // load hte blacklisted domains
            remoteProxy.LoadBlacklist("blacklist.txt");

            // Start remote requester thread
            Thread remoteListenerThread = new Thread(new ThreadStart(remoteProxy.StartRemoteListener));
            remoteListenerThread.Name = String.Format("remoteListenerThread");
            remoteListenerThread.Start();

            // listen for cc connection
            return remoteProxy;
        }

        // benchmarking function rather than start RuralCafe
        // currently only takes in simple log format of "google.txt"
        static void StartBenchmarking()
        {
            //Application.EnableVisualStyles();
            //Application.SetCompatibleTextRenderingDefault(false);
            //Application.Run(new Form1());
            Console.WindowWidth = Console.LargestWindowWidth;
            Console.WindowHeight = Console.LargestWindowHeight;
            Console.SetWindowPosition(0, 0);
            // fill extension map
            Util.FillExtMap();
            // fill the URL encoding map
            //GenericRequest.FillURLEncodingMap();

            // load Configuration Settings
            LoadConfigFile();

            /* skip this stuff for benchmarking
            // load LDC data
            LoadLDCData();

            // start the local proxy
            StartLocalProxy();
            */

            // start the remote proxy
            RemoteProxy remoteProxy = StartRemoteProxy();

            // parse the input file line by line
            // create reader & open file
            TextReader tr = new StreamReader("google.txt");

            uint linesParsed = 0;
            uint requestsMade = 0;
            // read a line and convert to lowercase
            string line = tr.ReadLine();
            string[] lineTokens;

            while (line != null)
            {
                linesParsed++;
                line = line.ToLower();

                lineTokens = line.Split('\t');
                // maximum array length is set to 100
                if (lineTokens.Length >= 100 || lineTokens.Length < 19)
                {
                    Console.WriteLine("Error, too many tokens to fit in array");
                    // read the next line
                    line = tr.ReadLine();
                    continue;
                }

                // make sure that its actually a search query
                string urlRequest = lineTokens[18];
                if (urlRequest.StartsWith("http://www.google.com/search?"))
                {
                    if (urlRequest.Length > 200)
                    {
                        // read the next line
                        line = tr.ReadLine();
                        continue;
                    }

                    Console.WriteLine(urlRequest);
                    // it is, so do stuff

                    /*
                    // check if the query has already been processed before
                    if (IsCached(fileName))
                    {
                        // skip cached files
                        continue;
                    }
                    */

                    RemoteRequest remoteRequest = new RemoteRequest(remoteProxy, null);
                    // XXX: check for validity of the urlRequest
                    remoteRequest._requestObject = new RequestObject(remoteProxy, urlRequest);

                    // process one search query at a time
                    remoteRequest.BackdoorGo();

                    // pause
                    Thread.Sleep(1500);
                }
                requestsMade++;

                if (requestsMade == 1000)
                {
                    SaveBenchmarkValues("linksOnResultsPage.out", RemoteRequest.linksOnResultsPage);
                    SaveBenchmarkValues("imagesOnResultsPage.out", RemoteRequest.imagesOnResultsPage);
                    SaveBenchmarkValues("imagesOnTargetPage.out", RemoteRequest.imagesOnTargetPage);
                }

                /* just to show that we're processing...
                if (DateTime.Now.Subtract(displayDotTimer).TotalMinutes > 0)
                {
                    Console.Write(".");
                    displayDotTimer = DateTime.Now;
                }*/

                // read the next line
                line = tr.ReadLine();
            }

            // close the stream
            tr.Close();

            // pause
            Console.ReadLine();
        }

        // helper to save benchmark values
        public static void SaveBenchmarkValues(string fileName, List<int> values)
        {
            TextWriter tw = new StreamWriter(fileName, true);

            foreach (int number in values)
            {
                tw.Write(number + "\n");
            }

            tw.Close();
        }
    }
}
