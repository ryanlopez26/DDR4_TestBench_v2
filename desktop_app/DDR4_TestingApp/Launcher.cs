#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DDR4_TestingApp
{
    public partial class Launcher : Form
    {
        // ====================== Cadences ======================

        private static readonly TimeSpan PowerPollInterval = TimeSpan.FromMilliseconds(150);
        private static readonly TimeSpan InfoPollInterval = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Deliberately slow. EngineStatusRsp embeds the entire in-flight Record, log
        /// arrays included, so during a long dynamic run each poll moves megabytes and
        /// the server holds STATUS.read() across the clone, the serialize and the socket
        /// write. At 500 ms that starved the 150 ms power poll of the shared send lock
        /// and blinked POWER SERVICES.
        /// </summary>
        private static readonly TimeSpan EnginePollInterval = TimeSpan.FromSeconds(5);

        private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(5);
        private const int ReconnectMaxAttempts = 180;        // 5 s x 180 = 15 minutes

        /// <summary>Relay state is re-read this often so the pill shows reality, not intent.</summary>
        private static readonly TimeSpan ContactorVerifyInterval = TimeSpan.FromSeconds(1);

        /// <summary>Half-period of the restart flash.</summary>
        private const long FlashPeriodMs = 400;

        // ====================== Palette ======================

        private static readonly Color TrueColor = Color.FromArgb(0x27, 0xAE, 0x60);
        private static readonly Color FalseColor = Color.FromArgb(0xC0, 0x39, 0x2B);
        private static readonly Color OfflineColor = Color.FromArgb(0x7F, 0x8C, 0x8D);

        private static readonly Color BusyColor = FalseColor;
        private static readonly Color IdleColor = TrueColor;

        private static readonly Color OperationalColor = Color.FromArgb(0x1E, 0x7E, 0x34);  // dark green
        private static readonly Color ShutdownColor = Color.FromArgb(0x8B, 0x1A, 0x1A);  // dark red
        private static readonly Color FlashRed = Color.FromArgb(0xC0, 0x39, 0x2B);
        private static readonly Color FlashOrange = Color.FromArgb(0xE6, 0x7E, 0x22);

        private static readonly Color ModeSelected = TrueColor;
        private static readonly Color ModeUnselected = Color.Black;

        // ====================== Power state ======================

        private enum OverrideMode { Off, Auto, On }

        private OverrideMode overrideMode = OverrideMode.Auto;

        /// <summary>
        /// Set when a poweroff request has been honoured. Only the ON button clears it.
        /// This is what stops the "no poll performed, so assume it wants power" default
        /// from re-energising the board the instant the socket drops after a shutdown.
        /// </summary>
        private bool shutdownLatched;

        /// <summary>
        /// True from the moment a reboot is honoured until a poll reports a normal
        /// request again — which, because the board is rebooting and the socket is down,
        /// spans the contactor-open window, the delay, the boot and the reconnect.
        /// </summary>
        private bool restartInProgress;
        private DateTime restartOpenedAtUtc;

        /// <summary>
        /// The board must ask for a normal state before it may reboot itself again.
        /// Reboot loops are intentional for some tests, but the server never clears
        /// power_request on its own, so without this gate one request would cycle the
        /// contactor forever if the board came back still asking.
        /// </summary>
        private bool rebootArmed = true;

        /// <summary>Contactor state the manager wants. Closed (power flowing) by default.</summary>
        private bool contactorDesired = true;

        /// <summary>Last state read back from the relay, or null when no board is present.</summary>
        private bool? contactorActual;

        private string? relaySerial;
        private DateTime lastRelayScanUtc = DateTime.MinValue;
        private DateTime lastContactorVerifyUtc = DateTime.MinValue;

        // ====================== Connection state ======================

        private string? lastHost;
        private ushort lastPort;
        private bool intentionalDisconnect = true;   // nothing to reconnect to until first connect
        private int reconnectAttempts;
        private bool reconnectExhausted;
        private bool connectInProgress;

        // ====================== Cancellation ======================

        private CancellationTokenSource? loopCts;

        // ====================== Construction ======================

        public Launcher()
        {
            InitializeComponent();

            ConfigureControls();

            TcpManager.StatusChanged += OnConnectionStatusChanged;
            ApplyConnectionStatus(TcpManager.Status);

            abortButton.Click += AbortButton_Click;
            powerOn.Click += (s, e) => SetOverrideMode(OverrideMode.On);
            powerAUTO.Click += (s, e) => SetOverrideMode(OverrideMode.Auto);
            powerOFF.Click += (s, e) => SetOverrideMode(OverrideMode.Off);

            FormClosed += Launcher_FormClosed;
            Shown += Launcher_Shown;
        }

        private void Launcher_Shown(object? sender, EventArgs e)
        {
            loopCts = new CancellationTokenSource();
            CancellationToken ct = loopCts.Token;

            _ = PowerLoopAsync(ct);
            _ = InfoLoopAsync(ct);
            _ = EngineLoopAsync(ct);
            _ = ReconnectLoopAsync(ct);
        }

        private void Launcher_FormClosed(object? sender, FormClosedEventArgs e)
        {
            TcpManager.StatusChanged -= OnConnectionStatusChanged;
            loopCts?.Cancel();
        }

        private void ConfigureControls()
        {
            // Every indicator is a Button with Enabled = false. A disabled Button under
            // the default FlatStyle is painted by the themed renderer, which ignores
            // BackColor entirely — Flat is what makes any of these colours appear.
            foreach (var pill in StatusPills().Concat(new[] { idleStatus, powerStatus,
                                                             contactorStatus, powerOn,
                                                             powerAUTO, powerOFF }))
            {
                pill.FlatStyle = FlatStyle.Flat;
                pill.FlatAppearance.BorderSize = 0;
                pill.ForeColor = Color.White;
            }

            foreach (var pill in StatusPills())
            {
                pill.Enabled = false;
                pill.TabStop = false;
            }
            idleStatus.Enabled = false;
            idleStatus.TabStop = false;
            powerStatus.Enabled = false;
            powerStatus.TabStop = false;
            contactorStatus.Enabled = false;
            contactorStatus.TabStop = false;

            // Mode buttons stay clickable; black unselected, green selected.
            foreach (var b in new[] { powerOn, powerAUTO, powerOFF })
                b.FlatAppearance.MouseOverBackColor = Color.FromArgb(0x40, 0x40, 0x40);

            socketStatus.ForeColor = Color.Black;

            taskProgress.Minimum = 0;
            taskProgress.Maximum = 100;
            taskProgress.Style = ProgressBarStyle.Continuous;

            // Designer defaults are Minimum 0 / Maximum 100, which would cap the restart
            // delay at 100 ms and allow a relay index of 0 — index 0 is the board's
            // serial-rewrite target, not a channel.
            relayID.Minimum = 1;
            relayID.Maximum = 8;
            relayID.Value = 1;

            restartDelay.Minimum = 100;
            restartDelay.Maximum = 120000;
            restartDelay.Value = 5000;

            abortButton.Enabled = false;

            ApplyEngineOffline();
            ApplyInfoOffline();
            ApplyModeButtons();
            PaintPowerStatus();
            PaintContactor();
        }

        private Button[] StatusPills() => new[]
        {
            psStatus, fpgaStatus, plStatus, uiStatus, calbStatus,
            clampStatus, activeStatus, possibleStatus,
            sdram0, sdram1, sdram2, sdram3,
        };

        // ====================== Apps ======================

        private void launchRunEditor_Click(object sender, EventArgs e) => new RunEditor().Show(this);
        private void configEditor_Click(object sender, EventArgs e) => new ConfigEditor().Show(this);
        private void launchRecordManager_Click(object sender, EventArgs e) => new RecordManager().Show(this);
        private void button6_Click(object sender, EventArgs e) { }

        // ====================== Connection ======================

        private async void CM_connect_Click(object sender, EventArgs e)
        {
            if (TcpManager.Status == TcpManager.ConnectionStatus.Connected)
            {
                // Pressing Disconnect is the one case that suppresses auto-reconnect.
                intentionalDisconnect = true;
                reconnectAttempts = 0;
                reconnectExhausted = false;
                TcpManager.Disconnect();
                UpdateSocketStatus();
                return;
            }

            if (!ushort.TryParse(CM_port.Text, out ushort port))
            {
                MessageBox.Show("Port must be 0-65535.");
                return;
            }

            await ConnectAsync(CM_ip.Text.Trim(), port, manual: true);
        }

        private async Task<bool> ConnectAsync(string host, ushort port, bool manual)
        {
            if (connectInProgress) return false;
            connectInProgress = true;

            if (manual)
            {
                CM_connect.Enabled = false;
                reconnectAttempts = 0;
                reconnectExhausted = false;
            }

            UpdateSocketStatus();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await TcpManager.ConnectAsync(host, port, cts.Token);

                // Only a successful connection establishes what to reconnect TO. The
                // retry uses these rather than the live text boxes, so editing the
                // address mid-retry doesn't silently redirect it.
                lastHost = host;
                lastPort = port;
                intentionalDisconnect = false;
                reconnectAttempts = 0;
                reconnectExhausted = false;
                return true;
            }
            catch (OperationCanceledException)
            {
                if (manual) MessageBox.Show("Connection timed out.");
                return false;
            }
            catch (Exception ex)
            {
                if (manual) MessageBox.Show($"Connect failed: {ex.Message}");
                return false;
            }
            finally
            {
                connectInProgress = false;
                if (manual && !IsDisposed) CM_connect.Enabled = true;
                if (!IsDisposed) UpdateSocketStatus();
            }
        }

        private void OnConnectionStatusChanged(TcpManager.ConnectionStatus status)
        {
            if (IsDisposed || !IsHandleCreated) return;

            // A framing fault raises this from whichever pool thread was mid-read.
            if (InvokeRequired)
                BeginInvoke((Action)(() => ApplyConnectionStatus(status)));
            else
                ApplyConnectionStatus(status);
        }

        private void ApplyConnectionStatus(TcpManager.ConnectionStatus status)
        {
            bool connected = status == TcpManager.ConnectionStatus.Connected;
            CM_connect.Text = connected ? "Disconnect" : "Connect";

            // Left editable while retrying so a wrong address can be corrected, but the
            // retry keeps using the last address that actually worked until the operator
            // presses Connect.
            CM_ip.Enabled = !connected;
            CM_port.Enabled = !connected;

            if (!connected) ApplyInfoOffline();
            UpdateSocketStatus();
        }

        /// <summary>
        /// Polls rather than reacting to StatusChanged. ConnectAsync calls Disconnect()
        /// internally, so the event fires during a connect attempt — reacting to it
        /// would arm a retry against the attempt already in flight.
        /// </summary>
        private async Task ReconnectLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(ReconnectInterval, ct); }
                catch (OperationCanceledException) { return; }

                if (IsDisposed) return;

                bool shouldRetry =
                    TcpManager.Status != TcpManager.ConnectionStatus.Connected
                    && !intentionalDisconnect
                    && !reconnectExhausted
                    && !connectInProgress
                    && lastHost != null;

                if (!shouldRetry) continue;

                reconnectAttempts++;
                if (reconnectAttempts > ReconnectMaxAttempts)
                {
                    reconnectExhausted = true;
                    UpdateSocketStatus();
                    continue;
                }

                UpdateSocketStatus();
                await ConnectAsync(lastHost!, lastPort, manual: false);
            }
        }

        private void UpdateSocketStatus()
        {
            if (IsDisposed) return;

            if (TcpManager.Status == TcpManager.ConnectionStatus.Connected)
            {
                socketStatus.Text = $"Connected to {lastHost}:{lastPort}";
            }
            else if (connectInProgress)
            {
                socketStatus.Text = reconnectAttempts > 0
                    ? $"Reconnecting ({reconnectAttempts}/{ReconnectMaxAttempts})..."
                    : "Connecting...";
            }
            else if (reconnectExhausted)
            {
                socketStatus.Text = "Reconnect failed after 15 minutes - press Connect";
            }
            else if (intentionalDisconnect)
            {
                socketStatus.Text = "Disconnected";
            }
            else
            {
                socketStatus.Text =
                    $"Link lost - retrying ({reconnectAttempts}/{ReconnectMaxAttempts})";
            }
        }

        // ====================== Power management ======================

        private void SetOverrideMode(OverrideMode mode)
        {
            overrideMode = mode;

            // ON is the only thing that clears a honoured shutdown, by design: AUTO
            // alone would let the assume-power default re-energise a board that was
            // deliberately shut down.
            if (mode == OverrideMode.On)
            {
                shutdownLatched = false;
                restartInProgress = false;
            }

            ApplyModeButtons();
            PaintPowerStatus();
        }

        private void ApplyModeButtons()
        {
            SetPill(powerOn, "ON", overrideMode == OverrideMode.On ? ModeSelected : ModeUnselected);
            SetPill(powerAUTO, "AUTO", overrideMode == OverrideMode.Auto ? ModeSelected : ModeUnselected);
            SetPill(powerOFF, "OFF", overrideMode == OverrideMode.Off ? ModeSelected : ModeUnselected);
        }

        /// <summary>
        /// Runs at 150 ms, which is also what keeps the server's power-services
        /// heartbeat fresh — it stamps a timestamp on every GETPOWERMODE and reports
        /// whether that stamp is recent through InfoRsp.PowerServicesProvided.
        /// </summary>
        private async Task PowerLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (IsDisposed) return;

                if (TcpManager.Status == TcpManager.ConnectionStatus.Connected)
                {
                    try
                    {
                        PowerMode pm = await TcpManager.GetPowerModeAsync(ct);
                        if (ct.IsCancellationRequested || IsDisposed) return;
                        HandlePowerRequest(pm);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex) when (IsConnectionFault(ex))
                    {
                        // The link dropping is not a power event. Whatever state the
                        // contactor is in stays, and the timers below keep running —
                        // which is what lets a restart finish after the board has gone.
                    }
                }

                await ServiceContactorAsync(ct);

                if (!IsDisposed)
                {
                    PaintPowerStatus();
                    PaintContactor();
                }

                try { await Task.Delay(PowerPollInterval, ct); }
                catch (OperationCanceledException) { return; }
            }
        }

        private void HandlePowerRequest(PowerMode pm)
        {
            bool normal = !pm.RequestPoweroff && !pm.RequestReboot;

            if (normal)
            {
                // A restart ends when the board reports a normal state again, which
                // after a reboot means it has booted and reconnected on its own.
                restartInProgress = false;

                // And only then may it ask for another cycle.
                rebootArmed = true;
                return;
            }

            // ON and OFF forcibly override, so requests are only acted on in AUTO. The
            // server parks with the flag set, so a request arriving while forced is not
            // lost — it is honoured on the first AUTO poll instead.
            if (overrideMode != OverrideMode.Auto) return;

            if (pm.RequestPoweroff)
            {
                shutdownLatched = true;
                restartInProgress = false;
                return;
            }

            if (pm.RequestReboot && rebootArmed && !restartInProgress)
            {
                restartInProgress = true;
                restartOpenedAtUtc = DateTime.UtcNow;
                rebootArmed = false;
            }
        }

        /// <summary>Contactor state the current mode and latches call for.</summary>
        private bool ComputeDesiredContactor()
        {
            switch (overrideMode)
            {
                case OverrideMode.On: return true;
                case OverrideMode.Off: return false;

                default:
                    if (shutdownLatched) return false;

                    if (restartInProgress)
                    {
                        // Open for the delay, then close and hold closed while the board
                        // boots. The timer runs off the local clock, so it completes even
                        // though the socket went down the instant power was cut.
                        double elapsed = (DateTime.UtcNow - restartOpenedAtUtc).TotalMilliseconds;
                        return elapsed >= (double)restartDelay.Value;
                    }

                    // No poll yet, or nothing requested: assume the board wants power.
                    return true;
            }
        }

        private async Task ServiceContactorAsync(CancellationToken ct)
        {
            bool desired = ComputeDesiredContactor();
            bool changed = desired != contactorDesired;
            contactorDesired = desired;

            DateTime now = DateTime.UtcNow;
            bool verifyDue = now - lastContactorVerifyUtc >= ContactorVerifyInterval;

            if (!changed && !verifyDue) return;

            int channel = (int)relayID.Value;
            bool invert = invertRelay.Checked;

            // USB control transfers block for a few milliseconds; keep them off the UI
            // thread so the 150 ms cadence doesn't jitter when the board is slow.
            var result = await Task.Run(() => ApplyContactor(channel, desired, invert, changed), ct)
                                   .ConfigureAwait(true);

            if (ct.IsCancellationRequested || IsDisposed) return;

            contactorActual = result;
            lastContactorVerifyUtc = now;
        }

        /// <summary>
        /// Drive and read back one channel. Returns the contactor state actually
        /// observed, or null when no board is attached.
        ///
        /// Invert maps the rail's wiring to the relay coil. Unchecked means the rail is
        /// on the NC contacts, which conduct when the coil is NOT energised, so power on
        /// = relay off. Checked means the rail is on NO, which conducts when energised,
        /// so power on = relay on. Both directions reduce to one XNOR.
        /// </summary>
        private bool? ApplyContactor(int channel, bool wantPower, bool invert, bool force)
        {
            if (relaySerial == null)
            {
                // Rescan occasionally so a board plugged in after launch is picked up.
                if (DateTime.UtcNow - lastRelayScanUtc < TimeSpan.FromSeconds(2)) return null;
                lastRelayScanUtc = DateTime.UtcNow;

                relaySerial = UsbRelay.Enumerate().FirstOrDefault()?.Serial;
                if (relaySerial == null) return null;
            }

            bool energise = wantPower == invert;

            bool? energised = UsbRelay.IsOn(relaySerial, channel);
            if (energised == null)
            {
                // Board vanished; drop the cached serial so the next pass rescans.
                relaySerial = null;
                return null;
            }

            if (force || energised.Value != energise)
            {
                if (!UsbRelay.Set(relaySerial, channel, energise)) return null;
                energised = energise;
            }

            // Same XNOR in reverse — the mapping is its own inverse.
            return energised.Value == invert;
        }

        private void PaintPowerStatus()
        {
            if (restartInProgress)
            {
                bool flashOn = (Environment.TickCount64 / FlashPeriodMs) % 2 == 0;
                SetPill(powerStatus, "SYSTEM RESTARTING", flashOn ? FlashRed : FlashOrange);
            }
            else if (shutdownLatched)
            {
                SetPill(powerStatus, "SYSTEM SHUTDOWN", ShutdownColor);
            }
            else
            {
                SetPill(powerStatus, "SYSTEM OPERATIONAL", OperationalColor);
            }
        }

        private void PaintContactor()
        {
            if (contactorActual == null)
                SetPill(contactorStatus, "NO RELAY", OfflineColor);
            else
                SetPill(contactorStatus,
                        contactorActual.Value ? "CLOSED" : "OPEN",
                        contactorActual.Value ? TrueColor : FalseColor);
        }

        // ====================== Info polling ======================

        private async Task InfoLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (IsDisposed) return;

                if (TcpManager.Status == TcpManager.ConnectionStatus.Connected)
                {
                    try
                    {
                        InfoRsp info = await TcpManager.GetInfoAsync(ct);
                        if (ct.IsCancellationRequested || IsDisposed) return;
                        ApplyInfo(info);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex) when (IsConnectionFault(ex))
                    {
                        if (!IsDisposed) ApplyInfoOffline();
                    }
                }

                try { await Task.Delay(InfoPollInterval, ct); }
                catch (OperationCanceledException) { return; }
            }
        }

        private void ApplyInfo(InfoRsp info)
        {
            SetBoolPill(psStatus, "POWER SERVICES", info.PowerServicesProvided);
            SetBoolPill(fpgaStatus, "FPGA READY", info.FpgaLoaded);
            SetBoolPill(plStatus, "PL", info.PlClock);
            SetBoolPill(uiStatus, "UI", info.UiClock);
            SetBoolPill(calbStatus, "CALIBRATED", info.ControllerCalibrated);

            SetBoolPill(activeStatus, "ACTIVE", info.BeamActive);
            SetBoolPill(possibleStatus, "POSSIBLE", info.BeamPossible);

            // InfoRsp has no clamp field yet; held false until the server reports it.
            SetBoolPill(clampStatus, "CLAMP", false);

            SetBoolPill(sdram0, "SDRAM 0", info.Sdram0);
            SetBoolPill(sdram1, "SDRAM 1", info.Sdram1);
            SetBoolPill(sdram2, "SDRAM 2", info.Sdram2);
            SetBoolPill(sdram3, "SDRAM 3", info.Sdram3);
        }

        private void ApplyInfoOffline()
        {
            if (IsDisposed) return;
            foreach (var pill in StatusPills())
                if (pill.BackColor != OfflineColor) pill.BackColor = OfflineColor;
        }

        private static void SetBoolPill(Button pill, string text, bool value)
        {
            Color back = value ? TrueColor : FalseColor;
            if (pill.Text != text) pill.Text = text;
            if (pill.BackColor != back) pill.BackColor = back;
        }

        private static void SetPill(Button pill, string text, Color back)
        {
            if (pill.Text != text) pill.Text = text;
            if (pill.BackColor != back) pill.BackColor = back;
        }

        // ====================== Engine status ======================

        private async Task EngineLoopAsync(CancellationToken ct)
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
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex) when (IsConnectionFault(ex))
                    {
                        if (!IsDisposed) ApplyEngineOffline();
                    }
                }
                else
                {
                    ApplyEngineOffline();
                }

                try { await Task.Delay(EnginePollInterval, ct); }
                catch (OperationCanceledException) { return; }
            }
        }

        private void ApplyEngineStatus(EngineStatus status)
        {
            // The engine pops a task off the queue before starting it, so the in-flight
            // record is NOT counted here — Queue is strictly what is still pending.
            queueSize.Text = $"{status.Queue.Length} Task(s)";

            taskName.Text = status.ActiveRecord == null
                ? "NONE"
                : status.ActiveRecord.Rtype.ToString().ToUpperInvariant();

            SetProgress(status.ActiveRecord == null ? 0.0 : status.RecordProgress);

            if (status.IsStalled)
            {
                // Idle with work queued: the engine refuses to start while /dvr is in
                // safe mode or the calibration line reads low, and says nothing about it.
                SetPill(idleStatus, "STALLED", BusyColor);
            }
            else
            {
                SetPill(idleStatus, status.IsIdle ? "IDLE" : "BUSY",
                                    status.IsIdle ? IdleColor : BusyColor);
            }

            abortButton.Enabled = !status.IsIdle;
        }

        private void ApplyEngineOffline()
        {
            if (IsDisposed) return;
            queueSize.Text = "0 Task(s)";
            taskName.Text = "NONE";
            SetProgress(0.0);
            SetPill(idleStatus, "OFFLINE", OfflineColor);
            abortButton.Enabled = false;
        }

        private void SetProgress(double percent)
        {
            if (double.IsNaN(percent) || percent < 0) percent = 0;
            if (percent > 100) percent = 100;

            int value = (int)Math.Round(percent);
            if (taskProgress.Value == value) return;

            // Aero eases a rising Value over ~200 ms. Overshooting by one and stepping
            // back lands on the target immediately.
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

        private async void AbortButton_Click(object? sender, EventArgs e)
        {
            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected) return;

            if (MessageBox.Show(
                    "Abort the task currently running?\n\nThe partial record is archived " +
                    "with its aborted flag set. The engine only notices at its next " +
                    "progress tick, so this is not instantaneous.",
                    "Abort Task", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                != DialogResult.Yes)
                return;

            try
            {
                await TcpManager.AbortAsync(loopCts?.Token ?? CancellationToken.None);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!IsDisposed)
                    MessageBox.Show($"Abort failed: {ex.Message}", "Abort Task",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ====================== Helpers ======================

        private static bool IsConnectionFault(Exception ex) =>
            ex is TimeoutException
            || ex is IOException
            || ex is SocketException
            || ex is InvalidDataException
            || ex is InvalidOperationException;
    }
}