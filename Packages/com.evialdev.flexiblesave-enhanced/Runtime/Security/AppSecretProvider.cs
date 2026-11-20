using System;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace FlexibleSave.Enhanced.Security
{
    public static partial class AppSecretProvider
    {
        [AutoStaticsCleanup]
        private static string s_cachedSecret;

        /// <summary>
        /// Returns a cached application-scoped secret derived from <see cref="Application.identifier"/>
        /// with fallback to <see cref="Application.productName"/>.
        /// Must be initialized on the Unity main thread before use on background threads.
        /// </summary>
        public static string GetSecret()
        {
            if (!string.IsNullOrEmpty(s_cachedSecret))
            {
                return s_cachedSecret;
            }

            string raw = Application.identifier;
            if (string.IsNullOrWhiteSpace(raw))
            {
                raw = Application.productName;
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidOperationException(
                    "Both Application.identifier and Application.productName are empty; cannot derive default save encryption secret.");
            }

            s_cachedSecret = raw;
            return s_cachedSecret;
        }

        internal static void WarmCache()
        {
            _ = GetSecret();
        }
    }
}
