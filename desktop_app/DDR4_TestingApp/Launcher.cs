using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;

namespace DDR4_TestingApp
{
    public partial class Launcher : Form
    {
        public Launcher()
        {
            InitializeComponent();
            TcpManager.StatusChanged += OnConnectionStatusChanged;
            ApplyConnectionStatus(TcpManager.Status);   // reflect state on open
            FormClosed += (s, e) => TcpManager.StatusChanged -= OnConnectionStatusChanged;
            StartEngineStatusPolling();
        }

        private void launchRunEditor_Click(object sender, EventArgs e)
        {
            new RunEditor().Show();
        }

        private void OnConnectionStatusChanged(TcpManager.ConnectionStatus status)
        {
            if (IsDisposed || !IsHandleCreated) return;

            // Disconnect() can be called from a pool thread — TcpManager's internal reads
            // use ConfigureAwait(false), so a framing fault raises this off the UI thread.
            if (InvokeRequired)
                BeginInvoke((Action)(() => ApplyConnectionStatus(status)));
            else
                ApplyConnectionStatus(status);
        }

        private void ApplyConnectionStatus(TcpManager.ConnectionStatus status)
        {
            bool connected = status == TcpManager.ConnectionStatus.Connected;
            CM_connect.Text = connected ? "Disconnect" : "Connect";
            CM_ip.Enabled = !connected;
            CM_port.Enabled = !connected;
        }

        private async void CM_connect_Click(object sender, EventArgs e)
        {
            if (TcpManager.Status == TcpManager.ConnectionStatus.Connected)
            {
                TcpManager.Disconnect();   // synchronous; StatusChanged updates the button
                return;
            }

            if (!ushort.TryParse(CM_port.Text, out ushort port))
            {
                MessageBox.Show("Port must be 0-65535.");
                return;
            }

            CM_connect.Enabled = false;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await TcpManager.ConnectAsync(CM_ip.Text.Trim(), port, cts.Token);
            }
            catch (OperationCanceledException)
            {
                MessageBox.Show("Connection timed out.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Connect failed: {ex.Message}");
            }
            finally
            {
                CM_connect.Enabled = true;
            }
        }


        private static readonly TimeSpan EnginePollInterval = TimeSpan.FromMilliseconds(500);

        private CancellationTokenSource? enginePollCts;

        // Status-pill palette.
        private static readonly Color BusyColor = Color.FromArgb(0xC0, 0x39, 0x2B);
        private static readonly Color IdleColor = Color.FromArgb(0x27, 0xAE, 0x60);
        private static readonly Color OfflineColor = Color.FromArgb(0x7F, 0x8C, 0x8D);

        // ============================== Lifecycle ==============================

        /// <summary>Begin polling. Safe to call twice; the second call is ignored.</summary>
        public void StartEngineStatusPolling()
        {
            if (enginePollCts != null) return;

            ConfigureStatusStrip();

            enginePollCts = new CancellationTokenSource();
            FormClosed += (s, e) => StopEngineStatusPolling();

            // Fire and forget: the loop swallows its own exceptions. Not awaited
            // because it only ends when the form closes.
            _ = PollEngineStatusLoopAsync(enginePollCts.Token);
        }

        public void StopEngineStatusPolling()
        {
            enginePollCts?.Cancel();
            enginePollCts = null;
        }

        /// <summary>
        /// One-time control setup. The progress bar is Continuous so it jumps rather
        /// than easing between values.
        /// </summary>
        private void ConfigureStatusStrip()
        {
            taskProgress.Minimum = 0;
            taskProgress.Maximum = 100;
            taskProgress.Style = ProgressBarStyle.Continuous;

            // A disabled Button ignores BackColor under the default FlatStyle — the
            // themed renderer paints system colours instead. Flat is what makes the
            // red/green actually show up.
            idleStatus.FlatStyle = FlatStyle.Flat;
            idleStatus.FlatAppearance.BorderSize = 0;
            idleStatus.ForeColor = Color.White;
            idleStatus.Enabled = false;
            idleStatus.TabStop = false;

            ApplyEngineOffline();
        }

        // ============================== Poll loop ==============================

        /// <summary>
        /// Self-spacing loop: the next request starts 500 ms after the previous one
        /// FINISHES, so a slow poll can never overlap itself. Continuations land on the
        /// UI thread, so the paint methods need no marshaling.
        /// </summary>
        private async Task PollEngineStatusLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (IsDisposed) return;

                if (TcpManager.Status == TcpManager.ConnectionStatus.Connected)
                {
                    try
                    {
                        EngineStatus status = await TcpManager.GetEngineStatusAsync(ct);
                        if (ct.IsCancellationRequested || IsDisposed) return;
                        ApplyEngineStatus(status);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex) when (IsEngineConnectionFault(ex))
                    {
                        // TcpManager drops the link itself on a framing fault or its own
                        // timeout; next iteration will see Disconnected and paint OFFLINE.
                        if (!IsDisposed) ApplyEngineOffline();
                    }
                }
                else
                {
                    ApplyEngineOffline();
                }

                try
                {
                    await Task.Delay(EnginePollInterval, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        // ============================== Painting ==============================

        private void ApplyEngineStatus(EngineStatus status)
        {
            queueSize.Text = $"{status.Queue.Length} Task(s)";

            // The engine pops a task off the queue before it starts, so the in-flight
            // record is NOT counted above — Queue is strictly what is still pending.
            taskName.Text = status.ActiveRecord == null
                ? "NONE"
                : status.ActiveRecord.Rtype.ToString().ToUpperInvariant();

            SetProgress(status.ActiveRecord == null ? 0.0 : status.RecordProgress);

            if (status.IsStalled)
            {
                // Idle with work queued: the engine refuses to start while /dvr is in
                // safe mode or the MIG calibration line reads low, and it reports no
                // reason. Surfacing it as BUSY would be a lie, IDLE hides a problem.
                SetStatusPill("STALLED", BusyColor);
            }
            else
            {
                SetStatusPill(status.IsIdle ? "IDLE" : "BUSY",
                              status.IsIdle ? IdleColor : BusyColor);
            }
        }

        private void ApplyEngineOffline()
        {
            queueSize.Text = "0 Task(s)";
            taskName.Text = "NONE";
            SetProgress(0.0);
            SetStatusPill("OFFLINE", OfflineColor);
        }

        private void SetStatusPill(string text, Color back)
        {
            if (idleStatus.Text != text) idleStatus.Text = text;
            if (idleStatus.BackColor != back) idleStatus.BackColor = back;
        }

        /// <summary>
        /// Clamp and apply. The server computes progress as a float division, so a
        /// degenerate config can hand back NaN — guard before casting.
        /// </summary>
        private void SetProgress(double percent)
        {
            if (double.IsNaN(percent) || percent < 0) percent = 0;
            if (percent > 100) percent = 100;

            int value = (int)Math.Round(percent);
            if (taskProgress.Value == value) return;

            // Aero animates a rising Value over ~200 ms, which lags a 500 ms poll badly.
            // Overshooting by one and stepping back lands on the target immediately.
            if (value < taskProgress.Maximum)
            {
                taskProgress.Value = value + 1;
                taskProgress.Value = value;
            }
            else
            {
                taskProgress.Value = value;
            }
        }

        private static bool IsEngineConnectionFault(Exception ex) =>
            ex is TimeoutException
            || ex is IOException
            || ex is SocketException
            || ex is InvalidDataException
            || ex is InvalidOperationException;

        private void configEditor_Click(object sender, EventArgs e)
        {
            new ConfigEditor().Show();
        }

        private void launchRecordManager_Click(object sender, EventArgs e)
        {
            new RecordManager().Show();
        }
    }
}
