using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace SaveLoadSystem
{
    /// <summary>
    /// Strict type serialization binder that validates type metadata during deserialization.
    /// Prevents arbitrary code execution and deserialization attacks by enforcing a strict type whitelist.
    /// </summary>
    public class StrictTypeSerializationBinder : DefaultSerializationBinder
    {
        private static readonly HashSet<Type> s_allowedTypes = new HashSet<Type>
        {
            typeof(object),
            typeof(string),
            typeof(bool),
            typeof(byte),
            typeof(sbyte),
            typeof(short),
            typeof(ushort),
            typeof(int),
            typeof(uint),
            typeof(long),
            typeof(ulong),
            typeof(float),
            typeof(double),
            typeof(decimal),
            typeof(char),
            typeof(Guid),
            typeof(DateTime),
            typeof(DateTimeOffset),
            typeof(TimeSpan)
        };

        private static readonly HashSet<Type> s_allowedGenericDefinitions = new HashSet<Type>
        {
            typeof(List<>),
            typeof(Dictionary<,>),
            typeof(HashSet<>),
            typeof(KeyValuePair<,>),
            typeof(Queue<>),
            typeof(Stack<>),
            typeof(LinkedList<>)
        };

        private static readonly HashSet<string> s_allowedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> s_blockedPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System.Diagnostics",
            "System.IO",
            "System.Reflection",
            "System.Security",
            "System.Configuration",
            "System.Runtime",
            "System.Windows",
            "System.Management",
            "System.Net",
            "System.CodeDom",
            "System.Web",
            "System.Activities",
            "System.Workflow",
            "Microsoft.Win32"
        };

        /// <summary>
        /// Optional delegate to customize type validation for project-specific requirements.
        /// </summary>
        public static Func<Type, bool> CustomTypeValidator { get; set; }

        /// <summary>
        /// Explicitly registers an allowed type for deserialization.
        /// </summary>
        public static void RegisterType(Type type)
        {
            if (type != null)
            {
                lock (s_allowedTypes)
                {
                    s_allowedTypes.Add(type);
                }
            }
        }

        /// <summary>
        /// Explicitly registers an allowed type for deserialization.
        /// </summary>
        public static void RegisterType<T>()
        {
            RegisterType(typeof(T));
        }

        /// <summary>
        /// Explicitly registers an allowed assembly name for deserialization.
        /// </summary>
        public static void RegisterAssembly(string assemblyName)
        {
            if (!string.IsNullOrEmpty(assemblyName))
            {
                lock (s_allowedAssemblies)
                {
                    s_allowedAssemblies.Add(assemblyName);
                }
            }
        }

        /// <summary>
        /// Explicitly registers an allowed assembly for deserialization.
        /// </summary>
        public static void RegisterAssembly(Assembly assembly)
        {
            if (assembly != null)
            {
                RegisterAssembly(assembly.GetName().Name);
            }
        }

        public override Type BindToType(string assemblyName, string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;

            // Block known dangerous namespaces immediately
            foreach (var blocked in s_blockedPrefixes)
            {
                if (typeName.StartsWith(blocked, StringComparison.OrdinalIgnoreCase))
                {
                    throw new JsonSerializationException($"Deserialization of type '{typeName}' is blocked for security reasons.");
                }
            }

            Type resolvedType = null;
            try
            {
                resolvedType = base.BindToType(assemblyName, typeName);
            }
            catch
            {
                // Fallback resolution across loaded assemblies if base resolution fails
            }

            if (resolvedType == null)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    resolvedType = assembly.GetType(typeName);
                    if (resolvedType != null)
                        break;
                }
            }

            if (resolvedType == null)
            {
                throw new JsonSerializationException($"Type '{typeName}' in assembly '{assemblyName}' could not be resolved.");
            }

            if (!IsTypeAllowed(resolvedType))
            {
                throw new JsonSerializationException($"Deserialization of type '{resolvedType.FullName}' is not allowed for security reasons.");
            }

            return resolvedType;
        }

        private static bool IsTypeAllowed(Type type)
        {
            if (type == null) return false;

            lock (s_allowedTypes)
            {
                if (s_allowedTypes.Contains(type)) return true;
            }

            if (type.IsPrimitive || type.IsEnum) return true;

            Type underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null)
                return IsTypeAllowed(underlying);

            if (type.IsArray)
                return IsTypeAllowed(type.GetElementType());

            if (type.IsGenericType)
            {
                Type genericDef = type.GetGenericTypeDefinition();
                if (s_allowedGenericDefinitions.Contains(genericDef))
                {
                    foreach (Type arg in type.GetGenericArguments())
                    {
                        if (!IsTypeAllowed(arg))
                            return false;
                    }
                    return true;
                }
            }

            string ns = type.Namespace ?? string.Empty;
            foreach (var blocked in s_blockedPrefixes)
            {
                if (ns.Equals(blocked, StringComparison.OrdinalIgnoreCase) ||
                    ns.StartsWith(blocked + ".", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            // SaveLoadSystem namespace is trusted
            if (ns.Equals("SaveLoadSystem", StringComparison.OrdinalIgnoreCase) ||
                ns.StartsWith("SaveLoadSystem.", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Unity value types (Vector2, Vector3, Quaternion, Color, Rect, Bounds, etc.) are safe
            if (ns.Equals("UnityEngine", StringComparison.OrdinalIgnoreCase) ||
                ns.StartsWith("UnityEngine.", StringComparison.OrdinalIgnoreCase))
            {
                if (type.IsValueType)
                    return true;
            }

            if (CustomTypeValidator != null && CustomTypeValidator(type))
            {
                return true;
            }

            string asmName = type.Assembly.GetName().Name ?? string.Empty;
            lock (s_allowedAssemblies)
            {
                if (s_allowedAssemblies.Contains(asmName))
                    return true;
            }

            bool isSystemAssembly = asmName.Equals("mscorlib", StringComparison.OrdinalIgnoreCase) ||
                                   asmName.StartsWith("System", StringComparison.OrdinalIgnoreCase) ||
                                   asmName.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) ||
                                   asmName.StartsWith("UnityEditor", StringComparison.OrdinalIgnoreCase) ||
                                   asmName.StartsWith("Newtonsoft", StringComparison.OrdinalIgnoreCase);

            // In user game assemblies, allow value types, ISaveable implementers, or [Serializable] types
            if (!isSystemAssembly)
            {
                if (type.IsValueType ||
                    typeof(ISaveable).IsAssignableFrom(type) ||
                    (type.DeclaringType != null && typeof(ISaveable).IsAssignableFrom(type.DeclaringType)) ||
                    type.IsDefined(typeof(SerializableAttribute), false))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
