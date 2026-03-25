// PartNumbering/RenumberProgressForm.cs
// Non-modal progress dialog for part numbering operations.
// Shown with Show() (not ShowDialog) so the synchronous renumber loop
// can update it via UpdateProgress() and Application.DoEvents().

using System;
using System.Drawing;
using System.Windows.Forms;
using Krypton.Toolkit;

namespace RMAC_Efficiency_Addin.PartNumbering
{
    internal sealed class RenumberProgressForm : KryptonForm
    {
        // ---- Theme ----
        private readonly KryptonManager _kryptonManager = new()
        {
            GlobalPaletteMode = PaletteMode.Microsoft365Black
        };

        // ---- Controls ----
        private readonly KryptonLabel _lblOperation;
        private readonly KryptonLabel _lblPercentage;
        private readonly ProgressBar _progressBar;

        // ---- State ----
        private int _totalSteps = 1;

        public RenumberProgressForm()
        {
            Text = "RMAC Part Numbering";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ControlBox = false;        // no close button — renumberer manages lifecycle
            TopMost = true;            // stays visible over Inventor redraws
            ClientSize = new Size(480, 140);

            // Operation label
            _lblOperation = new KryptonLabel
            {
                Text = "Preparing\u2026",
                AutoSize = false,
                Dock = DockStyle.None,
                Location = new Point(20, 16),
                Size = new Size(440, 22),
                LabelStyle = LabelStyle.NormalPanel
            };
            _lblOperation.StateCommon.ShortText.TextH = PaletteRelativeAlign.Near;

            // Progress bar (standard WinForms ProgressBar works fine inside KryptonForm)
            _progressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Style = ProgressBarStyle.Continuous,
                Location = new Point(20, 48),
                Size = new Size(440, 28)
            };

            // Percentage label
            _lblPercentage = new KryptonLabel
            {
                Text = "0%",
                AutoSize = false,
                Dock = DockStyle.None,
                Location = new Point(20, 86),
                Size = new Size(440, 22),
                LabelStyle = LabelStyle.NormalPanel
            };
            _lblPercentage.StateCommon.ShortText.TextH = PaletteRelativeAlign.Center;

            Controls.Add(_lblOperation);
            Controls.Add(_progressBar);
            Controls.Add(_lblPercentage);
        }

        /// <summary>
        /// Sets the total number of steps for percentage calculation.
        /// Call once after BOM rows are counted.
        /// </summary>
        public void SetTotalSteps(int total)
        {
            _totalSteps = Math.Max(1, total);
        }

        /// <summary>
        /// Updates the progress display. Called from the synchronous renumber loop.
        /// Uses DoEvents() to pump the message queue so the form repaints.
        /// </summary>
        public void UpdateProgress(int currentStep, string operationText)
        {
            if (IsDisposed) return;

            try
            {
                int pct = Math.Min(100, (int)(currentStep * 100.0 / _totalSteps));

                _lblOperation.Text = operationText ?? "";
                _progressBar.Value = pct;
                _lblPercentage.Text = $"{pct}%  ({currentStep} / {_totalSteps})";

                Application.DoEvents();
            }
            catch { }
        }

        /// <summary>
        /// Updates only the operation text label without changing the progress bar.
        /// Used for phase messages (e.g. "Preloading documents...").
        /// </summary>
        public void UpdateStatusText(string operationText)
        {
            if (IsDisposed) return;

            try
            {
                _lblOperation.Text = operationText ?? "";
                Application.DoEvents();
            }
            catch { }
        }

        private bool _allowClose;

        /// <summary>
        /// Closes the form programmatically (bypasses Alt+F4 guard).
        /// </summary>
        public void CloseAllowed()
        {
            _allowClose = true;
            Close();
        }

        // Prevent accidental close via Alt+F4, but allow programmatic close
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }
    }
}
