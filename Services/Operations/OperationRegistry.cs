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

            _handlers[operationName.ToLowerInvariant()] = handler;
            return this;
        }

        /// <summary>
        /// Get a handler for the given operation
        /// </summary>
        public IOperationHandler Get(string operationName)
        {
            if (string.IsNullOrWhiteSpace(operationName))
                return null;

            return _handlers.TryGetValue(operationName.ToLowerInvariant(), out var handler) ? handler : null;
        }

        /// <summary>
        /// Check if an operation is registered
        /// </summary>
        public bool Contains(string operationName)
        {
            return !string.IsNullOrWhiteSpace(operationName) && 
                   _handlers.ContainsKey(operationName.ToLowerInvariant());
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
            registry.Register("select_face", new Utilities.SelectFaceHandler());
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
            registry.Register("constraint", new Sketching.ConstraintHandler());

            // ===== PART FEATURES =====
            registry.Register("extrude", new PartFeatures.ExtrudeHandler());
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
