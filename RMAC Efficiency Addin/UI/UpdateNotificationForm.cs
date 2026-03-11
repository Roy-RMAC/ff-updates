using System.Drawing;
using System.Windows.Forms;
using Krypton.Toolkit;
using RMAC_Efficiency_Addin.Updates;

namespace RMAC_Efficiency_Addin.UI
{
    /// <summary>
    /// Notification dialog shown when an update has been downloaded and is ready to install.
    /// Returns:
    ///   DialogResult.OK     → Install when Inventor closes
    ///   DialogResult.Cancel → Not now (don't install this session)
    ///   DialogResult.Ignore → Skip this version (don't ask again for this version)
    /// </summary>
    internal sealed class UpdateNotificationForm : KryptonForm
    {
        private readonly KryptonManager _kryptonManager = new();

        public UpdateNotificationForm(UpdateInfo update)
        {
            _kryptonManager.GlobalPaletteMode = PaletteMode.Microsoft365Black;

            Text = "Update Available";
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(440, 300);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(16)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // heading
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // description
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // release notes
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // buttons

            // Heading
            var lblHeading = new Label
            {
                Text = $"Version {update.Version} is ready to install",
                Font = new Font(Font.FontFamily, 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(220, 220, 220),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 8)
            };
            layout.Controls.Add(lblHeading, 0, 0);

            // Description
            var lblDesc = new Label
            {
                Text = "The update has been downloaded and will install\nautomatically when you close Inventor.",
                ForeColor = Color.FromArgb(180, 180, 180),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 12)
            };
            layout.Controls.Add(lblDesc, 0, 1);

            // Release notes
            if (!string.IsNullOrWhiteSpace(update.ReleaseNotes))
            {
                var txtNotes = new KryptonTextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical,
                    Text = update.ReleaseNotes,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 0, 0, 12)
                };
                layout.Controls.Add(txtNotes, 0, 2);
            }

            // Button row
            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0)
            };

            var btnInstall = new KryptonButton
            {
                Text = "Install on Close",
                DialogResult = DialogResult.OK,
                AutoSize = true,
                Margin = new Padding(4, 0, 0, 0)
            };

            var btnNotNow = new KryptonButton
            {
                Text = "Not Now",
                DialogResult = DialogResult.Cancel,
                AutoSize = true,
                Margin = new Padding(4, 0, 0, 0)
            };

            var btnSkip = new KryptonButton
            {
                Text = "Skip This Version",
                DialogResult = DialogResult.Ignore,
                AutoSize = true,
                Margin = new Padding(0)
            };

            // RightToLeft flow: add in reverse visual order
            btnPanel.Controls.Add(btnInstall);
            btnPanel.Controls.Add(btnNotNow);
            btnPanel.Controls.Add(btnSkip);

            layout.Controls.Add(btnPanel, 0, 3);

            Controls.Add(layout);
            AcceptButton = btnInstall;
            CancelButton = btnNotNow;
        }
    }
}
