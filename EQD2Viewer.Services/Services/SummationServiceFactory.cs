using EQD2Viewer.Core.Interfaces;
using EQD2Viewer.Core.Data;
using System.Collections.Generic;

namespace EQD2Viewer.Services
{
    /// <summary>
    /// Factory that creates <see cref="SummationService"/> instances.
    /// </summary>
    public class SummationServiceFactory : ISummationServiceFactory
    {
        public ISummationService Create(
            VolumeData referenceCtImage,
            ISummationDataLoader dataLoader,
            List<RegistrationData> registrations)
        {
            return new SummationService(referenceCtImage, dataLoader, registrations);
        }
    }
}
