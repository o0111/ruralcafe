using BzReader;
using Lucene.Net.Documents;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RuralCafe.Lucenenet
{
    /// <summary>
    /// A search result.
    /// </summary>
    public class SearchResult
    {
        private string _title;
        private string _uri;
        private string _contentSnippet;

        /// <summary>
        /// The title.
        /// </summary>
        public string Title
        {
            get { return _title; }
        }
        /// <summary>
        /// The URI.
        /// </summary>
        public string URI
        {
            get { return _uri; }
        }
        /// <summary>
        /// The content snippet.
        /// </summary>
        public string ContentSnippet
        {
            get { return _contentSnippet; }
        }

        /// <summary>
        /// A new search result with empty content snippet.
        /// </summary>
        /// <param name="title">The title.</param>
        /// <param name="uri">The URI.</param>
        public SearchResult(string title, string uri) :this(title, uri, "") { }
        /// <summary>
        /// A new search result..
        /// </summary>
        /// <param name="title">The title.</param>
        /// <param name="uri">The URI.</param>
        /// <param name="contentSnippet">The content snippet.</param>
        public SearchResult(string title, string uri, string contentSnippet)
        {
            this._title = title;
            this._uri = uri;
            this._contentSnippet = contentSnippet;
        }
    }

    /// <summary>
    /// Simple data class that combines a list of search results, and the number
    /// of total results. Can be used for both Lucene and BzReader results.
    /// </summary>
    public class SearchResults : IEnumerable<SearchResult>
    {
        private List<SearchResult> _results;
        /// <summary>
        /// The Documents
        /// </summary>
        public List<SearchResult> Results
        {
            get { return _results; }
        }
        /// <summary>
        /// The number of results.
        /// </summary>
        public int NumResults;

        /// <summary>
        /// Construct an at first empty result list.
        /// </summary>
        public SearchResults()
        {
            this._results = new List<SearchResult>();
        }

        /// <summary>
        /// Add a lucene document without content snippet to the list.
        /// </summary>
        /// <param name="document">The document</param>
        public void AddLuceneDocument(Document document)
        {
            SearchResult result = new SearchResult(document.Get("title"), document.Get("uri"));
            this._results.Add(result);
        }

        /// <summary>
        /// Add a lucene document and its content snippet to the list.
        /// </summary>
        /// <param name="document">The document</param>
        /// <param name="contentSnippet">The content snippet</param>
        public void AddLuceneDocument(Document document, string contentSnippet)
        {
            SearchResult result = new SearchResult(document.Get("title"), document.Get("uri"), contentSnippet);
            this._results.Add(result);
        }

        /// <summary>
        /// Add a BzReader document to the list.
        /// </summary>
        /// <param name="pageInfo">The page info.</param>
        /// <param name="urlPrefix">The url prefix.</param>
        public void AddBzReaderDocument(PageInfo pageInfo, string urlPrefix)
        {
            SearchResult result = new SearchResult(pageInfo.Name, urlPrefix + pageInfo.Name);
            this._results.Add(result);
        }

        /// <summary>
        /// Adds a whole BzReader hitcollection to the list.
        /// </summary>
        /// <param name="hitCollection">The hit collection.</param>
        /// <param name="urlPrefix">The url prefix.</param>
        public void AddHitCollection(HitCollection hitCollection, string urlPrefix)
        {
            foreach (PageInfo pi in hitCollection)
            {
                AddBzReaderDocument(pi, urlPrefix);
            }
        }

        /// <summary>
        /// Removes the Document at the specified index. No index check is being made.
        /// NumResults is being decremented.
        /// </summary>
        /// <param name="index">The index to remove.</param>
        public void RemoveDocument(int index)
        {
            this._results.RemoveAt(index);
            this.NumResults--;
        }

        // Methods for the IEnumerable interface
        /// <summary></summary>
        /// <returns>The enumerator.</returns>
        public IEnumerator<SearchResult> GetEnumerator()
        {
            return Results.GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
