namespace ExpandScreen.Core.Capture
{
    /// <summary>
    /// 屏幕捕获接口
    /// </summary>
    public interface IScreenCapture
    {
        /// <summary>
        /// 开始捕获
        /// </summary>
        void Start();

        /// <summary>
        /// 停止捕获
        /// </summary>
        void Stop();

        /// <summary>
        /// 获取下一帧
        /// </summary>
        byte[]? CaptureFrame();
    }
}
