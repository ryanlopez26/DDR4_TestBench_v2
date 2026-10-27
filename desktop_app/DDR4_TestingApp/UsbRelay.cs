#nullable enable

// UsbRelay.cs — control for dcttech/"USBRelayN" HID relay boards.
//
// Requires the HidSharp NuGet package (2.1.0 or later):
//     dotnet add package HidSharp
// HidSharp works on Windows, Linux and macOS with no driver and no libusb,
// because these boards enumerate as plain HID devices.
//
// ============================== PROTOCOL ==============================
//
// Device: VID 0x16C0 / PID 0x05DF (Van Ooijen Technische Informatica), sold as
// www.dcttech.com "USBRelay1/2/4/8". Some clones use 0x0519/0x2018.
//
// Commands are 9-byte reports: report id, command, relay index, then zeros.
//
//     [0] 0x00   report id
//     [1] 0xFF   turn relay on       (CMD_ON)
//         0xFD   turn relay off      (CMD_OFF)
//         0xFA   write a new serial  (CMD_SET_SERIAL — not exposed here)
//     [2] 1..8   relay index, ONE-BASED. Index 0 is the serial-change target,
//                not "all relays", so never pass 0 as a channel.
//     [3..8]     zero
//
// Reading back gives both identity and state in one feature report:
//
//     [0]        report id
//     [1..5]     5-character ASCII serial
//     [6]        unused
//     [7]        state bitmap, bit (relay-1) set when that relay is closed
//     [8]        unused
//
// IMPORTANT: these boards do NOT populate the USB iSerialNumber descriptor.
// The only identity is the one in the feature report above, so telling two
// boards apart means opening each and reading it — you cannot filter on the
// USB serial. This is the single most common mistake with these devices.
//
// The board exposes an IN endpoint only, with no OUT endpoint, so commands go
// over the control pipe. That is why writes use SetFeature rather than Write;
// Write happens to work on Linux (hidapi silently falls back to a control
// transfer) but is unreliable on Windows. There is a fallback below for
// firmware revisions that only accept output reports.
//
// NOT SUPPORTED: LCUS-type modules. Those are CH340 USB-serial devices with a
// completely different framing, despite being sold alongside these. If the
// board shows up as a CH340/serial port rather than 16C0:05DF, none of this
// applies.
//
// ============================== LINUX PERMISSIONS ==============================
//
// hidraw nodes are root-only by default. Drop a udev rule:
//
//     SUBSYSTEMS=="usb", ATTRS{idVendor}=="16c0", ATTRS{idProduct}=="05df", MODE="0660", GROUP="plugdev"
//
// then `sudo udevadm control -R` and replug. On Windows nothing is needed.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HidSharp;

namespace DDR4_TestingApp
{
    /// <summary>One relay board as reported by its feature report.</summary>
    public sealed class RelayBoard
    {
        /// <summary>5-character id from the feature report, e.g. "PSUIS".</summary>
        public string Serial { get; init; } = "";

        /// <summary>Channels on the board, parsed from the product string ("USBRelay4" -> 4).</summary>
        public int RelayCount { get; init; } = 1;

        /// <summary>Bitmap at the time of enumeration; bit (relay-1) set means closed.</summary>
        public byte State { get; init; }

        /// <summary>OS device path — the stable handle when two boards share a serial.</summary>
        public string DevicePath { get; init; } = "";

        /// <summary>USB product string, e.g. "USBRelay2".</summary>
        public string Product { get; init; } = "";

        /// <summary>State of one channel at enumeration time. Channels are 1-based.</summary>
        public bool IsOn(int relay) =>
            relay >= 1 && relay <= 8 && (State & (1 << (relay - 1))) != 0;

        public override string ToString() =>
            $"{Serial} ({Product}, {RelayCount} relay{(RelayCount == 1 ? "" : "s")}, state 0x{State:X2})";
    }

    /// <summary>
    /// Enumerate and drive USB HID relay boards.
    ///
    /// Every call opens the device, does its work and closes again rather than
    /// holding a handle. Operations are a single control transfer, so the open
    /// costs little, and not holding the device means the `usbrelay` CLI and this
    /// code can be used interchangeably without one locking the other out.
    /// </summary>
    public static class UsbRelay
    {
        public const int VendorId = 0x16C0;
        public const int ProductId = 0x05DF;

        // Alternative id used by some clones.
        public const int AltVendorId = 0x0519;
        public const int AltProductId = 0x2018;

        private const byte CmdOn = 0xFF;
        private const byte CmdOff = 0xFD;

        private const int ReportLength = 9;
        private const int SerialOffset = 1;
        private const int SerialLength = 5;

        /// <summary>
        /// Index of the state bitmap in the feature report. Verify once against your
        /// board with <see cref="DumpFeatureReport"/>: toggle a relay and see which
        /// byte changes. 7 is correct for stock dcttech firmware.
        /// </summary>
        public const int StateByteIndex = 7;

        /// <summary>Reason the last operation failed, or null if it succeeded.</summary>
        public static string? LastError { get; private set; }

        // HidStream is not thread-safe and these boards tolerate exactly one
        // conversation at a time; serialize everything.
        private static readonly object gate = new object();

        // ============================== Enumeration ==============================

        /// <summary>
        /// Every relay board currently attached. Each is opened and queried, so this
        /// costs a control transfer per board — call it on demand, not in a tight loop.
        /// Boards that fail to open are skipped rather than throwing.
        /// </summary>
        public static IReadOnlyList<RelayBoard> Enumerate()
        {
            LastError = null;
            var found = new List<RelayBoard>();

            lock (gate)
            {
                foreach (HidDevice device in CandidateDevices())
                {
                    try
                    {
                        if (!device.TryOpen(out HidStream stream)) continue;
                        using (stream)
                        {
                            byte[]? report = TryReadFeature(stream);
                            if (report == null) continue;

                            string serial = ReadSerial(report);
                            string product = SafeProductName(device);

                            found.Add(new RelayBoard
                            {
                                Serial = serial,
                                Product = product,
                                RelayCount = ParseRelayCount(product),
                                State = report[StateByteIndex],
                                DevicePath = device.DevicePath,
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        // A board that can't be opened (permissions, another process
                        // holding it) shouldn't hide the ones that can.
                        LastError = ex.Message;
                    }
                }
            }

            return found;
        }

        private static IEnumerable<HidDevice> CandidateDevices() =>
            DeviceList.Local.GetHidDevices(VendorId, ProductId)
                      .Concat(DeviceList.Local.GetHidDevices(AltVendorId, AltProductId));

        /// <summary>First board with this serial, or null.</summary>
        public static RelayBoard? Find(string serial) =>
            Enumerate().FirstOrDefault(
                b => string.Equals(b.Serial, serial, StringComparison.Ordinal));

        // ============================== State ==============================

        /// <summary>
        /// Current bitmap for the board with this serial, or null if it isn't attached
        /// or couldn't be read. Bit (relay-1) set means the channel is closed.
        /// </summary>
        public static byte? ReadState(string serial)
        {
            LastError = null;

            lock (gate)
            {
                HidDevice? device = ResolveLocked(serial, out HidStream? stream);
                if (device == null || stream == null) return null;

                using (stream)
                {
                    byte[]? report = TryReadFeature(stream);
                    if (report != null) return report[StateByteIndex];

                    LastError = $"no readable feature report from {serial}";
                    return null;
                }
            }
        }

        /// <summary>State of one channel, or null if unknown. Channels are 1-based.</summary>
        public static bool? IsOn(string serial, int relay)
        {
            byte? state = ReadState(serial);
            if (state == null) return null;
            if (relay < 1 || relay > 8)
            {
                LastError = $"relay {relay} out of range (1-8)";
                return null;
            }
            return (state.Value & (1 << (relay - 1))) != 0;
        }

        // ============================== Control ==============================

        /// <summary>
        /// Open or close one channel. Channels are ONE-BASED — index 0 is the
        /// serial-rewrite target on these boards, not "all relays".
        /// </summary>
        public static bool Set(string serial, int relay, bool on)
        {
            LastError = null;

            if (relay < 1 || relay > 8)
            {
                LastError = $"relay {relay} out of range (1-8); index 0 is the " +
                            "serial-change command, not all-relays";
                return false;
            }

            lock (gate)
            {
                HidDevice? device = ResolveLocked(serial, out HidStream? stream);
                if (device == null || stream == null) return false;

                using (stream)
                {
                    return SendCommand(stream, on ? CmdOn : CmdOff, (byte)relay);
                }
            }
        }

        /// <summary>
        /// Set every channel on the board. Issued one channel at a time: the dedicated
        /// all-on/all-off opcodes (0xFE/0xFC) aren't honoured by every firmware
        /// revision, and looping always works.
        /// </summary>
        public static bool SetAll(string serial, bool on)
        {
            RelayBoard? board = Find(serial);
            if (board == null)
            {
                LastError = $"no relay board with serial {serial}";
                return false;
            }

            bool ok = true;
            for (int relay = 1; relay <= board.RelayCount; relay++)
                ok &= Set(serial, relay, on);

            return ok;
        }

        /// <summary>
        /// Open a channel, wait, then close it — i.e. interrupt power for
        /// <paramref name="dwell"/>. Assumes the load is wired through the normally
        /// closed contacts, which is the safe arrangement: a USB dropout or a host
        /// reboot releases the relay and restores power rather than cutting it.
        ///
        /// Leave the dwell generous. A ZCU104's bulk capacitance holds the rails up
        /// for longer than feels reasonable, and a short interruption can produce a
        /// partial brown-out instead of a clean reset — which is worse than no reset,
        /// because the PS may come back with the PL in an undefined state.
        /// </summary>
        public static async Task<bool> PowerCycleAsync(
            string serial,
            int relay,
            TimeSpan? dwell = null,
            CancellationToken ct = default)
        {
            TimeSpan off = dwell ?? TimeSpan.FromSeconds(5);

            if (!Set(serial, relay, true))
                return false;

            try
            {
                await Task.Delay(off, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Never leave the load de-energised because the caller gave up.
                Set(serial, relay, false);
                throw;
            }

            return Set(serial, relay, false);
        }

        // ============================== Diagnostics ==============================

        /// <summary>
        /// Raw feature report as hex, for confirming <see cref="StateByteIndex"/> on an
        /// unfamiliar board: dump, toggle a relay, dump again, and see which byte moved.
        /// Returns null if the board can't be read.
        /// </summary>
        public static string? DumpFeatureReport(string serial)
        {
            lock (gate)
            {
                HidDevice? device = ResolveLocked(serial, out HidStream? stream);
                if (device == null || stream == null) return null;

                using (stream)
                {
                    byte[]? report = TryReadFeature(stream);
                    if (report == null) return null;

                    var sb = new StringBuilder();
                    for (int i = 0; i < report.Length; i++)
                        sb.Append(i).Append(':').Append(report[i].ToString("X2")).Append(' ');
                    return sb.ToString().TrimEnd();
                }
            }
        }

        // ============================== Internals ==============================

        /// <summary>
        /// Locate a board by serial and hand back an OPEN stream. Caller owns the
        /// stream. Must be called with <see cref="gate"/> held.
        /// </summary>
        private static HidDevice? ResolveLocked(string serial, out HidStream? stream)
        {
            stream = null;

            foreach (HidDevice device in CandidateDevices())
            {
                HidStream? candidate = null;
                try
                {
                    if (!device.TryOpen(out candidate)) continue;

                    byte[]? report = TryReadFeature(candidate);
                    if (report != null &&
                        string.Equals(ReadSerial(report), serial, StringComparison.Ordinal))
                    {
                        stream = candidate;
                        return device;
                    }
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                }

                candidate?.Dispose();
            }

            LastError ??= $"no relay board with serial {serial}";
            return null;
        }

        /// <summary>
        /// Read the identity/state feature report.
        ///
        /// The report id is tried as 0 and then 1. Stock usbrelay asks for report 1
        /// even though the descriptor declares no report ids, and which value works
        /// depends on the HID backend: Windows ignores it for id-less devices, hidraw
        /// does not. A report is accepted only if the serial field is printable ASCII,
        /// which rules out an all-zero reply from the wrong id.
        /// </summary>
        private static byte[]? TryReadFeature(HidStream stream)
        {
            foreach (byte reportId in new byte[] { 0x00, 0x01 })
            {
                try
                {
                    var buf = new byte[ReportLength];
                    buf[0] = reportId;
                    stream.GetFeature(buf);

                    if (LooksLikeSerial(buf)) return buf;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                }
            }
            return null;
        }

        private static bool LooksLikeSerial(byte[] report)
        {
            for (int i = SerialOffset; i < SerialOffset + SerialLength; i++)
            {
                byte c = report[i];
                // Trailing NULs are normal on boards with a short id.
                if (c == 0x00) continue;
                if (c < 0x20 || c > 0x7E) return false;
            }
            return true;
        }

        /// <summary>
        /// Send a 9-byte command. SetFeature first: the board has no OUT endpoint, so
        /// commands have to travel over the control pipe, and an output-report write is
        /// only translated into one on some backends. Write is the fallback for
        /// firmware that rejects SetFeature.
        /// </summary>
        private static bool SendCommand(HidStream stream, byte command, byte relay)
        {
            var buf = new byte[ReportLength];
            buf[0] = 0x00;      // report id
            buf[1] = command;
            buf[2] = relay;

            try
            {
                stream.SetFeature(buf);
                return true;
            }
            catch (Exception featureEx)
            {
                try
                {
                    stream.Write(buf);
                    return true;
                }
                catch (Exception writeEx)
                {
                    LastError = $"SetFeature failed ({featureEx.Message}); " +
                                $"Write also failed ({writeEx.Message})";
                    return false;
                }
            }
        }

        private static string ReadSerial(byte[] report)
        {
            var sb = new StringBuilder(SerialLength);
            for (int i = SerialOffset; i < SerialOffset + SerialLength; i++)
            {
                if (report[i] == 0x00) break;
                sb.Append((char)report[i]);
            }
            return sb.ToString();
        }

        private static string SafeProductName(HidDevice device)
        {
            try { return device.GetProductName() ?? ""; }
            catch { return ""; }   // throws on some platforms when the string is absent
        }

        /// <summary>"USBRelay4" -> 4. Falls back to 1 when the string says nothing.</summary>
        private static int ParseRelayCount(string product)
        {
            if (string.IsNullOrEmpty(product)) return 1;

            int end = product.Length;
            int start = end;
            while (start > 0 && char.IsDigit(product[start - 1])) start--;

            if (start == end) return 1;

            return int.TryParse(product.Substring(start, end - start),
                                NumberStyles.Integer, CultureInfo.InvariantCulture,
                                out int n) && n >= 1 && n <= 8
                ? n : 1;
        }
    }
}