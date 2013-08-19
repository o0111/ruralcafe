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
using RuralCafe.Util;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Security.Principal;
using System.Diagnostics;
using System.Reflection;

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

        // Path Settings
        private static readonly string LOCAL_PROXY_PATH = Directory.GetCurrentDirectory()
            + Path.DirectorySeparatorChar + "LocalProxy" + Path.DirectorySeparatorChar;
        private static readonly string REMOTE_PROXY_PATH = Directory.GetCurrentDirectory()
            + Path.DirectorySeparatorChar + "RemoteProxy" + Path.DirectorySeparatorChar;
        private static readonly string PACKAGE_PATH = "Packages" + Path.DirectorySeparatorChar;
        // FIXME this should not be public, but we have an ugly workaround
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
            // XXX check if positive for mega small screens
            Console.WindowWidth = Console.LargestWindowWidth - 4;
            Console.WindowHeight = Console.LargestWindowHeight - 10;
            Console.SetWindowPosition(0, 0);

            // Setting form at startup.
            SettingsForm sf = new SettingsForm();
            sf.ShowDialog();
            SaveConfigs();

            // Config Logger
            RCLogger.InitLogger();

            // Log configuration
            LogConfiguration();

            // Configure Windows
            ConfigureWindows();

            bool localProxyStarted = false;
            // start the local proxy
            if (Properties.Settings.Default.LOCAL_PROXY_IP_ADDRESS != null 
                && !Properties.Settings.Default.LOCAL_PROXY_IP_ADDRESS.Equals(""))
            {
                localProxyStarted = true;
                StartLocalProxy();
            }

            // start the remote proxy only if we're not starting the local proxy
            // or both proxies are running on the same box
            if ((!localProxyStarted || 
                Properties.Settings.Default.LOCAL_PROXY_IP_ADDRESS.Equals(
                Properties.Settings.Default.REMOTE_PROXY_IP_ADDRESS)) &&
                Properties.Settings.Default.REMOTE_PROXY_IP_ADDRESS != null
                && !Properties.Settings.Default.REMOTE_PROXY_IP_ADDRESS.Equals(""))
            {
                StartRemoteProxy();
            }
        }

        /// <summary>
        /// Logs all configuration items. This will only be printed if LogLevel >= INFO.
        /// </summary>
        private static void LogConfiguration()
        {
            foreach (SettingsPropertyValue currentProperty in Properties.Settings.Default.PropertyValues)
            {
                _logger.Info(currentProperty.Name + ": " + currentProperty.PropertyValue);
            }
        }

        /// <summary>
        /// Saves the configuration.
        /// </summary>
        public static void SaveConfigs()
        {
            // Path and stuff Configuration Settings
            INDEX_PATH = Properties.Settings.Default.INDEX_PATH + Path.DirectorySeparatorChar;
            if (!INDEX_PATH.Contains(":\\"))
            {
                INDEX_PATH = LOCAL_PROXY_PATH + INDEX_PATH;
            }
            LOCAL_CACHE_PATH = Properties.Settings.Default.LOCAL_CACHE_PATH + Path.DirectorySeparatorChar;
            if (!LOCAL_CACHE_PATH.Contains(":\\"))
            {
                LOCAL_CACHE_PATH = LOCAL_PROXY_PATH + LOCAL_CACHE_PATH;
            }
            REMOTE_CACHE_PATH = Properties.Settings.Default.REMOTE_CACHE_PATH + Path.DirectorySeparatorChar;
            if (!REMOTE_CACHE_PATH.Contains(":\\"))
            {
                REMOTE_CACHE_PATH = REMOTE_PROXY_PATH + REMOTE_CACHE_PATH;
            }
            WIKI_DUMP_FILE =  Properties.Settings.Default.WIKI_DUMP_DIR
                + Path.DirectorySeparatorChar + Properties.Settings.Default.WIKI_DUMP_FILE;
            if(!WIKI_DUMP_FILE.Contains(":\\"))
            {
                WIKI_DUMP_FILE = LOCAL_PROXY_PATH + WIKI_DUMP_FILE;
            }
        }

        /// <summary>
        /// Starts the local proxy.
        /// </summary>
        public static RCLocalProxy StartLocalProxy()
        {
            // create the proxy
            RCLocalProxy localProxy = new RCLocalProxy(IPAddress.Parse(Properties.Settings.Default.LOCAL_PROXY_IP_ADDRESS),
                Properties.Settings.Default.LOCAL_PROXY_LISTEN_PORT, LOCAL_PROXY_PATH, INDEX_PATH,
                ((long)(Properties.Settings.Default.LOCAL_MAX_CACHE_SIZE_MIB)) * 1024 * 1024, LOCAL_CACHE_PATH,
                WIKI_DUMP_FILE, PACKAGE_PATH);

            // set the remote proxy
            localProxy.SetRemoteProxy(IPAddress.Parse(Properties.Settings.Default.REMOTE_PROXY_IP_ADDRESS), 
                Properties.Settings.Default.REMOTE_PROXY_LISTEN_PORT);

            // XXX: currently this doesn't work if the remote proxy must be reached through a firewall/gateway.
            // XXX: it would be a chain of 2 proxies anyway and needs tunneling support
            if (Properties.Settings.Default.REMOTE_PROXY_IP_ADDRESS != Properties.Settings.Default.LOCAL_PROXY_IP_ADDRESS)
            {
                // FIXME Either we must require a gateway to be set in this case or check if one is given...
                // set the gateway proxy info and login for the local proxy
                //localProxy.SetGatewayProxy(IPAddress.Parse(Properties.Settings.Default.EXTERNAL_PROXY_IP_ADDRESS), Properties.Settings.Default.EXTERNAL_PROXY_LISTEN_PORT,
                //                           Properties.Settings.Default.EXTERNAL_PROXY_LOGIN, Properties.Settings.Default.EXTERNAL_PROXY_PASS);
            }

            // set the RC search page
            localProxy.SetRCSearchPage(Properties.Settings.Default.DEFAULT_SEARCH_PAGE);
            // Set network status and auto detection
            localProxy.NetworkStatus = Properties.Settings.Default.NETWORK_STATUS;
            localProxy.DetectNetworkStatusAuto = Properties.Settings.Default.DETECT_NETWORK_AUTO;

            // load the blacklisted domains
            localProxy.LoadBlacklist("blacklist.txt");

            // Log cache metrics
            // localProxy.ProxyCacheManager.LogCacheMetrics();

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

            // Start the clustering timer
            localProxy.StartClusteringTimer();            

            // listen for cc connection
            return localProxy;
        }

        /// <summary>
        /// Start the remote proxy.
        /// </summary>
        public static RCRemoteProxy StartRemoteProxy()
        {
            // create the proxy
            RCRemoteProxy remoteProxy = new RCRemoteProxy(IPAddress.Parse(Properties.Settings.Default.REMOTE_PROXY_IP_ADDRESS),
                Properties.Settings.Default.REMOTE_PROXY_LISTEN_PORT,
                REMOTE_PROXY_PATH, ((long)(Properties.Settings.Default.REMOTE_MAX_CACHE_SIZE_MIB)) * 1024 * 1024,
                REMOTE_CACHE_PATH, PACKAGE_PATH);

            /*
            // XXX: buggy...
            // XXX: currently only used if both proxies are running on the same machine
            if (REMOTE_PROXY_IP_ADDRESS == LOCAL_PROXY_IP_ADDRESS)
            {
                // set the gateway proxy info and login for the remote proxy
                remoteProxy.SetGatewayProxy(GATEWAY_PROXY_IP_ADDRESS, GATEWAY_PROXY_LISTEN_PORT,
                                            GATEWAY_PROXY_LOGIN, GATEWAY_PROXY_PASS);
            }*/

            // default remote proxy network status to online
            remoteProxy.NetworkStatus = global::RuralCafe.RCLocalProxy.NetworkStatusCode.Online;

            // set the maximum downlink speed to the local proxy
            remoteProxy.MAXIMUM_DOWNLINK_BANDWIDTH = Properties.Settings.Default.MAXIMUM_DOWNLOAD_SPEED;

            // load the blacklisted domains
            remoteProxy.LoadBlacklist("blacklist.txt");

            // set the default low watermark for each request
            RemoteRequestHandler.DEFAULT_LOW_WATERMARK = Properties.Settings.Default.DEFAULT_QUOTA / LOW_WATERMARK_DIVIDOR_QUOTA;

            // Log cache metrics
            // remoteProxy.ProxyCacheManager.LogCacheMetrics();

            // start remote listener thread
            Thread remoteListenerThread = new Thread(new ThreadStart(remoteProxy.StartListener));
            remoteListenerThread.Name = String.Format("remoteListenerThread");
            remoteListenerThread.Start();

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
