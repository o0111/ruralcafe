using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;

namespace RuralCafe.Clusters
{
    /// <summary>
    /// A prepopulated ontology can be weighted with this class.
    /// </summary>
    public static class Ontology
    {
        /// <summary>
        /// TODO
        /// </summary>
        /// <param name="path"></param>
        /// <param name="proxy"></param>
        public static void CreateWeights(string path, RCLocalProxy proxy)
        {
            proxy.Logger.Info("Ontology: Weighting ontology.");
            string xmlFileName = path + IndexServer.CLUSTERS_XML_FILE_NAME;
            XmlDocument xmlDoc = IndexServer.GetClustersXMLDocument(xmlFileName);
            lock (xmlDoc)
            {
                XmlElement rootXml = xmlDoc.DocumentElement;

                if (rootXml == null)
                {
                    proxy.Logger.Warn("Ontology: No proper clusters.xml with ontology. Aborting weighting.");
                }

                int i = 1;
                foreach (XmlElement categoryElement in rootXml.ChildNodes)
                {
                    proxy.Logger.Debug(String.Format("Ontology: Calculating weights for category ({0}/{1}): {2}",
                      i, rootXml.ChildNodes.Count, categoryElement.GetAttribute(IndexServer.INDEX_FEATURES_XML_ATTR)));
                    // Determine the weight for the category and all subcategories
                    DetermineWeight(categoryElement, proxy);
                    foreach (XmlElement subcategoryElement in categoryElement.ChildNodes)
                    {
                        DetermineWeight(subcategoryElement, proxy);
                    }
                    i++;
                }

                // Set timestamp for the new clusters.xml
                rootXml.SetAttribute("time", "" + DateTime.Now.ToFileTime());

                // Save new xml
                xmlDoc.Save(xmlFileName);
            }

            proxy.Logger.Info("Ontology: Finished successfully.");
        }

        /// <summary>
        /// Determines the weight for a (sub)category.
        /// </summary>
        /// <param name="element">The XML element</param>
        /// <param name="proxy">The proxy.</param>
        private static void DetermineWeight(XmlElement element, RCLocalProxy proxy)
        {
            string title = element.GetAttribute(IndexServer.INDEX_FEATURES_XML_ATTR);
            int weight = proxy.IndexWrapper.NumberOfResults(title);
            // Set weight
            element.SetAttribute(IndexServer.INDEX_WEIGHT_XML_ATTR, "" + weight);
        }
    }
}
