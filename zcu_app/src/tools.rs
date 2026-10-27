use std::process::Command;
use std::thread;
use std::time::{Duration, SystemTime, UNIX_EPOCH};
use std::collections::hash_map::RandomState;
use std::hash::{BuildHasher, Hasher};


pub fn get_epoch() -> u64 {
    SystemTime::now().duration_since(UNIX_EPOCH).unwrap().as_secs()
}


pub fn rand_u16() -> u16 {
    RandomState::new().build_hasher().finish() as u16
}

pub fn unmount_dvr() {
    loop {
        if !is_mounted("/dvr") {
            return;
        }
        let out = Command::new("umount").arg("/dvr").output();
        match out {
            Ok(o) if o.status.success() => return,
            Ok(o) => eprintln!("umount: {}", String::from_utf8_lossy(&o.stderr).trim()),
            Err(e) => eprintln!("cannot run umount: {e}"),
        }
        thread::sleep(Duration::from_millis(250));
    }
}

fn is_mounted(target: &str) -> bool {
    std::fs::read_to_string("/proc/mounts")
        .map(|s| s.lines().any(|l| l.split(' ').nth(1) == Some(target)))
        .unwrap_or(false)
}