#if TEST_HARNESS
using System;
using Inventor;
using RMAC_Efficiency_Addin.Infrastructure;

namespace RMAC_Efficiency_Addin.TestHarness.TestCases
{
    // Tests for InventorTransaction.RunFast.
    //
    // The first fixture-based tests in the harness — they prove the entire workspace
    // pipeline works end-to-end:
    //   - source dataset copied to working location
    //   - file opens from working copy
    //   - transaction wraps a real document
    //   - ScreenUpdating restored after the action
    //   - exception path also restores ScreenUpdating
    //   - cleanup closes the doc and deletes the working copy
    //
    // Target file: 3D CAD\P-01.ipt — any small part is fine, we just need a valid Document.
    // The path constant lives in InventorTransactionTestHelpers below.

    internal sealed class RunFast_OpensFixturePart : IFixtureTestCase
    {
        public string Name => nameof(RunFast_OpensFixturePart);
        public void Run(Application app)
        {
            // The smallest possible smoke test: prove we can open a real file from the workspace.
            var doc = FixtureWorkspace.OpenFile(app, InventorTransactionTestHelpers.FixturePart);
            TestAssert.NotNull(doc);
            TestAssert.True(doc.DocumentType == DocumentTypeEnum.kPartDocumentObject,
                $"Expected a part document, got {doc.DocumentType}.");
        }
    }

    internal sealed class RunFast_ActionIsInvoked : IFixtureTestCase
    {
        public string Name => nameof(RunFast_ActionIsInvoked);
        public void Run(Application app)
        {
            var doc = FixtureWorkspace.OpenFile(app, InventorTransactionTestHelpers.FixturePart);

            bool ran = false;
            InventorTransaction.RunFast(app, (_Document)doc, "smoke", () => { ran = true; });

            TestAssert.True(ran, "Action passed to RunFast was not invoked.");
        }
    }

    internal sealed class RunFast_ScreenUpdatingFalseDuringAction : IFixtureTestCase
    {
        public string Name => nameof(RunFast_ScreenUpdatingFalseDuringAction);
        public void Run(Application app)
        {
            var doc = FixtureWorkspace.OpenFile(app, InventorTransactionTestHelpers.FixturePart);

            bool? observed = null;
            InventorTransaction.RunFast(app, (_Document)doc, "screen-updating-check", () =>
            {
                try { observed = app.ScreenUpdating; } catch { observed = null; }
            });

            TestAssert.NotNull(observed);
            TestAssert.False(observed!.Value,
                "ScreenUpdating should be false inside the RunFast action.");
        }
    }

    internal sealed class RunFast_ScreenUpdatingRestoredAfterAction : IFixtureTestCase
    {
        public string Name => nameof(RunFast_ScreenUpdatingRestoredAfterAction);
        public void Run(Application app)
        {
            var doc = FixtureWorkspace.OpenFile(app, InventorTransactionTestHelpers.FixturePart);

            bool prior = true;
            try { prior = app.ScreenUpdating; } catch { }

            // Force it on for the test so we can verify it gets restored
            try { app.ScreenUpdating = true; } catch { }

            InventorTransaction.RunFast(app, (_Document)doc, "restore-check", () => { /* no-op */ });

            bool after = false;
            try { after = app.ScreenUpdating; } catch { }
            TestAssert.True(after, "ScreenUpdating should be true after RunFast (restored to prior value).");

            // Be polite — restore whatever it was before this test ran
            try { app.ScreenUpdating = prior; } catch { }
        }
    }

    internal sealed class RunFast_ExceptionStillRestoresScreenUpdating : IFixtureTestCase
    {
        public string Name => nameof(RunFast_ExceptionStillRestoresScreenUpdating);
        public void Run(Application app)
        {
            var doc = FixtureWorkspace.OpenFile(app, InventorTransactionTestHelpers.FixturePart);

            bool prior = true;
            try { prior = app.ScreenUpdating; } catch { }
            try { app.ScreenUpdating = true; } catch { }

            bool threwAsExpected = false;
            try
            {
                InventorTransaction.RunFast(app, (_Document)doc, "throw-test", () =>
                {
                    throw new InvalidOperationException("intentional");
                });
            }
            catch (InvalidOperationException ex) when (ex.Message == "intentional")
            {
                threwAsExpected = true;
            }

            TestAssert.True(threwAsExpected,
                "RunFast should propagate the action's exception unchanged.");

            bool after = false;
            try { after = app.ScreenUpdating; } catch { }
            TestAssert.True(after,
                "ScreenUpdating should be restored even when the action throws.");

            try { app.ScreenUpdating = prior; } catch { }
        }
    }

    internal static class InventorTransactionTestHelpers
    {
        // Path relative to the working project root. Picked because P-01.ipt is small,
        // exists at the top of 3D CAD/, and has no special requirements.
        public const string FixturePart = @"3D CAD\P-01.ipt";
    }
}
#endif
