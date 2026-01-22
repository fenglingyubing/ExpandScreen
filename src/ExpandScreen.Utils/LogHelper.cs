namespace ExpandScreen.Utils
{
    /// <summary>
    /// 日志辅助类
    /// </summary>
    public static class LogHelper
    {
        public static void LogInfo(string message)
        {
            Serilog.Log.Information(message);
        }

        public static void LogError(string message, Exception? ex = null)
        {
            if (ex != null)
            {
                Serilog.Log.Error(ex, message);
            }
            else
            {
                Serilog.Log.Error(message);
            }
        }

        public static void LogDebug(string message)
        {
            Serilog.Log.Debug(message);
        }
    }
}
