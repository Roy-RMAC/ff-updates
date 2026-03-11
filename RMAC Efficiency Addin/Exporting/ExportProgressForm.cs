// Exporting/ExportProgressForm.cs
// Non-modal progress dialog for batch export operations.
// Shown with Show() (not ShowDialog) so the synchronous export loop
// can update it via UpdateProgress() and Application.DoEvents().

using System;
using System.Drawing;
using System.Windows.Forms;
using Krypton.Toolkit;

namespace RMAC_Efficiency_Addin.Exporting
{
    internal sealed class ExportProgressForm : KryptonForm
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
        private readonly KryptonButton _btnCancel;

        // ---- State ----
        private int _totalSteps = 1;

        /// <summary>
        /// When true, the user has requested cancellation of the export.
        /// The caller should check this property inside its job loop.
        /// </summary>
        public bool CancellationRequested { get; private set; }

        public ExportProgressForm()
        {
            Text = "RMAC Export";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ControlBox = false;        // no close button — export manages lifecycle
            TopMost = true;            // stays visible over Inventor redraws
            ClientSize = new Size(480, 180);

            // Operation label
            _lblOperation = new KryptonLabel
            {
                Text = "Preparing export\u2026",
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

            // Cancel button
            _btnCancel = new KryptonButton
            {
                Text = "Cancel",
                Location = new Point(190, 118),
                Size = new Size(100, 32)
            };
            _btnCancel.Click += (_, _) =>
            {
                CancellationRequested = true;
                _btnCancel.Text = "Cancelling...";
                _btnCancel.Enabled = false;
            };

            Controls.Add(_lblOperation);
            Controls.Add(_progressBar);
            Controls.Add(_lblPercentage);
            Controls.Add(_btnCancel);
        }

        /// <summary>
        /// Sets the total number of steps for percentage calculation.
        /// Call once after the export plan is built.
        /// </summary>
        public void SetTotalSteps(int total)
        {
            _totalSteps = Math.Max(1, total);
        }

        /// <summary>
        /// Updates the progress display. Called from the synchronous export loop.
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
        /// Used by the status callback for non-step messages (e.g. "Reading BOM...").
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

        /// <summary>
        /// Shows a completion state briefly before the form is disposed.
        /// </summary>
        public void SetComplete()
        {
            if (IsDisposed) return;

            try
            {
                _lblOperation.Text = "Export complete.";
                _progressBar.Value = 100;
                _lblPercentage.Text = "100%";
                Application.DoEvents();
            }
            catch { }
        }

        // Prevent accidental close via Alt+F4 unless cancellation was requested
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !CancellationRequested)
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }
    }
}
