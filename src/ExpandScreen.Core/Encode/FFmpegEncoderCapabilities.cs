using ExpandScreen.Utils;
using FFmpeg.AutoGen;

namespace ExpandScreen.Core.Encode
{
    public static unsafe class FFmpegEncoderCapabilities
    {
        public static bool IsEncoderAvailable(string encoderName)
        {
            return TryFindEncoderByName(encoderName) != null;
        }

        internal static AVCodec* TryFindEncoderByName(string encoderName)
        {
            if (string.IsNullOrWhiteSpace(encoderName))
            {
                return null;
            }

            try
            {
                return ffmpeg.avcodec_find_encoder_by_name(encoderName);
            }
            catch (DllNotFoundException ex)
            {
                LogHelper.Debug($"FFmpeg native library not found, cannot probe encoder '{encoderName}': {ex.Message}");
                return null;
            }
            catch (BadImageFormatException ex)
            {
                LogHelper.Debug($"FFmpeg native library invalid, cannot probe encoder '{encoderName}': {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                LogHelper.Debug($"Probe encoder '{encoderName}' failed: {ex.Message}");
                return null;
            }
        }
    }
}
