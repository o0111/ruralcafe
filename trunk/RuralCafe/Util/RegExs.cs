using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RuralCafe.Util
{
    /// <summary>
    /// Provides some useful RegEx's
    /// </summary>
    public static class RegExs
    {
        /// <summary>Newlines.</summary>
        public static readonly Regex NEWLINE_REGEX = new Regex(@"\r\n|\n|\r");
        /// <summary>Chars unsafe for URI replacements 1.</summary>
        public static readonly Regex UNSAFE_CHARS1_REGEX = new Regex(@"[^a-z0-9\\\-\.]");
        /// <summary>Chars unsafe for URI replacements 2.</summary>
        public static readonly Regex UNSAFE_CHARS2_REGEX = new Regex(@"[^a-z0-9/\-\.]");
        /// <summary>Regex that matches two or more spaces. Useful for trimming them to one space.</summary>
        public static Regex MULTIPLE_SPACES_REGEX = new Regex(@"\s\s+");
        /// <summary>Regex that matches the number of search results in a google results page.</summary>
        public static Regex GOOGLE_RESULTS_NUM_REGEX = new Regex("<div id=\"resultStats\">(Page \\d+ of a|A)bout (?<num>[\\d,]+) results");
        /// <summary>Regex to identify a HTTP redirection.</summary>
        public static readonly Regex REDIR_REGEX = new Regex(@"HTTP/1\.1 301 Moved Permanently\s?Location: (?<uri>\S+)");
        /// <summary>Regex for html tags.</summary>
        public static readonly Regex HTML_TAG_REGEX = new Regex(@"<[^<]+?>", RegexOptions.IgnoreCase);
        /// <summary>Matches "localhost" or "127.0.0.1" followed by anything but a dot.
        /// Provides mathcing groups add1 and add2</summary>
        public static readonly Regex LOCAL_ADDRESS_REGEX = new Regex(@"(?<add1>(localhost|127\.0\.0\.1))(?<add2>[^\.])");
    }
}
