using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Threading;
using System.Web;
using System.Collections.Specialized;
using System.Text.RegularExpressions;

namespace RuralCafe
{
    public class RequestObject
    {
        public string _uri;
        public string _refererUri;
        public string _cacheFileName;
        public HttpWebRequest _webRequest;
        public int _status;
        public long _fileSize;
        public string _packageIndexFileName;
        public ManualResetEvent[] resetEvents;

        protected Dictionary<string, string> _searchFields;

        // only used by the transparent part of the proxy
        public string _recvString;

        public RequestObject(GenericProxy proxy, string uri) 
            : this(proxy, uri, "")
        {
            // do nothing
        }

        public RequestObject(GenericProxy proxy, string uri, string referrerUri)
        {
            _status = GenericRequest.STATUS_RECEIVED;

            _uri = uri.Trim();
            _refererUri = referrerUri.Trim();
            _webRequest = (HttpWebRequest)WebRequest.Create(_uri);

            _fileSize = 0;
            string fileName = UriToFilePath(_uri);
            _cacheFileName = proxy.CachePath + HashedFilePath(fileName);
            if (IsCompressed())
            {
                _cacheFileName = _cacheFileName + ".bz2";
            }
            _packageIndexFileName = proxy.PackagePath + fileName + ".gzip";
        }

        // override for object equality
        public override bool Equals(object obj)
        {
            if (_uri.Equals(((RequestObject)obj)._uri))
            {
                return true;
            }
            return false;
        }
        
        // override for object hash code
        public override int GetHashCode()
        {
            return _uri.GetHashCode();
        }

        private static string HashedFilePath(string fileName)
        {
            string filePath = hashedFileName(fileName) + fileName;
            return filePath;
        }
        // for resolving the filename to its hashed filename
        private static string hashedFileName(string fileName)
        {
            fileName = fileName.Replace("\\", ""); // compability with linux filepath delimeter
            int value1 = 0;
            int value2 = 0;

            int hashSpace = 5000;
            int length = fileName.Length;
            if (length == 0) {
                return "0\\0\\" + fileName;
            }

            value1 = HashString(fileName.Substring(length/2));
            value2 = HashString(fileName.Substring(0, length/2));

            if (value1 < 0)
            {
                value1 = value1 * -1;
            }

            if (value2 < 0)
            {
                value2 = value2 * -1;
            }

            value1 = value1 % hashSpace;
            value2 = value2 % hashSpace;

            string hashedPath = value1.ToString() + "\\" + value2.ToString() + "\\"; // +fileName;
            return hashedPath;
        }
        // port of python implementation of string hashing
        private static int HashString(string s)
        {
            if (s == null)
                return 0;
            int value = (int)s[0] << 7;
            for (int i = 0; i < s.Length; i++) {
                char c = s[i];
                value = (1000003 * value) ^ (int)c;
            }
            value = value ^ s.Length;
            if (value == -1) {
                value = -2;
            }
            return value;
        }

        // Reset event setter
        public void SetEvent(int objectNumber)
        {
            if (objectNumber < resetEvents.Length)
            {
                resetEvents[objectNumber].Set();
            }
            else
            {
                //LogDebug("Error, attempted to set objectNumber " + objectNumber + ", only " + resetEvents.Length + " events");
            }
        }

        // Translate a URI to a file path, synchronized with CIP implementation in Python
        public static string UriToFilePath(string uri)
        {
            // need to shrink the size of filenames for ruralcafe search prefs
            if (uri.Contains("ruralcafe.net") &&
                uri.Contains("textfield"))
            {
                int offset1 = uri.IndexOf("textfield");
                uri = uri.Substring(offset1 + "textfield".Length + 1);
                offset1 = uri.IndexOf('&');
                //offset1 = fileName.IndexOf("%26");
                if (offset1 > 0)
                {
                    uri = uri.Substring(0, offset1);
                }
            }

            // trim http header junk
            if (uri.StartsWith("http://"))
            {
                uri = uri.Substring(7);
            }

            if (uri.IndexOf("/") == -1)
            {
                uri = uri + "/";
            }

            uri = uri.Replace("/", "\\");

            uri = System.Web.HttpUtility.UrlDecode(uri);
            string fileName = MakeSafeUri(uri);

            // fix the filename extension
            if (fileName.EndsWith("\\"))
            {
                fileName = fileName + "index.html";
            }

            return fileName;
        }
        // private helper for uri to filepath
        private static string MakeSafeUri(string uri)
        {
            // first trim the raw string
            string safe = uri.Trim();

            // replace spaces with hyphens
            safe = safe.Replace(" ", "-").ToLower();

            // replace any 'double spaces' with singles
            if (safe.IndexOf("--") > -1)
                while (safe.IndexOf("--") > -1)
                    safe = safe.Replace("--", "-");

            // trim out illegal characters
            safe = Regex.Replace(safe, "[^a-z0-9\\\\\\-\\.]", "");


            // trim the length
            if (safe.Length > 220)
                safe = safe.Substring(0, 219);

            // clean the beginning and end of the filename
            char[] replace = { '-', '.' };
            safe = safe.TrimStart(replace);
            safe = safe.TrimEnd(replace);

            return safe;
        }

        // JJJ: synched with CIP implementation
        public bool IsCompressed()
        {
            if (_uri.EndsWith(".html") ||
                _uri.EndsWith(".htm") ||
                _uri.EndsWith(".txt") ||
                _uri.EndsWith(".xml") ||
                _uri.EndsWith(".js"))
            {
                return true;
            }
            return false;
        }

        // parse the RuralCafe query fields
        public void ParseSearchFields()
        {
            _searchFields = new Dictionary<string, string>();

            string queryString = "";
            int offset = _uri.IndexOf('?');
            if (offset >= 0)
            {
                queryString = (offset < _uri.Length - 1) ? _uri.Substring(offset + 1) : String.Empty;

                // Parse the query string variables into a NameValueCollection.
                try
                {
                    NameValueCollection qscoll = HttpUtility.ParseQueryString(queryString);

                    foreach (String key in qscoll.AllKeys)
                    {
                        if (!_searchFields.ContainsKey(key))
                        {
                            _searchFields.Add(key, qscoll[key]);
                        }
                    }
                }
                catch (Exception e)
                {
                    // nothing to parse
                    return;
                }
            }
        }

        // get the value for a particular RuralCafe search field
        public string GetRCSearchField(string key)
        {
            if (_searchFields.ContainsKey(key))
            {
                return _searchFields[key];
            }

            return "";
        }

        public string TranslateRCSearchToGoogle()
        {
            string searchTerms = GetRCSearchField("textfield");
            string googleWebRequestUri = "http://www.google.com/search?hl=en&q=" +
                                        searchTerms.Replace(' ', '+') +
                                        "&btnG=Google+Search&aq=f&oq=";

            return googleWebRequestUri;
        }
    }
}
