using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RuralCafe.Util
{
    using HtmlAgilityPack;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Util fields and methods for HTML handling.
    /// </summary>
    public static class HtmlUtils
    {
        /// <summary>
        /// Regex for html tags
        /// </summary>
        private static readonly Regex htmlTagRegex = new Regex(@"<[^<]+?>", RegexOptions.IgnoreCase);
        /// <summary>
        /// Tags that usually do not contain content text.
        /// </summary>
        private static string[] noContentTextHtmlTags = new string[] { "script", "meta", "style" };

        /// <summary>
        /// HTML attributes that represent links.
        /// </summary>
        public static string[,] LinkTagAttributes = new string[,] {
                  {"a",        "href"}
        };
        /// <summary>
        /// HTML attributes that represent embedded objects.
        /// </summary>
        public static string[,] EmbeddedObjectTagAttributes = new string[,] {
                  //{"base",        "href"},           // 2.0
                  //{"form",        "action"},         // 2.0g
                  {"img",         "src"},            // 2.0
                  {"link",        "href"},           // 2.0

                  {"applet",      "code"},           // 3.2
                  {"applet",      "codebase"},       // 3.2
                  //{"area",        "href"},           // 3.2g
                  {"body",        "background"},     // 3.2
                  //{"img",         "usemap"},         // 3.2g
                  {"input",       "src"},            // 3.2

                  {"applet",      "archive"},        // 4.01
                  {"applet",      "object"},         // 4.01
                  //{"blockquote",  "cite"},           // 4.01g
                  //{"del",         "cite"},           // 4.01g
                  //{"frame",       "longdesc"},       // 4.01g
                  {"frame",       "src"},            // 4.01
                  {"head",        "profile"},        // 4.01
                   //{"iframe",      "longdesc"},       // 4.01g
                  {"iframe",      "src"},            // 4.01
                   //{"img",         "longdesc"},       // 4.01g
                   //{"input",       "usemap"},         // 4.01g
                   //{"ins",         "cite"},           // 4.01g
                  {"object",      "archive"},        // 4.01
                   //{"object",      "classid"},        // 4.01g
                   //{"object",      "codebase"},       // 4.01g
                  {"object",      "data"},           // 4.01
                   //{"object",      "usemap"},         // 4.01g
                   //{"q",           "cite"},           // 4.01g
                  {"script",      "src"},            // 4.01

                  {"audio",       "src"},            // 5.0
                  {"command",     "icon"},           // 5.0
                  {"embed",       "src"},            // 5.0
                  {"event-source","src"},            // 5.0
                  //{"html",        "manifest"},       // 5.0g
                  {"source",      "src"},            // 5.0
                  {"video",       "src"},            // 5.0
                  {"video",       "poster"},         // 5.0

                  {"bgsound",     "src"},            // Extension
                  //{"body",        "credits"},        // Extensiong
                  //{"body",        "instructions"},   // Extensiong
                  //{"body",        "logo"},           // Extensiong
                  //{"div",         "href"},           // Extensiong
                  {"div",         "src"},            // Extension
                  {"embed",       "code"},           // Extension
                  {"embed",       "pluginspage"},    // Extension
                  {"html",        "background"},     // Extension
                  {"ilayer",      "src"},            // Extension
                  {"img",         "dynsrc"},         // Extension
                  {"img",         "lowsrc"},         // Extension
                  {"input",       "dynsrc"},         // Extension
                  {"input",       "lowsrc"},         // Extension
                  {"table",       "background"},     // Extension
                  {"td",          "background"},     // Extension
                  {"th",          "background"},     // Extension
                  {"layer",       "src"},            // Extension
                  {"xml",         "src"},            // Extension

                  //{"button",      "action"},         // Forms 2.0g
                  {"datalist",    "data"},           // Forms 2.0
                  {"form",        "data"},           // Forms 2.0
                  //{"input",       "action"},         // Forms 2.0g
                  {"select",      "data"},           // Forms 2.0

                  {"html",        "xmlns"},

                  //{"access",      "path"},           // 1.3
                  //{"card",        "onenterforward"}, // 1.3
                  //{"card",        "onenterbackward"},// 1.3
                  //{"card",        "ontimer"},        // 1.3
                  //{"go",          "href"},           // 1.3
                  //{"option",      "onpick"},         // 1.3
                  //{"template",    "onenterforward"}, // 1.3
                  //{"template",    "onenterbackward"},// 1.3
                  //{"template",    "ontimer"},        // 1.3
                  {"wml",         "xmlns"}          // 2.0
        };

        /// <summary>
        /// Gets the title from an HTML document.
        /// </summary>
        /// <param name="doc"></param>
        /// <returns></returns>
        private static string GetTitle(HtmlDocument doc)
        {
            HtmlNode titleNode = doc.DocumentNode.SelectSingleNode("/html/head/title");
            return titleNode != null ? titleNode.InnerText : "";
        }

        /// <summary>
        /// Gets the title from a page.
        /// </summary>
        /// <param name="fileName">The file name.</param>
        /// <returns>String containing the page title.</returns>
        public static string GetPageTitleFromFile(string fileName)
        {
            HtmlDocument doc = new HtmlDocument();
            doc.Load(fileName);
            return GetTitle(doc);
        }

        /// <summary>
        /// Gets the title from a page.
        /// </summary>
        /// <param name="pageContent">Page content.</param>
        /// <returns>String containing the page title.</returns>
        public static string GetPageTitleFromHTML(string pageContent)
        {
            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(pageContent);
            return GetTitle(doc);
        }

        /// <summary>
        /// Strips a string of all HTML tags.
        /// </summary>
        /// <param name="source">Page content.</param>
        /// <returns>String containing the stripped text.</returns>
        public static string StripTagsCharArray(string source)
        {
            return StripTagsCharArray(source, true);
        }

        /// <summary>
        /// Strips a string of all HTML tags, except bold tags, if wished.
        /// </summary>
        /// <param name="source">Page content.</param>
        /// <param name="stripBoldTags">If true, bold tags are stripped, too.</param>
        /// <returns>String containing the stripped text.</returns>
        public static string StripTagsCharArray(string source, bool stripBoldTags)
        {
            if (stripBoldTags)
            {
                return htmlTagRegex.Replace(source, "");
            }
            else
            {
                string result = htmlTagRegex.Replace(source, delegate(Match match)
                {
                    string matchedString = match.ToString();
                    return matchedString.Equals("<b>") || matchedString.Equals("</b>") ?
                        matchedString : "";
                });

                // XXX: sometimes there is "<" or ">" between "<b>" and "</b>". Then the closing bold tag
                // is not recognized. This may also be an error of the Lucene highlighter or its usage.
                return result;
            }
        }

        /// <summary>
        /// Removes the Head of a HTML string, if there is any.
        /// </summary>
        /// <param name="input">An HMTL string.</param>
        /// <returns>The same string, with anything before the body removed.</returns>
        public static string RemoveHead(string input)
        {
            // Remove everything before <body>, if there is a body.
            int index = input.IndexOf("<body>");
            return index != -1 ? input.Substring(index) : input;
        }

        /// <summary>
        /// Extracts Text from HTML pages.
        /// Source: http://stackoverflow.com/questions/2113651/how-to-extract-text-from-resonably-sane-html (Modified)
        /// </summary>
        /// <param name="html">The HTML text.</param>
        /// <returns>The plain text.</returns>
        public static string ExtractText(string html)
        {
            if (html == null)
            {
                throw new ArgumentNullException("html");
            }

            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(html);
            List<string> chunks = new List<string>();

            foreach (HtmlNode item in doc.DocumentNode.DescendantsAndSelf())
            {
                if (item.NodeType == HtmlNodeType.Text)
                {
                    if (item.ParentNode != null && !noContentTextHtmlTags.Contains(item.ParentNode.Name))
                    {
                        string trimmedInnerText;
                        if ((trimmedInnerText = item.InnerText.Trim()) != "")
                        {
                            chunks.Add(trimmedInnerText);
                        }
                    }
                }
            }
            return String.Join(" ", chunks);
        }
    }
}