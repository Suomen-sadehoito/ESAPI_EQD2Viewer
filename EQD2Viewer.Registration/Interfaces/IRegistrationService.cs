using EQD2Viewer.Core.Data;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EQD2Viewer.Registration.Interfaces
{
    /// <summary>
    /// Performs deformable image registration between two CT volumes.
    /// Implemented by ItkRegistrationService when SimpleITK is available,
    /// or StubRegistrationService when ITK is not loaded.
    /// </summary>
    public interface IRegistrationService
    {
        /// <summary>
        /// Registers moving onto fixed and returns a deformation vector field.
        /// Returns null if registration is unavailable or fails.
        /// </summary>
        Task<DeformationField?> RegisterAsync(
            VolumeData fixed_,
            VolumeData moving,
            IProgress<int>? progress,
            CancellationToken ct);
    }
}
