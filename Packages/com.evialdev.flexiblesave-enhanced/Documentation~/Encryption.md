# Encryption

`SaveSystem` supports pluggable binary encryption via `ISaveEncryptor` and ships with a fast built-in `LightSaveEncryptor`.

## Rules

- `LightSaveEncryptor` provides **light obfuscation and tamper detection** against casual file inspection or editing on rooted/jailbroken devices. It is **not** cryptographic security against a determined attacker who can inspect the app binary or memory.
- `LightSaveEncryptor` derives AES-128-CBC and HMAC-SHA256 keys once per secret via SHA-256 (no slow PBKDF2 per save) and writes raw binary bytes (no Base64 bloat).
- Encrypted files begin with the `"FSV"` magic header followed by a 1-byte UTF-8 ID length and the `ISaveEncryptor.Id` string. On load, any ID mismatch fails as `LoadStatus.Corrupt` naming both the file's encryptor ID and the configured encryptor ID.
- By default, `AppSecretProvider` derives its secret from `Application.identifier` (falling back to `Application.productName`). Changing your app identifier will make previously encrypted local saves unreadable.
- Assign `SaveSystem.Instance.Encryptor` (and/or `SecretProvider`) **before** calling `Load()`.

## Example

```csharp
using System;
using FlexibleSave.Enhanced;
using FlexibleSave.Enhanced.Security;

public sealed class CustomXorEncryptor : ISaveEncryptor
{
    public string Id => "my-custom-v1";

    public byte[] Encrypt(byte[] plain)
    {
        var output = new byte[plain.Length];
        for (int i = 0; i < plain.Length; i++)
            output[i] = (byte)(plain[i] ^ 0xA5);
        return output;
    }

    public byte[] Decrypt(byte[] cipher) => Encrypt(cipher);
}

// Usage before Load():
// SaveSystem.Instance.Encryptor = new CustomXorEncryptor();
```
