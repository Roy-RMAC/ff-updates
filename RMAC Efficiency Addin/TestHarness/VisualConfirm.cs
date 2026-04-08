#if TEST_HARNESS
using Inventor;
using WF = System.Windows.Forms;

namespace RMAC_Efficiency_Addin.TestHarness
{
    /// <summary>
    /// Interactive checkpoint helper. ALWAYS pauses when called — refreshes the view so
    /// the user sees the current document state, then shows an OK/Cancel MessageBox.
    /// Cancel throws to fail the test.
    ///
    /// Usage pattern: scatter <c>VisualConfirm.Step(...)</c> calls inside a test you're
    /// actively authoring or debugging. Comment them out (or delete them) once that test
    /// is verified and you want it to run silently in the automated suite. Tests with no
    /// active <c>Step()</c> calls run fully unattended.
    ///
    /// When a test contains active <c>Step()</c> calls, also pass <c>visible: true</c>
    /// to <see cref="FixtureWorkspace.OpenFile"/> so the document actually appears in
    /// the viewport — otherwise you'll be staring at a MessageBox with nothing to verify.
    /// </summary>
    internal static class VisualConfirm
    {
        /// <summary>
        /// Pause the test and ask the user to visually verify the document state.
        /// </summary>
        public static void Step(Application app, _Document? doc, string message)
        {
            // Force a view refresh so the user sees the latest mutations
            try { doc?.Update(); } catch { }
            try { app.ActiveView?.Update(); } catch { }
            try { WF.Application.DoEvents(); } catch { }

            var result = WF.MessageBox.Show(
                message + "\n\nClick OK if the viewport looks correct.\nClick Cancel to fail the test.",
                "Test Checkpoint",
                WF.MessageBoxButtons.OKCancel,
                WF.MessageBoxIcon.Information);

            if (result != WF.DialogResult.OK)
            {
                throw new TestAssertException(
                    $"User cancelled at checkpoint: {message}");
            }
        }
    }
}
#endif
