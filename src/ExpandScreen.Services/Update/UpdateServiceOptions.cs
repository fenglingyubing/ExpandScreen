namespace ExpandScreen.Services.Update
{
    public sealed record UpdateServiceOptions(
        Uri? ManifestUri,
        Version CurrentVersion,
        string? TrustedManifestPublicKeyPem = null,
        bool RequireManifestSignature = false
    );
}

