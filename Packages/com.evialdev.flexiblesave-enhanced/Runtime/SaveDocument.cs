using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Scripting;

namespace FlexibleSave.Enhanced
{
    [Serializable, Preserve]
    public struct Vector2Data
    {
        public float x, y;

        public Vector2Data(Vector2 vec)
        {
            x = vec.x;
            y = vec.y;
        }

        public void FromVector2(Vector2 v)
        {
            x = v.x;
            y = v.y;
        }

        public readonly Vector2 ToVector2() => new Vector2(x, y);
    }

    [Serializable, Preserve]
    public struct Vector3Data
    {
        public float x, y, z;

        public Vector3Data(Vector3 vec)
        {
            x = vec.x;
            y = vec.y;
            z = vec.z;
        }

        public void FromVector3(Vector3 v)
        {
            x = v.x;
            y = v.y;
            z = v.z;
        }

        public readonly Vector3 ToVector3() => new Vector3(x, y, z);
    }

    [Serializable, Preserve]
    public struct Vector4Data
    {
        public float x, y, z, w;

        public Vector4Data(Vector4 vec)
        {
            x = vec.x;
            y = vec.y;
            z = vec.z;
            w = vec.w;
        }

        public Vector4Data(Quaternion q)
        {
            x = q.x;
            y = q.y;
            z = q.z;
            w = q.w;
        }

        public void FromVector4(Vector4 v)
        {
            x = v.x;
            y = v.y;
            z = v.z;
            w = v.w;
        }

        public void FromQuaternion(Quaternion q)
        {
            x = q.x;
            y = q.y;
            z = q.z;
            w = q.w;
        }

        public readonly Vector4 ToVector4() => new Vector4(x, y, z, w);
        public readonly Quaternion ToQuaternion() => new Quaternion(x, y, z, w);
    }

    [Serializable, Preserve]
    public struct LocRotScale
    {
        public Vector3Data position;
        public Vector4Data rotation;
        public Vector3Data scale;
    }

    [Serializable, Preserve]
    public struct TransformData
    {
        public LocRotScale local;
    }

    [Serializable, Preserve]
    public sealed class EntityMeta
    {
        public string name;
        public string id;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string parentId;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string parentPath;

        public bool hasPrefab;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string prefabId;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string childId;

        public bool saveTransform;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public TransformData? transform;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<string> deletedChildren;

        public bool needsReinstantiation;
    }

    [Serializable, Preserve]
    public sealed class EntityRecord
    {
        public EntityMeta meta;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, JToken> components;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<EntityRecord> children;
    }

    [Serializable, Preserve]
    public sealed class SaveDocument
    {
        public const string FormatName = "FlexibleSave";
        public const int CurrentFormatVersion = 1;

        public string format = FormatName;
        public int formatVersion = CurrentFormatVersion;
        public long savedAtUtc;
        public Dictionary<string, EntityRecord> entities = new Dictionary<string, EntityRecord>();
        public List<string> deleted = new List<string>();
    }
}
