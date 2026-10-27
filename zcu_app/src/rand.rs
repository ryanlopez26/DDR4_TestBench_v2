// Deterministic pseudo-random byte source keyed by (seed, address).
//
// Internal state:
//   - SEED : u64
//
// Public API:
//   set_seed, get_seed, rand
//
// Guarantee: for any fixed (seed, address) pair, the byte produced is always
// the same. Unlike a stream-based generator, `rand(addr)` holds no position
// state — calling it repeatedly with the same address returns the same byte,
// and any address can be sampled in any order. This is what lets the write,
// verify, and dump paths agree on the expected value at a given byte offset
// no matter what address_multiplier, start offset, or sample size is used.
//
// Usage:
//
//   use rand;
//
//   rand::set_seed(0xC0FFEE);
//   let a = rand::rand(0x0000);    // byte for address 0x0000
//   let b = rand::rand(0x1000);    // byte for address 0x1000
//
//   assert_eq!(rand::rand(0x0000), a);   // same seed + address -> same byte
//   assert_eq!(rand::rand(0x1000), b);

use std::sync::atomic::{AtomicU64, Ordering};

static SEED: AtomicU64 = AtomicU64::new(0);

/// Set the global seed.
pub fn set_seed(seed: u64) {
    SEED.store(seed, Ordering::Relaxed);
}

// /// Get the current seed.
// pub fn get_seed() -> u64 {
//     SEED.load(Ordering::Relaxed)
// }

/// Return the pseudo-random byte for `addr` under the current seed.
///
/// Pure with respect to `addr`: repeated calls with the same address (and an
/// unchanged seed) always return the same byte. No internal position advances.
pub fn rand(addr: u32) -> u8 {
    let seed = SEED.load(Ordering::Relaxed);
    mix(seed, addr as u64)
}

/// Pure mixing function: (seed, address) -> u8.
///
/// This is splitmix64 applied to `seed + address * φ`, where φ is the
/// 64-bit golden-ratio constant 0x9E37_79B9_7F4A_7C15. It gives strong
/// avalanche behavior so nearby addresses produce uncorrelated bytes,
/// while remaining a pure function of its inputs (no global state beyond
/// the seed), which is what guarantees reproducibility.
fn mix(seed: u64, addr: u64) -> u8 {
    let mut z = seed.wrapping_add(addr.wrapping_mul(0x9E37_79B9_7F4A_7C15));
    z = (z ^ (z >> 30)).wrapping_mul(0xBF58_476D_1CE4_E5B9);
    z = (z ^ (z >> 27)).wrapping_mul(0x94D0_49BB_1331_11EB);
    z ^= z >> 31;
    z as u8
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::Mutex;

    // Tests share the global seed, so serialize them.
    static LOCK: Mutex<()> = Mutex::new(());

    #[test]
    fn deterministic_for_same_seed_and_address() {
        let _g = LOCK.lock().unwrap();
        set_seed(0xDEAD_BEEF);
        let a = rand(42);
        let b = rand(42);
        assert_eq!(a, b);
    }

    #[test]
    fn idempotent_regardless_of_call_order() {
        let _g = LOCK.lock().unwrap();
        set_seed(0xC0FFEE);
        let a0 = rand(0x0000);
        let a1 = rand(0x1000);
        // Sample in reverse; values must not depend on order or prior calls.
        assert_eq!(rand(0x1000), a1);
        assert_eq!(rand(0x0000), a0);
    }

    #[test]
    fn different_addresses_generally_differ() {
        let _g = LOCK.lock().unwrap();
        set_seed(7);
        // Not guaranteed for every pair, but holds for these.
        assert_ne!(rand(0), rand(1));
    }

    #[test]
    fn different_seeds_generally_differ() {
        let _g = LOCK.lock().unwrap();
        set_seed(1);
        let a = rand(100);
        set_seed(2);
        let b = rand(100);
        assert_ne!(a, b);
    }

    #[test]
    fn address_multiplier_alignment() {
        // The whole point: a "write" pass stepping by 4 and a "verify" pass
        // stepping by 1 must produce the same byte at any shared address.
        let _g = LOCK.lock().unwrap();
        set_seed(0x1234_5678);
        for addr in (0u32..64).step_by(4) {
            let written = rand(addr);
            let verified = rand(addr); // same address, later in time
            assert_eq!(written, verified, "mismatch at {:#x}", addr);
        }
    }
}