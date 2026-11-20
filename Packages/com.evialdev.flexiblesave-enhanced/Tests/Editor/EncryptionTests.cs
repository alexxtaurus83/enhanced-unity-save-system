using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FlexibleSave.Enhanced.Security;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FlexibleSave.Enhanced.Tests.Editor
{
    public class EncryptionTests
    {
        private string m_tempDir;
        private GameObject m_sysObj;

        private sealed class XorEncryptor : ISaveEncryptor
        {
            public string Id => "custom-xor-v1";
            public byte[] Encrypt(byte[] plain)
            {
                var outBytes = new byte[plain.Length];
                for (int i = 0; i < plain.Length; i++) outBytes[i] = (byte)(plain[i] ^ 0x5A);
                return outBytes;
            }
            public byte[] Decrypt(byte[] cipher) => Encrypt(cipher);
        }

        [SetUp]
        public void SetUp()
        {
            SaveableEntityRegistry.Clear();
            m_tempDir = Path.Combine(Path.GetTempPath(), "FlexibleSaveEncTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (m_sysObj != null) UnityEngine.Object.DestroyImmediate(m_sysObj);
            SaveableEntityRegistry.Clear();
            if (Directory.Exists(m_tempDir)) Directory.Delete(m_tempDir, recursive: true);
        }

        [Test]
        public void LightSaveEncryptor_RoundTrip_BinaryPayload()
        {
            var encryptor = new LightSaveEncryptor(() => "test-app-secret");
            byte[] plain = Encoding.UTF8.GetBytes("{\"hello\":\"world\",\"n\":123}");

            byte[] cipher1 = encryptor.Encrypt(plain);
            byte[] cipher2 = encryptor.Encrypt(plain);

            // Random IV ensures two encryptions of identical plaintext differ.
            CollectionAssert.AreNotEqual(cipher1, cipher2);
            CollectionAssert.AreEqual(plain, encryptor.Decrypt(cipher1));
            CollectionAssert.AreEqual(plain, encryptor.Decrypt(cipher2));
        }

        [Test]
        public void LightSaveEncryptor_Tamper_ThrowsCryptographicException()
        {
            var encryptor = new LightSaveEncryptor(() => "test-app-secret");
            byte[] plain = Encoding.UTF8.GetBytes("sensitive-state");
            byte[] cipher = encryptor.Encrypt(plain);

            cipher[cipher.Length / 2] ^= 0x01;
            Assert.Throws<CryptographicException>(() => encryptor.Decrypt(cipher));
        }

        [Test]
        public void LightSaveEncryptor_WrongSecret_ThrowsCryptographicException()
        {
            var encA = new LightSaveEncryptor(() => "secret-a");
            var encB = new LightSaveEncryptor(() => "secret-b");

            byte[] cipher = encA.Encrypt(Encoding.UTF8.GetBytes("payload"));
            Assert.Throws<CryptographicException>(() => encB.Decrypt(cipher));
        }

        [Test]
        public void SaveSystem_WritesBinaryHeader_WithLightAndCustomEncryptor()
        {
            m_sysObj = new GameObject("SaveSys");
            var sys = m_sysObj.AddComponent<SaveSystem>();
            sys.BasePath = m_tempDir;
            sys.Folder = string.Empty;
            sys.FileName = "enc.bin";
            sys.SecretProvider = () => "unit-test-secret";
            sys.Encryption = EncryptionMode.Light;

            Assert.AreEqual(LoadStatus.NoSaveFile, sys.Load().Status);
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);

            byte[] raw = File.ReadAllBytes(sys.FullPath);
            Assert.IsTrue(SaveSystem.HasEncryptedHeader(raw));
            int idLen = raw[3];
            string headerId = Encoding.UTF8.GetString(raw, 4, idLen);
            Assert.AreEqual(LightSaveEncryptor.DefaultId, headerId);

            // Reload with Light encryptor
            Assert.AreEqual(LoadStatus.Ok, sys.Load().Status);

            // Switch to custom encryptor
            sys.Encryptor = new XorEncryptor();
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);
            byte[] customRaw = File.ReadAllBytes(sys.FullPath);
            Assert.AreEqual("custom-xor-v1", Encoding.UTF8.GetString(customRaw, 4, customRaw[3]));
            Assert.AreEqual(LoadStatus.Ok, sys.Load().Status);

            // Tampering fails load
            customRaw[customRaw.Length - 1] ^= 0xFF;
            // With XorEncryptor, corrupting JSON causes JSON parse failure -> Corrupt
            File.WriteAllBytes(sys.FullPath, customRaw);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("is corrupt"));
            Assert.AreEqual(LoadStatus.Corrupt, sys.Load().Status);
        }

        [Test]
        public void SaveSystem_EncryptedMode_RejectsPlaintextBypass()
        {
            m_sysObj = new GameObject("SaveSys");
            var sys = m_sysObj.AddComponent<SaveSystem>();
            sys.BasePath = m_tempDir;
            sys.Folder = string.Empty;
            sys.FileName = "enc_bypass.bin";
            sys.SecretProvider = () => "unit-test-secret";
            sys.Encryption = EncryptionMode.Light;

            Assert.AreEqual(LoadStatus.NoSaveFile, sys.Load().Status);
            Assert.AreEqual(SaveStatus.Ok, sys.Save().Status);

            // Replace encrypted file with valid unencrypted FlexibleSave JSON (tamper bypass attempt).
            const string spoofedJson = "{\"format\":\"FlexibleSave\",\"formatVersion\":1,\"savedAtUtc\":1,\"entities\":{},\"deleted\":[]}";
            File.WriteAllText(sys.FullPath, spoofedJson, Encoding.UTF8);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("unencrypted plaintext"));
            LoadResult res = sys.Load();
            Assert.AreEqual(LoadStatus.Corrupt, res.Status);
            Assert.IsTrue(sys.IsCorrupt);
        }
    }
}
