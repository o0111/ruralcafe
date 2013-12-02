using HtmlAgilityPack;
using RuralCafe.Lucenenet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RuralCafe.LinkSuggestion
{
    /// <summary>
    /// Helper methods for the link suggestions.
    /// </summary>
    public static class LinkSuggestionHelper
    {
        /// <summary>
        /// The boosts for the different link suggestion fields.
        /// </summary>
        public static readonly float[] LINK_SUGGESTION_BOOSTS = new float[] {
                1, // url
                0.5f, // refUrl
                2, // anchorText
                1 //surroundingText
            };

        /// <summary>
        /// If this is the anchor text, it will have no link suggestions.
        /// </summary>
        public static readonly string[] LINK_ANCHOR_BLACKLIST = new string[] {
            "home", "here", "there", "see", "see more", "show", "view", "follow", "edit",
            "login", "register", "signup", "sign up", "check in", "help", "email", "e-mail",
            "summary", "faq", "search", "index", "rss", "copyright", "conditions", "privacy policy",
            "options", "forum", "members", "users", "chat", "about", "blog", "contact", "connect",
            "terms", "privacy", "main", "reset", "reply", "post", "send", "answer"
        };

        /// <summary>
        /// Gets the link suggestions for an uncached link.
        /// </summary>
        /// <param name="url">The absolute URL.</param>
        /// <param name="refUrl">The referer URL.</param>
        /// <param name="anchorText">The anchor text.</param>
        /// <param name="surroundingText">The surrounding text.</param>
        /// <param name="amount">The amount of suggestions to get.</param>
        /// <param name="proxy">The local proxy.</param>
        /// <returns>The suggestions.</returns>
        public static IEnumerable<SearchResult> GetLinkSuggestions(string url, string refUrl, string anchorText,
            string surroundingText, int amount, RCLocalProxy proxy)
        {
            // Remove all http:// or https:// from the query
            string url0 = url.Replace("http://", "").Replace("https://", "");
            string refUrl0 = refUrl.Replace("http://", "").Replace("https://", "");
            string anchorText0 = anchorText.Replace("http://", "").Replace("https://", "");
            string surroundingText0 = surroundingText.Replace("http://", "").Replace("https://", "");

            // If we're debugging, we want 101 results, otherwise 
            // we want one result more, as we're very probably going to find the referrer page
            int amount0 = Properties.Network.Default.LS_DEBUG ? 101 : amount + 1;

            SearchResults luceneResults = proxy.IndexWrapper.Query(new string[]
                { url0, refUrl0, anchorText0, surroundingText0}, LINK_SUGGESTION_BOOSTS,
                proxy.CachePath, 0, amount0, true, -1);

            // remove the referrer page from the results
            for (int i = 0; i < luceneResults.Results.Count; i++)
            {
                if (luceneResults.Results[i].URI.ToLower().Equals(refUrl.ToLower()))
                {
                    luceneResults.RemoveDocument(i);
                    break;
                }
            }
            // In the rare case that the referrer page was not among the results, we have to remove the last result
            if (luceneResults.Results.Count > amount0)
            {
                luceneResults.RemoveDocument(luceneResults.Results.Count - 1);
            }

            if (Properties.Network.Default.LS_DEBUG)
            {
                bool found = false;
                // If we're debugging we want to find out the position of the original url.
                for (int i = 0; i < luceneResults.Results.Count; i++)
                {
                    if (luceneResults.Results[i].URI.ToLower().Equals(url.ToLower()))
                    {
                        found = true;
                        // Warn level makes it easier to find.
                        proxy.Logger.Warn(String.Format("LS_DEBUG: {0}|{1}|{2}", i, refUrl0, url0));
                        break;
                    }
                }
                if (!found)
                {
                    proxy.Logger.Warn(String.Format("LS_DEBUG: -1|{1}|{2}", refUrl0, url0));
                }
                return luceneResults.Take(amount);
            }

            return luceneResults;
        }

        /// <summary>
        /// Injects tooltips for every link in the HTML with meaningful text.
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
            opentipStyle.SetAttributeValue("type", "text/css");
            opentipStyle.SetAttributeValue("rel", "stylesheet");
            opentipStyle.SetAttributeValue("href", "http://www.ruralcafe.net/css/opentip.css");
            head.AppendChild(opentipStyle);
            // include opentip js document
            HtmlNode opentipJs = doc.CreateElement("script");
            opentipJs.SetAttributeValue("type", "text/javascript");
            opentipJs.SetAttributeValue("src", "http://www.ruralcafe.net/js/opentip-native.min.js");
            head.AppendChild(opentipJs);
            // include our js document
            HtmlNode ourJs = doc.CreateElement("script");
            ourJs.SetAttributeValue("type", "text/javascript");
            ourJs.SetAttributeValue("src", "http://www.ruralcafe.net/js/linkSuggestion.js");
            head.AppendChild(ourJs);
            // include our ajax js document (Opentip ajax does not like us...)
            HtmlNode ourAjaxJs = doc.CreateElement("script");
            ourAjaxJs.SetAttributeValue("type", "text/javascript");
            ourAjaxJs.SetAttributeValue("src", "http://www.ruralcafe.net/js/ajax.js");
            head.AppendChild(ourAjaxJs);
            // Include the invisible trigger element
            HtmlNode trigger = doc.CreateElement("div");
            trigger.SetAttributeValue("id", "rclink-trigger");
            body.AppendChild(trigger);

            // Gets all links containing text.
            HtmlNodeCollection links = doc.DocumentNode.SelectNodes("//a[not(node()[2])][text()]/@href");
            if (links != null)
            {
                // Modify all links
                int i = 0;
                int ign;
                foreach (HtmlNode link in links)
                {
                    if (LINK_ANCHOR_BLACKLIST.Contains(link.InnerText.ToLower())
                        || Int32.TryParse(link.InnerText, out ign))
                    {
                        // No "Here", ... links or number links
                        continue;
                    }

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
