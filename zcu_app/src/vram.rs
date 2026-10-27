// vram.rs — Simulated PL DDR4 backend for desktop testing.
//
// Drop-in replacement for ram.rs that backs the "memory" with a heap
// allocation instead of an mmap of /dev/mem. Lets the rest of the server
// be developed and tested on a normal Linux desktop without the ZCU104
// hardware in the loop.
//
// Memory usage:
//   `vec![0u8; PL_DDR4_SIZE]` is alloc_zeroed-backed. On Linux the kernel
//   serves zero pages via a shared copy-on-write zero page, so the 4 GiB
//   region is virtual-only — untouched bytes cost no physical RAM, and
//   pages get real backing only when first written. A test that exercises
//   a few MB stays at a few MB of resident memory.
//
// Behavioural parity with ram.rs:
//   * Same public surface: init / read / write, identical signatures.
//   * Same bounds-check semantics (panic on out-of-range offset).
//   * Volatile accesses preserved so codegen is comparable; in the
//     simulator there's no other AXI master, but the option to swap
//     backends without recompiling the call sites is the whole point.
//
// What this DOES NOT simulate:
//   * Memory-controller timing, refresh cycles, or burst behaviour.
//   * Per-chip stuck-at faults (could be layered on top — see notes
//     in the module bottom).
//   * Cache coherency quirks of the real /dev/mem mapping.

use std::io;
use std::ptr;
use std::sync::OnceLock;

/// Size of the simulated PL DDR4 region. Must match ram.rs so that
/// chip.rs's address arithmetic is valid regardless of which backend
/// is compiled in.
const PL_DDR4_SIZE: usize = 0x1_0000_0000; // 4 GiB

/// Owning handle for the heap-backed buffer. We keep the `Box<[u8]>`
/// alive in `_owner` so it's dropped when the process exits; the raw
/// pointer is what fast paths use.
///
/// `Box<[u8]>` stores its data on the heap, so moving the Box does not
/// move the underlying bytes — the pointer captured before the move
/// into `Buffer` remains valid for the lifetime of the static.
struct Buffer {
    ptr: *mut u8,
    _owner: Box<[u8]>,
}

// SAFETY: the buffer is a fixed, never-resized region of plain bytes.
// Concurrent access correctness is the caller's responsibility (same
// contract as ram.rs).
unsafe impl Send for Buffer {}
unsafe impl Sync for Buffer {}

static BUFFER: OnceLock<Buffer> = OnceLock::new();

/// Allocate the simulated PL DDR4 region. Call once at program start.
/// Subsequent calls are no-ops and return `Ok(())`.
pub fn init() -> io::Result<()> {
    if BUFFER.get().is_some() {
        return Ok(());
    }

    // alloc_zeroed: on Linux the kernel hands back lazy zero pages, so
    // this is cheap even at 4 GiB.
    let mut owner: Box<[u8]> = vec![0u8; PL_DDR4_SIZE].into_boxed_slice();
    let ptr = owner.as_mut_ptr();

    // If another thread won the race to set, our `owner` is dropped here
    // — its allocation is freed and the winning thread's stays live.
    let _ = BUFFER.set(Buffer { ptr, _owner: owner });
    Ok(())
}

/// Internal accessor — returns the base pointer or panics if `init()`
/// wasn't called.
fn base() -> *mut u8 {
    BUFFER
        .get()
        .expect("vram: init() must be called before read()/write()")
        .ptr
}

/// Write a single byte at `offset` bytes into the simulated PL DDR4.
///
/// Panics if `offset` is past the end of the simulated region or if
/// `init()` has not been called.
pub fn write(offset: u32, value: u8) {
    let off = offset as usize;
    assert!(
        off < PL_DDR4_SIZE,
        "vram::write: offset 0x{:X} out of range (size 0x{:X})",
        off,
        PL_DDR4_SIZE
    );
    unsafe {
        ptr::write_volatile(base().add(off), value);
    }
}

/// Read a single byte at `offset` bytes into the simulated PL DDR4.
///
/// Panics if `offset` is past the end of the simulated region or if
/// `init()` has not been called.
pub fn read(offset: u32) -> u8 {
    let off = offset as usize;
    assert!(
        off < PL_DDR4_SIZE,
        "vram::read: offset 0x{:X} out of range (size 0x{:X})",
        off,
        PL_DDR4_SIZE
    );
    unsafe { ptr::read_volatile(base().add(off)) }
}

// --- Notes for future extension ----------------------------------------
//
// To inject faults (stuck-at-0, stuck-at-1, single-bit flips at chosen
// addresses) wrap the read/write in a fault map — e.g. a HashMap<u32, Fault>
// consulted on each access. Keep it behind a feature flag so the fast
// path stays a single volatile op when faults aren't needed.
//
// To gather access statistics (read/write counters, address histogram)
// add AtomicU64 counters alongside BUFFER and bump them in read/write.
// Atomic ops on a hot path will slow things down — keep behind a flag
// for the same reason.
