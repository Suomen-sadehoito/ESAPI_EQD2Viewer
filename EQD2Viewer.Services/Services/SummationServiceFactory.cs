using EQD2Viewer.Core.Interfaces;
using EQD2Viewer.Core.Data;
using System.Collections.Generic;

namespace EQD2Viewer.Services
{
    /// <summary>
    /// Factory that creates <see cref="SummationService"/> instances.
    /// Optionally holds an <see cref="IDeformationFieldLoader"/> for DIR support.
    /// </summary>
    public class SummationServiceFactory : ISummationServiceFactory
    {
        private readonly IDeformationFieldLoader? _dfLoader;

        public SummationServiceFactory(IDeformationFieldLoader? dfLoader = null)
        {
            _dfLoader = dfLoader;
        }

        public ISummationService Create(
            VolumeData referenceCtImage,
            ISummationDataLoader dataLoader,
            List<RegistrationData> registrations)
        {
            return new SummationService(referenceCtImage, dataLoader, registrations, _dfLoader);
        }
    }
}
