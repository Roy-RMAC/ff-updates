using System;
using System.Linq;
using System.Windows.Forms;
using Krypton.Toolkit;

namespace RMAC_Efficiency_Addin
{
    internal sealed class EditCommentListsForm : KryptonForm
    {
        private readonly KryptonTextBox _txtPart = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };
        private readonly KryptonTextBox _txtAssy = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };
        private readonly KryptonTextBox _txtInsep = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };

        private readonly KryptonButton _btnSave = new() { Text = "Save" };
        private readonly KryptonButton _btnCancel = new() { Text = "Cancel" };

        private readonly string _path;
        private readonly CommentOptions _options;

        public EditCommentListsForm(string configPath, CommentOptions options)
        {
            _path = configPath;
            _options = options;

            Text = "Edit Comment Lists";
            Width = 900;
            Height = 650;
            StartPosition = FormStartPosition.CenterScreen;

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 2
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            grid.Controls.Add(new KryptonLabel { Text = "Part comments (one per line)", Dock = DockStyle.Fill }, 0, 0);
            grid.Controls.Add(new KryptonLabel { Text = "Assembly comments (one per line)", Dock = DockStyle.Fill }, 1, 0);
            grid.Controls.Add(new KryptonLabel { Text = "Treat as inseparable when assembly is…", Dock = DockStyle.Fill }, 2, 0);

            _txtPart.Dock = DockStyle.Fill;
            _txtAssy.Dock = DockStyle.Fill;
            _txtInsep.Dock = DockStyle.Fill;

            grid.Controls.Add(_txtPart, 0, 1);
            grid.Controls.Add(_txtAssy, 1, 1);
            grid.Controls.Add(_txtInsep, 2, 1);

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 45,
                FlowDirection = FlowDirection.RightToLeft
            };
            bottom.Controls.Add(_btnSave);
            bottom.Controls.Add(_btnCancel);

            Controls.Add(grid);
            Controls.Add(bottom);

            _txtPart.Text = string.Join(Environment.NewLine, _options.Part);
            _txtAssy.Text = string.Join(Environment.NewLine, _options.Assembly);
            _txtInsep.Text = string.Join(Environment.NewLine, _options.TreatAsInseparableWhenAssemblyIs);

            _btnCancel.Click += (_, __) => { DialogResult = DialogResult.Cancel; Close(); };
            _btnSave.Click += (_, __) => Save();
        }

        private void Save()
        {
            _options.Part = _txtPart.Lines.Select(l => l.Trim()).ToList();
            _options.Assembly = _txtAssy.Lines.Select(l => l.Trim()).ToList();
            _options.TreatAsInseparableWhenAssemblyIs = _txtInsep.Lines.Select(l => l.Trim()).ToList();

            try
            {
                _options.Save(_path);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Save failed");
            }
        }
    }
}
