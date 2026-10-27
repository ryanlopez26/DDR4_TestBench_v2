//! gpio.rs — EMIO GPIO channels for the ZCU104 DDR4 tester.
//!
//! Exposes eleven EMIO lines on the ZynqMP PS GPIO controller (gpiochip1,
//! `zynqmp_gpio`). MIO lines occupy 0..=77, so EMIO bit N lands at line
//! 78 + N. Confirmed on this board with `gpioget -c gpiochip1 <line>`.
//!
//!   EMIO bit  |  gpiochip1 line  |  Direction |  Channel
//!   ----------|------------------|------------|---------------------------
//!       0     |        78        |   input    | Beam Active
//!       1     |        79        |   input    | Beam Possible
//!       2     |        80        |   input    | Calibration Signal
//!       3     |        81        |   input    | UI Clock Signal
//!       4     |        82        |   input    | PL Clock Signal
//!       5     |        83        |   input    | FPGA Loaded Status
//!       6     |        84        |   input    | SDRAM #0 Selected
//!       7     |        85        |   input    | SDRAM #1 Selected
//!       8     |        86        |   input    | SDRAM #2 Selected
//!       9     |        87        |   input    | SDRAM #3 Selected
//!      10     |        88        |   OUTPUT   | Clamp Request
//!
//! Bits 0..=9 are inputs. Bit 10 (Clamp Request) is the only output in this
//! module and the only line anything here ever drives — see
//! `set_clamp_request`.
//!
//! If a kernel update shifts the EMIO base away from 78, adjust the
//! `*_LINE_OFFSET` constants below (or override via the per-channel env
//! vars documented on `init`).
//!
//! Usage:
//!     gpio::init()?;                                    // once, at startup
//!     let active   = gpio::get_beam_active();             // hot path
//!     let possible = gpio::get_beam_possible();           // hot path
//!     let cal      = gpio::get_calibration_signal();      // hot path
//!     let ui_clk   = gpio::get_ui_clock_signal();         // hot path
//!     let pl_clk   = gpio::get_pl_clock_signal();         // hot path
//!     let loaded   = gpio::get_fpga_loaded_status();      // hot path
//!     let ram      = gpio::get_selected_sdram();          // Option<usize>
//!     gpio::set_clamp_request(true)?;                     // drive the output
//!
//! ## The `hardware` feature
//!
//! Real GPIO access below (the `backend` module gated on
//! `feature = "hardware"`) depends on the `gpio-cdev` crate, which talks
//! to `/dev/gpiochipN` character devices - a Linux-only API. That's fine
//! on the board, but it means this file can't even compile on Windows/
//! macOS dev machines, independent of `SIMULATION_MODE`'s runtime value,
//! since Rust still compiles a module's code even if nothing at runtime
//! ever calls into it.
//!
//! `hardware` is on by default (see Cargo.toml), so nothing changes for
//! the existing board build. Building with `--no-default-features`
//! (e.g. `cargo build --no-default-features` on a Windows/Mac box) swaps
//! in the second `backend` module below: a pure-software stand-in with
//! the exact same public functions, so `main.rs` and `commands.rs` don't
//! need to know or care which one is compiled in.
//!
//! Depends on the `gpio-cdev` crate (modern character-device interface;
//! the legacy /sys/class/gpio sysfs path is deprecated) when `hardware`
//! is enabled. In Cargo.toml:
//!     gpio-cdev = { version = "0.6", optional = true }
//!
//! On the Yocto side, make sure the gpio chardev nodes exist in the image
//! (they do by default with the xlnx kernel) — no extra IMAGE_INSTALL needed
//! for this crate itself, though `libgpiod-tools` is handy for `gpioinfo`.

/// Number of SDRAM select inputs (EMIO bits 6..=9).
pub const SDRAM_SELECT_COUNT: usize = 4;

/// Errors surfaced from this module.
#[derive(Debug)]
pub enum GpioError {
    /// init() was already called successfully; the lines are already held.
    AlreadyInitialized,
    /// A read was attempted before init() acquired the lines.
    //NotInitialized,
    /// Underlying gpio-cdev failure (chip open, line request, etc.).
    /// Only constructible when the `hardware` feature is enabled - the
    /// simulated backend never fails this way.
    #[cfg(feature = "hardware")]
    Cdev(gpio_cdev::Error),
}

impl std::fmt::Display for GpioError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            GpioError::AlreadyInitialized => write!(f, "GPIO already initialized"),
            //GpioError::NotInitialized => write!(f, "GPIO not initialized; call init() first"),
            #[cfg(feature = "hardware")]
            GpioError::Cdev(e) => write!(f, "gpio-cdev error: {e}"),
        }
    }
}

impl std::error::Error for GpioError {}

#[cfg(feature = "hardware")]
impl From<gpio_cdev::Error> for GpioError {
    fn from(e: gpio_cdev::Error) -> Self {
        GpioError::Cdev(e)
    }
}

// =========================================================================
// Real backend: gpio-cdev against /dev/gpiochip1. Requires `hardware`.
// =========================================================================
#[cfg(feature = "hardware")]
mod backend {
    use super::{GpioError, SDRAM_SELECT_COUNT};
    use std::sync::atomic::{AtomicBool, Ordering};
    use std::sync::OnceLock;

    use gpio_cdev::{Chip, LineHandle, LineRequestFlags};

    /// GPIO chip device exposing the ZynqMP PS GPIO controller.
    /// On this board gpiochip1 is `zynqmp_gpio` (174 lines = 78 MIO + 96 EMIO);
    /// gpiochip0 is the 4-line firmware GPIO and gpiochip2 is an I2C expander.
    const GPIO_CHIP: &str = "/dev/gpiochip1";

    /// Line offsets within the chip for each EMIO channel. MIO 0..=77, then
    /// EMIO begins at 78. Verify with `gpioinfo`.
    const BEAM_ACTIVE_LINE_OFFSET: u32 = 78;
    const BEAM_POSSIBLE_LINE_OFFSET: u32 = 79;
    const CALIBRATION_LINE_OFFSET: u32 = 80;
    const UI_CLOCK_LINE_OFFSET: u32 = 81;
    const PL_CLOCK_LINE_OFFSET: u32 = 82;
    const FPGA_LOADED_LINE_OFFSET: u32 = 83;
    const SDRAM_SELECT_LINE_OFFSETS: [u32; SDRAM_SELECT_COUNT] = [84, 85, 86, 87];
    const CLAMP_REQUEST_LINE_OFFSET: u32 = 88;

    /// Consumer labels that show up in `gpioinfo` so it's clear who holds each line.
    const BEAM_ACTIVE_CONSUMER: &str = "ddr4-tester-beam-active";
    const BEAM_POSSIBLE_CONSUMER: &str = "ddr4-tester-beam-possible";
    const CALIBRATION_CONSUMER: &str = "ddr4-tester-calibration";
    const UI_CLOCK_CONSUMER: &str = "ddr4-tester-ui-clock";
    const PL_CLOCK_CONSUMER: &str = "ddr4-tester-pl-clock";
    const FPGA_LOADED_CONSUMER: &str = "ddr4-tester-fpga-loaded";
    const SDRAM_SELECT_CONSUMERS: [&str; SDRAM_SELECT_COUNT] = [
        "ddr4-tester-sdram-sel0",
        "ddr4-tester-sdram-sel1",
        "ddr4-tester-sdram-sel2",
        "ddr4-tester-sdram-sel3",
    ];
    const CLAMP_REQUEST_CONSUMER: &str = "ddr4-tester-clamp-request";

    /// Environment variables that override each SDRAM select line offset.
    const SDRAM_SELECT_ENV_VARS: [&str; SDRAM_SELECT_COUNT] = [
        "SDRAM_SELECT0_GPIO_LINE",
        "SDRAM_SELECT1_GPIO_LINE",
        "SDRAM_SELECT2_GPIO_LINE",
        "SDRAM_SELECT3_GPIO_LINE",
    ];

    /// All eleven line handles, initialized once by `init()`.
    ///
    /// OnceLock gives a lock-free read after init, which matters because the
    /// ten inputs are polled very often on the hot path.
    static GPIO_LINES: OnceLock<GpioLines> = OnceLock::new();

    /// Shadow copy of the last value written to the Clamp Request output.
    ///
    /// Lets `get_clamp_request()` answer without an ioctl, and keeps the
    /// simulated path (SIMULATION_MODE = true, where no line is ever driven)
    /// returning something consistent with what the caller asked for.
    static CLAMP_ASSERTED: AtomicBool = AtomicBool::new(false);

    struct GpioLines {
        beam_active: LineHandle,
        beam_possible: LineHandle,
        calibration: LineHandle,
        ui_clock: LineHandle,
        pl_clock: LineHandle,
        fpga_loaded: LineHandle,
        sdram_select: [LineHandle; SDRAM_SELECT_COUNT],
        clamp_request: LineHandle,
    }

    /// Open the GPIO chip and acquire all eleven channels, holding the handles
    /// open for the lifetime of the process.
    ///
    /// Call exactly once at startup, before any other function in this module.
    /// Holding the lines open (rather than re-requesting them per call) is what
    /// keeps the getters cheap on the hot path: a single ioctl per poll, no
    /// open/request/close churn.
    ///
    /// EMIO bits 0..=9 are requested as inputs. Bit 10 (Clamp Request) is
    /// requested as an output with an initial value of 0, i.e. de-asserted —
    /// note that the request itself starts driving the line at init, so if the
    /// PL side treats the clamp as active-low you want to invert in fabric (or
    /// change the default below) rather than inheriting an asserted clamp.
    ///
    /// Each line offset defaults to the `*_LINE_OFFSET` constant above. To
    /// override any of them without recompiling — e.g. if `gpioinfo` shows a
    /// different base on your kernel — set the corresponding environment
    /// variable:
    ///     BEAM_ACTIVE_GPIO_LINE, BEAM_POSSIBLE_GPIO_LINE,
    ///     CALIBRATION_GPIO_LINE, UI_CLOCK_GPIO_LINE, PL_CLOCK_GPIO_LINE,
    ///     FPGA_LOADED_GPIO_LINE, SDRAM_SELECT0_GPIO_LINE ..
    ///     SDRAM_SELECT3_GPIO_LINE, CLAMP_REQUEST_GPIO_LINE
    pub fn init() -> Result<(), GpioError> {
        if GPIO_LINES.get().is_some() {
            return Err(GpioError::AlreadyInitialized);
        }

        let mut chip = Chip::new(GPIO_CHIP)?;

        let beam_active_offset =
            line_offset_from_env("BEAM_ACTIVE_GPIO_LINE", BEAM_ACTIVE_LINE_OFFSET);
        let beam_possible_offset =
            line_offset_from_env("BEAM_POSSIBLE_GPIO_LINE", BEAM_POSSIBLE_LINE_OFFSET);
        let calibration_offset =
            line_offset_from_env("CALIBRATION_GPIO_LINE", CALIBRATION_LINE_OFFSET);
        let ui_clock_offset = line_offset_from_env("UI_CLOCK_GPIO_LINE", UI_CLOCK_LINE_OFFSET);
        let pl_clock_offset = line_offset_from_env("PL_CLOCK_GPIO_LINE", PL_CLOCK_LINE_OFFSET);
        let fpga_loaded_offset =
            line_offset_from_env("FPGA_LOADED_GPIO_LINE", FPGA_LOADED_LINE_OFFSET);
        let clamp_request_offset =
            line_offset_from_env("CLAMP_REQUEST_GPIO_LINE", CLAMP_REQUEST_LINE_OFFSET);

        let beam_active = chip
            .get_line(beam_active_offset)?
            .request(LineRequestFlags::INPUT, 0, BEAM_ACTIVE_CONSUMER)?;
        let beam_possible = chip
            .get_line(beam_possible_offset)?
            .request(LineRequestFlags::INPUT, 0, BEAM_POSSIBLE_CONSUMER)?;
        let calibration = chip
            .get_line(calibration_offset)?
            .request(LineRequestFlags::INPUT, 0, CALIBRATION_CONSUMER)?;
        let ui_clock = chip
            .get_line(ui_clock_offset)?
            .request(LineRequestFlags::INPUT, 0, UI_CLOCK_CONSUMER)?;
        let pl_clock = chip
            .get_line(pl_clock_offset)?
            .request(LineRequestFlags::INPUT, 0, PL_CLOCK_CONSUMER)?;
        let fpga_loaded = chip
            .get_line(fpga_loaded_offset)?
            .request(LineRequestFlags::INPUT, 0, FPGA_LOADED_CONSUMER)?;

        // The four SDRAM select inputs are identical apart from their offset
        // and label, so request them in a loop and collect into a fixed array.
        let mut selects = Vec::with_capacity(SDRAM_SELECT_COUNT);
        for i in 0..SDRAM_SELECT_COUNT {
            let offset = line_offset_from_env(SDRAM_SELECT_ENV_VARS[i], SDRAM_SELECT_LINE_OFFSETS[i]);
            selects.push(
                chip.get_line(offset)?
                    .request(LineRequestFlags::INPUT, 0, SDRAM_SELECT_CONSUMERS[i])?,
            );
        }
        let sdram_select: [LineHandle; SDRAM_SELECT_COUNT] = match selects.try_into() {
            Ok(arr) => arr,
            // try_into on a Vec can only fail on a length mismatch, and the
            // loop above pushes exactly SDRAM_SELECT_COUNT handles.
            Err(_) => unreachable!("expected exactly {SDRAM_SELECT_COUNT} SDRAM select handles"),
        };

        // Output: initial value 0 = clamp de-asserted.
        let clamp_request = chip
            .get_line(clamp_request_offset)?
            .request(LineRequestFlags::OUTPUT, 0, CLAMP_REQUEST_CONSUMER)?;
        CLAMP_ASSERTED.store(false, Ordering::SeqCst);

        let lines = GpioLines {
            beam_active,
            beam_possible,
            calibration,
            ui_clock,
            pl_clock,
            fpga_loaded,
            sdram_select,
            clamp_request,
        };

        // If two threads race init(), only the first store wins; the loser's
        // handles are dropped (releasing their requests) and we report success
        // either way, since the lines are now held.
        let _ = GPIO_LINES.set(lines);
        Ok(())
    }

    fn line_offset_from_env(var: &str, default: u32) -> u32 {
        std::env::var(var)
            .ok()
            .and_then(|s| s.parse::<u32>().ok())
            .unwrap_or(default)
    }

    fn lines() -> &'static GpioLines {
        GPIO_LINES
            .get()
            .expect("gpio::init() must be called before using any gpio:: function")
    }

    // fn try_lines() -> Result<&'static GpioLines, GpioError> {
    //     GPIO_LINES.get().ok_or(GpioError::NotInitialized)
    // }

    // -------------------------------------------------------------------
    // Beam Active (input, EMIO bit 0 / line 78)
    // -------------------------------------------------------------------

    /// Return `true` if the Beam Active GPIO reads high (beam is on target
    /// right now), `false` if low.
    ///
    /// Hot path: designed to be polled very often. After `init()`, this is a
    /// single `get_value` ioctl on an already-open line.
    ///
    /// # Panics
    /// Panics if called before a successful `init()`. This is deliberate: a
    /// poll loop running against an uninitialized line is a programming error,
    /// and a panic surfaces it immediately rather than silently returning a
    /// bogus `false`.
    pub fn get_beam_active() -> bool {
        if crate::config::SIMULATION_MODE {
            return false;
        }

        // A read failure here is unexpected for an already-acquired input line;
        // treat it as "not high" rather than panicking on the hot path. Adjust
        // to taste if you'd rather propagate the error.
        matches!(lines().beam_active.get_value(), Ok(1))
    }

    // -------------------------------------------------------------------
    // Beam Possible (input, EMIO bit 1 / line 79)
    // -------------------------------------------------------------------

    /// Return `true` if the Beam Possible GPIO reads high, i.e. the facility
    /// is in a state where beam could be delivered (shutters/interlocks permit
    /// it), whether or not beam is actually on target — for that, see
    /// `get_beam_active`.
    ///
    /// Hot path: designed to be polled very often. After `init()`, this is a
    /// single `get_value` ioctl on an already-open line.
    ///
    /// # Panics
    /// Panics if called before a successful `init()`. See `get_beam_active`
    /// for rationale.
    pub fn get_beam_possible() -> bool {
        // Mirrors get_beam_active(): in simulation there is no facility, so
        // neither beam signal is ever asserted. Flip this to `true` if your
        // sim runs need to exercise an arm-while-beam-permitted path.
        if crate::config::SIMULATION_MODE {
            return false;
        }

        matches!(lines().beam_possible.get_value(), Ok(1))
    }

    // -------------------------------------------------------------------
    // Calibration Signal (input, EMIO bit 2 / line 80)
    // -------------------------------------------------------------------

    /// Return `true` if the Calibration Signal GPIO reads high (MIG
    /// calibration complete), `false` if low.
    ///
    /// Hot path: designed to be polled very often. After `init()`, this is a
    /// single `get_value` ioctl on an already-open line.
    ///
    /// # Panics
    /// Panics if called before a successful `init()`. See `get_beam_active`
    /// for rationale.
    pub fn get_calibration_signal() -> bool {
        if crate::config::SIMULATION_MODE {
            return true;
        }

        matches!(lines().calibration.get_value(), Ok(1))
    }

    // -------------------------------------------------------------------
    // UI Clock Signal (input, EMIO bit 3 / line 81)
    // -------------------------------------------------------------------

    /// Return `true` if the UI Clock Signal GPIO reads high.
    ///
    /// Intended as a presence/heartbeat check on `c0_ddr4_ui_clk` (e.g. via a
    /// clock-detector IP that latches high once toggling is observed), not as
    /// a way to sample the clock's actual waveform over a slow GPIO poll.
    ///
    /// Hot path: designed to be polled very often. After `init()`, this is a
    /// single `get_value` ioctl on an already-open line.
    ///
    /// # Panics
    /// Panics if called before a successful `init()`. See `get_beam_active`
    /// for rationale.
    pub fn get_ui_clock_signal() -> bool {
        if crate::config::SIMULATION_MODE {
            return true;
        }

        matches!(lines().ui_clock.get_value(), Ok(1))
    }

    // -------------------------------------------------------------------
    // PL Clock Signal (input, EMIO bit 4 / line 82)
    // -------------------------------------------------------------------

    /// Return `true` if the PL Clock Signal GPIO reads high.
    ///
    /// Same intent as `get_ui_clock_signal`, but for `pl_clk0` — useful for
    /// confirming the PL fabric clock is present independent of whether the
    /// MIG UI clock has come up.
    ///
    /// Hot path: designed to be polled very often. After `init()`, this is a
    /// single `get_value` ioctl on an already-open line.
    ///
    /// # Panics
    /// Panics if called before a successful `init()`. See `get_beam_active`
    /// for rationale.
    pub fn get_pl_clock_signal() -> bool {
        if crate::config::SIMULATION_MODE {
            return true;
        }

        matches!(lines().pl_clock.get_value(), Ok(1))
    }

    // -------------------------------------------------------------------
    // FPGA Loaded Status (input, EMIO bit 5 / line 83)
    // -------------------------------------------------------------------

    /// Return `true` if the FPGA Loaded Status GPIO reads high (bitstream
    /// loaded / PL fabric configured), `false` if low.
    ///
    /// Hot path: designed to be polled very often. After `init()`, this is a
    /// single `get_value` ioctl on an already-open line.
    ///
    /// # Panics
    /// Panics if called before a successful `init()`. See `get_beam_active`
    /// for rationale.
    pub fn get_fpga_loaded_status() -> bool {
        if crate::config::SIMULATION_MODE {
            return true;
        }

        matches!(lines().fpga_loaded.get_value(), Ok(1))
    }

    // -------------------------------------------------------------------
    // SDRAM Select (inputs, EMIO bits 6..=9 / lines 84..=87)
    // -------------------------------------------------------------------

    /// Return `true` if the select line for SDRAM `index` (0..=3) reads high.
    ///
    /// Hot path: designed to be polled very often. After `init()`, this is a
    /// single `get_value` ioctl on an already-open line.
    ///
    /// # Panics
    /// Panics if `index >= SDRAM_SELECT_COUNT`, or if called before a
    /// successful `init()`. See `get_beam_active` for rationale.
    pub fn get_sdram_select(index: usize) -> bool {
        assert!(
            index < SDRAM_SELECT_COUNT,
            "SDRAM select index {index} out of range (0..{SDRAM_SELECT_COUNT})"
        );

        if crate::config::SIMULATION_MODE {
            // Pretend RAM #0 is strapped in, matching the "healthy board"
            // defaults used by the other simulated channels.
            return index == 0;
        }

        matches!(lines().sdram_select[index].get_value(), Ok(1))
    }

    /// Read all four SDRAM select lines in one call.
    ///
    /// Note these are four independent ioctls, so the sample is not atomic
    /// across lines; if the PL can change the strapping mid-read you'll want a
    /// single PL-side register instead.
    ///
    /// # Panics
    /// Panics if called before a successful `init()`.
    pub fn get_sdram_selects() -> [bool; SDRAM_SELECT_COUNT] {
        let mut out = [false; SDRAM_SELECT_COUNT];
        for (i, slot) in out.iter_mut().enumerate() {
            *slot = get_sdram_select(i);
        }
        out
    }

    /// Decode the SDRAM select lines as one-hot and return the index of the
    /// selected RAM.
    ///
    /// Returns `None` if no line is asserted or if more than one is — both are
    /// invalid encodings and worth treating as "unknown" rather than guessing.
    /// Use `get_sdram_selects()` if you need to see the raw pattern.
    ///
    /// # Panics
    /// Panics if called before a successful `init()`.
    pub fn get_selected_sdram() -> Option<usize> {
        let selects = get_sdram_selects();
        let mut found = None;
        for (i, &asserted) in selects.iter().enumerate() {
            if asserted {
                if found.is_some() {
                    return None; // more than one line high
                }
                found = Some(i);
            }
        }
        found
    }

    // -------------------------------------------------------------------
    // Clamp Request (OUTPUT, EMIO bit 10 / line 88)
    // -------------------------------------------------------------------

    /// Drive the Clamp Request output: `true` asserts (drives high), `false`
    /// de-asserts (drives low).
    ///
    /// Unlike the getters above this returns a `Result`. Failing to assert a
    /// clamp is not something to swallow on a beam run, so the error from the
    /// `set_value` ioctl is propagated to the caller.
    ///
    /// Under `SIMULATION_MODE` nothing is driven; the requested state is just
    /// recorded so `get_clamp_request()` stays consistent.
    ///
    /// # Panics
    /// Panics if called before a successful `init()`. See `get_beam_active`
    /// for rationale.
    pub fn set_clamp_request(assert: bool) -> Result<(), GpioError> {
        if crate::config::SIMULATION_MODE {
            CLAMP_ASSERTED.store(assert, Ordering::SeqCst);
            return Ok(());
        }

        lines().clamp_request.set_value(u8::from(assert))?;
        CLAMP_ASSERTED.store(assert, Ordering::SeqCst);
        Ok(())
    }

    /// Assert the Clamp Request output.
    pub fn assert_clamp_request() -> Result<(), GpioError> {
        set_clamp_request(true)
    }

    /// De-assert the Clamp Request output.
    pub fn release_clamp_request() -> Result<(), GpioError> {
        set_clamp_request(false)
    }

    /// Return `true` if the Clamp Request output is currently asserted.
    ///
    /// Reads the line back through the output handle (which reports the value
    /// being driven); if that ioctl fails, or we're in simulation, falls back
    /// to the shadow copy of the last successful write.
    ///
    /// # Panics
    /// Panics if called before a successful `init()`.
    pub fn get_clamp_request() -> bool {
        if crate::config::SIMULATION_MODE {
            return CLAMP_ASSERTED.load(Ordering::SeqCst);
        }

        match lines().clamp_request.get_value() {
            Ok(v) => v == 1,
            Err(_) => CLAMP_ASSERTED.load(Ordering::SeqCst),
        }
    }
}

// =========================================================================
// Simulated backend: no gpio-cdev, no real lines. Used whenever the
// `hardware` feature is off (e.g. `cargo build --no-default-features` on
// a Windows/Mac/dev box). Mirrors the real backend's own SIMULATION_MODE
// defaults exactly - both beam signals low, everything else "healthy"
// (calibrated, clocks present, FPGA loaded, RAM #0 strapped), clamp
// de-asserted - so behaviour is identical to a hardware build that happens
// to have `SIMULATION_MODE = true`.
// =========================================================================
#[cfg(not(feature = "hardware"))]
mod backend {
    use super::{GpioError, SDRAM_SELECT_COUNT};
    use std::sync::atomic::{AtomicBool, Ordering};

    static CLAMP_ASSERTED: AtomicBool = AtomicBool::new(false);

    pub fn init() -> Result<(), GpioError> {
        Ok(())
    }

    pub fn get_beam_active() -> bool {
        false
    }

    pub fn get_beam_possible() -> bool {
        false
    }

    pub fn get_calibration_signal() -> bool {
        true
    }

    pub fn get_ui_clock_signal() -> bool {
        true
    }

    pub fn get_pl_clock_signal() -> bool {
        true
    }

    pub fn get_fpga_loaded_status() -> bool {
        true
    }

    pub fn get_sdram_select(index: usize) -> bool {
        assert!(
            index < SDRAM_SELECT_COUNT,
            "SDRAM select index {index} out of range (0..{SDRAM_SELECT_COUNT})"
        );
        index == 0
    }

    pub fn get_sdram_selects() -> [bool; SDRAM_SELECT_COUNT] {
        let mut out = [false; SDRAM_SELECT_COUNT];
        for (i, slot) in out.iter_mut().enumerate() {
            *slot = get_sdram_select(i);
        }
        out
    }

    pub fn get_selected_sdram() -> Option<usize> {
        Some(0)
    }

    pub fn set_clamp_request(assert: bool) -> Result<(), GpioError> {
        CLAMP_ASSERTED.store(assert, Ordering::SeqCst);
        Ok(())
    }

    pub fn assert_clamp_request() -> Result<(), GpioError> {
        set_clamp_request(true)
    }

    pub fn release_clamp_request() -> Result<(), GpioError> {
        set_clamp_request(false)
    }

    pub fn get_clamp_request() -> bool {
        CLAMP_ASSERTED.load(Ordering::SeqCst)
    }
}

pub use backend::*;

/// Deprecated alias kept so existing call sites don't break with the new
/// EMIO map, which splits the old single "Beam Signal" into Beam Active
/// (bit 0) and Beam Possible (bit 1).
#[deprecated(note = "renamed to get_beam_active(); see also get_beam_possible()")]
pub fn get_beam_signal() -> bool {
    get_beam_active()
}