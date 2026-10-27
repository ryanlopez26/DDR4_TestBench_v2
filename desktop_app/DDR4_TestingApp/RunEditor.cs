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
    public partial class RunEditor : Form
    {
        // ====================== Stage model ======================

        private enum Stage { Preliminary, Primary, Post }
        private enum StageState { Open, Pending, Locked }

        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

        // ====================== State ======================

        /// <summary>
        /// The run being edited. Always non-null: an unsaved new run is a Run with
        /// default fields, which keeps every code path uniform.
        /// </summary>
        private Run workingRun = NewBlankRun();

        /// <summary>
        /// Signature of the run as the server last reported it, for the dirty check.
        /// Null means this run has never been saved.
        /// </summary>
        private string? serverSignature;

        /// <summary>Id of the run loaded from the browser, or null for a new run.</summary>
        private ushort? loadedRunId;

        /// <summary>rid -> summary, refreshed on every poll, for labelling the record trees.</summary>
        private Dictionary<ushort, RecordSummary> recordIndex = new();

        /// <summary>
        /// Record ids issued by GENRECORD this session. Two purposes. The server's
        /// generator only excludes ids already in the archive, and a record isn't
        /// archived until it completes, so without this two requests in a row can
        /// collide and the second record overwrites the first. It also tells a queued
        /// record apart from a deleted one, which is what stops Prune from cutting the
        /// link to a task that is still running.
        /// </summary>
        private readonly HashSet<ushort> issuedRids = new();

        private bool loading;               // set while pushing model -> controls
        private CancellationTokenSource? pollCts;
        private readonly CancellationTokenSource formClosing = new();
        private string lastBrowserSignature = "";
        private RunSummary[] cachedRuns = Array.Empty<RunSummary>();

        private static readonly Color OpenColor = Color.FromArgb(0x27, 0xAE, 0x60);
        private static readonly Color PendingColor = Color.FromArgb(0xD6, 0xB0, 0x00);
        private static readonly Color LockedColor = Color.FromArgb(0xC0, 0x39, 0x2B);
        private static readonly Color NeutralColor = Color.FromArgb(0x7F, 0x8C, 0x8D);

        // ====================== Construction ======================

        public RunEditor()
        {
            InitializeComponent();

            ConfigureControls();
            WireEvents();
            StartNewRun();

            Shown += RunEditor_Shown;
            FormClosed += RunEditor_FormClosed;
            TcpManager.StatusChanged += OnConnectionStatusChanged;
        }

        private void RunEditor_Shown(object? sender, EventArgs e)
        {
            pollCts = new CancellationTokenSource();
            _ = PollLoopAsync(pollCts.Token);
        }

        private void RunEditor_FormClosed(object? sender, FormClosedEventArgs e)
        {
            // Static event: without this the form is kept alive for the life of the
            // process and the handler fires after disposal.
            TcpManager.StatusChanged -= OnConnectionStatusChanged;
            pollCts?.Cancel();
            formClosing.Cancel();
        }

        private void ConfigureControls()
        {
            // Run ids are u16; the designer leaves Maximum at its default of 100, so
            // anything above that can't even be displayed.
            runNumberEditor.Minimum = 0;
            runNumberEditor.Maximum = ushort.MaxValue;

            foreach (var box in new[] { createdTimestamp, modifiedTimestamp, lastUpdated, expectedStage })
            {
                // Enabled + ReadOnly so the text renders legibly; a disabled TextBox
                // greys its content.
                box.Enabled = true;
                box.ReadOnly = true;
                box.TabStop = false;
            }

            foreach (var pill in new[] { prelimaryLock, primaryLock, postLock, syncStatus })
            {
                // A disabled Button under the default FlatStyle is painted by the themed
                // renderer, which ignores BackColor entirely.
                pill.FlatStyle = FlatStyle.Flat;
                pill.FlatAppearance.BorderSize = 0;
                pill.ForeColor = Color.White;
                pill.Enabled = false;
                pill.TabStop = false;
            }

            foreach (var tree in new[] { runBrowser, preliminaryRecords, primaryRecords, postRecords })
            {
                tree.Scrollable = true;       // default, set explicitly
                tree.HideSelection = false;
                tree.ShowNodeToolTips = true;
            }

            runBrowser.FullRowSelect = false;   // incompatible with ShowLines

            sortBy.DropDownStyle = ComboBoxStyle.DropDownList;
            groupBy.DropDownStyle = ComboBoxStyle.DropDownList;
            runStage.DropDownStyle = ComboBoxStyle.DropDownList;
            runOperation.DropDownStyle = ComboBoxStyle.DropDownList;

            if (sortBy.SelectedIndex < 0) sortBy.SelectedIndex = 0;
            if (groupBy.SelectedIndex < 0) groupBy.SelectedIndex = 0;

            runStage.Items.AddRange(new object[] { "PRELIMINARY", "PRIMARY", "POST-RUN" });
            runStage.SelectedIndex = 0;

            // Every operation is legal in every stage now: a stage is just a list of
            // record ids, with no per-type slots to fill. The combo is therefore fixed
            // and no longer rebuilt when the stage changes.
            runOperation.Items.AddRange(new object[] { "WRITE", "VERIFY", "READ", "DYNAMIC" });
            runOperation.SelectedIndex = 0;
        }

        private void WireEvents()
        {
            runBrowser.AfterSelect += RunBrowser_AfterSelect;

            foreach (var box in new[] { runSODIMM, runIon, runEnergy, runLET, runAngle,
                                        runFlux, runAirgap, runFluence, textBox1 })
            {
                box.TextChanged += RunField_Changed;
            }
            runDescription.TextChanged += RunField_Changed;
            runNumberEditor.ValueChanged += RunField_Changed;

            sortBy.SelectedIndexChanged += BrowserOption_Changed;
            groupBy.SelectedIndexChanged += BrowserOption_Changed;
            invertSort.CheckedChanged += BrowserOption_Changed;

            runStage.SelectedIndexChanged += (s, e) => RefreshRequestEnabled();
            runOperation.SelectedIndexChanged += (s, e) => RefreshRequestEnabled();
            requestOperation.Click += RequestOperation_Click;

            submitRun.Click += SubmitRun_Click;
            deleteRun.Click += DeleteRun_Click;
            pruneRecords.Click += PruneRecords_Click;

            preliminaryRecords.NodeMouseDoubleClick += RecordTree_DoubleClick;
            primaryRecords.NodeMouseDoubleClick += RecordTree_DoubleClick;
            postRecords.NodeMouseDoubleClick += RecordTree_DoubleClick;
        }

        private void OnConnectionStatusChanged(TcpManager.ConnectionStatus status)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) BeginInvoke((Action)RefreshSyncPill);
            else RefreshSyncPill();
        }

        // ====================== New / load ======================

        private static Run NewBlankRun() => new Run
        {
            TimeStarted = 0,
            TimeModified = 0,
            Id = 0,
            SodimmId = "",
            Ion = "",
            Description = "",
        };

        /// <summary>Reset the editor to an unsaved, empty run.</summary>
        public void StartNewRun()
        {
            workingRun = NewBlankRun();
            loadedRunId = null;
            serverSignature = null;
            PushRunToUi();
        }

        private async Task LoadRunAsync(ushort id, CancellationToken ct)
        {
            try
            {
                Run? run = await TcpManager.GetRunAsync(id, ct);
                if (IsDisposed) return;

                if (run == null)
                {
                    ReportError(new InvalidOperationException(
                        $"the server no longer has run {id} — the browser list is stale"));
                    return;
                }

                workingRun = run;
                loadedRunId = id;
                serverSignature = RunSignature(run);
                PushRunToUi();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (IsConnectionFault(ex))
            {
                ReportError(ex);
            }
        }

        // ====================== Model <-> UI ======================

        /// <summary>
        /// Model -> controls. Guarded by <c>loading</c> so the TextChanged handlers
        /// don't write straight back into the model.
        /// </summary>
        private void PushRunToUi()
        {
            loading = true;
            try
            {
                runNumber.Text = loadedRunId.HasValue
                    ? $"Run #{loadedRunId.Value}"
                    : "New Run";

                runNumberEditor.Value = Math.Min(workingRun.Id, ushort.MaxValue);
                runSODIMM.Text = workingRun.SodimmId;
                runIon.Text = workingRun.Ion;
                runEnergy.Text = Num(workingRun.Energy);
                runLET.Text = Num(workingRun.LET);
                runAngle.Text = Num(workingRun.Angle);
                runFlux.Text = Num(workingRun.Flux);
                runAirgap.Text = Num(workingRun.AirGap);
                runFluence.Text = Num(workingRun.Fluence);
                textBox1.Text = Num(workingRun.Temperature);
                runDescription.Text = workingRun.Description;

                createdTimestamp.Text = FormatEpoch(workingRun.TimeStarted);
                modifiedTimestamp.Text = FormatEpoch(workingRun.TimeModified);
            }
            finally
            {
                loading = false;
            }

            RefreshStageUi();
            RefreshRecordTrees();
            RefreshSyncPill();
        }

        /// <summary>Controls -> model. Invalid numbers are flagged and left uncommitted.</summary>
        private void PullRunFromUi()
        {
            workingRun.Id = (ushort)runNumberEditor.Value;
            workingRun.SodimmId = runSODIMM.Text.Trim();
            workingRun.Ion = runIon.Text.Trim();
            workingRun.Description = runDescription.Text;

            workingRun.Energy = ParseInto(runEnergy, workingRun.Energy);
            workingRun.LET = ParseInto(runLET, workingRun.LET);
            workingRun.Angle = ParseInto(runAngle, workingRun.Angle);
            workingRun.Flux = ParseInto(runFlux, workingRun.Flux);
            workingRun.AirGap = ParseInto(runAirgap, workingRun.AirGap);
            workingRun.Fluence = ParseInto(runFluence, workingRun.Fluence);
            workingRun.Temperature = ParseInto(textBox1, workingRun.Temperature);
        }

        private static double ParseInto(TextBox box, double current)
        {
            string text = box.Text.Trim();
            if (text.Length == 0)
            {
                box.BackColor = SystemColors.Window;
                return 0.0;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture,
                                out double v))
            {
                box.BackColor = SystemColors.Window;
                return v;
            }

            // Don't fight the operator mid-keystroke; flag it and keep the old value.
            box.BackColor = Color.MistyRose;
            return current;
        }

        private void RunField_Changed(object? sender, EventArgs e)
        {
            if (loading) return;
            PullRunFromUi();
            RefreshSyncPill();
        }

        // ====================== Stage logic ======================

        private List<ushort> StageList(Stage s) => s switch
        {
            Stage.Preliminary => workingRun.PreliminaryRecords,
            Stage.Primary => workingRun.PrimaryRecords,
            _ => workingRun.PostRecords,
        };

        /// <summary>
        /// The stage currently being worked: the latest stage holding at least one
        /// record, or Preliminary when the run is empty.
        /// </summary>
        private Stage CurrentStage =>
            workingRun.PostRecords.Count > 0 ? Stage.Post
          : workingRun.PrimaryRecords.Count > 0 ? Stage.Primary
          : Stage.Preliminary;

        /// <summary>
        /// Locked when a LATER stage holds a record, so order can't be violated —
        /// Preliminary may be skipped but not revisited once Primary has started.
        /// Open when it is the current stage, Pending when it is empty and still ahead.
        /// Locked wins over pending.
        /// </summary>
        private StageState StateOf(Stage s)
        {
            bool laterHasEntries = s switch
            {
                Stage.Preliminary => workingRun.PrimaryRecords.Count > 0
                                  || workingRun.PostRecords.Count > 0,
                Stage.Primary => workingRun.PostRecords.Count > 0,
                _ => false,
            };
            if (laterHasEntries) return StageState.Locked;
            return s == CurrentStage ? StageState.Open : StageState.Pending;
        }

        private void RefreshStageUi()
        {
            ApplyPill(prelimaryLock, StateOf(Stage.Preliminary));
            ApplyPill(primaryLock, StateOf(Stage.Primary));
            ApplyPill(postLock, StateOf(Stage.Post));

            expectedStage.Text = StageName(CurrentStage);

            // Record counts on the group headers. dynamicBox is the "Primary Records"
            // group despite the name the designer still uses for it.
            preliminaryBox.Text = $"Preliminary Records ({workingRun.PreliminaryRecords.Count})";
            dynamicBox.Text = $"Primary Records ({workingRun.PrimaryRecords.Count})";
            postBox.Text = $"Post-run Records ({workingRun.PostRecords.Count})";

            RefreshRequestEnabled();
        }

        private static void ApplyPill(Button pill, StageState state)
        {
            (string text, Color back) = state switch
            {
                StageState.Open => ("OPEN", OpenColor),
                StageState.Pending => ("PENDING", PendingColor),
                _ => ("LOCKED", LockedColor),
            };
            if (pill.Text != text) pill.Text = text;
            if (pill.BackColor != back) pill.BackColor = back;
        }

        private static string StageName(Stage s) => s switch
        {
            Stage.Preliminary => "PRELIMINARY",
            Stage.Primary => "PRIMARY",
            _ => "POST-RUN",
        };

        private Stage SelectedStage() => runStage.SelectedIndex switch
        {
            1 => Stage.Primary,
            2 => Stage.Post,
            _ => Stage.Preliminary,
        };

        private RecordType SelectedOperation() => runOperation.SelectedItem?.ToString() switch
        {
            "VERIFY" => RecordType.Verify,
            "READ" => RecordType.Read,
            "DYNAMIC" => RecordType.Dynamic,
            _ => RecordType.Write,
        };

        /// <summary>
        /// Why the current stage can't be requested into, or null if it can. Only the
        /// link and the stage order constrain this now — any record type may be
        /// attached to any stage.
        /// </summary>
        private string? RequestBlockedReason()
        {
            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
                return "Not connected.";

            Stage stage = SelectedStage();
            if (StateOf(stage) == StageState.Locked)
                return $"The {StageName(stage)} stage is locked: a later stage already has "
                     + "records, and the run order can't be walked backwards.";

            return null;
        }

        private void RefreshRequestEnabled()
        {
            requestOperation.Enabled = RequestBlockedReason() == null;
        }

        // ====================== Record trees ======================

        private void RefreshRecordTrees()
        {
            PopulateRecordTree(preliminaryRecords, workingRun.PreliminaryRecords);
            PopulateRecordTree(primaryRecords, workingRun.PrimaryRecords);
            PopulateRecordTree(postRecords, workingRun.PostRecords);
        }

        /// <summary>
        /// Render a stage's rid list the way the record manager does: status colour,
        /// rid, type, time, duration. Grouped by record type, since a stage can now hold
        /// any mix of them. Rids with no archived record are shown rather than hidden —
        /// greyed as "queued" if this editor issued them, "missing" otherwise, which is
        /// exactly the distinction Prune acts on.
        /// </summary>
        private void PopulateRecordTree(TreeView tree, List<ushort> rids)
        {
            ushort? selected = tree.SelectedNode?.Tag as ushort?;
            var collapsed = tree.Nodes.Cast<TreeNode>()
                                .Where(n => !n.IsExpanded)
                                .Select(n => n.Name)
                                .ToHashSet();

            tree.BeginUpdate();
            try
            {
                tree.Nodes.Clear();

                foreach (RecordType type in new[] { RecordType.Write, RecordType.Verify,
                                                    RecordType.Read, RecordType.Dynamic })
                {
                    // An unarchived rid has no known type; it is listed under Unknown.
                    var ofType = rids.Where(r => recordIndex.TryGetValue(r, out var s)
                                                 && s.Rtype == type).ToList();
                    if (ofType.Count == 0) continue;

                    var parent = tree.Nodes.Add(type.ToString(),
                                                $"{type} ({ofType.Count})");
                    foreach (ushort rid in ofType) AddRecordNode(parent.Nodes, rid);
                }

                var unknown = rids.Where(r => !recordIndex.ContainsKey(r)).ToList();
                if (unknown.Count > 0)
                {
                    var parent = tree.Nodes.Add("Unknown", $"Unknown ({unknown.Count})");
                    foreach (ushort rid in unknown) AddRecordNode(parent.Nodes, rid);
                }

                foreach (TreeNode n in tree.Nodes)
                    if (!collapsed.Contains(n.Name)) n.Expand();

                if (selected.HasValue) RestoreRecordSelection(tree, selected.Value);
            }
            finally
            {
                tree.EndUpdate();
            }
        }

        private void AddRecordNode(TreeNodeCollection target, ushort rid)
        {
            var node = target.Add($"rec:{rid:X4}", DescribeRecord(rid));
            node.Tag = rid;
            node.ForeColor = ColorForRecord(rid);
            node.ToolTipText = recordIndex.ContainsKey(rid)
                ? "Double-click to open in the record viewer."
                : issuedRids.Contains(rid)
                    ? "Queued — no record is archived until the task finishes."
                    : "No such record on the ZCU. Prune Records will drop this link.";
        }

        private string DescribeRecord(ushort rid)
        {
            if (!recordIndex.TryGetValue(rid, out var r))
                return issuedRids.Contains(rid)
                    ? $"{rid:X4}    queued"
                    : $"{rid:X4}    MISSING";

            string status = r.Aborted ? "ABORTED" : r.Completed ? "COMPLETED" : "UNKNOWN";
            string started = r.TimeStarted == 0
                ? "—"
                : DateTimeOffset.FromUnixTimeSeconds((long)r.TimeStarted)
                                .LocalDateTime.ToString("MM-dd HH:mm:ss");

            return $"{rid:X4}    {started}    {FormatDuration(r.TimeDuration)}    {status}";
        }

        private Color ColorForRecord(ushort rid)
        {
            if (!recordIndex.TryGetValue(rid, out var r))
                return issuedRids.Contains(rid) ? NeutralColor : LockedColor;
            return r.Aborted ? LockedColor : r.Completed ? OpenColor : NeutralColor;
        }

        private static void RestoreRecordSelection(TreeView tree, ushort rid)
        {
            foreach (TreeNode top in tree.Nodes)
            {
                if (top.Tag is ushort t && t == rid) { tree.SelectedNode = top; return; }
                foreach (TreeNode child in top.Nodes)
                    if (child.Tag is ushort c && c == rid) { tree.SelectedNode = child; return; }
            }
        }

        private void RecordTree_DoubleClick(object? sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Node?.Tag is ushort rid) new RecordViewer(rid).Show(this);
        }

        // ====================== Requesting an operation ======================

        private async void RequestOperation_Click(object? sender, EventArgs e)
        {
            // Re-checked here rather than trusting the button state: a poll can land
            // between the last RefreshRequestEnabled and this click.
            string? blocked = RequestBlockedReason();
            if (blocked != null)
            {
                MessageBox.Show(blocked, "Request Operation",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                RefreshRequestEnabled();
                return;
            }

            Stage stage = SelectedStage();
            RecordType type = SelectedOperation();

            // The engine snapshots the server's committed config when a task STARTS, so
            // locally edited but unsent geometry silently does not apply.
            if (Config.IsDirty &&
                MessageBox.Show(
                    "The local configuration has not been sent to the ZCU, so this task " +
                    "will run with the server's current geometry.\n\nQueue it anyway?",
                    "Configuration out of date",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            requestOperation.Enabled = false;
            try
            {
                var ct = pollCts?.Token ?? CancellationToken.None;

                ushort? rid = await AllocateRecordIdAsync(ct);
                if (rid == null)
                {
                    MessageBox.Show(
                        "Could not obtain an unused record id. Let the queued tasks finish " +
                        "and try again.", "Request Operation",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // A dynamic task carries three extra fields. parent_run links the record
                // back to this run server-side; auto_reboot_on_sefi follows the config's
                // automated SEFI setting, which is otherwise unreachable from the wire.
                await TcpManager.QueueTaskAsync(
                    type, rid.Value,
                    autoRebootOnSefi: Config.AutomatedSefiMode,
                    numberOfTrials: 1,
                    parentRun: workingRun.Id,
                    ct: ct);

                issuedRids.Add(rid.Value);
                StageList(stage).Add(rid.Value);

                PushRunToUi();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!IsDisposed)
                    MessageBox.Show($"Request failed: {ex.Message}", "Request Operation",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (!IsDisposed) RefreshRequestEnabled();
            }
        }

        /// <summary>
        /// Ask the server for a record id, re-rolling if it hands back one already
        /// queued or already cited by this run. GENRECORD picks at random and only
        /// excludes ids in the archive, so a collision with an in-flight request is
        /// entirely possible — and with submit_record upserting, the loser is silently
        /// overwritten rather than duplicated.
        /// </summary>
        private async Task<ushort?> AllocateRecordIdAsync(CancellationToken ct)
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                ushort rid = await TcpManager.NewRecordIdAsync(ct);
                if (!issuedRids.Contains(rid) && !workingRun.AllRecords.Contains(rid))
                    return rid;
            }
            return null;
        }

        // ====================== Prune ======================

        /// <summary>
        /// Drop every rid this run cites that has no record on the ZCU. Purely local:
        /// the lists are edited in memory and the change is not persisted until Submit,
        /// so a mistaken prune is undone by reselecting the run in the browser.
        ///
        /// Records queued by this editor but not yet archived are DELIBERATELY kept.
        /// They are absent from LISTRECORDS for the same reason a deleted record is —
        /// the archive only gains a record when its task finishes — so pruning on
        /// absence alone would cut the link to a task that is still running.
        /// </summary>
        private void PruneRecords_Click(object? sender, EventArgs e)
        {
            var missing = workingRun.AllRecords
                                    .Distinct()
                                    .Where(rid => !recordIndex.ContainsKey(rid)
                                               && !issuedRids.Contains(rid))
                                    .OrderBy(rid => rid)
                                    .ToList();

            int queued = workingRun.AllRecords.Distinct().Count(
                rid => !recordIndex.ContainsKey(rid) && issuedRids.Contains(rid));

            if (missing.Count == 0)
            {
                MessageBox.Show(
                    queued > 0
                        ? $"Nothing to prune. {queued} record(s) are queued but not yet " +
                          "archived; those links are kept."
                        : "Every record this run cites exists on the ZCU.",
                    "Prune Records", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                // The record index is whatever the last successful poll returned. On a
                // dead link it is stale, and everything would look missing.
                MessageBox.Show(
                    "Not connected. The record list is stale, so pruning now could drop " +
                    "links to records that do exist.",
                    "Prune Records", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string list = string.Join(", ", missing.Take(20).Select(r => r.ToString("X4")))
                        + (missing.Count > 20 ? $", +{missing.Count - 20} more" : "");

            string queuedNote = queued > 0
                ? $"\n\n{queued} queued record(s) are not yet archived; their links are kept."
                : "";

            if (MessageBox.Show(
                    $"Remove {missing.Count} dead record link(s) from this run?\n\n{list}" +
                    queuedNote +
                    "\n\nThis only edits the run locally — press Submit Run to persist it.",
                    "Prune Records", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                != DialogResult.Yes)
                return;

            var dead = missing.ToHashSet();
            workingRun.PreliminaryRecords.RemoveAll(dead.Contains);
            workingRun.PrimaryRecords.RemoveAll(dead.Contains);
            workingRun.PostRecords.RemoveAll(dead.Contains);

            PushRunToUi();
        }

        // ====================== Submit / delete ======================

        private async void SubmitRun_Click(object? sender, EventArgs e)
        {
            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                MessageBox.Show("Not connected.");
                return;
            }

            PullRunFromUi();

            // The id comes straight from the Run # box and the server upserts by id, so
            // typing an id that already exists silently replaces that run.
            ushort id = workingRun.Id;
            if (loadedRunId.HasValue && loadedRunId.Value != id &&
                MessageBox.Show(
                    $"This editor loaded run {loadedRunId.Value}, but the Run # box says " +
                    $"{id}. Submitting will write to run {id}, overwriting it if it exists, " +
                    $"and leave run {loadedRunId.Value} as it was.\n\nContinue?",
                    "Run number changed", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                != DialogResult.Yes)
            {
                return;
            }

            ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (workingRun.TimeStarted == 0) workingRun.TimeStarted = now;
            workingRun.TimeModified = now;

            submitRun.Enabled = false;
            try
            {
                bool ok = await TcpManager.SaveRunAsync(workingRun, formClosing.Token);
                if (IsDisposed) return;

                if (!ok)
                {
                    MessageBox.Show(
                        "The server refused the save. SAVERUN returns false while /dvr is " +
                        "unmounted in safe mode, or if the file could not be written.",
                        "Submit Run", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                loadedRunId = id;
                serverSignature = RunSignature(workingRun);
                lastBrowserSignature = "";          // force the browser to pick it up
                PushRunToUi();
                await RefreshAsync(formClosing.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!IsDisposed)
                    MessageBox.Show($"Submit failed: {ex.Message}", "Submit Run",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (!IsDisposed) submitRun.Enabled = true;
            }
        }

        private async void DeleteRun_Click(object? sender, EventArgs e)
        {
            if (loadedRunId is not ushort id)
            {
                MessageBox.Show("This run has never been submitted, so there is nothing " +
                                "on the server to delete.");
                return;
            }

            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                MessageBox.Show("Not connected.");
                return;
            }

            if (MessageBox.Show(
                    $"Permanently delete run {id} from the ZCU, along with every record it " +
                    "references and those records' captures?\n\nThis cannot be undone.",
                    "Delete Run", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                != DialogResult.Yes)
                return;

            deleteRun.Enabled = false;
            try
            {
                bool ok = await TcpManager.DeleteRunAsync(id, formClosing.Token);
                if (IsDisposed) return;

                if (!ok)
                {
                    // delete_run drops the run entry even when a child unlink failed, so
                    // false can mean "mostly deleted" — re-list rather than assume.
                    MessageBox.Show(
                        "The server reported a problem. Some files may not have been " +
                        "removed; refresh the browser to see the current state.",
                        "Delete Run", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                StartNewRun();
                lastBrowserSignature = "";
                await RefreshAsync(formClosing.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!IsDisposed)
                    MessageBox.Show($"Delete failed: {ex.Message}", "Delete Run",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (!IsDisposed) deleteRun.Enabled = true;
            }
        }

        // ====================== Sync pill ======================

        /// <summary>
        /// "SYNCED" means the working copy matches what the server last reported. Record
        /// links added by Request, and links removed by Prune, live only in this editor
        /// until Submit — both turn this red.
        /// </summary>
        private void RefreshSyncPill()
        {
            if (IsDisposed) return;

            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
                SetPill(syncStatus, "NOT CONNECTED", NeutralColor);
            else if (serverSignature == null)
                SetPill(syncStatus, "NOT SUBMITTED", NeutralColor);
            else if (RunSignature(workingRun) != serverSignature)
                SetPill(syncStatus, "UNSAVED", LockedColor);
            else
                SetPill(syncStatus, "SYNCED", OpenColor);

            RefreshRequestEnabled();
        }

        private static void SetPill(Button b, string text, Color back)
        {
            if (b.Text != text) b.Text = text;
            if (b.BackColor != back) b.BackColor = back;
        }

        private static string RunSignature(Run r)
        {
            var sb = new StringBuilder();
            sb.Append(r.Id).Append('|')
              .Append(r.TimeStarted).Append('|')
              .Append(r.Fluence.ToString("R", CultureInfo.InvariantCulture)).Append('|')
              .Append(r.Temperature.ToString("R", CultureInfo.InvariantCulture)).Append('|')
              .Append(r.SodimmId).Append('|')
              .Append(r.Energy.ToString("R", CultureInfo.InvariantCulture)).Append('|')
              .Append(r.Ion).Append('|')
              .Append(r.LET.ToString("R", CultureInfo.InvariantCulture)).Append('|')
              .Append(r.Angle.ToString("R", CultureInfo.InvariantCulture)).Append('|')
              .Append(r.Flux.ToString("R", CultureInfo.InvariantCulture)).Append('|')
              .Append(r.AirGap.ToString("R", CultureInfo.InvariantCulture)).Append('|')
              .Append(r.Description).Append('|');

            // TimeModified is deliberately excluded: Submit stamps it, so including it
            // would make every saved run read dirty immediately afterwards.
            void Ids(IEnumerable<ushort> v)
            {
                foreach (ushort x in v) sb.Append(x).Append(',');
                sb.Append(';');
            }

            Ids(r.PreliminaryRecords);
            Ids(r.PrimaryRecords);
            Ids(r.PostRecords);

            return sb.ToString();
        }

        // ====================== Browser ======================

        private void BrowserOption_Changed(object? sender, EventArgs e)
        {
            lastBrowserSignature = "";   // ordering changed, force a rebuild
            PopulateBrowser(cachedRuns);
        }

        private async void RunBrowser_AfterSelect(object? sender, TreeViewEventArgs e)
        {
            if (e.Node?.Tag is not RunSummary summary) return;
            if (loadedRunId == summary.Id) return;

            if (serverSignature != null && RunSignature(workingRun) != serverSignature &&
                MessageBox.Show(
                    "The current run has unsaved changes, including any record links added " +
                    "or pruned since the last Submit. Discard them and load the selected run?",
                    "Unsaved changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                != DialogResult.Yes)
            {
                return;
            }

            await LoadRunAsync(summary.Id, formClosing.Token);
        }

        private void PopulateBrowser(RunSummary[] runs)
        {
            string signature = string.Join("|", runs.Select(
                r => $"{r.Id}:{r.TimeModified}:{r.SodimmId}:{r.Ion}:{r.LET}"))
                + $"#{sortBy.SelectedIndex}:{groupBy.SelectedIndex}:{invertSort.Checked}";

            if (signature == lastBrowserSignature) return;
            lastBrowserSignature = signature;

            ushort? selected = (runBrowser.SelectedNode?.Tag as RunSummary)?.Id;

            runBrowser.BeginUpdate();
            try
            {
                runBrowser.Nodes.Clear();

                IEnumerable<RunSummary> ordered = SortRuns(runs);

                foreach (var group in runs.Length == 0
                            ? Enumerable.Empty<IGrouping<string, RunSummary>>()
                            : ordered.GroupBy(GroupKey))
                {
                    TreeNodeCollection target;
                    if (groupBy.SelectedIndex == 0)
                    {
                        // Grouping by ID would make one node per run; keep it flat.
                        target = runBrowser.Nodes;
                    }
                    else
                    {
                        var parent = runBrowser.Nodes.Add(
                            "grp:" + group.Key,
                            $"{(group.Key.Length == 0 ? "(none)" : group.Key)} ({group.Count()})");
                        target = parent.Nodes;
                    }

                    foreach (var r in group)
                    {
                        var node = target.Add($"run:{r.Id}", DescribeRun(r));
                        node.Tag = r;
                    }
                }

                foreach (TreeNode n in runBrowser.Nodes) n.Expand();

                if (selected.HasValue)
                {
                    foreach (TreeNode top in runBrowser.Nodes)
                    {
                        if ((top.Tag as RunSummary)?.Id == selected) { runBrowser.SelectedNode = top; break; }
                        foreach (TreeNode child in top.Nodes)
                            if ((child.Tag as RunSummary)?.Id == selected) { runBrowser.SelectedNode = child; break; }
                    }
                }
            }
            finally
            {
                runBrowser.EndUpdate();
            }
        }

        private IEnumerable<RunSummary> SortRuns(RunSummary[] runs)
        {
            IEnumerable<RunSummary> q = sortBy.SelectedIndex switch
            {
                1 => runs.OrderBy(r => r.SodimmId).ThenBy(r => r.Id),
                2 => runs.OrderBy(r => r.Ion).ThenBy(r => r.Id),
                3 => runs.OrderBy(r => r.LET).ThenBy(r => r.Id),
                _ => runs.OrderBy(r => r.Id),
            };
            return invertSort.Checked ? q.Reverse() : q;
        }

        private string GroupKey(RunSummary r) => groupBy.SelectedIndex switch
        {
            1 => r.SodimmId,
            2 => r.Ion,
            3 => r.LET.ToString("0.###"),
            _ => r.Id.ToString(),
        };

        private static string DescribeRun(RunSummary r) =>
            $"Run {r.Id}    {r.SodimmId}    {r.Ion}    LET {r.LET:0.###}    {r.Description}";

        // ====================== Polling ======================

        /// <summary>
        /// Self-spacing loop: the next poll starts two seconds after the previous one
        /// finishes, so a slow round trip can't overlap itself.
        ///
        /// Deliberately does NOT re-fetch the open run — that would clobber unsaved
        /// edits. It refreshes the browser list and the record summary index, which is
        /// what makes the record trees and stage pills advance as queued tasks finish.
        /// </summary>
        private async Task PollLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (IsDisposed) return;

                await RefreshAsync(ct);

                try { await Task.Delay(PollInterval, ct); }
                catch (OperationCanceledException) { return; }
            }
        }

        private async Task RefreshAsync(CancellationToken ct)
        {
            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected) return;

            try
            {
                RunSummary[] runs = await TcpManager.ListRunsAsync(ct);
                RecordSummary[] records = await TcpManager.ListRecordsAsync(ct);
                if (ct.IsCancellationRequested || IsDisposed) return;

                cachedRuns = runs;
                recordIndex = records.GroupBy(r => r.Rid)
                                     .ToDictionary(g => g.Key, g => g.Last());

                // Anything now archived is no longer an outstanding reservation — and
                // stops being treated as "queued" by Prune and the trees.
                issuedRids.RemoveWhere(recordIndex.ContainsKey);

                PopulateBrowser(runs);
                RefreshRecordTrees();
                RefreshStageUi();
                RefreshSyncPill();

                lastUpdated.Text = DateTime.Now.ToString("HH:mm:ss");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (IsConnectionFault(ex))
            {
                // Leave the trees as they are — a dropped link shouldn't blank them, and
                // must not make every record look missing to Prune.
                ReportError(ex);
            }
        }

        // ====================== Helpers ======================

        private static string Num(double v) =>
            v == 0.0 ? "" : v.ToString("0.######", CultureInfo.InvariantCulture);

        private static string FormatEpoch(ulong epochSeconds) =>
            epochSeconds == 0
                ? ""
                : DateTimeOffset.FromUnixTimeSeconds((long)epochSeconds)
                                .LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");

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

        /// <summary>
        /// Replace with whatever your status bar uses. Deliberately not a MessageBox: a
        /// 2 s poll would stack dialogs on a dropped link.
        /// </summary>
        private void ReportError(Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[runEditor] {ex.GetType().Name}: {ex.Message}");
        }
    }
}