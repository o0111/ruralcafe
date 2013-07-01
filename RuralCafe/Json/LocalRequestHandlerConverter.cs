using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RuralCafe.Json
{
    /// <summary>
    /// A converter for LocalRequestHandlers.
    /// </summary>
    public class LocalRequestHandlerConverter : JsonCreationConverter<LocalRequestHandler>
    {
        private RCLocalProxy _proxy;

        /// <summary>
        /// Creates a LocalRequestHandlerConverter.
        /// </summary>
        /// <param name="proxy">The current local proxy.</param>
        public LocalRequestHandlerConverter(RCLocalProxy proxy)
        {
            this._proxy = proxy;
        }

        /// <summary>
        /// Creates a LocalRequestHandler.
        /// </summary>
        /// <param name="objectType">The object type.</param>
        /// <param name="jObject">The JObject.</param>
        /// <returns>A LocalRequestHandler.</returns>
        protected override LocalRequestHandler Create(Type objectType, JObject jObject)
        {
            // Create a requesthandler with the provided proxy.
            return new LocalRequestHandler(_proxy);
        }

        /// <summary>
        /// Reads JSON and returns an object of type LocalRequestHandler. Does the same as in the base class,
        /// but adds (and removes) a RCRequestConverter for the field population.
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
            LocalRequestHandler target = LoadAndCreate(reader, objectType, existingValue, serializer, out jObject);

            // Add converter
            RCRequestConverter converter = new RCRequestConverter(target);
            serializer.Converters.Add(converter);
            // Populate the object properties
            serializer.Populate(jObject.CreateReader(), target);
            // Remove converter
            serializer.Converters.Remove(converter);

            return target;
        }
    }
}
