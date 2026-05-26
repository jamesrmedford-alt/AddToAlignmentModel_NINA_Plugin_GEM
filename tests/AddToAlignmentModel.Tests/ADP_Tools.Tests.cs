using ADPUK.NINA.AddToAlignmentModel;
using ADPUK.NINA.AddToAlignmentModel.Locales;
using Moq;
using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.PlateSolving;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace AddToAlignmentModel.Tests {

    // Characterization tests for the Tier-1 pure logic in ADP_Tools. These pin
    // the CURRENT Alt-Az behavior so that any later GEM change which alters an
    // output fails CI. They intentionally assert existing behavior, not "ideal"
    // behavior.
    //
    // ADP_Tools.ReadyToStart is deliberately not tested: it calls a WPF
    // MessageBox and cannot be exercised without a source refactor that Phase 1
    // forbids. It is excluded from the CI coverage gate for the same reason.
    public class ADP_ToolsTests {

        // ---- AboveMinAlt (Coordinates overload) --------------------------------

        // The altitude is derived from the real astrometry library, so we compute
        // the expected altitude the same way and pin the >= comparison around it.
        private static double ExpectedAltitude(Coordinates c, double latitude) {
            return AstroUtil.GetAltitude(c.RADegrees, latitude, c.Dec);
        }

        [Fact]
        public void AboveMinAlt_Coordinates_NullHorizon_ComparesAltitudeToThreshold() {
            Coordinates coords = new Coordinates(45.0, 20.0, Epoch.J2000, RAType.Degrees);
            double latitude = 50.0;
            double alt = ExpectedAltitude(coords, latitude);

            Assert.True(ADP_Tools.AboveMinAlt(coords, null, latitude, alt - 1.0));
            Assert.False(ADP_Tools.AboveMinAlt(coords, null, latitude, alt + 1.0));
        }

        [Fact]
        public void AboveMinAlt_Coordinates_WithHorizon_AddsHorizonAltitude() {
            Coordinates coords = new Coordinates(45.0, 20.0, Epoch.J2000, RAType.Degrees);
            double latitude = 50.0;
            double alt = ExpectedAltitude(coords, latitude);

            // Flat horizon below the point: alt >= (alt-5) + 0  => true.
            CustomHorizon belowPoint = FlatHorizon(alt - 5.0);
            Assert.True(ADP_Tools.AboveMinAlt(coords, belowPoint, latitude, 0.0));

            // Flat horizon above the point: alt >= (alt+5) + 0  => false.
            CustomHorizon abovePoint = FlatHorizon(alt + 5.0);
            Assert.False(ADP_Tools.AboveMinAlt(coords, abovePoint, latitude, 0.0));
        }

        // ---- AboveMinAlt (TopocentricCoordinates overload) ---------------------

        private static TopocentricCoordinates Topo(double azimuth, double altitude) {
            return new TopocentricCoordinates(
                Angle.ByDegree(azimuth),
                Angle.ByDegree(altitude),
                Angle.ByDegree(50.0),
                Angle.ByDegree(0.0));
        }

        [Fact]
        public void AboveMinAlt_Topocentric_NullHorizon_ComparesAltitudeToThreshold() {
            TopocentricCoordinates topo = Topo(azimuth: 90.0, altitude: 40.0);

            Assert.True(ADP_Tools.AboveMinAlt(topo, null, 39.0));
            Assert.True(ADP_Tools.AboveMinAlt(topo, null, 40.0));   // boundary: >=
            Assert.False(ADP_Tools.AboveMinAlt(topo, null, 41.0));
        }

        [Fact]
        public void AboveMinAlt_Topocentric_WithHorizon_AddsHorizonAltitudePlusMargin() {
            TopocentricCoordinates topo = Topo(azimuth: 90.0, altitude: 40.0);
            CustomHorizon horizon = FlatHorizon(10.0);

            // 40 >= 10 + 25 (35) => true
            Assert.True(ADP_Tools.AboveMinAlt(topo, horizon, 25.0));
            // 40 >= 10 + 30 (40) => true (boundary)
            Assert.True(ADP_Tools.AboveMinAlt(topo, horizon, 30.0));
            // 40 >= 10 + 31 (41) => false
            Assert.False(ADP_Tools.AboveMinAlt(topo, horizon, 31.0));
        }

        // ---- ValidateConnections ----------------------------------------------

        private static Mock<IPluginOptionsAccessor> SettingsWithEquatorial(bool enabled) {
            Mock<IPluginOptionsAccessor> settings = new Mock<IPluginOptionsAccessor>();
            settings.Setup(x => x.GetValueBoolean("EnableEquatorialMounts", false)).Returns(enabled);
            return settings;
        }

        private static TelescopeInfo Scope(bool connected, string name, AlignmentMode mode) {
            return new TelescopeInfo {
                Connected = connected,
                Name = name,
                AlignmentMode = mode
            };
        }

        private static CameraInfo Camera(bool connected) {
            return new CameraInfo { Connected = connected };
        }

        [Fact]
        public void ValidateConnections_AllGood_ReturnsNoErrors() {
            List<string> errors = ADP_Tools.ValidateConnections(
                Scope(true, "CPWI Telescope", AlignmentMode.AltAz),
                Camera(true),
                SettingsWithEquatorial(false).Object);

            Assert.Empty(errors);
        }

        [Fact]
        public void ValidateConnections_TelescopeDisconnected_ReturnsSingleError() {
            List<string> errors = ADP_Tools.ValidateConnections(
                Scope(false, "CPWI Telescope", AlignmentMode.AltAz),
                Camera(true),
                SettingsWithEquatorial(false).Object);

            Assert.Single(errors);
        }

        [Fact]
        public void ValidateConnections_NameWithoutCPWI_AddsRequireCPWI() {
            List<string> errors = ADP_Tools.ValidateConnections(
                Scope(true, "Some Other Mount", AlignmentMode.AltAz),
                Camera(true),
                SettingsWithEquatorial(false).Object);

            Assert.Contains(ViewStrings.RequireCPWI, errors);
        }

        [Fact]
        public void ValidateConnections_NameMatchIsCaseInsensitive() {
            List<string> errors = ADP_Tools.ValidateConnections(
                Scope(true, "my cpwi mount", AlignmentMode.AltAz),
                Camera(true),
                SettingsWithEquatorial(false).Object);

            Assert.DoesNotContain(ViewStrings.RequireCPWI, errors);
        }

        [Fact]
        public void ValidateConnections_NonAltAz_WithoutEquatorialFlag_AddsAltAzOnly() {
            List<string> errors = ADP_Tools.ValidateConnections(
                Scope(true, "CPWI Telescope", AlignmentMode.GermanPolar),
                Camera(true),
                SettingsWithEquatorial(false).Object);

            Assert.Contains(ViewStrings.AltAzOnly, errors);
        }

        [Fact]
        public void ValidateConnections_NonAltAz_WithEquatorialFlag_IsAllowed() {
            List<string> errors = ADP_Tools.ValidateConnections(
                Scope(true, "CPWI Telescope", AlignmentMode.GermanPolar),
                Camera(true),
                SettingsWithEquatorial(true).Object);

            Assert.DoesNotContain(ViewStrings.AltAzOnly, errors);
            Assert.Empty(errors);
        }

        [Fact]
        public void ValidateConnections_CameraDisconnected_ReturnsSingleError() {
            List<string> errors = ADP_Tools.ValidateConnections(
                Scope(true, "CPWI Telescope", AlignmentMode.AltAz),
                Camera(false),
                SettingsWithEquatorial(false).Object);

            Assert.Single(errors);
        }

        [Fact]
        public void ValidateConnections_AllFailing_ReturnsFourErrors() {
            List<string> errors = ADP_Tools.ValidateConnections(
                Scope(false, "Some Other Mount", AlignmentMode.GermanPolar),
                Camera(false),
                SettingsWithEquatorial(false).Object);

            Assert.Equal(4, errors.Count);
        }

        // ---- CreateCaptureSolverParameter --------------------------------------

        private static Mock<IProfile> ProfileFor(
            int numberOfAttempts,
            short binning,
            int downSample,
            int maxObjects,
            double reattemptDelayMinutes,
            int regions,
            double searchRadius,
            bool blindFailover,
            double focalLength,
            double pixelSize) {

            Mock<IPlateSolveSettings> plate = new Mock<IPlateSolveSettings>();
            plate.SetupGet(x => x.NumberOfAttempts).Returns(numberOfAttempts);
            plate.SetupGet(x => x.Binning).Returns(binning);
            plate.SetupGet(x => x.DownSampleFactor).Returns(downSample);
            plate.SetupGet(x => x.MaxObjects).Returns(maxObjects);
            plate.SetupGet(x => x.ReattemptDelay).Returns(reattemptDelayMinutes);
            plate.SetupGet(x => x.Regions).Returns(regions);
            plate.SetupGet(x => x.SearchRadius).Returns(searchRadius);
            plate.SetupGet(x => x.BlindFailoverEnabled).Returns(blindFailover);

            Mock<ITelescopeSettings> telescope = new Mock<ITelescopeSettings>();
            telescope.SetupGet(x => x.FocalLength).Returns(focalLength);

            Mock<ICameraSettings> camera = new Mock<ICameraSettings>();
            camera.SetupGet(x => x.PixelSize).Returns(pixelSize);

            Mock<IProfile> profile = new Mock<IProfile>();
            profile.SetupGet(x => x.PlateSolveSettings).Returns(plate.Object);
            profile.SetupGet(x => x.TelescopeSettings).Returns(telescope.Object);
            profile.SetupGet(x => x.CameraSettings).Returns(camera.Object);
            return profile;
        }

        [Fact]
        public void CreateCaptureSolverParameter_MapsAllProfileFields() {
            Mock<IProfile> profile = ProfileFor(
                numberOfAttempts: 3, binning: 2, downSample: 4, maxObjects: 500,
                reattemptDelayMinutes: 1.5, regions: 1000, searchRadius: 30.0,
                blindFailover: true, focalLength: 700.0, pixelSize: 3.8);
            Coordinates coords = new Coordinates(10.0, 20.0, Epoch.J2000, RAType.Degrees);

            CaptureSolverParameter p = ADP_Tools.CreateCaptureSolverParameter(profile.Object, coords);

            Assert.Equal(3, p.Attempts);                                  // null solveAttempts -> profile value
            Assert.Equal((short)2, p.Binning);
            Assert.Equal(4, p.DownSampleFactor);
            Assert.Equal(700.0, p.FocalLength);
            Assert.Equal(500, p.MaxObjects);
            Assert.Equal(3.8, p.PixelSize);
            Assert.Equal(TimeSpan.FromMinutes(1.5), p.ReattemptDelay);
            Assert.Equal(1000, p.Regions);
            Assert.Equal(30.0, p.SearchRadius);
            Assert.True(p.BlindFailoverEnabled);
            Assert.Same(coords, p.Coordinates);
        }

        [Fact]
        public void CreateCaptureSolverParameter_ExplicitSolveAttempts_OverridesProfile() {
            Mock<IProfile> profile = ProfileFor(
                numberOfAttempts: 3, binning: 2, downSample: 4, maxObjects: 500,
                reattemptDelayMinutes: 1.5, regions: 1000, searchRadius: 30.0,
                blindFailover: true, focalLength: 700.0, pixelSize: 3.8);
            Coordinates coords = new Coordinates(10.0, 20.0, Epoch.J2000, RAType.Degrees);

            CaptureSolverParameter p = ADP_Tools.CreateCaptureSolverParameter(profile.Object, coords, 7);

            Assert.Equal(7, p.Attempts);
        }

        // ---- helpers -----------------------------------------------------------

        // Builds a CustomHorizon whose altitude is the same constant at every
        // azimuth, via the standard "azimuth altitude" text format.
        private static CustomHorizon FlatHorizon(double altitudeDegrees) {
            string text =
                "0 " + altitudeDegrees.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" +
                "360 " + altitudeDegrees.ToString(System.Globalization.CultureInfo.InvariantCulture);
            using (StringReader reader = new StringReader(text)) {
                return CustomHorizon.FromReader_Standard(reader);
            }
        }
    }
}
