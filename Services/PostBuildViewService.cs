using System;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace AICAD.Services
{
    internal static class PostBuildViewService
    {
        internal const string PostBuildViewModeKey = "PostBuildViewMode";
        internal const string NoneMode = "none";
        internal const string IsometricMode = "isometric";
        internal const string TopMode = "top";
        internal const string FrontMode = "front";
        internal const string RightMode = "right";
        internal const string LeftMode = "left";

        internal static string GetConfiguredMode()
        {
            return NormalizeMode(SettingsManager.GetString(PostBuildViewModeKey, IsometricMode));
        }

        internal static bool ApplyConfiguredView(IModelDoc2 model)
        {
            return ApplyView(model, GetConfiguredMode());
        }

        internal static string NormalizeMode(string mode)
        {
            var normalized = (mode ?? string.Empty).Trim().ToLowerInvariant();
            switch (normalized)
            {
                case IsometricMode:
                case TopMode:
                case FrontMode:
                case RightMode:
                case LeftMode:
                case NoneMode:
                    return normalized;
                default:
                    return IsometricMode;
            }
        }

        internal static bool ApplyView(IModelDoc2 model, string mode)
        {
            if (model == null) return false;

            var normalized = NormalizeMode(mode);
            if (normalized == NoneMode) return true;

            try
            {
                var view = ResolveView(normalized);
                model.ShowNamedView2(view.ViewName, (int)view.ViewId);
                model.ViewZoomtofit2();
                model.GraphicsRedraw2();
                return true;
            }
            catch (Exception ex)
            {
                AddinStatusLogger.Error(nameof(PostBuildViewService), $"Failed to apply post-build view '{normalized}'", ex);
                return false;
            }
        }

        private static (string ViewName, swStandardViews_e ViewId) ResolveView(string mode)
        {
            switch (NormalizeMode(mode))
            {
                case TopMode:
                    return ("*Top", swStandardViews_e.swTopView);
                case FrontMode:
                    return ("*Front", swStandardViews_e.swFrontView);
                case RightMode:
                    return ("*Right", swStandardViews_e.swRightView);
                case LeftMode:
                    return ("*Left", swStandardViews_e.swLeftView);
                case NoneMode:
                    return (string.Empty, swStandardViews_e.swIsometricView);
                default:
                    return ("*Isometric", swStandardViews_e.swIsometricView);
            }
        }
    }
}
