using Newtonsoft.Json.Linq;
using Util;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Text;

namespace RuralCafe.Json
{
    /// <summary>
    /// A converter for RCRequests.
    /// </summary>
    public class RCRequestConverter : JsonCreationConverter<RCRequest>
    {
        private LocalRequestHandler _lrh;

        /// <summary>
        /// Creates a RCRequestConverter.
        /// </summary>
        /// <param name="lrh">The current local request handler.</param>
        public RCRequestConverter(LocalRequestHandler lrh)
        {
            _lrh = lrh;
        }

        /// <summary>
        /// Creates a RCRequest.
        /// </summary>
        /// <param name="objectType">The object type.</param>
        /// <param name="jObject">The JObject.</param>
        /// <returns>A RCRequest.</returns>
        protected override RCRequest Create(Type objectType, JObject jObject)
        {
            string url = jObject.GetValue("_webRequest")["RequestUri"].ToString();
            HttpWebRequest webRequest =  (HttpWebRequest)WebRequest.Create(url);
            // Restore the metadata from json
            ModifyWebRequest(webRequest, jObject.GetValue("_webRequest"));
            // Get RCRequest fields for the constructor
            string refererUri = jObject.GetValue("_refererUri").ToString();
            byte[] body = jObject.GetValue("_body").ToObject<byte[]>();

            // Remove _webRequest attribute, so Json.NET will not try to deserialize
            // automatically later
            jObject.Remove("_webRequest");

            return new RCRequest(_lrh.Proxy, webRequest, "", refererUri, body);
        }

        /// <summary>
        /// Modifies a web request by integrating metadata (headers, HTTP method, etc.)
        /// </summary>
        /// <param name="request">The web request.</param>
        /// <param name="jsonToken">The JSON token representing the web Request.</param>
        private void ModifyWebRequest(HttpWebRequest request, JToken jsonToken)
        {
            NameValueCollection headers = Utils.DictionaryToNVC(
                jsonToken["Headers"].ToObject<Dictionary<string, string[]>>());
            // Integrate headers
            HttpUtils.IntegrateHeadersIntoWebRequest(request, headers);
            // Set other fields
            request.Method = jsonToken["Method"].ToObject<string>();
            request.Accept = jsonToken["Accept"].ToObject<string>();
            request.UserAgent = jsonToken["UserAgent"].ToObject<string>();
            request.ContentLength = jsonToken["ContentLength"].ToObject<int>();
            request.ContentType = jsonToken["ContentType"].ToObject<string>();
            request.Referer = jsonToken["Referer"].ToObject<string>();
        }
    }
}
