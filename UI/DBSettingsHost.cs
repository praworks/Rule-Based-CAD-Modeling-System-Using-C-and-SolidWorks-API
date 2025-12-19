using System;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

namespace AICAD.UI
{
    public class DBSettingsHost : UserControl
    {
        private ElementHost _host;
        private DBSettingsControl _wpfControl;

        public DBSettingsHost()
        {
            Dock = DockStyle.Fill;
            _host = new ElementHost { Dock = DockStyle.Fill };
            _wpfControl = new DBSettingsControl();
            _host.Child = _wpfControl;
            Controls.Add(_host);
        }

    protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_host != null)
                {
                    _host.Child = null;
                    _host.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
