using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine.Scripting;

namespace FlexibleSave.Enhanced
{
    [Preserve]
    public sealed class PrivateFieldContractResolver : DefaultContractResolver
    {
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var prop = base.CreateProperty(member, memberSerialization);
            if (!prop.Writable && member is FieldInfo field && !field.IsInitOnly)
            {
                prop.Writable = true;
            }
            return prop;
        }
    }
}
