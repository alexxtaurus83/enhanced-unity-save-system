using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace SaveLoadSystem
{
    public static class AppSecretProvider
    {
        // Public, reproducible inputs intentionally provide only protection against casual editing.
        // Keep the application identifier and this derivation unchanged to read restored saves.
        public static string GetSecret()
        {
            string appId = Application.identifier;
            if (string.IsNullOrWhiteSpace(appId))
                throw new InvalidOperationException("An application identifier is required for save obfuscation.");

            using (var hash = SHA256.Create())
                return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes("FlexibleSaveSystem/app-obfuscation/v1:" + appId)));
        }
    }
}
