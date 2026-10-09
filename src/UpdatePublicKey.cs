namespace AuraToggle;

/// <summary>The public half of the update signing key (SubjectPublicKeyInfo, Base64). An update is
/// installed only when its detached signature verifies against this key.</summary>
internal static class UpdatePublicKey
{
    public const string Value = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEnIvNA/baCtXy5KUSI7n+7gfjfuF9XNV+ObtwvGrc+7grGf7g0sx5xSomvgt4ILuPGG+w/ZhrPdAnlecmRbyPiQ==";
}
