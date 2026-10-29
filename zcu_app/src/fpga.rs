use std::path::{Path, PathBuf};
use std::process::Command;
use std::sync::RwLock;

use crate::task_engine::{CMD, STATUS};

/// Directory on the card holding flashable bitstreams.
const FPGA_DIR: &str = "/dvr/fpga";

/// Suffix of a flashable image. Raw Vivado `.bit` is correct here: the ZynqMP
/// FPGA Manager has accepted both Vivado-generated .bit and bootgen-generated
/// .bin since the 2018.3 release, so there is no need to run bootgen first.
/// (`fpgautil -h` still prints "-b <binfile>"; that help text is stale, not a
/// constraint. The bin-only restriction applies to Zynq-7000's PCAP path.)
const IMAGE_SUFFIX: &str = ".bit";

/// Currently loaded FPGA image, as last set by a SUCCESSFUL flash. None means
/// "this process has not loaded anything", not "the PL is empty" — after a
/// server restart the PL may well still hold a bitstream from before.
pub static current_image: RwLock<Option<String>> = RwLock::new(None);

/// Loads `/dvr/fpga/<image_name>.bit` into the PL via `fpgautil -b`.
/// Blocks until fpgautil exits. Returns true only if the image existed, the
/// engine was idle, and fpgautil reported success.
pub fn flash_fpga(image_name: &str) -> bool {
    // Keep a network-supplied name from escaping /dvr/fpga
    if image_name.is_empty()
        || image_name.contains('/')
        || image_name.contains('\0')
        || image_name.contains("..")
    {
        eprintln!("flash_fpga: rejected image name {image_name:?}");
        return false;
    }

    // Hold the engine's command lock for the whole operation.
    //
    // This is load-bearing, not just tidiness. ram.rs keeps a /dev/mem mapping
    // of the PL DDR4 window open for the life of the process, and during
    // reconfiguration that AXI slave disappears. A read into a window with no
    // responder does not return an error — it stalls the core until the AXI
    // timeout, or hangs it outright. Checking STATUS.is_idle alone is
    // check-then-act: the engine can dequeue a task in the gap between the
    // check and fpgautil returning. The engine takes this same lock to pull
    // work off the queue, so holding it here keeps it parked.
    //
    // Acquired BEFORE the idle check so the two cannot be reordered by a
    // racing dequeue.
    let _engine_parked = match CMD.write() {
        Ok(g) => g,
        // A poisoned CMD means the engine thread is already dead. Reconfiguring
        // the PL under a process whose state is unknown is not worth the risk.
        Err(_) => {
            eprintln!("flash_fpga: engine command lock is poisoned, refusing to flash");
            return false;
        }
    };

    //Make sure engine is idle before flashing, refuse to flash if busy
    match STATUS.read() {
        Ok(s) if s.is_idle => {}
        Ok(_) => {
            eprintln!("flash_fpga: engine is busy, refusing to flash");
            return false;
        }
        Err(_) => {
            eprintln!("flash_fpga: engine status lock is poisoned, refusing to flash");
            return false;
        }
    }

    let path: PathBuf = Path::new(FPGA_DIR).join(format!("{image_name}{IMAGE_SUFFIX}"));

    if !path.is_file() {
        eprintln!("flash_fpga: {} not found", path.display());
        return false;
    }

    match Command::new("fpgautil").arg("-b").arg(&path).output() {
        Ok(out) if out.status.success() => {
            //Update the currently loaded image
            *current_image.write().unwrap_or_else(|e| e.into_inner()) =
                Some(image_name.to_owned());
            true
        }
        Ok(out) => {
            eprintln!(
                "flash_fpga: fpgautil exited with {} — {}",
                out.status,
                String::from_utf8_lossy(&out.stderr).trim()
            );
            // current_image is deliberately LEFT ALONE. A failed fpgautil does
            // not unload the PL — whatever was in there before is still in
            // there. Clearing it made the client report
            // "(unknown — PL reports configured)" for a PL whose contents were
            // in fact perfectly well known.
            false
        }
        Err(e) => {
            eprintln!("flash_fpga: failed to spawn fpgautil: {e}");
            // Same reasoning: fpgautil never ran, so nothing changed in the PL.
            false
        }
    }
}

/// Every FPGA image available in `/dvr/fpga`, reported as the bare name that
/// `flash_fpga` takes: `/dvr/fpga/memtest.bit` is listed as `memtest`.
///
/// Returns an empty Vec when the directory is missing or unreadable, which is
/// the state while `/dvr` is unmounted in safe mode. An empty list therefore
/// means "nothing available right now", not necessarily "the card is empty".
pub fn list_fpga_images() -> Vec<String> {
    let mut images: Vec<String> = Vec::new();

    let entries = match std::fs::read_dir(FPGA_DIR) {
        Ok(e) => e,
        Err(e) => {
            eprintln!("list_fpga_images: cannot read {FPGA_DIR}: {e}");
            return images;
        }
    };

    for entry in entries.flatten() {
        let path = entry.path();

        // Mirrors flash_fpga's own check rather than DirEntry::file_type(),
        // which does not follow symlinks on Unix. Using the same test keeps
        // the list and what is actually flashable in agreement, and drops a
        // directory that happens to be named "something.bit".
        if !path.is_file() {
            continue;
        }

        let name = match path.file_name().and_then(|n| n.to_str()) {
            Some(n) => n,
            None => continue,
        };

        // Case-insensitive, because /dvr is FAT32. A directory entry with no
        // long-filename component displays as 8.3 — "SYSTEM.BIT" — under the
        // default shortname=mixed. A case-sensitive strip_suffix would skip
        // that file here while flash_fpga("SYSTEM") still found it, since
        // is_file() goes through vfat's case-insensitive lookup. Listing and
        // flashing would then disagree, which is the confusing direction.
        //
        // Note the flip side: on FAT, flash_fpga("foo") also matches FOO.bit,
        // so two images cannot be distinguished by case alone.
        if !name.to_ascii_lowercase().ends_with(IMAGE_SUFFIX) {
            continue;
        }

        // Safe slice: the suffix check above guarantees the length, and the
        // suffix is ASCII so the byte index is a char boundary.
        let stem = &name[..name.len() - IMAGE_SUFFIX.len()];

        // Guards against a file named literally ".bit", which would yield an
        // empty name that flash_fpga rejects anyway.
        if !stem.is_empty() {
            images.push(stem.to_owned());
        }
    }

    // read_dir order is whatever the filesystem hands back and differs between
    // boots, so sort for a stable ordering in the client's image list.
    images.sort_unstable();

    // FAT's case-insensitive lookup means SYSTEM.BIT and system.bit cannot
    // coexist in one directory, but a stale listing plus a rename can still
    // produce duplicates after the sort. Collapse them so the client's combo
    // box does not show the same image twice.
    images.dedup();

    images
}