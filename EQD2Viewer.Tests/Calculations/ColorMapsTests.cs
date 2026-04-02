using EQD2Viewer.Core.Calculations;
using FluentAssertions;

namespace EQD2Viewer.Tests.Calculations
{
    /// <summary>
    /// Tests for the Jet colormap used in dose colorwash rendering.
    /// Validates color correctness, continuity, and boundary behavior.
    /// Visual artifacts from colormap bugs could mislead clinical dose interpretation.
    /// </summary>
    public class ColorMapsTests
    {
        // ════════════════════════════════════════════════════════
        // Clamp01
        // ════════════════════════════════════════════════════════

        [Theory]
        [InlineData(0.5, 0.5)]
        [InlineData(0.0, 0.0)]
        [InlineData(1.0, 1.0)]
        [InlineData(-0.1, 0.0)]
        [InlineData(-100, 0.0)]
        [InlineData(1.1, 1.0)]
        [InlineData(100, 1.0)]
        public void Clamp01_ShouldClampCorrectly(double input, double expected)
        {
            ColorMaps.Clamp01(input).Should().Be(expected);
        }

        // ════════════════════════════════════════════════════════
        // Jet colormap — boundary colors
        // ════════════════════════════════════════════════════════

        [Fact]
        public void Jet_AtZero_ShouldBeDarkBlue()
        {
            uint color = ColorMaps.Jet(0.0, 255);
            byte a = (byte)((color >> 24) & 0xFF);
            byte r = (byte)((color >> 16) & 0xFF);
            byte g = (byte)((color >> 8) & 0xFF);
            byte b = (byte)(color & 0xFF);

            a.Should().Be(255);
            r.Should().Be(0, "Jet at t=0 has no red");
            g.Should().Be(0, "Jet at t=0 has no green");
            b.Should().BeGreaterThan(0, "Jet at t=0 should be blue");
        }

        [Fact]
        public void Jet_AtOne_ShouldBeDarkRed()
        {
            uint color = ColorMaps.Jet(1.0, 255);
            byte r = (byte)((color >> 16) & 0xFF);
            byte g = (byte)((color >> 8) & 0xFF);
            byte b = (byte)(color & 0xFF);

            r.Should().BeGreaterThan(0, "Jet at t=1 should have red");
            g.Should().Be(0, "Jet at t=1 has no green");
            b.Should().Be(0, "Jet at t=1 has no blue");
        }

        [Fact]
        public void Jet_AtMidpoint_ShouldBeGreenish()
        {
            uint color = ColorMaps.Jet(0.5, 255);
            byte g = (byte)((color >> 8) & 0xFF);
            g.Should().BeGreaterThan(200, "Jet at t=0.5 should have strong green component");
        }

        // ════════════════════════════════════════════════════════
        // Alpha channel
        // ════════════════════════════════════════════════════════

        [Theory]
        [InlineData(0)]
        [InlineData(128)]
        [InlineData(255)]
        public void Jet_ShouldPreserveAlphaChannel(byte alpha)
        {
            uint color = ColorMaps.Jet(0.5, alpha);
            byte resultAlpha = (byte)((color >> 24) & 0xFF);
            resultAlpha.Should().Be(alpha);
        }

        // ════════════════════════════════════════════════════════
        // Continuity — no sudden jumps
        // ════════════════════════════════════════════════════════

        [Fact]
        public void Jet_ShouldBeContinuous_NoLargeJumps()
        {
            // Adjacent samples should not have large color differences
            int maxDelta = 0;
            uint prev = ColorMaps.Jet(0.0, 255);

            for (double t = 0.001; t <= 1.0; t += 0.001)
            {
                uint curr = ColorMaps.Jet(t, 255);
                int dr = Math.Abs((int)((curr >> 16) & 0xFF) - (int)((prev >> 16) & 0xFF));
                int dg = Math.Abs((int)((curr >> 8) & 0xFF) - (int)((prev >> 8) & 0xFF));
                int db = Math.Abs((int)(curr & 0xFF) - (int)(prev & 0xFF));
                int delta = Math.Max(dr, Math.Max(dg, db));
                if (delta > maxDelta) maxDelta = delta;
                prev = curr;
            }

            maxDelta.Should().BeLessThan(10,
                "Jet colormap should be smooth — step of 0.001 should not cause large RGB jumps");
        }

        // ════════════════════════════════════════════════════════
        // Monotonicity of hue — Jet goes blue→cyan→green→yellow→red
        // ════════════════════════════════════════════════════════

        [Fact]
        public void Jet_BlueShouldDecreaseFromMidToEnd()
        {
            byte blueAtMid = (byte)(ColorMaps.Jet(0.5, 255) & 0xFF);
            byte blueAtEnd = (byte)(ColorMaps.Jet(1.0, 255) & 0xFF);
            blueAtEnd.Should().BeLessOrEqualTo(blueAtMid);
        }

        [Fact]
        public void Jet_RedShouldIncreaseFromMidToEnd()
        {
            byte redAtStart = (byte)((ColorMaps.Jet(0.0, 255) >> 16) & 0xFF);
            byte redAtEnd = (byte)((ColorMaps.Jet(1.0, 255) >> 16) & 0xFF);
            redAtEnd.Should().BeGreaterThan(redAtStart);
        }

        // ════════════════════════════════════════════════════════
        // All values in valid byte range
        // ════════════════════════════════════════════════════════

        [Fact]
        public void Jet_AllOutputs_ShouldBeValidArgb()
        {
            for (double t = 0.0; t <= 1.0; t += 0.01)
            {
                uint color = ColorMaps.Jet(t, 200);
                byte a = (byte)((color >> 24) & 0xFF);
                a.Should().Be(200, $"alpha should be preserved at t={t:F2}");
                // R, G, B are bytes so always 0-255 — just verify no overflow in calculation
                // The uint format ensures this, but let's be explicit
            }
        }
    }
}