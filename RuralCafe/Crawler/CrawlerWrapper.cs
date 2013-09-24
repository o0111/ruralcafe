using RuralCafe.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using WindowsFormsApplication1;

namespace RuralCafe.Crawler
{
    /// <summary>
    /// A wrapper for Shariq's crawler with Satia's adaptations.
    /// </summary>
    public class CrawlerWrapper
    {
        // Constants
        private const string CRAWLER_DIR_NAME = "crawler";
        private const int CRAWLER_PAGE_TIMEOUT = RequestHandler.WEB_REQUEST_DEFAULT_TIMEOUT; // Maybe we wanna change this.

        // The local proxy.
        private RCLocalProxy _proxy;
        // An event to wait for places in the thread pool.
        private ManualResetEvent _threadPoolEvent = new ManualResetEvent(false);

        /// <summary>
        /// Instantiate a new CrawlerWrapper.
        /// </summary>
        /// <param name="proxy">The local proxy.</param>
        public CrawlerWrapper(RCLocalProxy proxy)
        {
            this._proxy = proxy;
        }

        /// <summary>
        /// Starts the crawler in a modal dialog. This method should be started in an own thread if it is not to be blocking the
        /// current execution.
        /// </summary>
        public void StartCrawler()
        {
            ThreadPool.SetMaxThreads(RCProxy.MAX_THREADS, RCProxy.MAX_THREADS);
            ACrawlerWin crawler = new ACrawlerWin(Properties.Files.Default.BASE_DIR + Path.DirectorySeparatorChar +
                CRAWLER_DIR_NAME + Path.DirectorySeparatorChar, WaitAndDownloadPage);
            crawler.ShowDialog();
        }

        /// <summary>
        /// Waits until there are threads available in the thread pool, then start a new thread
        /// actually downloading the page and returns it.
        /// </summary>
        /// <param name="uri">The URI to download.</param>
        /// <returns>The already started thread.</returns>
        public Thread WaitAndDownloadPage(string uri)
        {
            int workersAvailable, portersAvailable;
            while (true)
            {
                lock (_threadPoolEvent)
                {
                    ThreadPool.GetAvailableThreads(out workersAvailable, out portersAvailable);
                    if (workersAvailable > 0)
                    {
                        // Run Download Page in a new thread.
                        Thread t = new Thread(() => DownloadPage(uri));
                        t.Start();
                        return t;
                    }
                    else
                    {
                        _threadPoolEvent.Reset();
                    }
                }
                _threadPoolEvent.WaitOne();
            }
        }

        /// <summary>
        /// This method works very similar to RemoteRequestHandler.DownloadPageRecursively.
        /// It downloads a page and all its embedded objects to the local cache, and also
        /// indexes main pages.
        /// </summary>
        /// <param name="uri">The URI to download.</param>
        private void DownloadPage(string uri)
        {
            // create the main RCRequest
            RCRequest rcRequest = new RCRequest(_proxy, (HttpWebRequest)WebRequest.Create(uri));
            rcRequest.GenericWebRequest.Timeout = CRAWLER_PAGE_TIMEOUT;
            // Only download for not already existing items
            if (!_proxy.ProxyCacheManager.IsCached(rcRequest.RelCacheFileName))
            {
                // Download!
                try
                {
                    _proxy.WaitForAdmissionControlAndAddActiveRequest(rcRequest.RequestId);
                    // Index main pages
                    rcRequest.DownloadToCache(true);
                }
                catch (Exception)
                {
                    // Ignore
                    return;
                }
                finally
                {
                    _proxy.RemoveActiveRequest();
                }

                if (!_proxy.ProxyCacheManager.IsHTMLFile(rcRequest.RelCacheFileName))
                {
                    return;
                }
                // Getting embedded objects only makes sense for html pages.
                Uri baseUri = new Uri(rcRequest.Uri);
                string htmlContent = Utils.ReadFileAsString(rcRequest.CacheFileName).ToLower();

                // get the embedded content of the search result page
                DownloadEmbeddedObjects(rcRequest, baseUri, htmlContent);
            }

            // Notify that new threads are available in the pool
            lock (_threadPoolEvent)
            {
                _threadPoolEvent.Set();
            }
        }

        /// <summary>
        /// Downloads embedded objects based on the richness.
        /// </summary>
        /// <param name="rcRequest">Request page to start from.</param>
        /// <param name="baseUri">The Uri of the website where to download embedded objects.</param>
        /// <param name="htmlContent">The HTML content of the webiste.</param>
        /// <returns>List of RCRequests of embedded objects downloaded</returns>
        private LinkedList<RCRequest> DownloadEmbeddedObjects(RCRequest rcRequest, Uri baseUri, string htmlContent)
        {
            LinkedList<Uri> filteredEmbeddedObjects = new LinkedList<Uri>();
            LinkedList<Uri> embeddedObjects = HtmlUtils.ExtractEmbeddedObjects(baseUri, htmlContent);
            return DownloadObjectsInParallel(rcRequest, embeddedObjects);
        }

        /// <summary>
        /// Downloads a set of URIs in parallel using a ThreadPool.
        /// </summary>
        /// <param name="parentRequest">Root request.</param>
        /// <param name="childObjects">Children URIs to be downloaded.</param>
        /// <returns>List of downloaded requests.</returns>
        private LinkedList<RCRequest> DownloadObjectsInParallel(RCRequest parentRequest, LinkedList<Uri> childObjects)
        {
            LinkedList<RCRequest> addedObjects = new LinkedList<RCRequest>();
            parentRequest.ResetEvents = new ManualResetEvent[childObjects.Count];

            try
            {
                // queue up worker threads to download URIs
                for (int i = 0; i < childObjects.Count; i++)
                {
                    // create the RCRequest for the object
                    RCRequest currChildObject = new RCRequest(_proxy, (HttpWebRequest)WebRequest.Create(childObjects.ElementAt(i)));
                    currChildObject.ChildNumber = i;
                    // Set the root request.
                    currChildObject.RootRequest = parentRequest;
                    addedObjects.AddLast(currChildObject);

                    // set the resetEvent
                    currChildObject.ResetEvents = parentRequest.ResetEvents;
                    parentRequest.ResetEvents[i] = new ManualResetEvent(false);

                    // download the page
                    ThreadPool.QueueUserWorkItem(new WaitCallback(DownloadPageWorkerThread), (object)currChildObject);
                }

                // wait for timeout
                Utils.WaitAll(parentRequest.ResetEvents);
            }
            catch (Exception)
            {
            }

            return addedObjects;
        }

        /// <summary>
        /// Worker thread to download a webpage given the RequestWrapper's parameters.
        /// </summary>
        /// <param name="requestObj">The requested URI.</param>
        private void DownloadPageWorkerThread(object requestObj)
        {
            // cast the RCRequest
            RCRequest request = (RCRequest)requestObj;

            // make sure this root request is not timed out
            if (!request.RootRequest.IsTimedOut(CRAWLER_PAGE_TIMEOUT))
            {
                // reduce the timer
                DateTime currTime = DateTime.Now;
                DateTime endTime = request.RootRequest.StartTime.AddMilliseconds(CRAWLER_PAGE_TIMEOUT);
                if (endTime.CompareTo(currTime) > 0)
                {
                    request.GenericWebRequest.Timeout = (int)(endTime.Subtract(currTime)).TotalMilliseconds;

                    // download the page, if it does not exist already
                    if (!_proxy.ProxyCacheManager.IsCached(request.RelCacheFileName))
                    {
                        // Download!
                        try
                        {
                            _proxy.AddActiveRequest();
                            // Do not index embedded objects
                            request.DownloadToCache(false);
                        }
                        catch { } // Ignore
                        finally
                        {
                            _proxy.RemoveActiveRequest();
                        }
                    }
                }
                else
                {
                    request.GenericWebRequest.Timeout = 0;
                }
            }

            // mark this thread as done
            request.SetDone();
        }
    }
}
