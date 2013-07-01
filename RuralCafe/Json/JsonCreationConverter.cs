using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RuralCafe.Json
{
    /// <summary>
    /// Taken from
    /// http://stackoverflow.com/questions/8030538/how-to-implement-custom-jsonconverter-in-json-net-to-deserialize-a-list-of-base
    /// </summary>
    /// <typeparam name="T">The type to deserialize.</typeparam>
    public abstract class JsonCreationConverter<T> : JsonConverter
    {
        /// <summary>
        /// Create an instance of objectType, based properties in the JSON object
        /// </summary>
        /// <param name="objectType">type of object expected</param>
        /// <param name="jObject">contents of JSON object that will be deserialized</param>
        /// <returns></returns>
        protected abstract T Create(Type objectType, JObject jObject);

        /// <summary>
        /// A type can be converted if it's a subtype of T.
        /// </summary>
        /// <param name="objectType">The object type.</param>
        /// <returns>If this can convert that type.</returns>
        public override bool CanConvert(Type objectType)
        {
            return typeof(T).IsAssignableFrom(objectType);
        }

        /// <summary>
        /// Reads JSON and returns an object of type T.
        /// </summary>
        /// <param name="reader">The JSON reader.</param>
        /// <param name="objectType">The object type.</param>
        /// <param name="existingValue">The existing value.</param>
        /// <param name="serializer">The JSON serializer.</param>
        /// <returns>An object of type T</returns>
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            JObject jObject;
            // Create target object based on JObject
            T target = LoadAndCreate(reader, objectType, existingValue, serializer, out jObject);

            // Populate the object properties
            serializer.Populate(jObject.CreateReader(), target);

            return target;
        }

        /// <summary>
        /// Loads the JSON and created the object.
        /// </summary>
        /// <param name="reader">The JSON reader.</param>
        /// <param name="objectType">The object type.</param>
        /// <param name="existingValue">The existing value.</param>
        /// <param name="serializer">The JSON serializer.</param>
        /// <param name="jObject">An out var, where the JObject will be stored.</param>
        /// <returns>An object of type T</returns>
        protected virtual T LoadAndCreate(JsonReader reader, Type objectType, object existingValue,
            JsonSerializer serializer, out JObject jObject)
        {
            // Load JObject from stream
            jObject = JObject.Load(reader);

            // Create target object based on JObject
            return Create(objectType, jObject);
        }

        /// <summary>
        /// This class is only for deserialization.
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="value"></param>
        /// <param name="serializer"></param>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}
