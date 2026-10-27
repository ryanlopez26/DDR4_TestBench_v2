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
    public partial class RecordManager : Form
    {
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

        private CancellationTokenSource? refreshCts;

        /// <summary>
        /// Record ids handed out by GENRECORD and queued, but not yet visible in the
        /// archive. This exists because the server's gen_record_number() only checks
        /// RECORD_LOG, and a record isn't added there until it COMPLETES — so two
        /// GENRECORD calls before the first task finishes can return the same id. With
        /// submit_record now upserting, the second record would overwrite the first.
        /// Tracking what we've already issued lets us re-roll instead.
        /// </summary>
        private readonly HashSet<ushort> pendingRids = new HashSet<ushort>();

        /// <summary>Cheap change-detector so a 1 Hz refresh doesn't rebuild an unchanged tree.</summary>
        private string lastSignature = "";

        private static readonly Color CompletedColor = Color.FromArgb(0x1E, 0x7E, 0x34);
        private static readonly Color AbortedColor = Color.FromArgb(0xA9, 0x30, 0x26);
        private static readonly Color PendingColor = Color.FromArgb(0x7F, 0x8C, 0x8D);

        public RecordManager()
        {
            InitializeComponent();

            ConfigureControls();
            WireEvents();

            Shown += RecordManager_Shown;
            FormClosed += RecordManager_FormClosed;
            TcpManager.StatusChanged += OnConnectionStatusChanged;
        }

        // ============================== Lifecycle ==============================

        private void RecordManager_Shown(object? sender, EventArgs e)
        {
            ApplyConnectionState();

            refreshCts = new CancellationTokenSource();
            _ = RefreshLoopAsync(refreshCts.Token);
        }

        private void RecordManager_FormClosed(object? sender, FormClosedEventArgs e)
        {
            // Static event: without this the form is kept alive for the life of the
            // process and its handler keeps firing after disposal.
            TcpManager.StatusChanged -= OnConnectionStatusChanged;
            refreshCts?.Cancel();
        }

        private void ConfigureControls()
        {
            allRecords.HideSelection = false;   // keep the selection visible when focus leaves
            allRecords.FullRowSelect = false;   // incompatible with ShowLines
            allRecords.ShowNodeToolTips = true;
        }

        private void WireEvents()
        {
            requestWrite.Click += async (s, e) => await QueueRequestAsync(RecordType.Write);
            requestVerify.Click += async (s, e) => await QueueRequestAsync(RecordType.Verify);
            requestRead.Click += async (s, e) => await QueueRequestAsync(RecordType.Read);
            requestDynamic.Click += async (s, e) => await QueueRequestAsync(RecordType.Dynamic);

            allRecords.NodeMouseDoubleClick += AllRecords_NodeMouseDoubleClick;
            allRecords.KeyDown += AllRecords_KeyDown;
        }

        private void OnConnectionStatusChanged(TcpManager.ConnectionStatus status)
        {
            if (IsDisposed || !IsHandleCreated) return;

            // Can arrive from a pool thread when a framing fault drops the link.
            if (InvokeRequired) BeginInvoke((Action)ApplyConnectionState);
            else ApplyConnectionState();
        }

        private void ApplyConnectionState()
        {
            bool connected = TcpManager.Status == TcpManager.ConnectionStatus.Connected;
            requestWrite.Enabled = connected;
            requestVerify.Enabled = connected;
            requestRead.Enabled = connected;
            requestDynamic.Enabled = connected;
        }

        // ============================== Queueing ==============================

        /// <summary>
        /// Allocate a record id and push a task onto the engine queue.
        ///
        /// The queue commands get NO REPLY from the server — they only append a
        /// TaskRequest — so "success" here means the frame went out, not that the task
        /// ran or even that the engine accepted it. The task appears under Pending until
        /// it finishes and lands in the archive.
        /// </summary>
        private async Task QueueRequestAsync(RecordType type)
        {
            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                MessageBox.Show("Not connected.");
                return;
            }

            // The engine snapshots the server's committed config when a task STARTS, so
            // a locally edited but unsent config silently does not apply. Easy mistake
            // to make between the Config editor and this window.
            if (Config.IsDirty &&
                MessageBox.Show(
                    "The local configuration has not been sent to the ZCU, so this task " +
                    "will run with the server's current geometry instead.\n\nQueue it anyway?",
                    "Configuration out of date",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            SetRequestButtonsEnabled(false);
            try
            {
                var ct = refreshCts?.Token ?? CancellationToken.None;

                ushort? rid = await AllocateRecordIdAsync(ct);
                if (rid == null)
                {
                    MessageBox.Show(
                        "Could not obtain an unused record id. Wait for the queued tasks " +
                        "to finish and try again.", "Queue Request",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                await TcpManager.QueueTaskAsync(type, rid.Value, ct);

                pendingRids.Add(rid.Value);
                lastSignature = "";              // force the tree to redraw with the new entry
                await RefreshOnceAsync(ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!IsDisposed)
                    MessageBox.Show($"Request failed: {ex.Message}", "Queue Request",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (!IsDisposed) SetRequestButtonsEnabled(true);
            }
        }

        /// <summary>
        /// Ask the server for a record id, re-rolling if it hands back one we have
        /// already queued. GENRECORD picks at random and only excludes ids already in
        /// the archive, so a collision with an in-flight request is entirely possible.
        /// </summary>
        private async Task<ushort?> AllocateRecordIdAsync(CancellationToken ct)
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                ushort rid = await TcpManager.NewRecordIdAsync(ct);
                if (!pendingRids.Contains(rid)) return rid;
            }
            return null;
        }

        private void SetRequestButtonsEnabled(bool enabled)
        {
            requestWrite.Enabled = enabled;
            requestVerify.Enabled = enabled;
            requestRead.Enabled = enabled;
            requestDynamic.Enabled = enabled;
        }

        // ============================== Refresh ==============================

        /// <summary>
        /// Self-spacing loop: the next request starts one second after the previous one
        /// finishes, so a slow poll can't overlap itself. Continuations resume on the UI
        /// thread, so the tree rebuild needs no marshaling.
        /// </summary>
        private async Task RefreshLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (IsDisposed) return;

                await RefreshOnceAsync(ct);

                try { await Task.Delay(RefreshInterval, ct); }
                catch (OperationCanceledException) { return; }
            }
        }

        private async Task RefreshOnceAsync(CancellationToken ct)
        {
            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected) return;

            try
            {
                RecordSummary[] records = await TcpManager.ListRecordsAsync(ct);
                if (ct.IsCancellationRequested || IsDisposed) return;
                PopulateTree(records);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (IsConnectionFault(ex))
            {
                // Leave the existing nodes alone — a dropped link shouldn't look like
                // "all your records are gone".
            }
        }

        /// <summary>
        /// Rebuild the tree, grouped by record type, newest first. Skips the rebuild
        /// entirely when nothing has changed, which at 1 Hz is almost every tick:
        /// clearing and repopulating a TreeView collapses groups, drops the selection,
        /// and flickers, so doing it needlessly is actively unpleasant to use.
        /// </summary>
        private void PopulateTree(RecordSummary[] records)
        {
            // A record that has reached the archive is no longer pending.
            if (pendingRids.Count > 0)
            {
                var archived = records.Select(r => r.Rid).ToHashSet();
                pendingRids.RemoveWhere(archived.Contains);
            }

            string signature = BuildSignature(records);
            if (signature == lastSignature) return;
            lastSignature = signature;

            ushort? selected = SelectedRid();
            var collapsed = allRecords.Nodes
                                      .Cast<TreeNode>()
                                      .Where(n => !n.IsExpanded)
                                      .Select(n => n.Name)
                                      .ToHashSet();

            allRecords.BeginUpdate();
            try
            {
                allRecords.Nodes.Clear();

                // Requests that have been queued but haven't produced a record yet. Costs
                // nothing to show (it comes from local state) and gives the operator
                // immediate confirmation that the button did something.
                if (pendingRids.Count > 0)
                {
                    var group = allRecords.Nodes.Add("pending", $"Pending ({pendingRids.Count})");
                    foreach (ushort rid in pendingRids.OrderBy(x => x))
                    {
                        var node = group.Nodes.Add($"pending:{rid:X4}", $"{rid:X4}    queued");
                        node.ForeColor = PendingColor;
                        node.ToolTipText = "Queued; no record is archived until the task finishes.";
                    }
                }

                foreach (RecordType type in new[] { RecordType.Write, RecordType.Verify,
                                                    RecordType.Read, RecordType.Dynamic })
                {
                    var ofType = records.Where(r => r.Rtype == type)
                                        .OrderByDescending(r => r.TimeStarted)
                                        .ThenBy(r => r.Rid)
                                        .ToArray();
                    if (ofType.Length == 0) continue;

                    string key = type.ToString();
                    var group = allRecords.Nodes.Add(key, $"{type} ({ofType.Length})");

                    foreach (var r in ofType)
                    {
                        var node = group.Nodes.Add($"rec:{r.Rid:X4}", DescribeRecord(r));
                        node.Tag = r;
                        node.ForeColor = r.Aborted ? AbortedColor
                                       : r.Completed ? CompletedColor
                                       : PendingColor;
                        node.ToolTipText = "Double-click to open in the record viewer.";
                    }
                }

                foreach (TreeNode group in allRecords.Nodes)
                {
                    if (!collapsed.Contains(group.Name)) group.Expand();
                }

                RestoreSelection(selected);
            }
            finally
            {
                allRecords.EndUpdate();
            }
        }

        /// <summary>
        /// Everything the tree actually renders. Duration is included because it is the
        /// only summary field that changes once a record exists.
        /// </summary>
        private string BuildSignature(RecordSummary[] records)
        {
            var sb = new StringBuilder();
            foreach (var r in records)
            {
                sb.Append(r.Rid).Append(':').Append((int)r.Rtype).Append(':')
                  .Append(r.TimeStarted).Append(':').Append(r.TimeDuration).Append(':')
                  .Append(r.Completed ? 'C' : '-').Append(r.Aborted ? 'A' : '-').Append('|');
            }
            sb.Append("pending:");
            foreach (ushort rid in pendingRids.OrderBy(x => x)) sb.Append(rid).Append(',');
            return sb.ToString();
        }

        private static string DescribeRecord(RecordSummary r)
        {
            string status = r.Aborted ? "ABORTED" : r.Completed ? "COMPLETED" : "UNKNOWN";
            string started = r.TimeStarted == 0
                ? "—"
                : DateTimeOffset.FromUnixTimeSeconds((long)r.TimeStarted)
                                .LocalDateTime.ToString("MM-dd HH:mm:ss");

            return $"{r.Rid:X4}    {started}    {FormatDuration(r.TimeDuration)}    {status}";
        }

        // ============================== Opening ==============================

        private void AllRecords_NodeMouseDoubleClick(object? sender, TreeNodeMouseClickEventArgs e)
        {
            // Group nodes carry no Tag; let the double-click fall through to expand.
            if (e.Node?.Tag is RecordSummary r) OpenViewer(r.Rid);
        }

        private void AllRecords_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            if (SelectedRid() is ushort rid)
            {
                OpenViewer(rid);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Opens a fresh viewer each time rather than reusing one, so two records can be
        /// compared side by side.
        /// </summary>
        private void OpenViewer(ushort rid)
        {
            var viewer = new RecordViewer(rid);
            viewer.Show(this);
        }

        private ushort? SelectedRid() =>
            allRecords.SelectedNode?.Tag is RecordSummary r ? r.Rid : (ushort?)null;

        private void RestoreSelection(ushort? rid)
        {
            if (rid == null) return;

            foreach (TreeNode group in allRecords.Nodes)
            {
                foreach (TreeNode node in group.Nodes)
                {
                    if (node.Tag is RecordSummary r && r.Rid == rid.Value)
                    {
                        allRecords.SelectedNode = node;
                        return;
                    }
                }
            }
        }

        // ============================== Helpers ==============================

        private static string FormatDuration(ulong seconds)
        {
            if (seconds == 0) return "—";
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}h{ts.Minutes:00}m"
                : (ts.TotalMinutes >= 1 ? $"{ts.Minutes}m{ts.Seconds:00}s" : $"{ts.Seconds}s");
        }

        private static bool IsConnectionFault(Exception ex) =>
            ex is TimeoutException
            || ex is IOException
            || ex is SocketException
            || ex is InvalidDataException
            || ex is InvalidOperationException;

        private void splitContainer1_Panel2_Paint(object sender, PaintEventArgs e)
        {

        }
    }
}