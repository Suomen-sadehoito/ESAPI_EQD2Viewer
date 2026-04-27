using EQD2Viewer.Core.Calculations;
using EQD2Viewer.Core.Data;
using EQD2Viewer.Tests.Common;
using FluentAssertions;

namespace EQD2Viewer.Tests.Calculations
{
    /// <summary>
    /// Edge-case tests for HotspotFinder. Critical because the UI uses IsValid directly to
    /// decide whether to show the Dmax label and the Jump-to-hotspot button. A bogus result
    /// (or an exception) here would break the main-window summation panel.
    /// </summary>
    public class HotspotFinderTests
    {
        private static DoseVolumeData MakeDose(int xs, int ys, int zs, double value)
            => TestVolumeFactory.MakeDoseVolume(xs, ys, zs, value);

        [Fact]
        public void FindInDoseVolume_Null_ReturnsInvalid()
        {
            HotspotFinder.FindInDoseVolume(null!).IsValid.Should().BeFalse();
        }

        [Fact]
        public void FindInDoseVolume_AllZeroVolume_ReturnsZeroMax()
        {
            // UI relies on this returning IsValid=true but MaxGy=0, so the Dmax label shows
            // "Dmax: 0.00 Gy" rather than disappearing entirely for a truly cold plan.
            var dose = MakeDose(4, 4, 2, value: 0);
            var hs = HotspotFinder.FindInDoseVolume(dose);
            hs.IsValid.Should().BeTrue();
            hs.MaxGy.Should().Be(0);
        }

        [Fact]
        public void FindInDoseVolume_SingleHotVoxel_LocatesItExactly()
        {
            var dose = MakeDose(5, 5, 3, value: 0);
            dose.Voxels[1][3, 2] = 1000; // one hot voxel at (3, 2, slice 1)
            var hs = HotspotFinder.FindInDoseVolume(dose);
            hs.IsValid.Should().BeTrue();
            hs.MaxGy.Should().Be(1000);
            hs.SliceZ.Should().Be(1);
            hs.PixelX.Should().Be(3);
            hs.PixelY.Should().Be(2);
        }

        [Fact]
        public void FindInDoseVolume_AppliesScalingBeforeComparison()
        {
            // Scaling must be applied before max is taken, otherwise a large raw value with
            // negative unitToGy would seem "higher" than a small raw with positive scaling.
            var dose = MakeDose(2, 2, 1, value: 0);
            dose.Voxels[0][0, 0] = 100;  // 100 × 0.5 = 50 Gy
            dose.Voxels[0][1, 1] = 200;  // 200 × 0.5 = 100 Gy (should win)
            dose.Scaling.RawScale = 0.5;
            dose.Scaling.UnitToGy = 1.0;
            var hs = HotspotFinder.FindInDoseVolume(dose);
            hs.MaxGy.Should().BeApproximately(100, 1e-9);
            hs.PixelX.Should().Be(1);
            hs.PixelY.Should().Be(1);
        }

        [Fact]
        public void FindInDoseVolume_NullSliceInMiddle_StaysRobust()
        {
            // Defensive: sparse-dose volumes sometimes have null slices.
            var dose = MakeDose(3, 3, 3, value: 5);
            dose.Voxels[1] = null!;
            var act = () => HotspotFinder.FindInDoseVolume(dose);
            act.Should().NotThrow();
            HotspotFinder.FindInDoseVolume(dose).MaxGy.Should().Be(5);
        }
    }
}
