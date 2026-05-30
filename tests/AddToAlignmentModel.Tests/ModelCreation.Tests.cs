using ADPUK.NINA.AddToAlignmentModel;
using ADPUK.NINA.AddToAlignmentModel.Locales;
using AddToAlignmentModel.Tests.TestHelpers;
using Moq;
using NINA.Astrometry;
using NINA.Core.Model;
using NINA.PlateSolving;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Params = ADPUK.NINA.AddToAlignmentModel.ModelPointCreator.ModelCreationParameters;

namespace AddToAlignmentModel.Tests {

    // Tier-2 characterization of ModelPointCreator's per-point acquisition flow,
    // exercised with Moq'd mediators and a stubbed plate solve. The behavior that
    // matters most for the eventual GEM work is whether the
    // "Telescope:AddAlignmentReference" action is pushed to the mount, so that is
    // what these tests pin.
    //
    // ExecuteCreate (the grid loop) is deliberately NOT covered here: it calls
    // ADP_Tools.ReadyToStart, which opens a WPF MessageBox and cannot run
    // headless until that method is refactored (a separate, sign-off-gated step).
    public class ModelCreationTests {

        private const string AddReferenceAction = "Telescope:AddAlignmentReference";

        // CreateModelPoint and GetCurrentLocation transform coordinates to JNOW,
        // which calls NINA's native NOVAS library. That DLL is vendored into the
        // test project (native/NOVAS31lib.dll, copied to External/x64/NOVAS in the
        // build output) so these tests run on CI.

        private static TopocentricCoordinates Topo(double azimuth, double altitude) {
            return new TopocentricCoordinates(
                Angle.ByDegree(azimuth), Angle.ByDegree(altitude),
                Angle.ByDegree(50.0), Angle.ByDegree(0.0));
        }

        private static Params PointParams(TopocentricCoordinates target, double minElevationAboveHorizon = 0.0) {
            return new Params {
                TargetCoordinatesAltAz = target,
                MinElevationAboveHorizon = minElevationAboveHorizon,
                SolveAttempts = 1,
                PlateSolveCloseDelay = 0
            };
        }

        private static PlateSolveResult Solved() {
            return new PlateSolveResult {
                Success = true,
                Coordinates = new Coordinates(10.0, 20.0, Epoch.J2000, Coordinates.RAType.Degrees)
            };
        }

        private static PlateSolveResult Failed() {
            return new PlateSolveResult { Success = false };
        }

        // ---- SolveDirectToMount ------------------------------------------------

        [Fact]
        public async Task SolveDirectToMount_Success_PushesAlignmentReference() {
            ModelPointCreatorHarness harness = new ModelPointCreatorHarness();
            ModelPointCreator creator = harness.Create(Solved());

            PlateSolveResult result = await creator.SolveDirectToMount(
                1, 0, new Progress<ApplicationStatus>(), CancellationToken.None, showDialog: true);

            Assert.True(result.Success);
            harness.Telescope.Verify(t => t.Action(AddReferenceAction, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task SolveDirectToMount_Failure_DoesNotPushReference() {
            ModelPointCreatorHarness harness = new ModelPointCreatorHarness();
            ModelPointCreator creator = harness.Create(Failed());

            PlateSolveResult result = await creator.SolveDirectToMount(
                1, 0, new Progress<ApplicationStatus>(), CancellationToken.None, showDialog: true);

            Assert.False(result.Success);
            harness.Telescope.Verify(t => t.Action(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SolveDirectToMount_PushesJNowTransformedCoordinates() {
            // The mount's alignment model is in its native epoch (JNOW on CPWI);
            // plate solves return J2000. The pushed payload must be the JNOW-
            // transformed coordinates, not the raw J2000 result (the bug that put
            // every reference point ~0.4 deg off).
            Coordinates j2000 = new Coordinates(Angle.ByHours(6.0), Angle.ByDegree(45.0), Epoch.J2000);
            PlateSolveResult result = new PlateSolveResult { Success = true, Coordinates = j2000 };

            ModelPointCreatorHarness harness = new ModelPointCreatorHarness();
            string payload = null;
            harness.Telescope
                .Setup(t => t.Action("Telescope:AddAlignmentReference", It.IsAny<string>()))
                .Callback<string, string>((_, p) => payload = p)
                .Returns(string.Empty);
            ModelPointCreator creator = harness.Create(result);

            await creator.SolveDirectToMount(1, 0, new Progress<ApplicationStatus>(), CancellationToken.None, showDialog: true);

            Assert.NotNull(payload);
            string[] parts = payload.Split(':');
            double pushedRa = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
            double pushedDec = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);

            Coordinates jnow = j2000.Transform(Epoch.JNOW);
            // Pushed coordinates match the JNOW transform (within ~arcsec)...
            Assert.Equal(jnow.RA, pushedRa, 3);
            Assert.Equal(jnow.Dec, pushedDec, 3);
            // ...and are meaningfully shifted from the raw J2000 RA (precession is real).
            Assert.True(System.Math.Abs(pushedRa - j2000.RA) > 0.0001);
        }

        // ---- CreateModelPoint --------------------------------------------------

        [Fact]
        public async Task CreateModelPoint_AboveHorizonAndSolved_SlewsAndPushesReference() {
            ModelPointCreatorHarness harness = new ModelPointCreatorHarness(cameraConnected: true);
            ModelPointCreator creator = harness.Create(Solved());
            Params parameters = PointParams(Topo(90.0, 60.0));

            ModelPoint point = await creator.CreateModelPoint(
                parameters, new Progress<ApplicationStatus>(), CancellationToken.None, showDialog: true);

            harness.Telescope.Verify(t => t.SlewToCoordinatesAsync(It.IsAny<Coordinates>(), It.IsAny<CancellationToken>()), Times.Once);
            harness.Telescope.Verify(t => t.Action(AddReferenceAction, It.IsAny<string>()), Times.Once);
            Assert.NotEqual(ViewStrings.PlateSolveFailed, point.ActualRAString);
        }

        [Fact]
        public async Task CreateModelPoint_AboveHorizonButSolveFails_MarksFailedAndPushesNothing() {
            ModelPointCreatorHarness harness = new ModelPointCreatorHarness(cameraConnected: true);
            ModelPointCreator creator = harness.Create(Failed());
            Params parameters = PointParams(Topo(90.0, 60.0));

            ModelPoint point = await creator.CreateModelPoint(
                parameters, new Progress<ApplicationStatus>(), CancellationToken.None, showDialog: true);

            Assert.Equal(ViewStrings.PlateSolveFailed, point.ActualRAString);
            harness.Telescope.Verify(t => t.Action(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task CreateModelPoint_TargetBelowHorizon_MarksBelowHorizon_AndPushesNothing() {
            // A below-horizon point is skipped: marked below-horizon, no slew, no
            // alignment reference pushed. This path previously threw
            // NullReferenceException from an unconditional service.DelayedClose in
            // finally (the window service is only created on the camera-connected
            // solve path); that close is now null-safe.
            ModelPointCreatorHarness harness = new ModelPointCreatorHarness(cameraConnected: true);
            ModelPointCreator creator = harness.Create(Solved());
            Params parameters = PointParams(Topo(90.0, 10.0), minElevationAboveHorizon: 20.0);

            ModelPoint point = await creator.CreateModelPoint(
                parameters, new Progress<ApplicationStatus>(), CancellationToken.None, showDialog: true);

            Assert.Equal(ViewStrings.TargetBelowHorizon, point.ActualRAString);
            harness.Telescope.Verify(t => t.SlewToCoordinatesAsync(It.IsAny<Coordinates>(), It.IsAny<CancellationToken>()), Times.Never);
            harness.Telescope.Verify(t => t.Action(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // ---- GetCurrentLocation ------------------------------------------------

        [Fact]
        public async Task GetCurrentLocation_Success_PushesReference() {
            ModelPointCreatorHarness harness = new ModelPointCreatorHarness();
            ModelPointCreator creator = harness.Create(Solved());

            ModelPoint point = await creator.GetCurrentLocation(
                1, 0, new Progress<ApplicationStatus>(), CancellationToken.None, showDialog: true);

            harness.Telescope.Verify(t => t.GetCurrentPosition(), Times.Once);
            harness.Telescope.Verify(t => t.Action(AddReferenceAction, It.IsAny<string>()), Times.Once);
            Assert.NotEqual(ViewStrings.PlateSolveFailed, point.ActualRAString);
        }
    }
}
