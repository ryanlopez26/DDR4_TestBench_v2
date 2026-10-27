// sodimm.rs — User-space access to the PL DDR4 SODIMM on the ZCU104.
//
// Approach: mmap the PL DDR4 physical region into the process address space
// via /dev/mem, then expose simple byte read/write helpers.
//
// Requirements:
//   * Linux on the PS (PetaLinux or similar).
//   * Process runs as root (or has CAP_SYS_RAWIO) to open /dev/mem.
//   * Kernel built with CONFIG_DEVMEM=y and CONFIG_STRICT_DEVMEM=n, or with
//     the PL DDR4 region added to the devmem whitelist.
//
// Cargo.toml:
//
//     [dependencies]
//     libc = { version = "0.2", optional = true }
//
// !!! IMPORTANT — VERIFY THESE CONSTANTS FOR YOUR DESIGN !!!
// The base address and size of PL DDR4 are determined by the MIG/DDR4
// controller IP in your Vivado block design. Open the Address Editor in
// Vivado and read the actual values; the defaults below are typical
// ZynqMP placements but will not be correct for every project.
//
// ## The `hardware` feature
//
// The real implementation below (mmap() of /dev/mem via `libc`) is gated
// on the `hardware` Cargo feature, on by default. `libc::mmap`/`open`/
// `off_t` are POSIX-only, so this file can't compile at all on Windows/
// macOS regardless of SIMULATION_MODE's runtime value - Rust still
// compiles a module's code even when nothing at runtime ever calls into
// it. Building with `--no-default-features` swaps in a stub backend
// below with the same three functions (`init`/`read`/`write`), so
// chip.rs and main.rs don't need to change.
//
// Unlike gpio.rs, the stub here doesn't pretend to simulate memory -
// that's vram.rs's job, and chip.rs already picks between vram and ram
// based on `SIMULATION_MODE`. The stub exists purely so the `ram` module
// still compiles and links without the `hardware` feature; if it's ever
// actually called (i.e. `SIMULATION_MODE = false` in a build that wasn't
// compiled with `hardware`, which is a real misconfiguration - there's
// nothing for it to fall back to), it fails loudly instead of silently
// touching the wrong memory.

#[cfg(feature = "hardware")]
mod backend {
    use std::ffi::CString;
    use std::io;
    use std::ptr;
    use std::sync::OnceLock;

    /// Physical base address of the PL DDR4 region (set by Vivado Address Editor).
    const PL_DDR4_BASE: libc::off_t = 0x4_0000_0000;

    /// Size of the PL DDR4 region in bytes. ZCU104 reference designs are commonly
    /// 512 MiB or up to 4 GiB depending on the SODIMM installed.
    const PL_DDR4_SIZE: usize = 0x1_0000_0000; // 4 GiB

    // Raw pointers aren't Send/Sync by default, but the mmap region itself is
    // shared global memory that's safe to access from any thread (the caller is
    // responsible for whatever higher-level synchronization their use case needs).
    // Wrap the pointer so we can store it in a OnceLock.
    struct Mapping(*mut u8);
    unsafe impl Send for Mapping {}
    unsafe impl Sync for Mapping {}

    static MAPPING: OnceLock<Mapping> = OnceLock::new();

    /// Open /dev/mem and mmap the PL DDR4 region. Call once at program start.
    ///
    /// Subsequent calls are no-ops and return `Ok(())`.
    pub fn init() -> io::Result<()> {
        if MAPPING.get().is_some() {
            return Ok(());
        }

        // --- open /dev/mem ---
        // O_SYNC requests an uncached mapping, which is what we want when other
        // AXI masters (DMA engines, PL logic) may touch the same memory and we
        // don't want stale CPU cache lines hiding their writes.
        let path = CString::new("/dev/mem").unwrap();
        let fd = unsafe { libc::open(path.as_ptr(), libc::O_RDWR | libc::O_SYNC) };
        if fd < 0 {
            return Err(io::Error::last_os_error());
        }

        // --- mmap the PL DDR4 region ---
        let ptr = unsafe {
            libc::mmap(
                ptr::null_mut(),                       // let the kernel choose vaddr
                PL_DDR4_SIZE,
                libc::PROT_READ | libc::PROT_WRITE,
                libc::MAP_SHARED,
                fd,
                PL_DDR4_BASE,
            )
        };

        // mmap holds its own reference to the underlying mapping; closing the fd
        // does not unmap. Close it eagerly so we don't leak the descriptor.
        unsafe { libc::close(fd) };

        if ptr == libc::MAP_FAILED {
            return Err(io::Error::last_os_error());
        }

        // Store the pointer. If another thread raced us and won, unmap ours.
        if MAPPING.set(Mapping(ptr as *mut u8)).is_err() {
            unsafe { libc::munmap(ptr, PL_DDR4_SIZE) };
        }
        Ok(())
    }

    /// Internal accessor — returns the base pointer or panics if `init()` wasn't called.
    fn base() -> *mut u8 {
        MAPPING
            .get()
            .expect("sodimm: init() must be called before read()/write()")
            .0
    }

    /// Write a single byte at `offset` bytes into PL DDR4.
    ///
    /// Panics if `offset` is past the end of the mapped region or if `init()`
    /// has not been called.
    pub fn write(offset: u32, value: u8) {
        let off = offset as usize;
        assert!(
            off < PL_DDR4_SIZE,
            "sodimm::write: offset 0x{:X} out of range (size 0x{:X})",
            off,
            PL_DDR4_SIZE
        );
        // Volatile so the compiler can't elide or reorder the store relative to
        // other volatile accesses — important since the memory may be touched
        // by other masters on the AXI fabric.
        unsafe {
            ptr::write_volatile(base().add(off), value);
        }
    }

    /// Read a single byte at `offset` bytes into PL DDR4.
    ///
    /// Panics if `offset` is past the end of the mapped region or if `init()`
    /// has not been called.
    pub fn read(offset: u32) -> u8 {
        let off = offset as usize;
        assert!(
            off < PL_DDR4_SIZE,
            "sodimm::read: offset 0x{:X} out of range (size 0x{:X})",
            off,
            PL_DDR4_SIZE
        );
        unsafe { ptr::read_volatile(base().add(off)) }
    }
}

// =========================================================================
// Stub backend: no libc mmap, no real memory. Used whenever the
// `hardware` feature is off. Only reached at all if something calls
// ram::read/write/init while SIMULATION_MODE is false in a build that
// wasn't compiled with `hardware` - a real misconfiguration, since
// there's no hardware to fall back to (that's what vram.rs/SIMULATION_MODE
// are for). Fails loudly rather than quietly reading/writing nothing.
// =========================================================================
#[cfg(not(feature = "hardware"))]
mod backend {
    use std::io;

    const NO_HARDWARE_MSG: &str = "ram:: was called but this build doesn't have the \
        \"hardware\" Cargo feature enabled (e.g. built with --no-default-features). \
        Set config::SIMULATION_MODE = true to use vram instead, or rebuild with the \
        \"hardware\" feature to get real PL DDR4 access.";

    pub fn init() -> io::Result<()> {
        Err(io::Error::new(io::ErrorKind::Unsupported, NO_HARDWARE_MSG))
    }

    pub fn write(_offset: u32, _value: u8) {
        panic!("{NO_HARDWARE_MSG}");
    }

    pub fn read(_offset: u32) -> u8 {
        panic!("{NO_HARDWARE_MSG}");
    }
}

pub use backend::*;
