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
    /// <summary>
    /// Status dashboard and hardware supervisor for the ZCU test bench.
    ///
    /// It owns TWO relay channels on the same USB relay board:
    ///
    ///   * POWER  — the contactor feeding the ZCU104. Driven from
    ///     GetPowerModeRsp.request_poweroff / request_reboot, which the server only
    ///     raises once the task engine has unmounted /dvr and marked itself safe.
    ///
    ///   * BEAM   — the facility beam-enable / clamp interlock. Driven from
    ///     GetPowerModeRsp.request_beam, the flag the engine's Dynamic and Expose
    ///     arms raise through set_beam_request() when they want beam on target.
    ///
    /// Both arrive in the same GETPOWERMODE reply, so one 150 ms poll services both
    /// and the same heartbeat keeps InfoRsp.PowerServicesProvided fresh.
    ///
    /// The two differ in exactly one respect, deliberately: their fail-safe
    /// direction. With no link the contactor assumes the board WANTS power — a ZCU
    /// that has lost comms still needs to finish booting and reconnect. With no link
    /// the beam relay drops. Beam that nothing is watching is beam that gets turned
    /// off. See ComputeDesiredPower / ComputeDesiredBeam.
    /// </summary>
    public partial class Launcher : Form
    {
        // ====================== Cadences ======================

        private static readonly TimeSpan PowerPollInterval = TimeSpan.FromMilliseconds(150);
        private static readonly TimeSpan InfoPollInterval = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Used to be five seconds because EngineStatusRsp embedded the whole in-flight
        /// Record, log arrays included, and the server held STATUS.read() across the
        /// clone, the serialize and the socket write — which starved the 150 ms power
        /// poll of the shared send lock and blinked POWER SERVICES. The server now
        /// sends only Option&lt;RecordType&gt;, a progress number, the idle flag and the
        /// pending queue, so a poll is about twenty bytes and this can be brisk again.
        /// </summary>
        private static readonly TimeSpan EnginePollInterval = TimeSpan.FromMilliseconds(750);

        private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(5);
        private const int ReconnectMaxAttempts = 180;        // 5 s x 180 = 15 minutes

        /// <summary>Relay state is re-read this often so the pills show reality, not intent.</summary>
        private static readonly TimeSpan RelayVerifyInterval = TimeSpan.FromSeconds(1);

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

        /// <summary>Accepted by the server but not yet acted on by the task engine.</summary>
        private static readonly Color RequestedColor = Color.FromArgb(0xD6, 0x89, 0x10);

        /// <summary>A request the operator's override is deliberately ignoring.</summary>
        private static readonly Color OverriddenColor = Color.FromArgb(0xD6, 0x89, 0x10);

        /// <summary>Misconfiguration, not a hardware state.</summary>
        private static readonly Color FaultColor = Color.FromArgb(0x8E, 0x44, 0xAD);

        private static readonly Color ModeSelected = TrueColor;
        private static readonly Color ModeUnselected = Color.Black;

        // ====================== Modes ======================

        private enum OverrideMode { Off, Auto, On }

        // ====================== Power state ======================

        private OverrideMode powerMode = OverrideMode.Auto;

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
        /// A power state asked for with the PWR buttons that the server has accepted
        /// but not yet acted on. The engine only reaches its power check BETWEEN tasks,
        /// so a request issued mid-run sits for the rest of that run while GETPOWERMODE
        /// keeps reporting nothing — without this the UI would read SYSTEM OPERATIONAL
        /// the whole time and the button press would look like it did nothing.
        /// </summary>
        private PowerRequest? pendingRequest;

        /// <summary>
        /// The board must ask for a normal state before it may reboot itself again.
        /// Reboot loops are intentional for some tests, but the server never clears
        /// power_request on its own, so without this gate one request would cycle the
        /// contactor forever if the board came back still asking.
        /// </summary>
        private bool rebootArmed = true;

        // ====================== Beam state ======================

        private OverrideMode beamMode = OverrideMode.Auto;

        /// <summary>
        /// request_beam from the last GETPOWERMODE reply. Reset to false the moment the
        /// link is known to be down — see ComputeDesiredBeam for why this one does not
        /// get the benefit of the doubt that the contactor does.
        /// </summary>
        private bool beamRequested;

        /// <summary>True while the two relay channels are set to the same index.</summary>
        private bool relayChannelConflict;

        // ====================== Relay state ======================

        /// <summary>Relay state each channel is being driven to.</summary>
        private bool powerDesired = true;      // contactor closed (power flowing) by default
        private bool beamDesired;              // beam blocked by default

        /// <summary>Last state read back, or null when no board / no channel is present.</summary>
        private bool? powerActual;
        private bool? beamActual;

        private string? relaySerial;
        private DateTime lastRelayScanUtc = DateTime.MinValue;
        private DateTime lastRelayVerifyUtc = DateTime.MinValue;

        /// <summary>
        /// Beam channel config as of the last service pass. Cached rather than re-read
        /// on shutdown: FormClosed runs on the way into Dispose, and reading
        /// NumericUpDown.Value from there can land on a disposed control.
        /// </summary>
        private int? lastBeamChannel;
        private bool lastBeamInvert;

        /// <summary>
        /// Serializes USB access. Both channels live on one HID device and the library
        /// is not reentrant, so the two are driven inside a single worker pass rather
        /// than from two tasks.
        /// </summary>
        private readonly SemaphoreSlim relayLock = new SemaphoreSlim(1, 1);

        // ====================== Engine state ======================

        /// <summary>Mirrors EngineStatus.IsIdle, inverted. Read by the power-request dialog.</summary>
        private bool engineBusy;

        // ====================== Connection state ======================

        private string? lastHost;
        private ushort lastPort;
        private bool intentionalDisconnect = true;   // nothing to reconnect to until first connect
        private int reconnectAttempts;
        private bool reconnectExhausted;
        private bool connectInProgress;

        // ====================== Cancellation ======================

        private CancellationTokenSource? loopCts;
        private bool loopsStarted;

        private Button[]? statusPills;

        // ====================== Construction ======================

        public Launcher()
        {
            InitializeComponent();

            ConfigureControls();

            TcpManager.StatusChanged += OnConnectionStatusChanged;
            ApplyConnectionStatus(TcpManager.Status);

            abortButton.Click += AbortButton_Click;
            requestOff.Click += async (s, e) => await RequestPowerStateAsync(PowerRequest.Shutdown);
            requestReboot.Click += async (s, e) => await RequestPowerStateAsync(PowerRequest.Reboot);

            powerOn.Click += (s, e) => SetPowerMode(OverrideMode.On);
            powerAUTO.Click += (s, e) => SetPowerMode(OverrideMode.Auto);
            powerOFF.Click += (s, e) => SetPowerMode(OverrideMode.Off);

            beamOn.Click += (s, e) => SetBeamMode(OverrideMode.On);
            beamAUTO.Click += (s, e) => SetBeamMode(OverrideMode.Auto);
            beamOFF.Click += (s, e) => SetBeamMode(OverrideMode.Off);

            relayID_power.ValueChanged += (s, e) => CheckRelayChannels();
            relayID_beam.ValueChanged += (s, e) => CheckRelayChannels();

            FormClosing += Launcher_FormClosing;
            FormClosed += Launcher_FormClosed;
            Shown += Launcher_Shown;
        }

        private void Launcher_Shown(object? sender, EventArgs e)
        {
            // Shown fires again on every Hide()/Show() cycle. Without this guard a
            // second pass would spawn a duplicate set of pollers, each holding the
            // send lock in turn and halving the effective cadence of the others.
            if (loopsStarted) return;
            loopsStarted = true;

            loopCts = new CancellationTokenSource();
            CancellationToken ct = loopCts.Token;

            _ = PowerLoopAsync(ct);
            _ = InfoLoopAsync(ct);
            _ = EngineLoopAsync(ct);
            _ = ReconnectLoopAsync(ct);
        }

        private void Launcher_FormClosing(object? sender, FormClosingEventArgs e)
        {
            // Closing drops the beam relay (see Launcher_FormClosed). Say so while the
            // beam is actually enabled rather than letting it happen silently.
            if (beamActual == true && e.CloseReason == CloseReason.UserClosing)
            {
                if (MessageBox.Show(
                        "Beam is currently enabled.\n\nClosing the launcher releases the " +
                        "beam relay, which blocks beam. The power contactor is left as-is.",
                        "Close Launcher", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning)
                    != DialogResult.OK)
                {
                    e.Cancel = true;
                }
            }
        }

        private void Launcher_FormClosed(object? sender, FormClosedEventArgs e)
        {
            TcpManager.StatusChanged -= OnConnectionStatusChanged;
            loopCts?.Cancel();

            // Best effort, on the UI thread: a few milliseconds of blocking USB on the
            // way out is worth not leaving a beam interlock latched open with nothing
            // supervising it. The contactor is deliberately NOT touched — cutting the
            // board's power because somebody closed a window would be worse than
            // leaving it running.
            try
            {
                if (relaySerial != null && lastBeamChannel is int ch)
                    UsbRelay.Set(relaySerial, ch, !lastBeamInvert);
            }
            catch { /* the board is gone, or HID is busy; nothing useful to do here */ }
        }

        private void ConfigureControls()
        {
            statusPills = new[]
            {
                psStatus, fpgaStatus, plStatus, uiStatus, calbStatus,
                clampStatus, activeStatus, possibleStatus,
                sdram0, sdram1, sdram2, sdram3,
            };

            // Every indicator is a Button with Enabled = false. A disabled Button under
            // the default FlatStyle is painted by the themed renderer, which ignores
            // BackColor entirely — Flat is what makes any of these colours appear.
            var flat = statusPills.Concat(new[]
            {
                idleStatus, powerStatus, contactorStatus,
                beamRequestStatus, beamRelayStatus,
                powerOn, powerAUTO, powerOFF,
                beamOn, beamAUTO, beamOFF,
            });

            foreach (var pill in flat)
            {
                pill.FlatStyle = FlatStyle.Flat;
                pill.FlatAppearance.BorderSize = 0;
                pill.ForeColor = Color.White;
            }

            foreach (var pill in statusPills)
            {
                pill.Enabled = false;
                pill.TabStop = false;
            }

            foreach (var pill in new[] { idleStatus, powerStatus, contactorStatus,
                                         beamRequestStatus, beamRelayStatus })
            {
                pill.Enabled = false;
                pill.TabStop = false;
            }

            // Mode buttons stay clickable; black unselected, green selected.
            foreach (var b in new[] { powerOn, powerAUTO, powerOFF, beamOn, beamAUTO, beamOFF })
                b.FlatAppearance.MouseOverBackColor = Color.FromArgb(0x40, 0x40, 0x40);

            socketStatus.ForeColor = Color.Black;

            taskProgress.Minimum = 0;
            taskProgress.Maximum = 100;
            taskProgress.Style = ProgressBarStyle.Continuous;

            // Defensive: the designer now carries these ranges, but a careless re-save
            // from the designer surface drops back to Minimum 0 / Maximum 100, which
            // would cap the restart delay at 100 ms and allow a relay index of 0 —
            // index 0 is the board's serial-rewrite target, not a channel. Maximum is
            // raised before Minimum so neither assignment clamps the other.
            ConfigureSpinner(relayID_power, min: 1, max: 8, value: relayID_power.Value);
            ConfigureSpinner(relayID_beam, min: 1, max: 8, value: relayID_beam.Value);
            ConfigureSpinner(restartDelay, min: 100, max: 120000, value: restartDelay.Value);

            abortButton.Enabled = false;
            requestOff.Enabled = false;
            requestReboot.Enabled = false;

            CheckRelayChannels();

            ApplyEngineOffline();
            ApplyInfoOffline();
            ApplyModeButtons();
            PaintPowerStatus();
            PaintBeamStatus();
            PaintRelays();
        }

        private static void ConfigureSpinner(NumericUpDown spin, decimal min, decimal max, decimal value)
        {
            spin.Maximum = max;
            spin.Minimum = min;
            spin.Value = Math.Min(Math.Max(value, min), max);
        }

        private Button[] StatusPills() => statusPills ?? Array.Empty<Button>();

        // ====================== Apps ======================

        private void launchRunEditor_Click(object sender, EventArgs e) => new RunEditor().Show(this);
        private void configEditor_Click(object sender, EventArgs e) => new ConfigEditor().Show(this);
        private void launchRecordManager_Click(object sender, EventArgs e) => new RecordManager().Show(this);

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

            requestOff.Enabled = connected;
            requestReboot.Enabled = connected;

            // Left editable while retrying so a wrong address can be corrected, but the
            // retry keeps using the last address that actually worked until the operator
            // presses Connect.
            CM_ip.Enabled = !connected;
            CM_port.Enabled = !connected;

            if (!connected)
            {
                ApplyInfoOffline();

                // The beam request does not survive the link. Dropping it here as well
                // as in ComputeDesiredBeam means the pill stops claiming REQUESTED the
                // instant the socket dies, rather than at the next poll.
                beamRequested = false;
                PaintBeamStatus();
            }

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

        // ====================== Mode buttons ======================

        private void SetPowerMode(OverrideMode mode)
        {
            powerMode = mode;

            // ON is the only thing that clears a honoured shutdown, by design: AUTO
            // alone would let the assume-power default re-energise a board that was
            // deliberately shut down.
            if (mode == OverrideMode.On)
            {
                shutdownLatched = false;
                restartInProgress = false;
                pendingRequest = null;
            }

            ApplyModeButtons();
            PaintPowerStatus();
        }

        private void SetBeamMode(OverrideMode mode)
        {
            // Forcing beam on from the UI bypasses every interlock the test software
            // provides. It is here because bench work needs it, but it should not be
            // one misclick away.
            //if (mode == OverrideMode.On && beamMode != OverrideMode.On)
            //{
            //    if (MessageBox.Show(
            //            "Force the beam relay closed?\n\n" +
            //            "This asserts beam-enable regardless of what the ZCU is asking " +
            //            "for, and holds it asserted even if the link drops or a task " +
            //            "aborts. The ZCU's own clamp line is NOT changed, so the CLAMP " +
            //            "indicator will disagree with this relay.",
            //            "Force Beam On", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
            //        != DialogResult.Yes)
            //        return;
            //}

            beamMode = mode;
            ApplyModeButtons();
            PaintBeamStatus();
        }

        private void ApplyModeButtons()
        {
            SetPill(powerOn, "ON", powerMode == OverrideMode.On ? ModeSelected : ModeUnselected);
            SetPill(powerAUTO, "AUTO", powerMode == OverrideMode.Auto ? ModeSelected : ModeUnselected);
            SetPill(powerOFF, "OFF", powerMode == OverrideMode.Off ? ModeSelected : ModeUnselected);

            SetPill(beamOn, "ON", beamMode == OverrideMode.On ? ModeSelected : ModeUnselected);
            SetPill(beamAUTO, "AUTO", beamMode == OverrideMode.Auto ? ModeSelected : ModeUnselected);
            SetPill(beamOFF, "OFF", beamMode == OverrideMode.Off ? ModeSelected : ModeUnselected);
        }

        /// <summary>
        /// Both channels sit on one relay board, so the same index for both would have
        /// the power and beam rules fighting over one coil at 150 ms. Detect it and
        /// refuse to drive either rather than let them chatter.
        /// </summary>
        private void CheckRelayChannels()
        {
            bool conflict = (int)relayID_power.Value == (int)relayID_beam.Value;
            if (conflict == relayChannelConflict) return;

            relayChannelConflict = conflict;
            PaintRelays();
        }

        // ====================== Power requests ======================

        /// <summary>
        /// Ask the ZCU to enter a power state (CMD_REQUESTPOWERSTATE). The server only
        /// records the request: the task engine acts on it at the top of its loop, where
        /// it unmounts /dvr, hibernates the queue and marks itself safe — and only then
        /// does GETPOWERMODE start reporting it to the power manager, which opens the
        /// contactor. So the chain is request -> engine honours -> poll reports ->
        /// contactor moves, and this method only covers the first link.
        /// </summary>
        private async Task RequestPowerStateAsync(PowerRequest state)
        {
            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                MessageBox.Show("Not connected.");
                return;
            }

            if (powerMode != OverrideMode.Auto)
            {
                MessageBox.Show(
                    $"Power override is set to {powerMode.ToString().ToUpperInvariant()}, " +
                    "so the contactor will not follow this request. Switch to AUTO first.",
                    "Power Request", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string verb = state == PowerRequest.Shutdown ? "shut down" : "reboot";

            // The engine only reaches the power check between tasks, so a request made
            // mid-run waits for that run to finish. Say so rather than let the operator
            // conclude the button is broken.
            string wait = engineBusy
                ? "\n\nA task is currently running. The ZCU will not act on this until " +
                  "that task finishes — abort it first if you need the change now."
                : "";

            string consequence = state == PowerRequest.Shutdown
                ? "\n\nThe contactor will open and STAY open. Power is restored only by " +
                  "pressing ON."
                : $"\n\nThe contactor will open for {restartDelay.Value} ms and then close.";

            if (MessageBox.Show(
                    $"Ask the ZCU to {verb}?{consequence}{wait}",
                    "Power Request", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                != DialogResult.Yes)
                return;

            requestOff.Enabled = false;
            requestReboot.Enabled = false;
            try
            {
                bool ok = await TcpManager.SetPowerStateAsync(
                    state, loopCts?.Token ?? CancellationToken.None);

                if (IsDisposed) return;

                if (ok)
                {
                    pendingRequest = state;
                    PaintPowerStatus();
                }
                else
                {
                    MessageBox.Show(
                        "The ZCU rejected the request. It only accepts states 0-2.",
                        "Power Request", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!IsDisposed)
                    MessageBox.Show($"Power request failed: {ex.Message}", "Power Request",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (!IsDisposed)
                {
                    bool connected = TcpManager.Status == TcpManager.ConnectionStatus.Connected;
                    requestOff.Enabled = connected;
                    requestReboot.Enabled = connected;
                }
            }
        }

        // ====================== Power / beam loop ======================

        /// <summary>
        /// Runs at 150 ms and services both relays from one GETPOWERMODE reply. The same
        /// call keeps the server's power-services heartbeat fresh — it stamps a
        /// timestamp on every GETPOWERMODE and reports it back through
        /// InfoRsp.PowerServicesProvided.
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
                        HandleBeamRequest(pm);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex) when (IsConnectionFault(ex))
                    {
                        // The link dropping is not a power event. Whatever state the
                        // contactor is in stays, and the timers below keep running —
                        // which is what lets a restart finish after the board has gone.
                        //
                        // The beam relay is the exception: a request we can no longer
                        // confirm is a request we stop honouring.
                        beamRequested = false;
                    }
                }
                else
                {
                    beamRequested = false;
                }

                await ServiceRelaysAsync(ct);

                if (ct.IsCancellationRequested || IsDisposed) return;

                PaintPowerStatus();
                PaintBeamStatus();
                PaintRelays();

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

                // A reboot that completed also retires the pending marker; a shutdown
                // clears it below, when the flag is first seen.
                if (pendingRequest == PowerRequest.Reboot) pendingRequest = null;

                // And only then may it ask for another cycle.
                rebootArmed = true;
                return;
            }

            // ON and OFF forcibly override, so requests are only acted on in AUTO. The
            // server parks with the flag set, so a request arriving while forced is not
            // lost — it is honoured on the first AUTO poll instead.
            if (powerMode != OverrideMode.Auto) return;

            if (pm.RequestPoweroff)
            {
                shutdownLatched = true;
                restartInProgress = false;
                pendingRequest = null;      // honoured; the latch now tells the story
                return;
            }

            if (pm.RequestReboot && rebootArmed && !restartInProgress)
            {
                restartInProgress = true;
                restartOpenedAtUtc = DateTime.UtcNow;
                rebootArmed = false;
                pendingRequest = null;      // honoured; the flash now tells the story
            }
        }

        /// <summary>
        /// request_beam is a LIVE signal, not a one-shot event like the two power flags,
        /// so there is nothing to latch or arm here. The engine raises it through
        /// set_beam_request() when a Dynamic or Expose task wants beam and drops it when
        /// the task ends, a SEFI is detected, or the exposure limit is hit. The relay
        /// follows it directly.
        /// </summary>
        private void HandleBeamRequest(PowerMode pm)
        {
            beamRequested = pm.RequestBeam;
        }

        /// <summary>Contactor state the current power mode and latches call for.</summary>
        private bool ComputeDesiredPower()
        {
            switch (powerMode)
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

        /// <summary>
        /// Beam-enable state the current beam mode calls for.
        ///
        /// Note the fail-safe runs the OTHER WAY from ComputeDesiredPower. With no link
        /// the contactor assumes power is wanted, because a board that has lost comms
        /// still has to finish booting. With no link the beam relay opens, because
        /// nothing is left to drop it when the exposure limit is reached or a SEFI
        /// appears. beamRequested is already forced false on every path that notices the
        /// link is gone; the explicit check here covers the window before the first poll
        /// and any state the event handler has not seen yet.
        /// </summary>
        private bool ComputeDesiredBeam()
        {
            switch (beamMode)
            {
                case OverrideMode.On: return true;
                case OverrideMode.Off: return false;

                default:
                    if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
                        return false;
                    return beamRequested;
            }
        }

        private async Task ServiceRelaysAsync(CancellationToken ct)
        {
            bool wantPower = ComputeDesiredPower();
            bool wantBeam = ComputeDesiredBeam();

            bool changed = wantPower != powerDesired || wantBeam != beamDesired;
            powerDesired = wantPower;
            beamDesired = wantBeam;

            DateTime now = DateTime.UtcNow;
            bool verifyDue = now - lastRelayVerifyUtc >= RelayVerifyInterval;

            if (!changed && !verifyDue) return;

            if (relayChannelConflict)
            {
                // Both pills already say CH CONFLICT; driving nothing is the safe
                // reading of an ambiguous configuration.
                powerActual = null;
                beamActual = null;
                lastRelayVerifyUtc = now;
                return;
            }

            // Read the config on the UI thread — NumericUpDown and CheckBox are not
            // safe to touch from the worker.
            int powerChannel = (int)relayID_power.Value;
            int beamChannel = (int)relayID_beam.Value;
            bool powerInvert = invertRelay_power.Checked;
            bool beamInvert = invertRelay_beam.Checked;

            lastBeamChannel = beamChannel;
            lastBeamInvert = beamInvert;

            // USB control transfers block for a few milliseconds each and the HID
            // handle is not reentrant, so both channels are driven inside one worker
            // pass behind one lock. Keeping it off the UI thread is what stops the
            // 150 ms cadence from jittering when the board is slow.
            if (!await relayLock.WaitAsync(0, ct).ConfigureAwait(true))
                return;   // a previous pass is still in flight; skip this tick

            (bool? power, bool? beam) result;
            try
            {
                result = await Task.Run(() => ApplyRelays(
                        powerChannel, wantPower, powerInvert,
                        beamChannel, wantBeam, beamInvert,
                        changed), ct)
                    .ConfigureAwait(true);
            }
            finally
            {
                relayLock.Release();
            }

            if (ct.IsCancellationRequested || IsDisposed) return;

            powerActual = result.power;
            beamActual = result.beam;
            lastRelayVerifyUtc = now;
        }

        /// <summary>
        /// Drive and read back both channels in one pass. Returns the state actually
        /// observed on each, or null when no board is attached or that channel could not
        /// be reached. Runs on a worker thread — touch no controls from here.
        /// </summary>
        private (bool? power, bool? beam) ApplyRelays(
            int powerChannel, bool wantPower, bool powerInvert,
            int beamChannel, bool wantBeam, bool beamInvert,
            bool force)
        {
            if (relaySerial == null)
            {
                // Rescan occasionally so a board plugged in after launch is picked up.
                if (DateTime.UtcNow - lastRelayScanUtc < TimeSpan.FromSeconds(2))
                    return (null, null);
                lastRelayScanUtc = DateTime.UtcNow;

                relaySerial = UsbRelay.Enumerate().FirstOrDefault()?.Serial;
                if (relaySerial == null) return (null, null);
            }

            bool? power = DriveChannel(powerChannel, wantPower, powerInvert, force);

            // If the power channel lost the board, relaySerial is already null and a
            // beam attempt would only throw. Report both as unknown and rescan next tick.
            if (relaySerial == null) return (null, null);

            bool? beam = DriveChannel(beamChannel, wantBeam, beamInvert, force);

            return (power, beam);
        }

        /// <summary>
        /// Drive and read back one channel. Returns the LOAD state observed (true =
        /// energised load: contactor closed, or beam permitted), or null on failure.
        ///
        /// Invert maps the load's wiring to the relay coil. Unchecked means the load is
        /// on the NC contacts, which conduct when the coil is NOT energised, so load on
        /// = relay off. Checked means the load is on NO, which conducts when energised,
        /// so load on = relay on. Both directions reduce to one XNOR, and the mapping is
        /// its own inverse.
        ///
        /// For the BEAM channel, NO wiring (Invert checked) is the right choice and is
        /// the designer default: a relay board that is unplugged, unpowered or hung
        /// leaves the coil de-energised, which on NO blocks beam. On NC the same failure
        /// would permit it.
        /// </summary>
        private bool? DriveChannel(int channel, bool wantOn, bool invert, bool force)
        {
            if (relaySerial == null) return null;

            bool energise = wantOn == invert;

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

            return energised.Value == invert;
        }

        // ====================== Painting ======================

        private void PaintPowerStatus()
        {
            if (IsDisposed) return;

            if (restartInProgress)
            {
                bool flashOn = (Environment.TickCount64 / FlashPeriodMs) % 2 == 0;
                SetPill(powerStatus, "SYSTEM RESTARTING", flashOn ? FlashRed : FlashOrange);
            }
            else if (shutdownLatched)
            {
                SetPill(powerStatus, "SYSTEM SHUTDOWN", ShutdownColor);
            }
            else if (pendingRequest.HasValue)
            {
                // Accepted by the server but not yet honoured by the engine. Amber
                // rather than red: nothing has moved yet and the contactor is still
                // closed.
                SetPill(powerStatus,
                        pendingRequest == PowerRequest.Shutdown
                            ? "SHUTDOWN REQUESTED" : "RESTART REQUESTED",
                        RequestedColor);
            }
            else
            {
                SetPill(powerStatus, "SYSTEM OPERATIONAL", OperationalColor);
            }
        }

        private void PaintBeamStatus()
        {
            if (IsDisposed) return;

            // Forced modes are reported as forced, whatever the ZCU is asking for —
            // the whole point of the override is that the request is being ignored.
            if (beamMode == OverrideMode.On)
            {
                SetPill(beamRequestStatus, "FORCED ON", OverriddenColor);
                return;
            }

            if (beamMode == OverrideMode.Off)
            {
                SetPill(beamRequestStatus,
                        beamRequested ? "REQ BLOCKED" : "FORCED OFF",
                        beamRequested ? OverriddenColor : ShutdownColor);
                return;
            }

            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                SetPill(beamRequestStatus, "NO LINK", OfflineColor);
                return;
            }

            SetPill(beamRequestStatus,
                    beamRequested ? "REQUESTED" : "NOT REQUESTED",
                    beamRequested ? TrueColor : OperationalColor);
        }

        private void PaintRelays()
        {
            if (IsDisposed) return;

            if (relayChannelConflict)
            {
                SetPill(contactorStatus, "CH CONFLICT", FaultColor);
                SetPill(beamRelayStatus, "CH CONFLICT", FaultColor);
                return;
            }

            if (powerActual == null)
                SetPill(contactorStatus, "NO RELAY", OfflineColor);
            else
                SetPill(contactorStatus,
                        powerActual.Value ? "CLOSED" : "OPEN",
                        powerActual.Value ? TrueColor : FalseColor);

            if (beamActual == null)
                SetPill(beamRelayStatus, "NO RELAY", OfflineColor);
            else
                SetPill(beamRelayStatus,
                        beamActual.Value ? "ENABLED" : "CLAMPED",
                        beamActual.Value ? TrueColor : FalseColor);
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

            SetBoolPill(activeStatus, "ACTIVE", info.BeamPresent);
            SetBoolPill(possibleStatus, "POSSIBLE", info.BeamPossible);

            // Unlike every other pill here, this reflects an OUTPUT the ZCU drives
            // rather than a line it senses. It should track BEAM REQUEST: both come
            // from set_beam_request(). If it disagrees, either the beam override is
            // forced, or something has driven the clamp line out of band with
            // CMD_CLAMPCMD, which bypasses the REQUEST_BEAM global.
            SetBoolPill(clampStatus, "CLAMP", info.ClampRequest);

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
            // task is NOT counted here — Queue is strictly what is still pending.
            queueSize.Text = $"{status.Queue.Length} Task(s)";

            // The reply no longer carries the running Record, only its TYPE, so there is
            // no rid to show. It also only becomes non-null at the engine's first
            // progress tick, which is up to 200 ms after the task starts — expect a
            // brief "BUSY / NONE" overlap at the top of every task.
            taskName.Text = status.CurrentRequestType.HasValue
                ? status.CurrentRequestType.Value.ToString().ToUpperInvariant()
                : "NONE";

            // An Expose task has no usable progress number: the engine divides seconds
            // by milliseconds there, and reports a flat 0.5 when no exposure limit is
            // configured. IsProgressKnown is false for it, and a bar parked at zero for
            // a twenty-minute exposure reads as "stuck", so show marquee instead.
            if (status.IsProgressKnown)
            {
                SetProgressStyle(ProgressBarStyle.Continuous);
                SetProgress((status.NormalizedProgress ?? 0.0) * 100.0);
            }
            else if (status.CurrentRequestType == RecordType.Expose)
            {
                SetProgressStyle(ProgressBarStyle.Marquee);
            }
            else
            {
                SetProgressStyle(ProgressBarStyle.Continuous);
                SetProgress(0.0);
            }

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

            engineBusy = !status.IsIdle;
            abortButton.Enabled = engineBusy;
        }

        private void ApplyEngineOffline()
        {
            if (IsDisposed) return;
            queueSize.Text = "0 Task(s)";
            taskName.Text = "NONE";
            SetProgressStyle(ProgressBarStyle.Continuous);
            SetProgress(0.0);
            SetPill(idleStatus, "OFFLINE", OfflineColor);
            engineBusy = false;
            abortButton.Enabled = false;
        }

        /// <summary>
        /// Switch the bar between a measured fraction and an indeterminate animation.
        /// Guarded because assigning Style calls UpdateStyles() unconditionally, which
        /// restarts the marquee animation — at a 750 ms poll that reads as a stutter.
        /// </summary>
        private void SetProgressStyle(ProgressBarStyle style)
        {
            if (taskProgress.Style == style) return;
            taskProgress.Style = style;
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
                    "progress tick, so this is not instantaneous. An Expose or Dynamic " +
                    "task also drops its beam request on the way out, which releases the " +
                    "beam relay.",
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