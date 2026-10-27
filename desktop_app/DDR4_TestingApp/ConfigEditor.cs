#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DDR4_TestingApp
{
    public partial class ConfigEditor : Form
    {
        // Set while pushing Config -> controls so the SelectedIndexChanged /
        // TextChanged handlers don't bounce the value straight back into Config.
        private bool loading;

        private readonly CancellationTokenSource formClosing = new CancellationTokenSource();

        // Indicator colours, matching the engine status pill on MainForm.
        private static readonly Color SyncedColor = Color.FromArgb(0x27, 0xAE, 0x60);
        private static readonly Color StaleColor = Color.FromArgb(0xC0, 0x39, 0x2B);
        private static readonly Color UnknownColor = Color.FromArgb(0x7F, 0x8C, 0x8D);

        private static readonly Random seedSource = new Random();

        /// <summary>
        /// Byte values behind the "4 KB"… "1 GB" items shared by selectionSize and
        /// blockSize. Index order must match the designer's Items.AddRange.
        /// </summary>
        private static readonly uint[] SizeTable =
        {
            0x0000_1000,  // 4 KB
            0x0001_0000,  // 64 KB
            0x0010_0000,  // 1 MB
            0x0100_0000,  // 16 MB
            0x1000_0000,  // 256 MB
            0x2000_0000,  // 512 MB
            0x4000_0000,  // 1 GB
        };

        /// <summary>
        /// The dataPattern combo reads ONES, ZEROS, PRNG — which is NOT the order of the
        /// server's pattern byte (0 = zeros, 1 = ones, 2 = prng). The old MainForm did
        /// `Pattern = (byte)SelectedIndex`, which with this combo would write zeros when
        /// the operator picked ones. Map explicitly.
        /// </summary>
        private static readonly DataPattern[] PatternByIndex =
        {
            DataPattern.Ones,
            DataPattern.Zeros,
            DataPattern.PseudoRandom,
        };

        public ConfigEditor()
        {
            InitializeComponent();

            ConfigureControls();
            WireEvents();
            LoadFromConfig();

            Config.Changed += OnConfigChanged;
            TcpManager.StatusChanged += OnConnectionStatusChanged;

            Shown += ConfigEditor_Shown;
            FormClosed += ConfigEditor_FormClosed;
        }

        // ============================== Lifecycle ==============================

        private async void ConfigEditor_Shown(object? sender, EventArgs e)
        {
            RefreshSamplingInfo();
            RefreshSyncStatus();
            await RefreshFpgaImageAsync();
        }

        private void ConfigEditor_FormClosed(object? sender, FormClosedEventArgs e)
        {
            // Both are STATIC events — without these the form is kept alive for the
            // life of the process and its handlers keep firing after disposal.
            Config.Changed -= OnConfigChanged;
            TcpManager.StatusChanged -= OnConnectionStatusChanged;
            formClosing.Cancel();
        }

        private void ConfigureControls()
        {
            // Free text in these would have to be parsed; the items are the contract.
            selectionSize.DropDownStyle = ComboBoxStyle.DropDownList;
            blockSize.DropDownStyle = ComboBoxStyle.DropDownList;
            blockFactor.DropDownStyle = ComboBoxStyle.DropDownList;
            dataPattern.DropDownStyle = ComboBoxStyle.DropDownList;

            samplingInfo.ReadOnly = true;
            addrInfo.ReadOnly = true;
            currentImage.ReadOnly = true;
            lastSync.ReadOnly = true;

            // 0.1 % resolution over 0..100 %. SefiThreshold itself is a FRACTION
            // (0..1) on the wire, so the slider is 1000x the stored value.
            sefiThresholdSlider.Minimum = 0;
            sefiThresholdSlider.Maximum = 1000;
            sefiThresholdSlider.TickFrequency = 100;
            sefiThresholdSlider.SmallChange = 1;
            sefiThresholdSlider.LargeChange = 10;

            // A disabled Button ignores BackColor under the default FlatStyle — the
            // themed renderer paints system colours instead. Flat makes the red/green show.
            syncStatus.FlatStyle = FlatStyle.Flat;
            syncStatus.FlatAppearance.BorderSize = 0;
            syncStatus.ForeColor = Color.White;
            syncStatus.Enabled = false;
            syncStatus.TabStop = false;
        }

        private void WireEvents()
        {
            selectionSize.SelectedIndexChanged += selectionSize_SelectedIndexChanged;
            blockSize.SelectedIndexChanged += blockSize_SelectedIndexChanged;
            blockFactor.SelectedIndexChanged += blockFactor_SelectedIndexChanged;

            dataPattern.SelectedIndexChanged += dataPattern_SelectedIndexChanged;
            dataSeed.TextChanged += dataSeed_TextChanged;
            regenSeed.Click += regenSeed_Click;

            sefiThresholdSlider.ValueChanged += sefiThresholdSlider_ValueChanged;
            enableAutoSEFIMode.CheckedChanged += enableAutoSEFIMode_CheckedChanged;

            flashFPGA.Click += flashFPGA_Click;
            sendConfig.Click += sendConfig_Click;

            // sefiThreshold.TextChanged is already wired by the designer.
        }

        // ============================== Config -> UI ==============================

        /// <summary>
        /// Push the current configuration into the input controls. Only called
        /// explicitly (construction, or after a preset load) — never from
        /// <see cref="OnConfigChanged"/>, so a UI edit can't loop back on itself.
        /// </summary>
        public void LoadFromConfig()
        {
            loading = true;
            try
            {
                selectionSize.SelectedIndex = IndexForSize(Program.selection_size);
                blockSize.SelectedIndex = IndexForSize(Config.BlockSize);
                blockFactor.SelectedIndex = IndexForFactor(Config.BlockFactor);

                dataPattern.SelectedIndex = Array.IndexOf(PatternByIndex, Config.Pattern);
                dataSeed.Text = ((uint)Config.PatternSeed).ToString("X8");

                int slider = (int)Math.Round(Config.SefiThreshold * 1000.0 * 100.0 / 100.0);
                sefiThresholdSlider.Value = Clamp(slider, 0, 1000);
                sefiThreshold.Text = (Config.SefiThreshold * 100.0).ToString("0.###");

                enableAutoSEFIMode.Checked = Config.AutomatedSefiMode;
            }
            finally
            {
                loading = false;
            }

            RefreshSamplingInfo();
            RefreshSyncStatus();
        }

        /// <summary>
        /// Config changed — possibly from another form. Refresh the read-only displays
        /// only; touching the inputs here would re-enter the handler that caused it.
        /// </summary>
        private void OnConfigChanged(ConfigCmd cfg)
        {
            if (IsDisposed || loading) return;
            RefreshSamplingInfo();
            RefreshSyncStatus();
        }

        // ============================== Memory Sampling ==============================

        private void selectionSize_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (loading) return;
            Program.selection_size = SizeAt(selectionSize.SelectedIndex);
            RefreshSamplingInfo();
        }

        private void blockSize_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (loading) return;
            Config.BlockSize = SizeAt(blockSize.SelectedIndex);
            RefreshSamplingInfo();
        }

        private void blockFactor_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (loading) return;

            // Items are "1".."128". A parse failure floors at 1 — BlockFactor of 0
            // panics the server's engine thread on step_by(0).
            Config.BlockFactor =
                uint.TryParse(blockFactor.SelectedItem?.ToString(), out uint f) && f > 0 ? f : 1u;

            RefreshSamplingInfo();
        }

        /// <summary>
        /// Recompute the derived geometry and repaint both information panes. This is
        /// the replacement for MainForm.updateAddrInfo().
        ///
        /// The old version computed "Blocks Sampled" as NumBlocks / BlockFactor, a
        /// FLOOR. step_by yields 0, f, 2f, … so a NumBlocks that isn't a multiple of
        /// the factor still gets one final partial stride; Config.SampledBlocks is the
        /// ceiling and is what the server actually visits.
        /// </summary>
        private void RefreshSamplingInfo()
        {
            if (IsDisposed) return;

            // Derives NumBlocks from Program.selection_size and republishes
            // Program.sample_size. Must run before anything reads the derived values.
            Config.updateCalculations();

            addrInfo.Text =
                $"Start Address: \t{Hex(0)}\n" +
                $"End Address: \t\t{Hex(Config.ExtentBytes)}\n";

            string warnings = "";
            foreach (string w in Config.Warnings())
                warnings += "\n! " + w;

            samplingInfo.Text =
                $"Total Number of Blocks: \t{Config.NumBlocks}\n" +
                $"Blocks Sampled: \t\t{Config.SampledBlocks}\n" +
                $"Block Size: \t\t\t{Hex(Config.BlockSize)} ( {Bytes(Config.BlockSize)} )\n" +
                $"Block Factor: \t\t{Config.BlockFactor}\n\n" +
                $"Selection Size: \t\t{Hex(Program.selection_size)} ( {Bytes(Program.selection_size)} )\n" +
                $"Sample Size: \t\t\t{Hex(Config.SampledBytes)} ( {Bytes(Config.SampledBytes)} )\n\n" +
                $"Percent Sampled: \t\t{Config.CoveragePercent:0.##}%\n" +
                $"Capture per Pass: \t\t{Bytes(Config.EstimatedCaptureBytesPerPass)}" +
                warnings;

            RefreshSyncStatus();
        }

        // ============================== Data pattern ==============================

        private void dataPattern_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (loading) return;

            int i = dataPattern.SelectedIndex;
            if (i >= 0 && i < PatternByIndex.Length)
                Config.Pattern = PatternByIndex[i];

            dataSeed.Enabled = Config.Pattern == DataPattern.PseudoRandom;
            regenSeed.Enabled = dataSeed.Enabled;
        }

        /// <summary>Seed is read as HEX, matching the box's "00000000" default.</summary>
        private void dataSeed_TextChanged(object? sender, EventArgs e)
        {
            if (loading) return;

            string text = dataSeed.Text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);

            if (uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                              out uint seed))
            {
                Config.PatternSeed = seed;
                dataSeed.BackColor = SystemColors.Window;
            }
            else
            {
                // Don't fight the operator mid-keystroke; just flag it.
                dataSeed.BackColor = Color.MistyRose;
            }
        }

        private void regenSeed_Click(object? sender, EventArgs e)
        {
            uint seed = unchecked((uint)seedSource.Next(int.MinValue, int.MaxValue));
            dataSeed.Text = seed.ToString("X8");   // TextChanged commits it
        }

        // ============================== SEFI ==============================

        private void sefiThresholdSlider_ValueChanged(object? sender, EventArgs e)
        {
            if (loading) return;

            loading = true;
            try
            {
                double percent = sefiThresholdSlider.Value / 10.0;
                Config.SefiThreshold = percent / 100.0;
                sefiThreshold.Text = percent.ToString("0.###");
            }
            finally { loading = false; }

            RefreshSyncStatus();
        }

        /// <summary>
        /// Wired by the designer. The box holds a PERCENT; Config.SefiThreshold holds the
        /// fraction the server compares against (ratio &gt;= threshold).
        /// </summary>
        private void sefiThreshold_TextChanged(object sender, EventArgs e)
        {
            if (loading) return;

            if (!double.TryParse(sefiThreshold.Text.Trim(), NumberStyles.Float,
                                 CultureInfo.InvariantCulture, out double percent))
            {
                sefiThreshold.BackColor = Color.MistyRose;
                return;
            }

            percent = Math.Max(0.0, Math.Min(100.0, percent));
            sefiThreshold.BackColor = SystemColors.Window;

            loading = true;
            try
            {
                Config.SefiThreshold = percent / 100.0;
                sefiThresholdSlider.Value = Clamp((int)Math.Round(percent * 10.0), 0, 1000);
            }
            finally { loading = false; }

            RefreshSyncStatus();
        }

        private void enableAutoSEFIMode_CheckedChanged(object? sender, EventArgs e)
        {
            if (loading) return;
            Config.AutomatedSefiMode = enableAutoSEFIMode.Checked;
            RefreshSyncStatus();
        }

        // ============================== FPGA ==============================

        private async Task RefreshFpgaImageAsync()
        {
            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                currentImage.Text = "(not connected)";
                return;
            }

            try
            {
                string? name = await TcpManager.GetFpgaImageAsync(formClosing.Token);
                if (IsDisposed) return;

                // The server tracks this in memory only, so it reads null after a
                // restart even when the PL is configured.
                currentImage.Text = name ?? "(unknown)";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (IsConnectionFault(ex))
            {
                if (!IsDisposed) currentImage.Text = "(unavailable)";
            }
        }

        private async void flashFPGA_Click(object? sender, EventArgs e)
        {
            string name = (fpgaImages.SelectedItem?.ToString() ?? fpgaImages.Text).Trim();
            if (name.Length == 0)
            {
                MessageBox.Show("Pick an image name first.");
                return;
            }

            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                MessageBox.Show("Not connected.");
                return;
            }

            if (MessageBox.Show(
                    $"Load '{name}.bit.bin' into the PL?\n\n" +
                    "The DDR4 controller will re-train afterwards, so queued work will " +
                    "stall until calibration comes back.",
                    "Flash FPGA", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning)
                != DialogResult.OK)
                return;

            flashFPGA.Enabled = false;
            try
            {
                bool ok = await TcpManager.SetFpgaImageAsync(name, formClosing.Token);
                if (IsDisposed) return;

                if (!ok)
                    MessageBox.Show(
                        "Flash refused. The server rejects the request when the image " +
                        "isn't in /dvr/fpga, the engine is busy, or fpgautil fails.");

                await RefreshFpgaImageAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!IsDisposed) MessageBox.Show($"Flash failed: {ex.Message}");
            }
            finally
            {
                if (!IsDisposed) flashFPGA.Enabled = true;
            }
        }

        // ============================== Sync ==============================

        private async void sendConfig_Click(object? sender, EventArgs e)
        {
            // Make sure NumBlocks reflects the current combo selections before sending.
            // The old MainForm had the same call here for the same reason: without it a
            // stale NumBlocks goes to the server and gets cached as the committed
            // geometry, leaving client and server disagreeing on the block count.
            Config.updateCalculations();

            if (!Config.TryValidate(out string why))
            {
                MessageBox.Show(why, "Configuration rejected",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            sendConfig.Enabled = false;
            try
            {
                bool ok = await Config.ApplyAsync(formClosing.Token);
                if (IsDisposed) return;

                if (ok)
                    lastSync.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                else
                    MessageBox.Show(Config.LastError ?? "Config failed.", "Configuration",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (!IsDisposed)
                {
                    sendConfig.Enabled = true;
                    RefreshSyncStatus();
                }
            }
        }

        /// <summary>
        /// Repaint the sync indicator. "IN SYNC" means the local configuration matches
        /// the last one this client pushed — the server has no GETCONFIG, so that is the
        /// strongest claim available. After a reconnect the committed value is cleared
        /// and this reads out of date even if nothing changed.
        /// </summary>
        private void RefreshSyncStatus()
        {
            if (IsDisposed) return;

            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                SetSyncPill("NOT CONNECTED", UnknownColor);
                return;
            }

            if (Config.IsDirty) SetSyncPill("OUT OF DATE", StaleColor);
            else SetSyncPill("IN SYNC", SyncedColor);
        }

        private void SetSyncPill(string text, Color back)
        {
            if (syncStatus.Text != text) syncStatus.Text = text;
            if (syncStatus.BackColor != back) syncStatus.BackColor = back;
        }

        private void OnConnectionStatusChanged(TcpManager.ConnectionStatus status)
        {
            if (IsDisposed || !IsHandleCreated) return;

            // Can arrive from a pool thread when a framing fault drops the link.
            if (InvokeRequired)
                BeginInvoke((Action)(() => OnConnectionStatusChangedCore()));
            else
                OnConnectionStatusChangedCore();
        }

        private async void OnConnectionStatusChangedCore()
        {
            if (IsDisposed) return;
            RefreshSyncStatus();
            await RefreshFpgaImageAsync();
        }

        // ============================== Helpers ==============================

        private static uint SizeAt(int index) =>
            index >= 0 && index < SizeTable.Length ? SizeTable[index] : SizeTable[0];

        /// <summary>Combo index for a byte count, or -1 when it isn't one of the presets.</summary>
        private static int IndexForSize(uint bytes) => Array.IndexOf(SizeTable, bytes);

        private static int IndexForFactor(uint factor)
        {
            // Items are powers of two, 1..128.
            for (int i = 0, v = 1; i < 8; i++, v <<= 1)
                if (factor == (uint)v) return i;
            return -1;
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        /// <summary>
        /// Tools.ToHexString takes a uint; the derived extents are 64-bit and can reach
        /// exactly 4 GiB, which doesn't fit. Fall back to a plain format above that.
        /// </summary>
        private static string Hex(ulong v) =>
            v <= uint.MaxValue ? Tools.ToHexString((uint)v) : $"0x{v:X}";

        private static string Bytes(ulong v) =>
            v <= uint.MaxValue ? Tools.FormatBytes((uint)v)
                               : $"{v / (1024.0 * 1024.0 * 1024.0):0.##} GB";

        private static bool IsConnectionFault(Exception ex) =>
            ex is TimeoutException
            || ex is IOException
            || ex is SocketException
            || ex is InvalidDataException
            || ex is InvalidOperationException;
    }
}