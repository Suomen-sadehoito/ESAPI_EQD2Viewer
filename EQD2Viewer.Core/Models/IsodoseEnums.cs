namespace EQD2Viewer.Core.Models
{
    /// <summary>
    /// Determines how isodose thresholds are interpreted for display.
    /// </summary>
    public enum IsodoseMode
    {
        /// <summary>
        /// Thresholds as percentage of a reference dose (Eclipse-style).
        /// Typical for single-plan viewing where prescription dose is the reference.
        /// Example: 95% of 50 Gy = 47.5 Gy threshold.
        /// </summary>
        Relative,

        /// <summary>
        /// Thresholds as absolute Gy values (no reference dose needed).
        /// Used for EQD2 summation where clinical tolerances are defined in absolute Gy
        /// (e.g., spinal cord 45 Gy EQD2, brainstem 50 Gy EQD2).
        /// </summary>
        Absolute
    }

    /// <summary>
    /// Display unit for isodose level labels within <see cref="IsodoseMode.Relative"/> mode.
    /// Controls whether the label shows "95%" or "47.5 Gy".
    /// </summary>
    public enum IsodoseUnit
    {
        /// <summary>Show as percentage of reference dose (e.g., "95%").</summary>
        Percent,
        /// <summary>Show as absolute Gy computed from fraction * reference (e.g., "47.5 Gy").</summary>
        Gy
    }
}
