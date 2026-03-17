using Xunit;
using FluentAssertions;
using ESAPI_EQD2Viewer.Core.Calculations;

namespace ESAPI_EQD2Viewer.Tests.Calculations
{
    public class EQD2CalculatorTests
    {
        [Theory]
        [InlineData(50.0, 25, 3.0, 50.0)] // 2 Gy / fraktio, α/β = 3 -> EQD2 = 50 Gy
        [InlineData(60.0, 30, 10.0, 60.0)] // 2 Gy / fraktio, α/β = 10 -> EQD2 = 60 Gy
        [InlineData(45.0, 15, 3.0, 54.0)] // 3 Gy / fraktio, α/β = 3 -> EQD2 = 45 * (3+3)/(2+3) = 54 Gy
        public void ToEQD2_ShouldReturnCorrectEquivalentDose(double totalDose, int fractions, double alphaBeta, double expectedEqd2)
        {
            // Act
            double result = EQD2Calculator.ToEQD2(totalDose, fractions, alphaBeta);

            // Assert
            result.Should().BeApproximately(expectedEqd2, 0.001, "Koska standardikaava on D * (d + a/b) / (2 + a/b)");
        }

        [Fact]
        public void ToEQD2_ShouldReturnTotalDose_WhenFractionsAreZeroOrLess()
        {
            // Act
            double result = EQD2Calculator.ToEQD2(50.0, 0, 3.0);

            // Assert
            result.Should().Be(50.0);
        }
    }
}