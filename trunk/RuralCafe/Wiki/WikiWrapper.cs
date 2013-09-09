using BzReader;
using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Highlight;
using RuralCafe.Lucenenet;
using RuralCafe.Util;
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
        // Max number of results to handle that we can't search case sensitive
        private const int FIND_PAGE_MAX_RESULTS = 5;
        /// <summary>
        /// The maximum number of results.
        /// </summary>
        private const int WIKI_MAX_RESULTS = 100;

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
                    redirectTarget = EN_WIKI_URI + urea.RedirectTarget;
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
            // We cannot make BzReader search case sensitive, so we will look
            // an the first N results
            HitCollection hits = Indexer.Search(e.Url, _wikiIndices.Values, FIND_PAGE_MAX_RESULTS);
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

        /// <summary>
        /// Queries the wiki index for a list of results.
        /// </summary>
        /// <param name="queryString">String to query the index for.</param>
        /// <param name="offset">The offset for the first result to return.</param>
        /// <param name="resultAmount">The max munber of results to return for the current page.</param>
        /// <returns>A list of search results.</returns>
        public SearchResults Query(string queryString, int offset, int resultAmount)
        {
            SearchResults results = new SearchResults();
            HitCollection wikiResults = Indexer.Search(queryString.ToLower(),
                WikiIndices.Values, WIKI_MAX_RESULTS);
            // Save results num
            results.NumResults = wikiResults.Count;
            // Add documents with respect to offset and amount
            for (int i = offset; i < wikiResults.Count && i < offset + resultAmount; i++)
            {
                results.AddBzReaderDocument(wikiResults[i], EN_WIKI_URI);
            }
            
            return results;
        }
    }
}
