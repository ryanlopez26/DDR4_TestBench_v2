#nullable enable

// Config.cs — local model of the ZCU server's ConfigCmd, plus the push.
//
// WHY THE FIELD BECAME PROPERTIES
//
// `ConfigCmd` is a struct. The old `static public ConfigCmd sys` worked only
// because it was a FIELD: `Config.sys.NumBlocks = n` mutates in place. The
// moment anyone turns it into a property, that same line mutates a returned
// COPY and silently does nothing — it won't even warn. Everything here goes
// through typed properties over one private struct so that trap can't happen,
// and `Snapshot` hands out a copy explicitly.
//
// NOTE ON READING CONFIG BACK: the server has no GETCONFIG command. It loads
// config.bin at startup and keeps CONFIG in memory, but never reports it. So
// "the server's current config" is only knowable as "the last config this
// client pushed" (TcpManager.CommittedConfig). After a server restart, or if
// another client pushed a config, this client has no way to find out — worth
// adding a CMD_GETCONFIG on the Rust side.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DDR4_TestingApp
{
    static public class Config
    {
        public struct RAM_Card
        {
            public String name;
        }

        // The single source of truth. Private so nobody can mutate a copy.
        private static ConfigCmd current = new ConfigCmd
        {
            EnableLogging = false,
            BlockSize = 0x0001_0000,   // 64 KiB
            BlockFactor = 0x0000_0004,
            NumBlocks = 0x0000_0100,   // 256 blocks -> 16 MiB extent
            SefiThreshold = 0.0,
            DataPattern = 0,
            PatternSeed = 0,
            IsX16 = false,
            EnabledByteLanes = 0xFF,
            FpgaAutoConfig = false,
            AutomatedSefiMode = false,
        };

        /// <summary>Raised after any property changes, with the new value.</summary>
        public static event Action<ConfigCmd>? Changed;

        /// <summary>Reason the last <see cref="ApplyAsync"/> failed, or null on success.</summary>
        public static string? LastError { get; private set; }

        // ============================== Properties ==============================

        /// <summary>Server-side per-record logging (record.rs log_* arrays).</summary>
        public static bool EnableLogging
        {
            get => current.EnableLogging;
            set => Set(ref current.EnableLogging, value);
        }

        /// <summary>Bytes per block. Must be &gt; 0.</summary>
        public static uint BlockSize
        {
            get => current.BlockSize;
            set => Set(ref current.BlockSize, value);
        }

        /// <summary>
        /// Stride over blocks: the engine runs
        /// <c>(0..NumBlocks).step_by(BlockFactor)</c>. MUST be &gt;= 1 — the server
        /// panics its engine thread on step_by(0), so the setter floors at 1.
        /// </summary>
        public static uint BlockFactor
        {
            get => current.BlockFactor;
            set => Set(ref current.BlockFactor, value == 0 ? 1u : value);
        }

        /// <summary>Total blocks in the logical extent. Must be &gt; 0.</summary>
        public static uint NumBlocks
        {
            get => current.NumBlocks;
            set => Set(ref current.NumBlocks, value);
        }

        /// <summary>
        /// SEFI trip point as a FRACTION of bad samples (0..1), not a percentage.
        /// Must be &gt; 0 for dynamic runs: the server tests <c>ratio &gt;= threshold</c>,
        /// so zero reports a SEFI on the first sample of every run.
        /// </summary>
        public static double SefiThreshold
        {
            get => current.SefiThreshold;
            set => Set(ref current.SefiThreshold, value);
        }

        /// <summary>Pattern written and expected: zeros, 0xFF, or splitmix64(seed, address).</summary>
        public static DataPattern Pattern
        {
            get => (DataPattern)current.DataPattern;
            set => Set(ref current.DataPattern, (byte)value);
        }

        /// <summary>
        /// Seed for the pseudorandom pattern. The server reseeds on every config save,
        /// so changing this invalidates data already written to the DIMM — rewrite
        /// before verifying.
        /// </summary>
        public static ulong PatternSeed
        {
            get => current.PatternSeed;
            set => Set(ref current.PatternSeed, value);
        }

        /// <summary>Which FPGA image geometry the DIMM is wired for.</summary>
        public static bool IsX16
        {
            get => current.IsX16;
            set => Set(ref current.IsX16, value);
        }

        /// <summary>
        /// Bitmask of active byte lanes, one bit per lane.
        /// NOTE: the server does not read this field yet — lane masking is unimplemented,
        /// so a disabled lane is still tested and its errors still counted.
        /// </summary>
        public static byte EnabledByteLanes
        {
            get => current.EnabledByteLanes;
            set => Set(ref current.EnabledByteLanes, value);
        }

        /// <summary>NOTE: not read by the server yet — auto-flash is unimplemented.</summary>
        public static bool FpgaAutoConfig
        {
            get => current.FpgaAutoConfig;
            set => Set(ref current.FpgaAutoConfig, value);
        }

        /// <summary>
        /// When set, the engine requests a REBOOT after a dynamic run finishes: it
        /// unmounts /dvr and parks. Nothing in the server actually reboots, and the
        /// safe-mode flag is never cleared, so the engine stays parked until the
        /// operator power-cycles it. Treat as experimental.
        /// </summary>
        public static bool AutomatedSefiMode
        {
            get => current.AutomatedSefiMode;
            set => Set(ref current.AutomatedSefiMode, value);
        }

        // ============================== Bulk access ==============================

        /// <summary>A copy of the whole configuration. Mutating it does not affect Config.</summary>
        public static ConfigCmd Snapshot => current;

        /// <summary>Replace the whole configuration at once (e.g. loading a preset).</summary>
        public static void Load(ConfigCmd cfg)
        {
            if (cfg.BlockFactor == 0) cfg.BlockFactor = 1;
            current = cfg;
            OnChanged();
        }

        /// <summary>Restore the server's built-in defaults (config.rs CONFIG).</summary>
        public static void ResetToDefaults() => Load(ConfigCmd.Default);

        /// <summary>True if a lane bit is set. Lanes are 0..7.</summary>
        public static bool IsLaneEnabled(int lane) =>
            lane >= 0 && lane < 8 && (current.EnabledByteLanes & (1 << lane)) != 0;

        /// <summary>Set or clear one lane bit.</summary>
        public static void SetLaneEnabled(int lane, bool enabled)
        {
            if (lane < 0 || lane > 7) throw new ArgumentOutOfRangeException(nameof(lane));
            byte mask = (byte)(1 << lane);
            EnabledByteLanes = enabled
                ? (byte)(current.EnabledByteLanes | mask)
                : (byte)(current.EnabledByteLanes & ~mask);
        }

        // ============================== Derived values ==============================

        /// <summary>Logical extent in bytes: NumBlocks * BlockSize. Computed in 64-bit.</summary>
        public static ulong ExtentBytes => (ulong)current.NumBlocks * current.BlockSize;

        /// <summary>
        /// Blocks the engine actually visits. This is a CEILING, not a floor: step_by
        /// yields 0, f, 2f, ... so a NumBlocks that isn't a multiple of BlockFactor
        /// still visits one final partial stride. The old `NumBlocks / BlockFactor`
        /// under-reported by one block in that case.
        /// </summary>
        public static ulong SampledBlocks =>
            current.BlockFactor == 0
                ? 0
                : ((ulong)current.NumBlocks + current.BlockFactor - 1) / current.BlockFactor;

        /// <summary>Bytes actually read or written in one pass.</summary>
        public static ulong SampledBytes => SampledBlocks * current.BlockSize;

        /// <summary>Fraction of the extent covered, 0..100.</summary>
        public static double CoveragePercent =>
            ExtentBytes == 0 ? 0.0 : 100.0 * SampledBytes / ExtentBytes;

        /// <summary>
        /// Capture size for one pass of a Read or Verify record: the engine writes one
        /// byte per tested byte. A Dynamic record loops until the beam drops, so its
        /// capture is this much PER PASS and unbounded overall.
        /// </summary>
        public static ulong EstimatedCaptureBytesPerPass => SampledBytes;

        /// <summary>
        /// Set the extent from a byte count, keeping BlockSize fixed. Truncates to a
        /// whole number of blocks — read <see cref="ExtentBytes"/> back for the value
        /// actually applied.
        /// </summary>
        public static void SetSelectionSize(ulong bytes)
        {
            if (current.BlockSize == 0)
                throw new InvalidOperationException("set BlockSize before SetSelectionSize");

            ulong blocks = bytes / current.BlockSize;
            NumBlocks = blocks > uint.MaxValue ? uint.MaxValue : (uint)blocks;
        }

        /// <summary>
        /// Kept for compatibility with existing call sites. Recomputes NumBlocks from
        /// Program.selection_size and publishes the sampled size back to
        /// Program.sample_size.
        /// </summary>
        static public void updateCalculations()
        {
            if (current.BlockSize == 0) return;   // was a divide-by-zero crash

            SetSelectionSize(Program.selection_size);

            ulong sampled = SampledBytes;
            Program.sample_size = sampled > uint.MaxValue ? uint.MaxValue : (uint)sampled;
        }

        // ============================== Validation ==============================

        /// <summary>
        /// True if the server will accept this configuration. On false,
        /// <paramref name="error"/> explains what would break.
        /// </summary>
        public static bool TryValidate(out string error)
        {
            try
            {
                current.Validate();
                error = "";
                return true;
            }
            catch (ArgumentException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Non-fatal things worth showing the operator before a beam run. Empty when
        /// there's nothing to say.
        /// </summary>
        public static IReadOnlyList<string> Warnings()
        {
            var w = new List<string>();

            if (current.BlockFactor > 1 && current.NumBlocks % current.BlockFactor != 0)
                w.Add($"NumBlocks ({current.NumBlocks}) is not a multiple of BlockFactor " +
                      $"({current.BlockFactor}); the last stride is partial, so " +
                      $"{SampledBlocks} blocks are visited, not {current.NumBlocks / current.BlockFactor}.");

            if (EstimatedCaptureBytesPerPass > 512UL * 1024 * 1024)
                w.Add($"Each pass writes a {EstimatedCaptureBytesPerPass / (1024.0 * 1024 * 1024):0.##} GB " +
                      "capture to /dvr. Check free space before queueing.");

            if (current.Pattern() == DataPattern.PseudoRandom && current.PatternSeed == 0)
                w.Add("Pseudorandom pattern with seed 0 — fine, but every run will use the " +
                      "same data unless you vary the seed.");

            if (current.AutomatedSefiMode)
                w.Add("Automated SEFI mode will unmount /dvr and park the engine when the " +
                      "dynamic run ends; the server cannot reboot itself.");

            return w;
        }

        /// <summary>
        /// True when the local configuration differs from the last one successfully
        /// pushed on this connection (or when nothing has been pushed yet).
        /// </summary>
        public static bool IsDirty
        {
            get
            {
                var committed = TcpManager.CommittedConfig;
                if (committed == null) return true;
                return !Equal(committed.Value, current);
            }
        }

        // ============================== Push ==============================

        /// <summary>
        /// Validate and push the configuration to the server, which persists it to
        /// config.bin and reseeds its PRNG. Returns false and sets
        /// <see cref="LastError"/> on failure; no UI is shown from here.
        ///
        /// Do NOT pass a short-deadline token. Cancelling mid-request abandons a reply
        /// that is still in the socket, which desynchronizes the next command.
        /// TcpManager already applies its own DefaultTimeout.
        /// </summary>
        public static async Task<bool> ApplyAsync(CancellationToken ct = default)
        {
            LastError = null;

            if (!TryValidate(out string why))
            {
                LastError = why;
                Program.taskInfo = "Config rejected: " + why;
                return false;
            }

            if (TcpManager.Status != TcpManager.ConnectionStatus.Connected)
            {
                LastError = "Not connected.";
                Program.taskInfo = LastError;
                return false;
            }

            Program.taskName = "CONFIG";
            Program.taskProgress = 0.0f;

            try
            {
                await TcpManager.ApplyConfigAsync(current, ct);
                Program.taskInfo = "Configuration applied.";
                return true;
            }
            catch (OperationCanceledException)
            {
                LastError = "Config cancelled.";
                Program.taskInfo = LastError;
                return false;
            }
            catch (TimeoutException ex)
            {
                LastError = ex.Message;
                Program.taskInfo = "Config timed out.";
                return false;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Program.taskInfo = "Config failed: " + ex.Message;
                return false;
            }
            finally
            {
                Program.taskProgress = 100.0f;
            }
        }

        /// <summary>
        /// Fire-and-forget wrapper for existing call sites. Safe only because
        /// ApplyAsync swallows its own exceptions — prefer awaiting ApplyAsync so the
        /// caller can react to the result.
        /// </summary>
        static public async void apply()
        {
            await ApplyAsync();
        }

        // ============================== Internals ==============================

        private static void Set<T>(ref T field, T value) where T : struct, IEquatable<T>
        {
            if (field.Equals(value)) return;
            field = value;
            OnChanged();
        }

        private static void OnChanged() => Changed?.Invoke(current);

        private static bool Equal(ConfigCmd a, ConfigCmd b) =>
            a.EnableLogging == b.EnableLogging
            && a.NumBlocks == b.NumBlocks
            && a.BlockSize == b.BlockSize
            && a.BlockFactor == b.BlockFactor
            && a.SefiThreshold.Equals(b.SefiThreshold)
            && a.DataPattern == b.DataPattern
            && a.PatternSeed == b.PatternSeed
            && a.IsX16 == b.IsX16
            && a.EnabledByteLanes == b.EnabledByteLanes
            && a.FpgaAutoConfig == b.FpgaAutoConfig
            && a.AutomatedSefiMode == b.AutomatedSefiMode;
    }

    internal static class ConfigCmdExtensions
    {
        /// <summary>Typed view of the raw DataPattern byte.</summary>
        public static DataPattern Pattern(this ConfigCmd c) => (DataPattern)c.DataPattern;
    }
}