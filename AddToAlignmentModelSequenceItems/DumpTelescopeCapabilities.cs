using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ADPUK.NINA.AddToAlignmentModel.AddToAlignmentModelSequenceItems {
    /// <summary>
    /// Diagnostic-only sequence item. Dumps the connected telescope's reported
    /// info and ASCOM SupportedActions list, and probes the
    /// "Telescope:AddAlignmentReference" Action with a deliberately invalid
    /// payload so we can tell whether the CPWI driver exposes it in the
    /// current connection mode (AltAz vs Polar / GermanPolar) without
    /// modifying any existing alignment model.
    ///
    /// Drop this file into AddToAlignmentModelSequenceItems/ alongside
    /// SolveAddToAlignmentModel.cs. If the .csproj is SDK-style it will pick
    /// it up automatically; for a legacy-format .csproj add a
    /// <Compile Include="AddToAlignmentModelSequenceItems\DumpTelescopeCapabilities.cs" />
    /// entry under the existing ItemGroup of Compile items.
    ///
    /// After build + install in NINA, find it in the sequencer under the
    /// "Add To CPWI Alignment Model" category as "Dump Telescope Capabilities".
    /// Run it with the AVX connected via CPWI in EQ mode; results go to the
    /// NINA log and a timestamped .txt file under %LOCALAPPDATA%\NINA\Logs\.
    /// For the pier-side correlation, run it twice: once with the scope on a
    /// target EAST of the meridian and once WEST, plus once at the home position.
    /// </summary>
    [ExportMetadata("Name", "Dump Telescope Capabilities (diagnostic)")]
    [ExportMetadata("Description", "Diagnostic: writes connected mount info, GEM capability flags, a pier-side/hour-angle correlation, SupportedActions, and a non-destructive probe of Telescope:AddAlignmentReference to the NINA log and a timestamped file.")]
    [ExportMetadata("Icon", "CrosshairSVG")]
    [ExportMetadata("Category", "Add To CPWI Alignment Model")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class DumpTelescopeCapabilities : SequenceItem, IValidatable {
        private readonly ITelescopeMediator telescopeMediator;
        private IList<string> issues = new List<string>();

        [ImportingConstructor]
        public DumpTelescopeCapabilities(ITelescopeMediator telescopeMediator) {
            this.telescopeMediator = telescopeMediator;
        }

        private DumpTelescopeCapabilities(DumpTelescopeCapabilities cloneMe) : this(cloneMe.telescopeMediator) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            return new DumpTelescopeCapabilities(this);
        }

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        public bool Validate() {
            var i = new List<string>();
            var info = telescopeMediator.GetInfo();
            if (!info.Connected) {
                i.Add("Telescope is not connected");
            }
            Issues = i;
            return i.Count == 0;
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var info = telescopeMediator.GetInfo();
            var sb = new StringBuilder();

            sb.AppendLine("=== Telescope Capability Dump ===");
            sb.AppendLine($"Timestamp UTC:    {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            // -- Core identity, mode, and pose --
            // The properties below are the standard set on NINA's TelescopeInfo.
            // If any of them have been renamed in your NINA build the helper
            // will catch the exception and write "<n/a: ...>" rather than fail
            // the whole dump.
            sb.AppendLine("--- Identity & driver ---");
            TryAppend(sb, "Name",             () => info.Name);
            TryAppend(sb, "Description",      () => info.Description);
            TryAppend(sb, "DriverInfo",       () => info.DriverInfo);
            TryAppend(sb, "DriverVersion",    () => info.DriverVersion);
            sb.AppendLine();

            sb.AppendLine("--- Mode & state ---");
            TryAppend(sb, "AlignmentMode",    () => info.AlignmentMode.ToString());
            TryAppend(sb, "EquatorialSystem", () => info.EquatorialSystem.ToString());
            TryAppend(sb, "Connected",        () => info.Connected.ToString());
            TryAppend(sb, "AtPark",           () => info.AtPark.ToString());
            TryAppend(sb, "AtHome",           () => info.AtHome.ToString());
            TryAppend(sb, "Tracking",         () => info.TrackingEnabled.ToString());
            TryAppend(sb, "SideOfPier",       () => info.SideOfPier.ToString());
            sb.AppendLine();

            sb.AppendLine("--- Pose ---");
            TryAppend(sb, "RightAscension",   () => $"{info.RightAscensionString} ({info.RightAscension:F6} h)");
            TryAppend(sb, "Declination",      () => $"{info.DeclinationString} ({info.Declination:F4} deg)");
            TryAppend(sb, "Altitude",         () => $"{info.AltitudeString} ({info.Altitude:F4} deg)");
            TryAppend(sb, "Azimuth",          () => $"{info.AzimuthString} ({info.Azimuth:F4} deg)");
            TryAppend(sb, "SiderealTime",     () => $"{info.SiderealTime:F6} h");
            TryAppend(sb, "SiteLatitude",     () => $"{info.SiteLatitude:F4} deg");
            TryAppend(sb, "SiteLongitude",    () => $"{info.SiteLongitude:F4} deg");
            sb.AppendLine();

            // -- GEM-relevant capability flags --
            // What the driver admits it can do in the current connection mode.
            // These inform the German-equatorial pier-side / meridian handling.
            sb.AppendLine("--- Capabilities (GEM-relevant) ---");
            TryAppend(sb, "CanSlew",             () => info.CanSlew.ToString());
            TryAppend(sb, "CanPark",             () => info.CanPark.ToString());
            TryAppend(sb, "CanFindHome",         () => info.CanFindHome.ToString());
            TryAppend(sb, "CanSetPierSide",      () => info.CanSetPierSide.ToString());
            TryAppend(sb, "CanMovePrimaryAxis",  () => info.CanMovePrimaryAxis.ToString());
            TryAppend(sb, "CanMoveSecondaryAxis",() => info.CanMoveSecondaryAxis.ToString());
            TryAppend(sb, "TrackingRate",        () => info.TrackingRate.ToString());
            TryAppend(sb, "TimeToMeridianFlip",  () => $"{info.TimeToMeridianFlip:F4} h");
            TryAppend(sb, "TargetSideOfPier",    () => info.TargetSideOfPier?.ToString() ?? "<null>");
            sb.AppendLine();

            // -- Pier side vs meridian correlation --
            // ASCOM's SideOfPier is reported inconsistently across drivers (CPWI
            // may return Unknown). Hour angle (HA = LocalSiderealTime - RA) tells
            // us deterministically which side of the meridian the scope points to.
            // Capturing both, at one target EAST and one WEST of the meridian,
            // tells us whether the driver's SideOfPier is trustworthy or whether
            // we should derive pier side from HA ourselves for grid partitioning.
            sb.AppendLine("=== Pier side / meridian correlation ===");
            sb.AppendLine("Run once with the scope EAST of the meridian and once WEST.");
            try {
                double lst = info.SiderealTime;
                double ra = info.RightAscension;
                double ha = lst - ra;
                while (ha < -12.0) { ha += 24.0; }
                while (ha >= 12.0) { ha -= 24.0; }
                string impliedSide = ha < 0.0
                    ? "object EAST of meridian (rising)"
                    : "object WEST of meridian (setting)";
                sb.AppendLine($"{"SiderealTime(h)",-20}{lst:F6}");
                sb.AppendLine($"{"RightAscension(h)",-20}{ra:F6}");
                sb.AppendLine($"{"HourAngle(h)",-20}{ha:F6}");
                sb.AppendLine($"{"HA implies",-20}{impliedSide}");
                TryAppend(sb, "Driver SideOfPier", () => info.SideOfPier.ToString());
                sb.AppendLine("(We correlate 'HA implies' against 'Driver SideOfPier' across both");
                sb.AppendLine(" positions to decide which is the reliable source of pier side.)");
            } catch (Exception ex) {
                sb.AppendLine($"Could not compute hour-angle correlation: {ex.GetType().Name}: {ex.Message}");
            }
            sb.AppendLine();

            // -- SupportedActions (the canonical ASCOM discovery property) --
            sb.AppendLine("=== SupportedActions ===");
            try {
                var actions = info.SupportedActions;
                if (actions == null || actions.Count == 0) {
                    sb.AppendLine("(driver reports no supported actions)");
                } else {
                    foreach (var a in actions) {
                        sb.AppendLine($"  {a}");
                    }
                }
            } catch (Exception ex) {
                sb.AppendLine($"Could not read SupportedActions: {ex.GetType().Name}: {ex.Message}");
                sb.AppendLine("(if this is a compile-time issue on your NINA version, replace info.SupportedActions with the correct property name)");
            }
            sb.AppendLine();

            // -- Non-destructive probe of the action this plugin depends on --
            // We pass a payload that clearly cannot be parsed as RA:Dec, so:
            //   - If the driver throws ActionNotImplementedException, the
            //     Action is NOT exposed in the current connection mode.
            //   - If it throws anything else (parse error, format error,
            //     argument exception), the Action IS exposed; CPWI just
            //     rejected our garbage payload.
            //   - If it somehow returns without throwing, the Action is
            //     exposed and apparently very tolerant; either way, we have
            //     not added a real reference point because the payload was
            //     not valid coordinates.
            sb.AppendLine("=== Action probe: Telescope:AddAlignmentReference ===");
            sb.AppendLine("Sending deliberately invalid payload \"INVALID_PROBE_NOT_RADEC\".");
            const string probeName = "Telescope:AddAlignmentReference";
            const string probePayload = "INVALID_PROBE_NOT_RADEC";
            try {
                string result = telescopeMediator.Action(probeName, probePayload);
                sb.AppendLine($"  -> No exception. Returned: \"{result}\"");
                sb.AppendLine("  Interpretation: the Action IS exposed by the driver.");
                sb.AppendLine("  Next step: verify empirically that calling it with valid");
                sb.AppendLine("  RA:Dec after a plate solve actually updates the alignment model.");
            } catch (Exception ex) {
                sb.AppendLine($"  -> {ex.GetType().FullName}");
                sb.AppendLine($"     Message: {ex.Message}");
                string typeName = ex.GetType().Name;
                if (typeName.Contains("ActionNotImplemented") || typeName.Contains("NotImplemented")) {
                    sb.AppendLine("  Interpretation: the driver does NOT expose this Action in the current connection mode.");
                    sb.AppendLine("  The plugin's approach will not work without a different command or a CPWI update.");
                } else {
                    sb.AppendLine("  Interpretation: the Action IS exposed; the driver rejected the bogus payload as expected.");
                    sb.AppendLine("  Next step: verify empirically that calling it with valid RA:Dec after a plate solve");
                    sb.AppendLine("  actually updates the alignment model (pointing accuracy should improve).");
                }
            }

            string text = sb.ToString();

            // Write to NINA log
            Logger.Info(text);

            // Also write a standalone timestamped file for easy copy/paste
            string filePath = null;
            try {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NINA", "Logs");
                Directory.CreateDirectory(folder);
                filePath = Path.Combine(folder, $"TelescopeCapabilities_{DateTime.UtcNow:yyyyMMdd_HHmmss}.txt");
                File.WriteAllText(filePath, text);
            } catch (Exception ex) {
                Logger.Warning($"Could not write capability dump file: {ex.Message}");
            }

            string msg = filePath != null
                ? $"Telescope capabilities dumped to NINA log and {filePath}"
                : "Telescope capabilities dumped to NINA log";
            Notification.ShowInformation(msg);

            return Task.CompletedTask;
        }

        private static void TryAppend(StringBuilder sb, string label, Func<string> getter) {
            try {
                sb.AppendLine($"{label,-18}{getter()}");
            } catch (Exception ex) {
                sb.AppendLine($"{label,-18}<n/a: {ex.GetType().Name}>");
            }
        }
    }
}
