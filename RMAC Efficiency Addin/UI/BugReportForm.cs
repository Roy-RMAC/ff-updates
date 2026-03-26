using System;
using System.Drawing;
using System.Windows.Forms;
using Krypton.Toolkit;
using RMAC_Efficiency_Addin.Updates;

namespace RMAC_Efficiency_Addin.UI
{
    internal sealed class BugReportForm : KryptonForm
    {
        private readonly KryptonManager _kryptonManager = new();
        private readonly KryptonTextBox _txtTitle;
        private readonly KryptonTextBox _txtDescription;
        private readonly KryptonTextBox _txtSteps;
        private readonly KryptonTextBox _txtSystemInfo;
        private readonly KryptonButton _btnSubmit;
        private readonly KryptonButton _btnCancel;

        public BugReportForm(object? inventorApp = null)
        {
            _kryptonManager.GlobalPaletteMode = PaletteMode.Microsoft365Black;

            Text = "Report a Problem";
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(500, 480);
            MinimumSize = new Size(400, 400);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 9,
                Padding = new Padding(16)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 0: title label
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 1: title input
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 2: desc label
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));    // 3: desc input
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 4: steps label
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 30));    // 5: steps input
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 6: sysinfo label
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25));    // 7: sysinfo box
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 8: buttons

            // Title
            layout.Controls.Add(MakeLabel("Title *"), 0, 0);

            _txtTitle = new KryptonTextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 8)
            };
            layout.Controls.Add(_txtTitle, 0, 1);

            // Description
            layout.Controls.Add(MakeLabel("What happened?"), 0, 2);

            _txtDescription = new KryptonTextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 8)
            };
            layout.Controls.Add(_txtDescription, 0, 3);

            // Steps
            layout.Controls.Add(MakeLabel("Steps to reproduce (optional)"), 0, 4);

            _txtSteps = new KryptonTextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 8)
            };
            layout.Controls.Add(_txtSteps, 0, 5);

            // System info
            layout.Controls.Add(MakeLabel("System information (sent with report)"), 0, 6);

            _txtSystemInfo = new KryptonTextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Text = BugReportService.GatherSystemInfo(inventorApp),
                Margin = new Padding(0, 0, 0, 12)
            };
            layout.Controls.Add(_txtSystemInfo, 0, 7);

            // Buttons
            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0)
            };

            _btnSubmit = new KryptonButton
            {
                Text = "Submit",
                AutoSize = true,
                Enabled = false,
                Margin = new Padding(4, 0, 0, 0)
            };
            _btnSubmit.Click += BtnSubmit_Click;

            _btnCancel = new KryptonButton
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                AutoSize = true,
                Margin = new Padding(0)
            };

            btnPanel.Controls.Add(_btnSubmit);
            btnPanel.Controls.Add(_btnCancel);
            layout.Controls.Add(btnPanel, 0, 8);

            Controls.Add(layout);
            AcceptButton = _btnSubmit;
            CancelButton = _btnCancel;

            _txtTitle.TextChanged += (_, __) =>
                _btnSubmit.Enabled = !string.IsNullOrWhiteSpace(_txtTitle.Text);
        }

        private async void BtnSubmit_Click(object? sender, EventArgs e)
        {
            _btnSubmit.Enabled = false;
            _btnSubmit.Text = "Submitting...";
            _btnCancel.Enabled = false;

            try
            {
                var (success, issueUrl, error) = await BugReportService.SubmitAsync(
                    _txtTitle.Text.Trim(),
                    _txtDescription.Text.Trim(),
                    _txtSteps.Text.Trim(),
                    _txtSystemInfo.Text);

                if (success)
                {
                    MessageBox.Show(this,
                        "Thank you! Your report has been submitted successfully.",
                        "Report Submitted", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    DialogResult = DialogResult.OK;
                    Close();
                }
                else
                {
                    MessageBox.Show(this,
                        $"Failed to submit report:\n{error}",
                        "Submission Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Failed to submit report:\n{ex.Message}",
                    "Submission Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _btnSubmit.Text = "Submit";
                _btnSubmit.Enabled = !string.IsNullOrWhiteSpace(_txtTitle.Text);
                _btnCancel.Enabled = true;
            }
        }

        private static Label MakeLabel(string text) => new()
        {
            Text = text,
            ForeColor = Color.FromArgb(180, 180, 180),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4)
        };
    }
}
