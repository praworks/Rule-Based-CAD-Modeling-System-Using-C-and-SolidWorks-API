using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;

namespace AICAD.Services
{
    internal static class ModelStateProvider
    {
        public static JObject Capture(ISldWorks swApp, bool emitLogs = false)
        {
            var doc = swApp?.ActiveDoc as IModelDoc2;
            if (doc == null) return null;
            return ModelInspector.InspectModel(doc, emitLogs: emitLogs);
        }
    }
}
