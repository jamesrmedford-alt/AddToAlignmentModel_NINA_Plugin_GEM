using ADPUK.NINA.AddToAlignmentModel;
using Moq;
using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Utility.WindowService;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Interfaces.Mediator;
using NINA.PlateSolving;
using NINA.PlateSolving.Interfaces;
using NINA.Profile.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AddToAlignmentModel.Tests.TestHelpers {

    // A ModelPointCreator whose plate solve is replaced by a canned result, so the
    // per-point acquisition flow can be exercised without real imaging or solving.
    // DoSolve is virtual on the base type; overriding it is the test seam.
    internal sealed class StubModelPointCreator : ModelPointCreator {
        private readonly PlateSolveResult result;

        public StubModelPointCreator(
            ICameraMediator cameraMediator, ITelescopeMediator telescopeMediator,
            IRotatorMediator rotatorMediator, IImagingMediator imagingMediator,
            IFilterWheelMediator filterWheelMediator, IPlateSolverFactory plateSolverFactory,
            IWindowServiceFactory windowServiceFactory, IProfileService profileService,
            PlateSolveResult result)
            : base(cameraMediator, telescopeMediator, rotatorMediator, imagingMediator,
                   filterWheelMediator, plateSolverFactory, windowServiceFactory, profileService) {
            this.result = result;
        }

        public override Task<PlateSolveResult> DoSolve(IProgress<ApplicationStatus> progress, int solveAttempts, CancellationToken token) {
            return Task.FromResult(result);
        }
    }

    // Wires up Moq'd NINA mediators for ModelPointCreator with sensible defaults:
    // camera connected, slews succeed, a no-op window service, and a null custom
    // horizon. Tests tweak what they need and assert against the exposed mocks.
    internal sealed class ModelPointCreatorHarness {
        public Mock<ICameraMediator> Camera { get; } = new Mock<ICameraMediator>();
        public Mock<ITelescopeMediator> Telescope { get; } = new Mock<ITelescopeMediator>();
        public Mock<IRotatorMediator> Rotator { get; } = new Mock<IRotatorMediator>();
        public Mock<IImagingMediator> Imaging { get; } = new Mock<IImagingMediator>();
        public Mock<IFilterWheelMediator> FilterWheel { get; } = new Mock<IFilterWheelMediator>();
        public Mock<IPlateSolverFactory> SolverFactory { get; } = new Mock<IPlateSolverFactory>();
        public Mock<IWindowServiceFactory> WindowServiceFactory { get; } = new Mock<IWindowServiceFactory>();
        public Mock<IWindowService> WindowService { get; } = new Mock<IWindowService>();
        public Mock<IProfileService> ProfileService { get; } = new Mock<IProfileService>();
        public Mock<IProfile> Profile { get; } = new Mock<IProfile>();
        public Mock<IAstrometrySettings> AstrometrySettings { get; } = new Mock<IAstrometrySettings>();

        public ModelPointCreatorHarness(bool cameraConnected = true) {
            Camera.Setup(c => c.GetInfo()).Returns(new CameraInfo { Connected = cameraConnected });
            Telescope.Setup(t => t.SlewToCoordinatesAsync(It.IsAny<Coordinates>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            Telescope.Setup(t => t.Action(It.IsAny<string>(), It.IsAny<string>())).Returns(string.Empty);
            Telescope.Setup(t => t.GetCurrentPosition()).Returns(new Coordinates(10.0, 20.0, Epoch.J2000, Coordinates.RAType.Degrees));
            WindowServiceFactory.Setup(w => w.Create()).Returns(WindowService.Object);
            ProfileService.Setup(p => p.ActiveProfile).Returns(Profile.Object);
            Profile.Setup(p => p.AstrometrySettings).Returns(AstrometrySettings.Object);
            AstrometrySettings.Setup(a => a.Horizon).Returns((CustomHorizon)null);
        }

        public ModelPointCreator Create(PlateSolveResult result) {
            return new StubModelPointCreator(
                Camera.Object, Telescope.Object, Rotator.Object, Imaging.Object,
                FilterWheel.Object, SolverFactory.Object, WindowServiceFactory.Object,
                ProfileService.Object, result);
        }
    }
}
