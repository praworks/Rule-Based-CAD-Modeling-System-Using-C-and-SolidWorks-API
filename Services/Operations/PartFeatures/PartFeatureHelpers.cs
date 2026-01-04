using System;

namespace AICAD.Services.Operations.PartFeatures
{
    internal static class PartFeatureHelpers
    {
        public static double ToMeters(double mm) => mm / 1000.0;
    }
}
