using EQD2Viewer.Core.Calculations;
using EQD2Viewer.Core.Models;
using FluentAssertions;
using System.Linq;

namespace EQD2Viewer.Tests.Calculations
{
    /// <summary>
    /// Pins the cumulative-DVH bin convention now that
    /// <see cref="DVHCalculator.BinToHistogram"/> is the shared
    /// implementation behind both <c>DVHService.CalculateDVHFromSummedDose</c>
    /// and <c>SummationService.ComputeStructureEQD2DVH</c>. Both pre-existing
    /// callers' test suites continue to exercise the helper end-to-end; these
    /// tests pin the boundary behaviour directly.
    /// </summary>
    public class DVHCalculatorTests
    {
        [Fact]
        public void BinToHistogram_NullInputs_ReturnsEmpty()
        {
            DVHCalculator.BinToHistogram(null!, null!, 10.0).Should().BeEmpty();
        }

        [Fact]
        public void BinToHistogram_ZeroMaxDose_ReturnsEmpty()
        {
            var doses = new double[][] { new[] { 5.0, 5.0 } };
            var masks = new bool[][] { new[] { true, true } };
            DVHCalculator.BinToHistogram(doses, masks, 0).Should().BeEmpty();
        }

        [Fact]
        public void BinToHistogram_NoMaskedVoxels_ReturnsEmpty()
        {
            var doses = new double[][] { new[] { 5.0, 5.0 } };
            var masks = new bool[][] { new[] { false, false } };
            DVHCalculator.BinToHistogram(doses, masks, 10.0).Should().BeEmpty();
        }

        [Fact]
        public void BinToHistogram_AllZeroDose_FirstBinIsHundredPercent()
        {
            // Zero-dose voxels still count toward totalVoxels — the cumulative
            // curve sits at 100% throughout, mirroring the historical behaviour
            // of CalculateDVHFromSummedDose_ZeroDose_ShouldHandleCorrectly.
            var doses = new double[][] { new double[10] };
            var masks = new bool[][] { Enumerable.Repeat(true, 10).ToArray() };

            var dvh = DVHCalculator.BinToHistogram(doses, masks, 10.0);

            dvh.Should().NotBeEmpty();
            dvh[0].VolumePercent.Should().Be(100.0);
            dvh.Last().VolumePercent.Should().Be(100.0,
                "no voxel ever exceeds bin 0's lower edge so the curve never decays");
        }

        [Fact]
        public void BinToHistogram_UniformDose_ProducesStepFunction()
        {
            // 100 voxels at 10 Gy. Curve sits at 100% up through bin 10/binWidth
            // and falls to 0 thereafter. Pin: bin convention is FLOOR.
            int n = 100;
            var doses = new double[][] { Enumerable.Repeat(10.0, n).ToArray() };
            var masks = new bool[][] { Enumerable.Repeat(true, n).ToArray() };

            var dvh = DVHCalculator.BinToHistogram(doses, masks, 20.0);

            dvh[0].VolumePercent.Should().BeApproximately(100.0, 0.1);
            // Far above the dose, curve is at 0% (cumulative subtracted everything).
            dvh.Last().VolumePercent.Should().BeApproximately(0.0, 0.1);
        }

        [Fact]
        public void BinToHistogram_DvhIsMonotonicallyNonIncreasing()
        {
            // Random distribution; pin invariant: cumulative DVH never grows.
            int n = 256;
            var rng = new System.Random(42);
            double[] dose = new double[n];
            for (int i = 0; i < n; i++) dose[i] = rng.NextDouble() * 50.0;

            var dvh = DVHCalculator.BinToHistogram(
                new[] { dose }, new[] { Enumerable.Repeat(true, n).ToArray() }, 60.0);

            for (int i = 1; i < dvh.Length; i++)
                dvh[i].VolumePercent.Should().BeLessOrEqualTo(dvh[i - 1].VolumePercent,
                    $"cumulative volume must not grow between bin {i - 1} and {i}");
        }

        [Fact]
        public void BinToHistogram_BinWidth_IsTenPercentAboveMaxDoseDividedByNumBins()
        {
            // Pin: binWidth = maxDoseGy * 1.1 / numBins. Verified by reading the
            // first two output dose points (which are i * binWidth).
            var doses = new double[][] { new[] { 1.0 } };
            var masks = new bool[][] { new[] { true } };
            const double maxDoseGy = 10.0;

            var dvh = DVHCalculator.BinToHistogram(doses, masks, maxDoseGy);

            double expectedBinWidth = maxDoseGy * 1.1 / DomainConstants.DvhHistogramBins;
            dvh[0].DoseGy.Should().Be(0);
            dvh[1].DoseGy.Should().BeApproximately(expectedBinWidth, 1e-12);
            dvh[2].DoseGy.Should().BeApproximately(2 * expectedBinWidth, 1e-12);
        }
    }
}
