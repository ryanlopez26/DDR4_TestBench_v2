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

        // ---- Digital-lane geometry for the boolean signals ---------------------
        //
        // The record now carries FOUR series that live in {0,1} — beam possible, beam
        // present, beam request, SEFI — plus error rate in 0..1. Drawn raw they land on
        // top of each other and the plot shows one line where there are five. Each gets
        // its own horizontal band instead, logic-analyzer style: lane k occupies
        // [k*LanePitch, k*LanePitch + LaneHeight] on the right axis.
        private const double LanePitch = 1.25;
        private const double LaneHeight = 0.85;

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
            // --- identity -------------------------------------------------------
            // The type is worth showing now that Expose exists: an Expose record has no
            // bit counts and an empty capture, so a reader who can't see the type reads
            // the zeros as a failed test rather than as "not applicable".
            recordID.Text = $"Record {r.Rid:X4} · {Describe(r.Rtype)}";
            Text = $"Record {r.Rid:X4} — {Describe(r.Rtype)}";

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
            // reads 0 for Read / Verify / Dynamic records, and an Expose record touches
            // no memory at all — every counter below is 0 by construction there.
            bool countsApply = r.Rtype != RecordType.Expose;

            processed_bits.Text = countsApply ? r.ProcessedBits.ToString("N0") : "n/a";
            correct_bits.Text = countsApply ? r.CorrectBits.ToString("N0") : "n/a";
            incorrect_bits.Text = countsApply ? r.IncorrectBits.ToString("N0") : "n/a";

            // err_bins counts BYTES binned by how many bits flipped in them, so its sum
            // is a byte count — deliberately not comparable to IncorrectBits (a bit count).
            if (!countsApply)
                err_bins_total.Text = "n/a";
            else
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
        /// Three things make this fiddlier than it looks.
        ///
        /// First, the server pushes a DIFFERENT SUBSET of series depending on record
        /// type and never pads them, so several of the twelve fields are empty in any
        /// given record. Each series is trimmed against the timestamps independently
        /// rather than assumed parallel.
        ///
        /// Second, the magnitudes span nine orders of magnitude (bits processed ~1e9,
        /// error rate 0..1, the flags 0/1), so the counters get the left axis and
        /// everything unit-scale goes on a right axis. On one shared axis everything
        /// except the largest trace flattens onto the baseline.
        ///
        /// Third — new this round — there are now FOUR boolean series rather than one.
        /// Beam possible, beam present, beam request and SEFI all take the values 0 and
        /// 1, so stacking them into separate lanes is the only way all four stay
        /// readable. Reading beam request against beam present is the point of the
        /// split: it separates "we asked and got beam" from "we asked and waited".
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
                    r.LogCurrentAddress.Length, r.LogBeamPresent.Length,
                    r.LogBeamPossible.Length, r.LogBeamRequest.Length,
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

            // Right-hand axis carries the stacked lanes.
            var rightAxis = plot.Axes.AddRightAxis();
            rightAxis.Label.Text = "Signals (stacked)";

            bool any = false;

            // --- counters, left axis ---------------------------------------------
            any |= AddSeries(t, ToDouble(r.LogBitsProcessed), "Bits Processed", null);
            any |= AddSeries(t, ToDouble(r.LogBitsCorrect), "Bits Correct", null);
            any |= AddSeries(t, ToDouble(r.LogBitsIncorrect), "Bits Incorrect", null);
            any |= AddSeries(t, ToDouble(r.LogCurrentAddress), "Address", null);

            // --- lanes, right axis -------------------------------------------------
            // Lanes are allocated bottom-up and only to series that have data, so a
            // Read record (two flags) does not leave three empty bands.
            int lane = 0;

            if (AddLane(t, r.LogErrorRate, "Error Rate", rightAxis, lane, isBool: false))
            { any = true; lane++; }

            if (AddLane(t, ToDouble(r.LogBeamPossible), "Beam Possible", rightAxis, lane, isBool: true))
            { any = true; lane++; }

            if (AddLane(t, ToDouble(r.LogBeamPresent), "Beam Present", rightAxis, lane, isBool: true))
            { any = true; lane++; }

            if (AddLane(t, ToDouble(r.LogBeamRequest), "Beam Request", rightAxis, lane, isBool: true))
            { any = true; lane++; }

            if (AddLane(t, ToDouble(r.LogSefiDetected), "SEFI", rightAxis, lane, isBool: true))
            { any = true; lane++; }

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

            var (x, y) = Align(xs, ys);
            if (x.Length == 0) return false;

            var scatter = log_plot.Plot.Add.Scatter(x, y);

            // Peak in the label: with four counters sharing the left axis, the smaller
            // ones are visually flat and the legend is the only place to read them.
            double max = y.Max();
            scatter.LegendText = $"{label} (max {max:0.###})";
            scatter.MarkerSize = 0;

            if (axis != null) scatter.Axes.YAxis = axis;

            return true;
        }

        /// <summary>
        /// Add one unit-scale series into its own horizontal band on the right axis.
        ///
        /// Booleans are drawn as square steps rather than straight segments: a straight
        /// line between two samples 200 ms apart implies the signal ramped, which for a
        /// TTL line it did not. The legend carries the duty cycle, which is usually the
        /// number actually wanted — "beam present 62% of samples" says more about a run
        /// than the shape of the trace does.
        /// </summary>
        private bool AddLane(double[] xs, double[] ys, string label,
                             ScottPlot.IYAxis axis, int lane, bool isBool)
        {
            if (ys.Length == 0) return false;

            var (x, y) = Align(xs, ys);
            if (x.Length == 0) return false;

            double baseline = lane * LanePitch;

            // Scale into the lane. Booleans are already 0..1; a rate could in principle
            // exceed 1, so normalize against its own peak and say so in the legend.
            double peak = isBool ? 1.0 : Math.Max(y.Max(), double.Epsilon);
            double[] laneY = y.Select(v => baseline + (v / peak) * LaneHeight).ToArray();

            if (isBool)
                (x, laneY) = ToSteps(x, laneY);

            var scatter = log_plot.Plot.Add.Scatter(x, laneY);
            scatter.MarkerSize = 0;

            scatter.LegendText = isBool
                ? $"{label} ({DutyCycle(y):0.#}% high)"
                : $"{label} (peak {peak:0.###})";

            scatter.Axes.YAxis = axis;
            return true;
        }

        /// <summary>
        /// Match a series to the timestamp axis.
        ///
        /// A series SHORTER than the timestamps is simply truncated against. A series
        /// LONGER than them is index-resampled rather than truncated, because truncating
        /// would silently show only the first part of the run compressed onto the whole
        /// axis. That case is real: the engine's Expose arm pushes log_beam_possible
        /// twice per progress tick, so on an Expose record that series arrives at
        /// roughly twice the length of every other one.
        /// </summary>
        private static (double[] xs, double[] ys) Align(double[] xs, double[] ys)
        {
            if (xs.Length == 0 || ys.Length == 0)
                return (Array.Empty<double>(), Array.Empty<double>());

            if (ys.Length == xs.Length)
                return (xs, ys);

            if (ys.Length < xs.Length)
                return (xs.Take(ys.Length).ToArray(), ys);

            // Longer than the axis: resample down, nearest sample per slot.
            int n = xs.Length;
            var resampled = new double[n];
            for (int i = 0; i < n; i++)
                resampled[i] = ys[(int)((long)i * ys.Length / n)];
            return (xs, resampled);
        }

        /// <summary>
        /// Expand a series into square steps: hold each value until the next sample,
        /// then jump. Produces 2n-1 points from n.
        /// </summary>
        private static (double[] xs, double[] ys) ToSteps(double[] xs, double[] ys)
        {
            int n = Math.Min(xs.Length, ys.Length);
            if (n <= 1) return (xs, ys);

            var sx = new double[n * 2 - 1];
            var sy = new double[n * 2 - 1];

            int k = 0;
            for (int i = 0; i < n; i++)
            {
                sx[k] = xs[i];
                sy[k] = ys[i];
                k++;

                if (i + 1 < n)
                {
                    sx[k] = xs[i + 1];
                    sy[k] = ys[i];   // hold the old level up to the next sample
                    k++;
                }
            }

            return (sx, sy);
        }

        /// <summary>Percentage of samples that were high, for a 0/1 series.</summary>
        private static double DutyCycle(double[] ys) =>
            ys.Length == 0 ? 0.0 : ys.Count(v => v >= 0.5) * 100.0 / ys.Length;

        // ============================== Bin plots ==============================

        /// <summary>
        /// Bar chart of one bin array. A null array (the server's Option was None) leaves
        /// the pane empty rather than drawing a row of zeros, which would read as
        /// "measured, all zero" instead of "not recorded".
        ///
        /// Note the engine now initializes both bin arrays to Some([0; N]) for every
        /// record type, so null only shows up on records written before that change —
        /// on a current Read or Expose record the pane draws a legitimate row of zeros.
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

            // An Expose record touches no memory: the engine creates the capture file
            // and writes nothing to it, so the download would succeed with 0 bytes.
            // Say so up front rather than after a round trip.
            if (record?.Rtype == RecordType.Expose)
            {
                MessageBox.Show(
                    "Expose records carry no capture data — the engine only tallies beam " +
                    "time, so the capture file is empty by design.",
                    "Download Capture", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                    string kind = record?.Rtype switch
                    {
                        RecordType.Read => "raw bytes read back",
                        RecordType.Expose => "empty — Expose records write no capture bytes",
                        _ => "actual XOR expected, one byte per sample",
                    };

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
                    "This cannot be undone. The server also strips this record's id from " +
                    "every run that cites it.",
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
                    "running — when the server is in safe mode with /dvr unmounted, or " +
                    "when a file could not be removed.",
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

        /// <summary>
        /// Human label for a record type. Has an explicit arm for every variant plus a
        /// fallback, so a sixth variant added to record.rs shows up as an unknown index
        /// rather than throwing out of a switch expression.
        /// </summary>
        private static string Describe(RecordType t) => t switch
        {
            RecordType.Read => "Read",
            RecordType.Verify => "Verify",
            RecordType.Write => "Write",
            RecordType.Dynamic => "Dynamic",
            RecordType.Expose => "Expose",
            _ => $"Type {(uint)t}",
        };

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