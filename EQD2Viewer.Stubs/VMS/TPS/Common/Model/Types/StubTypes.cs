// ═══════════════════════════════════════════════════════════
// ESAPI TYPE STUBS — Compilation-only, no Varian code
//
// These are minimal type definitions matching the public API
// signatures used by EQD2Viewer.App. They contain NO
// proprietary logic — only the type shapes needed to compile.
//
// Used by:
//   - GitHub Actions CI (where real ESAPI DLLs are unavailable)
//   - Test project compilation
//
// NOT used when building for actual Eclipse deployment
// (the real DLLs are referenced via lib/ folder locally).
// ═══════════════════════════════════════════════════════════

namespace VMS.TPS.Common.Model.Types
{
    public struct VVector
    {
        public double x, y, z;
        public VVector(double x, double y, double z)
        { this.x = x; this.y = y; this.z = z; }

        public static VVector operator +(VVector a, VVector b)
            => new VVector(a.x + b.x, a.y + b.y, a.z + b.z);
        public static VVector operator -(VVector a, VVector b)
            => new VVector(a.x - b.x, a.y - b.y, a.z - b.z);
        public static VVector operator *(VVector v, double s)
            => new VVector(v.x * s, v.y * s, v.z * s);
    }

    public struct DoseValue
    {
        public enum DoseUnit { Unknown, Gy, cGy, Percent }
        public double Dose { get; set; }
        public DoseUnit Unit { get; set; }
        public DoseValue(double dose, DoseUnit unit)
        { Dose = dose; Unit = unit; }
    }

    public struct DVHPoint
    {
        public DoseValue DoseValue { get; set; }
        public double Volume { get; set; }
        public string VolumeUnit { get; set; }
        public DVHPoint(DoseValue dose, double volume, string volumeUnit)
        { DoseValue = dose; Volume = volume; VolumeUnit = volumeUnit; }
    }

    public enum VolumePresentation { AbsoluteCm3, Relative }
    public enum DoseValuePresentation { Absolute, Relative }
}