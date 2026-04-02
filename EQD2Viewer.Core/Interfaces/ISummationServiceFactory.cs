using EQD2Viewer.Core.Data;
using System.Collections.Generic;

namespace EQD2Viewer.Core.Interfaces
{
    /// <summary>
    /// Factory for creating per-session <see cref="ISummationService"/> instances.
    /// 
    /// Each summation session gets its own service instance that holds per-plan
    /// physical dose arrays and the EQD2 display sum. The factory encapsulates
    /// the dependency on concrete <c>SummationService</c> so that ViewModels
    /// only depend on interfaces.
    /// </summary>
    public interface ISummationServiceFactory
    {
        /// <summary>
        /// Creates a new summation service for the given reference CT and registrations.
        /// </summary>
        /// <param name="referenceCtImage">The reference CT image (provides grid geometry).</param>
        /// <param name="dataLoader">Loader for plan dose data (ESAPI or fixture-based).</param>
        /// <param name="registrations">Available registrations for cross-plan mapping.</param>
        ISummationService Create(
              VolumeData referenceCtImage,
           ISummationDataLoader dataLoader,
           List<RegistrationData> registrations);
    }
}
