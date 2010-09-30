using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace RuralCafe
{
    public abstract class GenericProxy
    {
        public const string REMOTE_PROXY_NAME = "Remote Proxy";
        public const string LOCAL_PROXY_NAME = "Local Proxy";

        // blacklist
        public List<string> _blacklistedDomains = new List<string>();

        // XXX: shared by the local and remote proxies at the moment for the Amrita deployment
        public static int DEFAULT_MAX_DOWNLOAD_SPEED; // = 5000; // max speed in bytes per second

        protected IPAddress _listenAddress;
        protected int _listenPort;
        protected Logger _log;
        protected string _cachePath;
        protected string _packagePath;
        protected string _type;

        // remoteProxy
        protected WebProxy _remoteWebProxy;

        private static Object bandwidthMeasureObject = new Object();
        public static DateTime bandwidthMeasureStartTime = DateTime.Now;
        public static int bandwidthMeasureDataSent = 0;

        public string Name
        {
            get { return _type; }
        }
        public bool IsLocalProxy
        {
            get { return _type.Equals(LOCAL_PROXY_NAME); }
        }
        public bool IsRemoteProxy
        {
            get { return _type.Equals(REMOTE_PROXY_NAME); }
        }
        public string CachePath
        {
            get { return _cachePath; }
        }
        public string PackagePath
        {
            get { return _packagePath; }
        }
        public WebProxy RemoteWebProxy
        {
            get { return _remoteWebProxy; }
        }

        public GenericProxy(string proxyType, IPAddress listenAddress, int listenPort, 
            string proxyPath, string cachePath, string packagePath, string logPath)
        {
            _type = proxyType;
            // setup proxy listener variables
            _listenAddress = listenAddress;
            _listenPort = listenPort;

            //create and initialize the logger
            _log = new Logger(proxyType, proxyPath + logPath);

            // initialize the cache
            InitializePackageCache(proxyPath + packagePath);
        }

        // called once to make sure the cache directory is intact
        public void InitializeCache(string cachePath)
        {
            _cachePath = cachePath;

            if (!Directory.Exists(_cachePath))
            {
                System.IO.Directory.CreateDirectory(_cachePath);
            }
        }
        // called once to make sure the package cache directory is intact
        public void InitializePackageCache(string packagePath)
        {
            _packagePath = packagePath;

            if (!Directory.Exists(_packagePath))
            {
                System.IO.Directory.CreateDirectory(_packagePath);
            }
        }

        // XXX: refactor this later maybe
        public void SetRemoteProxy(IPAddress remoteProxyAddress, int remoteProxyPort)
        {
            if (remoteProxyAddress == null)
            {
                _remoteWebProxy = null;
            }
            else
            {
                _remoteWebProxy = new WebProxy(remoteProxyAddress.ToString(), remoteProxyPort);
            }
        }
        public void SetRemoteProxy(IPAddress remoteProxyAddress, int remoteProxyPort, string login, string password)
        {
            if (remoteProxyAddress == null)
            {
                _remoteWebProxy = null;
            }
            else
            {
                _remoteWebProxy = new WebProxy(remoteProxyAddress.ToString(), remoteProxyPort);
                _remoteWebProxy.Credentials = new NetworkCredential(login, password);
            }
        }

        // write a message to the log
        public void WriteMessage(int requestId, string str)
        {
            _log.WriteMessage(requestId, str);
        }
        // debug for the proxies
        public void WriteDebug(string str)
        {
            _log.WriteDebug(0, str);
        }
        // debug accessor for the requests
        public void WriteDebug(int requestId, string str)
        {
            _log.WriteDebug(requestId, str);
        }

        // used to rate limit the download
        public bool HasBandwidth(int bytes)
        {
            lock (bandwidthMeasureObject)
            {
                if (DEFAULT_MAX_DOWNLOAD_SPEED == 0)
                    return true;
                TimeSpan elapsed = DateTime.Now - bandwidthMeasureStartTime;
                if (elapsed.TotalMilliseconds > 1000)
                {
                    // reset
                    bandwidthMeasureStartTime = DateTime.Now;
                    bandwidthMeasureDataSent = bytes;
                    return true;
                }
                else
                {
                    int bps = bandwidthMeasureDataSent / (Convert.ToInt32(elapsed.TotalSeconds) + 1);
                    if (bps > DEFAULT_MAX_DOWNLOAD_SPEED)
                    {
                        return false;
                    }
                    else
                    {
                        bandwidthMeasureDataSent += bytes;
                        return true;
                    }
                }
            }
        }

        public void LoadBlacklist(string blacklistFile)
        {
            // create reader & open file
            try
            {
                string s = System.IO.File.ReadAllText(blacklistFile);

                string[] domains = s.Split('\n');

                foreach (string domain in domains)
                {
                    if (domain.Trim().Length > 0)
                    {
                        _blacklistedDomains.Add(domain.Trim());
                    }
                }
            }
            catch (Exception)
            {
                // no blacklist is ok
            }
        }

        public bool IsBlacklisted(string requestUri)
        {
            // blacklist stuff from the last request
            if (requestUri.Contains("toolbarqueries"))
            {
                return true;
            }

            if (requestUri.StartsWith("http://"))
            {
                requestUri = requestUri.Substring("http://".Length);
            }

            foreach (string domain in _blacklistedDomains)
            {
                if (requestUri.StartsWith(domain))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
