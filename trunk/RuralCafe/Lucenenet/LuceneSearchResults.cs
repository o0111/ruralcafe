using Lucene.Net.Documents;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RuralCafe.Lucenenet
{
    /// <summary>
    /// Simple data class that combines lucene index document with content snippets for the current query, and the number
    /// of total results.
    /// </summary>
    public class LuceneSearchResults
    {
        private List<Document> _documents;
        /// <summary>
        /// The Documents
        /// </summary>
        public List<Document> Documents
        {
            get { return _documents; }
        }
        private List<string> _contentSnippets;
        /// <summary>
        /// The content snippets
        /// </summary>
        public List<string> ContentSnippets
        {
            get { return _contentSnippets; }
        }
        private int _NumResults;
        /// <summary>
        /// The number of results.
        /// </summary>
        public int NumResults
        { get; set; }

        /// <summary>
        /// Construct an at first empty result list.
        /// </summary>
        public LuceneSearchResults()
        {
            this._documents = new List<Document>();
            this._contentSnippets = new List<string>();
        }

        /// <summary>
        /// Add a document and its content snippet to the list.
        /// </summary>
        /// <param name="document"></param>
        /// <param name="contentSnippet"></param>
        public void AddDocument(Document document, string contentSnippet)
        {
            this._documents.Add(document);
            this._contentSnippets.Add(contentSnippet);
        }

        /// <summary>
        /// Removes the Document at the specified index. No index check is being made.
        /// NumResults is being decremented.
        /// </summary>
        /// <param name="index">The index to remove.</param>
        public void RemoveDocument(int index)
        {
            this._documents.RemoveAt(index);
            this._contentSnippets.RemoveAt(index);
            this.NumResults--;
        }
    }
}
