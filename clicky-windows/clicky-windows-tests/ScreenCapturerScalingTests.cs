using clicky_windows;
using Xunit;

namespace clicky_windows_tests
{
    /// <summary>
    /// Tests for ScreenCapturer.ComputeScaledDimensions — the SPEC-03 AC1 logic that
    /// downscales captures so the longest side never exceeds 1280px, preserving
    /// aspect ratio. This is the pure math behind the payload-size fix; the actual
    /// BitBlt+resize happens in ScreenCapturer.CaptureScreenRect (untestable without
    /// a real display), but the dimension computation is fully deterministic.
    /// </summary>
    public class ScreenCapturerScalingTests
    {
        [Fact]
        public void FourK_Landscape_Downscale_LongestSideIsExactly1280()
        {
            // 3840x2160 (4K landscape) -> longest side 3840 capped to 1280.
            // Scale = 1280/3840 = 0.3333 -> 1280x720, aspect preserved.
            var (width, height) = ScreenCapturer.ComputeScaledDimensions(3840, 2160, ScreenCapturer.MaxCaptureDimension);

            Assert.Equal(1280, width);
            Assert.Equal(720, height);
        }

        [Fact]
        public void FourK_Portrait_Downscale_LongestSideIsExactly1280()
        {
            // 2160x3840 (portrait) -> longest side is height (3840) capped to 1280.
            // Scale = 1280/3840 = 0.3333 -> 720x1280.
            var (width, height) = ScreenCapturer.ComputeScaledDimensions(2160, 3840, ScreenCapturer.MaxCaptureDimension);

            Assert.Equal(720, width);
            Assert.Equal(1280, height);
        }

        [Theory]
        [InlineData(1280, 720)]   // exactly at cap, landscape
        [InlineData(720, 1280)]   // exactly at cap, portrait
        [InlineData(1024, 768)]   // under cap
        [InlineData(800, 600)]    // well under cap
        public void AtOrUnderCap_ReturnsOriginalDimensions(int width, int height)
        {
            var (scaledWidth, scaledHeight) = ScreenCapturer.ComputeScaledDimensions(width, height, ScreenCapturer.MaxCaptureDimension);

            Assert.Equal(width, scaledWidth);
            Assert.Equal(height, scaledHeight);
        }

        [Fact]
        public void AboveCap_ResultNeverExceedsCap()
        {
            // Several realistic high-res sizes; the longest side of the result must
            // always be <= MaxCaptureDimension, regardless of input aspect.
            var sizes = new (int W, int H)[]
            {
                (3840, 2160),  // 4K
                (5120, 2880),  // 5K
                (2560, 1440),  // 1440p
                (3440, 1440),  // ultrawide
                (1366, 768),   // under cap, must pass through
            };

            foreach (var (originalWidth, originalHeight) in sizes)
            {
                var (scaledWidth, scaledHeight) = ScreenCapturer.ComputeScaledDimensions(originalWidth, originalHeight, ScreenCapturer.MaxCaptureDimension);
                int longestSide = System.Math.Max(scaledWidth, scaledHeight);

                Assert.True(longestSide <= ScreenCapturer.MaxCaptureDimension,
                    $"Size {originalWidth}x{originalHeight} scaled to {scaledWidth}x{scaledHeight} (longest {longestSide}) exceeds cap {ScreenCapturer.MaxCaptureDimension}.");
            }
        }

        [Fact]
        public void Downscale_PreservesAspectRatioWithinTolerance()
        {
            // 3840x2160 has aspect 16:9 = 1.7778. The scaled result must match.
            var (width, height) = ScreenCapturer.ComputeScaledDimensions(3840, 2160, ScreenCapturer.MaxCaptureDimension);

            double originalAspect = 3840.0 / 2160.0;
            double scaledAspect = (double)width / height;

            Assert.True(System.Math.Abs(originalAspect - scaledAspect) < 0.01,
                $"Aspect changed: {originalAspect} -> {scaledAspect}.");
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(-1, 100)]
        [InlineData(100, -1)]
        public void InvalidDimensions_ReturnedAsIs(int width, int height)
        {
            // Degenerate inputs must short-circuit (return as-is) rather than crash
            // or produce negative scaled sizes — the capture path depends on this.
            var (scaledWidth, scaledHeight) = ScreenCapturer.ComputeScaledDimensions(width, height, ScreenCapturer.MaxCaptureDimension);

            Assert.Equal(width, scaledWidth);
            Assert.Equal(height, scaledHeight);
        }

        [Fact]
        public void CustomMaxDimension_Respected()
        {
            // The helper is parametric on max dimension (not hardcoded to 1280) so
            // future changes to the cap don't silently break callers. Verify with 800.
            var (width, height) = ScreenCapturer.ComputeScaledDimensions(3840, 2160, maxDimension: 800);

            Assert.Equal(800, width);
            Assert.Equal(450, height);
        }
    }
}
