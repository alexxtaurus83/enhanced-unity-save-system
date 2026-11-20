using System;
using System.Security.Cryptography;
using System.Text;

namespace FlexibleSave.Enhanced.Security
{
    /// <summary>
    /// Fast binary encryptor using cached SHA-256 derived keys, AES-128-CBC with a random 16-byte IV per save,
    /// and a 16-byte truncated HMAC-SHA256 authentication tag.
    /// Intended for light protection against casual tampering, not cryptographic security against determined attackers.
    /// </summary>
    public sealed class LightSaveEncryptor : ISaveEncryptor
    {
        public const string DefaultId = "fs-light-v1";

        private const byte PayloadVersion = 1;
        private const int VersionSize = 1;
        private const int IvSize = 16;
        private const int AesBlockSize = 16;
        private const int MacSize = 16;
        private const int HeaderSize = VersionSize + IvSize;
        private const string Salt = "FlexibleSave.Enhanced/LightSaveEncryptor/v1:";

        private readonly Func<string> m_secretProvider;
        private readonly object m_keyLock = new object();
        private string m_cachedSecret;
        private byte[] m_encKey;
        private byte[] m_macKey;

        public string Id => DefaultId;

        public LightSaveEncryptor(Func<string> secretProvider = null)
        {
            m_secretProvider = secretProvider ?? AppSecretProvider.GetSecret;
        }

        public byte[] Encrypt(byte[] plain)
        {
            if (plain == null)
            {
                throw new ArgumentNullException(nameof(plain));
            }

            GetKeys(out byte[] encKey, out byte[] macKey);

            var iv = new byte[IvSize];
            RandomNumberGenerator.Fill(iv);

            byte[] cipherBytes;
            using (var aes = Aes.Create())
            {
                aes.KeySize = 128;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = encKey;
                aes.IV = iv;
                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                {
                    cipherBytes = encryptor.TransformFinalBlock(plain, 0, plain.Length);
                }
            }

            int authenticatedLen = HeaderSize + cipherBytes.Length;
            var payload = new byte[authenticatedLen + MacSize];
            payload[0] = PayloadVersion;
            Buffer.BlockCopy(iv, 0, payload, VersionSize, IvSize);
            Buffer.BlockCopy(cipherBytes, 0, payload, HeaderSize, cipherBytes.Length);

            using (var hmac = new HMACSHA256(macKey))
            {
                byte[] fullMac = hmac.ComputeHash(payload, 0, authenticatedLen);
                Buffer.BlockCopy(fullMac, 0, payload, authenticatedLen, MacSize);
            }

            return payload;
        }

        public byte[] Decrypt(byte[] cipher)
        {
            if (cipher == null)
            {
                throw new ArgumentNullException(nameof(cipher));
            }

            int minLength = HeaderSize + AesBlockSize + MacSize;
            int cipherLength = cipher.Length - HeaderSize - MacSize;
            if (cipher.Length < minLength || cipher[0] != PayloadVersion || (cipherLength % AesBlockSize) != 0)
            {
                throw new CryptographicException("Invalid encrypted save payload structure.");
            }

            GetKeys(out byte[] encKey, out byte[] macKey);

            int macOffset = cipher.Length - MacSize;
            using (var hmac = new HMACSHA256(macKey))
            {
                byte[] expectedMac = hmac.ComputeHash(cipher, 0, macOffset);
                int diff = 0;
                for (int i = 0; i < MacSize; i++)
                {
                    diff |= expectedMac[i] ^ cipher[macOffset + i];
                }

                if (diff != 0)
                {
                    throw new CryptographicException("Save data authentication failed: tampered data or wrong secret.");
                }
            }

            var iv = new byte[IvSize];
            Buffer.BlockCopy(cipher, VersionSize, iv, 0, IvSize);

            using (var aes = Aes.Create())
            {
                aes.KeySize = 128;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = encKey;
                aes.IV = iv;
                using (ICryptoTransform decryptor = aes.CreateDecryptor())
                {
                    return decryptor.TransformFinalBlock(cipher, HeaderSize, cipherLength);
                }
            }
        }

        private void GetKeys(out byte[] encKey, out byte[] macKey)
        {
            string secret = m_secretProvider();
            if (string.IsNullOrWhiteSpace(secret))
            {
                throw new ArgumentException("Encryption secret cannot be null or empty.");
            }

            lock (m_keyLock)
            {
                if (m_encKey != null && string.Equals(m_cachedSecret, secret, StringComparison.Ordinal))
                {
                    encKey = m_encKey;
                    macKey = m_macKey;
                    return;
                }

                using (var sha = SHA256.Create())
                {
                    byte[] encFull = sha.ComputeHash(Encoding.UTF8.GetBytes("enc:" + Salt + secret));
                    var derivedEnc = new byte[16];
                    Buffer.BlockCopy(encFull, 0, derivedEnc, 0, 16);

                    byte[] derivedMac = sha.ComputeHash(Encoding.UTF8.GetBytes("mac:" + Salt + secret));

                    m_cachedSecret = secret;
                    m_encKey = derivedEnc;
                    m_macKey = derivedMac;
                }

                encKey = m_encKey;
                macKey = m_macKey;
            }
        }
    }
}
