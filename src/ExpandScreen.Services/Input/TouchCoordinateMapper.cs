using System.Drawing;

namespace ExpandScreen.Services.Input
{
    public sealed class TouchCoordinateMapper
    {
        private readonly object _lock = new();

        private int _sourceWidth;
        private int _sourceHeight;
        private Rectangle _targetBounds;
        private int _rotationDegrees;

        public TouchCoordinateMapper()
        {
            _targetBounds = Rectangle.Empty;
        }

        public void UpdateSourceScreen(int width, int height)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

            lock (_lock)
            {
                _sourceWidth = width;
                _sourceHeight = height;
            }
        }

        public void UpdateTargetBounds(Rectangle targetBounds)
        {
            lock (_lock)
            {
                _targetBounds = targetBounds;
            }
        }

        public void UpdateRotationDegrees(int rotationDegrees)
        {
            if (rotationDegrees is not (0 or 90 or 180 or 270))
            {
                throw new ArgumentOutOfRangeException(nameof(rotationDegrees), "Rotation must be 0/90/180/270");
            }

            lock (_lock)
            {
                _rotationDegrees = rotationDegrees;
            }
        }

        public Point Map(float sourceX, float sourceY)
        {
            int sourceWidth;
            int sourceHeight;
            Rectangle targetBounds;
            int rotation;

            lock (_lock)
            {
                sourceWidth = _sourceWidth;
                sourceHeight = _sourceHeight;
                targetBounds = _targetBounds;
                rotation = _rotationDegrees;
            }

            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                throw new InvalidOperationException("Source screen size not configured");
            }

            if (targetBounds.IsEmpty)
            {
                throw new InvalidOperationException("Target bounds not configured");
            }

            float x = sourceX;
            float y = sourceY;

            (x, y) = ApplyRotation(x, y, sourceWidth, sourceHeight, rotation);

            float clampedX = Math.Clamp(x, 0, sourceWidth - 1);
            float clampedY = Math.Clamp(y, 0, sourceHeight - 1);

            float normalizedX = clampedX / Math.Max(1, sourceWidth - 1);
            float normalizedY = clampedY / Math.Max(1, sourceHeight - 1);

            int mappedX = targetBounds.Left + (int)Math.Round(normalizedX * Math.Max(1, targetBounds.Width - 1));
            int mappedY = targetBounds.Top + (int)Math.Round(normalizedY * Math.Max(1, targetBounds.Height - 1));

            return new Point(mappedX, mappedY);
        }

        private static (float X, float Y) ApplyRotation(float x, float y, int width, int height, int rotationDegrees)
        {
            return rotationDegrees switch
            {
                0 => (x, y),
                90 => (height - 1 - y, x),
                180 => (width - 1 - x, height - 1 - y),
                270 => (y, width - 1 - x),
                _ => (x, y)
            };
        }
    }
}

