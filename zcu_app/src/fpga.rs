use std::path::{Path, PathBuf};
use std::process::Command;
use std::sync::RwLock;

use crate::task_engine::STATUS;

//Currently loaded FPGA image
pub static current_image: RwLock<Option<String>> = RwLock::new(None);

/// Loads `/dvr/fpga/<image_name>.bit.bin` into the PL via `fpgautil -b`.
/// Blocks until fpgautil exits. Returns true only if the image existed and
/// fpgautil reported success.
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

    let path: PathBuf = Path::new("/dvr/fpga").join(format!("{image_name}.bit.bin"));

    if !path.is_file() {
        eprintln!("flash_fpga: {} not found", path.display());
        return false;
    }

    //Make sure engine is idle before flashing, refuse to flash if busy
    if !STATUS.read().unwrap().is_idle {
        //Not idle, abort
        return false;
    }

    match Command::new("fpgautil").arg("-b").arg(&path).output() {
        Ok(out) if out.status.success() => {
            //Update the currently loaded image
            *current_image.write().unwrap() = Some(image_name.to_owned());
            true
        },
        Ok(out) => {
            eprintln!(
                "flash_fpga: fpgautil exited with {} — {}",
                out.status,
                String::from_utf8_lossy(&out.stderr).trim()
            );
            //Update the currently loaded image
            *current_image.write().unwrap() = None;
            false
        }
        Err(e) => {
            eprintln!("flash_fpga: failed to spawn fpgautil: {e}");
            //Update the currently loaded image
            *current_image.write().unwrap() = None;
            false
        }
    }
}

/// Every FPGA image available in `/dvr/fpga`, reported as the bare name that
/// `flash_fpga` takes: `/dvr/fpga/memtest.bit.bin` is listed as `memtest`.
///
/// Returns an empty Vec when the directory is missing or unreadable, which is
/// the state while `/dvr` is unmounted in safe mode. An empty list therefore
/// means "nothing available right now", not necessarily "the card is empty".
pub fn list_fpga_images() -> Vec<String> {
    let mut images: Vec<String> = Vec::new();

    let entries = match std::fs::read_dir("/dvr/fpga") {
        Ok(e) => e,
        Err(e) => {
            eprintln!("list_fpga_images: cannot read /dvr/fpga: {e}");
            return images;
        }
    };

    for entry in entries.flatten() {
        let path = entry.path();

        // Mirrors flash_fpga's own check rather than DirEntry::file_type(),
        // which does not follow symlinks on Unix. Using the same test keeps
        // the list and what is actually flashable in agreement, and drops a
        // directory that happens to be named "something.bit.bin".
        if !path.is_file() {
            continue;
        }

        // file_stem() is wrong here: it strips only the final extension, so
        // "memtest.bit.bin" would come back as "memtest.bit". Strip the whole
        // compound suffix instead, which also filters out any other file
        // sitting in the directory (.bit, .bin, .xsa, notes.txt).
        if let Some(name) = path.file_name().and_then(|n| n.to_str()) {
            if let Some(stem) = name.strip_suffix(".bit.bin") {
                // Guards against a file named literally ".bit.bin", which
                // would yield an empty name that flash_fpga rejects anyway.
                if !stem.is_empty() {
                    images.push(stem.to_owned());
                }
            }
        }
    }

    // read_dir order is whatever the filesystem hands back and differs between
    // boots, so sort for a stable ordering in the client's image list.
    images.sort_unstable();
    images
}