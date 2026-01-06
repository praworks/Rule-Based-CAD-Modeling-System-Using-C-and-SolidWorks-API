using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;

namespace AICAD.Services
{
    public static class UnitManager
    {
        /// <summary>
        /// Sets the document unit system based on a string (e.g., "mm", "inch", "mks").
        /// </summary>
        /// <param name="swApp">The active SolidWorks application instance.</param>
        /// <param name="unitString">The unit string from the LLM (case-insensitive).</param>
        /// <returns>True if successful, False if no document was active.</returns>
        public static bool SetUnits(ISldWorks swApp, string unitString)
        {
            ModelDoc2 swModel = (ModelDoc2)swApp.ActiveDoc;
            if (swModel == null) return false;

            int unitEnum = GetSwUnitSystem(unitString);

            // Set the unit system preference for this specific document
            return swModel.SetUserPreferenceIntegerValue(
                (int)swUserPreferenceIntegerValue_e.swUnitSystem, 
                unitEnum
            );
        }

        private static int GetSwUnitSystem(string unit)
        {
            if (string.IsNullOrWhiteSpace(unit)) 
                return (int)swUnitSystem_e.swUnitSystem_MMGS; // Default

            switch (unit.ToLower().Trim())
            {
                // Millimeters (MMGS)
                case "mm":
                case "mmgs":
                case "millimeter":
                case "millimeters":
                    return (int)swUnitSystem_e.swUnitSystem_MMGS;

                // Inches (IPS)
                case "in":
                case "ips":
                case "inch":
                case "inches":
                    return (int)swUnitSystem_e.swUnitSystem_IPS;

                // Meters (MKS)
                case "m":
                case "mks":
                case "meter":
                case "meters":
                    return (int)swUnitSystem_e.swUnitSystem_MKS;

                // Centimeters (CGS)
                case "cm":
                case "cgs":
                case "centimeter":
                    return (int)swUnitSystem_e.swUnitSystem_CGS;

                default:
                    // Fallback to MMGS if unknown
                    return (int)swUnitSystem_e.swUnitSystem_MMGS; 
            }
        }
    }
}