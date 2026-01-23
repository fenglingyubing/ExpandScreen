namespace ExpandScreen.Services.Update
{
    public sealed record UpdateInfo(
        Version LatestVersion,
        Uri DownloadUri,
        string Sha256HexLower,
        string? ReleaseNotes
    );
}

