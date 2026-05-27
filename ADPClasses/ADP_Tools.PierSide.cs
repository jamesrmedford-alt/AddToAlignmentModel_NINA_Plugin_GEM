using System;
using System.Collections.Generic;

namespace ADPUK.NINA.AddToAlignmentModel {

    /// <summary>Which side of the local meridian a target lies on.</summary>
    public enum MeridianSide {
        /// <summary>Hour angle &lt; 0: target is east of the meridian (rising).</summary>
        East,
        /// <summary>Hour angle &gt;= 0: target is at or west of the meridian (setting).</summary>
        West
    }

    public partial class ADP_Tools {

        // German-equatorial pier-side primitives. Pure (no I/O), so they are fully
        // unit-tested. They encode the behavior verified on the Celestron AVX via
        // CPWI on 2026-05-27, where the driver's DestinationSideOfPier returned a
        // clean mapping that agreed exactly with the hour-angle sign:
        //   East of meridian (HA < 0)  -> pierWest
        //   West of meridian (HA >= 0) -> pierEast
        // with the flip occurring at the meridian (HA = 0).
        //
        // These are the reusable core for partitioning an alignment grid by pier
        // side so a GEM makes a single deliberate meridian flip instead of crossing
        // the meridian unpredictably mid-run. They are not yet wired into the grid
        // loop; that integration is gated on headless-testability of the loop and
        // on hardware confirmation that a real reference push improves pointing.

        /// <summary>
        /// Hour angle = local sidereal time - right ascension, normalized to
        /// the range [-12, +12) hours. Negative is east of the meridian (rising),
        /// positive is west (setting).
        /// </summary>
        public static double HourAngle(double localSiderealTimeHours, double rightAscensionHours) {
            double ha = localSiderealTimeHours - rightAscensionHours;
            while (ha < -12.0) { ha += 24.0; }
            while (ha >= 12.0) { ha -= 24.0; }
            return ha;
        }

        /// <summary>
        /// Side of the meridian for an hour angle: East when HA &lt; 0, West when
        /// HA &gt;= 0 (the meridian itself counts as West). On the AVX/CPWI this
        /// corresponds to pierWest (East) and pierEast (West) respectively.
        /// </summary>
        public static MeridianSide SideOfMeridian(double hourAngleHours) {
            return hourAngleHours < 0.0 ? MeridianSide.East : MeridianSide.West;
        }

        /// <summary>
        /// Orders points so that all points on one meridian side come before all
        /// points on the other, so a German equatorial mount performs at most one
        /// meridian flip across the sequence. Order is preserved within each side
        /// (stable). <paramref name="hourAngleSelector"/> returns each point's hour
        /// angle in hours; <paramref name="first"/> selects which side leads.
        /// </summary>
        public static List<T> OrderByMeridianSide<T>(
                IEnumerable<T> points,
                Func<T, double> hourAngleSelector,
                MeridianSide first = MeridianSide.West) {
            List<T> firstSide = new List<T>();
            List<T> secondSide = new List<T>();
            foreach (T point in points) {
                if (SideOfMeridian(hourAngleSelector(point)) == first) {
                    firstSide.Add(point);
                } else {
                    secondSide.Add(point);
                }
            }
            firstSide.AddRange(secondSide);
            return firstSide;
        }
    }
}
