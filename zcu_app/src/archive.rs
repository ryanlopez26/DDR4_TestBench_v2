use std::fs;
use std::io::ErrorKind;
use std::path::Path;
use std::sync::RwLock;

use crate::record::Record;
use crate::run::Run;
use crate::{PM, tools};



// Collection of all runs
pub static RUN_LOG: RwLock<Vec<Run>> = RwLock::new(Vec::new());

// Collection of all records
pub static RECORD_LOG: RwLock<Vec<Record>> = RwLock::new(Vec::new()); 

pub fn init() {

    //Obtain locks on necessary data structures
    let mut reclog = RECORD_LOG.write().unwrap();
    let mut runlog = RUN_LOG.write().unwrap();


    //Create necessary directories
    for d in ["/dvr/captures", "/dvr/records", "/dvr/runs", "/dvr/fpga"] {
        std::fs::create_dir_all(d).unwrap();
    }

    //Load records from disk
    if let Ok(entries) = fs::read_dir("/dvr/records") {
        for entry in entries.flatten() {
            let path = entry.path();
            if path.extension() != Some("rec".as_ref()) {
                continue;
            }
            if let Ok(s) = fs::read_to_string(&path) {
                if let Ok(rec) = serde_json::from_str::<Record>(&s) {
                    reclog.push(rec);
                }
            }
        }
    }

    //Load runs from disk
    if let Ok(entries) = fs::read_dir("/dvr/runs") {
        for entry in entries.flatten() {
            let path = entry.path();
            if path.extension() != Some("run".as_ref()) {
                continue;
            }
            if let Ok(s) = fs::read_to_string(&path) {
                if let Ok(run) = serde_json::from_str::<Run>(&s) {
                    runlog.push(run);
                }
            }
        }
    }
}

pub fn submit_record(r: Record) -> bool {

    // Refuse if safety is in place
    if PM.read().unwrap_or_else(|e| e.into_inner()).safe {
        return false;
    }

    // Serialize first — if this fails there is nothing to archive. to_string
    // rather than to_string_pretty: the log vectors pretty-print one number per
    // line, which triples the file for no benefit (from_str reads either).
    let s = match serde_json::to_string(&r) {
        Ok(s) => s,
        Err(_) => return false,
    };

    // Path of the record file. fs::write truncates, so an existing record with
    // this rid is overwritten rather than appended to.
    let path = Path::new("/dvr/records").join(format!("{:04X}.rec", r.rid));
    if std::fs::write(&path, s).is_err() {
        return false;
    }

    // Upsert into the archive registry: replace the entry for this rid if one
    // exists, otherwise append. A blind push left two entries pointing at the
    // single file, so listrecords showed duplicates and delete_record's retain
    // dropped both of them against one unlink.
    let mut rlog = RECORD_LOG.write().unwrap_or_else(|e| e.into_inner());
    match rlog.iter().position(|e| e.rid == r.rid) {
        Some(i) => rlog[i] = r,
        None     => rlog.push(r),
    }

    true
}

pub fn submit_run(r: Run) -> bool {

    // Refuse if safety is in place
    if PM.read().unwrap_or_else(|e| e.into_inner()).safe {
        return false;
    }

    // Serialize first — if this fails there is nothing to archive
    let s = match serde_json::to_string_pretty(&r) {
        Ok(s) => s,
        Err(_) => return false,
    };

    // Path of the record file. fs::write truncates, so an existing
    // run with this id is overwritten rather than appended to.
    let path = Path::new("/dvr/runs").join(format!("{:04X}.run", r.id));
    if std::fs::write(&path, s).is_err() {
        return false;
    }

    // Upsert into the archive registry: replace the entry for this id
    // if one exists, otherwise append.
    let mut rlog = RUN_LOG.write().unwrap_or_else(|e| e.into_inner());
    match rlog.iter().position(|e| e.id == r.id) {
        Some(i) => rlog[i] = r,
        None     => rlog.push(r),
    }

    true
}

// ============================= Deletion =============================

/// Remove one file. A file that is already gone counts as success — the
/// caller's goal is "this path does not exist", and a record with no capture
/// (or a run with a dangling record link) is a normal state, not a failure.
/// Any other error — permissions, EIO, read-only mount — is a real failure.
fn unlink(path: &Path) -> bool {
    match fs::remove_file(path) {
        Ok(()) => true,
        Err(e) if e.kind() == ErrorKind::NotFound => true,
        Err(_) => false,
    }
}

/// Delete the two files belonging to a record: the JSON in /dvr/records and
/// its capture dump in /dvr/captures. Does NOT touch RECORD_LOG, so it is
/// safe to call with the registry lock already held.
fn delete_record_files(rid: u16) -> bool {
    let rec = Path::new("/dvr/records").join(format!("{:04X}.rec", rid));
    let cap = Path::new("/dvr/captures").join(format!("{:04X}.cap", rid));

    // Both are attempted even if the first fails, so a stuck .rec does not
    // strand a multi-hundred-megabyte capture on the card forever.
    let a = unlink(&rec);
    let b = unlink(&cap);
    a && b
}

/// Every record id a run references, gathered from the test-results fields.
/// Sorted and deduplicated so a rid listed in two phases is only unlinked once.
fn run_children(r: &Run) -> Vec<u16> {
    let mut v: Vec<u16> = Vec::new();

    v.extend(&r.preliminary_records);
    v.extend(&r.primary_records);
    v.extend(&r.post_records);

    v.sort_unstable();
    v.dedup();
    v
}
/// True if any of a run's three result lists cite this record.
fn references_record(r: &Run, rid: u16) -> bool {
    r.preliminary_records.contains(&rid)
        || r.primary_records.contains(&rid)
        || r.post_records.contains(&rid)
}

/// Drop every occurrence of `rid` from a run's result lists. A rid listed in
/// two phases is removed from both.
fn strip_record_ref(r: &mut Run, rid: u16) {
    r.preliminary_records.retain(|&x| x != rid);
    r.primary_records.retain(|&x| x != rid);
    r.post_records.retain(|&x| x != rid);
}

/// Serialize one run over its file in /dvr/runs. Does NOT touch RUN_LOG, so it
/// is safe to call with the registry lock already held.
fn write_run_file(r: &Run) -> bool {
    let s = match serde_json::to_string_pretty(r) {
        Ok(s) => s,
        Err(_) => return false,
    };
    let path = Path::new("/dvr/runs").join(format!("{:04X}.run", r.id));
    std::fs::write(&path, s).is_ok()
}

/// Delete a record and its capture, and remove every reference to it from the
/// runs that cite it. Returns false if no record with this rid is in the
/// archive, if a file could not be removed, or if a run that referenced it
/// could not be rewritten on the card.
pub fn delete_record(rid: u16) -> bool {

    // Refuse if safety is in place. /dvr is unmounted in safe mode, so the
    // unlinks would either fail or hit an empty stub directory on the root
    // filesystem while the real files survive on the card.
    if PM.read().unwrap_or_else(|e| e.into_inner()).safe {
        return false;
    }

    // RECORD_LOG first, matching the order init() takes the two. Holding it
    // past the removal is load-bearing now: the rid becomes free the moment
    // the entry is gone, and gen_record_number() could hand it to a new
    // record while the run lists below are still being walked — the stripping
    // would then cut a link to that new record instead.
    let mut reclog = RECORD_LOG.write().unwrap_or_else(|e| e.into_inner());

    if !reclog.iter().any(|r| r.rid == rid) {
        return false;
    }

    // Files first. If they survive, init() would reload the record on the next
    // boot, so dropping the registry entry now would only desynchronize the two.
    if !delete_record_files(rid) {
        return false;
    }

    reclog.retain(|r| r.rid != rid);

    // Now the inbound links. Every run is checked, not just the first match —
    // a record can be cited by more than one run.
    let mut runlog = RUN_LOG.write().unwrap_or_else(|e| e.into_inner());
    let mut ok = true;

    for run in runlog.iter_mut() {
        if !references_record(run, rid) {
            continue;
        }

        // Disk before memory, as in submit_run: edit a copy, persist it, and
        // commit to the registry only once the card has it. A failed write
        // leaves both views still citing the dead rid, which reads as a
        // dangling link — a state the archive already tolerates — rather than
        // as a registry that disagrees with the files it was built from.
        let mut updated = run.clone();
        strip_record_ref(&mut updated, rid);
        updated.time_modified = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .map(|d| d.as_secs())
            .unwrap_or(updated.time_modified);

        if write_run_file(&updated) {
            *run = updated;
        } else {
            ok = false;
        }
    }

    ok
}

/// Delete a run, every record it references, and those records' captures.
/// Returns false if no run with this id is in the archive, or if a file could
/// not be removed. A child rid that is already absent from the archive is not
/// treated as a failure — the link can outlive the record it points at.
pub fn delete_run(id: u16) -> bool {

    // Refuse if safety is in place
    if PM.read().unwrap_or_else(|e| e.into_inner()).safe {
        return false;
    }

    // Collect the child ids and release the guard before locking RECORD_LOG.
    // init() takes RECORD_LOG and then RUN_LOG; holding RUN_LOG across a
    // RECORD_LOG acquisition here is the opposite order, which is how
    // deadlocks of this shape get built.
    let children: Vec<u16> = {
        let runlog = RUN_LOG.read().unwrap_or_else(|e| e.into_inner());
        match runlog.iter().find(|r| r.id == id) {
            Some(r) => run_children(r),
            None => return false,
        }
    };

    let mut ok = true;

    // Children before the parent: if the run entry disappeared first and a
    // child unlink then failed, nothing would reference that record again and
    // its capture would sit on the card unreachable.
    {
        let mut reclog = RECORD_LOG.write().unwrap_or_else(|e| e.into_inner());
        for rid in children {
            if delete_record_files(rid) {
                reclog.retain(|r| r.rid != rid);
            } else {
                ok = false;
            }
        }
    }

    // Then the run itself.
    if !unlink(&Path::new("/dvr/runs").join(format!("{:04X}.run", id))) {
        return false;
    }

    RUN_LOG
        .write()
        .unwrap_or_else(|e| e.into_inner())
        .retain(|r| r.id != id);

    ok
}

pub fn gen_run_number() -> u16 {
    let mut taken: Vec<u16> = {
        let runs = RUN_LOG.read().unwrap();
        runs.iter().map(|r| r.id).collect()
    };                                   // read guard dropped here
    taken.sort_unstable();

    let mut next: u32 = 0;
    for id in taken {
        match u32::from(id) {
            n if n == next => next += 1,  // contiguous, keep going
            n if n > next => break,       // found the gap
            _ => {}                       // duplicate, ignore
        }
    }
    next as u16
}

pub fn gen_record_number() -> u16 {
    let log = RECORD_LOG.read().unwrap();
    loop {
        let rid: u16 = tools::rand_u16();
        if !log.iter().any(|r| r.rid == rid) {
            return rid;
        }
    }
}