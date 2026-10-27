using ScottPlot;
using ScottPlot.Colormaps;
using ScottPlot.Plottables;
using ScottPlot.WinForms;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using static System.Runtime.InteropServices.JavaScript.JSType;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using Color = System.Drawing.Color;
using String = System.String;

namespace DDR4_TestingApp
{
    public partial class MainForm : Form
    {
        // with your other viewer constants (DATA_VIEWER_ROW_SIZE, etc.)
        const uint MEM_VIEWER_ROWS = 21;

        // MainForm fields
        private bool _viewerRefreshInProgress = false;

        // with your other fields — last-seen rate per row; null = never visited
        private readonly float?[] _memBandRate = new float?[MEM_VIEWER_ROWS];

        // Data viewer settings
        const uint DATA_VIEWER_ROW_SIZE = 16;
        const uint DATA_VIEWER_RENDER_SIZE = 112;

        float dyn_trigger_threshold = 0.5f;

        // Keep the collection alive for the lifespan of the Form
        private PrivateFontCollection privateFonts = new PrivateFontCollection();
        private Font customFont;


        private readonly System.Windows.Forms.Timer _uiTimer = new();
        private readonly System.Windows.Forms.Timer _statusTimer = new();

        // Parameters
        private bool captureDiff = false;
        private bool enableSEFI = false;
        private bool enableBeamTrigger = false;

        private DateTime startTime = DateTime.Now;
        private DateTime endTime = DateTime.Now;

        //Data arrays for dynamic plot
        private readonly List<float> dynDataX = new();
        private readonly List<float> dynDataY = new();

        // Static bar plots — populated from VerifyRsp update packets
        private ScottPlot.Plottables.BarPlot barPlot1;   // bit error bins      (plot1)
        private ScottPlot.Plottables.BarPlot barPlot2;   // adjacent error bins (plot2)
        private ScottPlot.Plottables.BarPlot dynbarPlot2;   // bit error bins      (plot1)
        private ScottPlot.Plottables.BarPlot dynbarPlot3;   // adjacent error bins (plot2)

        public MainForm()
        {
            InitializeComponent();

            // ===== Data connections =====
            var sp = dynPlot.Plot.Add.Scatter(dynDataX, dynDataY);
            sp.LineWidth = 2;
            sp.LineColor = Colors.Blue;

            // Bars start empty; they're filled on each VerifyRsp update packet.
            barPlot1 = plot1.Plot.Add.Bars(Array.Empty<double>());
            barPlot2 = plot2.Plot.Add.Bars(Array.Empty<double>());
            dynbarPlot2 = dynPlot2.Plot.Add.Bars(Array.Empty<double>());   // was plot1
            dynbarPlot3 = dynPlot3.Plot.Add.Bars(Array.Empty<double>());   // was plot2

            // ===== Dynamic plot =====
            dynPlot.Plot.Axes.SetLimitsX(0, 60);
            dynPlot.Plot.Axes.SetLimitsY(0, 1);
            dynPlot.Plot.Axes.Title.Label.IsVisible = false;
            dynPlot.Dock = DockStyle.Fill;

            Array.Clear(_memBandRate);

            // ===== Static plots =====
            plot1.Plot.Axes.SetLimitsX(1, 8);
            plot1.Plot.Axes.Title.Label.IsVisible = false;
            plot1.Dock = DockStyle.Fill;

            plot2.Plot.Axes.SetLimitsX(1, 7);
            plot2.Plot.Axes.Title.Label.IsVisible = false;
            plot2.Dock = DockStyle.Fill;

            dynPlot2.Plot.Axes.SetLimitsX(1, 8);
            dynPlot2.Plot.Axes.Title.Label.IsVisible = false;
            dynPlot2.Dock = DockStyle.Fill;

            dynPlot3.Plot.Axes.SetLimitsX(1, 7);
            dynPlot3.Plot.Axes.Title.Label.IsVisible = false;
            dynPlot3.Dock = DockStyle.Fill;

            plot1.Plot.Axes.SetupMultiplierNotation(plot1.Plot.Axes.Left);
            plot2.Plot.Axes.SetupMultiplierNotation(plot2.Plot.Axes.Left);
            dynPlot2.Plot.Axes.SetupMultiplierNotation(dynPlot2.Plot.Axes.Left);   // was plot2.Plot.Axes.Left
            dynPlot3.Plot.Axes.SetupMultiplierNotation(dynPlot3.Plot.Axes.Left);   // was plot2.Plot.Axes.Left

            dynPlot.Refresh();
            plot1.Refresh();
            plot2.Refresh();
            dynPlot2.Refresh();
            dynPlot3.Refresh();

            // ===== Timers last  =====
            _statusTimer.Interval = 20;    // 50 Hz
            _statusTimer.Tick += statusUpdate_Tick;
            _statusTimer.Start();

            _uiTimer.Interval = 100;       // 10 Hz
            _uiTimer.Tick += UiTimer_Tick;
            _uiTimer.Start();

            //Default settings
            selection_size.SelectedIndex = 6;
            block_size.SelectedIndex = 2;
            block_factor.SelectedIndex = 5;
            writeMode.SelectedIndex = 2;
            verifyMode.SelectedIndex = 2;
            prngSeed.Text = Random.Shared.Next(0, 100_000_000).ToString("D8");
            dyn_pattern.SelectedIndex = 0;
            dyn_trigger_threshold = 0.2f;
            dyn_trigger_bar.Value = 20;
            dyn_trigger_box.Text = "0.2";

        }



        // Push a set of values into a bar plot. If the bar count is unchanged, the existing
        // Bar objects are mutated in place (Bar is a reference type, so edits persist);
        // otherwise the plottable is rebuilt. Returns the (possibly new) BarPlot so the
        // caller can reassign its field. Must run on the UI thread.
        private static ScottPlot.Plottables.BarPlot UpdateBars(
            ScottPlot.WinForms.FormsPlot pane,
            ScottPlot.Plottables.BarPlot barPlot,
            double[] values)
        {
            if (barPlot.Bars.Count == values.Length)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    barPlot.Bars[i].Value = values[i];
                    // barPlot.Bars[i].Label = values[i].ToString();
                }
            }
            else
            {
                pane.Plot.Remove(barPlot);
                barPlot = pane.Plot.Add.Bars(values);
                //foreach (var bar in barPlot.Bars)
                //    bar.Label = bar.Value.ToString();
            }

            // Fit both axes so the bars fill the plot area:
            //   X spans exactly the bins (half-unit margin each side, so bars sit snug),
            //   Y runs from 0 (baseline) to just above the tallest bar.
            int n = values.Length;
            double max = n > 0 ? values.Max() : 0;
            //pane.Plot.Axes.SetLimitsX(-0.5, n > 0 ? n - 0.5 : 0.5);
            pane.Plot.Axes.SetLimitsY(0, max > 0 ? max * 1.1 : 1);

            pane.Refresh();
            return barPlot;

        }

        private async void UiTimer_Tick(object? sender, EventArgs e)
        {
            // --- Status indicator ---
            bool connected = TcpManager.Status == TcpManager.ConnectionStatus.Connected;

            // =============== UUID Management ===============

            //Verify the UUID
            if (!Program.busy)
            {
                UUID.verifyUUID();

                //Check if auto manage 
                if (autoManageUUID.Checked)
                {
                    if (UUID.GetUuid() is null)
                    {
                        //UUID initialize
                        UUID.SetUUID(0);

                    }
                    else
                    {
                        if (UUID.used && UUID.hasChecked() && !UUID._fetchingUUID)
                        {
                            //Increment UUID to obtain an unused
                            UUID.SetUUID(Convert.ToUInt16(UUID.GetUuid().Value + 1));
                        }

                    }

                }
            }

            // ===============================================

            //Attempt to update information
            if (!Program.busy) Info.update();

            //Update dynamic buttons
            if (captureDiff)
            {
                captureModeRaw.BackColor = Color.Black;
                captureModeDiff.BackColor = Color.ForestGreen;
            }
            else
            {
                captureModeDiff.BackColor = Color.Black;
                captureModeRaw.BackColor = Color.ForestGreen;
            }

            // Connection button
            if (connected) { connect_btn.Text = "Disconnect"; }
            else { connect_btn.Text = "Connect"; }


            updateAddrInfo();   // cheap label update — do it every tick, before any await

            // Live data-viewer refresh: at most ONE dump in flight, and none while a
            // foreground op owns the connection. Without these guards each 100ms tick
            // queues another dump on the single serialized connection; they pile up
            // during a long task, flood out at the end, and starve the UUID/Info pollers.

            if (connected && !Program.busy && !_viewerRefreshInProgress && !useLock.Checked)
            {
                _viewerRefreshInProgress = true;
                try
                {
                    // Bound the request to the geometry the SERVER was actually configured
                    // with (TcpManager.CommittedGeometry = the last applied Config), NOT the
                    // live Config.sys. The combos mutate Config.sys without re-applying, and
                    // the server reads each block at (block_index * BlockSize) with no bounds
                    // check: an out-of-range index -> an out-of-region /dev/mem read, and a
                    // BlockFactor of 0 -> step_by(0), and BOTH make the Rust server PANIC.
                    // When the server dies it drops the socket mid-dump, which then cascades
                    // into the client. If no Config has been applied there is nothing safe to
                    // show, so skip the refresh entirely.
                    var geo = TcpManager.CommittedGeometry;
                    if (geo.HasValue && geo.Value.BlockFactor > 0 && geo.Value.NumBlocks >= geo.Value.BlockFactor)
                    {
                        var g = geo.Value;

                        // Blocks the sampling actually wrote: 0, BlockFactor, 2*BlockFactor, ...
                        // The viewer index selects among THOSE, so it maps to raw block
                        // (index * BlockFactor) and never lands on a skipped/untouched block
                        // (which, in comparison mode, would read back as spurious errors).
                        uint sampledBlocks = g.NumBlocks / g.BlockFactor;

                        // Keep the NumericUpDown's own range in sync so typed/spinner input
                        // can't request a block past the sampled set either (it had no Maximum
                        // set, so it defaulted to 100 regardless of the actual block count).
                        if (viewerBlockNum.Maximum != sampledBlocks - 1)
                            viewerBlockNum.Maximum = sampledBlocks - 1;
                        viewerBlockNum.Minimum = 0;

                        uint sampledIndex = Math.Min((uint)viewerBlockNum.Value, sampledBlocks - 1);
                        uint blockOffset = sampledIndex * g.BlockFactor;   // raw index of that sampled block
                        // blockOffset * BlockSize < NumBlocks * BlockSize = selection_size,
                        // and selection_size maxes at 1 GB << the 4 GiB mapped region, so this
                        // address is guaranteed in range -> the server can never read past it.

                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        await DumpTableFormatter.WriteDumpHexTableAsync(
                            dataViewer,
                            blockOffset,                 // blockOffset (inclusive) — a real sampled block
                            1,                           // numBlocks is now a COUNT -> dump exactly this one block
                            1,                           // unitSize (one byte per cell)
                            DATA_VIEWER_ROW_SIZE,        // rowSize (columns)
                            viewerCmpMode.Checked,       // comparisonMode
                            DATA_VIEWER_RENDER_SIZE,     // cap the RENDER so the 10 Hz refresh can't freeze the UI
                            cts.Token);
                    }
                }
                catch (OperationCanceledException) { /* skip this cycle; retry next tick */ }
                catch (Exception) { /* transient background-refresh error; ignore */ }
                finally { _viewerRefreshInProgress = false; }
            }

        }


        public void statusUpdate_Tick(object? sender, EventArgs e)
        {
            //Attempt to update task indicator.
            // A ToolStripProgressBar throws ArgumentOutOfRangeException if Value is
            // set outside [Minimum, Maximum]. Program.taskProgress is a float driven
            // by several sources (server percent, dump address/extent ratio, ...) and
            // can legitimately land outside 0-100 (NaN/Inf on a bad ratio, or >100 if
            // the UI's BlockSize no longer matches the applied/server geometry). This
            // handler runs at 50 Hz with no try/catch, so an unclamped assignment here
            // is a hard, uncaught crash. Saturating float->int + clamp makes it safe.
            int pct = (int)Program.taskProgress;                       // NaN -> 0, +/-Inf saturate on .NET 5+
            taskProgress.Value = Math.Clamp(pct, taskProgress.Minimum, taskProgress.Maximum);
            //taskInfo.Text = Program.taskInfo;
            taskName.Text = Program.taskName;



            if (TcpManager.Status == TcpManager.ConnectionStatus.Connected) { onlineInd.BackColor = Color.Green; }
            else { onlineInd.BackColor = Color.Red; }


            if (Info.sys.HasValue && !Program.busy)
            {

                //Update status indicators


                if (Info.sys.Value.BeamSignal) { beamInd.BackColor = Color.Green; }
                else { beamInd.BackColor = Color.Red; }

                if (Info.sys.Value.ControllerCalibrated) { calInd.BackColor = Color.Green; }
                else { calInd.BackColor = Color.Red; }

                if (Info.sys.Value.UiClock) { uiInd.BackColor = Color.Green; }
                else { uiInd.BackColor = Color.Red; }

                if (Info.sys.Value.PlClock) { plInd.BackColor = Color.Green; }
                else { plInd.BackColor = Color.Red; }

                if (Info.sys.Value.FpgaLoaded) { loadedInd.BackColor = Color.Green; }
                else { loadedInd.BackColor = Color.Red; }

            }

            // Update UUID display
            if (UUID.GetUuid() is null)
            {
                showUUID.Text = "---";
                showUUID.BackColor = Color.LightGray;
            }
            else
            {
                showUUID.Text = $"{UUID.GetUuid()}";

                //Change color based on status
                if (UUID._fetchingUUID)
                {
                    showUUID.BackColor = Color.Yellow;
                }
                else
                {
                    //In use
                    if (UUID.used)
                    {
                        showUUID.BackColor = Color.LightBlue;
                    }
                    else
                    {
                        showUUID.BackColor = Color.LightGreen;
                    }
                }


            }
        }

        private void updateAddrInfo()
        {
            //Rerun config calc
            DDR4_TestingApp.Config.updateCalculations();

            addr_info.Text = $"Start Address: \t{Tools.ToHexString(0)}\n" +
                           $"End Address: \t\t{Tools.ToHexString(DDR4_TestingApp.Config.sys.NumBlocks * DDR4_TestingApp.Config.sys.BlockSize)}\n";

            sampling_info.Text =
                           $"Total Number of Blocks: \t{DDR4_TestingApp.Config.sys.NumBlocks}\n" +
                           $"Blocks Sampled: \t\t{DDR4_TestingApp.Config.sys.NumBlocks / DDR4_TestingApp.Config.sys.BlockFactor}\n" +
                           $"Block Size: \t\t\t{Tools.ToHexString(DDR4_TestingApp.Config.sys.BlockSize)} ( {Tools.FormatBytes(DDR4_TestingApp.Config.sys.BlockSize)} )\n" +
                           $"Block Factor: \t\t{DDR4_TestingApp.Config.sys.BlockFactor}\n\n" +
                           $"Selection Size: \t\t{Tools.ToHexString(Program.selection_size)} ( {Tools.FormatBytes(Program.selection_size)} )\n" +
                           $"Sample Size: \t\t\t{Tools.ToHexString(Program.sample_size)} ( {Tools.FormatBytes(Program.sample_size)} )\n\n" +
                           $"Percent Sampled: {((float)Program.sample_size / (float)Program.selection_size) * 100.0f}%";


        }

        private async void button1_Click(object sender, EventArgs e)
        {

            if (TcpManager.Status == TcpManager.ConnectionStatus.Connected)
            {
                connect_btn.Text = "Disconnecting...";

                TcpManager.Disconnect();
            }
            else
            {
                connect_btn.Text = "Connecting...";

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await TcpManager.ConnectAsync(ip_address.Text, int.Parse(port.Text), cts.Token);
                }
                catch (OperationCanceledException)
                {
                    MessageBox.Show("Could not reach the VCU server within 5 seconds.");
                }
            }
        }

        private async void writeButton_Click(object sender, EventArgs e)
        {
            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                MessageBox.Show("Not connected.");
                return;
            }

            Program.taskName = "WRITE";

            uint taskDelay = 0;


            var cmd = new WriteCmd
            {
                Pattern = (byte)writeMode.SelectedIndex,           // 0 = zeros, 1 = ones, 2 = pseudorandom
                Seed = UInt32.Parse(prngSeed.Text),
            };

            // Progress<T> captures the current SynchronizationContext (the UI thread),
            // so the lambda runs on the UI thread even though ConnectAsync's progress
            // reports come from a background continuation. Safe to touch controls.
            var progress = new Progress<WriteRsp>(rsp =>
            {
                //Update beam status

                Program.taskProgress = rsp.PercentComplete;
                Program.taskInfo = $"{rsp.BytesWritten:N0} bytes  ({rsp.PercentComplete:F1}%)  {(rsp.TimeSpentMs / 1000):F0}s";

            });

            using var cts = new CancellationTokenSource();
            EventHandler cancelHandler = (_, _) => cts.Cancel();

            writeButton.Enabled = false;

            Program.busy = true;

            try
            {
                WriteRsp final = await TcpManager.SendWriteAsync(cmd, progress, cts.Token);
                Program.taskInfo = $"Done. {final.BytesWritten:N0} bytes in {(final.TimeSpentMs / 1000):F0} seconds";
            }
            catch (OperationCanceledException)
            {
                Program.taskInfo = "Write cancelled.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Write failed: {ex.Message}");
            }
            finally
            {
                writeButton.Enabled = true;
                Program.busy = false;
            }
        }

        private void genSeed_Click(object sender, EventArgs e)
        {
            prngSeed.Text = Random.Shared.Next(0, 100_000_000).ToString("D8");
        }



        private async void verifyButton_Click_1(object sender, EventArgs e)
        {
            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                MessageBox.Show("Not connected.");
                return;
            }

            Program.taskName = "VERIFY";


            uint taskDelay = 0;

            //Ensure a UUID is present
            if (UUID.GetUuid() is null)
            {
                MessageBox.Show("No valid UUID ready");
                return;
            }


            var cmd = new VerifyCmd
            {
                Uuid = UUID.GetUuid().Value,
                Pattern = (byte)verifyMode.SelectedIndex,    // 0 = zeros, 1 = ones, 2 = pseudorandom
                Seed = UInt32.Parse(prngSeed.Text),
            };

            bool prevBeam = false;

            var progress = new Progress<VerifyRsp>(rsp =>
            {

                // Compose summary
                ulong total = rsp.NumCorrect + rsp.NumIncorrect;
                double corruptedPercent = total > 0 ? (rsp.NumIncorrect * 100.0 / total) : 0.0;
                double seconds = rsp.TimeSpentMs / 1000.0;

                verificationResults.Text =
                    $"Finished verification in {seconds:F2} seconds!\n\n" +
                    $"Correct bits:   {rsp.NumCorrect:N0} ({Tools.FormatBytes(rsp.NumCorrect / 8)})\n" +
                    $"Incorrect bits: {rsp.NumIncorrect:N0}\n\n" +
                    $"{corruptedPercent:F2}% of the bits were corrupted.";


                Program.taskProgress = (int)rsp.PercentComplete;

                // Render the bar plots from this update packet.
                double[] bitBins = rsp.ErrBins?.Select(v => (double)v).ToArray() ?? Array.Empty<double>();
                double[] adjBins = rsp.AdjErrBins?.Select(v => (double)v).ToArray() ?? Array.Empty<double>();

                barPlot1 = UpdateBars(plot1, barPlot1, bitBins);
                barPlot2 = UpdateBars(plot2, barPlot2, adjBins);


            });

            using var cts = new CancellationTokenSource();
            EventHandler cancelHandler = (_, _) => cts.Cancel();

            verifyButton.Enabled = false;

            Program.busy = true;

            try
            {
                VerifyRsp final = await TcpManager.SendVerifyAsync(cmd, progress, cts.Token);

                // Compose summary
                ulong total = final.NumCorrect + final.NumIncorrect;
                double corruptedPercent = total > 0 ? (final.NumIncorrect * 100.0 / total) : 0.0;
                double seconds = final.TimeSpentMs / 1000.0;

                verificationResults.Text =
                    $"Finished verification in {seconds:F2} seconds!\n\n" +
                    $"Correct bits:   {final.NumCorrect:N0} ({Tools.FormatBytes(final.NumCorrect / 8)})\n" +
                    $"Incorrect bits: {final.NumIncorrect:N0}\n\n" +
                    $"{corruptedPercent:F2}% of the bits were corrupted.";

                // Render the authoritative final response (mirrors the summary-text handling above).
                double[] bitBinsFinal = final.ErrBins?.Select(v => (double)v).ToArray() ?? Array.Empty<double>();
                double[] adjBinsFinal = final.AdjErrBins?.Select(v => (double)v).ToArray() ?? Array.Empty<double>();

                barPlot1 = UpdateBars(plot1, barPlot1, bitBinsFinal);
                barPlot2 = UpdateBars(plot2, barPlot2, adjBinsFinal);

                Program.taskInfo = $"Verify complete in {seconds:F1}s";
            }
            catch (OperationCanceledException)
            {
                Program.taskInfo = "Verify cancelled.";

                //Consume the UUID
                UUID.used = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Verify failed: {ex.Message}");

                //Consume the UUID
                UUID.used = true;
            }
            finally
            {

                //Mark beam inactive
                if (prevBeam)
                {
                    //Test ended with active beam
                    endTime = DateTime.Now;
                }

                verifyButton.Enabled = true;

                //Consume the UUID
                UUID.invalidate();
                Program.busy = false;
            }
        }

        private void applyConfiguration_Click(object sender, EventArgs e)
        {
            // Derive NumBlocks (= selection_size / BlockSize) and sample_size from the
            // current combo selections BEFORE sending. The combo handlers only set
            // selection_size / BlockSize / BlockFactor individually and never recompute
            // NumBlocks, so without this the struct's stale NumBlocks is what gets sent to
            // the server and cached as the committed geometry — leaving the client and
            // server disagreeing on the block count, which is what let the viewer address
            // out of range in the first place.
            DDR4_TestingApp.Config.updateCalculations();
            DDR4_TestingApp.Config.apply();
        }

        private void chip_capacity_SelectedIndexChanged(object sender, EventArgs e)
        {
            uint sb = 0;

            /*
            4 KB
            64 KB0
            1 MB
            16 MB
            256 MB
            512 MB
            1 GB
             */

            switch (selection_size.SelectedIndex)
            {
                case 0:
                    // 4 KB
                    sb = 0x1000;
                    break;
                case 1:
                    // 64 KB
                    sb = 0x10000;
                    break;
                case 2:
                    // 1 MB
                    sb = 0x100000;
                    break;
                case 3:
                    // 16 MB
                    sb = 0x1000000;
                    break;
                case 4:
                    // 256 MB
                    sb = 0x10000000;
                    break;
                case 5:
                    // 512 MB
                    sb = 0x20000000;
                    break;
                case 6:
                    // 1 GB
                    sb = 0x40000000;
                    break;
                default:
                    // No/invalid selection (e.g. SelectedIndex == -1) — safe minimum
                    sb = 0x1000;
                    break;
            }

            Program.selection_size = sb;
        }

        private void chipOrg_SelectedIndexChanged(object sender, EventArgs e)
        {
            uint sb = 0;

            switch (block_size.SelectedIndex)
            {
                case 0:
                    // 4 KB
                    sb = 0x1000;
                    break;
                case 1:
                    // 64 KB
                    sb = 0x10000;
                    break;
                case 2:
                    // 1 MB
                    sb = 0x100000;
                    break;
                case 3:
                    // 16 MB
                    sb = 0x1000000;
                    break;
                case 4:
                    // 256 MB
                    sb = 0x10000000;
                    break;
                case 5:
                    // 512 MB
                    sb = 0x20000000;
                    break;
                case 6:
                    // 1 GB
                    sb = 0x40000000;
                    break;
                default:
                    // No/invalid selection (e.g. SelectedIndex == -1) — safe minimum
                    sb = 0x1000;
                    break;
            }

            DDR4_TestingApp.Config.sys.BlockSize = sb;

        }

        private void DataViewerScrollUp_Click(object sender, EventArgs e)
        {
            // Step to the previous sampled block (bounded by the control's Minimum).
            if (viewerBlockNum.Value > viewerBlockNum.Minimum) viewerBlockNum.Value = viewerBlockNum.Value - 1;

        }

        private void DataViewerScrollDown_Click(object sender, EventArgs e)
        {
            // Step to the next sampled block. Maximum is kept in sync with the applied
            // geometry (sampledBlocks - 1) by the viewer refresh, so this can't scroll past
            // the blocks the server actually has. NOTE: the old bound divided
            // Config.sys.NumBlocks by Config.sys.BlockFactor inline, which threw
            // DivideByZeroException whenever BlockFactor was 0 (e.g. before any Config).
            if (viewerBlockNum.Value < viewerBlockNum.Maximum) viewerBlockNum.Value = viewerBlockNum.Value + 1;

        }


        private async void dyn_execute_Click(object sender, EventArgs e)
        {
            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                MessageBox.Show("Not connected.");
                return;
            }

            Program.taskName = "DYNAMIC";
            Program.taskProgress = 50;

            //Clear all previous data
            dynDataX.Clear();
            dynDataY.Clear();

            //Ensure a UUID is present
            if (UUID.GetUuid() is null)
            {
                MessageBox.Show("No valid UUID ready");
                return;
            }


            var cmd = new DynamicCmd
            {
                //Update UUID
                Uuid = UUID.GetUuid().Value,

                // Pattern generation
                Pattern = (byte)dyn_pattern.SelectedIndex,
                Seed = UInt32.Parse(prngSeed.Text),

                SampleSizeInBytes = UInt32.Parse(dyn_bps.Text),
                WaitForBeam = (bool)(dyn_beam.Checked),

                // SEFI threshold
                TriggerThreshold = dyn_trigger_threshold
            };

            // Per-run throughput state (captured by the progress callback below).
            var rateClock = System.Diagnostics.Stopwatch.StartNew();
            ulong lastBytes = 0;

            var progress = new Progress<DynamicRsp>(rsp =>
            {

                // Update status pane
                if (!rsp.SefiDetected)
                {
                    if (!rsp.ExposureStarted) dynStage.Text = "WAITING";
                    else dynStage.Text = "RUNNING";
                }
                else
                {
                    dynStage.Text = "TRIGGERED";
                }

                //Add data to collection
                dynDataX.Add(rsp.ExposureTimeMs);

                if (!double.IsNaN(rsp.ErrorRatePercent) && !double.IsInfinity(rsp.ErrorRatePercent))
                    dynDataY.Add(rsp.ErrorRatePercent);

                double cutoff = rsp.ExposureTimeMs - 10000;
                while (dynDataX.Count > 0 && dynDataX[0] < cutoff)
                {
                    dynDataX.RemoveAt(0);
                    dynDataY.RemoveAt(0);
                }

                dynPlot.Plot.Axes.AutoScaleX();   // now fits only the retained window
                dynPlot.Refresh();
                //Calculate bit error rate per thousand
                float err_per_thousand = ((float)rsp.ErrorRate / (cmd.SampleSizeInBytes * 8)) * 1000.0f;

                dynSEFI.Text = $"{Math.Round(err_per_thousand)} per thousand";

                // Update status indicators
                {
                    if (rsp.BeamSignal) { beamInd.BackColor = Color.Green; }
                    else { beamInd.BackColor = Color.Red; }

                    if (rsp.ControllerCalibrated) { calInd.BackColor = Color.Green; }
                    else { calInd.BackColor = Color.Red; }

                    if (rsp.UiClock) { uiInd.BackColor = Color.Green; }
                    else { uiInd.BackColor = Color.Red; }

                    if (rsp.PlClock) { plInd.BackColor = Color.Green; }
                    else { plInd.BackColor = Color.Red; }

                    if (rsp.FpgaLoaded) { loadedInd.BackColor = Color.Green; }
                    else { loadedInd.BackColor = Color.Red; }
                }


                //Update status boxes
                dynTotalTime.Text = rsp.TotalTimeMs.ToString() + " ms";
                dynExposureTime.Text = rsp.ExposureTimeMs.ToString() + " ms";
                dynTotalTime.Text = rsp.TotalTimeMs.ToString() + " ms";
                dynExposureTime.Text = rsp.ExposureTimeMs.ToString() + " ms";

                // Instantaneous throughput. Do the delta in the integer domain (exact), then
                // divide by the REAL inter-callback interval — progress frames land ~100ms
                // apart but not precisely, and TotalBytes is a billions-range counter, so a
                // float subtraction here would be catastrophic cancellation + timing jitter.
                ulong nowBytes = rsp.TotalBytes;
                ulong deltaBytes = nowBytes >= lastBytes ? nowBytes - lastBytes : 0; // guard run restart
                lastBytes = nowBytes;

                double secs = rateClock.Elapsed.TotalSeconds;
                rateClock.Restart();
                ulong bytesPerSec = secs > 0 ? (ulong)(deltaBytes / secs) : 0;

                dynBytes.Text = $"{Tools.FormatBytes(nowBytes)} ({Tools.FormatBytes(bytesPerSec)}/sec)";


                //Check if SEFI occured
                if (!rsp.SefiDetected)
                {
                    // No SEFI
                    dynSEFI.Text = "NO FAULTS";
                    dynUntilSEFI.Text = "==========";

                }
                else
                {
                    dynSEFI.Text = "SEFI DETECTED";
                    dynUntilSEFI.Text = rsp.TimeToSefi.ToString() + " ms";
                }

                //Update rate and error stats
                dynBitErrors.Text = rsp.ErrorRate.ToString() + " bit(s)";
                dynRateTime.Text = Tools.FormatBytes(Convert.ToUInt64(rsp.ErrorRatePerSecond) / 8) + "/sec";
                dynRatePercent.Text = rsp.ErrorRatePercent.ToString();


                // Render the bar plots from this update packet.
                double[] bitBins = rsp.ErrBins?.Select(v => (double)v).ToArray() ?? Array.Empty<double>();
                double[] adjBins = rsp.AdjErrBins?.Select(v => (double)v).ToArray() ?? Array.Empty<double>();

                dynbarPlot2 = UpdateBars(dynPlot2, dynbarPlot2, bitBins);
                dynbarPlot3 = UpdateBars(dynPlot3, dynbarPlot3, adjBins);


                //Render Memory Viewer
                {
                    const uint rows = MEM_VIEWER_ROWS;            // class const, = 21

                    uint start_addr = 0;
                    uint end_addr = DDR4_TestingApp.Config.sys.BlockSize * DDR4_TestingApp.Config.sys.NumBlocks;
                    uint step = (end_addr - start_addr) / rows;   // bytes represented by one row
                    if (step == 0) step = 1;                      // guard: chip smaller than the row count

                    // --- Where the device currently is ---
                    // If DynamicRsp reports the address under test, use it directly, e.g.:
                    //     uint currentAddr = rsp.CurrentAddress;
                    // The fallback below only works if the scan walks the chip linearly and wraps.
                    uint currentAddr = rsp.CurrentAddress;
                    uint currentRow = Math.Min((currentAddr - start_addr) / step, rows - 1);

                    // --- Record this row's rate so it persists after the cursor moves off it ---
                    // err_per_thousand is the per-thousand rate already computed just above.
                    if (!float.IsNaN(err_per_thousand) && !float.IsInfinity(err_per_thousand))
                        _memBandRate[currentRow] = err_per_thousand;

                    // --- Build the table ---
                    String txt = "";
                    for (uint b = 0; b < rows; b++)
                    {
                        uint rowAddr = start_addr + b * step;

                        string cursor = (b == currentRow) ? "===>" : "    ";        // col 1: cursor / blank (same width)
                        string rate = _memBandRate[b].HasValue                      // col 2: last-seen rate, 3 digits
                            ? ((uint)Math.Min(_memBandRate[b].Value, 999f)).ToString("000")
                            : "---";                                                //         dashes if never visited
                        string addr = Tools.ToHexString(rowAddr);                   // col 3: this row's address

                        txt += $"  {cursor}  | {rate} | {addr} " + Environment.NewLine;
                    }

                    memoryViewer.Text = txt;   // point this at your viewer control (see note)
                }

            });

            using var cts = new CancellationTokenSource();
            EventHandler cancelHandler = (_, _) => cts.Cancel();

            verifyButton.Enabled = false;
            Program.busy = true;

            try
            {
                await TcpManager.RunDynamicAsync(cmd, progress, cts.Token);
            }
            catch (OperationCanceledException)
            {
                Program.taskInfo = "Verify cancelled.";

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Verify failed: {ex.Message}");

            }
            finally
            {
                //Task complete
                Program.taskProgress = 100;

                //Fill in info
                dynStage.Text = "DONE";

                UUID.invalidate();
                Program.busy = false;
                verifyButton.Enabled = true;

            }
        }

        private void dyn_trigger_bar_Scroll(object sender, EventArgs e)
        {
            //Update from bar
            dyn_trigger_threshold = ((float)dyn_trigger_bar.Value) / 100;

            //Propagate to textbox
            dyn_trigger_box.Text = dyn_trigger_threshold.ToString();
        }

        private void dyn_trigger_box_TextChanged(object sender, EventArgs e)
        {
            // Update from textbox; any non-float zeroes both
            if (!float.TryParse(dyn_trigger_box.Text, out dyn_trigger_threshold))
            {
                dyn_trigger_threshold = 0;
                dyn_trigger_bar.Value = 0;
                return;
            }

            // Clamp overflows
            if (dyn_trigger_threshold > 1)
            {
                dyn_trigger_threshold = 1;
                dyn_trigger_box.Text = "1";
            }
            else if (dyn_trigger_threshold < 0)
            {
                dyn_trigger_threshold = 0;
                dyn_trigger_box.Text = "0";
            }

            // Propagate to bar (clamped to valid range as a guard)
            dyn_trigger_bar.Value = Math.Clamp((int)(dyn_trigger_threshold * 100), 0, 100);
        }

        private void enableLogs_CheckedChanged(object sender, EventArgs e)
        {
            DDR4_TestingApp.Config.sys.EnableLogging = enableLogs.Checked;
        }

        private void setUUID_Click(object sender, EventArgs e)
        {
            // Disable auto management
            autoManageUUID.Checked = false;

            // Fall back to 0 if the box isn't a valid u16
            // (non-numeric, empty, negative, or > 65535 all land here).
            if (!UInt16.TryParse(inputUUID.Text, out ushort uuid))
            {
                inputUUID.BackColor = Color.LightCoral;
            }
            else
            {
                UUID.SetUUID(uuid);
                inputUUID.BackColor = Color.LightGreen;
            }
        }

        private void selectSaveLocation_Click_1(object sender, EventArgs e)
        {
            dumpPath.Text = Tools.SelectFolder(dumpPath.Text);
        }

        private void captureModeRaw_Click_1(object sender, EventArgs e)
        {
            captureDiff = false;
        }

        private void captureModeDiff_Click(object sender, EventArgs e)
        {
            captureDiff = true;
        }

        private async void dumpButton_Click_1(object sender, EventArgs e)
        {
            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected || !Info.sys.HasValue)
            {
                MessageBox.Show("Not connected.");
                return;
            }

            // currentDumpPage layout — matches DumpTableFormatter's canonical view
            // (1 byte per cell, 16 cells per row).
            const uint UnitSize = 1;
            const uint RowSize = 16;

            // Whole-chip dump from offset 0. uint/uint (PAGE_SIZE is int, hence the cast)

            Program.taskName = "DUMP";
            var cmd = new DumpCmd { BlockOffset = 0, NumBlocks = DDR4_TestingApp.Config.sys.NumBlocks, ComparisonMode = captureDiff };

            // The server streams one page at a time and can emit hundreds of thousands
            // of them for a full-chip dump, so throttle the expensive per-page redraw to
            // ~100ms while still counting every page. The final page is drawn
            // deterministically after the await, so throttling can't drop it.
            int pagesReceived = 0;
            long bytesReceived = 0;
            long errorsSeen = 0;
            var opClock = System.Diagnostics.Stopwatch.StartNew();
            long lastRedrawMs = -1000;
            const long RedrawThrottleMs = 100;

            var progress = new Progress<DumpPage>(page =>
            {
                pagesReceived++;
                bytesReceived += page.Data.Length;
                errorsSeen += (long)page.NumErrors;
                Program.taskProgress = ((float)page.Address / (float)(DDR4_TestingApp.Config.sys.BlockSize * DDR4_TestingApp.Config.sys.NumBlocks)) * 100;

                // Cheap single-line update every page.
                currentDumpAddress.Text = $"0x{page.Address:X8}";

                // Expensive multi-line status + full-page table, throttled.
                if (opClock.ElapsedMilliseconds - lastRedrawMs >= RedrawThrottleMs)
                {
                    lastRedrawMs = opClock.ElapsedMilliseconds;
                    dumpStatus.Text = BuildDumpStatus("Dumping",
                                                      page.Address, bytesReceived, errorsSeen, opClock.Elapsed);
                    DumpTableFormatter.RenderBytesHexTable(currentDumpPage, page.Address, page.Data, UnitSize, RowSize);
                }
            });

            // Nothing currently triggers cancellation — hook cts.Cancel() to a cancel
            // button if/when you add one (this is what the old unused cancelHandler was for).
            using var cts = new CancellationTokenSource();

            dumpButton.Enabled = false;
            Program.busy = true;
            try
            {
                var pages = await TcpManager.SendDumpAsync(cmd, progress, cts.Token);

                // Persist raw bytes to disk. A whole-chip dump can be tens-to-hundreds
                // of MB and this continuation resumes on the UI thread, so writing
                // inline would freeze the window for the whole write. Read the control
                // text here (UI thread), then do the file I/O on a worker thread.
                string path = Path.Combine(dumpPath.Text, dumpFileName.Text + ".bin");
                await Task.Run(() =>
                {
                    using var fs = File.Create(path);
                    foreach (var page in pages)
                        fs.Write(page.Data, 0, page.Data.Length);
                }, cts.Token);

                long totalBytes = pages.Sum(p => (long)p.Data.Length);
                long totalErrors = pages.Sum(p => (long)p.NumErrors);

                // Deterministic final render of the last page (don't rely on the
                // throttled callback having drawn it).
                if (pages.Count > 0)
                {
                    DumpPage last = pages[pages.Count - 1];
                    currentDumpAddress.Text = $"0x{last.Address:X8}";
                    DumpTableFormatter.RenderBytesHexTable(currentDumpPage, last.Address, last.Data, UnitSize, RowSize);
                }

                Program.taskProgress = 100;
                dumpStatus.Text =
                    $"Dump complete.\n" +
                    $"Bytes:   {totalBytes:N0}\n" +
                    $"Errors:  {totalErrors:N0}\n" +
                    $"Elapsed: {opClock.Elapsed:hh\\:mm\\:ss}\n" +
                    $"Saved:   {path}";
            }
            catch (OperationCanceledException)
            {
                dumpStatus.Text = $"Dump cancelled.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Dump failed: {ex.Message}");
                dumpStatus.Text = $"Dump failed: {ex.Message}";
            }
            finally
            {
                dumpButton.Enabled = true;
                Program.busy = false;
            }
        }

        // Multi-line detail block for dumpStatus.
        private static string BuildDumpStatus(
            string state, uint address, long bytes, long errors, TimeSpan elapsed)
        {
            return
                $"{state}...\n" +
                $"Progress:   {((float)address / (float)(DDR4_TestingApp.Config.sys.BlockSize * DDR4_TestingApp.Config.sys.NumBlocks)) * 100.0}%\n" +
                $"Address: 0x{address:X8}\n" +
                $"Bytes:   {bytes:N0}\n" +
                $"Errors:  {errors:N0}\n" +
                $"Elapsed: {elapsed:hh\\:mm\\:ss}";
        }

        private void useLock_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void block_factor_SelectedIndexChanged(object sender, EventArgs e)
        {
            /*
            1
            2
            4
            8
            16
            32
            64
            128
             */

            uint bf = 0;

            switch (block_factor.SelectedIndex)
            {
                case 0:
                    bf = 1;
                    break;
                case 1:
                    bf = 2;
                    break;
                case 2:
                    bf = 4;
                    break;
                case 3:
                    bf = 8;
                    break;
                case 4:
                    bf = 16;
                    break;
                case 5:
                    bf = 32;
                    break;
                case 6:
                    bf = 64;
                    break;
                case 7:
                    bf = 128;
                    break;
                default:
                    // No/invalid selection (e.g. SelectedIndex == -1) — neutral factor (full coverage)
                    bf = 1;
                    break;
            }

            DDR4_TestingApp.Config.sys.BlockFactor = bf;
        }
    }
}