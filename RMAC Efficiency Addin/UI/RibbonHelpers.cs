using Inventor;

namespace RMAC_Efficiency_Addin.UI
{
    internal static class RibbonHelpers
    {
        internal static void AddButtonSafe(RibbonPanel panel, ButtonDefinition? btn, bool insertBefore)
        {
            if (panel == null || btn == null) return;

            try
            {
                dynamic controls = panel.CommandControls;
                dynamic cc = controls.AddButton(btn, insertBefore, false, "");

                try { cc.ShowText = true; } catch { }
                try { cc.IsLarge = false; } catch { }
                try { cc.UseLargeIcon = false; } catch { }
            }
            catch
            {
                try
                {
                    var cc = panel.CommandControls.AddButton(btn, insertBefore);
                    try
                    {
                        dynamic d = cc;
                        try { d.ShowText = true; } catch { }
                        try { d.IsLarge = false; } catch { }
                    }
                    catch { }
                }
                catch { }
            }
        }
    }
}
