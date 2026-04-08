using System;
using Inventor;

namespace RMAC_Efficiency_Addin.Infrastructure
{
    /// <summary>
    /// Helpers for running Inventor work inside a transaction with ScreenUpdating suppressed.
    /// Extracted from StandardAddInServer so Core classes and tests can use the same plumbing.
    /// </summary>
    internal static class InventorTransaction
    {
        /// <summary>
        /// Runs <paramref name="action"/> inside a named transaction on the given document, with
        /// ScreenUpdating disabled. Aborts the transaction on exception and always restores
        /// ScreenUpdating. If <paramref name="doc"/> is null, falls back to <c>app.ActiveDocument</c>.
        /// The parameter is typed as <see cref="_Document"/> so any specific document interface
        /// (PartDocument, AssemblyDocument, DrawingDocument) can be passed directly.
        /// </summary>
        public static void RunFast(Application app, _Document? doc, string name, Action action)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (action == null) throw new ArgumentNullException(nameof(action));

            var target = doc ?? (_Document)app.ActiveDocument;
            bool oldSU = app.ScreenUpdating;
            Transaction? tx = null;

            try
            {
                app.ScreenUpdating = false;
                tx = app.TransactionManager.StartTransaction(target, name);

                action();

                tx.End();
            }
            catch
            {
                try { tx?.Abort(); } catch { }
                throw;
            }
            finally
            {
                app.ScreenUpdating = oldSU;
                try { if (app.ActiveDocument != null) app.ActiveView.Update(); } catch { }
            }
        }
    }
}
