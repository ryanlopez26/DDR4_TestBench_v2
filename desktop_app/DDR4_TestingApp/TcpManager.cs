// TcpManager.cs — C# client for the Rust ZCU104 DDR4-tester TCP server
// ("ZCU Server V3 — NSRL Edition").
//
// ============================== WHAT CHANGED ==============================
//
// This is a rewrite, not a patch. The server's execution model is different:
//
//   OLD: a command (Write/Verify/Dump/Dynamic) ran synchronously on the
//        server's single thread and streamed progress frames back on the same
//        connection until percent_complete >= 100.
//
//   NEW: Write/Verify/Read/Dynamic only PUSH A TASK onto the engine queue and
//        send NO RESPONSE AT ALL. A separate engine thread runs one task at a
//        time. Progress comes from polling CMD_ENGINESTATUS. The finished
//        Record is written to /dvr/records and fetched with CMD_GETRECORD.
//        Byte-level data goes to a capture file fetched with CMD_GETCAPTURE.
//
// Consequences for callers:
//   * Do NOT await a reply after queueing a task — you will block forever.
//     Queue*Async methods return as soon as the frame is on the wire.
//   * A task is identified by a u16 "rid" obtained from CMD_GENRECORD.
//   * DumpPage / ExpectedDumpPages / PAGE_SIZE / ComparisonMode are gone.
//     The old paged dump is replaced by: queue a Read record, wait for it,
//     then DownloadCaptureAsync. A Read capture holds raw bytes read back;
//     a Verify or Dynamic capture holds (actual XOR expected) per byte, which
//     is what ComparisonMode used to select.
//   * The UUID availability check is gone. CMD_GENRECORD / CMD_GENRUN ALLOCATE
//     an id instead of testing one.
//   * Write/Verify/Dynamic no longer carry pattern+seed. Those now live in the
//     persistent CONFIG (data_pattern / pattern_seed), so apply a Config first.
//
// ============================== FRAMING ==============================
//
//     [SYNC u32 BE = 0xDEADBEEF]
//     [CMD  u8 ]
//     [LEN  u32 BE]          <-- WAS u16. Header is 9 bytes now, not 7.
//     [PAYLOAD ... LEN bytes]
//     [TERM u32 BE = 0xCAFEBABE]
//
// This is a flag day: a u16 length against the new server desyncs the frame
// immediately. Both halves must be deployed together.
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
//                   Write=2 Dynamic=3)
//
// reject_trailing_bytes means an OUTBOUND payload must match the server struct
// byte-for-byte. One extra byte is an error, not slack.
//
// ============================== COMMANDS ==============================
//
//  Code  Name            Payload (-> reply)
//  0x01  WRITE           u16 rid                     -> (nothing)
//  0x02  VERIFY          u16 rid                     -> (nothing)
//  0x03  READ            u16 rid                     -> (nothing)
//  0x04  CONFIG          ConfigCmd, 34 bytes         -> 1-byte ACK
//  0x05  DYNAMIC         u16 rid                     -> (nothing)
//  0x06  INFO            (empty)                     -> InfoRsp, 5 bools
//  0x07  GENRUN          (empty)                     -> u16 run id
//  0x08  GENRECORD       (empty)                     -> u16 record id
//  0x09  SAVERUN         Run                         -> bool success
//  0x0A  ABORT           (empty)                     -> (nothing)
//  0x0B  GETRECORD       u16 rid                     -> Option<Record>
//  0x0C  GETRUN          u16 id                      -> Option<Run>
//  0x0D  GETCAPTURE      u16 rid                     -> RAW BYTES (see below)
//  0x0E  ENGINESTATUS    (empty)                     -> EngineStatusRsp
//  0x0F  GETPOWERMODE    (empty)                     -> 2 bools
//  0x10  LISTRECORDS     (empty)                     -> ListRecordsRsp
//  0x11  LISTRUNS        (empty)                     -> ListRunsRsp
//  0x12  GETFPGAIMAGE    (empty)                     -> Option<String>
//  0x13  SETFPGAIMAGE    String image_name           -> bool success
//  0x14  DELRUN          u16 id                      -> bool success
//  0x15  DELRECORD       u16 rid                     -> bool success
//  0x16  GETFPGAIMAGES   (empty)                     -> Vec<String> images
//
// CMD_GETCAPTURE is the one command whose reply is NOT bincode. The server
// streams the capture file straight from disk to the socket, so the payload is
// the raw file contents and LEN is the file size. LEN == 0 means "no capture"
// (missing file, or /dvr unmounted in safe mode). Captures run to hundreds of
// megabytes, so never read one into a byte[] — use DownloadCaptureAsync, which
// copies to a Stream in chunks.
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
    /// types.rs ConfigCmd — 34 bytes on the wire. Field order here MUST match the
    /// Rust declaration order, since that is what determines the bincode layout.
    /// </summary>
    public struct ConfigCmd
    {
        public bool EnableLogging;      //  0  (1)
        public uint NumBlocks;          //  1  (4)
        public uint BlockSize;          //  5  (4)
        public uint BlockFactor;        //  9  (4)
        public double SefiThreshold;    // 13  (8)  fraction of bad samples, 0..1
        public byte DataPattern;        // 21  (1)
        public ulong PatternSeed;       // 22  (8)
        public bool IsX16;              // 30  (1)
        public byte EnabledByteLanes;   // 31  (1)
        public bool FpgaAutoConfig;     // 32  (1)
        public bool AutomatedSefiMode;  // 33  (1)

        public const int WireSize = 34;

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

        /// <summary>Runs of k adjacent flipped bits; 8 entries. Null if absent.</summary>
        public ulong[]? AdjErrBins;
        /// <summary>Bytes with exactly k flipped bits; 9 entries. Null if absent.</summary>
        public ulong[]? ErrBins;

        public double? TimeToSefi;
        public double? ExposureTime;

        // Time series. NOTE: the server does not push every series in every
        // record type, so these can legitimately have DIFFERENT LENGTHS within
        // one record. Zip them defensively.
        public double[] LogTimestamp = Array.Empty<double>();
        public ulong[] LogBitsProcessed = Array.Empty<ulong>();
        public ulong[] LogBitsIncorrect = Array.Empty<ulong>();
        public ulong[] LogBitsCorrect = Array.Empty<ulong>();
        public double[] LogErrorRate = Array.Empty<double>();   // never written by the server yet
        public uint[] LogCurrentAddress = Array.Empty<uint>();
        public bool[] LogBeamSignal = Array.Empty<bool>();
        public bool[] LogSefiDetected = Array.Empty<bool>();
        public ulong[][] LogAdjErrBins = Array.Empty<ulong[]>();
        public ulong[][] LogErrBins = Array.Empty<ulong[]>();

        /// <summary>Shortest of the per-sample series, i.e. how many rows can be zipped safely.</summary>
        public int SampleCount => new[]
        {
            LogTimestamp.Length,
            LogBitsIncorrect.Length,
            LogCurrentAddress.Length,
            LogBeamSignal.Length,
        }.Min();
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

        public List<ushort> Preverify = new();
        public List<ushort> Prewrite = new();

        public ushort? Priwrite;
        public List<ushort>? Pridynamic;
        public ushort? Priverify;

        public List<ushort> Postverify = new();
        public List<ushort> Postwrite = new();
    }

    /// <summary>task_engine.rs TaskRequest — one queued, not-yet-started task.</summary>
    public struct TaskRequest
    {
        public RecordType Rtype;
        public ushort Rid;
    }

    /// <summary>types.rs EngineStatusRsp.</summary>
    public sealed class EngineStatus
    {
        /// <summary>
        /// Snapshot of the in-flight record, or null when idle. This carries the
        /// full log arrays, so a long run makes every status poll expensive.
        /// </summary>
        public Record? ActiveRecord;

        /// <summary>Percent, 0..100. Sawtooths on Dynamic runs (one ramp per sweep).</summary>
        public double RecordProgress;

        public bool IsIdle;
        public TaskRequest[] Queue = Array.Empty<TaskRequest>();

        /// <summary>
        /// True when the engine claims to be idle but still has work queued. The
        /// engine refuses to start a task while /dvr is unmounted (safe mode) or
        /// while the MIG calibration line reads low, and it does so silently —
        /// check <see cref="TcpManager.GetInfoAsync"/> for which.
        /// </summary>
        public bool IsStalled => IsIdle && Queue.Length > 0;
    }

    /// <summary>types.rs InfoRsp — the five read-only EMIO lines from gpio.rs.</summary>
    public struct InfoRsp
    {
        public bool BeamSignal;
        public bool ControllerCalibrated;
        public bool UiClock;
        public bool PlClock;
        public bool FpgaLoaded;
    }

    /// <summary>
    /// types.rs GetPowerModeRsp. Both flags stay false unless the server has
    /// entered safe mode (/dvr unmounted) AND a power change was requested.
    /// </summary>
    public struct PowerMode
    {
        public bool RequestPoweroff;
        public bool RequestReboot;
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

        public RecordType ReadRecordType()
        {
            uint v = ReadU32();
            if (v > 3)
                throw new InvalidDataException($"unknown RecordType variant {v}");
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

        /// <summary>Frame header: SYNC(4) + CMD(1) + LEN(4).</summary>
        private const int HeaderSize = 9;

        /// <summary>
        /// Mirrors the server's MAX_PAYLOAD (server.rs), which is now enforced: the
        /// server logs the length and DROPS THE CONNECTION on anything larger, with no
        /// reply. Fail locally instead so the link survives.
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

            // (The old client reset Info._infoFetchInProgress here. Re-add that
            //  line if your Info screen still depends on it.)
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

        public static Task QueueDynamicAsync(ushort rid, CancellationToken ct = default) =>
            SendOnlyAsync(CMD_DYNAMIC, EncodeU16(rid), ct);

        public static Task QueueTaskAsync(RecordType type, ushort rid, CancellationToken ct = default) =>
            type switch
            {
                RecordType.Write => QueueWriteAsync(rid, ct),
                RecordType.Verify => QueueVerifyAsync(rid, ct),
                RecordType.Read => QueueReadAsync(rid, ct),
                RecordType.Dynamic => QueueDynamicAsync(rid, ct),
                _ => throw new ArgumentOutOfRangeException(nameof(type)),
            };

        /// <summary>
        /// Ask the engine to abandon the task in flight. Also no reply. The engine only
        /// notices at its next progress tick (~200 ms), and it archives the partial
        /// record with Aborted = true.
        /// </summary>
        public static Task AbortAsync(CancellationToken ct = default) =>
            SendOnlyAsync(CMD_ABORT, Array.Empty<byte>(), ct);

        // ============================== Engine status ==============================

        /// <summary>
        /// Poll the engine. Beware: the reply embeds the entire in-flight Record,
        /// including every log array, so on a long run each poll moves megabytes.
        /// Poll at 1 Hz, not 10 Hz.
        /// </summary>
        public static Task<EngineStatus> GetEngineStatusAsync(CancellationToken ct = default) =>
            RunAsync(async t =>
            {
                var p = await RequestAsync(CMD_ENGINESTATUS, Array.Empty<byte>(),
                                           CMD_ENGINESTATUS, t).ConfigureAwait(false);
                var r = new BincodeReader(p);
                var st = new EngineStatus
                {
                    ActiveRecord = r.ReadSomeTag() ? DecodeRecord(r) : null,
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
        /// Completion is detected by the record appearing in LISTRECORDS rather than
        /// by <c>IsIdle</c> alone: the engine does not clear IsIdle until it has
        /// dequeued, so a naive idle check returns "done" immediately after queueing.
        ///
        /// This can wait indefinitely — the engine refuses to start while /dvr is in
        /// safe mode or the MIG calibration line is low, and says nothing about it.
        /// Watch <see cref="EngineStatus.IsStalled"/> and always pass a cancellation
        /// token.
        /// </summary>
        public static async Task<Record?> RunTaskAsync(
            RecordType type,
            ushort rid,
            IProgress<EngineStatus>? progress = null,
            TimeSpan? pollInterval = null,
            CancellationToken ct = default)
        {
            await QueueTaskAsync(type, rid, ct).ConfigureAwait(false);
            return await WaitForRecordAsync(rid, progress, pollInterval, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Wait until record <paramref name="rid"/> has been archived, then return it.
        /// See <see cref="RunTaskAsync"/> for the completion-detection caveats.
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

                bool inFlight = status.ActiveRecord?.Rid == rid
                                || status.Queue.Any(q => q.Rid == rid);

                if (!inFlight)
                {
                    // Either it finished, or it has not started yet. The archive is
                    // the authority — records only land there on completion or abort.
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
            }, DefaultTimeout, ct);

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
        /// removed — note the run entry is dropped even when a child unlink failed, so
        /// false can mean "mostly deleted". Re-list afterwards rather than assuming.
        ///
        /// Unlike <see cref="DeleteRecordAsync"/>, this does NOT check whether a
        /// referenced record is currently running: the server unlinks child captures by
        /// id straight from the run's record lists. Deleting a run whose record is still
        /// in flight destroys that capture while the engine is writing it. Check
        /// <see cref="GetEngineStatusAsync"/> first.
        ///
        /// Takes longer than a normal command: the server holds its record lock while
        /// unlinking every child capture, which can be hundreds of megabytes each.
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
        /// capture, or when /dvr is unmounted (safe mode).
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
        /// ("memtest" for memtest.bit.bin). An EMPTY LIST IS AMBIGUOUS: it means the
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
        /// Load /dvr/fpga/&lt;imageName&gt;.bit.bin via fpgautil. Returns false if the
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
                var info = new InfoRsp
                {
                    BeamSignal = r.ReadBool(),
                    ControllerCalibrated = r.ReadBool(),
                    UiClock = r.ReadBool(),
                    PlClock = r.ReadBool(),
                    FpgaLoaded = r.ReadBool(),
                };
                r.ExpectEnd("InfoRsp");
                return info;
            }, DefaultTimeout, ct);

        /// <summary>
        /// Ask whether the server wants to be powered down or rebooted. Both flags stay
        /// false until the server has unmounted /dvr and entered safe mode. The server
        /// does not reboot itself — acting on this is the operator's job.
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
                };
                r.ExpectEnd("GetPowerModeRsp");
                return pm;
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
                .Bool(c.AutomatedSefiMode);

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
                .VecU16(r.Preverify)
                .VecU16(r.Prewrite)
                .OptU16(r.Priwrite)
                .OptVecU16(r.Pridynamic)
                .OptU16(r.Priverify)
                .VecU16(r.Postverify)
                .VecU16(r.Postwrite)
                .ToArray();
        }

        // ============================== Decoders ==============================

        // NOTE: these two decoders rely on C# evaluating object-initializer members
        // in source order, which the language guarantees. The reader is a CURSOR, so
        // reordering a line here silently decodes the wrong bytes rather than failing
        // to compile. Keep the order identical to record.rs / run.rs.
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
            LogBeamSignal = r.ReadVecBool(),
            LogSefiDetected = r.ReadVecBool(),
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
            Preverify = r.ReadVecU16().ToList(),
            Prewrite = r.ReadVecU16().ToList(),
            Priwrite = r.ReadOptionU16(),
            Pridynamic = r.ReadSomeTag() ? r.ReadVecU16().ToList() : null,
            Priverify = r.ReadOptionU16(),
            Postverify = r.ReadVecU16().ToList(),
            Postwrite = r.ReadVecU16().ToList(),
        };
    }
}