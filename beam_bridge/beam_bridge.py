#!/usr/bin/env python3
"""Relay beam state between the NSRL dosimetry system and the ZCU server.

Every 100 ms this program:

  1. reads beam-active from the dosimetry system over RS-232,
  2. reports it to the ZCU and reads back what the ZCU wants (CMD_BEAMSERVICES),
  3. starts or stops an exposure over RS-232 when that request changes.

HOW AN EXPOSURE IS DRIVEN
-------------------------
One exposure spans the whole run, not one per spill. The first time the ZCU
raises its beam request the bridge calls start_exposure; every drop calls
stop_exposure, which pauses; every later raise calls resume_exposure, which
continues the same exposure toward the same quantity target. So a run that
sees twenty spills makes one start, twenty stops and nineteen resumes, and the
accumulated quantity carries across all of them.

Calling start_exposure on each raise instead would reset the accumulation every
spill and the requested fluence would never be reached. That is the reason the
three command names in this file are fixed rather than looked up from a list of
near-synonyms.

It is the missing half of the ZCU's beam loop. beam.rs keeps BEAM_ON (beam on
target) and BEAM_SERVICES (a 200 ms freshness stamp), and nothing inside the
server writes either — the only writer is the CMD_BEAMSERVICES handler. Without
this program running, InfoRsp.beam_active and InfoRsp.beam_services read false
forever, the Launcher shows FACILITY SERVICES offline, and an Expose task ends
about 400 ms after it starts having measured nothing.

WHY 100 ms. beam_services_active() in beam.rs returns false once the stamp is
more than 200 ms old. A 100 ms cycle sits at half that, so a single missed or
slow cycle still leaves the ZCU inside the window; a 200 ms cycle would sit
exactly on the edge and flicker. The loop below measures its own period and
reports overruns, because an overrun is the thing that breaks the guarantee.

READ THIS BEFORE RUNNING IT AT A BEAM LINE
------------------------------------------
This program issues real exposure commands. Its fail-safe direction is "stop":
a lost ZCU link, a lost serial link, an unhandled exception and Ctrl-C all stop
the exposure, because losing contact with the test controller means nothing is
left to stop it when the fluence target is reached. The one thing it will not do
is start an exposure on its own initiative — every start traces back to the ZCU
raising REQUEST_BEAM.

It refuses to run live unless the dosimetry system offers BOTH a start and a
stop command. Being able to start but not stop is not a degraded mode, it is a
hazard. Pass --dry-run to watch the decisions without issuing anything.

MUTING THE SERIAL LINE
----------------------
Set SERIAL_TRANSMIT = False below (or pass --no-serial-tx) and this program
becomes physically incapable of putting a byte on the serial line. The gate sits
on the single write in DosimetryLink.begin, which every request goes through, so
nothing slips past it — not the startup handshake, not the subscription, not an
exposure command.

That is stronger than --dry-run, which still talks to the dosimetry system to
read beam state and only withholds the exposure commands. Muted, the program
cannot subscribe either, so beam-active stays at its default of false and the
only thing still running is the ZCU half of the loop. Which is the point: it
lets you prove CMD_BEAMSERVICES answers and watch FACILITY SERVICES come up in
the Launcher without the facility cable being touched at all.

DO NOT LEAVE IT MUTED DURING A REAL RUN. The ZCU is still told beam_on=false
every cycle, and with the dosimetry actually exposing that is a lie that lands
in the record: beam-active flat while beam-request is high, exposure_time near
zero. Safe, because false is the safe direction, but wrong.

SERVER-SIDE PREREQUISITE
-----------------------
server.rs needs a dispatch arm for CMD_BEAMSERVICES (0x1A). The code and both
payload structs exist; the arm does not. Add:

    CMD_BEAMSERVICES => match parse_payload::<BeamServicesCmd>(&payload) {
        Ok(c) => crate::commands::beamservices_command(&mut stream, c),
        Err(e) => eprintln!("[!] {}: invalid beam services params: {}", peer, e),
    },

with a handler that stores c.beam_on into BEAM_ON, stamps BEAM_SERVICES with
Instant::now(), and replies BeamServicesRsp { beam_request: get_beam_request() }.
Until then, --fallback-info reads the request out of CMD_INFO instead. That
keeps the exposure half working for bench testing, but the ZCU still never
learns beam state, so Expose records stay empty.

Usage:
    python beam_bridge.py --port COM7 --zcu 192.168.1.10
    python beam_bridge.py --port /dev/ttyS0 --zcu 10.0.0.5 --dry-run
    python beam_bridge.py --port COM7 --zcu 10.0.0.5 --fallback-info
"""

import argparse
import json
import socket
import struct
import sys
import time
from typing import Any, Callable

import serial


# ===================== ZCU wire protocol (config.rs) =====================

SYNC_MARKER = 0xDEAD_BEEF
TERM_MARKER = 0xCAFE_BABE

CMD_INFO = 0x06
CMD_GETPOWERMODE = 0x0F
CMD_BEAMSERVICES = 0x1A

# 4 (SYNC) + 1 (CMD) + 4 (LENGTH); the server writes the same shape back
HEADER = struct.Struct('>IBI')
U32 = struct.Struct('>I')

# Mirrors MAX_PAYLOAD in server.rs. A reply claiming more than this is a
# desynced frame, not a big message.
MAX_PAYLOAD = 1 << 20


# ===================== dosimetry serial interface =====================

# How long a single read from the port may block. The reader below never asks
# for bytes that have not already arrived, so this only bounds the tail of a
# read that is already in progress.
PORT_TIMEOUT = 0.05

# How long the reader sleeps when the port has nothing waiting. Small enough
# that a reply is picked up promptly, large enough not to spin a core.
IDLE_SLEEP = 0.002

# Startup calls (status, list, subscribe) may block; the loop may not.
STARTUP_TIMEOUT = 30.0

# ===================== serial transmit gate =====================
#
# False makes this program incapable of transmitting on the serial line. See the
# module docstring. --no-serial-tx sets it from the command line; this constant
# is the default, so flip it here to make muting the normal behaviour.
SERIAL_TRANSMIT = False

# Error codes worth treating individually. Anything else is an ordinary error.
NOT_PERMITTED = -32001
UNKNOWN_SIGNAL = -32002
PREFLIGHT_FAILED = -32006

# Not a dosimetry code. Generated locally when a request is refused by the
# transmit gate, so a caller that was waiting on a reply fails immediately and
# with a reason, rather than sitting out its timeout waiting for an answer to a
# question that was never asked.
TX_DISABLED = -32099

# The three commands this program drives. Exact names, not candidate lists: the
# distinction between them is the whole model, so substituting a near-neighbour
# would change what the run measures rather than just how it is spelled.
#
#   start_exposure   opens an exposure and begins accumulating toward the
#                    requested quantity
#   stop_exposure    PAUSES it. The exposure stays open and the accumulated
#                    quantity survives
#   resume_exposure  continues that same exposure toward the same target
#
# That pause/resume pair is exactly the shape a spill-by-spill run needs, which
# is why a beam request dropping must use stop_exposure and nothing else.
# abort_exposure, end_exposure and cancel_exposure CLOSE the exposure out, so
# the next beam request would begin a fresh one counting from zero and the
# run's fluence target would never be reached — it would restart on every gap
# between spills. They are deliberately not in this file.
#
# status['commands_allowed'] still decides what is actually reachable; these are
# only the names looked for there.
START_COMMAND = 'start_exposure'
STOP_COMMAND = 'stop_exposure'
RESUME_COMMAND = 'resume_exposure'


class RpcError(Exception):
    """An error reply from the dosimetry serial interface."""

    def __init__(self, code: int, message: str, data: Any = None) -> None:
        self.code = code
        self.message = message
        self.data = data
        super().__init__(f'{message} ({code}): {data}')


class ZcuFault(Exception):
    """The ZCU link broke or sent something unparseable.

    Always fatal to the connection. The protocol has no request identifiers, so
    replies are matched by order alone; once the byte stream is at an unknown
    position there is no way to resynchronize without guessing, and the only
    safe move is to drop the socket and reconnect.
    """


class DosimetryLink:
    """One conversation with the dosimetry system over an open serial port.

    Extends the reference client with non-blocking requests. The loop cannot
    afford to block: start_exposure does not reply until the dosimetry has
    actually reached an exposing state, which can take seconds, and waiting for
    that would stall the 100 ms heartbeat and drop the ZCU out of its window.

    So commands issued from the loop go out through begin(), which returns
    immediately, and their replies are collected later by poll(). Requests and
    the data stream share one cable, so every read goes through here and gets
    routed by identifier.
    """

    def __init__(self, port: serial.Serial,
                 on_data: Callable[[dict], None] | None = None,
                 transmit: bool | None = None) -> None:
        self.port = port
        self.on_data = on_data
        # None means "whatever SERIAL_TRANSMIT says", read HERE rather than as a
        # default argument. A default of `transmit=SERIAL_TRANSMIT` would bind the
        # constant when the function is defined, i.e. at import, so setting
        # beam_bridge.SERIAL_TRANSMIT afterwards would silently do nothing.
        #
        # Also settable at runtime: flipping link.transmit mid-run takes effect on
        # the very next request, since the gate is read at the write and cached
        # nowhere.
        self.transmit = SERIAL_TRANSMIT if transmit is None else transmit
        self.suppressed = 0
        self._buffer = b''
        self._next_identifier = 1
        # identifier -> (method, callback). A callback takes (result, error),
        # exactly one of which is None.
        self._pending: dict[int, tuple[str, Callable | None]] = {}

    # ---- outbound -------------------------------------------------------

    def begin(self, method: str, params: dict | None = None,
              on_reply: Callable[[Any, RpcError | None], None] | None = None) -> int:
        """Send a request without waiting. The reply arrives via poll().

        This is the ONLY place anything is written to the port, which is why the
        transmit gate lives here: call() routes through begin(), and so does
        every exposure command, so one check covers all of them.

        When muted the request is not queued in _pending and the callback is
        invoked immediately with a TX_DISABLED error. Both halves matter — an
        unqueued identifier can never be matched by a reply that will not come,
        and failing the callback at once stops call() from blocking for its full
        timeout and stops Bridge from leaving exposure_in_flight stuck true.
        """
        identifier = self._next_identifier
        self._next_identifier += 1
        request: dict[str, Any] = {'jsonrpc': '2.0', 'id': identifier,
                                   'method': method}
        if params:
            request['params'] = params

        if not self.transmit:
            self.suppressed += 1
            print(f'  [serial] TX MUTED, not sending {method}')
            if on_reply is not None:
                on_reply(None, RpcError(TX_DISABLED, 'serial transmit disabled',
                                        {'method': method}))
            return identifier

        self.port.write(json.dumps(request).encode('ascii') + b'\n')
        self._pending[identifier] = (method, on_reply)
        return identifier

    def call(self, method: str, params: dict | None = None,
             timeout: float = STARTUP_TIMEOUT) -> Any:
        """Send a request and block for its reply. Startup only."""
        box: dict[str, Any] = {}

        def done(result: Any, error: RpcError | None) -> None:
            box['result'] = result
            box['error'] = error

        self.begin(method, params, done)
        deadline = time.monotonic() + timeout
        while 'result' not in box and 'error' not in box:
            remaining = deadline - time.monotonic()
            if remaining <= 0.0:
                raise TimeoutError(f'no reply to {method} in {timeout:g} sec')
            self.poll(remaining)
        if box.get('error') is not None:
            raise box['error']
        return box['result']

    # ---- inbound --------------------------------------------------------

    def poll(self, budget: float) -> int:
        """Read and dispatch whatever arrives, for at most `budget` seconds.

        Returns how many messages were handled.

        `budget` is a ceiling on waiting, not a duration to fill. Once the first
        message has been delivered this stops waiting and only takes what is
        already buffered, then returns. Filling the budget instead would make
        every blocking call pay its whole timeout even when the reply arrived
        at once, and would make the loop's drain step burn its 5 ms every cycle
        whether or not anything was there.
        """
        deadline = time.monotonic() + budget
        delivered = 0
        while True:
            wait = 0.0 if delivered else max(0.0, deadline - time.monotonic())
            message = self._read_message(wait)
            if message is None:
                return delivered
            self._dispatch(message)
            delivered += 1
            if time.monotonic() >= deadline:
                return delivered

    def _dispatch(self, message: dict) -> None:
        identifier = message.get('id')
        if identifier in self._pending:
            method, callback = self._pending.pop(identifier)
            if 'error' in message:
                err = message['error']
                error = RpcError(err.get('code', 0), err.get('message', ''),
                                 err.get('data'))
                if callback:
                    callback(None, error)
                else:
                    print(f'  [serial] {method} refused: {error}')
            else:
                if callback:
                    callback(message.get('result'), None)
            return

        if message.get('method') == 'data':
            if self.on_data is not None:
                self.on_data(message.get('params', {}))
            return

        # A late reply to a request we gave up on, or something added on the
        # NSRL end since. Neither is an error, but surface it for debugging.
        print(f'  [serial] ignoring unexpected message: {message}')

    def _read_message(self, timeout: float) -> dict | None:
        """Read one message, or None if none completed within the timeout."""
        deadline = time.monotonic() + timeout
        while True:
            newline = self._buffer.find(b'\n')
            if newline >= 0:
                line = self._buffer[:newline]
                self._buffer = self._buffer[newline + 1:]
                if line.strip():
                    try:
                        return json.loads(line.decode('ascii'))
                    except (UnicodeDecodeError, json.JSONDecodeError) as error:
                        # One corrupt line should not take down a beam bridge.
                        print(f'  [serial] unparseable line dropped: {error}')
                        continue
                continue
            waiting = self.port.in_waiting
            if waiting > 0:
                # Only ever ask for bytes that have already arrived. read(1) on
                # a dry port blocks for the full PORT_TIMEOUT, which at 50 ms
                # would eat half the cycle budget and break the 200 ms
                # BEAM_SERVICES guarantee this program exists to hold.
                self._buffer += self.port.read(waiting)
                continue
            remaining = deadline - time.monotonic()
            if remaining <= 0.0:
                return None
            time.sleep(min(remaining, IDLE_SLEEP))

    @property
    def pending_methods(self) -> list[str]:
        return [method for method, _ in self._pending.values()]


class ZcuLink:
    """Framed binary connection to the ZCU server.

    Packet layout, all framing integers big-endian:

        [SYNC u32][CMD u8][LENGTH u32][payload][TERM u32]

    Payloads are bincode with big-endian fixed-width integers, so a bool is one
    byte and there is no padding anywhere.
    """

    def __init__(self, host: str, port: int = 8080,
                 connect_timeout: float = 1.0,
                 reply_timeout: float = 0.060) -> None:
        self.host = host
        self.port = port
        self.connect_timeout = connect_timeout
        self.reply_timeout = reply_timeout
        self.sock: socket.socket | None = None

    @property
    def connected(self) -> bool:
        return self.sock is not None

    def connect(self) -> None:
        self.close()
        sock = socket.create_connection((self.host, self.port),
                                        timeout=self.connect_timeout)
        # The server builds each response into one buffer, but the capture path
        # writes header and body separately; don't let Nagle sit on a header.
        sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        self.sock = sock

    def close(self) -> None:
        if self.sock is not None:
            try:
                self.sock.close()
            except OSError:
                pass
            self.sock = None

    # ---- commands -------------------------------------------------------

    def beam_services(self, beam_on: bool) -> bool:
        """CMD_BEAMSERVICES: report beam-active, read back the beam request.

        This is the call that keeps BEAM_SERVICES fresh, so it goes out every
        cycle whether or not beam_on has changed.
        """
        payload = self._request(CMD_BEAMSERVICES, bytes([1 if beam_on else 0]))
        if len(payload) != 1:
            raise ZcuFault(
                f'BeamServicesRsp should be 1 byte, got {len(payload)} — '
                'the reply shape has changed')
        return payload[0] != 0

    def info(self) -> dict:
        """CMD_INFO: the whole status block. Used by the --fallback-info path."""
        return _parse_info(self._request(CMD_INFO, b''))

    def power_mode(self) -> dict:
        """CMD_GETPOWERMODE: three bools, the third being request_beam.

        Also stamps the server's POWER-services heartbeat, which is a different
        250 ms window from the beam one. Not used by the loop — the Launcher
        owns that heartbeat — but handy for a one-off check.
        """
        payload = self._request(CMD_GETPOWERMODE, b'')
        if len(payload) != 3:
            raise ZcuFault(f'GetPowerModeRsp should be 3 bytes, got {len(payload)}')
        return {'request_poweroff': payload[0] != 0,
                'request_reboot': payload[1] != 0,
                'request_beam': payload[2] != 0}

    # ---- framing --------------------------------------------------------

    def _request(self, cmd: int, payload: bytes) -> bytes:
        """Send one command and read its reply.

        Any ZcuFault out of here leaves the socket CLOSED. Worth keeping that
        invariant tight: the protocol has no request identifiers, so after a
        fault the byte stream sits at an unknown offset and the next command
        would read the previous one's reply. Closing here means no caller can
        forget to.
        """
        try:
            return self._exchange(cmd, payload)
        except ZcuFault:
            self.close()
            raise

    def _exchange(self, cmd: int, payload: bytes) -> bytes:
        if self.sock is None:
            raise ZcuFault('not connected')

        frame = (HEADER.pack(SYNC_MARKER, cmd, len(payload))
                 + payload + U32.pack(TERM_MARKER))
        try:
            self.sock.sendall(frame)
        except OSError as error:
            raise ZcuFault(f'send failed: {error}') from error

        deadline = time.monotonic() + self.reply_timeout
        sync, got_cmd, length = HEADER.unpack(self._recv_exact(9, deadline))

        if sync != SYNC_MARKER:
            raise ZcuFault(f'bad SYNC 0x{sync:08X}')
        if length > MAX_PAYLOAD:
            raise ZcuFault(f'reply claims {length} bytes')

        body = self._recv_exact(length, deadline) if length else b''
        term, = U32.unpack(self._recv_exact(4, deadline))
        if term != TERM_MARKER:
            raise ZcuFault(f'bad TERM 0x{term:08X}')

        if got_cmd != cmd:
            raise ZcuFault(
                f'asked for 0x{cmd:02X}, got a reply to 0x{got_cmd:02X} — '
                'the stream is one frame out of step')
        return body

    def _recv_exact(self, n: int, deadline: float) -> bytes:
        buf = b''
        while len(buf) < n:
            remaining = deadline - time.monotonic()
            if remaining <= 0.0:
                raise ZcuFault(
                    f'no reply within {self.reply_timeout * 1000:.0f} ms '
                    f'({len(buf)} of {n} bytes)')
            assert self.sock is not None
            self.sock.settimeout(remaining)
            try:
                chunk = self.sock.recv(n - len(buf))
            except socket.timeout as error:
                raise ZcuFault('reply timed out mid-frame') from error
            except OSError as error:
                raise ZcuFault(f'recv failed: {error}') from error
            if not chunk:
                raise ZcuFault('connection closed mid-frame')
            buf += chunk
        return buf


def _parse_info(payload: bytes) -> dict:
    """Decode types.rs InfoRsp.

    13 bytes with no PMIC reading, 38 with one. Field order is the declaration
    order in types.rs, which is NOT the order commands.rs writes the initializer
    in — serde ignores initializer order, so don't use the handler as the
    reference when checking this.
    """
    at = 0

    def flag() -> bool:
        nonlocal at
        if at >= len(payload):
            raise ZcuFault('InfoRsp truncated')
        value = payload[at] != 0
        at += 1
        return value

    info = {'beam_active': flag(), 'beam_request': flag(),
            'beam_services': flag()}

    # pmic: Option<PowerStats> — 1 tag byte, then 3 x f64 + 1 bool if Some
    if flag():
        if at + 25 > len(payload):
            raise ZcuFault('InfoRsp PowerStats truncated')
        volts, amps, watts = struct.unpack_from('>ddd', payload, at)
        at += 24
        info['pmic'] = {'volts': volts, 'amps': amps, 'watts': watts,
                        'power_derived': payload[at] != 0}
        at += 1
    else:
        info['pmic'] = None

    for name in ('power_services_provided', 'controller_calibrated',
                 'ui_clock', 'pl_clock', 'fpga_loaded',
                 'sdram_0', 'sdram_1', 'sdram_2', 'sdram_3'):
        info[name] = flag()

    if at != len(payload):
        raise ZcuFault(f'InfoRsp has {len(payload) - at} trailing bytes — '
                       'types.rs and this parser have diverged')
    return info


# ===================== the bridge =====================

class Bridge:
    """The 100 ms loop.

    Two state variables matter and they are deliberately separate:

      beam_on         what the dosimetry says is happening, mirrored from the
                      subscription so the loop never pays for a serial round trip
      commanded       what we last told the dosimetry to do, so exposure commands
                      are edge-triggered
      exposure_started whether the single run-long exposure has been opened yet,
                      which decides start_exposure versus resume_exposure

    Edge-triggering is not an optimisation. start_exposure is refused when an
    exposure is already running (NOT_PERMITTED) and does not reply until the
    dosimetry reaches the exposing state, so re-issuing it every 100 ms would
    both spam refusals and pile up in-flight requests.
    """

    def __init__(self, link: DosimetryLink, zcu: ZcuLink, *,
                 beam_signal: str, start_command: str | None,
                 stop_command: str | None, resume_command: str | None,
                 dry_run: bool,
                 fallback_info: bool, min_dwell: float,
                 reconnect_interval: float, transmit: bool) -> None:
        self.link = link
        self.zcu = zcu
        self.beam_signal = beam_signal
        self.start_command = start_command
        self.stop_command = stop_command
        self.resume_command = resume_command
        self.dry_run = dry_run
        self.transmit = transmit
        self.fallback_info = fallback_info
        self.min_dwell = min_dwell
        self.reconnect_interval = reconnect_interval

        self.beam_on = False
        self.beam_on_seen = False
        self.commanded: bool | None = None
        self.last_command_at = 0.0
        self.exposure_in_flight = False

        # Set once start_exposure has been accepted. From then on a rising beam
        # request resumes the open exposure rather than opening a new one, which
        # is what keeps the accumulated quantity running across the gaps between
        # spills instead of restarting on each.
        self.exposure_started = False

        # Set when resume_exposure is refused. Nothing automatic can fix that —
        # see command_exposure — so it is surfaced in the status line and left
        # for the operator.
        self.exposure_stalled = False

        self.cycles = 0
        self.overruns = 0
        self.zcu_faults = 0
        self.worst_period = 0.0
        self._last_cycle_at = 0.0
        self._next_connect_at = 0.0

    # ---- serial side ----------------------------------------------------

    def on_data(self, payload: dict) -> None:
        """Subscription callback. Mirrors beam-active into local state."""
        values = payload.get('values', {})
        errors = payload.get('errors', {})
        if self.beam_signal in errors:
            # The dosimetry could not produce the value — a device down, or
            # nothing published yet. Treat it as "not on target": this program
            # must never infer beam from an absent reading.
            if self.beam_on:
                print(f'  [serial] {self.beam_signal} unavailable '
                      f'({errors[self.beam_signal]}); assuming beam off')
            self.beam_on = False
            self.beam_on_seen = True
            return
        if self.beam_signal in values:
            self.beam_on = bool(values[self.beam_signal])
            self.beam_on_seen = True

    def command_exposure(self, want: bool) -> None:
        """Issue a start or stop, edge-triggered and rate-limited."""
        now = time.monotonic()

        if self.exposure_in_flight:
            # Still waiting on the previous command. Issuing another would
            # queue a contradictory one behind it.
            return
        if now - self.last_command_at < self.min_dwell:
            return

        # Rising edge: open the exposure the first time, continue it afterwards.
        # Falling edge: always stop_exposure, which pauses rather than closes —
        # see the command table at the top for why nothing else will do.
        if not want:
            method = self.stop_command
        elif self.exposure_started:
            # Falling back to start when resume is not served matches what main()
            # warns about at startup: it opens a NEW exposure with the
            # accumulated quantity reset, so the fluence target restarts on every
            # gap between spills. Wrong for a real run, but better than silently
            # refusing to re-enable beam at all, and the warning says so.
            method = self.resume_command or self.start_command
        else:
            method = self.start_command

        # The gate is checked before the missing-command case, because muted mode
        # never discovers any command names — there is nothing to look up, and
        # the decision is still worth logging. `commanded` is advanced so the
        # loop reports one line per edge instead of re-deciding the same edge
        # every dwell period. begin() is the real backstop; this only keeps the
        # output readable.
        if self.dry_run or not self.transmit:
            fallback = ('resume' if self.exposure_started else 'start') if want else 'stop'
            print(f'  [{"dry-run" if self.dry_run else "tx-muted"}] would '
                  f'{method or fallback} exposure')
            self.commanded = want
            if want:
                self.exposure_started = True
            self.last_command_at = now
            return

        if method is None:
            print(f'  [serial] no command available to turn beam '
                  f'{"on" if want else "off"}')
            # Stamp the clock so this does not repeat every cycle. Nothing will
            # change until the program is restarted against a dosimetry system
            # that serves the command, so once per dwell is already generous.
            self.last_command_at = now
            return

        def replied(result: Any, error: RpcError | None) -> None:
            # `want` and `method` are captured from the enclosing call, which is
            # safe because at most one exposure command is ever in flight.
            self.exposure_in_flight = False
            if error is None:
                state = (result or {}).get('state', '?')
                print(f'  [serial] {method} done, state is {state}')
                # Only now is the exposure definitely open, so only now does the
                # next rising edge become a resume. Latching it optimistically at
                # send time would turn a refused start into a resume against an
                # exposure that was never opened.
                if want:
                    self.exposure_started = True
                    self.exposure_stalled = False
                return
            # A refusal leaves `commanded` set to what we asked for, which is
            # wrong, so roll it back and let the next cycle retry after the
            # dwell. The common refusals are worth distinguishing.
            self.commanded = None
            if error.code == PREFLIGHT_FAILED:
                report = (error.data or {}).get('preflight', {})
                print(f'  [serial] {method} refused by preflight: '
                      f"{(error.data or {}).get('message', '')}")
                for failure in report.get('failures', []):
                    print(f"    {failure.get('id', '?')}: "
                          f"{failure.get('message', '')}")
            elif error.code == NOT_PERMITTED and method == self.start_command:
                # An exposure is already open: one an operator started by hand,
                # or one a previous run of this program left paused. Not a
                # failure — it only means the right command was resume.
                #
                # Treating this as goal-met, the way the generic branch below
                # does, would be wrong and quiet about it: nothing resumed, so
                # the request is high while the exposure stays paused and the
                # bridge believes beam is on. Record that the exposure exists
                # and clear `commanded` so the next cycle re-decides this same
                # edge, which will now pick resume.
                self.exposure_started = True
                self.commanded = None
                print(f'  [serial] {method} not permitted, an exposure is '
                      f'already open — will send '
                      f'{self.resume_command or "no resume command"} instead')
            elif error.code == NOT_PERMITTED and method == self.resume_command:
                # A resume the dosimetry will not accept is the one refusal that
                # is NOT benign. The usual cause is that the exposure is no
                # longer open — the quantity cutoff was reached and it closed
                # itself — so the run is finished whether or not the ZCU knows.
                #
                # Nothing automatic is right here. Falling back to
                # start_exposure would open a FRESH exposure with the
                # accumulation reset to zero, silently turning one run into two.
                # Retrying forever would just fill the log. So: say it once,
                # hold `commanded` so the edge is not re-decided, raise the
                # stalled flag for the status line, and leave it to the operator.
                self.commanded = want
                self.exposure_stalled = True
                print(f'  [serial] *** {method} REFUSED: {error.data}')
                print('  [serial] *** the exposure is probably closed (quantity '
                      'cutoff reached).')
                print('  [serial] *** beam will NOT resume until an operator '
                      'starts a new exposure.')
            elif error.code == NOT_PERMITTED:
                # Wrong state for the command, which for this program's purposes
                # means the goal was already met: a stop refused because nothing
                # is exposing has achieved "not exposing". Keep `commanded` set
                # so the next cycle does not retry forever. (The other sense of
                # NOT_PERMITTED, "this installation does not serve the command
                # at all", is screened out at startup against commands_allowed.)
                self.commanded = want
                print(f'  [serial] {method} not permitted, treating as '
                      f'already-{"exposing" if want else "stopped"}: '
                      f'{error.data}')
            else:
                print(f'  [serial] {method} failed: {error}')

        print(f'  [serial] {method}  (beam request -> {want})')
        self.exposure_in_flight = True
        self.commanded = want
        self.last_command_at = now
        self.link.begin(method, None, replied)

    def stop_now(self, why: str) -> None:
        """Unconditional stop, used on every fail-safe path.

        Bypasses the dwell and the edge check on purpose: this runs when the
        loop is giving up, and "we already commanded stop" is not a reason to
        skip a final stop when the previous one may itself have been lost.

        Note this PAUSES rather than closes: the exposure is left open on the
        dosimetry with its accumulated quantity intact. That is the right
        fail-safe — beam off, nothing discarded — but it does mean an operator
        has to close the exposure out, or restart this program, before the
        quantity stops being carried forward.
        """
        if self.stop_command is None or self.dry_run or not self.transmit:
            if not self.transmit:
                reason = 'serial transmit disabled'
            elif self.dry_run:
                reason = 'dry run'
            else:
                reason = 'no stop command'
            print(f'  stop skipped ({why}): {reason}')
            return
        print(f'  stopping exposure: {why}')
        try:
            self.link.call(self.stop_command, timeout=5.0)
        except (RpcError, TimeoutError, serial.SerialException) as error:
            print(f'  stop failed: {error}', file=sys.stderr)

    # ---- ZCU side -------------------------------------------------------

    def ask_zcu(self) -> bool | None:
        """One round trip. Returns the beam request, or None if the link is down.

        None is the fail-safe answer, not "no request" — the caller stops the
        exposure on it, because a ZCU we cannot reach is a ZCU that cannot tell
        us to stop when the fluence target lands.
        """
        if not self.zcu.connected:
            now = time.monotonic()
            if now < self._next_connect_at:
                return None
            self._next_connect_at = now + self.reconnect_interval
            try:
                self.zcu.connect()
                print(f'  [zcu] connected to {self.zcu.host}:{self.zcu.port}')
            except OSError as error:
                print(f'  [zcu] connect failed: {error}')
                return None

        try:
            if self.fallback_info:
                # Read-only path: the request is in InfoRsp, but nothing tells
                # the ZCU about beam, so BEAM_ON and BEAM_SERVICES stay false.
                return bool(self.zcu.info()['beam_request'])
            return self.zcu.beam_services(self.beam_on)
        except ZcuFault as error:
            # _request has already closed the socket; the next cycle reconnects
            # once the backoff expires.
            self.zcu_faults += 1
            print(f'  [zcu] {error}')
            return None

    # ---- the loop -------------------------------------------------------

    def run(self, period: float, status_every: float) -> None:
        print(f'\nbridging every {period * 1000:.0f} ms — Ctrl-C to stop\n')
        next_cycle = time.monotonic()
        next_status = next_cycle + status_every

        while True:
            now = time.monotonic()
            if self._last_cycle_at:
                self.worst_period = max(self.worst_period,
                                        now - self._last_cycle_at)
            self._last_cycle_at = now

            if now > next_cycle + period:
                # Missed a whole cycle. That is the thing this program exists to
                # not do, so count it rather than quietly catching up.
                self.overruns += 1
                next_cycle = now
            next_cycle += period

            # 1. Drain the serial stream. Small budget: the subscription is
            #    on_change, so most cycles have nothing waiting.
            self.link.poll(0.005)

            # 2. Report beam state, read back the request.
            request = self.ask_zcu()

            # 3. Act on it.
            if request is None:
                if self.commanded is not False:
                    self.command_exposure(False)
            elif request != self.commanded:
                self.command_exposure(request)

            self.cycles += 1

            if time.monotonic() >= next_status:
                next_status += status_every
                self.print_status()

            # 4. Sleep out the rest of the cycle, draining serial while we wait
            #    so a reply to an exposure command is picked up promptly.
            while True:
                remaining = next_cycle - time.monotonic()
                if remaining <= 0.0:
                    break
                self.link.poll(min(remaining, 0.020))

    def print_status(self) -> None:
        beam = 'ON' if self.beam_on else 'off'
        if not self.beam_on_seen:
            beam = 'unknown'
        want = {True: 'EXPOSE', False: 'hold', None: '-'}[self.commanded]
        link = 'up' if self.zcu.connected else 'DOWN'
        pending = ','.join(self.link.pending_methods) or '-'
        tx = 'on' if self.link.transmit else f'MUTED({self.link.suppressed})'
        if self.exposure_stalled:
            exposure = 'STALLED'
        elif self.exposure_started:
            exposure = 'open'
        else:
            exposure = 'unopened'
        # worst_period is the number that matters: beam.rs drops BEAM_SERVICES
        # once its stamp is 200 ms old, so anything approaching that means the
        # ZCU has been reading the facility as offline between cycles.
        print(f'  beam={beam:<7} commanded={want:<6} exposure={exposure:<8} '
              f'zcu={link:<4} tx={tx:<10} '
              f'cycles={self.cycles} worst={self.worst_period * 1000:.0f}ms '
              f'overruns={self.overruns} faults={self.zcu_faults} '
              f'pending={pending}')


# ===================== startup =====================

def describe(link: DosimetryLink) -> dict:
    """Ask the dosimetry system about itself. Worth doing once at startup:
    which commands are served and how fast a subscription may run are set on the
    NSRL end, so they differ from trip to trip."""
    started = time.monotonic()
    link.call('ping', {'echo': 'bridge'})
    round_trip = (time.monotonic() - started) * 1000.0

    status = link.call('status')
    print(f'dosimetry {status["version"]} (round trip {round_trip:.0f} ms)')
    print(f'  controls network: '
          f'{"up" if status["controls_connected"] else "down"}')
    print(f'  commands served:  '
          f'{", ".join(status["commands_allowed"]) or "none"}')
    print(f'  fastest stream:   {status["min_interval"]:g} s')
    return status


def pick_command(allowed: list[str], name: str) -> str | None:
    """The command if this installation serves it, otherwise None."""
    return name if name in allowed else None


def subscribe_beam(link: DosimetryLink, signal: str) -> int:
    """Subscribe on_change to the beam signal.

    on_change rather than an interval because the exposure state arrives the
    moment the dosimetry changes it, rather than up to an interval later. The
    serial data stream coalesces updates, so a value that moves on every spill
    cannot outrun the cable.
    """
    available = [s['name'] for s in link.call('list')['signals']]
    if signal not in available:
        raise SystemExit(
            f'the dosimetry system does not publish "{signal}".\n'
            f'available: {", ".join(available)}\n'
            f'pass --beam-signal with the right name.')

    reply = link.call('subscribe', {'names': [signal], 'on_change': True})
    print(f'  subscribed to {signal} (subscription {reply["subscription"]})')

    # Seed the mirror so the first cycle does not act on a default.
    link.call('read', {'names': [signal], 'fresh': True})
    return reply['subscription']


def check_zcu(zcu: ZcuLink, fallback_info: bool) -> None:
    """Connect once and prove the command we intend to use actually answers."""
    zcu.connect()
    info = zcu.info()
    print(f'ZCU at {zcu.host}:{zcu.port}')
    print(f'  calibrated={info["controller_calibrated"]} '
          f'fpga_loaded={info["fpga_loaded"]} '
          f'beam_services={info["beam_services"]} '
          f'beam_request={info["beam_request"]}')

    if fallback_info:
        print('  using CMD_INFO (read-only): the ZCU will NOT learn beam state,\n'
              '  so beam_active stays false and Expose records stay empty.')
        return

    try:
        request = zcu.beam_services(False)
    except ZcuFault as error:
        raise SystemExit(
            f'CMD_BEAMSERVICES (0x1A) did not answer: {error}\n'
            '\n'
            'The code is declared in config.rs and both payload structs are in\n'
            'types.rs, but server.rs has no dispatch arm for it, so the server\n'
            'logs "unknown CMD 0x1A" and sends nothing back. Add the arm (see\n'
            'the module docstring) and rebuild, or run with --fallback-info to\n'
            'bring up the exposure half against CMD_INFO in the meantime.'
        ) from error

    print(f'  CMD_BEAMSERVICES answered, beam_request={request}')


def parse_arguments(argv: list[str] | None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=__doc__.splitlines()[0],
        formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('-p', '--port', default='COM7',
                        help='dosimetry serial device (default: %(default)s)')
    parser.add_argument('-b', '--baud', type=int, default=115200,
                        help='baud rate (default: %(default)s)')
    parser.add_argument('-z', '--zcu', required=True,
                        help='ZCU server host or IP')
    parser.add_argument('--zcu-port', type=int, default=8080,
                        help='ZCU server port (default: %(default)s)')
    parser.add_argument('--period', type=float, default=0.100,
                        help='cycle period in seconds. The server drops '
                             'BEAM_SERVICES after 200 ms, so do not raise this '
                             'above 0.1 (default: %(default)s)')
    parser.add_argument('--beam-signal', default='beam_on',
                        help='dosimetry signal carrying beam-active '
                             '(default: %(default)s)')
    parser.add_argument('--min-dwell', type=float, default=1.0,
                        help='minimum seconds between exposure commands, so a '
                             'flapping request cannot machine-gun the facility '
                             '(default: %(default)s)')
    parser.add_argument('--reconnect-interval', type=float, default=2.0,
                        help='seconds between ZCU reconnect attempts '
                             '(default: %(default)s)')
    parser.add_argument('--status-every', type=float, default=5.0,
                        help='seconds between status lines (default: %(default)s)')
    parser.add_argument('--dry-run', action='store_true',
                        help='log the decisions but issue no exposure commands')
    parser.add_argument('--no-serial-tx', action='store_true',
                        help='transmit NOTHING on the serial line. Stronger than '
                             '--dry-run: no handshake, no subscription, no '
                             'commands, so beam-active stays false and only the '
                             'ZCU half of the loop runs. For proving the ZCU '
                             'side without touching the facility.')
    parser.add_argument('--fallback-info', action='store_true',
                        help='read the beam request from CMD_INFO instead of '
                             'CMD_BEAMSERVICES. Use while server.rs still '
                             'lacks the 0x1A dispatch arm; the ZCU learns '
                             'nothing about beam in this mode.')
    parser.add_argument('--allow-no-stop', action='store_true',
                        help='run even if the dosimetry offers no stop command. '
                             'Starting without being able to stop relies '
                             'entirely on the quantity cutoff.')
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    arguments = parse_arguments(argv)

    if arguments.period > 0.2:
        print(f'refusing a {arguments.period * 1000:.0f} ms period: beam.rs '
              'drops BEAM_SERVICES after 200 ms, so the ZCU would read the '
              'facility as offline between cycles.', file=sys.stderr)
        return 2

    # 8 data bits, no parity, 1 stop bit, no flow control.
    try:
        port = serial.Serial(port=arguments.port, baudrate=arguments.baud,
                             bytesize=serial.EIGHTBITS,
                             parity=serial.PARITY_NONE,
                             stopbits=serial.STOPBITS_ONE,
                             timeout=PORT_TIMEOUT)
    except serial.SerialException as error:
        print(f'could not open {arguments.port}: {error}', file=sys.stderr)
        return 2

    # The constant is the default and the flag can only tighten it: there is no
    # way to turn transmit back ON from the command line against a source that
    # has it off, which is the right asymmetry for something that gates a beam
    # line.
    transmit = SERIAL_TRANSMIT and not arguments.no_serial_tx

    zcu = ZcuLink(arguments.zcu, arguments.zcu_port)
    bridge: Bridge | None = None
    subscription: int | None = None
    link = DosimetryLink(port, transmit=transmit)

    try:
        if transmit:
            status = describe(link)
            allowed = status.get('commands_allowed', [])
            start_command = pick_command(allowed, START_COMMAND)
            stop_command = pick_command(allowed, STOP_COMMAND)
            resume_command = pick_command(allowed, RESUME_COMMAND)
        else:
            # Every startup call needs the cable, so there is nothing to ask and
            # nothing to discover. Say exactly what is and is not running, because
            # from the ZCU's side a muted bridge looks identical to a working one.
            print('SERIAL TRANSMIT DISABLED')
            print('  no handshake, no subscription, no exposure commands')
            print('  beam-active will stay false for the whole run')
            print('  the ZCU is still told beam_on=false every cycle, so '
                  'FACILITY SERVICES')
            print('  will read ONLINE even though nothing is measuring beam')
            allowed = []
            start_command = stop_command = resume_command = None

        if not arguments.dry_run and transmit:
            if start_command is None:
                print('the dosimetry system serves no exposure start command; '
                      'nothing to drive. Use --dry-run to watch the decisions.',
                      file=sys.stderr)
                return 2
            if stop_command is None and not arguments.allow_no_stop:
                print(f'the dosimetry system does not serve {STOP_COMMAND}.\n'
                      'Refusing to run: starting an exposure that this program '
                      'cannot stop puts the whole cutoff on the dosimetry '
                      'system alone. Pass --allow-no-stop if that is genuinely '
                      'the intended arrangement.', file=sys.stderr)
                return 2
            if resume_command is None:
                # Not fatal, but it changes what the run measures, so it does not
                # get to be a footnote. Without resume the only way to re-enable
                # beam after a stop is to open a new exposure, which resets the
                # accumulated quantity — so the requested fluence restarts on
                # every gap between spills and is never reached.
                print(f'\nWARNING: {RESUME_COMMAND} is not served by this '
                      f'dosimetry system.\n'
                      f'  Every beam request after the first will call '
                      f'{START_COMMAND} instead, which opens a NEW exposure\n'
                      f'  with the accumulated quantity reset to zero. A run '
                      f'spanning several spills will not reach\n'
                      f'  its fluence target. Check the command list before '
                      f'relying on the numbers.\n', file=sys.stderr)

        print(f'  start={start_command or "-"}  stop={stop_command or "-"}  '
              f'resume={resume_command or "-"}')
        check_zcu(zcu, arguments.fallback_info)

        if transmit:
            subscription = subscribe_beam(link, arguments.beam_signal)
        else:
            # Unsolicited stream data is still parsed if any shows up — a
            # subscription left running by another client would feed us — but
            # nothing here asked for one, so do not expect any.
            print('  no subscription (transmit disabled); beam-active stays at '
                  'its default')

        bridge = Bridge(link, zcu,
                        beam_signal=arguments.beam_signal,
                        start_command=start_command,
                        stop_command=stop_command,
                        resume_command=resume_command,
                        dry_run=arguments.dry_run,
                        fallback_info=arguments.fallback_info,
                        min_dwell=arguments.min_dwell,
                        reconnect_interval=arguments.reconnect_interval,
                        transmit=transmit)
        link.on_data = bridge.on_data
        bridge.run(arguments.period, arguments.status_every)

    except KeyboardInterrupt:
        print('\ninterrupted')
    except (RpcError, TimeoutError) as error:
        print(f'\ndosimetry did not cooperate: {error}', file=sys.stderr)
        return 1
    except serial.SerialException as error:
        print(f'\nserial link lost: {error}', file=sys.stderr)
        return 1
    except SystemExit:
        raise
    except Exception as error:                       # noqa: BLE001
        # Anything unhandled still has to leave the beam off, which is why this
        # catch-all exists at all.
        print(f'\nunhandled error: {error!r}', file=sys.stderr)
        return 1
    finally:
        if bridge is not None:
            bridge.stop_now('shutting down')
            bridge.print_status()
        if subscription is not None:
            try:
                link.call('unsubscribe', {'subscription': subscription},
                          timeout=2.0)
            except (RpcError, TimeoutError, serial.SerialException):
                # The dosimetry drops a client's subscriptions when the port is
                # reopened, so leaving one behind is untidy rather than harmful.
                pass
        zcu.close()
        port.close()

    return 0


if __name__ == '__main__':
    sys.exit(main())