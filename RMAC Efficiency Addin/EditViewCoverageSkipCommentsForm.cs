using System;
using System.Linq;
using System.Windows.Forms;
using Krypton.Toolkit;

namespace RMAC_Efficiency_Addin
{
    internal sealed class EditViewCoverageSkipCommentsForm : KryptonForm
    {
        private static readonly string[] DefaultSkipComments = new[] { "FASTENERS", "FASTENER" };

        private readonly KryptonTextBox _txtSkip = new()
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill
        };

        private readonly KryptonButton _btnReset = new() { Text = "Reset defaults" };
        private readonly KryptonButton _btnSave = new() { Text = "Save" };
        private readonly KryptonButton _btnCancel = new() { Text = "Cancel" };

        private readonly string _path;
        private readonly CommentOptions _options;

        public EditViewCoverageSkipCommentsForm(string configPath, CommentOptions options)
        {
            _path = configPath;
            _options = options;

            Text = "Edit View Coverage Skip Comments";
            Width = 720;
            Height = 520;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = true;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(10)
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

            var lbl = new KryptonLabel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Text = "Comments in this list will be ignored by 'Check Views Coverage' (one per line)."
            };

            root.Controls.Add(lbl, 0, 0);
            root.Controls.Add(_txtSkip, 0, 1);

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };

            _btnSave.Width = 90;
            _btnCancel.Width = 90;

            bottom.Controls.Add(_btnSave);
            bottom.Controls.Add(_btnCancel);
            bottom.Controls.Add(_btnReset);

            root.Controls.Add(bottom, 0, 2);

            Controls.Add(root);

            // Init text
            var list = _options.SkipCommentsForViewCoverage;
            if (list == null || list.Count == 0)
                list = DefaultSkipComments.ToList();

            _txtSkip.Text = string.Join(Environment.NewLine, list);

            // Events
            _btnCancel.Click += (_, __) => { DialogResult = DialogResult.Cancel; Close(); };
            _btnSave.Click += (_, __) => Save();
            _btnReset.Click += (_, __) =>
            {
                _txtSkip.Text = string.Join(Environment.NewLine, DefaultSkipComments);
            };
        }

        private void Save()
        {
            try
            {
                _options.SkipCommentsForViewCoverage = _txtSkip.Lines
                    .Select(l => (l ?? "").Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToList();

                _options.Save(_path);

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
