using System;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;

namespace AICAD.Services.Operations
{
    /// <summary>
    /// Interface for all CAD operation handlers.
    /// Each handler implements a specific operation (extrude, fillet, hole, etc.)
    /// </summary>
    public interface IOperationHandler
    {
        /// <summary>
        /// Execute this operation on the given model.
        /// </summary>
        /// <param name="step">Step definition with parameters (e.g., {"op": "fillet", "radius": 2})</param>
        /// <param name="model">Active SolidWorks model document</param>
        /// <param name="sketchMgr">Sketch manager (may be null for non-sketch operations)</param>
        /// <param name="featMgr">Feature manager</param>
        /// <param name="inSketch">Whether currently in sketch editing mode</param>
        /// <returns>Operation result with success status and updated state</returns>
        OperationResult Execute(
            JObject step,
            IModelDoc2 model,
            ISketchManager sketchMgr,
            IFeatureManager featMgr,
            bool inSketch);
    }

    /// <summary>
    /// Result of executing an operation.
    /// </summary>
    public class OperationResult
    {
        /// <summary>
        /// True if operation succeeded
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if operation failed
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Whether we're still in sketch mode after this operation
        /// </summary>
        public bool InSketch { get; set; }

        /// <summary>
        /// Additional data (feature reference, selection info, etc.)
        /// </summary>
        public object Data { get; set; }

        public OperationResult()
        {
            Success = false;
            ErrorMessage = null;
            Data = null;
        }

        public static OperationResult CreateFailure(string error) 
            => new OperationResult { Success = false, ErrorMessage = error };

        public static OperationResult CreateSuccess(bool stillInSketch = false, object data = null)
            => new OperationResult { Success = true, InSketch = stillInSketch, Data = data };
    }
}
