using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RuralCafe.LinkSuggestion
{
    /// <summary>
    /// Injects tooltips for link suggestions in supplied HTML pages.
    /// </summary>
    public static class LinkSuggestionHtmlModifier
    {
        /// <summary>
        /// Injects tooltips for every link in the HTML.
        /// </summary>
        /// <param name="html">The original page.</param>
        /// <returns>The modified page.</returns>
        public static string IncludeTooltips(string html)
        {
            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(html);

            HtmlNode head = doc.DocumentNode.SelectSingleNode("/html/head");
            HtmlNode body = doc.DocumentNode.SelectSingleNode("/html/body");
            if (head == null || body == null)
            {
                // We haven't sane HTML, just return it as it is.
                return html;
            }
            // include style document
            HtmlNode opentipStyle = doc.CreateElement("link");
            head.AppendChild(opentipStyle);
            opentipStyle.SetAttributeValue("type", "text/css");
            opentipStyle.SetAttributeValue("rel", "stylesheet");
            opentipStyle.SetAttributeValue("href", "http://www.ruralcafe.net/css/opentip.css");
            // include opentip js document
            HtmlNode opentipJs = doc.CreateElement("script");
            head.AppendChild(opentipJs);
            opentipJs.SetAttributeValue("type", "text/javascript");
            opentipJs.SetAttributeValue("src", "http://www.ruralcafe.net/js/opentip-native.min.js");
            // include our js document
            HtmlNode ourJs = doc.CreateElement("script");
            head.AppendChild(ourJs);
            ourJs.SetAttributeValue("type", "text/javascript");
            ourJs.SetAttributeValue("src", "http://www.ruralcafe.net/js/linkSuggestion.js");
            // include our ajax js document (Opentip ajax does not like us...)
            HtmlNode ourAjaxJs = doc.CreateElement("script");
            head.AppendChild(ourAjaxJs);
            ourAjaxJs.SetAttributeValue("type", "text/javascript");
            ourAjaxJs.SetAttributeValue("src", "http://www.ruralcafe.net/js/ajax.js");

            // Include the invisible trigger element
            HtmlNode trigger = doc.CreateElement("div");
            trigger.SetAttributeValue("id", "rclink-trigger");
            body.AppendChild(trigger);

            HtmlNodeCollection links = doc.DocumentNode.SelectNodes("//a");
            if (links != null)
            {
                // Modify all links
                int i = 0;
                foreach (HtmlNode link in links)
                {
                    link.SetAttributeValue("id", "rclink-" + i);
                    link.SetAttributeValue("onmouseover", "showSuggestions(" + i + ")");
                    link.SetAttributeValue("onmouseout", "clearActiveLinkNumber()");
                    i++;
                }
            }

            return doc.DocumentNode.OuterHtml;
        }
    }
}
