using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace RMAC_Efficiency_Addin
{
    /// <summary>
    /// Dock UI with two scrollable lists:
    /// - Pinned layout parts (top) - later you will populate from JSON
    /// - Derived parts in the active part (bottom) - populated by controller
    /// </summary>
    public sealed class LayoutsDockPane : UserControl
    {
        private readonly ListBox _lstPinned;
        private readonly ListBox _lstDerived;

        public LayoutsDockPane()
        {
            Dock = DockStyle.Fill;

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 6,
                Panel1MinSize = 120,
                Panel2MinSize = 120,
                SplitterDistance = 170
            };

            // --- Top: Pinned layouts (scrolls automatically)
            var grpPinned = new GroupBox
            {
                Dock = DockStyle.Fill,
                Text = "Pinned Layout Parts"
            };

            _lstPinned = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false // better resizing behaviour
            };

            grpPinned.Controls.Add(_lstPinned);
            split.Panel1.Controls.Add(grpPinned);

            // --- Bottom: Derived parts in active document (scrolls automatically)
            var grpDerived = new GroupBox
            {
                Dock = DockStyle.Fill,
                Text = "Derived in Active Part"
            };

            _lstDerived = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false
            };

            grpDerived.Controls.Add(_lstDerived);
            split.Panel2.Controls.Add(grpDerived);

            Controls.Add(split);

            // Placeholder so it looks intentional before pinning is implemented
            SetPinnedItems(new[] { "(No pinned layouts yet)" });
        }

        public void SetPinnedItems(IEnumerable<string> items)
        {
            SetList(_lstPinned, items);
        }

        public void SetDerivedItems(IEnumerable<string> items)
        {
            SetList(_lstDerived, items);
        }

        private static void SetList(ListBox list, IEnumerable<string> items)
        {
            var arr = (items ?? Enumerable.Empty<string>()).ToArray();
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                if (arr.Length == 0)
                    list.Items.Add("(None)");
                else
                    list.Items.AddRange(arr);
            }
            finally
            {
                list.EndUpdate();
            }
        }
    }
}
