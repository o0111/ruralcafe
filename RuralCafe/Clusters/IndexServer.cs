using RuralCafe.Lucenenet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace RuralCafe.Clusters
{
    /// <summary>
    /// Provides methods to serve the three levels of our index (clusters/ontology).
    /// </summary>
    public static class IndexServer
    {
        // Constants
        public const string INDEX_CATEGORIES_XML_NAME = "categories";
        public const string INDEX_CATEGORY_XML_NAME = "category";
        public const string INDEX_SUBCATEGORY_XML_NAME = "subcategory";
        public const string INDEX_ID_XML_ATTR = "id";
        public const string INDEX_FEATURES_XML_ATTR = "title";
        public const string INDEX_SIZE_XML_ATTR = "size";
        public const string INDEX_WEIGHT_XML_ATTR = "weight";
        public const string INDEX_FEATURES_JOIN_STRING = ", ";
        public const string INDEX_ONLY_LEAF_CHILDS_TITLE = "Other";
        public const string INDEX_ONLY_LEAF_CHILDS_ID = "-1";
        public const string INDEX_LEVEL_XML_ATTR = "level";
        public const string ITEM_XML_NAME = "item";
        public const string ITEM_URL_XML_NAME = "url";
        public const string ITEM_TITLE_XML_NAME = "title";
        public const string ITEM_SNIPPET_XML_NAME = "snippet";
        public const string INDEX_TIME_ATTRIBUTE_XML_NAME = "time";
        public const string INDEX_TIME_NO_OVERRIDE_VALUE = "DO_NOT_OVERRIDE";

        // These define how many items appear on the index pages.
        public const int NUMBER_OF_CATEGORIES = 10; // level 1
        public const int NUMBER_OF_SUBCATEGORIES_PER_CATEGORY = 5; // level 1
        public const int NUMBER_OF_SUBCATEGORIES = 10; // level 2
        public const int NUMBER_OF_LINKS_PER_SUBCATEGORY = 3; // level 2
        public const int NUMBER_OF_LINKS = 10; // level 3

        /// <summary>The file name of the 3-level-hierarchy tree clusters xml file.</summary>
        public const string CLUSTERS_XML_FILE_NAME = "clusters.xml";

        /// <summary>
        /// The clusters.xml
        /// Whenever it is to be modifies, it should be locked using this object first.
        /// </summary>
        private static XmlDocument clustersXMLDoc = new XmlDocument();

        /// <summary>
        /// Gets the timestamp of the current clusters.xml, if existent.
        /// </summary>
        /// <param name="path">The path to the xml file.</param>
        /// <returns>The timestamp.</returns>
        public static DateTime GetClusteringTimeStamp(string path)
        {
            string xmlFileName = path + CLUSTERS_XML_FILE_NAME;
            if (File.Exists(xmlFileName))
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(new XmlTextReader(xmlFileName));

                string dateString = doc.DocumentElement.GetAttribute(INDEX_TIME_ATTRIBUTE_XML_NAME);
                if (!String.IsNullOrEmpty(dateString))
                {
                    if (dateString.Equals(INDEX_TIME_NO_OVERRIDE_VALUE))
                    {
                        // Return the latest possible.
                        return DateTime.MaxValue;
                    }

                    long dateFileTime;
                    if (Int64.TryParse(dateString, out dateFileTime))
                    {
                        // return the timestamp
                        return DateTime.FromFileTime(dateFileTime);
                    }
                }
            }

            // Return the earliest possible.
            return DateTime.MinValue;
        }

        /// <summary>
        /// Gets the clusters xml document. Loads the content, if they are not already loaded.
        /// </summary>
        /// <param name="clusterXMLFile">The fileName</param>
        /// <returns>The XML document.</returns>
        public static XmlDocument GetClustersXMLDocument(string clusterXMLFile)
        {
            lock (clustersXMLDoc)
            {
                if (clustersXMLDoc.DocumentElement == null && File.Exists(clusterXMLFile))
                {
                    clustersXMLDoc.Load(new XmlTextReader(clusterXMLFile));
                }

                return clustersXMLDoc;
            }
        }

        /// <summary>
        /// Computes the 1st level in the hierarchy.
        /// </summary>
        /// <param name="clusterXMLFile">The path to clusters.xml</param>
        /// <returns>The index.xml string.</returns>
        public static string Level1Index(string clusterXMLFile)
        {
            XmlDocument clustersDoc = GetClustersXMLDocument(clusterXMLFile);

            XmlDocument indexDoc = new XmlDocument();
            indexDoc.AppendChild(indexDoc.CreateXmlDeclaration("1.0", "UTF-8", String.Empty));

            XmlElement indexRootXml = indexDoc.CreateElement(INDEX_CATEGORIES_XML_NAME);
            indexDoc.AppendChild(indexRootXml);
            indexRootXml.SetAttribute(INDEX_LEVEL_XML_ATTR, String.Empty + 1);

            // Check for root node
            if (clustersDoc.DocumentElement.ChildNodes.Count == 0)
            {
                throw new ArgumentException("No categories");
            }
            XmlElement rootNode = (XmlElement)clustersDoc.DocumentElement;

            // Import up to maxCategories categories
            for (int i = 0; i < rootNode.ChildNodes.Count && (NUMBER_OF_CATEGORIES == 0 || i < NUMBER_OF_CATEGORIES); i++)
            {
                XmlNode category = indexRootXml.AppendChild(indexDoc.ImportNode(rootNode.ChildNodes[i], false));
                // For each category import up to maxSubCategories subCategories
                for (int j = 0; j < rootNode.ChildNodes[i].ChildNodes.Count &&
                    (NUMBER_OF_SUBCATEGORIES_PER_CATEGORY == 0 || j < NUMBER_OF_SUBCATEGORIES_PER_CATEGORY); j++)
                {
                    category.AppendChild(indexDoc.ImportNode(rootNode.ChildNodes[i].ChildNodes[j], false));
                }
            }

            return indexDoc.InnerXml;
        }

        /// <summary>
        /// Computes the 2nd level in the hierarchy for a given category.
        /// </summary>
        /// <param name="clusterXMLFile">The path to clusters.xml</param>
        /// <param name="categoryId">The category id.</param>
        /// <param name="proxy">Proxy access to conduct a Lucene search.</param>
        /// <returns>The index.xml string.</returns>
        public static string Level2Index(string clusterXMLFile, string categoryId, RCLocalProxy proxy)
        {
            XmlDocument clustersDoc = GetClustersXMLDocument(clusterXMLFile);

            XmlDocument indexDoc = new XmlDocument();
            indexDoc.AppendChild(indexDoc.CreateXmlDeclaration("1.0", "UTF-8", String.Empty));

            XmlElement indexRootXml = indexDoc.CreateElement(INDEX_CATEGORIES_XML_NAME);
            indexDoc.AppendChild(indexRootXml);
            indexRootXml.SetAttribute(INDEX_LEVEL_XML_ATTR, String.Empty + 2);

            XmlElement categoryElement = FindCategory(clustersDoc.DocumentElement, categoryId);
            if (categoryElement == null)
            {
                throw new ArgumentException("Could not find category with that id.");
            }

            // Import category
            XmlNode category = indexRootXml.AppendChild(indexDoc.ImportNode(categoryElement, false));
            // For the category import up to maxSubCategories subCategories
            for (int i = 0; i < categoryElement.ChildNodes.Count && (NUMBER_OF_SUBCATEGORIES == 0 || i < NUMBER_OF_SUBCATEGORIES); i++)
            {
                XmlNode subCategory = category.AppendChild(indexDoc.ImportNode(categoryElement.ChildNodes[i], false));

                if (categoryElement.ChildNodes[i].ChildNodes.Count == 0)
                {
                    // Do a Lucene search, if there are no items. No content snippets on level 2
                    SearchResults luceneResults = proxy.IndexWrapper.Query(
                        (categoryElement.ChildNodes[i] as XmlElement).GetAttribute(INDEX_FEATURES_XML_ATTR),
                        proxy.CachePath, 0, NUMBER_OF_LINKS_PER_SUBCATEGORY, false, -1);

                    // Add the results to the XML
                    LocalInternalRequestHandler.AppendSearchResultsXMLElements(luceneResults, indexDoc, subCategory as XmlElement);
                }
                else
                {
                    // For each subCategory import up to maxItems items
                    for (int j = 0; j < categoryElement.ChildNodes[i].ChildNodes.Count &&
                        (NUMBER_OF_LINKS_PER_SUBCATEGORY == 0 || j < NUMBER_OF_LINKS_PER_SUBCATEGORY); j++)
                    {
                        subCategory.AppendChild(indexDoc.ImportNode(categoryElement.ChildNodes[i].ChildNodes[j], true));
                    }
                }
            }

            return indexDoc.InnerXml;
        }

        /// <summary>
        /// Computes the 3rd level in the hierarchy for a given category and subcategory.
        /// </summary>
        /// <param name="clusterXMLFile">The path to clusters.xml</param>
        /// <param name="categoryId">The category id.</param>
        /// <param name="subCategoryId">The subcategory id.</param>
        /// <param name="proxy">Proxy access to conduct a Lucene search.</param>
        /// <returns>The index.xml string.</returns>
        public static string Level3Index(string clusterXMLFile, string categoryId, string subCategoryId, RCLocalProxy proxy)
        {
            XmlDocument clustersDoc = GetClustersXMLDocument(clusterXMLFile);

            XmlDocument indexDoc = new XmlDocument();
            indexDoc.AppendChild(indexDoc.CreateXmlDeclaration("1.0", "UTF-8", String.Empty));

            XmlElement indexRootXml = indexDoc.CreateElement(INDEX_CATEGORIES_XML_NAME);
            indexDoc.AppendChild(indexRootXml);
            indexRootXml.SetAttribute(INDEX_LEVEL_XML_ATTR, String.Empty + 3);

            // Find category and subcategory element
            XmlElement categoryElement, subCategoryElement;
            categoryElement = FindCategory(clustersDoc.DocumentElement, categoryId);
            if (categoryElement == null)
            {
                throw new ArgumentException("Could not find category with that id.");
            }
            subCategoryElement = FindCategory(categoryElement, subCategoryId);
            if (subCategoryElement == null)
            {
                throw new ArgumentException("Could not find subcategory with that id.");
            }

            // Import category
            XmlNode category = indexRootXml.AppendChild(indexDoc.ImportNode(categoryElement, false));
            // Import subcategory
            XmlNode subCategory = category.AppendChild(indexDoc.ImportNode(subCategoryElement, false));
            if (subCategoryElement.ChildNodes.Count == 0)
            {
                // Do a Lucene search, if there are no items.
                SearchResults luceneResults = proxy.IndexWrapper.Query(
                    subCategoryElement.GetAttribute(INDEX_FEATURES_XML_ATTR),
                    proxy.CachePath, 0, NUMBER_OF_LINKS, true, -1);

                // Add the results to the XML
                LocalInternalRequestHandler.AppendSearchResultsXMLElements(luceneResults, indexDoc, subCategory as XmlElement);
            }
            else
            {
                // Import up to maxItems items
                for (int i = 0; i < subCategoryElement.ChildNodes.Count && (NUMBER_OF_LINKS == 0 || i < NUMBER_OF_LINKS); i++)
                {
                    subCategory.AppendChild(indexDoc.ImportNode(subCategoryElement.ChildNodes[i], true));
                }
            }

            return indexDoc.InnerXml;
        }

        /// <summary>
        /// Finds the (sub)category with the given id in all children of the supplied parent.
        /// </summary>
        /// <param name="parent">The element to search.</param>
        /// <param name="id">The id to find.</param>
        /// <returns>The category with the given id or null.</returns>
        private static XmlElement FindCategory(XmlElement parent, string id)
        {
            foreach (XmlElement child in parent.ChildNodes)
            {
                if (id.Equals(child.GetAttribute(INDEX_ID_XML_ATTR)))
                {
                    return child;
                }
            }
            return null;
        }
    }
}
