using System;
using System.Security.Cryptography;
using System.Text;

namespace SaveLoadSystem
{
    /// <summary>
    /// Portable authenticated UTF-8 string encryption using only .NET cryptography APIs.
    /// Both endpoints must supply the same high-entropy shared passphrase and use this format.
    /// No Unity, device identity, storage, or transport dependencies are required.
    /// </summary>
    public static class EncryptionUtility
    {
        private const string Prefix = "FSS2:";
        private const int SaltSize = 16;
        private const int IvSize = 16;
        private const int MacSize = 32;
        private const int HeaderSize = 1 + SaltSize + IvSize;
        // Part of the FSS2 format: do not change independently at either endpoint.
        // Performance compromise; use a high-entropy shared secret, not a human password.
        private const int DerivationIterations = 100000;

        public static bool IsCurrentFormat(string cipherText)
        {
            return cipherText != null && cipherText.StartsWith(Prefix, StringComparison.Ordinal);
        }

        public static string Encrypt(string plainText, string passPhrase)
        {
            if (plainText == null) throw new ArgumentNullException(nameof(plainText));
            ValidatePassPhrase(passPhrase);
            byte[] salt = RandomBytes(SaltSize);
            byte[] iv = RandomBytes(IvSize);
            byte[] key = DeriveKey(passPhrase, salt);
            try
            {
                byte[] cipher;
                using (var aes = Aes.Create())
                {
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = key;
                    aes.IV = iv;
                    byte[] plain = Encoding.UTF8.GetBytes(plainText);
                    using (var encryptor = aes.CreateEncryptor())
                        cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);
                }

                var payload = new byte[HeaderSize + cipher.Length + MacSize];
                payload[0] = 2;
                Buffer.BlockCopy(salt, 0, payload, 1, SaltSize);
                Buffer.BlockCopy(iv, 0, payload, 1 + SaltSize, IvSize);
                Buffer.BlockCopy(cipher, 0, payload, HeaderSize, cipher.Length);
                byte[] mac = ComputeMac(key, payload, payload.Length - MacSize);
                Buffer.BlockCopy(mac, 0, payload, payload.Length - MacSize, MacSize);
                return Prefix + Convert.ToBase64String(payload);
            }
            finally { Array.Clear(key, 0, key.Length); }
        }

        public static string Decrypt(string cipherText, string passPhrase)
        {
            ValidatePassPhrase(passPhrase);
            if (!IsCurrentFormat(cipherText))
                throw new CryptographicException("Unsupported encrypted data format.");

            byte[] payload = Convert.FromBase64String(cipherText.Substring(Prefix.Length));
            int cipherLength = payload.Length - HeaderSize - MacSize;
            if (payload.Length < HeaderSize + IvSize + MacSize || payload[0] != 2 || cipherLength % IvSize != 0)
                throw new CryptographicException("Invalid encrypted data structure.");

            var salt = new byte[SaltSize];
            var iv = new byte[IvSize];
            Buffer.BlockCopy(payload, 1, salt, 0, SaltSize);
            Buffer.BlockCopy(payload, 1 + SaltSize, iv, 0, IvSize);
            byte[] key = DeriveKey(passPhrase, salt);
            try
            {
                int macOffset = payload.Length - MacSize;
                byte[] expected = ComputeMac(key, payload, macOffset);
                int difference = 0;
                for (int i = 0; i < MacSize; i++)
                    difference |= expected[i] ^ payload[macOffset + i];
                if (difference != 0)
                    throw new CryptographicException("Data authentication failed: wrong passphrase or modified data.");

                using (var aes = Aes.Create())
                {
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = key;
                    aes.IV = iv;
                    using (var decryptor = aes.CreateDecryptor())
                    {
                        byte[] plain = decryptor.TransformFinalBlock(payload, HeaderSize, cipherLength);
                        return new UTF8Encoding(false, true).GetString(plain);
                    }
                }
            }
            finally { Array.Clear(key, 0, key.Length); }
        }

        private static byte[] DeriveKey(string passPhrase, byte[] salt)
        {
            using (var kdf = new Rfc2898DeriveBytes(passPhrase, salt, DerivationIterations, HashAlgorithmName.SHA256))
                return kdf.GetBytes(32);
        }

        private static byte[] ComputeMac(byte[] key, byte[] payload, int count)
        {
            // Domain separation produces an independent authentication key from the master key.
            byte[] macKey;
            using (var derivation = new HMACSHA256(key))
                macKey = derivation.ComputeHash(Encoding.ASCII.GetBytes("FlexibleSaveSystem/FSS2/authentication"));
            try
            {
                using (var hmac = new HMACSHA256(macKey))
                    return hmac.ComputeHash(payload, 0, count);
            }
            finally { Array.Clear(macKey, 0, macKey.Length); }
        }

        private static byte[] RandomBytes(int count)
        {
            var bytes = new byte[count];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return bytes;
        }

        private static void ValidatePassPhrase(string passPhrase)
        {
            if (string.IsNullOrWhiteSpace(passPhrase))
                throw new ArgumentException("An encryption passphrase is required.", nameof(passPhrase));
        }
    }
}
