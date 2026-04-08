#if TEST_HARNESS
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using WF = System.Windows.Forms;

namespace RMAC_Efficiency_Addin.TestHarness
{
    /// <summary>
    /// Modeless prompt window for interactive tests. Shows a small always-on-top window
    /// with a label describing the current operation. The user presses Enter to continue
    /// or Escape to cancel — they never have to click an OK/Cancel button. Because the
    /// keys are read globally via GetKeyState (not via the form's KeyDown event), the
    /// user can freely click in Inventor's viewport during the prompt without losing
    /// focus on the response.
    ///
    /// Use the static <see cref="Confirm"/> method as a one-liner replacement for
    /// MessageBox.Show in tests.
    /// </summary>
    internal sealed class InteractivePrompt : WF.Form
    {
        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        private static bool IsKeyDown(WF.Keys k) => (GetKeyState((int)k) & 0x8000) != 0;

        private readonly WF.Label _label;

        private InteractivePrompt(string title, string message)
        {
            Text = title;
            FormBorderStyle = WF.FormBorderStyle.FixedToolWindow;
            StartPosition = WF.FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;

            // Position in upper-right corner so it doesn't cover the viewport
            var screen = WF.Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
            Width = 460;
            Height = 130;
            Location = new Point(screen.Right - Width - 20, screen.Top + 60);

            _label = new WF.Label
            {
                Dock = WF.DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10F, FontStyle.Regular),
                Padding = new WF.Padding(12),
            };
            SetMessage(message);
            Controls.Add(_label);
        }

        private void SetMessage(string message)
        {
            try { _label.Text = message + "\n\n[Enter] continue   [Esc] cancel"; } catch { }
        }

        /// <summary>
        /// Show a modeless prompt and wait for the user to press Enter (returns true) or
        /// Escape (returns false). Pumps the message loop while waiting so the user can
        /// freely interact with Inventor during the prompt.
        /// </summary>
        public static bool Confirm(string title, string message)
        {
            using var form = new InteractivePrompt(title, message);
            form.Show();
            try { form.BringToFront(); } catch { }

            // Wait for the user to press Enter or Escape (anywhere globally — focus
            // doesn't matter because we use GetKeyState).
            // Edge-detect so a held key only fires once.
            bool prevEnter = IsKeyDown(WF.Keys.Enter) || IsKeyDown(WF.Keys.Return);
            bool prevEsc = IsKeyDown(WF.Keys.Escape);

            bool? result = null;

            while (result == null && !form.IsDisposed)
            {
                try { WF.Application.DoEvents(); } catch { }
                Thread.Sleep(20);

                bool enter = IsKeyDown(WF.Keys.Enter) || IsKeyDown(WF.Keys.Return);
                bool esc = IsKeyDown(WF.Keys.Escape);

                if (enter && !prevEnter) result = true;
                else if (esc && !prevEsc) result = false;

                prevEnter = enter;
                prevEsc = esc;
            }

            try { form.Close(); } catch { }
            return result == true;
        }

        /// <summary>
        /// Show a non-blocking informational toast with a message. The form auto-closes
        /// after <paramref name="durationMs"/> milliseconds, but doesn't block — the
        /// caller continues immediately. Useful for "the test is now doing X" status
        /// updates that don't need confirmation.
        /// </summary>
        public static void Toast(string title, string message, int durationMs)
        {
            var form = new InteractivePrompt(title, message);
            form.Show();
            try { form.BringToFront(); } catch { }

            // Schedule a close on a background thread without blocking the caller.
            // The form is owned by the calling thread; we marshal the close back via
            // BeginInvoke so the WinForms thread affinity holds.
            var t = new Thread(() =>
            {
                Thread.Sleep(durationMs);
                try
                {
                    if (!form.IsDisposed)
                    {
                        form.BeginInvoke((WF.MethodInvoker)(() =>
                        {
                            try { form.Close(); form.Dispose(); } catch { }
                        }));
                    }
                }
                catch { }
            }) { IsBackground = true };
            t.Start();
        }
    }
}
#endif
