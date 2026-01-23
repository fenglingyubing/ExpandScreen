namespace ExpandScreen.Services.Update
{
    public sealed record DownloadedUpdate(
        UpdateInfo Info,
        string FilePath
    );
}

