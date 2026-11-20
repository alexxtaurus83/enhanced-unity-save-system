namespace FlexibleSave.Enhanced.Security
{
    /// <summary>
    /// Pluggable binary payload encryptor for save files.
    /// Implementations are intended for obfuscation/tamper detection or custom encryption.
    /// </summary>
    public interface ISaveEncryptor
    {
        /// <summary>
        /// Identifier written into the file header (1..255 UTF-8 bytes), e.g. "fs-light-v1".
        /// </summary>
        string Id { get; }

        byte[] Encrypt(byte[] plain);

        /// <summary>
        /// Decrypts <paramref name="cipher"/> and throws on tampering or wrong secret.
        /// </summary>
        byte[] Decrypt(byte[] cipher);
    }
}
