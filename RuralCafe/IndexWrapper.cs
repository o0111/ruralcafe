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

namespace RuralCafe
{
    /// <summary>
    /// Simple data class that combines a lucene index document with a content snippet for the current query.
    /// </summary>
    public class DocumentWithSnippet
    {
        public Document document;
        public String contentSnippet;

        public DocumentWithSnippet(Document document)
        {
            this.document = document;
            this.contentSnippet = "";
        }
    }

    /// <summary>
    /// Interacts with Lucene.Net to manage the cache index.
    /// Compatible with Lucene.Net version 2.9.1
    /// </summary>
    public class IndexWrapper
    {
        public static bool EnsureIndexExists(string indexPath)
        {
            if (!Directory.Exists(indexPath))
            {
                System.IO.Directory.CreateDirectory(indexPath);
            }

            Lucene.Net.Store.FSDirectory directory = Lucene.Net.Store.FSDirectory.Open(new System.IO.DirectoryInfo(indexPath));
            if (!IndexReader.IndexExists(directory))
            {
                IndexWriter writer = new IndexWriter(directory, new StandardAnalyzer(Lucene.Net.Util.Version.LUCENE_CURRENT), true);
                writer.Close();
            }
            return true;
        }

        /// <summary>
        /// Deletes a document from our cacheindexer.
        /// To be used for cache coherence/maintainence.
        /// Unused. Untested.
        /// </summary>
        public static void DeleteDocument(string indexPath, string Uri)
        {
            Lucene.Net.Store.FSDirectory directory = Lucene.Net.Store.FSDirectory.Open(new System.IO.DirectoryInfo(indexPath));
            IndexReader indexReader = IndexReader.Open(directory, true);
            indexReader.DeleteDocuments(new Term("uri", Uri));
        }

        /// <summary>
        /// Adds a document to the index.
        /// </summary>
        /// <param name="indexPath">Path to the Lucene index.</param>
        /// <param name="headers">HTTP response headers.</param>
        /// <param name="uri">URI of the page.</param>
        /// <param name="title">Title of the page.</param>
        /// <param name="content">The page contents.</param>
        public static void IndexDocument(string indexPath, string headers, string uri, string title, string content)
        {
            // remove the document if it exists in the index to prevent duplicates
            //DeleteDocument(indexPath, Uri);

            IndexWriter writer = new IndexWriter(indexPath, new StandardAnalyzer(Lucene.Net.Util.Version.LUCENE_CURRENT), false);

            Document doc = new Document();
            doc.Add(new Field("uri", uri, Field.Store.YES, Field.Index.NOT_ANALYZED));
            doc.Add(new Field("headers", headers, Field.Store.YES, Field.Index.NOT_ANALYZED));
            doc.Add(new Field("title", title, Field.Store.YES, Field.Index.ANALYZED));
            doc.Add(new Field("content", content, Field.Store.NO, Field.Index.ANALYZED));
            writer.AddDocument(doc);

            writer.Close();
        }

        /// <summary>
        /// Queries the index for a list of results.
        /// </summary>
        /// <param name="indexPath">Path to the Lucene index.</param>
        /// <param name="queryString">String to query the index for.</param>
        /// <param name="cachePath">The path to the local cache.</param>
        /// <param name="page">Which page is being requested.</param>
        /// <param name="pageSize">How many items are on one page and should be returned.</param>
        /// <returns>A List of Documents.</returns>
        public static List<DocumentWithSnippet> Query(string indexPath, string queryString, string cachePath,
            int page, int pageSize)
        {
            List<DocumentWithSnippet> results = new List<DocumentWithSnippet>();

            Lucene.Net.Store.FSDirectory directory = Lucene.Net.Store.FSDirectory.Open(new System.IO.DirectoryInfo(indexPath));
            IndexReader reader = IndexReader.Open(directory, true);
            IndexSearcher searcher = new IndexSearcher(reader);
            Analyzer analyzer =  new StandardAnalyzer(Lucene.Net.Util.Version.LUCENE_CURRENT);
            QueryParser parser = new QueryParser(Lucene.Net.Util.Version.LUCENE_CURRENT, "content", analyzer);
            
            // the search function
            string searchQuery = "(" + QueryParser.Escape(queryString) + ")";
            Query query = parser.Parse(searchQuery);
            // Request all results up to the page we actually need (this is quick)
            TopDocs topDocs = searcher.Search(query, page * pageSize);
            ScoreDoc[] hits = topDocs.scoreDocs;

            // Only loop through the hits that should be on the page
            for (int i = (page - 1) * pageSize; i < hits.Length; i++)
            {
                int docId = hits[i].doc;
                Document doc = searcher.Doc(docId);
                DocumentWithSnippet docWSnip = new DocumentWithSnippet(doc);

                // Read the whole file from the cache to find the content snippet.
                string filepath = RCRequest.GetRelativeCacheFileName(doc.Get("uri"));
                string documentContent = Util.ReadFileAsString(cachePath + filepath);
                // Find (and highlight) content snippets
                QueryScorer scorer = new QueryScorer(query);
                SimpleHTMLFormatter formatter = new SimpleHTMLFormatter("<b>", "</b>");
                Highlighter highlighter = new Highlighter(formatter, scorer);
                highlighter.SetTextFragmenter(new SimpleFragmenter(200));
                TokenStream stream = analyzer.TokenStream("content", new StringReader(documentContent));

                // TODO somehow find GOOD fragments
                // Remove unusable stuff.
                // documentContent = Util.RemoveTagsUnusableForSnippets(documentContent);
                
                // Get 1 fragment
                try
                {
                    string[] fragments = highlighter.GetBestFragments(stream, documentContent, 1);
                    if (fragments.Length > 0)
                    {
                        docWSnip.contentSnippet = Util.StripTagsCharArray(fragments[0], false);
                    }
                }
                catch (Exception)
                {
                    // Do nothing
                }
                

                results.Add(docWSnip);
            }
            
            searcher.Close();
            return results;
        }
    }
}
