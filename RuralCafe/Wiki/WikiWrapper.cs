using BzReader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;

namespace RuralCafe.Wiki
{
    /// <summary>
    /// A class that wraps access to wiki dumps.
    /// </summary>
    public class WikiWrapper
    {
        // Constants
        private const string EN_WIKI_URI = "http://en.wikipedia.org/wiki/";

        /// <summary>
        /// The path to the wiki dump file.
        /// </summary>
        private string _dumpFile;

        private Dictionary<string, Indexer> _wikiIndices = new Dictionary<string, Indexer>();
        /// <summary>
        ///  wiki indices (currently only wikipedia)
        /// </summary>
        public Dictionary<string, Indexer> WikiIndices
        {
            get { return _wikiIndices; }
        }

        /// <summary>
        /// Creates a new wiki wrapper.
        /// </summary>
        /// <param name="dumpFile">The path to the wiki dump file.</param>
        public WikiWrapper(string dumpFile)
        {
            this._dumpFile = dumpFile;
        }

        /// <summary>
        /// Initialize the wiki index.
        /// </summary>
        /// <returns>True or false for success or not.</returns>
        public bool InitializeWikiIndex()
        {
            // check if the file exists
            if (!File.Exists(_dumpFile))
            {
                return false;
            }

            // check if the index exists
            Indexer ixr = new Indexer(_dumpFile);
            if (!ixr.IndexExists)
            {
                return false;
            }

            // load the index
            _wikiIndices.Add(_dumpFile.ToLowerInvariant(), ixr);

            return true;
        }

        /// <summary>
        /// Checks to see if this proxy even has any Wiki indices
        /// </summary>
        public bool HasWikiIndices()
        {
            return (_wikiIndices.Count > 0);
        }

        /// <summary>
        /// Checks if the request is in the wikipedia cache.
        /// </summary>
        /// <param name="requestUri">The request URI.</param>
        /// <returns>True or false if the request is in the wiki cache or not.</returns>
        public bool IsInWikiCache(string requestUri)
        {
            if (!HasWikiIndices())
            {
                return false;
            }
            if (requestUri.StartsWith(EN_WIKI_URI))
            {
                // images aren't currently cached, just return no
                if (requestUri.StartsWith(EN_WIKI_URI + "File:"))
                {
                    return false;
                }

                if (_dumpFile.Equals(""))
                {
                    return false;
                }

                // XXX: need to check whether the request is actually in the cache
                return true;
            }
            return false;
        }

        /// <summary>
        /// Serves a Wikipedia page using the Wiki renderer, if available.
        /// </summary>
        /// <param name="requestUri">The request uri.</param>
        /// <param name="redirectTarget">If there is a redirect, the target will be put in here.
        /// Otherwise this will be the empty string.</param>
        /// <returns>The page content or null, if the page is not in the wiki.</returns>
        public string GetWikiContentIfAvailable(string requestUri, out string redirectTarget)
        {
            redirectTarget = "";
            // Check, if there could be anything
            if (!HasWikiIndices())
            {
                return null;
            }
            // This only gets content for english wikipedia at the moment
            if (!requestUri.StartsWith(EN_WIKI_URI))
            {
                return null;
            }
            // images aren't currently cached, just return null
            if (requestUri.StartsWith(EN_WIKI_URI + "File:"))
            {
                return null;
            }
            // No dump file -> No pages
            if (_dumpFile.Equals(""))
            {
                return null;
            }

            // Try to get the page, if available
            try
            {
                Uri uri = new Uri(requestUri);
                UrlRequestedEventArgs urea = new UrlRequestedEventArgs(
                    HttpUtility.UrlDecode(uri.AbsolutePath.Substring(6)));

                ServeWikiURLRenderPage(urea);
                if (urea.Redirect)
                {
                    redirectTarget = urea.RedirectTarget;
                }
                return urea.Response;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Gets called to render a URI.
        /// </summary>
        /// <param name="e">Request parameters.</param>
        private void ServeWikiURLRenderPage(UrlRequestedEventArgs e)
        {
            PageInfo page = null;
            HitCollection hits = Indexer.Search(e.Url, _wikiIndices.Values, 1);
            foreach (PageInfo pi in hits)
            {
                if (pi.Name.Equals(e.Url.Replace('_',' ')))
                {
                    page = pi;
                    break;
                }
            }            

            if (page != null)
            {
                e.Response = page.GetFormattedContent();
                e.RedirectTarget = page.RedirectToTopic;
                e.Redirect = !String.IsNullOrEmpty(e.RedirectTarget);
            }
        }
    }
}
