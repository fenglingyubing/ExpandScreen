namespace ExpandScreen.Utils
{
    /// <summary>
    /// 日志辅助类
    /// </summary>
    public static class LogHelper
    {
        public static void Info(string message)
        {
            Serilog.Log.Information(message);
        }

        public static void Warning(string message)
        {
            Serilog.Log.Warning(message);
        }

        public static void Error(string message, Exception? ex = null)
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

        public static void Debug(string message)
        {
            Serilog.Log.Debug(message);
        }

        // 保留旧方法以保持兼容性
        public static void LogInfo(string message) => Info(message);
        public static void LogError(string message, Exception? ex = null) => Error(message, ex);
        public static void LogDebug(string message) => Debug(message);
    }
}
