using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Util;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;

namespace RuralCafe.Json
{
    /// <summary>
    /// A converter for HttpWebRequests that serializes only some properties. Only for serialization.
    /// </summary>
    public class HttpWebRequestConverter : JsonConverter
    {
        /// <summary>
        /// A type can be converted if it's a subtype of HttpWebRequest.
        /// </summary>
        /// <param name="objectType">The object type.</param>
        /// <returns>If this can convert that type.</returns>
        public override bool CanConvert(Type objectType)
        {
            return typeof(HttpWebRequest).IsAssignableFrom(objectType);
        }

        /// <summary>
        /// This class is only for serialization.
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="objectType"></param>
        /// <param name="existingValue"></param>
        /// <param name="serializer"></param>
        /// <returns></returns>
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Writes the JSON.
        /// </summary>
        /// <param name="writer">The JSON writer</param>
        /// <param name="value">The object to convert.</param>
        /// <param name="serializer">The serializer</param>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            HttpWebRequest request = (HttpWebRequest)value;
            // Only save the properties we need.
            var requestProperties = new
            {
                Headers = request.Headers,
                RequestUri = request.RequestUri,
                Method = request.Method,
                Accept = request.Accept,
                UserAgent = request.UserAgent,
                ContentLength = request.ContentLength,
                ContentType = request.ContentType,
                Referer = request.Referer,
            };
            serializer.Serialize(writer, requestProperties);
        }
    }
}
