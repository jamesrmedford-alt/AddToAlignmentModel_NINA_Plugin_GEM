using ADPUK.NINA.AddToAlignmentModel;
using Xunit;
using Params = ADPUK.NINA.AddToAlignmentModel.ModelPointCreator.ModelCreationParameters;

namespace AddToAlignmentModel.Tests {

    // Characterization tests for the grid step-size math in
    // ModelPointCreator.ModelCreationParameters.
    //
    // IMPORTANT: the AltStepSize getter is NOT a pure read. It MUTATES
    // NumberOfAltitudePoints and MinElevation as a side effect. These tests pin
    // that current behavior on purpose; they are not asserting that the design is
    // correct. A fresh instance is used per case because reading AltStepSize
    // changes state.
    public class GridGenerationTests {

        private const int Precision = 9;

        // ---- AltStepSize -------------------------------------------------------

        [Fact]
        public void AltStepSize_MultiplePoints_DividesRangeEvenly_NoMutation() {
            Params p = new Params {
                MinElevation = 30,
                MaxElevation = 80,
                NumberOfAltitudePoints = 6
            };

            double step = p.AltStepSize;

            Assert.Equal(10.0, step, Precision);             // (80 - 30) / (6 - 1)
            Assert.Equal(6, p.NumberOfAltitudePoints);       // unchanged
            Assert.Equal(30.0, p.MinElevation, Precision);   // unchanged
        }

        [Fact]
        public void AltStepSize_RangeBelowFive_ForcesSinglePoint_AndMutatesMinElevation() {
            Params p = new Params {
                MinElevation = 40,
                MaxElevation = 43,   // range 3 (< 5)
                NumberOfAltitudePoints = 5
            };

            double step = p.AltStepSize;

            Assert.Equal(1, p.NumberOfAltitudePoints);       // forced to 1 by the < 5 guard
            Assert.Equal(41.5, p.MinElevation, Precision);   // mutated to (40 + 43) / 2
            // step reads the ALREADY-mutated MinElevation: (43 + 41.5) / 2
            Assert.Equal(42.25, step, Precision);
        }

        [Fact]
        public void AltStepSize_ExplicitSinglePoint_MutatesMinElevation() {
            Params p = new Params {
                MinElevation = 30,
                MaxElevation = 80,   // range 50 (>= 5), so the guard does not fire
                NumberOfAltitudePoints = 1
            };

            double step = p.AltStepSize;

            Assert.Equal(1, p.NumberOfAltitudePoints);
            Assert.Equal(55.0, p.MinElevation, Precision);   // (30 + 80) / 2
            // step reads the mutated MinElevation: (80 + 55) / 2
            Assert.Equal(67.5, step, Precision);
        }

        // ---- AzStepSize --------------------------------------------------------

        [Theory]
        [InlineData(4, 90.0)]
        [InlineData(8, 45.0)]
        [InlineData(12, 30.0)]
        public void AzStepSize_DividesCircleByPointCount(int points, double expected) {
            Params p = new Params { NumberOfAzimuthPoints = points };

            Assert.Equal(expected, p.AzStepSize, Precision);
        }

        [Fact]
        public void AzStepSize_ZeroPoints_IsInfinity() {
            // Characterizes current behavior: 360d / 0 is a double divide -> Infinity,
            // not an exception. Pinned so a future change to this edge is visible.
            Params p = new Params { NumberOfAzimuthPoints = 0 };

            Assert.True(double.IsPositiveInfinity(p.AzStepSize));
        }
    }
}
