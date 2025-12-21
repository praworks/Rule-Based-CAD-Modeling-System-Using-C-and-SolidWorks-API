using System;
using System.Windows.Forms.Integration;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace AICAD.UI
{
    /// <summary>
    /// WinForms wrapper control that hosts the WPF TextToCADTaskpaneWpf control.
    /// This allows SolidWorks COM API to work with the WPF control via WindowsFormsHost.
    /// </summary>
    public class TextToCADTaskpaneWrapper : UserControl
    {
        private ElementHost _elementHost;
        private TextToCADTaskpaneWpf _wpfControl;

        public TextToCADTaskpaneWrapper(ISldWorks swApp)
        {
            InitializeComponent(swApp);
        }

        private void InitializeComponent(ISldWorks swApp)
        {
            // Create ElementHost to host the WPF control
            _elementHost = new ElementHost
            {
                Dock = DockStyle.Fill
            };

            // Create the WPF control
            _wpfControl = new TextToCADTaskpaneWpf(swApp);
            _elementHost.Child = _wpfControl;

            // Add ElementHost to this WinForms UserControl
            this.Controls.Add(_elementHost);
        }
    }
}
