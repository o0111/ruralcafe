using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
            logger.Debug(METRIC + " " + o);
        }

        /// <summary>
        /// Logs a user connected metric at debug level.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="userId">The user id</param>
        /// <param name="o">The object to log</param>
        public static void Metric(this ILog logger, int userId, object o)
        {
            logger.Debug(METRIC + " User: " + (userId != -1 ? "" + userId : "None") + " " + o);
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
                refererCategorization = "Hyperlink clicked, ";
                // This covers way too much.
                // just abort ATM
                return;
            }

            logger.Metric(userId, refererCategorization
                + (isCached ? "result cached, " : "result not cached, ") + "URI: " + uri);
        }
    }
}
