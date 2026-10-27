#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DDR4_TestingApp
{
    public partial class RecordViewer : Form
    {
        private ushort? recordId;
        private Record? record;

        private readonly CancellationTokenSource formClosing = new CancellationTokenSource();

        private static readonly Color CompletedColor = Color.FromArgb(0x27, 0xAE, 0x60);
        private static readonly Color AbortedColor = Color.FromArgb(0xC0, 0x39, 0x2B);
        private static readonly Color UnknownColor = Color.FromArgb(0x7F, 0x8C, 0x8D);

        /// <summary>Designer / blank-state constructor. Call <see cref="LoadAsync"/> yourself.</summary>
        public RecordViewer()
        {
            InitializeComponent();
            ConfigureControls();
            WireEvents();
            Clear();

            Shown += RecordViewer_Shown;
            FormClosed += RecordViewer_FormClosed;
        }

        /// <summary>
        /// Open the viewer for a specific record. The fetch happens on Shown, so the
        /// window paints immediately and a slow link doesn't block the caller:
        /// <code>new RecordViewer(rid).Show(this);</code>
        /// </summary>
        public RecordViewer(ushort rid) : this()
        {
            recordId = rid;
            recordID.Text = $"Record {rid:X4}";
            Text = $"Record {rid:X4}";
            UpdateActionButtons();
        }

        /// <summary>The record this viewer is showing, or null if none has been loaded.</summary>
        public ushort? RecordId => recordId;

        // ============================== Lifecycle ==============================

        private async void RecordViewer_Shown(object? sender, EventArgs e)
        {
            if (recordId is ushort rid)
                await LoadAsync(rid, formClosing.Token);
        }

        private void RecordViewer_FormClosed(object? sender, FormClosedEventArgs e)
        {
            formClosing.Cancel();
        }

        private void ConfigureControls()
        {
            // RecordViewer.Designer.cs sets splitContainer1.Enabled = false, and Enabled
            // CASCADES: Control.Enabled's getter walks up the parent chain, so while an
            // ancestor is disabled every descendant renders and behaves disabled no
            // matter what is set on the child. That silently overrode the buttons' own
            // Enabled and greyed the text boxes and plots along with them. The real fix
            // is setting Enabled back to True on splitContainer1 in the designer
            // Properties window; this line is here so the form behaves either way.
            splitContainer1.Enabled = true;

            foreach (var box in new[] { startTime, endTime, duration, processed_bits,
                                        correct_bits, incorrect_bits, err_bins_total,
                                        exposure_time, time_to_sefi })
            {
                // ReadOnly, not Enabled = false: the text stays selectable and copyable
                // (useful for pasting a bit count elsewhere) but can't be edited. WinForms
                // paints a read-only box with the Control background, which is why it
                // still looks greyed.
                box.ReadOnly = true;
                box.TabStop = false;   // nine display fields in the tab order is a nuisance
            }

            // A disabled Button ignores BackColor under the default FlatStyle — the
            // themed renderer paints system colours instead. Flat is what makes the
            // red/green/grey actually render.
            conclusion_status.FlatStyle = FlatStyle.Flat;
            conclusion_status.FlatAppearance.BorderSize = 0;
            conclusion_status.ForeColor = Color.White;
            conclusion_status.Enabled = false;
            conclusion_status.TabStop = false;

            log_plot.Plot.Axes.Bottom.Label.Text = "Time (s)";
            err_bins_plot.Plot.Axes.Bottom.Label.Text = "Flipped bits in byte";
            adj_err_bins_plot.Plot.Axes.Bottom.Label.Text = "Adjacent flipped pairs";
        }

        private void WireEvents()
        {
            downloadCapture.Click += downloadCapture_Click;
            requestDeletion.Click += requestDeletion_Click;
        }

        // ============================== Load ==============================

        /// <summary>Fetch and display a record. Replaces whatever is currently shown.</summary>
        public async Task LoadAsync(ushort rid, CancellationToken ct = default)
        {
            recordId = rid;
            recordID.Text = $"Record {rid:X4}";
            Text = $"Record {rid:X4}";
            UpdateActionButtons();

            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                Clear();
                SetStatusPill("UNKNOWN", UnknownColor);
                return;
            }

            try
            {
                Record? r = await TcpManager.GetRecordAsync(rid, ct);
                if (IsDisposed) return;

                record = r;
                if (r == null)
                {
                    Clear();
                    SetStatusPill("UNKNOWN", UnknownColor);
                    MessageBox.Show($"The server has no record {rid:X4}.", "Record Viewer",
                                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Populate(r);
            }
            catch (OperationCanceledException)
            {
                // Window closed mid-fetch.
            }
            catch (Exception ex) when (IsConnectionFault(ex))
            {
                if (IsDisposed) return;
                Clear();
                SetStatusPill("UNKNOWN", UnknownColor);
                MessageBox.Show($"Could not load record: {ex.Message}", "Record Viewer",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Clear()
        {
            startTime.Text = "";
            endTime.Text = "";
            duration.Text = "";
            processed_bits.Text = "";
            correct_bits.Text = "";
            incorrect_bits.Text = "";
            err_bins_total.Text = "";
            exposure_time.Text = "";
            time_to_sefi.Text = "";

            foreach (var pane in new[] { log_plot, err_bins_plot, adj_err_bins_plot })
            {
                pane.Plot.Clear();
                pane.Refresh();
            }

            SetStatusPill("UNKNOWN", UnknownColor);
            UpdateActionButtons();
        }

        private void Populate(Record r)
        {
            // --- conclusion -----------------------------------------------------
            // Aborted wins: the engine archives a partial record with aborted = true,
            // and a record that is somehow neither is not a state to report as good.
            if (r.Aborted) SetStatusPill("ABORTED", AbortedColor);
            else if (r.Completed) SetStatusPill("COMPLETED", CompletedColor);
            else SetStatusPill("UNKNOWN", UnknownColor);

            // --- timestamps -----------------------------------------------------
            startTime.Text = FormatEpoch(r.TimeStarted);
            endTime.Text = FormatEpoch(r.TimeEnded);
            duration.Text = FormatDuration(r.TimeDuration);

            // --- bit totals -----------------------------------------------------
            // ProcessedBits is only maintained on the Write path server-side, so it
            // reads 0 for Read / Verify / Dynamic records.
            processed_bits.Text = r.ProcessedBits.ToString("N0");
            correct_bits.Text = r.CorrectBits.ToString("N0");
            incorrect_bits.Text = r.IncorrectBits.ToString("N0");

            // err_bins counts BYTES binned by how many bits flipped in them, so its sum
            // is a byte count — deliberately not comparable to IncorrectBits (a bit count).
            err_bins_total.Text = r.ErrBins == null
                ? "—"
                : r.ErrBins.Aggregate(0UL, (a, b) => a + b).ToString("N0") + " bytes";

            // --- beam metrics ---------------------------------------------------
            exposure_time.Text = r.ExposureTime.HasValue
                ? $"{r.ExposureTime.Value:0.###} s" : "—";
            time_to_sefi.Text = r.TimeToSefi.HasValue
                ? $"{r.TimeToSefi.Value:0.###} s" : "—";

            // --- plots ----------------------------------------------------------
            PlotSignals(r);
            PlotBins(err_bins_plot, r.ErrBins, 9);
            PlotBins(adj_err_bins_plot, r.AdjErrBins, 8);

            UpdateActionButtons();
        }

        // ============================== Signals plot ==============================

        /// <summary>
        /// One trace per non-empty log series, against log_timestamp.
        ///
        /// Two things make this fiddlier than it looks. First, the server pushes a
        /// DIFFERENT SUBSET of series depending on record type and never pads them, so
        /// three to five of the ten fields are empty in any given record — and a future
        /// arm gaining a conditional push would make the lengths diverge, so each series
        /// is trimmed against the timestamps independently rather than assumed parallel.
        /// Second, the magnitudes span nine orders of magnitude (bits processed ~1e9,
        /// error rate 0..1, beam 0/1), so the counters get the left axis and the
        /// unit-scale signals get a right axis. On one shared axis everything except
        /// the largest trace flattens onto the baseline.
        /// </summary>
        private void PlotSignals(Record r)
        {
            var plot = log_plot.Plot;
            plot.Clear();

            double[] t = r.LogTimestamp;

            // Fall back to sample index if there are no timestamps, so the series are
            // still readable.
            bool usingIndex = t.Length == 0;
            if (usingIndex)
            {
                int longest = new[]
                {
                    r.LogBitsProcessed.Length, r.LogBitsCorrect.Length,
                    r.LogBitsIncorrect.Length, r.LogErrorRate.Length,
                    r.LogCurrentAddress.Length, r.LogBeamSignal.Length,
                    r.LogSefiDetected.Length,
                }.Max();

                if (longest == 0)
                {
                    plot.Axes.Bottom.Label.Text = "No log data in this record";
                    log_plot.Refresh();
                    return;
                }

                t = Enumerable.Range(0, longest).Select(i => (double)i).ToArray();
            }

            plot.Axes.Bottom.Label.Text = usingIndex ? "Sample index" : "Time (s)";
            plot.Axes.Left.Label.Text = "Bits / address";

            // Right-hand axis for everything that lives in 0..1.
            var rightAxis = plot.Axes.AddRightAxis();
            rightAxis.Label.Text = "Rate / flag";

            bool any = false;
            any |= AddSeries(t, ToDouble(r.LogBitsProcessed), "Bits Processed", null);
            any |= AddSeries(t, ToDouble(r.LogBitsCorrect), "Bits Correct", null);
            any |= AddSeries(t, ToDouble(r.LogBitsIncorrect), "Bits Incorrect", null);
            any |= AddSeries(t, ToDouble(r.LogCurrentAddress), "Address", null);
            any |= AddSeries(t, r.LogErrorRate, "Error Rate", rightAxis);
            any |= AddSeries(t, ToDouble(r.LogBeamSignal), "Beam", rightAxis);
            any |= AddSeries(t, ToDouble(r.LogSefiDetected), "SEFI", rightAxis);

            if (any) plot.ShowLegend();

            plot.Axes.AutoScale();
            log_plot.Refresh();
        }

        /// <summary>
        /// Add one trace, trimmed to the shorter of x and y. Returns false if the series
        /// was empty (a "blank vec") and nothing was plotted.
        /// </summary>
        private bool AddSeries(double[] xs, double[] ys, string label, ScottPlot.IYAxis? axis)
        {
            if (ys.Length == 0) return false;

            int n = Math.Min(xs.Length, ys.Length);
            if (n == 0) return false;

            double[] x = xs.Length == n ? xs : xs.Take(n).ToArray();
            double[] y = ys.Length == n ? ys : ys.Take(n).ToArray();

            var scatter = log_plot.Plot.Add.Scatter(x, y);

            // Peak in the label: with four counters sharing the left axis, the smaller
            // ones are visually flat and the legend is the only place to read them.
            double max = y.Max();
            scatter.LegendText = $"{label} (max {max:0.###})";
            scatter.MarkerSize = 0;

            if (axis != null) scatter.Axes.YAxis = axis;

            return true;
        }

        // ============================== Bin plots ==============================

        /// <summary>
        /// Bar chart of one bin array. A null array (the server's Option was None) leaves
        /// the pane empty rather than drawing a row of zeros, which would read as
        /// "measured, all zero" instead of "not recorded".
        /// </summary>
        private static void PlotBins(ScottPlot.WinForms.FormsPlot pane, ulong[]? bins, int expected)
        {
            var plot = pane.Plot;
            plot.Clear();

            if (bins == null || bins.Length == 0)
            {
                pane.Refresh();
                return;
            }

            double[] values = bins.Select(v => (double)v).ToArray();
            plot.Add.Bars(values);

            double max = values.Max();
            plot.Axes.SetLimitsX(-0.5, Math.Max(expected, values.Length) - 0.5);
            plot.Axes.SetLimitsY(0, max > 0 ? max * 1.1 : 1);
            plot.Axes.SetupMultiplierNotation(plot.Axes.Left);

            pane.Refresh();
        }

        // ============================== Record management ==============================

        private async void downloadCapture_Click(object? sender, EventArgs e)
        {
            if (recordId is not ushort rid) return;

            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                MessageBox.Show("Not connected.");
                return;
            }

            using var dlg = new SaveFileDialog
            {
                FileName = $"{rid:X4}.cap",
                Filter = "Capture files (*.cap)|*.cap|All files (*.*)|*.*",
                Title = "Save capture",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            string original = downloadCapture.Text;
            downloadCapture.Enabled = false;
            try
            {
                var progress = new Progress<long>(bytes =>
                {
                    if (!IsDisposed)
                        downloadCapture.Text = $"{bytes / (1024.0 * 1024.0):0.#} MB";
                });

                long written = await TcpManager.DownloadCaptureToFileAsync(
                    rid, dlg.FileName, progress, formClosing.Token);

                if (IsDisposed) return;

                if (written == 0)
                {
                    // Length 0 is the server's "no capture": the file is missing, or
                    // /dvr is unmounted in safe mode. Both land here.
                    MessageBox.Show(
                        "No capture is available for this record.", "Download Capture",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    string kind = record?.Rtype == RecordType.Read
                        ? "raw bytes read back"
                        : "actual XOR expected, one byte per sample";

                    MessageBox.Show(
                        $"Saved {written / (1024.0 * 1024.0):0.##} MB.\n\nContents: {kind}.",
                        "Download Capture", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!IsDisposed)
                    MessageBox.Show($"Download failed: {ex.Message}", "Download Capture",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (!IsDisposed)
                {
                    downloadCapture.Text = original;
                    UpdateActionButtons();
                }
            }
        }

        private async void requestDeletion_Click(object? sender, EventArgs e)
        {
            if (recordId is not ushort rid) return;

            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                MessageBox.Show("Not connected.");
                return;
            }

            if (MessageBox.Show(
                    $"Permanently delete record {rid:X4} and its capture from the ZCU?\n\n" +
                    "This cannot be undone, and any run referencing this record will be " +
                    "left with a dangling link.",
                    "Request Deletion", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                != DialogResult.Yes)
                return;

            requestDeletion.Enabled = false;
            try
            {
                bool ok = await TcpManager.DeleteRecordAsync(rid, formClosing.Token);
                if (IsDisposed) return;

                if (ok)
                {
                    Close();   // nothing left to view
                    return;
                }

                MessageBox.Show(
                    "The server refused the deletion. It returns false when no record " +
                    "with that id is archived — including while the record is still " +
                    "running — or when a file could not be removed.",
                    "Request Deletion", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!IsDisposed)
                    MessageBox.Show($"Deletion failed: {ex.Message}", "Request Deletion",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (!IsDisposed) UpdateActionButtons();
            }
        }

        // ============================== Helpers ==============================

        /// <summary>
        /// Both actions need only the rid, not the loaded record, so they go live as soon
        /// as the viewer knows which record it is for — before the fetch returns, and
        /// still enabled if the fetch fails or the record is missing.
        /// </summary>
        private void UpdateActionButtons()
        {
            bool haveRecord = recordId.HasValue;
            downloadCapture.Enabled = haveRecord;
            requestDeletion.Enabled = haveRecord;
        }

        private void SetStatusPill(string text, Color back)
        {
            if (conclusion_status.Text != text) conclusion_status.Text = text;
            if (conclusion_status.BackColor != back) conclusion_status.BackColor = back;
        }

        private static double[] ToDouble(ulong[] v) => Array.ConvertAll(v, x => (double)x);
        private static double[] ToDouble(uint[] v) => Array.ConvertAll(v, x => (double)x);
        private static double[] ToDouble(bool[] v) => Array.ConvertAll(v, x => x ? 1.0 : 0.0);

        private static string FormatEpoch(ulong epochSeconds) =>
            epochSeconds == 0
                ? "—"
                : DateTimeOffset.FromUnixTimeSeconds((long)epochSeconds)
                                .LocalDateTime
                                .ToString("yyyy-MM-dd HH:mm:ss");

        private static string FormatDuration(ulong seconds)
        {
            if (seconds == 0) return "—";
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}h {ts.Minutes:00}m {ts.Seconds:00}s"
                : (ts.TotalMinutes >= 1 ? $"{ts.Minutes}m {ts.Seconds:00}s" : $"{ts.Seconds}s");
        }

        private static bool IsConnectionFault(Exception ex) =>
            ex is TimeoutException
            || ex is IOException
            || ex is SocketException
            || ex is InvalidDataException
            || ex is InvalidOperationException;
    }
}