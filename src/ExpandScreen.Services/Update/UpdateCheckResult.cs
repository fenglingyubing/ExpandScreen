namespace ExpandScreen.Services.Update
{
    public sealed record UpdateCheckResult(
        bool IsEnabled,
        bool IsUpdateAvailable,
        UpdateInfo? Update
    )
    {
        public static UpdateCheckResult Disabled() => new(false, false, null);
        public static UpdateCheckResult NoUpdate() => new(true, false, null);
        public static UpdateCheckResult HasUpdate(UpdateInfo update) => new(true, true, update);
    }
}

