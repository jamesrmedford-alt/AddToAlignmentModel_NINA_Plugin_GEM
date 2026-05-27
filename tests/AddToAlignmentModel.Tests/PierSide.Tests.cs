using ADPUK.NINA.AddToAlignmentModel;
using System.Collections.Generic;
using Xunit;

namespace AddToAlignmentModel.Tests {

    // Tests for the German-equatorial pier-side primitives. The expected values
    // are taken directly from the Celestron AVX / CPWI capability dump captured
    // 2026-05-27, so these tests pin the hardware-verified behavior.
    public class PierSideTests {

        [Fact]
        public void HourAngle_MatchesCapturedCpwiSample() {
            // Dump: SiderealTime 13.078692 h, RightAscension 7.078466 h, HA 6.000226.
            double ha = ADP_Tools.HourAngle(13.078692, 7.078466);
            Assert.Equal(6.000226, ha, 5);
        }

        [Theory]
        [InlineData(1.0, 20.0, 5.0)]    // 1 - 20 = -19  -> +24 -> 5
        [InlineData(20.0, 1.0, -5.0)]   // 20 - 1 = 19   -> -24 -> -5
        [InlineData(12.0, 0.0, -12.0)]  // 12 is the upper bound -> wraps to -12
        [InlineData(6.0, 6.0, 0.0)]
        public void HourAngle_NormalizesToPlusMinus12(double lst, double ra, double expected) {
            Assert.Equal(expected, ADP_Tools.HourAngle(lst, ra), 6);
        }

        [Theory]
        // East of meridian (HA < 0) — CPWI predicted pierWest for these.
        [InlineData(-6.0, MeridianSide.East)]
        [InlineData(-0.5, MeridianSide.East)]
        // West of meridian (HA >= 0) — CPWI predicted pierEast for these.
        [InlineData(0.5, MeridianSide.West)]
        [InlineData(6.0, MeridianSide.West)]
        [InlineData(0.0, MeridianSide.West)]   // the meridian itself counts as West
        public void SideOfMeridian_MatchesCapturedConvention(double hourAngle, MeridianSide expected) {
            Assert.Equal(expected, ADP_Tools.SideOfMeridian(hourAngle));
        }

        [Fact]
        public void OrderByMeridianSide_WestFirst_GroupsAndPreservesOrder() {
            double[] hourAngles = { -6.0, 2.0, -1.0, 4.0, -3.0, 0.5 };

            List<double> ordered = ADP_Tools.OrderByMeridianSide(hourAngles, h => h, MeridianSide.West);

            // West (HA >= 0) first in original order, then East (HA < 0) in order.
            Assert.Equal(new[] { 2.0, 4.0, 0.5, -6.0, -1.0, -3.0 }, ordered);
        }

        [Fact]
        public void OrderByMeridianSide_EastFirst_GroupsAndPreservesOrder() {
            double[] hourAngles = { 2.0, -1.0, 3.0, -4.0 };

            List<double> ordered = ADP_Tools.OrderByMeridianSide(hourAngles, h => h, MeridianSide.East);

            Assert.Equal(new[] { -1.0, -4.0, 2.0, 3.0 }, ordered);
        }

        [Fact]
        public void OrderByMeridianSide_AllOneSide_IsUnchanged() {
            double[] hourAngles = { 1.0, 2.0, 3.0 };

            List<double> ordered = ADP_Tools.OrderByMeridianSide(hourAngles, h => h, MeridianSide.West);

            Assert.Equal(new[] { 1.0, 2.0, 3.0 }, ordered);
        }
    }
}
