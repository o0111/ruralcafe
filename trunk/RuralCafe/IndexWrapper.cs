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

namespace RuralCafe
{
    /// <summary>
    /// Interacts with Lucene.Net to manage the cache index.
    /// Compatible with Lucene.Net version 2.9.1
    /// </summary>
    class IndexWrapper
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
        /// <returns>A List of Documents.</returns>
        public static List<Document> Query(string indexPath, string queryString)
        {
            List<Document> results = new List<Document>();

            Lucene.Net.Store.FSDirectory directory = Lucene.Net.Store.FSDirectory.Open(new System.IO.DirectoryInfo(indexPath));
            IndexSearcher searcher = new IndexSearcher(directory, true);
            QueryParser parser = new QueryParser(Lucene.Net.Util.Version.LUCENE_CURRENT, "content", new StandardAnalyzer(Lucene.Net.Util.Version.LUCENE_CURRENT));
            
            // the search function
            string searchQuery = "(" + QueryParser.Escape(queryString) + ")";
            Query query = parser.Parse(searchQuery);
            TopDocs topDocs = searcher.Search(query, 1000);
            ScoreDoc[] hits = topDocs.scoreDocs;

            for (int i = 0; i < hits.Length; i++)
            {
                int docId = hits[i].doc;
                Document doc = searcher.Doc(docId);

                results.Add(doc);
            }
            
            searcher.Close();

            return results;
        }
    }
}
