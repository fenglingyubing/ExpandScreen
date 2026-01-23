using ExpandScreen.Services.Configuration;

namespace ExpandScreen.IntegrationTests
{
    public class ConfigServiceTests
    {
        [Fact]
        public async Task LoadAsync_CreatesDefaultConfig_WhenMissing()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ExpandScreen.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "config.json");

            try
            {
                var service = new ConfigService(path);
                AppConfig config = await service.LoadAsync();

                Assert.True(File.Exists(path));
                Assert.Equal(ThemeMode.Dark, config.General.Theme);
                Assert.True(config.Network.TcpPort >= 1024);
            }
            finally
            {
                TryDeleteDirectory(dir);
            }
        }

        [Fact]
        public async Task SaveAsync_NormalizesInvalidValues()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ExpandScreen.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "config.json");

            try
            {
                var service = new ConfigService(path);

                var config = AppConfig.CreateDefault();
                config.Network.TcpPort = 80;
                config.Video.FrameRate = 999;
                config.Performance.EncodingThreadCount = 999;

                ConfigSaveResult result = await service.SaveAsync(config);

                Assert.NotEmpty(result.Warnings);
                Assert.NotEqual(80, result.Config.Network.TcpPort);
                Assert.InRange(result.Config.Video.FrameRate, 1, 240);
                Assert.InRange(result.Config.Performance.EncodingThreadCount, 0, 64);
            }
            finally
            {
                TryDeleteDirectory(dir);
            }
        }

        [Fact]
        public async Task LoadAsync_ResetsToDefaultAndBacksUp_WhenJsonInvalid()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ExpandScreen.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "config.json");

            try
            {
                await File.WriteAllTextAsync(path, "{ this is not valid json");

                var service = new ConfigService(path);
                AppConfig config = await service.LoadAsync();

                Assert.Equal(ThemeMode.Dark, config.General.Theme);
                Assert.True(File.Exists(path));

                string[] backups = Directory.GetFiles(dir, "config.json.bad-*");
                Assert.True(backups.Length >= 1);
            }
            finally
            {
                TryDeleteDirectory(dir);
            }
        }

        private static void TryDeleteDirectory(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}

