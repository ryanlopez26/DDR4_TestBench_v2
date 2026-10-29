// TcpManager.cs — C# client for the Rust ZCU104 DDR4-tester TCP server
// ("ZCU Server V3 — NSRL Edition").
//
// ============================== WHAT CHANGED THIS ROUND ==============================
//
// THIS ONE IS A FLAG DAY. Two reply shapes changed, so an old client does not
// fail cleanly — it misdecodes.
//
//  1. InfoRsp WAS RESTRUCTURED. It is no longer twelve bools. The beam fields
//     were replaced, clamp_request is gone, and a PMIC reading was inserted in
//     the middle:
//
//        beam_possible   -> GONE, replaced by beam_request + beam_services
//        beam_present    -> renamed beam_active, now the FIRST field
//        clamp_request   -> GONE (nothing reports the EMIO line any more)
//        pmic            -> NEW, Option<PowerStats>, 4th field, 1 or 26 bytes
//
//     Anything reading InfoRsp.BeamPossible or InfoRsp.ClampRequest has to be
//     reworked — those have no wire source now. Launcher.cs uses both.
//
//  2. Record's beam series COLLAPSED FROM THREE TO TWO and gained a PMIC
//     series. log_beam_present / log_beam_possible / log_beam_request became
//     log_beam_active / log_beam_request, with log_pmic inserted after
//     log_sefi_detected. RecordViewer.cs plots the old three.
//
//  3. NEW TYPE PowerStats { f64 volts, f64 amps, f64 watts, bool power_derived },
//     25 bytes. It arrives both as InfoRsp.pmic and per-sample in
//     Record.log_pmic. Read power.rs before trusting the numbers: this is a
//     VCC1V2 RAIL measurement that also carries PL bank I/O and the PS-side
//     DDR4 interface, not per-device SODIMM current.
//
//  4. CMD_BEAMSERVICES 0x1A exists in config.rs with BeamServicesCmd and
//     BeamServicesRsp in types.rs, but SERVER.RS HAS NO DISPATCH ARM FOR IT.
//     See SetBeamServicesAsync, and read the beam-path warning below — this
//     missing arm is currently load-bearing.
//
//  5. Expose progress is fixed and is now a PERCENTAGE like every other arm,
//     correctly scaled against the millisecond limit and capped at 100. The
//     only oddity left is a constant 50.0 when no limit is configured.
//
//  6. FPGA images are now plain `.bit`, not `.bit.bin`.
//
// ============================== THE BEAM PATH IS DEAD ========================
//
// Worth knowing before you read anything beam-related below. beam.rs declares
// BEAM_ON and BEAM_SERVICES but NOTHING IN THE TREE EVER WRITES THEM — the
// only writer would be the CMD_BEAMSERVICES arm that server.rs does not have.
// Everything downstream follows:
//
//   * InfoRsp.BeamActive and InfoRsp.BeamServices are always false.
//   * Record.LogBeamActive is all false on every record.
//   * THE EXPOSE TASK ENDS IMMEDIATELY. Its arm refreshes active_last_high only
//     while is_beam_active() is true, so that timer runs past the 200 ms hold
//     requirement on the first pass, the delayed exit arms, and the record
//     completes roughly 400 ms in with zero exposure. It does not hang — it
//     finishes, with nothing in it.
//   * The Dynamic arm's beam gating never sees beam.
//
// set_beam_request() also no longer touches GPIO, so CMD_CLAMPCMD moves a
// software flag and nothing else; gpio::set_clamp_request has no callers left
// anywhere in the tree. The EMIO beam lines (get_beam_present /
// get_beam_possible) are likewise uncalled — beam state is now meant to arrive
// over the wire from whatever supervises the facility signal.
//
// ============================= PRECEDING ROUND (mostly current) ==============
//
// One command was added and four fixes landed.
//
//  * NEW COMMAND 0x19 CMD_EXPOSE { u16 rid }, send-only. RecordType::Expose is
//    finally reachable — see QueueExposeAsync, and read its remarks before
//    queueing one, because an Expose task has two ways to sit forever.
//
//  * ConfigCmd.exposure_time_limit_ms IS NOW GENUINELY MILLISECONDS. The engine
//    compares it against exposure_time * 1000.0; it used to compare it against
//    raw seconds. Any limit tuned against the old build is now 1000x too short:
//    30.0 used to mean thirty seconds and now means thirty milliseconds.
//
//  * CMD_CLAMPCMD goes through set_beam_request() instead of poking the GPIO
//    directly, so it now moves REQUEST_BEAM as well as the line, and no longer
//    panics the handler thread on a GPIO error.
//
//  * InfoRsp.power_services_provided actually works. Its freshness test was
//    computed backwards and latched true once; it is a real 250 ms heartbeat
//    now, so a client polling GETPOWERMODE slower than that reads false.
//
// ============================= PRECEDING ROUND (still current) ===============
//
// Against the previous client, six things moved. The first two are flag days —
// an old client misdecodes the frame rather than failing cleanly.
//
//  1. EngineStatusRsp NO LONGER CARRIES THE RECORD. `current_request_type` is
//     now Option<RecordType> (1 tag byte + u32 variant), not Option<Record>.
//     EngineStatus.ActiveRecord is GONE — there is no rid, no progress arrays,
//     and no bit counts in a status poll any more. Polling is cheap now; use
//     GETRECORD once the record lands if you need the numbers.
//     *** This breaks any caller that read status.ActiveRecord (EngineStatusPoller,
//         Launcher, RecordManager). See the note on EngineStatus. ***
//
//  2. Record's single `log_beam_signal` SPLIT INTO THREE series:
//        log_beam_present   — beam on target (EMIO 78)
//        log_beam_possible  — facility says beam is available (EMIO 79)
//        log_beam_request   — what the server is asking for (clamp output)
//     In that order, between log_current_address and log_sefi_detected.
//     Record.LogBeamSignal survives as a read-only alias so
//     existing display code still compiles.
//
//  3. RecordType gained Expose = 4. Any `switch` on RecordType without a
//     default arm now throws at runtime on an Expose record.
//
//  4. ConfigCmd gained exposure_time_limit_enabled (bool) and
//     exposure_time_limit_ms (f64). WireSize is 43 bytes, was 34.
//
//  5. GetPowerModeRsp gained request_beam (bool), third field. This mirrors the
//     server's REQUEST_BEAM global, which is also what drives the clamp output.
//
//  6. New command 0x18 CMD_CLAMPCMD { bool state } — drive the clamp-request
//     line directly. Send-only, no reply. See SetClampAsync for the caveat.
//
// ============================== EXECUTION MODEL ==============================
//
//   Write/Verify/Read/Dynamic only PUSH A TASK onto the engine queue and send
//   NO RESPONSE AT ALL. A separate engine thread runs one task at a time.
//   Progress comes from polling CMD_ENGINESTATUS. The finished Record is
//   written to /dvr/records and fetched with CMD_GETRECORD. Byte-level data
//   goes to a capture file fetched with CMD_GETCAPTURE.
//
//   * Do NOT await a reply after queueing a task — you will block forever.
//     Queue*Async methods return as soon as the frame is on the wire.
//   * A task is identified by a u16 "rid" obtained from CMD_GENRECORD.
//   * Write/Verify/Dynamic carry no pattern+seed. Those live in the persistent
//     CONFIG (data_pattern / pattern_seed), so apply a Config first.
//
// ============================== FRAMING ==============================
//
//     [SYNC u32 BE = 0xDEADBEEF]
//     [CMD  u8 ]
//     [LEN  u32 BE]
//     [PAYLOAD ... LEN bytes]
//     [TERM u32 BE = 0xCAFEBABE]
//
// ============================== PAYLOAD ENCODING ==============================
//
// bincode with DefaultOptions().with_big_endian().with_fixint_encoding()
// .reject_trailing_bytes(). That means:
//
//   bool            1 byte, 0 or 1
//   u8/u16/u32/u64  fixed width, big-endian, no padding
//   f64             IEEE-754 bits, big-endian (8 bytes)
//   Option<T>       1 tag byte (0 = None, 1 = Some) followed by T if Some
//   String          u64 BE byte count, then UTF-8 bytes (NOT u32 — 8 bytes)
//   Vec<T>          u64 BE element count, then the elements back to back
//   [T; N]          N elements, NO length prefix
//   enum variant    u32 BE variant index (RecordType: Read=0 Verify=1
//                   Write=2 Dynamic=3 Expose=4)
//
// reject_trailing_bytes means an OUTBOUND payload must match the server struct
// byte-for-byte. One extra byte is an error, not slack.
//
// ============================== COMMANDS ==============================
//
//  Code  Name              Payload (-> reply)
//  0x01  WRITE             u16 rid                     -> (nothing)
//  0x02  VERIFY            u16 rid                     -> (nothing)
//  0x03  READ              u16 rid                     -> (nothing)
//  0x04  CONFIG            ConfigCmd, 43 bytes         -> 1-byte ACK
//  0x05  DYNAMIC           DynamicCmd, 7 bytes         -> (nothing)
//  0x06  INFO              (empty)                     -> InfoRsp, 12 bools
//  0x07  GENRUN            (empty)                     -> u16 run id
//  0x08  GENRECORD         (empty)                     -> u16 record id
//  0x09  SAVERUN           Run                         -> bool success
//  0x0A  ABORT             (empty)                     -> (nothing)
//  0x0B  GETRECORD         u16 rid                     -> Option<Record>
//  0x0C  GETRUN            u16 id                      -> Option<Run>
//  0x0D  GETCAPTURE        u16 rid                     -> RAW BYTES (see below)
//  0x0E  ENGINESTATUS      (empty)                     -> EngineStatusRsp
//  0x0F  GETPOWERMODE      (empty)                     -> 3 bools
//  0x10  LISTRECORDS       (empty)                     -> ListRecordsRsp
//  0x11  LISTRUNS          (empty)                     -> ListRunsRsp
//  0x12  GETFPGAIMAGE      (empty)                     -> Option<String>
//  0x13  SETFPGAIMAGE      String image_name           -> bool success
//  0x14  DELRUN            u16 id                      -> bool success
//  0x15  DELRECORD         u16 rid                     -> bool success
//  0x16  GETFPGAIMAGES     (empty)                     -> Vec<String> images
//  0x17  REQUESTPOWERSTATE u8 state (0/1/2)            -> bool success
//  0x18  CLAMPCMD          bool state                  -> (nothing)
//  0x19  EXPOSE            u16 rid                     -> (nothing)
//  0x1A  BEAMSERVICES      bool beam_on                -> bool beam_request *
//
//  * 0x1A is declared in config.rs and types.rs but has NO dispatch arm in
//    server.rs, so the server logs "unknown CMD" and never replies. See
//    SetBeamServicesAsync.
//
// CMD_GETCAPTURE is the one command whose reply is NOT bincode. The server
// streams the capture file straight from disk to the socket, so the payload is
// the raw file contents and LEN is the file size. LEN == 0 means "no capture"
// (missing file, /dvr unmounted in safe mode, or an Expose record, which never
// writes any capture bytes). Captures run to hundreds of megabytes, so never
// read one into a byte[] — use DownloadCaptureAsync, which copies in chunks.
//
// ============================== SAMPLING GEOMETRY ==============================
//
//     for blk in (0..num_blocks).step_by(block_factor):
//         for addr in blk*block_size .. (blk+1)*block_size: <test byte>
//
// block_factor is a STRIDE over blocks (coverage ~= 1/block_factor) and the
// logical extent is num_blocks * block_size. Two server-side landmines the
// client guards against in ConfigCmd.Validate():
//   * block_factor == 0 panics the engine thread (step_by(0)).
//   * num_blocks * block_size > 4 GiB overflows the server's u32 address math.
//
// Requires .NET 5 or later (BinaryPrimitives.Read/WriteDoubleBigEndian).

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DDR4_TestingApp
{
    // ============================== Enums ==============================

    /// <summary>Mirrors record.rs RecordType. Wire form is a u32 BE index.</summary>
    public enum RecordType : uint
    {
        Read = 0,
        Verify = 1,
        Write = 2,
        Dynamic = 3,

        /// <summary>
        /// Beam-exposure bookkeeping: no memory traffic at all. The engine waits for
        /// beam-possible, asserts the beam request, tallies how long beam-present was
        /// high into <see cref="Record.ExposureTime"/>, and ends when beam-possible
        /// drops or the configured exposure limit is reached. An Expose record has no
        /// bit counts and an EMPTY capture file.
        ///
        /// Queued with CMD_EXPOSE (0x19) — see
        /// <see cref="TcpManager.QueueExposeAsync"/>.
        /// </summary>
        Expose = 4,
    }

    /// <summary>Values accepted by CMD_REQUESTPOWERSTATE / PowerManager.power_request.</summary>
    public enum PowerRequest : byte
    {
        Active = 0,
        Shutdown = 1,
        Reboot = 2,
    }

    /// <summary>Values accepted by ConfigCmd.DataPattern.</summary>
    public enum DataPattern : byte
    {
        Zeros = 0,
        Ones = 1,          // 0xFF
        PseudoRandom = 2,  // splitmix64(seed, address) — see rand.rs
    }

    // ============================== Wire types ==============================

    /// <summary>
    /// power.rs PowerStats — 25 bytes on the wire (3 x f64 + 1 bool).
    ///
    /// READ WHAT THIS ACTUALLY MEASURES BEFORE USING IT. The ZCU104 has no
    /// current sense dedicated to the SODIMM socket. These numbers come from the
    /// IRPS5401 PMIC's VCC1V2 rail (PHASE_C, PMBus page 2) via Linux hwmon, and
    /// that rail feeds the SODIMM *plus* PL banks 64/65/66 driving it *plus* PS
    /// bank 504 — the PS-side DDR4 interface. So it is board-level rail health,
    /// good for catching a collapsing rail or a gross overdraw, and NOT the
    /// per-device current you want for latchup or SEFI detection. That comes
    /// from the INA293 shunt path on the custom SODIMM cards.
    ///
    /// The server returns a canned plausible reading (1.203 V, 0.234 A) when
    /// SIMULATION_MODE is set, so a simulated run shows something in range
    /// rather than zeroes that read as a dead rail.
    /// </summary>
    public struct PowerStats
    {
        /// <summary>Rail voltage, volts.</summary>
        public double Volts;

        /// <summary>Rail current, amps. Reads 0 if the PMIC exposes no iout page.</summary>
        public double Amps;

        /// <summary>Rail power, watts.</summary>
        public double Watts;

        /// <summary>
        /// True when <see cref="Watts"/> was computed as Volts * Amps rather than
        /// read from the PMIC's own pout register. The register is the better
        /// number — the product multiplies two readings that were not sampled at
        /// the same instant.
        /// </summary>
        public bool PowerDerived;

        public const int WireSize = 25;

        public override string ToString() =>
            $"{Volts:0.000} V, {Amps:0.000} A, {Watts:0.000} W{(PowerDerived ? " (derived)" : "")}";
    }

    /// <summary>
    /// types.rs ConfigCmd — 43 bytes on the wire (was 34; the two exposure fields are
    /// new). Field order here MUST match the Rust declaration order, since that is what
    /// determines the bincode layout.
    /// </summary>
    public struct ConfigCmd
    {
        public bool EnableLogging;            //  0  (1)
        public uint NumBlocks;                //  1  (4)
        public uint BlockSize;                //  5  (4)
        public uint BlockFactor;              //  9  (4)
        public double SefiThreshold;          // 13  (8)  fraction of bad samples, 0..1
        public byte DataPattern;              // 21  (1)
        public ulong PatternSeed;             // 22  (8)
        public bool IsX16;                    // 30  (1)
        public byte EnabledByteLanes;         // 31  (1)
        public bool FpgaAutoConfig;           // 32  (1)
        public bool AutomatedSefiMode;        // 33  (1)
        public bool ExposureTimeLimitEnabled; // 34  (1)  NEW
        public double ExposureTimeLimitMs;    // 35  (8)  NEW — but see the remark

        public const int WireSize = 43;

        /// <summary>
        /// The same limit in seconds, for call sites that think in seconds.
        ///
        /// The units are right on the server now: task_engine.rs tests
        /// <c>exposure_time_limit_ms &lt;= r.exposure_time * 1000.0</c>, and
        /// <c>r.exposure_time</c> comes from <c>Duration::as_secs_f64()</c>, so it is
        /// milliseconds against milliseconds. An earlier build compared against raw
        /// seconds — a 30-second cap was 30.0 there and is 30000.0 here, so a
        /// config.bin written before that fix now caps exposure 1000x too early.
        /// </summary>
        public double ExposureTimeLimitSeconds
        {
            get => ExposureTimeLimitMs / 1000.0;
            set => ExposureTimeLimitMs = value * 1000.0;
        }

        /// <summary>Matches the server's built-in defaults (config.rs CONFIG).</summary>
        public static ConfigCmd Default => new ConfigCmd
        {
            EnableLogging = true,
            NumBlocks = 0x0000_0100,   // 256 blocks
            BlockSize = 0x0010_0000,   // 1 MiB -> 256 MiB extent
            BlockFactor = 1,
            SefiThreshold = 0.0,
            DataPattern = 0,            // DataPattern.Zeros
            PatternSeed = 0,
            IsX16 = false,
            EnabledByteLanes = 0xFF,
            FpgaAutoConfig = false,
            AutomatedSefiMode = false,
            ExposureTimeLimitEnabled = false,
            ExposureTimeLimitMs = 0.0,
        };

        /// <summary>
        /// Rejects configurations that are known to misbehave on the server.
        /// Called automatically by <see cref="TcpManager.ApplyConfigAsync"/>.
        /// </summary>
        public void Validate()
        {
            if (BlockFactor == 0)
                throw new ArgumentException(
                    "BlockFactor must be >= 1: the server calls step_by(BlockFactor) " +
                    "and step_by(0) panics the engine thread.");

            if (BlockSize == 0 || NumBlocks == 0)
                throw new ArgumentException("BlockSize and NumBlocks must both be > 0.");

            ulong extent = (ulong)NumBlocks * BlockSize;
            if (extent > 0x1_0000_0000UL)
                throw new ArgumentException(
                    $"NumBlocks * BlockSize = 0x{extent:X} exceeds the 4 GiB u32 address " +
                    "space the server computes block addresses in.");

            if (AutomatedSefiMode && SefiThreshold <= 0.0)
                throw new ArgumentException(
                    "SefiThreshold must be > 0 for dynamic/automated runs. The server " +
                    "tests `ratio >= sefi_threshold`, so a threshold of 0 reports a SEFI " +
                    "on the first sample of every run.");

            if (DataPattern > 2)
                throw new ArgumentException(
                    $"DataPattern {DataPattern} is invalid (0 = zeros, 1 = ones, " +
                    "2 = pseudorandom). The server's Write path aborts the engine " +
                    "thread on an unknown pattern.");

            if (ExposureTimeLimitEnabled && !(ExposureTimeLimitMs > 0.0))
                throw new ArgumentException(
                    "ExposureTimeLimitMs must be > 0 when ExposureTimeLimitEnabled is " +
                    "set. The engine's Expose arm ends the record as soon as " +
                    "exposure_time_limit_ms <= exposure_time * 1000, so a limit of 0 " +
                    "closes the record on its first progress tick. The value is in " +
                    "MILLISECONDS — use ExposureTimeLimitSeconds if you think in " +
                    "seconds.");

            if (double.IsNaN(ExposureTimeLimitMs) || double.IsInfinity(ExposureTimeLimitMs))
                throw new ArgumentException("ExposureTimeLimitMs must be finite.");
        }
    }

    /// <summary>record.rs Record. Reference type — these can be several MB.</summary>
    public sealed class Record
    {
        public ulong TimeStarted;
        public ulong TimeEnded;
        public ulong TimeDuration;
        public ushort Rid;
        public RecordType Rtype;
        public bool Completed;
        public bool Aborted;

        public ulong CorrectBits;
        public ulong IncorrectBits;
        public ulong ProcessedBits;   // only populated for Write records

        /// <summary>
        /// Runs of k adjacent flipped bits; 8 entries. The engine now initializes this
        /// to Some([0; 8]) for EVERY record type, so null means "an older record from
        /// before that change", not "not applicable" — an all-zero array on a Read or
        /// Expose record means the field was simply never touched.
        /// </summary>
        public ulong[]? AdjErrBins;

        /// <summary>Bytes with exactly k flipped bits; 9 entries. Same caveat as AdjErrBins.</summary>
        public ulong[]? ErrBins;

        /// <summary>Seconds from beam capture to SEFI, Dynamic records only.</summary>
        public double? TimeToSefi;

        /// <summary>
        /// Accumulated beam-present time in SECONDS. Only the Expose arm maintains this.
        /// </summary>
        public double? ExposureTime;

        // ---- Time series -------------------------------------------------------
        //
        // The server pushes a DIFFERENT SUBSET of these per record type and never pads,
        // so within one record they legitimately have different lengths. Zip them
        // defensively — never index one by another's Length.
        //
        // Older records on the card carry a different shape. Before this round there
        // were THREE beam series (present / possible / request) and no PMIC series;
        // before that, one. A record reloaded from /dvr/records that predates a change
        // will not deserialize against the current struct at all, and archive::init
        // silently skips any record whose JSON no longer matches.

        public double[] LogTimestamp = Array.Empty<double>();
        public ulong[] LogBitsProcessed = Array.Empty<ulong>();
        public ulong[] LogBitsIncorrect = Array.Empty<ulong>();
        public ulong[] LogBitsCorrect = Array.Empty<ulong>();
        public double[] LogErrorRate = Array.Empty<double>();   // never written by the server yet
        public uint[] LogCurrentAddress = Array.Empty<uint>();

        /// <summary>
        /// Beam on target at each sample. ALL FALSE as the server stands — this reads
        /// beam.rs's BEAM_ON, which has no writer anywhere. See the header.
        /// </summary>
        public bool[] LogBeamActive = Array.Empty<bool>();

        /// <summary>
        /// What the server was ASKING for at each sample — the REQUEST_BEAM global.
        /// Reading this against <see cref="LogBeamActive"/> is how you tell "we asked
        /// and got it" from "we asked and waited", and also how the dead beam path
        /// shows up in a record: request goes high, active never does.
        /// </summary>
        public bool[] LogBeamRequest = Array.Empty<bool>();

        public bool[] LogSefiDetected = Array.Empty<bool>();

        /// <summary>
        /// Per-sample VCC1V2 rail telemetry, one entry per progress tick. An entry is
        /// null when that sample's read failed or the PMIC is not exposed through
        /// hwmon; every arm pushes this series unconditionally, so nulls are gaps
        /// rather than a shorter array. See <see cref="PowerStats"/> for what the
        /// numbers actually cover.
        /// </summary>
        public PowerStats?[] LogPmic = Array.Empty<PowerStats?>();

        public ulong[][] LogAdjErrBins = Array.Empty<ulong[]>();
        public ulong[][] LogErrBins = Array.Empty<ulong[]>();

        /// <summary>
        /// Compatibility alias for the single beam series this carried before it was
        /// split into three and then collapsed back into two. That series is now
        /// beam-active.
        /// </summary>
        public bool[] LogBeamSignal => LogBeamActive;

        /// <summary>
        /// Shortest of the series this record actually populated, i.e. how many rows can
        /// be zipped safely. Empty series are ignored so a Read record (no bit counts)
        /// does not report zero.
        /// </summary>
        public int SampleCount
        {
            get
            {
                int[] lengths =
                {
                    LogTimestamp.Length,
                    LogBitsProcessed.Length,
                    LogBitsIncorrect.Length,
                    LogBitsCorrect.Length,
                    LogCurrentAddress.Length,
                    LogBeamActive.Length,
                    LogBeamRequest.Length,
                    LogSefiDetected.Length,
                    LogPmic.Length,
                };

                int min = int.MaxValue;
                foreach (int n in lengths)
                    if (n > 0 && n < min) min = n;

                return min == int.MaxValue ? 0 : min;
            }
        }
    }

    /// <summary>
    /// types.rs BeamServicesCmd / BeamServicesRsp. Declared on both sides but
    /// UNREACHABLE — server.rs has no dispatch arm for CMD_BEAMSERVICES. Recorded here
    /// so the shape is on hand for whoever adds that arm.
    /// </summary>
    public struct BeamServices
    {
        /// <summary>Outbound: beam is on target right now (BeamServicesCmd.beam_on).</summary>
        public bool BeamOn;

        /// <summary>Inbound: what the server wants (BeamServicesRsp.beam_request).</summary>
        public bool BeamRequest;
    }

    /// <summary>run.rs Run. Sent by SAVERUN, returned by GETRUN.</summary>
    public sealed class Run
    {
        public ulong TimeStarted;
        public ulong TimeModified;
        public ushort Id;

        public double Fluence;
        public double Temperature;
        public string SodimmId = "";
        public double Energy;
        public string Ion = "";
        public double LET;
        public double Angle;
        public double Flux;
        public double AirGap;
        public string Description = "";

        public List<ushort> PreliminaryRecords = new();
        public List<ushort> PrimaryRecords = new();
        public List<ushort> PostRecords = new();

        /// <summary>Every record id this run cites, across all three stages.</summary>
        public IEnumerable<ushort> AllRecords =>
            PreliminaryRecords.Concat(PrimaryRecords).Concat(PostRecords);
    }

    /// <summary>task_engine.rs TaskRequest — one queued, not-yet-started task.</summary>
    public struct TaskRequest
    {
        public RecordType Rtype;
        public ushort Rid;
    }

    /// <summary>
    /// types.rs EngineStatusRsp.
    ///
    /// BREAKING CHANGE: this no longer carries the in-flight Record. The server sends
    /// Option&lt;RecordType&gt; where it used to send Option&lt;Record&gt;, so a status
    /// poll now tells you WHAT KIND of task is running and how far along it is, but not
    /// which rid, and none of the counters.
    ///
    /// Upside: a poll is ~20 bytes instead of megabytes, so the old "poll at 1 Hz, not
    /// 10 Hz" warning no longer applies. Downside: to know the rid of the running task
    /// you have to remember what you queued — the queue only lists work that has NOT
    /// started, since the engine removes the entry before it begins.
    /// </summary>
    public sealed class EngineStatus
    {
        /// <summary>Kind of task in flight, or null when nothing is running.</summary>
        public RecordType? CurrentRequestType;

        /// <summary>
        /// Progress, 0..100 for Read / Verify / Write / Dynamic (block index over
        /// num_blocks, so it sawtooths on Dynamic — one ramp per sweep).
        ///
        /// Expose now reports a percentage too — accumulated beam time against
        /// exposure_time_limit_ms, correctly scaled and capped at 100. It used to
        /// divide seconds by milliseconds without scaling, so it read 1000x low and as
        /// a fraction; that is fixed, and every arm is on the same scale.
        ///
        /// One placeholder remains: an Expose task with NO exposure limit configured
        /// reports a CONSTANT 50.0, because there is no denominator to measure against.
        /// That is not a half-finished task. <see cref="IsProgressKnown"/> recognises
        /// the case when this client is the one that applied the config.
        /// </summary>
        public double RecordProgress;

        public bool IsIdle;

        /// <summary>Tasks that have NOT started yet. The running task is not in here.</summary>
        public TaskRequest[] Queue = Array.Empty<TaskRequest>();

        /// <summary>
        /// Progress normalized to 0..1, or null when the engine is idle. Uniform across
        /// record types now that Expose reports a percentage like the rest.
        /// </summary>
        public double? NormalizedProgress
        {
            get
            {
                if (!CurrentRequestType.HasValue) return null;

                double raw = RecordProgress / 100.0;
                if (double.IsNaN(raw)) return 0.0;
                return Math.Max(0.0, Math.Min(1.0, raw));
            }
        }

        /// <summary>
        /// True when a task is running AND its progress number tracks real work.
        ///
        /// False while idle. Also false during an Expose task with no exposure limit,
        /// where the server emits a constant 50.0 — a bar sitting at exactly half for
        /// twenty minutes reads as a hung task rather than as the placeholder it is, so
        /// an indeterminate bar is the honest rendering.
        ///
        /// Whether the limit is set is read from <see cref="TcpManager.CommittedConfig"/>,
        /// which is only populated once this client has applied a config. On a fresh
        /// connect to an already-configured server this errs toward "known" rather than
        /// hiding a number that may well be real.
        /// </summary>
        public bool IsProgressKnown
        {
            get
            {
                if (!CurrentRequestType.HasValue) return false;
                if (CurrentRequestType != RecordType.Expose) return true;
                return TcpManager.CommittedConfig?.ExposureTimeLimitEnabled ?? true;
            }
        }

        /// <summary>
        /// True when the engine claims to be idle but still has work queued. The
        /// engine refuses to start a task while /dvr is unmounted (safe mode) or
        /// while the MIG calibration line reads low, and it does so silently —
        /// check <see cref="TcpManager.GetInfoAsync"/> for which.
        /// </summary>
        public bool IsStalled => IsIdle && Queue.Length > 0;
    }

    /// <summary>
    /// types.rs InfoRsp — RESTRUCTURED this round, see the header.
    ///
    /// FIELD ORDER IS THE WIRE ORDER. bincode serializes in struct DECLARATION
    /// order, and commands.rs builds the struct with its members written in a
    /// different order than types.rs declares them — harmless, since serde
    /// ignores initializer order, but don't use the command handler as the
    /// reference when checking this against the Rust.
    ///
    /// Size is 13 bytes with no PMIC reading, 38 with one.
    /// </summary>
    public struct InfoRsp
    {
        /// <summary>
        /// Beam is on target. ALWAYS FALSE as the server stands: beam.rs's
        /// BEAM_ON has no writer anywhere in the tree. See the header.
        /// </summary>
        public bool BeamActive;

        /// <summary>
        /// The server's REQUEST_BEAM flag — what the engine is asking for. The
        /// Expose and Dynamic arms raise and drop it, and CMD_CLAMPCMD writes it
        /// directly. This one does work.
        /// </summary>
        public bool BeamRequest;

        /// <summary>
        /// Whether something has reported beam state within the last 200 ms —
        /// the beam-side equivalent of <see cref="PowerServicesProvided"/>.
        /// ALWAYS FALSE as the server stands: BEAM_SERVICES has no writer, which
        /// is the same missing CMD_BEAMSERVICES arm. See the header.
        /// </summary>
        public bool BeamServices;

        /// <summary>
        /// VCC1V2 rail telemetry, or null when the PMIC is not exposed through
        /// hwmon, the expected PMBus page is absent, or a sysfs read failed.
        /// Read <see cref="PowerStats"/> before drawing conclusions from it.
        /// </summary>
        public PowerStats? Pmic;

        /// <summary>
        /// The server has heard a GETPOWERMODE request within the last 250 ms.
        /// A HEARTBEAT, not a hardware line: it means "something is supervising
        /// my power right now".
        ///
        /// The 250 ms window is the thing to design around. Whatever drives the
        /// contactor has to poll <see cref="TcpManager.GetPowerModeAsync"/>
        /// FASTER than that or this reads false even though the controller is
        /// alive and connected — a 500 ms cadence flickers and a 1 s cadence
        /// never shows true. The Launcher polls at 150 ms for this reason.
        /// </summary>
        public bool PowerServicesProvided;

        /// <summary>DDR4 controller has finished calibrating (EMIO 80). The engine
        /// refuses to start any task while this reads low.</summary>
        public bool ControllerCalibrated;

        /// <summary>UI clock alive (EMIO 81).</summary>
        public bool UiClock;

        /// <summary>PL clock alive (EMIO 82).</summary>
        public bool PlClock;

        /// <summary>A bitstream is loaded in the PL (EMIO 83).</summary>
        public bool FpgaLoaded;

        /// <summary>SDRAM select lines (EMIO 84-87).</summary>
        public bool Sdram0;
        public bool Sdram1;
        public bool Sdram2;
        public bool Sdram3;

        /// <summary>Alias for the pre-rename beam_present field.</summary>
        public bool BeamPresent => BeamActive;

        /// <summary>The four select lines as an array, index 0-3.</summary>
        public bool[] SdramSelects => new[] { Sdram0, Sdram1, Sdram2, Sdram3 };

        /// <summary>
        /// Index of the single asserted SDRAM select, or null when none or more
        /// than one is asserted — both of which are states worth showing
        /// differently from "chip 0 selected".
        /// </summary>
        public int? SelectedSdram
        {
            get
            {
                bool[] s = SdramSelects;
                int found = -1;
                for (int i = 0; i < s.Length; i++)
                {
                    if (!s[i]) continue;
                    if (found >= 0) return null;   // more than one asserted
                    found = i;
                }
                return found >= 0 ? found : (int?)null;
            }
        }
    }

    /// <summary>
    /// types.rs GetPowerModeRsp. Gained a third field this round.
    ///
    /// The two power flags stay false unless the server has entered safe mode
    /// (/dvr unmounted) AND a power change was requested. RequestBeam is NOT gated on
    /// safe mode — it is reported every poll.
    /// </summary>
    public struct PowerMode
    {
        public bool RequestPoweroff;
        public bool RequestReboot;

        /// <summary>
        /// NEW. The server's REQUEST_BEAM global: true while the engine wants beam on
        /// target. The same flag drives the clamp output and is logged per sample in
        /// <see cref="Record.LogBeamRequest"/>.
        /// </summary>
        public bool RequestBeam;

        /// <summary>True if the server wants the operator to cycle power either way.</summary>
        public bool WantsPowerChange => RequestPoweroff || RequestReboot;
    }

    /// <summary>One row of the LISTRECORDS column arrays.</summary>
    public sealed class RecordSummary
    {
        public ushort Rid;
        public ulong TimeStarted;
        public ulong TimeDuration;
        public bool Completed;
        public bool Aborted;
        public RecordType Rtype;
    }

    /// <summary>One row of the LISTRUNS column arrays.</summary>
    public sealed class RunSummary
    {
        public ushort Id;
        public ulong TimeModified;
        public string SodimmId = "";
        public double Energy;
        public string Ion = "";
        public double LET;
        public string Description = "";

        /// <summary>Server-side placeholder — currently always 0.</summary>
        public ushort NumRecords;
        /// <summary>Server-side placeholder — currently always false.</summary>
        public bool IsComplete;
    }

    // ============================== bincode codec ==============================

    /// <summary>
    /// Sequential reader for bincode(big-endian, fixint) payloads. Every Read*
    /// call advances the cursor, which is what variable-length structures like
    /// Record and Run require — fixed offsets cannot express them.
    /// </summary>
    internal sealed class BincodeReader
    {
        private readonly byte[] buf;
        private int pos;

        public BincodeReader(byte[] payload) { buf = payload; pos = 0; }

        public int Remaining => buf.Length - pos;

        private ReadOnlySpan<byte> Take(int n)
        {
            if (n < 0 || n > Remaining)
                throw new InvalidDataException(
                    $"payload truncated: need {n} more bytes at offset {pos}, have {Remaining}");
            var s = buf.AsSpan(pos, n);
            pos += n;
            return s;
        }

        public bool ReadBool()
        {
            byte b = Take(1)[0];
            if (b > 1)
                throw new InvalidDataException($"invalid bool byte 0x{b:X2} at offset {pos - 1}");
            return b != 0;
        }

        public byte ReadU8() => Take(1)[0];
        public ushort ReadU16() => BinaryPrimitives.ReadUInt16BigEndian(Take(2));
        public uint ReadU32() => BinaryPrimitives.ReadUInt32BigEndian(Take(4));
        public ulong ReadU64() => BinaryPrimitives.ReadUInt64BigEndian(Take(8));
        public double ReadF64() => BinaryPrimitives.ReadDoubleBigEndian(Take(8));

        /// <summary>Reads a Vec/String u64 length and sanity-checks it against what is left.</summary>
        private int ReadLen(int elementSize)
        {
            ulong n = ReadU64();
            if (elementSize > 0 && n > (ulong)(Remaining / elementSize))
                throw new InvalidDataException(
                    $"declared length {n} exceeds the {Remaining} bytes remaining at offset {pos}");
            if (n > int.MaxValue)
                throw new InvalidDataException($"declared length {n} is too large for a CLR array");
            return (int)n;
        }

        /// <summary>Accepts 0..4. Expose is variant 4.</summary>
        public RecordType ReadRecordType()
        {
            uint v = ReadU32();
            if (v > 4)
                throw new InvalidDataException(
                    $"unknown RecordType variant {v} — record.rs has gained a variant this " +
                    "client does not know about");
            return (RecordType)v;
        }

        public string ReadString()
        {
            int n = ReadLen(1);
            return Encoding.UTF8.GetString(Take(n));
        }

        public bool ReadSomeTag()
        {
            byte tag = Take(1)[0];
            return tag switch
            {
                0 => false,
                1 => true,
                _ => throw new InvalidDataException(
                        $"invalid Option tag 0x{tag:X2} at offset {pos - 1}"),
            };
        }

        public double? ReadOptionF64() => ReadSomeTag() ? ReadF64() : null;
        public ushort? ReadOptionU16() => ReadSomeTag() ? ReadU16() : null;
        public string? ReadOptionString() => ReadSomeTag() ? ReadString() : null;
        public RecordType? ReadOptionRecordType() => ReadSomeTag() ? ReadRecordType() : null;

        /// <summary>power.rs PowerStats: three f64 then a bool, 25 bytes, no padding.</summary>
        public PowerStats ReadPowerStats() => new PowerStats
        {
            Volts = ReadF64(),
            Amps = ReadF64(),
            Watts = ReadF64(),
            PowerDerived = ReadBool(),
        };

        public PowerStats? ReadOptionPowerStats() =>
            ReadSomeTag() ? ReadPowerStats() : (PowerStats?)null;

        /// <summary>
        /// Vec&lt;Option&lt;PowerStats&gt;&gt;. Element size varies — 1 byte for None, 26
        /// for Some — so the length guard uses the 1-byte minimum and the per-element
        /// reads do the real bounds checking.
        /// </summary>
        public PowerStats?[] ReadVecOptionPowerStats()
        {
            int n = ReadLen(1);
            var a = new PowerStats?[n];
            for (int i = 0; i < n; i++) a[i] = ReadOptionPowerStats();
            return a;
        }

        public ulong[] ReadFixedU64Array(int n)
        {
            var a = new ulong[n];
            for (int i = 0; i < n; i++) a[i] = ReadU64();
            return a;
        }

        public ulong[]? ReadOptionFixedU64Array(int n) =>
            ReadSomeTag() ? ReadFixedU64Array(n) : null;

        public double[] ReadVecF64()
        {
            int n = ReadLen(8);
            var a = new double[n];
            for (int i = 0; i < n; i++) a[i] = ReadF64();
            return a;
        }

        public ulong[] ReadVecU64()
        {
            int n = ReadLen(8);
            var a = new ulong[n];
            for (int i = 0; i < n; i++) a[i] = ReadU64();
            return a;
        }

        public uint[] ReadVecU32()
        {
            int n = ReadLen(4);
            var a = new uint[n];
            for (int i = 0; i < n; i++) a[i] = ReadU32();
            return a;
        }

        public ushort[] ReadVecU16()
        {
            int n = ReadLen(2);
            var a = new ushort[n];
            for (int i = 0; i < n; i++) a[i] = ReadU16();
            return a;
        }

        public bool[] ReadVecBool()
        {
            int n = ReadLen(1);
            var a = new bool[n];
            for (int i = 0; i < n; i++) a[i] = ReadBool();
            return a;
        }

        public string[] ReadVecString()
        {
            int n = ReadLen(8);  // each entry is at least a u64 length
            var a = new string[n];
            for (int i = 0; i < n; i++) a[i] = ReadString();
            return a;
        }

        public RecordType[] ReadVecRecordType()
        {
            int n = ReadLen(4);
            var a = new RecordType[n];
            for (int i = 0; i < n; i++) a[i] = ReadRecordType();
            return a;
        }

        public TaskRequest[] ReadVecTaskRequest()
        {
            int n = ReadLen(6);   // u32 rtype + u16 rid
            var a = new TaskRequest[n];
            for (int i = 0; i < n; i++)
                a[i] = new TaskRequest { Rtype = ReadRecordType(), Rid = ReadU16() };
            return a;
        }

        public ulong[][] ReadVecFixedU64Array(int width)
        {
            int n = ReadLen(width * 8);
            var a = new ulong[n][];
            for (int i = 0; i < n; i++) a[i] = ReadFixedU64Array(width);
            return a;
        }

        /// <summary>
        /// The server uses reject_trailing_bytes, so a well-formed reply should land
        /// exactly on the end. Leftovers mean the C# layout has drifted from the Rust one.
        /// </summary>
        public void ExpectEnd(string what)
        {
            if (Remaining != 0)
                throw new InvalidDataException(
                    $"{what}: {Remaining} trailing bytes — the C# struct layout no longer " +
                    "matches types.rs");
        }
    }

    /// <summary>Sequential writer for bincode(big-endian, fixint) payloads.</summary>
    internal sealed class BincodeWriter
    {
        private readonly MemoryStream ms = new MemoryStream(64);

        public BincodeWriter Bool(bool v) { ms.WriteByte(v ? (byte)1 : (byte)0); return this; }
        public BincodeWriter U8(byte v) { ms.WriteByte(v); return this; }

        public BincodeWriter U16(ushort v)
        {
            Span<byte> t = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(t, v);
            ms.Write(t);
            return this;
        }

        public BincodeWriter U32(uint v)
        {
            Span<byte> t = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(t, v);
            ms.Write(t);
            return this;
        }

        public BincodeWriter U64(ulong v)
        {
            Span<byte> t = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(t, v);
            ms.Write(t);
            return this;
        }

        public BincodeWriter F64(double v)
        {
            Span<byte> t = stackalloc byte[8];
            BinaryPrimitives.WriteDoubleBigEndian(t, v);
            ms.Write(t);
            return this;
        }

        public BincodeWriter Str(string v)
        {
            var bytes = Encoding.UTF8.GetBytes(v ?? "");
            U64((ulong)bytes.Length);
            ms.Write(bytes, 0, bytes.Length);
            return this;
        }

        public BincodeWriter VecU16(IReadOnlyList<ushort>? v)
        {
            v ??= Array.Empty<ushort>();
            U64((ulong)v.Count);
            foreach (ushort x in v) U16(x);
            return this;
        }

        public BincodeWriter OptU16(ushort? v)
        {
            if (v.HasValue) { Bool(true); U16(v.Value); } else Bool(false);
            return this;
        }

        public BincodeWriter OptVecU16(IReadOnlyList<ushort>? v)
        {
            if (v is null) Bool(false);
            else { Bool(true); VecU16(v); }
            return this;
        }

        public byte[] ToArray() => ms.ToArray();
    }

    // ============================== TcpManager ==============================

    internal static class TcpManager
    {
        // ---- Protocol constants (must match config.rs) ----
        private const uint SYNC_MARKER = 0xDEAD_BEEF;
        private const uint TERM_MARKER = 0xCAFE_BABE;

        private const byte CMD_WRITE = 0x01;
        private const byte CMD_VERIFY = 0x02;
        private const byte CMD_READ = 0x03;
        private const byte CMD_CONFIG = 0x04;
        private const byte CMD_DYNAMIC = 0x05;
        private const byte CMD_INFO = 0x06;
        private const byte CMD_GENRUN = 0x07;
        private const byte CMD_GENRECORD = 0x08;
        private const byte CMD_SAVERUN = 0x09;
        private const byte CMD_ABORT = 0x0A;
        private const byte CMD_GETRECORD = 0x0B;
        private const byte CMD_GETRUN = 0x0C;
        private const byte CMD_GETCAPTURE = 0x0D;
        private const byte CMD_ENGINESTATUS = 0x0E;
        private const byte CMD_GETPOWERMODE = 0x0F;
        private const byte CMD_LISTRECORDS = 0x10;
        private const byte CMD_LISTRUNS = 0x11;
        private const byte CMD_GETFPGAIMAGE = 0x12;
        private const byte CMD_SETFPGAIMAGE = 0x13;
        private const byte CMD_DELRUN = 0x14;
        private const byte CMD_DELRECORD = 0x15;
        private const byte CMD_GETFPGAIMAGES = 0x16;
        private const byte CMD_REQUESTPOWERSTATE = 0x17;
        private const byte CMD_CLAMPCMD = 0x18;
        private const byte CMD_EXPOSE = 0x19;
        private const byte CMD_BEAMSERVICES = 0x1A;          // NEW, not dispatched

        /// <summary>Frame header: SYNC(4) + CMD(1) + LEN(4).</summary>
        private const int HeaderSize = 9;

        /// <summary>
        /// Mirrors the server's MAX_PAYLOAD (server.rs), which is enforced: the server
        /// logs the length and DROPS THE CONNECTION on anything larger, with no reply.
        /// Fail locally instead so the link survives.
        /// </summary>
        public const int MaxOutboundPayload = 1 << 20;   // 1 MiB

        /// <summary>
        /// Largest reply buffered in memory. A desynced frame can claim up to 4 GiB,
        /// so this is the allocation guard. Captures are exempt: they stream.
        /// </summary>
        public static int MaxInlinePayload { get; set; } = 32 << 20;   // 32 MiB

        /// <summary>Per-command timeout for everything except capture downloads.</summary>
        public static TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>
        /// Timeout for a delete. Longer than a normal command because the server unlinks
        /// every child capture under its record lock, and freeing a few hundred MB of
        /// extents per file is not instant on the eMMC.
        /// </summary>
        public static TimeSpan DeleteTimeout { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Timeout for a capture download. Reading a few hundred MB off the eMMC and
        /// pushing it over the link takes tens of seconds; too short a value abandons
        /// the read mid-frame, which costs the connection.
        /// </summary>
        public static TimeSpan CaptureTimeout { get; set; } = TimeSpan.FromMinutes(10);

        // ---- Connection state ----
        private static TcpClient? client;
        private static NetworkStream? stream;

        /// <summary>
        /// Geometry from the last successful <see cref="ApplyConfigAsync"/>. The engine
        /// snapshots CONFIG when a task STARTS, so a config edited after queueing does
        /// not apply to already-queued work. Reset on disconnect.
        /// </summary>
        private static ConfigCmd? committedConfig;

        public static ConfigCmd? CommittedConfig => committedConfig;

        // One command in flight at a time. The protocol has no request ids, so
        // replies are matched purely by order — two concurrent callers would
        // read each other's frames.
        private static readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);

        public static string Host { get; private set; } = "";
        public static int Port { get; private set; } = 8080;

        public enum ConnectionStatus { Disconnected, Connected }

        public static ConnectionStatus Status =>
            (client?.Connected ?? false) ? ConnectionStatus.Connected
                                         : ConnectionStatus.Disconnected;

        /// <summary>Raised when <see cref="Status"/> changes.</summary>
        public static event Action<ConnectionStatus>? StatusChanged;

        // ============================== Connect / Disconnect ==============================

        public static async Task ConnectAsync(string host, int port = 8080,
                                              CancellationToken ct = default)
        {
            Disconnect();

            var c = new TcpClient();
            // Responses are built as one buffer server-side but the capture path
            // writes header/body/term separately; don't let Nagle hold the header.
            c.NoDelay = true;
            await c.ConnectAsync(host, port, ct).ConfigureAwait(false);

            client = c;
            stream = c.GetStream();
            Host = host;
            Port = port;

            StatusChanged?.Invoke(ConnectionStatus.Connected);
        }

        public static void Disconnect()
        {
            bool was = Status == ConnectionStatus.Connected;
            try { stream?.Close(); } catch { /* ignore */ }
            try { client?.Close(); } catch { /* ignore */ }
            stream = null;
            client = null;
            committedConfig = null;

            if (was) StatusChanged?.Invoke(ConnectionStatus.Disconnected);
        }

        /// <summary>
        /// Any framing error leaves the byte stream at an unknown position, and the
        /// marker-based protocol cannot resynchronize without guessing. Drop the
        /// connection and make the caller reconnect.
        /// </summary>
        private static Exception Fault(string message)
        {
            Disconnect();
            return new InvalidDataException(
                message + " — connection dropped; reconnect before retrying.");
        }

        // ============================== Config ==============================

        /// <summary>
        /// Validate and apply a configuration. The server persists it to config.bin
        /// and reseeds its PRNG, then ACKs with a single byte.
        /// </summary>
        public static Task ApplyConfigAsync(ConfigCmd cfg, CancellationToken ct = default)
        {
            cfg.Validate();

            return RunAsync(async t =>
            {
                var payload = EncodeConfig(cfg);
                await RequestAsync(CMD_CONFIG, payload, CMD_CONFIG, t).ConfigureAwait(false);
                committedConfig = cfg;
                return true;
            }, DefaultTimeout, ct);
        }

        // ============================== Id allocation ==============================

        /// <summary>Allocate a record id (CMD_GENRECORD). Random, checked against the archive.</summary>
        public static Task<ushort> NewRecordIdAsync(CancellationToken ct = default) =>
            RunAsync(async t =>
            {
                var p = await RequestAsync(CMD_GENRECORD, Array.Empty<byte>(), CMD_GENRECORD, t)
                            .ConfigureAwait(false);
                var r = new BincodeReader(p);
                ushort id = r.ReadU16();
                r.ExpectEnd("genRecordRsp");
                return id;
            }, DefaultTimeout, ct);

        /// <summary>
        /// Allocate a run id (CMD_GENRUN) — the lowest unused id. Note the server only
        /// adds a run to its archive on SAVERUN, so two GENRUN calls without an
        /// intervening save return the SAME id.
        /// </summary>
        public static Task<ushort> NewRunIdAsync(CancellationToken ct = default) =>
            RunAsync(async t =>
            {
                var p = await RequestAsync(CMD_GENRUN, Array.Empty<byte>(), CMD_GENRUN, t)
                            .ConfigureAwait(false);
                var r = new BincodeReader(p);
                ushort id = r.ReadU16();
                r.ExpectEnd("genRunRsp");
                return id;
            }, DefaultTimeout, ct);

        // ============================== Task queueing ==============================

        //  These send a frame and return. The server replies with NOTHING — it only
        //  pushes a TaskRequest onto the engine queue. Awaiting a reply here would
        //  hang until the next unrelated response arrived.

        public static Task QueueWriteAsync(ushort rid, CancellationToken ct = default) =>
            SendOnlyAsync(CMD_WRITE, EncodeU16(rid), ct);

        public static Task QueueVerifyAsync(ushort rid, CancellationToken ct = default) =>
            SendOnlyAsync(CMD_VERIFY, EncodeU16(rid), ct);

        public static Task QueueReadAsync(ushort rid, CancellationToken ct = default) =>
            SendOnlyAsync(CMD_READ, EncodeU16(rid), ct);

        /// <summary>
        /// Queue a dynamic task. Unlike the other three, DynamicCmd carries more than a
        /// rid — 7 bytes rather than 2 — so a rid alone is not a valid payload.
        /// </summary>
        /// <param name="autoRebootOnSefi">
        /// Carried on the wire but NOT read by the engine: the reboot-after-SEFI
        /// decision is taken from CONFIG.automated_sefi_mode, not from this flag. Set
        /// AutomatedSefiMode in the config if you want that behaviour.
        /// </param>
        /// <param name="numberOfTrials">Carried on the wire; the engine ignores it too.</param>
        /// <param name="parentRun">Run id this record belongs to, 0 for none.</param>
        public static Task QueueDynamicAsync(
            ushort rid,
            bool autoRebootOnSefi = false,
            ushort numberOfTrials = 1,
            ushort parentRun = 0,
            CancellationToken ct = default)
        {
            var payload = new BincodeWriter()
                .U16(rid)
                .Bool(autoRebootOnSefi)
                .U16(numberOfTrials)
                .U16(parentRun)
                .ToArray();

            return SendOnlyAsync(CMD_DYNAMIC, payload, ct);
        }

        /// <summary>
        /// Queue a beam-exposure task (CMD_EXPOSE, 0x19). No reply, like the rest.
        ///
        /// The engine touches no memory for this one. It asserts the beam request,
        /// accumulates beam-on-target time into <see cref="Record.ExposureTime"/>, and
        /// ends 200 ms after either beam-active has been low for 200 ms or the
        /// configured exposure limit is passed.
        ///
        /// AS THE SERVER STANDS THIS RECORD IS ALWAYS EMPTY. is_beam_active() reads
        /// beam.rs's BEAM_ON, which nothing writes, so the arm's beam-high timer is
        /// never refreshed: it crosses the 200 ms hold requirement on the first pass,
        /// the delayed exit arms, and the record completes about 400 ms in with zero
        /// exposure, zero bit counts and an empty capture. It does not hang — it
        /// finishes, having measured nothing. See <see cref="SetBeamServicesAsync"/>
        /// for what is missing and how to wire it up.
        ///
        /// Once beam state is actually fed in: with no exposure limit configured the
        /// task runs until beam-active drops, which is the intended "expose until the
        /// spill ends" mode. Set ConfigCmd.ExposureTimeLimitEnabled for a bounded
        /// exposure, and note the limit counts BEAM-ON time, not wall clock.
        /// </summary>
        public static Task QueueExposeAsync(ushort rid, CancellationToken ct = default) =>
            SendOnlyAsync(CMD_EXPOSE, EncodeU16(rid), ct);

        /// <summary>
        /// Queue any task type. The dynamic parameters are ignored for the other four.
        /// </summary>
        public static Task QueueTaskAsync(
            RecordType type,
            ushort rid,
            bool autoRebootOnSefi = false,
            ushort numberOfTrials = 1,
            ushort parentRun = 0,
            CancellationToken ct = default) =>
            type switch
            {
                RecordType.Write => QueueWriteAsync(rid, ct),
                RecordType.Verify => QueueVerifyAsync(rid, ct),
                RecordType.Read => QueueReadAsync(rid, ct),
                RecordType.Dynamic =>
                    QueueDynamicAsync(rid, autoRebootOnSefi, numberOfTrials, parentRun, ct),
                RecordType.Expose => QueueExposeAsync(rid, ct),
                _ => throw new ArgumentOutOfRangeException(nameof(type)),
            };

        /// <summary>
        /// Ask the engine to abandon the task in flight. Also no reply. The engine only
        /// notices at its next progress tick (~200 ms), and it archives the partial
        /// record with Aborted = true.
        /// </summary>
        public static Task AbortAsync(CancellationToken ct = default) =>
            SendOnlyAsync(CMD_ABORT, Array.Empty<byte>(), ct);

        // ============================== Clamp / beam request ==============================

        /// <summary>
        /// Set the server's beam request (CMD_CLAMPCMD, 0x18). Send-only — no reply.
        ///
        /// NOTE WHAT THIS NO LONGER DOES. It writes beam.rs's REQUEST_BEAM global and
        /// nothing else — set_beam_request() stopped touching the GPIO this round, and
        /// gpio::set_clamp_request has no callers left anywhere in the tree. So no
        /// hardware line moves, and InfoRsp no longer carries a clamp readback to check
        /// against. What it does change is what the server REPORTS it wants:
        /// <see cref="InfoRsp.BeamRequest"/>, <see cref="PowerMode.RequestBeam"/>, and
        /// Record.LogBeamRequest on any task recording at the time. Acting on that is
        /// the supervising host's job — in this bench, the Launcher's beam relay.
        ///
        /// A running Expose or Dynamic task owns this flag and re-evaluates it every
        /// progress tick, so a manual change made mid-task is overwritten within
        /// ~200 ms. Use it between tasks, or for bench checks.
        /// </summary>
        public static Task SetClampAsync(bool assert, CancellationToken ct = default) =>
            SendOnlyAsync(CMD_CLAMPCMD, new BincodeWriter().Bool(assert).ToArray(), ct);

        /// <summary>
        /// Report beam-on-target to the server and read back what it wants
        /// (CMD_BEAMSERVICES, 0x1A).
        ///
        /// THROWS. The command code is in config.rs and both payload structs are in
        /// types.rs, but server.rs has no dispatch arm for 0x1A — it falls through to
        /// the unknown-CMD branch, logs the byte and sends NOTHING. A client that sent
        /// the frame and waited would block until the timeout and then drop the
        /// connection, so this refuses locally instead.
        ///
        /// This is the missing piece that makes the whole beam path dead. BEAM_ON and
        /// BEAM_SERVICES in beam.rs have no other writer, so InfoRsp.BeamActive,
        /// InfoRsp.BeamServices and Record.LogBeamActive are all stuck false and the
        /// Expose task ends ~400 ms in with zero exposure. To wire it up, add to
        /// server.rs:
        ///
        /// <code>
        /// CMD_BEAMSERVICES => match parse_payload::&lt;BeamServicesCmd&gt;(&amp;payload) {
        ///     Ok(c) =&gt; crate::commands::beamservices_command(&amp;mut stream, c),
        ///     Err(e) =&gt; eprintln!("[!] {}: invalid beam services params: {}", peer, e),
        /// },
        /// </code>
        ///
        /// with a handler that stores <c>c.beam_on</c> into BEAM_ON, stamps
        /// BEAM_SERVICES with <c>Instant::now()</c>, and replies with
        /// <c>BeamServicesRsp { beam_request: beam::get_beam_request() }</c>. Then
        /// delete the throw below and uncomment the body — it is already correct for
        /// that shape, and the 200 ms BEAM_SERVICES window means the caller has to poll
        /// this faster than that, the same way GETPOWERMODE feeds
        /// <see cref="InfoRsp.PowerServicesProvided"/>.
        /// </summary>
        public static Task<bool> SetBeamServicesAsync(bool beamOn, CancellationToken ct = default)
        {
            throw new NotSupportedException(
                "CMD_BEAMSERVICES (0x1A) is declared in config.rs and types.rs but " +
                "server.rs has no dispatch arm for it — the server would log an " +
                "unknown command and never reply. Add the arm first; see the remarks " +
                "on this method for the exact code.");

            // return RunAsync(async t =>
            // {
            //     var payload = new BincodeWriter().Bool(beamOn).ToArray();
            //     var p = await RequestAsync(CMD_BEAMSERVICES, payload,
            //                                CMD_BEAMSERVICES, t).ConfigureAwait(false);
            //     var r = new BincodeReader(p);
            //     bool wantsBeam = r.ReadBool();
            //     r.ExpectEnd("BeamServicesRsp");
            //     return wantsBeam;
            // }, DefaultTimeout, ct);
        }

        // ============================== Engine status ==============================

        /// <summary>
        /// Poll the engine. Cheap now — the reply is the task KIND, a progress number,
        /// the idle flag and the pending queue, roughly twenty bytes plus six per queued
        /// task. No record payload any more, so a 10 Hz poll is affordable if you want
        /// one.
        /// </summary>
        public static Task<EngineStatus> GetEngineStatusAsync(CancellationToken ct = default) =>
            RunAsync(async t =>
            {
                var p = await RequestAsync(CMD_ENGINESTATUS, Array.Empty<byte>(),
                                           CMD_ENGINESTATUS, t).ConfigureAwait(false);
                var r = new BincodeReader(p);
                var st = new EngineStatus
                {
                    CurrentRequestType = r.ReadOptionRecordType(),
                    RecordProgress = r.ReadF64(),
                    IsIdle = r.ReadBool(),
                    Queue = r.ReadVecTaskRequest(),
                };
                r.ExpectEnd("EngineStatusRsp");
                return st;
            }, DefaultTimeout, ct);

        /// <summary>
        /// Queue a task and wait for its Record to appear in the archive, reporting
        /// engine status to <paramref name="progress"/> along the way.
        ///
        /// This can wait indefinitely — the engine refuses to start while /dvr is in
        /// safe mode or the MIG calibration line is low, and says nothing about it.
        /// Watch <see cref="EngineStatus.IsStalled"/> and always pass a cancellation
        /// token.
        ///
        /// <see cref="RecordType.Expose"/> is the opposite problem right now: it
        /// returns almost immediately with an empty record, because nothing feeds the
        /// server beam state. See <see cref="QueueExposeAsync"/>.
        /// </summary>
        public static async Task<Record?> RunTaskAsync(
            RecordType type,
            ushort rid,
            IProgress<EngineStatus>? progress = null,
            TimeSpan? pollInterval = null,
            CancellationToken ct = default)
        {
            await QueueTaskAsync(type, rid, false, 1, 0, ct).ConfigureAwait(false);
            return await WaitForRecordAsync(rid, progress, pollInterval, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Wait until record <paramref name="rid"/> has been archived, then return it.
        ///
        /// Completion is detected from LISTRECORDS, not from the engine status. That is
        /// now the only option: the status reply no longer names the running record, so
        /// a task that has been dequeued but not yet archived is indistinguishable from
        /// one that never existed. The archive is the authority — records land there on
        /// completion or abort and nowhere else.
        /// </summary>
        public static async Task<Record?> WaitForRecordAsync(
            ushort rid,
            IProgress<EngineStatus>? progress = null,
            TimeSpan? pollInterval = null,
            CancellationToken ct = default)
        {
            var period = pollInterval ?? TimeSpan.FromSeconds(1);

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var status = await GetEngineStatusAsync(ct).ConfigureAwait(false);
                progress?.Report(status);

                // Still waiting its turn — no point asking the archive.
                bool queued = status.Queue.Any(q => q.Rid == rid);

                if (!queued)
                {
                    var summaries = await ListRecordsAsync(ct).ConfigureAwait(false);
                    if (summaries.Any(s => s.Rid == rid))
                        return await GetRecordAsync(rid, ct).ConfigureAwait(false);
                }

                await Task.Delay(period, ct).ConfigureAwait(false);
            }
        }

        // ============================== Records ==============================

        /// <summary>Fetch one archived record, or null if the server has no such rid.</summary>
        public static Task<Record?> GetRecordAsync(ushort rid, CancellationToken ct = default) =>
            RunAsync(async t =>
            {
                var p = await RequestAsync(CMD_GETRECORD, EncodeU16(rid), CMD_GETRECORD, t)
                            .ConfigureAwait(false);
                var r = new BincodeReader(p);
                Record? rec = r.ReadSomeTag() ? DecodeRecord(r) : null;
                r.ExpectEnd("GetRecordRsp");
                return rec;
            }, DefaultTimeout, ct);

        /// <summary>Summary of every archived record (LISTRECORDS column arrays, zipped).</summary>
        public static Task<RecordSummary[]> ListRecordsAsync(CancellationToken ct = default) =>
            RunAsync(async t =>
            {
                var p = await RequestAsync(CMD_LISTRECORDS, Array.Empty<byte>(),
                                           CMD_LISTRECORDS, t).ConfigureAwait(false);
                var r = new BincodeReader(p);

                var rid = r.ReadVecU16();
                var started = r.ReadVecU64();
                var duration = r.ReadVecU64();
                var completed = r.ReadVecBool();
                var aborted = r.ReadVecBool();
                var rtype = r.ReadVecRecordType();
                r.ExpectEnd("ListRecordsRsp");

                int n = new[] { rid.Length, started.Length, duration.Length,
                                completed.Length, aborted.Length, rtype.Length }.Min();

                var rows = new RecordSummary[n];
                for (int i = 0; i < n; i++)
                    rows[i] = new RecordSummary
                    {
                        Rid = rid[i],
                        TimeStarted = started[i],
                        TimeDuration = duration[i],
                        Completed = completed[i],
                        Aborted = aborted[i],
                        Rtype = rtype[i],
                    };
                return rows;
            }, DefaultTimeout, ct);

        /// <summary>
        /// Delete a record's JSON and its capture file. Returns false if the server has
        /// no such rid, or if a file could not be removed.
        ///
        /// A record that is CURRENTLY RUNNING is not in the archive yet, so this returns
        /// false for it rather than deleting anything — the in-flight capture is safe.
        /// </summary>
        public static Task<bool> DeleteRecordAsync(ushort rid, CancellationToken ct = default) =>
            RunAsync(async t =>
            {
                var p = await RequestAsync(CMD_DELRECORD, EncodeU16(rid), CMD_DELRECORD, t)
                            .ConfigureAwait(false);
                var r = new BincodeReader(p);
                bool ok = r.ReadBool();
                r.ExpectEnd("DeleteRecordRsp");
                return ok;
            }, DeleteTimeout, ct);

        // ============================== Runs ==============================

        /// <summary>Persist a run to /dvr/runs. Returns the server's success flag.</summary>
        public static Task<bool> SaveRunAsync(Run run, CancellationToken ct = default) =>
            RunAsync(async t =>
            {
                var p = await RequestAsync(CMD_SAVERUN, EncodeRun(run), CMD_SAVERUN, t)
                            .ConfigureAwait(false);
                var r = new BincodeReader(p);
                bool ok = r.ReadBool();
                r.ExpectEnd("SaveRunRsp");
                return ok;
            }, DefaultTimeout, ct);

        public static Task<Run?> GetRunAsync(ushort id, CancellationToken ct = default) =>
            RunAsync(async t =>
            {
                var p = await RequestAsync(CMD_GETRUN, EncodeU16(id), CMD_GETRUN, t)
                            .ConfigureAwait(false);
                var r = new BincodeReader(p);
                Run? run = r.ReadSomeTag() ? DecodeRun(r) : null;
                r.ExpectEnd("GetRunRsp");
                return run;
            }, DefaultTimeout, ct);

        /// <summary>
        /// Summary of every archived run. NumRecords and IsComplete are placeholders
        /// the server has not implemented — they come back 0 and false.
        /// </summary>
        public static Task<RunSummary[]> ListRunsAsync(CancellationToken ct = default) =>
            RunAsync(async t =>
            {
                var p = await RequestAsync(CMD_LISTRUNS, Array.Empty<byte>(),
                                           CMD_LISTRUNS, t).ConfigureAwait(false);
                var r = new BincodeReader(p);

                var id = r.ReadVecU16();
                var modified = r.ReadVecU64();
                var sodimm = r.ReadVecString();
                var energy = r.ReadVecF64();
                var ion = r.ReadVecString();
                var let = r.ReadVecF64();
                var desc = r.ReadVecString();
                var numRecords = r.ReadVecU16();
                var isComplete = r.ReadVecBool();
                r.ExpectEnd("ListRunsRsp");

                int n = new[] { id.Length, modified.Length, sodimm.Length, energy.Length,
                                ion.Length, let.Length, desc.Length, numRecords.Length,
                                isComplete.Length }.Min();

                var rows = new RunSummary[n];
                for (int i = 0; i < n; i++)
                    rows[i] = new RunSummary
                    {
                        Id = id[i],
                        TimeModified = modified[i],
                        SodimmId = sodimm[i],
                        Energy = energy[i],
                        Ion = ion[i],
                        LET = let[i],
                        Description = desc[i],
                        NumRecords = numRecords[i],
                        IsComplete = isComplete[i],
                    };
                return rows;
            }, DefaultTimeout, ct);

        /// <summary>
        /// Delete a run, every record it references, and those records' captures.
        /// Returns false if the server has no such id, or if any file could not be
        /// removed. Allow a long timeout: the server unlinks every child capture, which
        /// can be hundreds of megabytes each.
        /// </summary>
        public static Task<bool> DeleteRunAsync(ushort id, CancellationToken ct = default) =>
            RunAsync(async t =>
            {
                var p = await RequestAsync(CMD_DELRUN, EncodeU16(id), CMD_DELRUN, t)
                            .ConfigureAwait(false);
                var r = new BincodeReader(p);
                bool ok = r.ReadBool();
                r.ExpectEnd("DeleteRunRsp");
                return ok;
            }, DeleteTimeout, ct);

        // ============================== Captures ==============================

        /// <summary>
        /// Stream the capture for <paramref name="rid"/> into <paramref name="destination"/>
        /// and return the number of bytes written. Returns 0 when the server has no such
        /// capture, when /dvr is unmounted (safe mode), or for an Expose record — the
        /// engine creates the file for every task but the Expose arm writes nothing to it.
        ///
        /// Contents depend on the record type: a Read capture holds the raw bytes read
        /// back; Verify and Dynamic captures hold (actual XOR expected) per byte, so a
        /// nonzero byte marks a mismatch and its set bits are the flipped bits.
        ///
        /// Do NOT request the capture of a record that is still running. The server
        /// frames the length it measured up front, so a file still being appended to
        /// makes the byte count disagree and the server abandons the frame without a
        /// terminator, which costs the connection. Use <see cref="WaitForRecordAsync"/> first.
        /// </summary>
        public static Task<long> DownloadCaptureAsync(
            ushort rid,
            Stream destination,
            IProgress<long>? progress = null,
            CancellationToken ct = default)
        {
            if (destination is null) throw new ArgumentNullException(nameof(destination));

            return RunAsync(async t =>
            {
                await sendLock.WaitAsync(t).ConfigureAwait(false);
                try
                {
                    await WriteFrameAsync(CMD_GETCAPTURE, EncodeU16(rid), t).ConfigureAwait(false);

                    var head = new byte[HeaderSize];
                    await ReadExactAsync(head, t).ConfigureAwait(false);

                    uint sync = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(0, 4));
                    if (sync != SYNC_MARKER)
                        throw Fault($"bad SYNC 0x{sync:X8} on capture reply");

                    byte cmd = head[4];
                    if (cmd != CMD_GETCAPTURE)
                        throw Fault($"expected capture reply 0x{CMD_GETCAPTURE:X2}, got 0x{cmd:X2}");

                    long length = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(5, 4));

                    long copied = 0;
                    if (length > 0)
                    {
                        var buf = new byte[64 * 1024];
                        while (copied < length)
                        {
                            int want = (int)Math.Min(buf.Length, length - copied);
                            int n = await stream!.ReadAsync(buf.AsMemory(0, want), t)
                                                 .ConfigureAwait(false);
                            if (n == 0)
                                throw Fault($"connection closed after {copied} of {length} capture bytes");

                            await destination.WriteAsync(buf.AsMemory(0, n), t).ConfigureAwait(false);
                            copied += n;
                            progress?.Report(copied);
                        }
                    }

                    var tail = new byte[4];
                    await ReadExactAsync(tail, t).ConfigureAwait(false);
                    uint term = BinaryPrimitives.ReadUInt32BigEndian(tail);
                    if (term != TERM_MARKER)
                        throw Fault($"bad TERM 0x{term:X8} after capture body " +
                                    "(the file most likely changed size while it was being sent)");

                    await destination.FlushAsync(t).ConfigureAwait(false);
                    return copied;
                }
                finally { sendLock.Release(); }
            }, CaptureTimeout, ct);
        }

        /// <summary>Convenience wrapper writing the capture straight to a file on disk.</summary>
        public static async Task<long> DownloadCaptureToFileAsync(
            ushort rid,
            string path,
            IProgress<long>? progress = null,
            CancellationToken ct = default)
        {
            await using var fs = new FileStream(
                path, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 1 << 20, useAsync: true);
            return await DownloadCaptureAsync(rid, fs, progress, ct).ConfigureAwait(false);
        }

        // ============================== FPGA ==============================

        /// <summary>
        /// Name of the image the server believes is loaded, or null. This is tracked in
        /// memory only, so it reads null after a server restart even when the PL is
        /// configured — cross-check <see cref="InfoRsp.FpgaLoaded"/>.
        /// </summary>
        public static Task<string?> GetFpgaImageAsync(CancellationToken ct = default) =>
            RunAsync(async t =>
            {
                var p = await RequestAsync(CMD_GETFPGAIMAGE, Array.Empty<byte>(),
                                           CMD_GETFPGAIMAGE, t).ConfigureAwait(false);
                var r = new BincodeReader(p);
                string? name = r.ReadOptionString();
                r.ExpectEnd("GetFPGAImageRsp");
                return name;
            }, DefaultTimeout, ct);

        /// <summary>
        /// Every image available in /dvr/fpga, as the bare stem flash expects
        /// ("memtest" for memtest.bit — the server dropped the .bit.bin suffix this
        /// round, so a directory still holding .bit.bin files now lists as empty).
        /// An EMPTY LIST IS AMBIGUOUS beyond that: it means the
        /// directory is empty, missing, or unreadable — the last of which is the normal
        /// state while /dvr is unmounted in safe mode. Don't present it as "no images".
        /// </summary>
        public static Task<string[]> ListFpgaImagesAsync(CancellationToken ct = default) =>
            RunAsync(async t =>
            {
                var p = await RequestAsync(CMD_GETFPGAIMAGES, Array.Empty<byte>(),
                                           CMD_GETFPGAIMAGES, t).ConfigureAwait(false);
                var r = new BincodeReader(p);
                string[] images = r.ReadVecString();
                r.ExpectEnd("GetFPGAImagesRsp");
                return images;
            }, DefaultTimeout, ct);

        /// <summary>
        /// Load /dvr/fpga/&lt;imageName&gt;.bit via fpgautil — note the suffix changed
        /// from .bit.bin this round. Returns false if the
        /// name was rejected, the file was missing, the engine was busy, or fpgautil
        /// failed. After a successful flash the MIG re-trains, so wait for
        /// <see cref="InfoRsp.ControllerCalibrated"/> before queueing work.
        /// </summary>
        public static Task<bool> SetFpgaImageAsync(string imageName, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(imageName))
                throw new ArgumentException("image name must not be empty", nameof(imageName));
            if (imageName.Contains('/') || imageName.Contains('\\') || imageName.Contains(".."))
                throw new ArgumentException(
                    "image name must be a bare file stem — the server rejects anything " +
                    "that could escape /dvr/fpga", nameof(imageName));

            return RunAsync(async t =>
            {
                var payload = new BincodeWriter().Str(imageName).ToArray();
                var p = await RequestAsync(CMD_SETFPGAIMAGE, payload, CMD_SETFPGAIMAGE, t)
                            .ConfigureAwait(false);
                var r = new BincodeReader(p);
                bool ok = r.ReadBool();
                r.ExpectEnd("SetFPGAImageRsp");
                return ok;
            }, DefaultTimeout, ct);
        }

        // ============================== Info / power ==============================

        public static Task<InfoRsp> GetInfoAsync(CancellationToken ct = default) =>
            RunAsync(async t =>
            {
                var p = await RequestAsync(CMD_INFO, Array.Empty<byte>(), CMD_INFO, t)
                            .ConfigureAwait(false);
                var r = new BincodeReader(p);
                // Declaration order from types.rs — see the note on InfoRsp.
                var info = new InfoRsp
                {
                    BeamActive = r.ReadBool(),
                    BeamRequest = r.ReadBool(),
                    BeamServices = r.ReadBool(),
                    Pmic = r.ReadOptionPowerStats(),
                    PowerServicesProvided = r.ReadBool(),
                    ControllerCalibrated = r.ReadBool(),
                    UiClock = r.ReadBool(),
                    PlClock = r.ReadBool(),
                    FpgaLoaded = r.ReadBool(),
                    Sdram0 = r.ReadBool(),
                    Sdram1 = r.ReadBool(),
                    Sdram2 = r.ReadBool(),
                    Sdram3 = r.ReadBool(),
                };
                r.ExpectEnd("InfoRsp");
                return info;
            }, DefaultTimeout, ct);

        /// <summary>
        /// Ask whether the server wants to be powered down or rebooted, and whether it
        /// currently wants beam. The two power flags stay false until the server has
        /// unmounted /dvr and entered safe mode; RequestBeam is reported unconditionally.
        /// The server does not reboot itself — acting on this is the operator's job.
        ///
        /// Calling this also serves as a HEARTBEAT: the server stamps the time of every
        /// request and reports through InfoRsp.PowerServicesProvided whether that stamp
        /// is under 250 ms old. Whatever drives the contactor therefore has to poll this
        /// faster than every 250 ms, or the ZCU reports that nothing is supervising its
        /// power even while this client is connected and polling.
        /// </summary>
        public static Task<PowerMode> GetPowerModeAsync(CancellationToken ct = default) =>
            RunAsync(async t =>
            {
                var p = await RequestAsync(CMD_GETPOWERMODE, Array.Empty<byte>(),
                                           CMD_GETPOWERMODE, t).ConfigureAwait(false);
                var r = new BincodeReader(p);
                var pm = new PowerMode
                {
                    RequestPoweroff = r.ReadBool(),
                    RequestReboot = r.ReadBool(),
                    RequestBeam = r.ReadBool(),      // NEW, third field
                };
                r.ExpectEnd("GetPowerModeRsp");
                return pm;
            }, DefaultTimeout, ct);

        /// <summary>
        /// Ask the server to enter a power state. The server stores the request and the
        /// task engine acts on it at the top of its loop: it unmounts /dvr, hibernates
        /// the queue, and marks itself safe — only then does GETPOWERMODE start
        /// reporting the request to whatever is driving the contactor.
        ///
        /// Two consequences worth knowing. The engine only reaches that check BETWEEN
        /// tasks, so a request issued during a long run is not acted on until the run
        /// finishes — `success` here means "accepted", not "acted on". And once the
        /// server has marked itself safe it refuses every archive write and will not
        /// start another task, so <see cref="PowerRequest.Active"/> does not undo a
        /// request that has already been honoured; only a real power cycle clears it.
        /// </summary>
        public static Task<bool> SetPowerStateAsync(PowerRequest state,
                                                    CancellationToken ct = default) =>
            RunAsync(async t =>
            {
                var payload = new BincodeWriter().U8((byte)state).ToArray();
                var p = await RequestAsync(CMD_REQUESTPOWERSTATE, payload,
                                           CMD_REQUESTPOWERSTATE, t).ConfigureAwait(false);
                var r = new BincodeReader(p);
                bool ok = r.ReadBool();
                r.ExpectEnd("RequestPowerStateRsp");
                return ok;
            }, DefaultTimeout, ct);

        // ============================== Plumbing ==============================

        private static async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> body,
                                                 TimeSpan timeout,
                                                 CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            try
            {
                return await body(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Our timeout, not the caller's cancellation. We may be mid-frame, so
                // the stream position is unknown and the connection is unusable.
                Disconnect();
                throw new TimeoutException(
                    $"ZCU server did not respond within {timeout.TotalSeconds:0.#} s " +
                    "— connection dropped.");
            }
        }

        /// <summary>Write a frame, then read exactly one reply and check its command byte.</summary>
        private static async Task<byte[]> RequestAsync(byte cmd, byte[] payload,
                                                       byte expect, CancellationToken ct)
        {
            await sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await WriteFrameAsync(cmd, payload, ct).ConfigureAwait(false);
                try
                {
                    var (got, body) = await ReadFrameAsync(ct).ConfigureAwait(false);
                    if (got != expect)
                        throw Fault($"expected reply 0x{expect:X2} to command 0x{cmd:X2}, got 0x{got:X2}");
                    return body;
                }
                catch (OperationCanceledException)
                {
                    // The request is already on the wire and the server will answer it
                    // regardless. Abandoning the read leaves that reply in the socket for
                    // the NEXT command to misread, so the connection cannot be reused.
                    Disconnect();
                    throw;
                }
            }
            finally { sendLock.Release(); }
        }

        /// <summary>Write a frame for a command the server does not answer.</summary>
        private static async Task SendOnlyAsync(byte cmd, byte[] payload, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(DefaultTimeout);

            await sendLock.WaitAsync(cts.Token).ConfigureAwait(false);
            try
            {
                await WriteFrameAsync(cmd, payload, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Disconnect();
                throw new TimeoutException("ZCU server did not accept the command in time.");
            }
            finally { sendLock.Release(); }
        }

        private static async Task WriteFrameAsync(byte cmd, byte[] payload, CancellationToken ct)
        {
            if (stream is null)
                throw new InvalidOperationException("not connected");
            if (payload.Length > MaxOutboundPayload)
                throw new ArgumentException(
                    $"payload too large: {payload.Length} bytes (server drops anything " +
                    $"over {MaxOutboundPayload})");

            // SYNC(4) + CMD(1) + LEN(4) + payload + TERM(4)
            var packet = new byte[HeaderSize + payload.Length + 4];
            BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(0, 4), SYNC_MARKER);
            packet[4] = cmd;
            BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(5, 4), (uint)payload.Length);
            Buffer.BlockCopy(payload, 0, packet, HeaderSize, payload.Length);
            BinaryPrimitives.WriteUInt32BigEndian(
                packet.AsSpan(HeaderSize + payload.Length, 4), TERM_MARKER);

            await stream.WriteAsync(packet, ct).ConfigureAwait(false);
        }

        private static async Task<(byte cmd, byte[] payload)> ReadFrameAsync(CancellationToken ct)
        {
            if (stream is null)
                throw new InvalidOperationException("not connected");

            var head = new byte[HeaderSize];
            await ReadExactAsync(head, ct).ConfigureAwait(false);

            uint sync = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(0, 4));
            if (sync != SYNC_MARKER)
                throw Fault($"bad SYNC 0x{sync:X8}, expected 0x{SYNC_MARKER:X8}");

            byte cmd = head[4];
            uint length = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(5, 4));

            if (length > MaxInlinePayload)
                throw Fault($"reply claims {length} bytes, over the {MaxInlinePayload}-byte " +
                            "inline limit (use DownloadCaptureAsync for captures)");

            var payload = length == 0 ? Array.Empty<byte>() : new byte[length];
            if (length > 0)
                await ReadExactAsync(payload, ct).ConfigureAwait(false);

            var tail = new byte[4];
            await ReadExactAsync(tail, ct).ConfigureAwait(false);
            uint term = BinaryPrimitives.ReadUInt32BigEndian(tail);
            if (term != TERM_MARKER)
                throw Fault($"bad TERM 0x{term:X8}, expected 0x{TERM_MARKER:X8}");

            return (cmd, payload);
        }

        /// <summary>
        /// Fill <paramref name="buf"/> completely. A single ReadAsync returns whatever
        /// happens to have arrived, which for anything past one segment is less than
        /// asked for — this loop is what makes multi-KB replies work at all.
        /// </summary>
        private static async Task ReadExactAsync(byte[] buf, CancellationToken ct)
        {
            int total = 0;
            while (total < buf.Length)
            {
                int n = await stream!.ReadAsync(buf.AsMemory(total, buf.Length - total), ct)
                                     .ConfigureAwait(false);
                if (n == 0)
                    throw Fault("connection closed mid-frame");
                total += n;
            }
        }

        // ============================== Encoders ==============================

        private static byte[] EncodeU16(ushort v)
        {
            var b = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(b, v);
            return b;
        }

        private static byte[] EncodeConfig(ConfigCmd c)
        {
            var w = new BincodeWriter()
                .Bool(c.EnableLogging)
                .U32(c.NumBlocks)
                .U32(c.BlockSize)
                .U32(c.BlockFactor)
                .F64(c.SefiThreshold)
                .U8(c.DataPattern)
                .U64(c.PatternSeed)
                .Bool(c.IsX16)
                .U8(c.EnabledByteLanes)
                .Bool(c.FpgaAutoConfig)
                .Bool(c.AutomatedSefiMode)
                .Bool(c.ExposureTimeLimitEnabled)
                .F64(c.ExposureTimeLimitMs);

            var bytes = w.ToArray();
            if (bytes.Length != ConfigCmd.WireSize)
                throw new InvalidOperationException(
                    $"encoded ConfigCmd is {bytes.Length} bytes, expected {ConfigCmd.WireSize} " +
                    "— types.rs and this struct have diverged");
            return bytes;
        }

        private static byte[] EncodeRun(Run r)
        {
            if (r is null) throw new ArgumentNullException(nameof(r));

            return new BincodeWriter()
                .U64(r.TimeStarted)
                .U64(r.TimeModified)
                .U16(r.Id)
                .F64(r.Fluence)
                .F64(r.Temperature)
                .Str(r.SodimmId)
                .F64(r.Energy)
                .Str(r.Ion)
                .F64(r.LET)
                .F64(r.Angle)
                .F64(r.Flux)
                .F64(r.AirGap)
                .Str(r.Description)
                .VecU16(r.PreliminaryRecords)
                .VecU16(r.PrimaryRecords)
                .VecU16(r.PostRecords)
                .ToArray();
        }

        // ============================== Decoders ==============================

        // NOTE: these two decoders rely on C# evaluating object-initializer members
        // in source order, which the language guarantees. The reader is a CURSOR, so
        // reordering a line here silently decodes the wrong bytes rather than failing
        // to compile. Keep the order identical to record.rs / run.rs.
        //
        // The beam series are ACTIVE then REQUEST. task_engine.rs pushes them in that
        // order too, but don't rely on that — record.rs declaration order is what
        // bincode follows, and the two have disagreed before.
        private static Record DecodeRecord(BincodeReader r) => new Record
        {
            TimeStarted = r.ReadU64(),
            TimeEnded = r.ReadU64(),
            TimeDuration = r.ReadU64(),
            Rid = r.ReadU16(),
            Rtype = r.ReadRecordType(),
            Completed = r.ReadBool(),
            Aborted = r.ReadBool(),
            CorrectBits = r.ReadU64(),
            IncorrectBits = r.ReadU64(),
            ProcessedBits = r.ReadU64(),
            AdjErrBins = r.ReadOptionFixedU64Array(8),
            ErrBins = r.ReadOptionFixedU64Array(9),
            TimeToSefi = r.ReadOptionF64(),
            ExposureTime = r.ReadOptionF64(),
            LogTimestamp = r.ReadVecF64(),
            LogBitsProcessed = r.ReadVecU64(),
            LogBitsIncorrect = r.ReadVecU64(),
            LogBitsCorrect = r.ReadVecU64(),
            LogErrorRate = r.ReadVecF64(),
            LogCurrentAddress = r.ReadVecU32(),
            LogBeamActive = r.ReadVecBool(),
            LogBeamRequest = r.ReadVecBool(),
            LogSefiDetected = r.ReadVecBool(),
            LogPmic = r.ReadVecOptionPowerStats(),
            LogAdjErrBins = r.ReadVecFixedU64Array(8),
            LogErrBins = r.ReadVecFixedU64Array(9),
        };

        private static Run DecodeRun(BincodeReader r) => new Run
        {
            TimeStarted = r.ReadU64(),
            TimeModified = r.ReadU64(),
            Id = r.ReadU16(),
            Fluence = r.ReadF64(),
            Temperature = r.ReadF64(),
            SodimmId = r.ReadString(),
            Energy = r.ReadF64(),
            Ion = r.ReadString(),
            LET = r.ReadF64(),
            Angle = r.ReadF64(),
            Flux = r.ReadF64(),
            AirGap = r.ReadF64(),
            Description = r.ReadString(),
            PreliminaryRecords = r.ReadVecU16().ToList(),
            PrimaryRecords = r.ReadVecU16().ToList(),
            PostRecords = r.ReadVecU16().ToList(),
        };
    }
}