using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RuralCafe.Util;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;

namespace RuralCafe.Json
{
    /// <summary>
    /// A converter that treats NameValueCollections as Dictionaries. Only for serialization.
    /// </summary>
    public class NameValueCollectionConverter : JsonConverter
    {
        /// <summary>
        /// A type can be converted if it's a subtype of NameValueCollection.
        /// </summary>
        /// <param name="objectType">The object type.</param>
        /// <returns>If this can convert that type.</returns>
        public override bool CanConvert(Type objectType)
        {
            return typeof(NameValueCollection).IsAssignableFrom(objectType);
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
            NameValueCollection nvc = (NameValueCollection) value;
            Dictionary<string, string[]> dict = Utils.NVCToDictionary(nvc);
            serializer.Serialize(writer, dict);
        }
    }
}
