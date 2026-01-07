using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;

namespace AICAD.Services.Operations
{
    /// <summary>
    /// Registry of all available operation handlers.
    /// Handlers are organized by category: Sketching, Part Features, Utilities.
    /// </summary>
    public class OperationRegistry
    {
        private readonly Dictionary<string, IOperationHandler> _handlers = 
            new Dictionary<string, IOperationHandler>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Register an operation handler
        /// </summary>
        public OperationRegistry Register(string operationName, IOperationHandler handler)
        {
            if (string.IsNullOrWhiteSpace(operationName))
                throw new ArgumentException("Operation name cannot be empty", nameof(operationName));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            var key = operationName.Trim().ToLowerInvariant();
            _handlers[key] = handler;
            return this;
        }

        /// <summary>
        /// Get a handler for the given operation
        /// </summary>
        public IOperationHandler Get(string operationName)
        {
            if (string.IsNullOrWhiteSpace(operationName))
                return null;

            var key = operationName.Trim().ToLowerInvariant();
            return _handlers.TryGetValue(key, out var handler) ? handler : null;
        }

        /// <summary>
        /// Check if an operation is registered
        /// </summary>
        public bool Contains(string operationName)
        {
            if (string.IsNullOrWhiteSpace(operationName)) return false;
            var key = operationName.Trim().ToLowerInvariant();
            return _handlers.ContainsKey(key);
        }

        /// <summary>
        /// Get all registered operation names
        /// </summary>
        public IEnumerable<string> GetRegisteredOperations()
        {
            return _handlers.Keys;
        }

        /// <summary>
        /// Create the default registry with all standard operations
        /// </summary>
        public static OperationRegistry CreateDefault()
        {
            var registry = new OperationRegistry();

            // ===== UTILITIES =====
            registry.Register("new_part", new Utilities.NewPartHandler());
            registry.Register("select_plane", new Utilities.SelectPlaneHandler());
            // Prefer PartFeatures.FaceHandler which implements more robust selection fallbacks
            registry.Register("select_face", new PartFeatures.FaceHandler());
            registry.Register("set_units", new Utilities.SetUnitsHandler());
            registry.Register("set_document_units", new Utilities.SetUnitsHandler());
            registry.Register("set_unit", new Utilities.SetUnitsHandler());
            registry.Register("set_material", new Utilities.SetMaterialHandler());
            registry.Register("description", new Utilities.DescriptionHandler());
            registry.Register("zoom_to_fit", new Utilities.ZoomToFitHandler());

            // ===== SKETCHING =====
            registry.Register("sketch_begin", new Sketching.SketchBeginHandler());
            registry.Register("sketch_end", new Sketching.SketchEndHandler());
            registry.Register("rectangle_center", new Sketching.RectangleCenterHandler());
            registry.Register("circle_center", new Sketching.CircleCenterHandler());
            registry.Register("line", new Sketching.LineHandler());
            registry.Register("arc", new Sketching.ArcHandler());
            registry.Register("dimension", new Sketching.DimensionHandler());
            // LLMs may emit alternative names — accept common aliases for robustness
            registry.Register("auto_dimension", new Sketching.DimensionHandler());
            registry.Register("auto-dimension", new Sketching.DimensionHandler());
            registry.Register("autodimension", new Sketching.DimensionHandler());
            registry.Register("constraint", new Sketching.ConstraintHandler());

            // ===== PART FEATURES =====
            registry.Register("extrude", new PartFeatures.ExtrudeBossHandler());
            registry.Register("extrude_cut", new PartFeatures.ExtrudeCutHandler());
            registry.Register("extrude-cut", new PartFeatures.ExtrudeCutHandler());
            registry.Register("revolve", new PartFeatures.RevolveHandler());
            registry.Register("sweep", new PartFeatures.SweepHandler());
            registry.Register("loft", new PartFeatures.LoftHandler());
            registry.Register("fillet", new PartFeatures.FilletHandler());
            registry.Register("chamfer", new PartFeatures.ChamferHandler());
            registry.Register("hole", new PartFeatures.HoleHandler());
            registry.Register("pocket", new PartFeatures.PocketHandler());

            return registry;
        }
    }
}
