use std::time::Duration;
use std::time::Instant;
use std::sync::RwLock;


// Beam services
pub static REQUEST_BEAM: RwLock<bool> = RwLock::new(false);
pub static BEAM_ON: RwLock<bool> = RwLock::new(false);
pub static BEAM_SERVICES: RwLock<Option<Instant>> = RwLock::new(None);

/// How stale a beam-services update may be before the CLIENT is told services
/// have lapsed. Reported verbatim in InfoRsp.beam_services.
pub const BEAM_SERVICES_WINDOW: Duration = Duration::from_millis(200);

/// How stale a beam-services update may be before the ENGINE stops believing
/// BEAM_ON. Deliberately wider than BEAM_SERVICES_WINDOW: the client refreshes
/// on roughly the same 200 ms cadence as the engine checkpoint, so a window
/// equal to that cadence would flap on ordinary scheduling jitter. One second
/// still catches a disconnect well inside TERMINATION_DELAY_FOR_SEFI_TEST.
///
/// NOTE: the client must refresh BEAMSERVICES faster than this. If it only
/// sends on state CHANGE rather than on a timer, a running Dynamic or Expose
/// record will see beam go false here and terminate.
pub const BEAM_TRUST_WINDOW: Duration = Duration::from_millis(1000);

// Beam modifier
//
// These hold plain bools, so a poisoned lock carries no torn state worth
// protecting against. into_inner() rather than unwrap(): the engine calls
// set_beam_request on its SEFI path, and a client thread that panicked while
// holding one of these would otherwise take the engine down with it.
pub fn set_beam_request(req: bool){
    *REQUEST_BEAM.write().unwrap_or_else(|e| e.into_inner()) = req;
}

pub fn get_beam_request() -> bool {
    *REQUEST_BEAM.read().unwrap_or_else(|e| e.into_inner())
}

/// Raw BEAM_ON as last reported by the client, with no freshness check.
/// Use this only for reporting state back to the client, alongside
/// `beam_services_active()` so it can see for itself whether the value is live.
pub fn is_beam_active() -> bool {
    *BEAM_ON.read().unwrap_or_else(|e| e.into_inner())
}

/// BEAM_ON, but false whenever the reading is too old to trust.
///
/// BEAM_ON is asserted by the client over BEAMSERVICES, not read from a pin.
/// If the client drops mid-run the flag freezes at its last value, and a
/// Dynamic record whose termination test is "beam has been low for 3 s" would
/// never fire — the run outlives the client and holds the engine forever.
/// Every engine decision and every logged sample goes through this.
pub fn is_beam_active_trusted() -> bool {
    is_beam_active() && beam_services_fresh(BEAM_TRUST_WINDOW)
}

/// True if the client has refreshed beam services within BEAM_SERVICES_WINDOW.
pub fn beam_services_active() -> bool {
    beam_services_fresh(BEAM_SERVICES_WINDOW)
}

/// True if the last beam-services update is newer than `window`. None (no
/// update has ever arrived) counts as stale.
fn beam_services_fresh(window: Duration) -> bool {
    match *BEAM_SERVICES.read().unwrap_or_else(|e| e.into_inner()) {
        Some(i) => i.elapsed() <= window,
        None => false,
    }
}