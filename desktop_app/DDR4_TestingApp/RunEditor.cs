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

        /// <summary>
        /// State of a single-slot primary link (priwrite / priverify). The slot counts
        /// as taken while it points at a record that exists or is on its way; it frees
        /// up only when that record is deleted, which is what makes "can't redo primary
        /// write unless the record is deleted" fall out of the data rather than needing
        /// a separate flag.
        /// </summary>
        private enum SlotState
        {
            Empty,       // no rid stored
            Queued,      // rid issued this session, task hasn't finished yet
            Performed,   // rid resolves to an archived record
            Dangling,    // rid stored but no such record — deleted from the viewer
        }

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
        /// Record ids issued by GENRECORD this session. The server's generator only
        /// excludes ids already in the archive, and a record isn't archived until it
        /// completes — so without this, two requests in a row can collide and the
        /// second record overwrites the first.
        /// </summary>
        private readonly HashSet<ushort> issuedRids = new();

        private bool loading;               // set while pushing model -> controls
        private CancellationTokenSource? pollCts;
        private readonly CancellationTokenSource formClosing = new();
        private string lastBrowserSignature = "";

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
                // Enabled = true + ReadOnly so the text renders legibly; a disabled
                // TextBox greys its content.
                box.Enabled = true;
                box.ReadOnly = true;
                box.TabStop = false;
            }

            foreach (var pill in new[] { prelimaryLock, primaryLock, postLock, syncStatus,
                                         primaryWrite, primaryVerify })
            {
                // A disabled Button ignores BackColor under the default FlatStyle — the
                // themed renderer paints system colours instead.
                pill.FlatStyle = FlatStyle.Flat;
                pill.FlatAppearance.BorderSize = 0;
                pill.ForeColor = Color.White;
            }

            prelimaryLock.Enabled = false;
            primaryLock.Enabled = false;
            postLock.Enabled = false;
            syncStatus.Enabled = false;
            prelimaryLock.TabStop = false;
            primaryLock.TabStop = false;
            postLock.TabStop = false;
            syncStatus.TabStop = false;

            foreach (var tree in new[] { runBrowser, preliminaryRecords, postRecords, treeView1 })
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
            RefreshOperationChoices();
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

            runStage.SelectedIndexChanged += RunStage_Changed;
            runOperation.SelectedIndexChanged += (s, e) => RefreshRequestEnabled();
            requestOperation.Click += RequestOperation_Click;

            submitRun.Click += SubmitRun_Click;
            deleteRun.Click += DeleteRun_Click;

            primaryWrite.Click += (s, e) => OpenPrimary(workingRun.Priwrite);
            primaryVerify.Click += (s, e) => OpenPrimary(workingRun.Priverify);

            preliminaryRecords.NodeMouseDoubleClick += RecordTree_DoubleClick;
            postRecords.NodeMouseDoubleClick += RecordTree_DoubleClick;
            treeView1.NodeMouseDoubleClick += RecordTree_DoubleClick;
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
                        $"the server no longer has run {id:X4} — the browser list is stale"));
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
                    : "Create a New Run";

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

        private int PreliminaryCount =>
            workingRun.Preverify.Count + workingRun.Prewrite.Count;

        private int PrimaryCount =>
            (workingRun.Priwrite.HasValue ? 1 : 0)
            + (workingRun.Priverify.HasValue ? 1 : 0)
            + (workingRun.Pridynamic?.Count ?? 0);

        private int PostCount =>
            workingRun.Postverify.Count + workingRun.Postwrite.Count;

        private int DynamicCount => workingRun.Pridynamic?.Count ?? 0;

        /// <summary>
        /// The stage currently being worked: the latest stage holding at least one
        /// record, or Preliminary when the run is empty.
        /// </summary>
        private Stage CurrentStage =>
            PostCount > 0 ? Stage.Post
          : PrimaryCount > 0 ? Stage.Primary
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
                Stage.Preliminary => PrimaryCount > 0 || PostCount > 0,
                Stage.Primary => PostCount > 0,
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

            // Record counts on the group headers.
            preliminaryBox.Text = $"Preliminary Records ({PreliminaryCount})";
            dynamicBox.Text = $"Primary Dynamic ({DynamicCount})";
            postBox.Text = $"Post-run Records ({PostCount})";

            ApplyPrimaryButton(primaryWrite, workingRun.Priwrite);
            ApplyPrimaryButton(primaryVerify, workingRun.Priverify);

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

        /// <summary>
        /// Resolve a stored primary rid against the archive. A rid this editor issued
        /// but hasn't seen archived yet is Queued; one it did not issue and cannot find
        /// has been deleted, which frees the slot for another request.
        /// </summary>
        private SlotState PrimarySlot(ushort? rid)
        {
            if (!rid.HasValue) return SlotState.Empty;
            if (recordIndex.ContainsKey(rid.Value)) return SlotState.Performed;
            if (issuedRids.Contains(rid.Value)) return SlotState.Queued;
            return SlotState.Dangling;
        }

        /// <summary>
        /// Primary write/verify are single-slot Options. Only Performed is clickable —
        /// the other three states have no record to open.
        /// </summary>
        private void ApplyPrimaryButton(Button button, ushort? rid)
        {
            SlotState slot = PrimarySlot(rid);

            (string text, Color back, bool clickable) = slot switch
            {
                SlotState.Performed => ($"Performed ({rid!.Value:X4})", OpenColor, true),
                SlotState.Queued => ($"Queued ({rid!.Value:X4})", NeutralColor, false),
                SlotState.Dangling => ($"Deleted ({rid!.Value:X4})", PendingColor, false),
                _ => ("Not Performed", LockedColor, false),
            };

            if (button.Text != text) button.Text = text;
            if (button.BackColor != back) button.BackColor = back;
            button.Enabled = clickable;
            button.TabStop = clickable;

            button.FlatAppearance.BorderSize = slot == SlotState.Dangling ? 1 : 0;
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

        private void RunStage_Changed(object? sender, EventArgs e)
        {
            RefreshOperationChoices();
            RefreshRequestEnabled();
        }

        /// <summary>
        /// Dynamic is only meaningful in the primary stage; the Run struct has no
        /// dynamic list anywhere else. Read is filed under the verify vectors.
        /// </summary>
        private void RefreshOperationChoices()
        {
            string? previous = runOperation.SelectedItem?.ToString();

            runOperation.Items.Clear();
            runOperation.Items.AddRange(new object[] { "WRITE", "VERIFY", "READ" });
            if (SelectedStage() == Stage.Primary) runOperation.Items.Add("DYNAMIC");

            int i = previous == null ? -1 : runOperation.Items.IndexOf(previous);
            runOperation.SelectedIndex = i >= 0 ? i : 0;
        }

        /// <summary>
        /// Operation chosen in the combo, or Write if nothing is selected.
        /// </summary>
        private RecordType SelectedOperation() => runOperation.SelectedItem?.ToString() switch
        {
            "VERIFY" => RecordType.Verify,
            "READ" => RecordType.Read,
            "DYNAMIC" => RecordType.Dynamic,
            _ => RecordType.Write,
        };

        /// <summary>
        /// Why the current stage/operation pair can't be requested, or null if it can.
        /// Used both to gate the button and to explain a click that slips through.
        /// </summary>
        private string? RequestBlockedReason()
        {
            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
                return "Not connected.";

            Stage stage = SelectedStage();
            if (StateOf(stage) == StageState.Locked)
                return $"The {StageName(stage)} stage is locked: a later stage already has "
                     + "records, and the run order can't be walked backwards.";

            // Primary write and verify hold exactly one rid each. Once taken, the only
            // way to free the slot is to delete that record in the record viewer.
            RecordType op = SelectedOperation();
            if (stage == Stage.Primary && op != RecordType.Dynamic)
            {
                ushort? rid = op == RecordType.Write ? workingRun.Priwrite : workingRun.Priverify;
                string name = op == RecordType.Write ? "write" : "verify";

                switch (PrimarySlot(rid))
                {
                    case SlotState.Performed:
                        return $"Primary {name} has already been performed as record "
                             + $"{rid!.Value:X4}. Delete that record in the record viewer "
                             + "to free the slot.";
                    case SlotState.Queued:
                        return $"Primary {name} is already queued as record {rid!.Value:X4}. "
                             + "Wait for it to finish.";
                }
            }

            return null;
        }

        private void RefreshRequestEnabled()
        {
            requestOperation.Enabled = RequestBlockedReason() == null;
        }

        // ====================== Record trees ======================

        private void RefreshRecordTrees()
        {
            // Preliminary: write and verify lists (Read requests land in verify).
            PopulateRecordTree(preliminaryRecords, new (string, IReadOnlyList<ushort>)[]
            {
                ("Write", workingRun.Prewrite),
                ("Verify", workingRun.Preverify),
            });

            // Primary dynamic is a flat list — every entry is the same type.
            PopulateRecordTree(treeView1, new (string, IReadOnlyList<ushort>)[]
            {
                ("", workingRun.Pridynamic ?? (IReadOnlyList<ushort>)Array.Empty<ushort>()),
            });

            PopulateRecordTree(postRecords, new (string, IReadOnlyList<ushort>)[]
            {
                ("Write", workingRun.Postwrite),
                ("Verify", workingRun.Postverify),
            });
        }

        /// <summary>
        /// Render rid lists the way the record manager does: status colour, rid, time,
        /// duration. A group name of "" produces a flat list instead of a parent node.
        /// Rids with no archived record yet (still queued, or deleted out from under
        /// the run) are shown greyed rather than hidden.
        /// </summary>
        private void PopulateRecordTree(TreeView tree,
                                        (string Group, IReadOnlyList<ushort> Rids)[] groups)
        {
            ushort? selected = tree.SelectedNode?.Tag as ushort?;

            tree.BeginUpdate();
            try
            {
                tree.Nodes.Clear();

                foreach (var (group, rids) in groups)
                {
                    if (rids.Count == 0) continue;

                    TreeNodeCollection target;
                    if (group.Length == 0)
                    {
                        target = tree.Nodes;
                    }
                    else
                    {
                        var parent = tree.Nodes.Add(group, $"{group} ({rids.Count})");
                        target = parent.Nodes;
                    }

                    foreach (ushort rid in rids)
                    {
                        var node = target.Add($"rec:{rid:X4}", DescribeRecord(rid));
                        node.Tag = rid;
                        node.ForeColor = ColorForRecord(rid);
                        node.ToolTipText = "Double-click to open in the record viewer.";
                    }
                }

                foreach (TreeNode n in tree.Nodes) n.Expand();

                if (selected.HasValue) RestoreRecordSelection(tree, selected.Value);
            }
            finally
            {
                tree.EndUpdate();
            }
        }

        private string DescribeRecord(ushort rid)
        {
            if (!recordIndex.TryGetValue(rid, out var r))
                return $"{rid:X4}    queued / not archived";

            string status = r.Aborted ? "ABORTED" : r.Completed ? "COMPLETED" : "UNKNOWN";
            string started = r.TimeStarted == 0
                ? "—"
                : DateTimeOffset.FromUnixTimeSeconds((long)r.TimeStarted)
                                .LocalDateTime.ToString("MM-dd HH:mm:ss");

            return $"{rid:X4}    {r.Rtype}    {started}    {FormatDuration(r.TimeDuration)}    {status}";
        }

        private Color ColorForRecord(ushort rid)
        {
            if (!recordIndex.TryGetValue(rid, out var r)) return NeutralColor;
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

        private void OpenPrimary(ushort? rid)
        {
            if (rid.HasValue) new RecordViewer(rid.Value).Show(this);
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
            // locally edited but unsent geometry silently doesn't apply.
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

                await TcpManager.QueueTaskAsync(type, rid.Value, ct);

                issuedRids.Add(rid.Value);
                AttachRecord(stage, type, rid.Value);

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

        /// <summary>Read is filed with verify; dynamic only exists in the primary stage.</summary>
        private void AttachRecord(Stage stage, RecordType type, ushort rid)
        {
            bool verifyLike = type == RecordType.Verify || type == RecordType.Read;

            switch (stage)
            {
                case Stage.Preliminary:
                    if (verifyLike) workingRun.Preverify.Add(rid);
                    else workingRun.Prewrite.Add(rid);
                    break;

                case Stage.Primary:
                    if (type == RecordType.Dynamic)
                    {
                        workingRun.Pridynamic ??= new List<ushort>();
                        workingRun.Pridynamic.Add(rid);
                    }
                    else if (verifyLike) workingRun.Priverify = rid;
                    else workingRun.Priwrite = rid;
                    break;

                default:
                    if (verifyLike) workingRun.Postverify.Add(rid);
                    else workingRun.Postwrite.Add(rid);
                    break;
            }
        }

        private async Task<ushort?> AllocateRecordIdAsync(CancellationToken ct)
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                ushort rid = await TcpManager.NewRecordIdAsync(ct);
                if (!issuedRids.Contains(rid) && !RunReferences(rid)) return rid;
            }
            return null;
        }

        private bool RunReferences(ushort rid) =>
            workingRun.Prewrite.Contains(rid)
            || workingRun.Preverify.Contains(rid)
            || workingRun.Priwrite == rid
            || workingRun.Priverify == rid
            || (workingRun.Pridynamic?.Contains(rid) ?? false)
            || workingRun.Postwrite.Contains(rid)
            || workingRun.Postverify.Contains(rid);

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
        /// links added by Request live only in this editor until Submit, so requesting
        /// an operation turns this red.
        /// </summary>
        private void RefreshSyncPill()
        {
            if (IsDisposed) return;

            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                SetPill(syncStatus, "NOT CONNECTED", NeutralColor);
            }
            else if (serverSignature == null)
            {
                SetPill(syncStatus, "NOT SUBMITTED", NeutralColor);
            }
            else if (RunSignature(workingRun) != serverSignature)
            {
                SetPill(syncStatus, "UNSAVED", LockedColor);
            }
            else
            {
                SetPill(syncStatus, "SYNCED", OpenColor);
            }

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
            void Ids(IEnumerable<ushort>? v)
            {
                if (v == null) { sb.Append("-;"); return; }
                foreach (ushort x in v) sb.Append(x).Append(',');
                sb.Append(';');
            }

            Ids(r.Preverify); Ids(r.Prewrite);
            sb.Append(r.Priwrite?.ToString() ?? "-").Append(';');
            Ids(r.Pridynamic);
            sb.Append(r.Priverify?.ToString() ?? "-").Append(';');
            Ids(r.Postverify); Ids(r.Postwrite);

            return sb.ToString();
        }

        // ====================== Browser ======================

        private void BrowserOption_Changed(object? sender, EventArgs e)
        {
            lastBrowserSignature = "";   // ordering changed, force a rebuild
            PopulateBrowser(cachedRuns);
        }

        private RunSummary[] cachedRuns = Array.Empty<RunSummary>();

        private async void RunBrowser_AfterSelect(object? sender, TreeViewEventArgs e)
        {
            if (e.Node?.Tag is not RunSummary summary) return;
            if (loadedRunId == summary.Id) return;

            if (serverSignature != null && RunSignature(workingRun) != serverSignature &&
                MessageBox.Show(
                    "The current run has unsaved changes, including any record links added " +
                    "since the last Submit. Discard them and load the selected run?",
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

                // Anything now archived is no longer an outstanding reservation.
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
                // Leave the tree as it is — a dropped link shouldn't blank the browser.
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