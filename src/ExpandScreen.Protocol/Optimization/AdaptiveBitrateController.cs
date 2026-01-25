using ExpandScreen.Protocol.Messages;

namespace ExpandScreen.Protocol.Optimization
{
    public sealed class AdaptiveBitrateConfig
    {
        public int MinBitrateBps { get; set; } = 500_000;
        public int MaxBitrateBps { get; set; } = 12_000_000;
        public int IncreaseStepBps { get; set; } = 250_000;
        public double DecreaseFactor { get; set; } = 0.75;
        public double SmoothingAlpha { get; set; } = 0.2;
        public double BandwidthHeadroom { get; set; } = 0.85;

        public double LossDecreaseThreshold { get; set; } = 0.01; // 1%
        public double RttDecreaseThresholdMs { get; set; } = 200;
    }

    public sealed class AdaptiveBitrateDecision
    {
        public int TargetBitrateBps { get; set; }
        public bool Changed { get; set; }
        public string Reason { get; set; } = string.Empty;
        public double LossRatio { get; set; }
        public double EstimatedBandwidthBps { get; set; }
        public double AverageRttMs { get; set; }
    }

    /// <summary>
    /// 自适应码率控制器（BOTH-302）：根据 RTT/丢消息/接收速率执行 AIMD + 平滑过渡。
    /// </summary>
    public sealed class AdaptiveBitrateController
    {
        private readonly AdaptiveBitrateConfig _config;
        private double _targetBitrateBps;

        public int TargetBitrateBps => (int)Math.Round(_targetBitrateBps);

        public AdaptiveBitrateController(AdaptiveBitrateConfig? config = null, int? initialBitrateBps = null)
        {
            _config = config ?? new AdaptiveBitrateConfig();
            _targetBitrateBps = initialBitrateBps ?? Math.Clamp(5_000_000, _config.MinBitrateBps, _config.MaxBitrateBps);
        }

        public AdaptiveBitrateDecision Update(ProtocolFeedbackMessage feedback)
        {
            double estimatedBandwidthBps = feedback.ReceiveRateBps > 0 ? feedback.ReceiveRateBps : 0;

            double lossRatio = 0;
            long sentApprox = feedback.TotalMessagesDelta + feedback.DroppedMessagesDelta;
            if (sentApprox > 0 && feedback.DroppedMessagesDelta > 0)
            {
                lossRatio = (double)feedback.DroppedMessagesDelta / sentApprox;
            }

            bool shouldDecrease =
                lossRatio >= _config.LossDecreaseThreshold ||
                (feedback.AverageRttMs > 0 && feedback.AverageRttMs >= _config.RttDecreaseThresholdMs);

            double rawTarget = _targetBitrateBps;
            string reason;

            if (shouldDecrease)
            {
                rawTarget = Math.Max(_config.MinBitrateBps, _targetBitrateBps * _config.DecreaseFactor);
                reason = lossRatio >= _config.LossDecreaseThreshold
                    ? $"loss {lossRatio:P1} (delta {feedback.DroppedMessagesDelta})"
                    : $"rtt {feedback.AverageRttMs:F0}ms";
            }
            else
            {
                rawTarget = Math.Min(_config.MaxBitrateBps, _targetBitrateBps + _config.IncreaseStepBps);
                reason = "stable";
            }

            if (estimatedBandwidthBps > 0)
            {
                rawTarget = Math.Min(rawTarget, estimatedBandwidthBps * _config.BandwidthHeadroom);
            }

            rawTarget = Math.Clamp(rawTarget, _config.MinBitrateBps, _config.MaxBitrateBps);

            double alpha = Math.Clamp(_config.SmoothingAlpha, 0.0, 1.0);
            double smoothed = _targetBitrateBps * (1 - alpha) + rawTarget * alpha;
            smoothed = Math.Clamp(smoothed, _config.MinBitrateBps, _config.MaxBitrateBps);

            int previous = (int)Math.Round(_targetBitrateBps);
            int next = (int)Math.Round(smoothed);
            bool changed = Math.Abs(next - previous) >= 50_000;

            if (changed)
            {
                _targetBitrateBps = smoothed;
            }

            return new AdaptiveBitrateDecision
            {
                TargetBitrateBps = (int)Math.Round(_targetBitrateBps),
                Changed = changed,
                Reason = reason,
                LossRatio = lossRatio,
                EstimatedBandwidthBps = estimatedBandwidthBps,
                AverageRttMs = feedback.AverageRttMs
            };
        }
    }
}

