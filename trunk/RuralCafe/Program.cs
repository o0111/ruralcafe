/*
   Copyright 2010 Jay Chen

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.

*/

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
using System.Configuration;
using log4net;
using System.Collections.Specialized;
using Util;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Security.Principal;
using System.Diagnostics;
using System.Reflection;
using RuralCafe.Crawler;

namespace RuralCafe
{
    /// <summary>
    /// The entry point static class for Rural Cafe.
    /// </summary>
    [Guid("97E8E8E6-FF51-4468-8DDE-CBD8F32BF3A4")]
    public static class Program
    {
        /// <summary>
        /// If this is x, and the quota is q, the DEFAULT_LOW_WATERMARK will be q/x.
        /// </summary>
        private const int LOW_WATERMARK_DIVIDOR_QUOTA = 20;
        /// <summary>
        /// The percentage RC can use of the free disk space on C, by default.
        /// </summary>
        private const double PERCENTAGE_RC_CAN_USE_OF_FREE_SPACE = 0.3;
        /// <summary>
        /// By default, the max. cache size will not be greater than 150 GB.
        /// </summary>
        private const long CACHE_SIZE_MIB_MAX_DEFAULT = 150 * 1024;

        // Path Settings
        private static readonly string PACKAGE_PATH = "Packages" + Path.DirectorySeparatorChar;
        public static readonly string LOCAL_PROXY_PATH = "LocalProxy" + Path.DirectorySeparatorChar;
        public static readonly string REMOTE_PROXY_PATH = "RemoteProxy" + Path.DirectorySeparatorChar;

        // FIXME this should not be public, but we have an ugly workaround
        /// <summary>The path to the index for the local proxy.</summary>
        public static string INDEX_PATH;
        private static string LOCAL_CACHE_PATH;
        private static string REMOTE_CACHE_PATH;
        private static string WIKI_DUMP_FILE;

        // The logger
        private static readonly ILog _logger = LogManager.GetLogger(typeof(Program));

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        private static void Main()
        {
            if (!IsRunAsAdministrator())
            {
                // It is not possible to launch a ClickOnce app as administrator directly, so instead we launch the
                // app as administrator in a new process.
                ProcessStartInfo processInfo = new ProcessStartInfo(Assembly.GetExecutingAssembly().CodeBase);

                // The following properties run the new process as administrator
                processInfo.UseShellExecute = true;
                processInfo.Verb = "runas";

                // Start the new process
                try
                {
                    Process.Start(processInfo);
                }
                catch (Exception)
                {
                    // The user did not allow the application to run as administrator
                    MessageBox.Show("Sorry, this application must be run as Administrator.");
                }

                // Shut down the current process
                Environment.Exit(-1);
            }
            else
            {
                // We are running as administrator
                MyMain();
            }
        }

        /// <summary>
        /// Checks whether we have admin rights.
        /// </summary>
        /// <returns>If we have admin rights.</returns>
        private static bool IsRunAsAdministrator()
        {
            WindowsIdentity wi = WindowsIdentity.GetCurrent();
            WindowsPrincipal wp = new WindowsPrincipal(wi);

            return wp.IsInRole(WindowsBuiltInRole.Administrator);
        }

        /// <summary>
        /// Now we're defenitely admin and can start everything.
        /// </summary>
        private static void MyMain()
        {
            StartRuralCafe();

            // for adding a list of URLs to the index
            //CacheIndexer.IndexSquidLog("urls.txt");

            // for analyzing search result pages
            //AnalysisTools.CountEmbeddedObjects();

            // Register our handler as event handler when the console exits
            // Like this we can do cleanup stuff before closing
            handler = new ConsoleEventDelegate(ConsoleEventCallback);
            SetConsoleCtrlHandler(handler, true);
        }

        #region Console shutdown delegate stuff
        /// <summary>
        /// Our cleanup method. It just calls another delegate (shutDownDelegates),
        /// where anybody can add his method.
        /// </summary>
        /// <param name="eventType">The console event type.</param>
        /// <returns>false.</returns>
        static bool ConsoleEventCallback(int eventType)
        {
            shutDownDelegates();
            return false;
        }
        // Keeps it from getting garbage collected
        static ConsoleEventDelegate handler;
        private delegate bool ConsoleEventDelegate(int eventType);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleCtrlHandler(ConsoleEventDelegate callback, bool add);

        /// <summary>
        /// A delegate with no parameters or return value used for cleaning up.
        /// </summary>
        public delegate void ShutDownDelegate();
        /// <summary>
        /// All cleanup delegates are gathered here.
        /// </summary>
        private static ShutDownDelegate shutDownDelegates;
        /// <summary>
        /// Adds a shutdown delegate to be executed when the console is about to close.
        /// </summary>
        /// <param name="d">The delegate</param>
        public static void AddShutDownDelegate(ShutDownDelegate d)
        {
            // Add delegate to our shutdown delegates
            shutDownDelegates += d;
            // Also add to system shutdown/user logoff delegates
            SystemEvents.SessionEnding += (a, b) => d();
        }
        #endregion

        /// <summary>
        /// Starts RuralCafe
        /// </summary>
        private static void StartRuralCafe()
        {
            Console.WindowWidth = Console.LargestWindowWidth - 4;
            Console.WindowHeight = Console.LargestWindowHeight - 10;
            Console.SetWindowPosition(0, 0);

            // Adjust max cache size settings if necessary
            AdjustMaxCacheSizeSettings();

            // Setting form at startup.
            SettingsForm sf = new SettingsForm();
            DialogResult dialogResults = sf.ShowDialog();
            SaveConfigs();

            // Config Logger
            RCLogger.InitLogger();

            // Log configuration
            LogConfiguration();

            // Configure Windows
            ConfigureWindows();

            // Reset global HTTP proxy to null, if one is set.
            WebRequest.DefaultWebProxy = null;

            bool localProxyStarted = false;
            // start the local proxy
            if (!String.IsNullOrEmpty(Properties.Connection.Default.LOCAL_PROXY_IP_ADDRESS))
            {
                localProxyStarted = true;
                // If the dialogResults are yes ("Save and start crawler" button")
                // we start the crawler
                StartLocalProxy(dialogResults == DialogResult.Yes);
            }

            // start the remote proxy only if we're not starting the local proxy
            // or both proxies are running on the same box
            if ((!localProxyStarted ||
                Properties.Connection.Default.LOCAL_PROXY_IP_ADDRESS.Equals(
                Properties.Connection.Default.REMOTE_PROXY_IP_ADDRESS)) &&
                !String.IsNullOrEmpty(Properties.Connection.Default.REMOTE_PROXY_IP_ADDRESS))
            {
                StartRemoteProxy();
            }
        }

        /// <summary>
        /// If the max cache size settings are 0, sets it to the minumu of
        /// (together) 30 % of the free space on c: and 150 GB.
        /// </summary>
        private static void AdjustMaxCacheSizeSettings()
        {
            // Get free space in MiB on current drive
            string driveLetter = Path.GetPathRoot(Environment.CurrentDirectory);
            long mbFree = Utils.GetTotalFreeSpaceBytes(driveLetter) / (1024 * 1024);

            bool localUsed = false;
            bool remoteUsed = false;
            double percentagePerProxy = PERCENTAGE_RC_CAN_USE_OF_FREE_SPACE;

            // See which proxies are used
            if (!String.IsNullOrEmpty(Properties.Connection.Default.LOCAL_PROXY_IP_ADDRESS))
            {
                if (!String.IsNullOrEmpty(Properties.Connection.Default.REMOTE_PROXY_IP_ADDRESS) &&
                Properties.Connection.Default.LOCAL_PROXY_IP_ADDRESS.Equals(Properties.Connection.Default.REMOTE_PROXY_IP_ADDRESS))
                {
                    // Both proxies are running
                    percentagePerProxy = PERCENTAGE_RC_CAN_USE_OF_FREE_SPACE / 2;
                    localUsed = remoteUsed = true;
                }
                else
                {
                    // Only local proxy is running
                    percentagePerProxy = PERCENTAGE_RC_CAN_USE_OF_FREE_SPACE;
                    localUsed = true;
                }
            }
            else if (!String.IsNullOrEmpty(Properties.Connection.Default.REMOTE_PROXY_IP_ADDRESS) &&
                Properties.Connection.Default.LOCAL_PROXY_IP_ADDRESS.Equals(Properties.Connection.Default.REMOTE_PROXY_IP_ADDRESS))
            {
                // only remote proxy is running
                percentagePerProxy = PERCENTAGE_RC_CAN_USE_OF_FREE_SPACE;
                remoteUsed = true;
            }
            
            // Adjust settings for both proxies
            if (localUsed && Properties.Files.Default.LOCAL_MAX_CACHE_SIZE_MIB == 0)
            {
                Properties.Files.Default.LOCAL_MAX_CACHE_SIZE_MIB = (int)Math.Min(percentagePerProxy * mbFree, CACHE_SIZE_MIB_MAX_DEFAULT);
            }
            if (remoteUsed && Properties.Files.Default.REMOTE_MAX_CACHE_SIZE_MIB == 0)
            {
                Properties.Files.Default.REMOTE_MAX_CACHE_SIZE_MIB = (int)Math.Min(percentagePerProxy * mbFree, CACHE_SIZE_MIB_MAX_DEFAULT);
            }

            // Save
            Properties.Files.Default.Save();
        }

        /// <summary>
        /// Logs all configuration items. This will only be printed if LogLevel >= INFO.
        /// </summary>
        private static void LogConfiguration()
        {
            // Setting.settings
            foreach (SettingsPropertyValue currentProperty in Properties.Settings.Default.PropertyValues)
            {
                _logger.Info(currentProperty.Name + ": " + currentProperty.PropertyValue);
            }
            // all other .settings files in Properties
            foreach (SettingsPropertyValue currentProperty in Properties.Connection.Default.PropertyValues)
            {
                _logger.Info("Connection - " + currentProperty.Name + ": " + currentProperty.PropertyValue);
            }
            foreach (SettingsPropertyValue currentProperty in Properties.Files.Default.PropertyValues)
            {
                _logger.Info("Files - " + currentProperty.Name + ": " + currentProperty.PropertyValue);
            }
            foreach (SettingsPropertyValue currentProperty in Properties.Network.Default.PropertyValues)
            {
                _logger.Info("Network - " + currentProperty.Name + ": " + currentProperty.PropertyValue);
            }
        }

        /// <summary>
        /// Saves the configuration.
        /// </summary>
        public static void SaveConfigs()
        {
            // Path and stuff Configuration Settings
            INDEX_PATH = Properties.Files.Default.LOCAL_INDEX_PATH + Path.DirectorySeparatorChar;
            if (!INDEX_PATH.Contains(":\\"))
            {
                INDEX_PATH = Properties.Files.Default.BASE_DIR + Path.DirectorySeparatorChar + LOCAL_PROXY_PATH + INDEX_PATH;
            }
            LOCAL_CACHE_PATH = Properties.Files.Default.LOCAL_CACHE_PATH + Path.DirectorySeparatorChar;
            if (!LOCAL_CACHE_PATH.Contains(":\\"))
            {
                LOCAL_CACHE_PATH = Properties.Files.Default.BASE_DIR + Path.DirectorySeparatorChar + LOCAL_PROXY_PATH + LOCAL_CACHE_PATH;
            }
            REMOTE_CACHE_PATH = Properties.Files.Default.REMOTE_CACHE_PATH + Path.DirectorySeparatorChar;
            if (!REMOTE_CACHE_PATH.Contains(":\\"))
            {
                REMOTE_CACHE_PATH = Properties.Files.Default.BASE_DIR + Path.DirectorySeparatorChar + REMOTE_PROXY_PATH + REMOTE_CACHE_PATH;
            }
            WIKI_DUMP_FILE = Properties.Files.Default.LOCAL_WIKI_DUMP_FILE;
            if(!WIKI_DUMP_FILE.Contains(":\\"))
            {
                WIKI_DUMP_FILE = Properties.Files.Default.BASE_DIR + Path.DirectorySeparatorChar + LOCAL_PROXY_PATH + WIKI_DUMP_FILE;
            }
        }

        /// <summary>
        /// Starts the local proxy.
        /// </summary>
        /// <param name="startCrawler">Whether the crawler should be started.</param>
        public static RCLocalProxy StartLocalProxy(bool startCrawler)
        {
            // create the proxy
            RCLocalProxy localProxy = new RCLocalProxy(
                IPAddress.Parse(Properties.Connection.Default.LOCAL_PROXY_IP_ADDRESS),
                Properties.Connection.Default.LOCAL_PROXY_LISTEN_PORT,
                Properties.Connection.Default.LOCAL_PROXY_HTTPS_PORT,
                Properties.Files.Default.BASE_DIR + Path.DirectorySeparatorChar + LOCAL_PROXY_PATH,
                INDEX_PATH,
                ((long)(Properties.Files.Default.LOCAL_MAX_CACHE_SIZE_MIB)) * 1024 * 1024,
                LOCAL_CACHE_PATH,
                WIKI_DUMP_FILE,
                PACKAGE_PATH);

            if (startCrawler)
            {
                CrawlerWrapper cw = new CrawlerWrapper(localProxy);
                cw.StartCrawler();
            }

            // set the remote proxy
            localProxy.SetRemoteProxy(IPAddress.Parse(Properties.Connection.Default.REMOTE_PROXY_IP_ADDRESS),
                Properties.Connection.Default.REMOTE_PROXY_LISTEN_PORT);
            localProxy.RemoteProxyHTTPSPort = Properties.Connection.Default.REMOTE_PROXY_HTTPS_PORT;

            // XXX: currently this doesn't work if the remote proxy must be reached through a firewall/gateway.
            // XXX: it would be a chain of 2 proxies anyway and needs tunneling support
            if (Properties.Connection.Default.REMOTE_PROXY_IP_ADDRESS != Properties.Connection.Default.LOCAL_PROXY_IP_ADDRESS)
            {
                // FIXME Either we must require a gateway to be set in this case or check if one is given...
                // set the gateway proxy info and login for the local proxy
                //localProxy.SetGatewayProxy(IPAddress.Parse(Properties.Settings.Default.EXTERNAL_PROXY_IP_ADDRESS), Properties.Settings.Default.EXTERNAL_PROXY_LISTEN_PORT,
                //                           Properties.Settings.Default.EXTERNAL_PROXY_LOGIN, Properties.Settings.Default.EXTERNAL_PROXY_PASS);
            }

            // set the RC search page
            localProxy.SetRCSearchPage(Properties.Files.Default.DEFAULT_SEARCH_PAGE);
            // Set network status and auto detection
            localProxy.NetworkStatus = Properties.Network.Default.NETWORK_STATUS;
            localProxy.DetectNetworkStatusAuto = Properties.Network.Default.DETECT_NETWORK_AUTO;

            // start local listener thread
            Thread localListenerThread = new Thread(new ThreadStart(localProxy.StartListener));
            localListenerThread.Name = "localListenerThread";
            localListenerThread.Start();

            // start local HTTPS listener thread
            Thread localHttpsListenerThread = new Thread(new ThreadStart(localProxy.StartHttpsListener));
            localHttpsListenerThread.Name = "localHTTPSListenerThread";
            localHttpsListenerThread.Start();

            // start local requester thread
            Thread localRequesterThread = new Thread(new ThreadStart(localProxy.StartDispatcher));
            localRequesterThread.Name = "localRequesterThread";
            localRequesterThread.Start();

            // Start the timer
            localProxy.StartPeriodicTimer();            

            // listen for cc connection
            return localProxy;
        }

        /// <summary>
        /// Start the remote proxy.
        /// </summary>
        public static RCRemoteProxy StartRemoteProxy()
        {
            // create the proxy
            RCRemoteProxy remoteProxy = new RCRemoteProxy(
                IPAddress.Parse(Properties.Connection.Default.REMOTE_PROXY_IP_ADDRESS),
                Properties.Connection.Default.REMOTE_PROXY_LISTEN_PORT,
                Properties.Connection.Default.REMOTE_PROXY_HTTPS_PORT,
                Properties.Files.Default.BASE_DIR + Path.DirectorySeparatorChar + REMOTE_PROXY_PATH,
                ((long)(Properties.Files.Default.REMOTE_MAX_CACHE_SIZE_MIB)) * 1024 * 1024,
                REMOTE_CACHE_PATH, 
                PACKAGE_PATH);

            /*
            // XXX: buggy...
            // XXX: currently only used if both proxies are running on the same machine
            if (REMOTE_PROXY_IP_ADDRESS == LOCAL_PROXY_IP_ADDRESS)
            {
                // set the gateway proxy info and login for the remote proxy
                remoteProxy.SetGatewayProxy(GATEWAY_PROXY_IP_ADDRESS, GATEWAY_PROXY_LISTEN_PORT,
                                            GATEWAY_PROXY_LOGIN, GATEWAY_PROXY_PASS);
            }*/

            // Remote Proxy is always online.
            remoteProxy.NetworkStatus = RCProxy.NetworkStatusCode.Online;

            // set the maximum downlink speed to the local proxy
            remoteProxy.MAXIMUM_DOWNLINK_BANDWIDTH = Properties.Network.Default.MAXIMUM_DOWNLOAD_SPEED;

            // set the default low watermark for each request
            RemoteRequestHandler.DEFAULT_LOW_WATERMARK = Properties.Settings.Default.DEFAULT_QUOTA / LOW_WATERMARK_DIVIDOR_QUOTA;

            // start remote listener thread
            Thread remoteListenerThread = new Thread(new ThreadStart(remoteProxy.StartListener));
            remoteListenerThread.Name = String.Format("remoteListenerThread");
            remoteListenerThread.Start();

            // start remote HTTPS listener thread
            Thread remoteHttpsListenerThread = new Thread(new ThreadStart(remoteProxy.StartHttpsListener));
            remoteHttpsListenerThread.Name = "remoteHTTPSListenerThread";
            remoteHttpsListenerThread.Start();

            // start remote requester thread
            Thread remoteRequesterThread = new Thread(new ThreadStart(remoteProxy.StartDispatcher));
            remoteRequesterThread.Name = String.Format("remoteRequesterThread");
            remoteRequesterThread.Start();

            // listen for cc connection
            return remoteProxy;
        }

        /// <summary>
        /// Configures Windows (registry) according to the user preferences.
        /// Changes DNS caching behaviour.
        /// </summary>
        private static void ConfigureWindows()
        {
            // Check if OS too old or Mac or Unix
            if(System.Environment.OSVersion.Platform != System.PlatformID.Win32NT)
            {
                // We can't do anything here
                return;
            }


            string regPath = @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters";
            int maxCacheTtl = Properties.Settings.Default.DNS_CACHE_TTL;
            int maxNegCacheTtl = 300;
            int cacheHashTableSize = 64000;
            int cacheHashTableBucketSize = 16;

            try
            {
                // Check if we're running on XP
                if (System.Environment.OSVersion.Version.Major == 5
                    && System.Environment.OSVersion.Version.Minor == 1)
                {
                    Utils.WriteRegistryKey(Registry.LocalMachine, regPath, "MaxCacheTtl", maxCacheTtl);
                    Utils.WriteRegistryKey(Registry.LocalMachine, regPath, "MaxNegativeCacheTtl", maxNegCacheTtl);
                }
                else
                {
                    // Hopefully we're running on 2000/Vista/7 where this should work.
                    Utils.WriteRegistryKey(Registry.LocalMachine, regPath, "MaxCacheEntryTtlLimit", maxCacheTtl);
                    Utils.WriteRegistryKey(Registry.LocalMachine, regPath, "MaxNegativeCacheTtl", maxNegCacheTtl);
                    Utils.WriteRegistryKey(Registry.LocalMachine, regPath, "CacheHashTableSize", cacheHashTableSize);
                    Utils.WriteRegistryKey(Registry.LocalMachine, regPath, "CacheHashTableBucketSize", cacheHashTableBucketSize);
                }
            }
            catch (Exception e)
            {
                _logger.Error("Could not change windows registry: ", e);
                return;
            }
            _logger.Info("Wrote DNS cache registry keys.");            
        }
    }
}
