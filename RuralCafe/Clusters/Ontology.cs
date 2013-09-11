using RuralCafe.Lucenenet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

using RuralCafe.Util;

namespace RuralCafe.Clusters
{
    /// <summary>
    /// A prepopulated ontology can be weighted with this class.
    /// </summary>
    public static class Ontology
    {
        /// <summary>
        /// Weights all categories and subcategories by the number of search results,
        /// sort the XML by that and saves search results with content snippets in the subcategories,
        /// that do actually appear in dex index pages.
        /// </summary>
        /// <param name="path">The path to the directory where the XML file is in.</param>
        /// <param name="proxy">The proxy.</param>
        public static void CreateWeightsAndSaveSearchResults(string path, RCLocalProxy proxy)
        {
            proxy.Logger.Info("Ontology: Weighting ontology. This can take several minutes or hours.");
            string xmlFileName = path + IndexServer.CLUSTERS_XML_FILE_NAME;
            XmlDocument xmlDoc = IndexServer.GetClustersXMLDocument(xmlFileName);
            lock (xmlDoc)
            {
                XmlElement rootXml = xmlDoc.DocumentElement;

                if (rootXml == null)
                {
                    proxy.Logger.Warn("Ontology: No proper clusters.xml with ontology. Aborting weighting.");
                    return;
                }

                proxy.Logger.Debug("Ontology: Step 1/3: Getting number of cached items.");
                int limit = proxy.ProxyCacheManager.CachedItems();
                int i = 1;
                foreach (XmlElement categoryElement in rootXml.ChildNodes)
                {
                    proxy.Logger.Debug(String.Format("Ontology: Step 2/3: Calculating weights for category ({0}/{1}): {2}",
                      i, rootXml.ChildNodes.Count, categoryElement.GetAttribute(IndexServer.INDEX_FEATURES_XML_ATTR)));
                    // Determine the weight for the category and all subcategories
                    DetermineWeight(categoryElement, proxy, limit);
                    foreach (XmlElement subcategoryElement in categoryElement.ChildNodes)
                    {
                        DetermineWeight(subcategoryElement, proxy, limit);
                    }
                    i++;
                }
                SortByWeight(rootXml);

                // Getting search results for all subcategories visible on the index page.
                for (int catNo = 0; catNo < Math.Min(IndexServer.NUMBER_OF_CATEGORIES, rootXml.ChildNodes.Count); catNo++)
                {
                    proxy.Logger.Debug(String.Format("Ontology: Step 3/3: Getting number of cached items for category ({0}/{1})",
                        catNo + 1, IndexServer.NUMBER_OF_CATEGORIES));
                    for (int subcatNo = 0; subcatNo < Math.Min(IndexServer.NUMBER_OF_SUBCATEGORIES, rootXml.ChildNodes[catNo].ChildNodes.Count); subcatNo++)
                    {
                        AppendSearchResults(rootXml.ChildNodes[catNo].ChildNodes[subcatNo] as XmlElement, proxy, IndexServer.NUMBER_OF_LINKS);
                    }
                }

                // Set timestamp for the new clusters.xml
                rootXml.SetAttribute("time", "" + DateTime.Now.ToFileTime());

                // Save new xml
                xmlDoc.Save(xmlFileName);
            }

            proxy.Logger.Info("Ontology: Finished successfully.");
        }

        /// <summary>
        /// Sorts the categories and the subcategorie of each category be weight. Must be provided with a "categories" element.
        /// </summary>
        /// <param name="categoriesElement">The "categories" element.</param>
        private static void SortByWeight(XmlElement categoriesElement)
        {
            // Convert to XElement so we can use LINQ2XML
            XElement xCategoriesElement = XElement.Parse(categoriesElement.OuterXml);
            // Sort the categories
            IOrderedEnumerable<XElement> xOrderedChilds = xCategoriesElement.Elements().
                OrderByDescending(SelectWeightFromXElement);
            xCategoriesElement.ReplaceNodes(xOrderedChilds);

            // Sort the subcategories for each category
            foreach (XElement xCategoryElement in xCategoriesElement.Elements())
            {
                IOrderedEnumerable<XElement> xOrderedGrandChilds = xCategoryElement.Elements().
                    OrderByDescending(SelectWeightFromXElement);
                xCategoryElement.ReplaceNodes(xOrderedGrandChilds);
            }
            // Convert back
            XmlReader reader = xCategoriesElement.CreateReader();
            reader.MoveToContent();
            categoriesElement.InnerXml = reader.ReadInnerXml();
        }

        /// <summary>
        /// Selects the weight from an XElement.
        /// </summary>
        /// <param name="e">The XElement</param>
        /// <returns>The weight.</returns>
        private static int SelectWeightFromXElement(XElement e)
        {
            XAttribute attr = e.Attribute(IndexServer.INDEX_WEIGHT_XML_ATTR);
            return attr == null ? 0 : Int32.Parse(attr.Value);
        }

        /// <summary>
        /// Determines the weight for a (sub)category.
        /// </summary>
        /// <param name="element">The XML element</param>
        /// <param name="limit">The upper limit for number of search results, which is used as weight.</param>
        /// <param name="proxy">The proxy.</param>
        private static void DetermineWeight(XmlElement element, RCLocalProxy proxy, int limit)
        {
            string title = element.GetAttribute(IndexServer.INDEX_FEATURES_XML_ATTR);
            int weight = proxy.IndexWrapper.NumberOfResults(title, limit);
            // Set weight
            element.SetAttribute(IndexServer.INDEX_WEIGHT_XML_ATTR, "" + weight);
        }

        /// <summary>
        /// Appends lucene search results to a subcategory element.
        /// </summary>
        /// <param name="subCategoryElement">The element.</param>
        /// <param name="proxy">The proxy.</param>
        /// <param name="numberOfResults">The maximum nunber of results to add.</param>
        private static void AppendSearchResults(XmlElement subCategoryElement, RCLocalProxy proxy, int numberOfResults)
        {
            string title = subCategoryElement.GetAttribute(IndexServer.INDEX_FEATURES_XML_ATTR);
            // Do a Lucene search
            SearchResults luceneResults = proxy.IndexWrapper.Query(
                title, proxy.CachePath, 0, numberOfResults, true);
            // Remove current children
            subCategoryElement.RemoveAllChilds();
            // Add the results to the XML
            LocalInternalRequestHandler.AppendSearchResultsXMLElements(luceneResults, subCategoryElement.OwnerDocument, subCategoryElement);
        }
    }
}
