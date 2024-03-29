﻿using log4net;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml;

namespace RuralCafe.Util
{
    /// <summary>
    /// Extension methods to other classes are stored here.
    /// </summary>
    public static class ExtensionMethods
    {
        private const string METRIC = "METRIC";

        /// <summary>
        /// Logs a metric at debug level.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="o">The object to log</param>
        public static void Metric(this ILog logger, object o)
        {
            logger.Info(METRIC + " " + o);
        }

        /// <summary>
        /// Logs a user connected metric at debug level.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="userId">The user id</param>
        /// <param name="o">The object to log</param>
        public static void Metric(this ILog logger, int userId, object o)
        {
            logger.Info(METRIC + " User: " + (userId != -1 ? "" + userId : "None") + " " + o);
        }

        /// <summary>
        /// Logs a query.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="userId">The user id</param>
        /// <param name="isCached">Whether the result is in the cache.</param>
        /// <param name="referer">The referer</param>
        /// <param name="uri">The requested uri.</param>
        public static void QueryMetric(this ILog logger, int userId, bool isCached, string referer, string uri)
        {
            string refererCategorization;
            if (referer.StartsWith(RequestHandler.RC_PAGE + "result")
                || referer.StartsWith(RequestHandler.RC_PAGE_WITHOUT_WWW + "result"))
            {
                refererCategorization = "Search result clicked, ";
            }
            else if (referer.StartsWith(RequestHandler.RC_PAGE + "trotro-user.html")
                || referer.StartsWith(RequestHandler.RC_PAGE_WITHOUT_WWW + "trotro-user.html"))
            {
                refererCategorization = "Queue item clicked, ";
            }
            else
            {
                refererCategorization = "Hyperlink clicked or page subrequest, ";
            }

            logger.Metric(userId, refererCategorization
                + (isCached ? "result cached, " : "result not cached, ") + "URI: " + uri);
        }

        /// <summary>
        /// Removes all childs from an XML element.
        /// </summary>
        /// <param name="element">The element.</param>
        public static void RemoveAllChilds(this XmlElement element)
        {
            // Do NOT use foreach or count forward!
            for (int i = element.ChildNodes.Count - 1; i >= 0; i--)
            {
                element.RemoveChild(element.ChildNodes[i]);
            }
        }

        /// <summary>
        /// Converts a cookie into a string that can be used in Headers.Add.
        /// Only "expires" and "path" are set apart from the name/value pair.
        /// </summary>
        /// <returns>The cookie string.</returns>
        public static string ToCookieString(this Cookie cookie)
        {
            return cookie.ToString() + "; expires=" + cookie.Expires.ToUniversalTime().ToString("r") + " ; path=" + cookie.Path;
        }
    }
}
