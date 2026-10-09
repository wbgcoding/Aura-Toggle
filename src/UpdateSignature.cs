using System;
using System.Security.Cryptography;

namespace AuraToggle;

/// <summary>
/// The one gate between a downloaded update and running it: a detached ECDSA P-256 signature (IEEE
/// P1363, SHA-256) over the file bytes, checked against the public key built into the program.
/// Anything that is not a clean "yes" - an empty key, a malformed key or signature, a changed file, a
/// different key - is a "no".
/// </summary>
internal static class UpdateSignature
{
    public static bool Verify(byte[] data, string signatureBase64, string publicKeyBase64)
    {
        if (string.IsNullOrWhiteSpace(publicKeyBase64) || string.IsNullOrWhiteSpace(signatureBase64))
        {
            return false;
        }

        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64.Trim()), out _);
            return key.VerifyData(
                data, Convert.FromBase64String(signatureBase64.Trim()), HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            return false;
        }
    }
}
