using System.Security.Cryptography;

namespace UpdateSigner;

/// <summary>
/// Build-time helper for the in-app update check. <c>keygen</c> makes the signing key pair once and
/// <c>sign</c> writes a detached signature next to a release file. Only the BCL is used. The private
/// key is read here and nowhere else, and it is never printed.
/// </summary>
internal static class Program
{
    private const string KeyEnvironmentVariable = "AURA_SIGNING_KEY";

    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            Console.Error.WriteLine($"UpdateSigner: {ex.Message}");
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length == 0)
            return Usage();

        var keyPath = TakeOption(ref args, "--key") ?? Environment.GetEnvironmentVariable(KeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            Console.Error.WriteLine($"UpdateSigner: no key path (pass --key <path> or set {KeyEnvironmentVariable}).");
            return 2;
        }

        switch (args[0])
        {
            case "keygen":
                var csPath = TakeOption(ref args, "--out-cs");
                return KeyGen(keyPath, csPath);
            case "sign" when args.Length == 2:
                return Sign(keyPath, args[1]);
            default:
                return Usage();
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: UpdateSigner keygen [--key <pem>] [--out-cs <UpdatePublicKey.cs>]");
        Console.Error.WriteLine("       UpdateSigner sign [--key <pem>] <file>");
        Console.Error.WriteLine($"The key path may also come from {KeyEnvironmentVariable}.");
        return 2;
    }

    /// <summary>Removes <paramref name="name"/> and its value from <paramref name="args"/> and returns
    /// the value, or null when the option is absent.</summary>
    private static string? TakeOption(ref string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0 || index + 1 >= args.Length)
            return null;

        var value = args[index + 1];
        args = [.. args.Where((_, i) => i != index && i != index + 1)];
        return value;
    }

    private static int KeyGen(string keyPath, string? publicKeyCsPath)
    {
        if (File.Exists(keyPath))
        {
            Console.Error.WriteLine("UpdateSigner: a key file already exists at that path; it is never overwritten.");
            return 1;
        }

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var directory = Path.GetDirectoryName(Path.GetFullPath(keyPath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // CreateNew: fails if another process made the file in the meantime, instead of replacing it.
        using (var stream = new FileStream(keyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream))
            writer.Write(key.ExportPkcs8PrivateKeyPem());

        var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        if (publicKeyCsPath is not null)
            File.WriteAllText(publicKeyCsPath, PublicKeySource(publicKey));

        Console.WriteLine(publicKey);
        return 0;
    }

    private static string PublicKeySource(string publicKey) =>
        "namespace AuraToggle;\r\n\r\n"
        + "/// <summary>The public half of the update signing key (SubjectPublicKeyInfo, Base64). An update is\r\n"
        + "/// installed only when its detached signature verifies against this key.</summary>\r\n"
        + "internal static class UpdatePublicKey\r\n{\r\n"
        + $"    public const string Value = \"{publicKey}\";\r\n}}\r\n";

    private static int Sign(string keyPath, string file)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(File.ReadAllText(keyPath));

        var signature = key.SignData(File.ReadAllBytes(file), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        File.WriteAllText(file + ".sig", Convert.ToBase64String(signature));
        Console.WriteLine($"Signed {Path.GetFileName(file)}");
        return 0;
    }
}
