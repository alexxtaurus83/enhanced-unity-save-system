using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Scripting;

namespace FlexibleSave.Enhanced
{
    [Preserve]
    public static class SaveJson
    {
        private static readonly PrivateFieldContractResolver s_resolver = new PrivateFieldContractResolver();

        public static readonly JsonSerializerSettings CompactSettings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            Formatting = Formatting.None,
            TypeNameHandling = TypeNameHandling.None,
            ContractResolver = s_resolver
        };

        public static readonly JsonSerializerSettings IndentedSettings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            Formatting = Formatting.Indented,
            TypeNameHandling = TypeNameHandling.None,
            ContractResolver = s_resolver
        };

        public static readonly JsonSerializer Serializer = JsonSerializer.Create(CompactSettings);
        public static readonly JsonSerializer IndentedSerializer = JsonSerializer.Create(IndentedSettings);

        public static string Serialize(object value, bool indented = false)
        {
            return JsonConvert.SerializeObject(value, indented ? IndentedSettings : CompactSettings);
        }

        public static JToken ToToken(object value)
        {
            if (value == null)
            {
                return JValue.CreateNull();
            }
            if (value is JToken token)
            {
                return token;
            }
            return JToken.FromObject(value, Serializer);
        }
    }

    [Preserve]
    public static class SaveState
    {
        /// <summary>
        /// Converts a state object passed to <see cref="ISaveable.LoadState"/> or
        /// <see cref="ISaveable.AllObjectsStateLoaded"/> into <typeparamref name="T"/>.
        /// Handles both direct typed objects and <see cref="JToken"/> instances.
        /// </summary>
        public static T As<T>(object state)
        {
            if (state is T typed)
            {
                return typed;
            }

            if (state is JToken token)
            {
                if (token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
                {
                    return default;
                }
                return token.ToObject<T>(SaveJson.Serializer);
            }

            if (state == null)
            {
                return default;
            }

            return JToken.FromObject(state, SaveJson.Serializer).ToObject<T>(SaveJson.Serializer);
        }
    }
}
