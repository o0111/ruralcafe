﻿using HtmlAgilityPack;
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

            HtmlNodeCollection links = doc.DocumentNode.SelectNodes("//a");
            // Modify all links
            int i = 0;
            foreach(HtmlNode link in links)
            {
                link.SetAttributeValue("id", "rclink-" + i);
                link.SetAttributeValue("onmouseover", "showSuggestions(" + i + ")");
                i++;
            }

            return doc.DocumentNode.OuterHtml;
        }

    }
}