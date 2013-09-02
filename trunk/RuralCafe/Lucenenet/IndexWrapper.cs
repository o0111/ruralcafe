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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Lucene.Net.Index;
using Lucene.Net.Documents;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Search;
using Lucene.Net.QueryParsers;
using System.Net;
using Lucene.Net.Highlight;
using RuralCafe.Util;
using System.Collections.Specialized;

namespace RuralCafe.Lucenenet
{
    /// <summary>
    /// Interacts with Lucene.Net to manage the cache index.
    /// Compatible with Lucene.Net version 2.9.1
    /// </summary>
    public class IndexWrapper
    {
        /// <summary>
        /// The maximum number of results.
        /// </summary>
        private const int LUCENE_MAX_RESULTS = 1000;

        private string _indexPath;
        private Analyzer _analyzer = new StandardAnalyzer(Lucene.Net.Util.Version.LUCENE_CURRENT);

        /// <summary>
        /// The path to the index.
        /// </summary>
        public string IndexPath
        {
            get { return _indexPath; }
        }

        /// <summary>
        /// Creates a new index wrapper with the specified path.
        /// </summary>
        /// <param name="indexPath"></param>
        public IndexWrapper(string indexPath)
        {
            this._indexPath = indexPath;
        }

        /// <summary>
        /// Ensure that the index exists.
        /// </summary>
        public void EnsureIndexExists()
        {
            if (!Directory.Exists(_indexPath))
            {
                Directory.CreateDirectory(_indexPath);
            }

            Lucene.Net.Store.FSDirectory directory = Lucene.Net.Store.FSDirectory.Open(new System.IO.DirectoryInfo(_indexPath));
            if (!IndexReader.IndexExists(directory))
            {
                // Create the index with a writer, if there is none
                IndexWriter writer = new IndexWriter(directory, _analyzer, true);
                writer.Close();
            }
            // Remove an old lock file that may not have been deleted on system shutdown
            FileInfo lockFile = new FileInfo(_indexPath + "write.lock");
            if (lockFile.Exists)
            {
                lockFile.Delete();
            }
        }

        /// <summary>
        /// Deletes a document from our cache indexer.
        /// To be used for cache coherence/maintainence.
        /// Should not be called when there is already an indexWriter active.
        /// </summary>
        /// <param name="uri">The uri to delete from the index.</param>
        public void DeleteDocument(string uri)
        {
            IndexWriter writer = new IndexWriter(_indexPath, _analyzer, false);
            writer.DeleteDocuments(new Term("uri", uri));
            writer.Close();
        }

        /// <summary>
        /// Adds a document to the index.
        /// </summary>
        /// <param name="uri">URI of the page.</param>
        /// <param name="title">Title of the page.</param>
        /// <param name="content">The page contents.</param>
        public void IndexDocument(string uri, string title, string content)
        {
            IndexWriter writer = new IndexWriter(_indexPath, _analyzer, false);
            // Delete in order not to have duplicates
            writer.DeleteDocuments(new Term("uri", uri));

            Document doc = new Document();
            doc.Add(new Field("uri", uri, Field.Store.YES, Field.Index.NOT_ANALYZED));
            doc.Add(new Field("title", title, Field.Store.YES, Field.Index.ANALYZED));
            doc.Add(new Field("content", content, Field.Store.NO, Field.Index.ANALYZED));
            writer.AddDocument(doc);

            writer.Close();
        }

        /// <summary>
        /// Removes all dead links from the index.
        /// </summary>
        /// <param name="proxy">The proxy, to log and to gain access to the cache manager.</param>
        public void RemoveAllDeadLinks(RCLocalProxy proxy)
        {
            proxy.Logger.Info(String.Format("The index contains {0} documents.",
                        IndexItemCount()));

            proxy.Logger.Info("Deleting all dead links from index...");
            Lucene.Net.Store.FSDirectory directory = Lucene.Net.Store.FSDirectory.Open(new System.IO.DirectoryInfo(_indexPath));
            IndexReader reader = IndexReader.Open(directory, true);
            IndexWriter writer = new IndexWriter(_indexPath, _analyzer, false);

            int deleted = 0;
            int cacheFailures = 0;

            for (int i = 0; i < reader.MaxDoc(); i++)
            {
                if (reader.IsDeleted(i))
                {
                    continue;
                }
                proxy.Logger.Debug(i + " files scanned out of: " + reader.MaxDoc());
                Document doc = reader.Document(i);

                string uri = doc.Get("uri");
                string relFileName = CacheManager.GetRelativeCacheFileName(uri, "GET");
                string absFileName = proxy.ProxyCacheManager.CachePath + relFileName;

                if (!proxy.ProxyCacheManager.IsCached(relFileName))
                {
                    if (File.Exists(absFileName))
                    {
                        cacheFailures++;
                        proxy.Logger.Warn(String.Format(
                            "Cache Failure {0}. Not cached but file exists: {1}",
                        cacheFailures, relFileName));

                        NameValueCollection headers = new NameValueCollection()
                        {
                            // We need to include content-type, as we always want that header!
                            { "Content-Type", "text/html"}
                        };

                        // The index entry already exists, so we don't want to create a new one here.
                        GlobalCacheItemToAdd newItem = new GlobalCacheItemToAdd(relFileName, headers, 200, false);

                        // Add file to the database
                        proxy.ProxyCacheManager.AddCacheItemsForExistingFiles(new HashSet<GlobalCacheItemToAdd>() { newItem });
                        continue;
                    }

                     deleted++;
                     proxy.Logger.Info(String.Format("This is doc number {0} we delete. Deleting {1} from the lucene index.",
                        deleted, relFileName));
                    writer.DeleteDocuments(new Term("uri", uri));
                    if (deleted % 100 == 0)
                    {
                        writer.Commit();
                    }
                }
            }

            writer.Commit();
            reader.Close();
            writer.Close();
            proxy.Logger.Info(String.Format(
                "Deleted all dead links from index. Deleted {0} index items and added {1} DB items.",
                deleted, cacheFailures));
        }

        /// <summary>
        /// Counts all non-deleted documents in the index.
        /// </summary>
        /// <returns>The number of documents.</returns>
        public int IndexItemCount()
        {
            Lucene.Net.Store.FSDirectory directory = Lucene.Net.Store.FSDirectory.Open(new System.IO.DirectoryInfo(_indexPath));
            IndexReader reader = IndexReader.Open(directory, false);

             int items = 0;

             for (int i = 0; i < reader.MaxDoc(); i++)
             {
                 if (reader.IsDeleted(i))
                 {
                     continue;
                 }
                 items++;
             }
             return items;
        }

        /// <summary>
        /// Queries the index for a list of results.
        /// </summary>
        /// <param name="queryString">String to query the index for.</param>
        /// <param name="cachePath">The path to the local cache.</param>
        /// <param name="offset">The offset for the first result to return.</param>
        /// <param name="resultAmount">The max number of results to return for the current page.</param>
        /// <param name="includeContentSnippets">Whether the results should contain content snippets.</param>
        /// <returns>A list of search results.</returns>
        public SearchResults Query(string queryString, string cachePath,
            int offset, int resultAmount, bool includeContentSnippets)
        {
            SearchResults results = new SearchResults();
            Lucene.Net.Store.FSDirectory directory = Lucene.Net.Store.FSDirectory.Open(new System.IO.DirectoryInfo(_indexPath));
            IndexReader reader = IndexReader.Open(directory, true);
            IndexSearcher searcher = new IndexSearcher(reader);
            QueryParser parser = new QueryParser(Lucene.Net.Util.Version.LUCENE_CURRENT, "content", _analyzer);

            // the search function
            string searchQuery = "(" + QueryParser.Escape(queryString) + ")";
            Query query = parser.Parse(searchQuery);
            // Request all results up to the page we actually need (this is quick)
            TopDocs topDocs = searcher.Search(query, LUCENE_MAX_RESULTS);
            ScoreDoc[] hits = topDocs.scoreDocs;
            // Save num results
            results.NumResults = hits.Length;

            // Only loop through the hits that should be on the page
            for (int i = offset; i < hits.Length && i < offset + resultAmount; i++)
            {
                int docId = hits[i].doc;
                Document doc = searcher.Doc(docId);

                if (includeContentSnippets)
                {
                    // Read the whole file from the cache to find the content snippet.
                    string filepath = CacheManager.GetRelativeCacheFileName(doc.Get("uri"), "GET");
                    string documentContent = Utils.ReadFileAsString(cachePath + filepath);

                    // Remove unusable stuff.
                    documentContent = HtmlUtils.RemoveHead(documentContent);
                    documentContent = HtmlUtils.ExtractText(documentContent);

                    // Find (and highlight) content snippets
                    QueryScorer scorer = new QueryScorer(query);
                    SimpleHTMLFormatter formatter = new SimpleHTMLFormatter("<b>", "</b>");
                    Highlighter highlighter = new Highlighter(formatter, scorer);
                    highlighter.SetTextFragmenter(new SentenceFragmenter());
                    TokenStream stream = _analyzer.TokenStream("content", new StringReader(documentContent));

                    // Get 1 fragment
                    string contentSnippet = "";
                    try
                    {
                        string[] fragments = highlighter.GetBestFragments(stream, documentContent, 1);
                        if (fragments.Length > 0)
                        {
                            contentSnippet = HtmlUtils.StripTagsCharArray(fragments[0], false);
                            // If the content snippet does end in mid of a sentence, let's append "..."
                            if (!new char[] { '.', '!', '?' }.Contains(contentSnippet[contentSnippet.Length - 1]))
                            {
                                contentSnippet += "...";
                            }
                        }
                    }
                    catch (Exception)
                    {
                    }
                    results.AddLuceneDocument(doc, contentSnippet);
                }
                else
                {
                    results.AddLuceneDocument(doc);
                }
            }

            searcher.Close();
            return results;
        }
    }
}
