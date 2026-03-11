using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RMAC_Efficiency_Addin.Infrastructure
{
    internal static class DockUiRefresh
    {
        private const uint RDW_INVALIDATE = 0x0001;
        private const uint RDW_UPDATENOW = 0x0100;
        private const uint RDW_ALLCHILDREN = 0x0080;
        private const uint RDW_FRAME = 0x0400;

        [DllImport("user32.dll")]
        private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

        public static void Force(Control? c)
        {
            if (c == null) return;

            // Ensure handle exists
            if (!c.IsHandleCreated)
            {
                try { c.CreateControl(); }
                catch { return; }
            }

            try
            {
                c.SuspendLayout();
                c.PerformLayout();
                c.Invalidate(true);
                c.Update();
            }
            catch { /* ignore */ }
            finally
            {
                try { c.ResumeLayout(true); } catch { }
            }

            try
            {
                RedrawWindow(c.Handle, IntPtr.Zero, IntPtr.Zero,
                    RDW_INVALIDATE | RDW_UPDATENOW | RDW_ALLCHILDREN | RDW_FRAME);
            }
            catch { /* ignore */ }

            // Helps Inventor sometimes “commit” the visual swap in a dock stack.
            try { Application.DoEvents(); } catch { }
        }

        public static void ForceToggleVisible(Inventor.DockableWindow? dock, Control? child, bool visible)
        {
            if (dock == null) return;

            try
            {
                dock.Visible = visible;

                // If we just made it visible, doing a quick toggle often forces Inventor to repaint the host.
                if (visible)
                {
                    dock.Visible = false;
                    dock.Visible = true;
                }
            }
            catch { /* ignore */ }

            Force(child);
        }

        public static void BringToFront(Control? c)
        {
            if (c == null) return;
            if (!c.IsHandleCreated)
            {
                try { c.CreateControl(); } catch { return; }
            }

            try
            {
                // If it's hosted in a dock pane, this often nudges it to repaint/focus correctly
                c.BringToFront();
                c.Focus();
            }
            catch { /* ignore */ }
        }

    }
}
