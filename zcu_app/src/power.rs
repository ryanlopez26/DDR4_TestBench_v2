//! power.rs — rail power telemetry for the PL-side DDR4 SODIMM on the ZCU104.
//!
//! ## What this actually measures
//!
//! The ZCU104 has no current sense dedicated to the SODIMM. The socket (J1) is
//! fed from VCC1V2, which on the stock board is PHASE_C of the Infineon
//! IRPS5401 at U179 (PMBus 0x43 / I2C 0x13 on PS I2C1). That same VCC1V2 rail
//! also supplies:
//!
//!   - PL banks 64, 65, 66  -> SODIMM DQ[0:31], DQ[32:63], ADDR/CTRL
//!   - PS bank 504          -> the 2 GB PS-side DDR4 component interface
//!
//! So the current reported here is SODIMM VDD/VDDQ *plus* the PL I/O driving
//! it *plus* all PS DDR4 I/O activity. It is a rail measurement, not a device
//! measurement. For per-device current during a beam run — the thing you
//! actually want for SEFI/latchup detection — use the INA293 shunt path on the
//! custom SODIMM cards; this module is for board-level health and for catching
//! gross events (a rail collapsing, a device drawing far outside its envelope).
//!
//! The SODIMM's other two supplies are on separate rails not covered here:
//! VPP (2.5V) and PL_DDR4_VTT (0.6V, from its own regulator).
//!
//! ## Where the numbers come from
//!
//! Linux hwmon, via the pmbus/irps5401 driver. The PMBus core numbers sensors
//! per class in page-enumeration order, so `in3_input` and `curr3_input` are
//! *not* guaranteed to describe the same rail. The labels are, which is why
//! everything below is resolved by label rather than by index:
//!
//!   page N  ->  label "vout{N+1}"  (volts)
//!           ->  label "iout{N+1}"  (amps)
//!           ->  label "pout{N+1}"  (watts)
//!
//! VCC1V2 is PHASE_C, the third channel, hence page 2 -> vout3/iout3/pout3.
//! VERIFY THIS ON TARGET before trusting a number — the page ordering depends
//! on how the device tree declares the PMIC, and a board revision or a DT
//! change can shift it. `dump_rails()` prints every labeled rail for exactly
//! this purpose.
//!
//! hwmon units are fixed by the ABI: millivolts, milliamps, microwatts.
//!
//! ## Requirements on target
//!
//! The IRPS5401 must be instantiated in the device tree behind the I2C1 mux
//! and CONFIG_SENSORS_IRPS5401 built in, or no hwmon node appears at all.
//! Check with `grep -H '' /sys/class/hwmon/hwmon*/name`.

use std::fs;
use std::path::{Path, PathBuf};
use std::sync::OnceLock;

use serde::{Deserialize, Serialize};

/// hwmon `name` values to accept, in priority order. The pmbus driver reports
/// the chip name; `pmbus` is the generic fallback binding.
const PMIC_HWMON_NAMES: [&str; 2] = ["irps5401", "pmbus"];

/// PMBus page carrying VCC1V2. PHASE_A=0, B=1, C=2, D=3, LDO=4.
const SODIMM_PMBUS_PAGE: u8 = 2;

/// A single rail reading. Volts/amps/watts, already scaled out of hwmon's
/// integer milli/micro units.
#[derive(Debug, Clone, Copy, Serialize, Deserialize)]
pub struct PowerStats {
    /// Rail voltage, volts.
    pub volts: f64,

    /// Rail current, amps.
    pub amps: f64,

    /// Rail power, watts. Read from pout when the PMIC reports it, otherwise
    /// computed as volts * amps.
    pub watts: f64,

    /// True if `watts` was computed rather than read from the PMIC. The PMIC's
    /// own pout register is a better number than the product of two separately
    /// sampled readings, which are not simultaneous.
    pub power_derived: bool,
}

/// Resolved sysfs paths for the rail, found once and reused. Discovery walks
/// /sys/class/hwmon and reads a handful of label files, which is far too much
/// work to repeat on every poll.
struct RailPaths {
    volt: PathBuf,
    curr: Option<PathBuf>,
    power: Option<PathBuf>,
}

static RAIL: OnceLock<Option<RailPaths>> = OnceLock::new();

/// Read the SODIMM rail. Returns None if the PMIC is not exposed through
/// hwmon, if the expected page is not present, or if a read fails.
///
/// Cheap enough to poll: three small sysfs reads after the first call.
pub fn read_sodimm_power() -> Option<PowerStats> {
    if crate::config::SIMULATION_MODE {
        // Plausible idle figures for a 1.2V rail feeding one SODIMM plus the
        // PS DDR4 I/O, so simulated runs produce something in range rather
        // than zeroes that look like a collapsed rail.
        return Some(PowerStats {
            volts: 1.203,
            amps: 0.234,
            watts: 0.281,
            power_derived: false,
        });
    }

    let rail = RAIL.get_or_init(resolve_rail).as_ref()?;

    // Voltage is the one reading with no fallback: without it there is nothing
    // meaningful to report.
    let volts = read_scaled(&rail.volt, 1e-3)?;

    let amps = rail
        .curr
        .as_ref()
        .and_then(|p| read_scaled(p, 1e-3))
        .unwrap_or(0.0);

    let (watts, power_derived) = match rail.power.as_ref().and_then(|p| read_scaled(p, 1e-6)) {
        Some(w) => (w, false),
        None => (volts * amps, true),
    };

    Some(PowerStats {
        volts,
        amps,
        watts,
        power_derived,
    })
}

/// Print every labeled rail the PMIC exposes, with its page label and current
/// value. Use this during bring-up to confirm which page is VCC1V2 before
/// trusting SODIMM_PMBUS_PAGE.
pub fn dump_rails() {
    let hwmon = match find_hwmon() {
        Some(h) => h,
        None => {
            eprintln!("power: no PMIC hwmon node found under /sys/class/hwmon");
            return;
        }
    };

    eprintln!("power: PMIC at {}", hwmon.display());

    for (prefix, scale, unit) in [
        ("in", 1e-3, "V"),
        ("curr", 1e-3, "A"),
        ("power", 1e-6, "W"),
    ] {
        for (label, input) in labeled_inputs(&hwmon, prefix) {
            match read_scaled(&input, scale) {
                Some(v) => eprintln!("  {label:<8} {v:>10.4} {unit}"),
                None => eprintln!("  {label:<8} {:>10} {unit}", "unreadable"),
            }
        }
    }
}

/// Locate the PMIC's hwmon directory and the three attribute files for the
/// configured page.
fn resolve_rail() -> Option<RailPaths> {
    let hwmon = find_hwmon()?;

    // Page is 0-based; PMBus labels are 1-based.
    let page = page_from_env();
    let n = page + 1;

    let volt = find_labeled(&hwmon, "in", &format!("vout{n}"))?;
    let curr = find_labeled(&hwmon, "curr", &format!("iout{n}"));
    let power = find_labeled(&hwmon, "power", &format!("pout{n}"));

    if curr.is_none() {
        eprintln!(
            "power: {} exposes no iout{n}; current will read 0",
            hwmon.display()
        );
    }

    eprintln!(
        "power: SODIMM rail = {} page {page} (vout{n})",
        hwmon.display()
    );

    Some(RailPaths { volt, curr, power })
}

/// First hwmon device whose `name` matches one of PMIC_HWMON_NAMES, or the
/// name in SODIMM_HWMON_NAME if that is set.
fn find_hwmon() -> Option<PathBuf> {
    let wanted: Vec<String> = match std::env::var("SODIMM_HWMON_NAME") {
        Ok(n) if !n.is_empty() => vec![n],
        _ => PMIC_HWMON_NAMES.iter().map(|s| s.to_string()).collect(),
    };

    // Outer loop over wanted names, not over directories: with both an
    // irps5401 and a generic pmbus node present, the specific one wins
    // regardless of hwmon numbering.
    for name in &wanted {
        for entry in fs::read_dir("/sys/class/hwmon").ok()?.filter_map(Result::ok) {
            let dir = entry.path();
            let found = fs::read_to_string(dir.join("name")).unwrap_or_default();
            if found.trim() == name {
                return Some(dir);
            }
        }
    }

    None
}

/// Find the `<prefix>N_input` file whose sibling `<prefix>N_label` contains
/// `want`. Matching on the label is what keeps in/curr/power aligned to the
/// same rail, since their indices are numbered independently.
fn find_labeled(dir: &Path, prefix: &str, want: &str) -> Option<PathBuf> {
    labeled_inputs(dir, prefix)
        .into_iter()
        .find(|(label, _)| label == want)
        .map(|(_, input)| input)
}

/// Every (label, input path) pair for one attribute class in this hwmon dir.
fn labeled_inputs(dir: &Path, prefix: &str) -> Vec<(String, PathBuf)> {
    let mut out = Vec::new();

    let entries = match fs::read_dir(dir) {
        Ok(e) => e,
        Err(_) => return out,
    };

    for entry in entries.filter_map(Result::ok) {
        let fname = entry.file_name();
        let fname = fname.to_string_lossy();

        let stem = match fname.strip_suffix("_label") {
            Some(s) if s.starts_with(prefix) => s,
            _ => continue,
        };

        // Guard against "power" matching nothing sane and against prefix
        // collisions: the character after the prefix must be a digit.
        if !stem[prefix.len()..].starts_with(|c: char| c.is_ascii_digit()) {
            continue;
        }

        if let Ok(label) = fs::read_to_string(entry.path()) {
            out.push((label.trim().to_string(), dir.join(format!("{stem}_input"))));
        }
    }

    out.sort_by(|a, b| a.0.cmp(&b.0));
    out
}

/// Read one integer sysfs attribute and scale it to base units.
fn read_scaled(path: &Path, scale: f64) -> Option<f64> {
    let raw = fs::read_to_string(path).ok()?;
    let n: f64 = raw.trim().parse().ok()?;
    Some(n * scale)
}

/// Page override, for when the DT enumerates channels differently than the
/// stock board.
fn page_from_env() -> u8 {
    std::env::var("SODIMM_PMBUS_PAGE")
        .ok()
        .and_then(|s| s.parse::<u8>().ok())
        .unwrap_or(SODIMM_PMBUS_PAGE)
}