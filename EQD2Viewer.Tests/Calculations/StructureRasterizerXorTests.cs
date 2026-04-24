using EQD2Viewer.Core.Calculations;
using FluentAssertions;
using System.Collections.Generic;
using System.Linq;

namespace EQD2Viewer.Tests.Calculations
{
    /// <summary>
    /// Tests for CombineContourMasks XOR topology. Re-irradiation planning frequently hits
    /// structures with holes (rectum around urethra, PTV around GTV boost volume), and
    /// multi-contour slices with 3+ overlapping masks can appear in complex OARs.
    /// Even-odd XOR is the standard DICOM-RT convention for this.
    /// </summary>
    public class StructureRasterizerXorTests
    {
        private static bool[] Rect(int w, int h, int x0, int y0, int x1, int y1)
        {
            var m = new bool[w * h];
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (x >= 0 && x < w && y >= 0 && y < h) m[y * w + x] = true;
            return m;
        }

        [Fact]
        public void CombineContourMasks_Null_ReturnsEmptyMask()
        {
            var result = StructureRasterizer.CombineContourMasks(null!, 4, 4);
            result.Should().HaveCount(16);
            result.All(b => !b).Should().BeTrue();
        }

        [Fact]
        public void CombineContourMasks_SingleMask_ReturnsItAsIs()
        {
            var inner = Rect(5, 5, 1, 1, 3, 3);
            var result = StructureRasterizer.CombineContourMasks(new List<bool[]> { inner }, 5, 5);
            result.Should().BeEquivalentTo(inner);
        }

        [Fact]
        public void CombineContourMasks_OuterMinusInner_HoleInMiddle()
        {
            // Classic donut: outer rectangle (0..4) XOR inner rectangle (1..3)
            // → border pixels stay true, inner pixels become false (hole).
            var outer = Rect(6, 6, 0, 0, 4, 4);
            var inner = Rect(6, 6, 1, 1, 3, 3);
            var result = StructureRasterizer.CombineContourMasks(new List<bool[]> { outer, inner }, 6, 6);

            // Centre pixel (2, 2) is in both → XOR'd out
            result[2 * 6 + 2].Should().BeFalse("inner overlap must XOR to 0 for a hole");
            // Corner of outer-but-not-inner (0, 0)
            result[0 * 6 + 0].Should().BeTrue("outer-only pixels survive");
            // Outside everything (5, 5)
            result[5 * 6 + 5].Should().BeFalse();
        }

        [Fact]
        public void CombineContourMasks_ThreeOverlappingMasks_CancelsAtTriplePoint()
        {
            // Three rectangles all covering (2, 2). XOR of 3 trues = true (odd count).
            // But two rectangles where only one covers (0, 0) → true (1 odd).
            // Where two cover (1, 1) but not the third → false (2 even).
            var a = Rect(5, 5, 0, 0, 2, 2);
            var b = Rect(5, 5, 1, 1, 2, 2);
            var c = Rect(5, 5, 2, 2, 4, 4);
            var result = StructureRasterizer.CombineContourMasks(
                new List<bool[]> { a, b, c }, 5, 5);

            // (2, 2) covered by all 3 → XOR of 3 trues = true
            result[2 * 5 + 2].Should().BeTrue("odd count of overlaps remains inside");
            // (1, 1) covered by a and b (2) → XOR = false
            result[1 * 5 + 1].Should().BeFalse("even count of overlaps is hole");
            // (0, 0) covered by a only → true
            result[0 * 5 + 0].Should().BeTrue();
            // (4, 4) covered by c only → true
            result[4 * 5 + 4].Should().BeTrue();
        }
    }
}
