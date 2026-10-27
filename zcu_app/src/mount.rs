use std::fs;
use std::os::unix::fs::FileTypeExt;
use std::process::Command;

/// Candidate mass-storage disks, probed in this order.
const CANDIDATE_DISKS: [&str; 4] = ["/dev/sda", "/dev/sdb", "/dev/sdc", "/dev/sdd"];

/// Where the capture volume gets mounted.
const DVR_MOUNTPOINT: &str = "/dvr";

/// Options passed to mount(8). Deliberately filesystem-agnostic: the type is
/// autodetected, and vfat-only options like umask= would make an ext4 mount
/// fail outright. Add them, with -t vfat, if the drive is always FAT.
const DVR_MOUNT_OPTIONS: &str = "rw,noatime";

/// True if `path` exists and is a block device. A plain existence check is not
/// enough — a stale file left at /dev/sda by something else would pass it.
fn is_block_device(path: &str) -> bool {
    fs::metadata(path)
        .map(|m| m.file_type().is_block_device())
        .unwrap_or(false)
}

/// The device currently mounted at `target`, per /proc/mounts.
fn mounted_source(target: &str) -> Option<String> {
    let mounts = fs::read_to_string("/proc/mounts").ok()?;

    // Last match wins: if mounts are stacked on this path, the most recent one
    // is what is actually visible.
    mounts
        .lines()
        .filter(|l| l.split_whitespace().nth(1) == Some(target))
        .last()
        .and_then(|l| l.split_whitespace().next())
        .map(str::to_string)
}

/// Find the first attached disk among /dev/sda../dev/sdd and mount its first
/// partition on /dvr. Returns the partition node that was mounted, or None if
/// nothing could be mounted.
pub fn mount_dvr() -> Option<String> {

    // Mounting over an existing mount stacks a second filesystem on top: the
    // old one stays mounted underneath, invisible, still holding its files
    // open. Report what is already there rather than burying it.
    if let Some(existing) = mounted_source(DVR_MOUNTPOINT) {
        eprintln!("mount_dvr: {DVR_MOUNTPOINT} already holds {existing}");
        return Some(existing);
    }

    if let Err(e) = fs::create_dir_all(DVR_MOUNTPOINT) {
        eprintln!("mount_dvr: cannot create {DVR_MOUNTPOINT}: {e}");
        return None;
    }

    for disk in CANDIDATE_DISKS {
        if !is_block_device(disk) {
            continue;
        }

        let part = format!("{disk}1");
        if !is_block_device(&part) {
            // Disk node present but no first partition: an empty card reader,
            // or a drive written as a whole device with no partition table.
            eprintln!("mount_dvr: {disk} present but {part} is missing, skipping");
            continue;
        }

        match Command::new("mount")
            .arg("-o")
            .arg(DVR_MOUNT_OPTIONS)
            .arg(&part)
            .arg(DVR_MOUNTPOINT)
            .output()
        {
            Ok(out) if out.status.success() => {
                eprintln!("mount_dvr: mounted {part} on {DVR_MOUNTPOINT}");
                return Some(part);
            }
            Ok(out) => {
                // Wrong filesystem, dirty FAT, missing kernel driver. Try the
                // next disk rather than giving up on the whole scan.
                eprintln!(
                    "mount_dvr: mounting {part} failed ({}): {}",
                    out.status,
                    String::from_utf8_lossy(&out.stderr).trim()
                );
            }
            Err(e) => {
                // mount(8) itself is missing or not executable. No other disk
                // will fare better, so stop here.
                eprintln!("mount_dvr: cannot run mount(8): {e}");
                return None;
            }
        }
    }

    eprintln!("mount_dvr: no mountable partition found on {CANDIDATE_DISKS:?}");
    None
}