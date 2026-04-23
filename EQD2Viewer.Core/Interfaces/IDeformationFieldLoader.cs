using EQD2Viewer.Core.Data;

namespace EQD2Viewer.Core.Interfaces
{
    /// <summary>
    /// Loads a deformation vector field from a file path.
    /// Implemented by MhaReader in EQD2Viewer.Registration.
    /// </summary>
    public interface IDeformationFieldLoader
    {
        /// <summary>Loads a DVF from the given file path. Returns null on failure.</summary>
        DeformationField? Load(string path);
    }
}
