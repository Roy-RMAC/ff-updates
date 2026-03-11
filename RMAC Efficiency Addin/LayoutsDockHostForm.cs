using System.Windows.Forms;

namespace RMAC_Efficiency_Addin
{
    /// <summary>
    /// Hosted inside Inventor DockableWindow. Keep as a normal (TopLevel) modeless form.
    /// We will re-parent it to the DockableWindow HWND.
    /// </summary>
    public sealed class LayoutsDockHostForm : Form
    {
        public LayoutsDockPane Pane { get; }

        public LayoutsDockHostForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            ControlBox = false;

            Pane = new LayoutsDockPane { Dock = DockStyle.Fill };
            Controls.Add(Pane);
        }
    }
}
