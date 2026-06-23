using clicky_windows;
using Xunit;

namespace clicky_windows_tests
{
    /// <summary>
    /// Tests for the overlay's device-pixel to DIP conversion. This is the SPEC-07
    /// regression guard: the old code multiplied the screen origin by Scaling and
    /// then divided by Scaling, which collapsed to the identity at 100% scale
    /// (single-monitor setups worked) but drifted on multi-DPI setups. The fix
    /// subtracts the origin in device pixels and divides by Scaling exactly once.
    /// </summary>
    public class CoordinateMathTests
    {
        [Fact]
        public void SingleMonitor_100PercentScale_MatchesOldBehavior()
        {
            // Primary monitor at (0,0), 100% scaling. A point at device px (500, 300)
            // must map to DIPs (500, 300) — the no-regression case that used to work.
            double resultX = CoordinateMath.ToLocalDip(globalDevicePixel: 500, screenOriginDevicePixel: 0, scaling: 1.0);
            double resultY = CoordinateMath.ToLocalDip(globalDevicePixel: 300, screenOriginDevicePixel: 0, scaling: 1.0);

            Assert.Equal(500, resultX, precision: 2);
            Assert.Equal(300, resultY, precision: 2);
        }

        [Fact]
        public void SecondaryMonitor_100PercentScale_AppliesOriginOffset()
        {
            // Two monitors side by side, both 100%. Secondary at device origin
            // (1920, 0). A point at global device px (2420, 400) is 500 device px
            // to the right of the secondary's left edge -> 500 DIPs locally.
            double result = CoordinateMath.ToLocalDip(globalDevicePixel: 2420, screenOriginDevicePixel: 1920, scaling: 1.0);

            Assert.Equal(500, result, precision: 2);
        }

        [Fact]
        public void PrimaryMonitor_150PercentScale_ConvertsDevicePxToDips()
        {
            // 150% scaling. A point at device px (600, 450) on a primary monitor at
            // (0,0) is 600/1.5 = 400 DIPs. The old code computed (600 - 0*1.5)/1.5
            // = 400 too, so this alone wouldn't catch the bug — but combined with a
            // non-zero origin it would have. See the next test.
            double resultX = CoordinateMath.ToLocalDip(globalDevicePixel: 600, screenOriginDevicePixel: 0, scaling: 1.5);

            Assert.Equal(400, resultX, precision: 2);
        }

        [Fact]
        public void SecondaryMonitor_150PercentScale_OriginIsNotDoubleScaled()
        {
            // THIS is the case the old code got wrong. Secondary monitor at device
            // origin (2880, 0) (e.g. a 1920-wide primary at 150% => 2880 device px),
            // 150% scaling. A point at global device px (3380, 0) is 500 device px
            // right of the secondary origin -> 500/1.5 = 333.33 DIPs.
            //
            // Old buggy code: (3380 - 2880*1.5) / 1.5 = (3380 - 4320)/1.5 = -626.67  (WRONG, negative)
            // Fixed code:     (3380 - 2880) / 1.5     = 500/1.5 = 333.33            (correct)
            double result = CoordinateMath.ToLocalDip(globalDevicePixel: 3380, screenOriginDevicePixel: 2880, scaling: 1.5);

            Assert.Equal(333.33, result, precision: 1);
            Assert.True(result > 0, "Point to the right of the origin must be positive, not negative (old bug).");
        }

        [Fact]
        public void MouseFollow_AtExactOrigin_ReturnsZero()
        {
            // Cursor at the monitor's top-left corner -> local (0,0).
            double result = CoordinateMath.ToLocalDip(globalDevicePixel: 1920, screenOriginDevicePixel: 1920, scaling: 1.0);

            Assert.Equal(0, result, precision: 2);
        }

        [Fact]
        public void HighDpi_250PercentScale_HandlesFractionalDips()
        {
            // 250% scaling (common on high-DPI laptops). Point at device px (1000)
            // on primary origin (0) -> 1000/2.5 = 400 DIPs.
            double result = CoordinateMath.ToLocalDip(globalDevicePixel: 1000, screenOriginDevicePixel: 0, scaling: 2.5);

            Assert.Equal(400, result, precision: 2);
        }
    }
}
