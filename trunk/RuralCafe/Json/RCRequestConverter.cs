using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
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
            string url = jObject.GetValue("_webRequest")["_OriginUri"].ToString();
            HttpWebRequest webRequest =  (HttpWebRequest)WebRequest.Create(url);
            // TODO restore metadata!
            string refererUri = jObject.GetValue("_refererUri").ToString();
            byte[] body = jObject.GetValue("_body").ToObject<byte[]>();

            // Remove _webRequest attribute, so Json.NET will not try to deserialize
            // automatically later
            jObject.Remove("_webRequest");

            return new RCRequest(_lrh, webRequest, "", refererUri, body);
        }
    }
}
