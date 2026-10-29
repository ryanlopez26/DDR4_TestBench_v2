use std::{path::Path, sync::RwLock, time::Instant};
use serde::{Deserialize, Serialize};

use crate::{PM, archive, beam, hibernate, power, record::{self, Record}, gpio, tools::{self, get_epoch}, types::ConfigCmd};
use std::{thread, time::Duration};
use std::fs::{self, File};
use beam::*;
use std::io::{BufWriter, Write};

// ============================================================================
// Tunables
// ============================================================================

/// Bytes moved per chip transaction batch. Everything in the engine — the
/// compare, the capture write, the progress checkpoint, the beam poll — now
/// happens once per chunk instead of once per byte.
///
/// 4 KB keeps `actual` and `expected` in L1 together. Raising it reduces
/// per-chunk overhead further but coarsens progress reporting and, in Dynamic,
/// lengthens the write-to-read retention window (see the note on that arm).
const CHUNK: usize = 4096;

/// Capture file write buffer. Fixed, NOT derived from block_size — the old
/// `BufWriter::with_capacity(config.block_size as usize, ..)` allocated a
/// buffer as large as the configured block, which at the 1 GB block option
/// tried to allocate 1 GiB on a 2 GB board.
const CAPTURE_BUF_BYTES: usize = 1 << 20;

/// false = dense capture (one XOR byte per byte verified).
/// true  = sparse capture (4-byte LE address + 1 XOR byte, errors only).
///
/// Dense writes one capture byte per byte read, so a 1 GB sample produces a
/// 1 GB file. At SD 2.0 speeds (~15 MB/s through the ZCU104's level shifter)
/// that is ~67 s/GB of pure I/O, and it hits the FAT32 4 GiB file ceiling at a
/// 4 GB sample. Sparse is four orders of magnitude smaller in a clean run.
/// Leave false until the C# RecordViewer parses the sparse layout.
const SPARSE_CAPTURE: bool = false;

// PARAMS
const UPDATE_FREQUENCY: Duration = Duration::from_millis(200);
const SIGNAL_HOLD_REQUIREMENT: Duration = Duration::from_millis(200);
const TERMINATION_DELAY_FOR_SEFI_TEST: Duration = Duration::from_millis(3000);

/// Poll interval while parked waiting for beam. The old code spun the full
/// per-byte loop with `have_started == false`, burning a core to do nothing.
const BEAM_WAIT_POLL: Duration = Duration::from_millis(2);

// ============================================================================
// Bit-statistics helpers
// ============================================================================

/// Adjacency-run lookup table, indexed by the 8-bit XOR mask.
///
/// Replaces the per-byte `for i in 0..8` scan. `LUT[m][c]` for c >= 1 is the
/// number of maximal runs of length c in `m & (m >> 1)`; `LUT[m][0]` is 1 when
/// m has exactly one bit set, mirroring the original's "optimization trick"
/// branch. Max run in an 8-bit adjacency mask is 7, so index 7 is the ceiling
/// and [u64; 8] cannot overflow.
static ADJ_LUT: [[u8; 8]; 256] = build_adj_lut();

const fn build_adj_lut() -> [[u8; 8]; 256] {
    let mut lut = [[0u8; 8]; 256];
    let mut m = 1usize;

    while m < 256 {
        // m & (m - 1) == 0 means exactly one bit set, i.e. no adjacency possible.
        if m & (m - 1) == 0 {
            lut[m][0] = 1;
        } else {
            let mask = m as u8;
            let adj = mask & (mask >> 1);

            let mut i = 0usize;
            let mut run = 0usize;
            while i < 8 {
                if adj & (1 << i) != 0 {
                    run += 1;
                } else if run > 0 {
                    lut[m][run] += 1;
                    run = 0;
                }
                i += 1;
            }
            if run > 0 {
                lut[m][run] += 1;
            }
        }
        m += 1;
    }

    lut
}

/// Score one non-zero XOR mask into the running statistics.
#[inline(always)]
fn score_mask(
    m: usize,
    correct: &mut u64,
    incorrect: &mut u64,
    err_bins: &mut [u64; 9],
    adj_bins: &mut [u64; 8],
) {
    let bits = (m as u8).count_ones() as usize;
    *correct += 8 - bits as u64;
    *incorrect += bits as u64;
    err_bins[bits] += 1;

    let runs = &ADJ_LUT[m];
    let mut c = 0;
    while c < 8 {
        if runs[c] != 0 {
            adj_bins[c] += runs[c] as u64;
        }
        c += 1;
    }
}

/// The byte this address should hold under the configured pattern.
#[inline(always)]
fn pattern_byte(pattern: u8, addr: u64) -> u8 {
    match pattern {
        0 => 0x00,
        1 => 0xFF,
        2 => crate::rand::rand(addr as u32),
        _ => 0x00,
    }
}

/// True when the pattern is address-independent, so the expected buffer can be
/// filled once for the whole run instead of per chunk. Patterns 0, 1 and any
/// invalid value are constant; only PRNG needs per-address work.
#[inline]
fn pattern_is_constant(pattern: u8) -> bool {
    pattern != 2
}

/// Materialize the expected bytes for [base, base + buf.len()).
///
/// Patterns 0 and 1 compile to a vectorized memset. Pattern 2 has to call
/// rand() per address, which is why PRNG runs stay slower than solid patterns.
#[inline]
fn fill_expected(pattern: u8, base: u64, buf: &mut [u8]) {
    match pattern {
        0 => buf.fill(0x00),
        1 => buf.fill(0xFF),
        2 => {
            for (i, b) in buf.iter_mut().enumerate() {
                *b = crate::rand::rand((base + i as u64) as u32);
            }
        }
        _ => buf.fill(0x00),
    }
}

// ============================================================================
// Chip access
//
// THESE TWO FUNCTIONS ARE THE REMAINING BOTTLENECK. Everything else in this
// file is now amortized per chunk, but each of these still issues one
// chip::read / chip::write per byte. If chip.rs can expose a burst that walks
// the AXI window with copy_nonoverlapping or 64-bit volatile accesses, drop it
// in here and the whole engine speeds up by whatever that burst is worth.
// Nothing else needs to change.
// ============================================================================

/// Read into `buf`. Returns how many leading bytes are valid.
#[inline]
fn read_chunk(config: &ConfigCmd, base: u64, buf: &mut [u8]) -> usize {
    let mut got = 0usize;
    for (i, b) in buf.iter_mut().enumerate() {
        match crate::chip::read(config, (base + i as u64) as u32) {
            Ok(v) => {
                *b = v;
                got += 1;
            }
            // The original swallowed read errors and fell through comparing a
            // stale buffer value as though it were data. Stop the chunk so the
            // byte is not scored at all.
            Err(_) => break,
        }
    }
    got
}

/// Write `buf`. Returns how many leading bytes were accepted.
#[inline]
fn write_chunk(config: &ConfigCmd, base: u64, buf: &[u8]) -> usize {
    let mut put = 0usize;
    for (i, b) in buf.iter().enumerate() {
        match crate::chip::write(config, (base + i as u64) as u32, *b) {
            Ok(_) => put += 1,
            Err(_) => break,
        }
    }
    put
}

// ============================================================================
// Task request / engine state
// ============================================================================

// Task request
#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct TaskRequest {

    //Type of record that is desired
    pub rtype: record::RecordType,

    //UUID to give the record
    pub rid: u16
}

// Engine command

pub struct EngineCommand {

    // Task queue
    pub task_queue: Vec<TaskRequest>,

    // Task abort (abandon the current task)
    pub abort_task: bool,

}


// Engine status - only the engine should write to this
pub struct EngineStatus {

    // Record of the request currently underway
    pub active_record: Option<record::Record>,

    // Status of the engine
    pub is_idle: bool,
    pub task_runtime: f64,
    pub task_progress: f64
}

// Global vars for engine

pub static CMD: RwLock<EngineCommand> = RwLock::new(EngineCommand { task_queue: vec![], abort_task: false });

pub static STATUS: RwLock<EngineStatus> = RwLock::new(
    EngineStatus{
        active_record: None,
        is_idle: true,
        task_runtime: 0.0,
        task_progress: 100.0,
    }
);

// Abort checker
fn abort_check() -> bool {

    //Obtain lock on command variable
    let cmd = CMD.read().unwrap();

    // Return status
    cmd.abort_task

}

// ============================================================================
// Checkpoint plumbing
// ============================================================================

/// Publish a snapshot of the record to STATUS.
///
/// The clone happens BEFORE the write lock is taken. The old code cloned
/// inside the lock, and a Record carries nine Vecs that each grow by one
/// element per checkpoint — so the copy cost rose linearly with run length
/// while every status reader was blocked behind it. A ten-minute run at 200 ms
/// checkpoints is 3000 iterations, each copying up to 3000 elements per vector.
fn publish(r: &Record, progress: f64) {
    let snapshot = r.clone();
    let runtime = (get_epoch() - r.time_started) as f64;

    let mut s = STATUS.write().unwrap();
    s.active_record = Some(snapshot);
    s.task_progress = progress;
    s.task_runtime = runtime;
}

/// Close out a record that is being abandoned mid-flight and submit whatever
/// was gathered. Flushes the capture first — the old abort paths in Read,
/// Write and Dynamic jumped straight to `continue 'main`, discarding up to a
/// full BufWriter of capture data.
fn abort_record(r: &mut Record, cap: &mut Option<BufWriter<File>>) {
    if let Some(c) = cap.as_mut() {
        let _ = c.flush();
    }

    r.time_ended = get_epoch();
    r.time_duration = r.time_ended - r.time_started;
    r.aborted = true;

    archive::submit_record(r.clone());
}

/// Open the capture file for a record, creating /dvr/captures if the partition
/// is freshly formatted.
///
/// Returns None rather than panicking. The old `File::create(..).unwrap()` took
/// the whole engine thread down when /dvr/captures did not exist — which is the
/// normal state of a newly formatted FAT32 data partition — and again whenever
/// the card filled up.
fn open_capture(rid: u16) -> Option<BufWriter<File>> {
    let dir = Path::new("/dvr/captures");

    if let Err(e) = fs::create_dir_all(dir) {
        eprintln!("[!] cannot create {}: {}", dir.display(), e);
        return None;
    }

    match File::create(dir.join(format!("{:04X}.cap", rid))) {
        Ok(f) => Some(BufWriter::with_capacity(CAPTURE_BUF_BYTES, f)),
        Err(e) => {
            eprintln!("[!] cannot create capture file for {:04X}: {}", rid, e);
            None
        }
    }
}

/// Append to the capture, reporting failure instead of panicking.
#[inline]
fn cap_write(cap: &mut Option<BufWriter<File>>, bytes: &[u8]) -> bool {
    match cap.as_mut() {
        Some(c) => match c.write_all(bytes) {
            Ok(_) => true,
            Err(e) => {
                eprintln!("[!] capture write failed: {}", e);
                false
            }
        },
        None => true,
    }
}

// ============================================================================
// Engine
// ============================================================================

// Main of the engine thread
pub fn engine_main(){

    // Active record to work on
    let mut rec: Option<Record> = None;

    // Last abort check
    let mut last_update = Instant::now();

    // Local copy of the config
    let mut config: ConfigCmd;

    // Reusable chip buffers. Allocated once for the life of the thread rather
    // than per task; CHUNK is a compile-time constant so these are stack
    // arrays, no allocator traffic in the hot path at all.
    let mut actual = [0u8; CHUNK];
    let mut expected = [0u8; CHUNK];

    // Begin main process loop
    'main: loop{

        //Clear the status
        {
            let mut sts = STATUS.write().unwrap();

            sts.active_record = None;
            sts.is_idle = true;
            sts.task_progress = 0.0;
            sts.task_runtime = 0.0;
        }

        // Check if we need to change power states
        if PM.read().unwrap().power_request > 0 {

            // Power change requested!

            // Only act once. The old version left power_request set, so this
            // whole block — unmount, hibernate::save(), safe=true — re-ran on
            // every pass of the idle loop, rewriting the hibernate file once a
            // second for as long as the board stayed in safe mode.
            if !PM.read().unwrap().safe {

                // Umount disks
                tools::unmount_dvr();

                // Hibernate the queue
                hibernate::save();

                // Mark the system as safe now
                PM.write().unwrap().safe = true;
            }

        }

        //Check the queue for a task to do
        {
            //Obtain an updated configuration
            {
                config = crate::config::CONFIG.read().unwrap().clone();
            }

            //Lock cmd variable
            let mut cmd = CMD.write().unwrap();

            // Reinitialize the system
            cmd.abort_task = false;

            // Check if there are any non-start conditions, which include:
            // No tasks queued, disk is in safe mode (unmounted), or the FPGA isn't setup (prevent deadlock)
            if cmd.task_queue.len() == 0 || PM.read().unwrap().safe || !gpio::get_calibration_signal() {

                // Nothing to do, release lock and wait
                drop(cmd);

                //Wait then try again
                thread::sleep(Duration::from_millis(1000));

                continue;

            }

            // There is a task request, perform it
            rec = Some(Record {
                time_started: get_epoch(),
                time_ended: 0,
                time_duration: 0,
                rid: cmd.task_queue.first().unwrap().rid,
                rtype: cmd.task_queue.first().unwrap().rtype,
                completed: false,
                aborted: false,
                adj_err_bins: Some([0,0,0,0,0,0,0,0]),
                err_bins: Some([0,0,0,0,0,0,0,0,0]),
                time_to_sefi: None,
                exposure_time: None,
                log_timestamp: Vec::new(),
                log_error_rate: Vec::new(),
                log_current_address: Vec::new(),
                log_beam_request: Vec::new(),
                log_sefi_detected: Vec::new(),
                correct_bits: 0,
                incorrect_bits: 0,
                processed_bits: 0,
                log_bits_processed: Vec::new(),
                log_bits_incorrect: Vec::new(),
                log_bits_correct: Vec::new(),
                log_adj_err_bins: Vec::new(),
                log_err_bins: Vec::new(),
                log_beam_active: Vec::new(),
                log_pmic: Vec::new(),
            });

            //Remove the task from the queue
            cmd.task_queue.remove(0);

        }

        // Begin task
        match rec {
            Some(mut r) => {

                //We are now busy
                STATUS.write().unwrap().is_idle = false;

                // ============ Geometry, validated once ============
                //
                // step_by(0) panics, and a zero block_size or num_blocks makes
                // the memory arms no-ops and the Dynamic arm spin forever on an
                // empty inner loop. The C# client floors block_factor at 1, but
                // the server cannot rely on the client being the only writer.
                let factor = config.block_factor.max(1) as usize;
                let blk_size = config.block_size as u64;
                let num_blocks = config.num_blocks as u64;
                let total_bytes = num_blocks.saturating_mul(blk_size);

                let geometry_ok = blk_size > 0 && num_blocks > 0;
                let pattern = config.data_pattern;
                let logging = config.enable_logging;

                if !geometry_ok && !matches!(r.rtype, record::RecordType::Expose) {
                    eprintln!(
                        "[!] rejecting {:04X}: block_size={} num_blocks={}",
                        r.rid, blk_size, num_blocks
                    );
                    r.time_ended = get_epoch();
                    r.time_duration = r.time_ended - r.time_started;
                    r.aborted = true;
                    archive::submit_record(r);
                    continue 'main;
                }

                if pattern > 2 {
                    // The old Write arm did a bare `return;` here, which exits
                    // engine_main() entirely and kills the engine thread for the
                    // life of the process — every later task would sit in the
                    // queue forever with no indication why. Fail the record.
                    eprintln!("[!] invalid data_pattern {} for {:04X}", pattern, r.rid);
                    r.time_ended = get_epoch();
                    r.time_duration = r.time_ended - r.time_started;
                    r.aborted = true;
                    archive::submit_record(r);
                    continue 'main;
                }

                // ============ Capture file, opened only if used ============
                //
                // Expose writes no capture at all, and Verify/Dynamic only when
                // logging is on. The old code created and left a zero-length
                // .cap for every record regardless.
                let needs_capture = match r.rtype {
                    record::RecordType::Read => true,
                    record::RecordType::Verify | record::RecordType::Dynamic => logging,
                    record::RecordType::Write | record::RecordType::Expose => false,
                };

                let mut cap_buf: Option<BufWriter<File>> =
                    if needs_capture { open_capture(r.rid) } else { None };

                if needs_capture && cap_buf.is_none() {
                    eprintln!("[!] {:04X}: continuing with capture disabled", r.rid);
                }

                // Record start time
                r.time_started = get_epoch();

                // Start instant
                let start = Instant::now();

                // Progress denominator, guarded against the zero case above.
                let progress_scale = if total_bytes > 0 {
                    100.0 / total_bytes as f64
                } else {
                    0.0
                };

                //Split record based on operation that needs to be performed
                match r.rtype {

                    // ================================================== READ
                    record::RecordType::Read => {

                        'read: for blk_ind in (0..num_blocks).step_by(factor) {

                            // u64 throughout. The original computed
                            // blk_ind * block_size in u32, which wraps at a
                            // 4 GiB extent and silently re-reads block 0.
                            let blk_start = blk_ind * blk_size;
                            let blk_end = blk_start + blk_size;

                            let mut at = blk_start;
                            while at < blk_end {
                                let n = CHUNK.min((blk_end - at) as usize);
                                let got = read_chunk(&config, at, &mut actual[..n]);

                                if got > 0 {
                                    if !cap_write(&mut cap_buf, &actual[..got]) { break 'read; }
                                    r.processed_bits += got as u64 * 8;
                                }

                                if got < n {
                                    // Unreadable tail: mark it and fill the
                                    // capture so file offsets still map to
                                    // addresses one-to-one.
                                    let missing = n - got;
                                    expected[..missing].fill(0xFE);
                                    if !cap_write(&mut cap_buf, &expected[..missing]) { break 'read; }
                                    r.incorrect_bits += missing as u64 * 8;
                                }

                                at += n as u64;

                                if last_update.elapsed() > UPDATE_FREQUENCY {
                                    last_update = Instant::now();

                                    r.log_timestamp.push(start.elapsed().as_secs_f64());
                                    r.log_bits_incorrect.push(r.incorrect_bits);
                                    r.log_bits_correct.push(r.correct_bits);
                                    r.log_bits_processed.push(r.processed_bits);
                                    r.log_beam_active.push(beam::is_beam_active());
                                    r.log_beam_request.push(beam::get_beam_request());
                                    r.log_pmic.push(power::read_sodimm_power());
                                    r.log_current_address.push(at as u32);

                                    publish(&r, at as f64 * progress_scale);

                                    if abort_check() {
                                        abort_record(&mut r, &mut cap_buf);
                                        continue 'main;
                                    }
                                }
                            }
                        }
                    },

                    // ================================================ VERIFY
                    record::RecordType::Verify => {

                        // Accumulate in locals. Going through
                        // `r.err_bins.as_mut().unwrap()[i] += 1` forced a
                        // load/store through the Option on every byte; these
                        // stay in registers and merge into the record at each
                        // checkpoint.
                        let mut correct: u64 = r.correct_bits;
                        let mut incorrect: u64 = r.incorrect_bits;
                        let mut err_bins = r.err_bins.unwrap_or([0u64; 9]);
                        let mut adj_bins = r.adj_err_bins.unwrap_or([0u64; 8]);

                        // For a constant pattern the expected buffer never
                        // changes, so fill it once for the whole run instead of
                        // once per chunk.
                        let constant = pattern_is_constant(pattern);
                        if constant {
                            fill_expected(pattern, 0, &mut expected);
                        }

                        let mut sparse: Vec<u8> = if SPARSE_CAPTURE && cap_buf.is_some() {
                            Vec::with_capacity(CHUNK * 5)
                        } else {
                            Vec::new()
                        };

                        'verify: for blk_ind in (0..num_blocks).step_by(factor) {

                            let blk_start = blk_ind * blk_size;
                            let blk_end = blk_start + blk_size;

                            let mut at = blk_start;
                            while at < blk_end {
                                let n = CHUNK.min((blk_end - at) as usize);

                                let got = read_chunk(&config, at, &mut actual[..n]);
                                if got == 0 {
                                    // Whole chunk unreadable; advance rather
                                    // than spinning on the same address.
                                    at += n as u64;
                                    continue;
                                }

                                if !constant {
                                    fill_expected(pattern, at, &mut expected[..got]);
                                }

                                if SPARSE_CAPTURE {
                                    sparse.clear();
                                }

                                // Compare eight bytes at a time. In a clean run
                                // every word takes the x == 0 path and the
                                // per-byte decomposition never runs.
                                let words = got / 8;
                                for w in 0..words {
                                    let i = w * 8;
                                    let a = u64::from_ne_bytes(actual[i..i + 8].try_into().unwrap());
                                    let e = u64::from_ne_bytes(expected[i..i + 8].try_into().unwrap());
                                    let x = a ^ e;

                                    if x == 0 {
                                        correct += 64;
                                        continue;
                                    }

                                    for b in 0..8 {
                                        let m = ((x >> (b * 8)) & 0xFF) as usize;

                                        if m == 0 {
                                            correct += 8;
                                            continue;
                                        }

                                        if SPARSE_CAPTURE {
                                            let addr = (at + (i + b) as u64) as u32;
                                            sparse.extend_from_slice(&addr.to_le_bytes());
                                            sparse.push(m as u8);
                                        }

                                        score_mask(m, &mut correct, &mut incorrect,
                                                   &mut err_bins, &mut adj_bins);
                                    }
                                }

                                // Tail when the chunk is not a multiple of 8.
                                for i in (words * 8)..got {
                                    let m = (actual[i] ^ expected[i]) as usize;

                                    if m == 0 {
                                        correct += 8;
                                        continue;
                                    }

                                    if SPARSE_CAPTURE {
                                        let addr = (at + i as u64) as u32;
                                        sparse.extend_from_slice(&addr.to_le_bytes());
                                        sparse.push(m as u8);
                                    }

                                    score_mask(m, &mut correct, &mut incorrect,
                                               &mut err_bins, &mut adj_bins);
                                }

                                // Capture, once per chunk rather than per byte.
                                if cap_buf.is_some() {
                                    if SPARSE_CAPTURE {
                                        if !sparse.is_empty()
                                            && !cap_write(&mut cap_buf, &sparse) { break 'verify; }
                                    } else {
                                        // Dense: XOR the chunk in place into the
                                        // expected buffer and write it as one
                                        // block. The old code issued a
                                        // write_all of a single byte per byte.
                                        for i in 0..got {
                                            expected[i] ^= actual[i];
                                        }
                                        if !cap_write(&mut cap_buf, &expected[..got]) { break 'verify; }

                                        // XOR corrupted the expected buffer, so
                                        // it has to be rebuilt either way now.
                                        if constant {
                                            fill_expected(pattern, 0, &mut expected[..got]);
                                        }
                                    }
                                }

                                at += got as u64;

                                // Checkpoint moved from per-byte to per-chunk.
                                // Instant::now()/elapsed() is a vDSO
                                // clock_gettime at 20-30 ns on an A53 — called
                                // once per byte it was a large fraction of the
                                // entire loop cost.
                                if last_update.elapsed() > UPDATE_FREQUENCY {
                                    last_update = Instant::now();

                                    r.correct_bits = correct;
                                    r.incorrect_bits = incorrect;
                                    r.processed_bits = correct + incorrect;
                                    r.err_bins = Some(err_bins);
                                    r.adj_err_bins = Some(adj_bins);

                                    r.log_timestamp.push(start.elapsed().as_secs_f64());
                                    r.log_bits_incorrect.push(incorrect);
                                    r.log_bits_correct.push(correct);
                                    r.log_bits_processed.push(r.processed_bits);
                                    r.log_beam_active.push(beam::is_beam_active());
                                    r.log_beam_request.push(beam::get_beam_request());
                                    r.log_pmic.push(power::read_sodimm_power());
                                    r.log_current_address.push(at as u32);
                                    r.log_err_bins.push(err_bins);
                                    r.log_adj_err_bins.push(adj_bins);

                                    publish(&r, at as f64 * progress_scale);

                                    if abort_check() {
                                        abort_record(&mut r, &mut cap_buf);
                                        continue 'main;
                                    }
                                }
                            }
                        }

                        // Final merge — the last partial interval never hit a
                        // checkpoint, so without this the record under-reports
                        // everything gathered since the previous one.
                        r.correct_bits = correct;
                        r.incorrect_bits = incorrect;
                        r.processed_bits = correct + incorrect;
                        r.err_bins = Some(err_bins);
                        r.adj_err_bins = Some(adj_bins);
                    },

                    // ================================================= WRITE
                    record::RecordType::Write => {

                        let constant = pattern_is_constant(pattern);
                        if constant {
                            fill_expected(pattern, 0, &mut expected);
                        }

                        for blk_ind in (0..num_blocks).step_by(factor) {

                            let blk_start = blk_ind * blk_size;
                            let blk_end = blk_start + blk_size;

                            let mut at = blk_start;
                            while at < blk_end {
                                let n = CHUNK.min((blk_end - at) as usize);

                                if !constant {
                                    fill_expected(pattern, at, &mut expected[..n]);
                                }

                                let put = write_chunk(&config, at, &expected[..n]);

                                r.processed_bits += put as u64 * 8;
                                if put < n {
                                    r.incorrect_bits += (n - put) as u64 * 8;
                                }

                                at += n as u64;

                                if last_update.elapsed() > UPDATE_FREQUENCY {
                                    last_update = Instant::now();

                                    r.log_timestamp.push(start.elapsed().as_secs_f64());
                                    r.log_beam_active.push(beam::is_beam_active());
                                    r.log_beam_request.push(beam::get_beam_request());
                                    r.log_pmic.push(power::read_sodimm_power());
                                    r.log_bits_incorrect.push(r.incorrect_bits);
                                    r.log_bits_processed.push(r.processed_bits);
                                    r.log_bits_correct.push(r.correct_bits);
                                    r.log_current_address.push(at as u32);
                                    r.log_err_bins.push(r.err_bins.unwrap_or([0u64; 9]));
                                    r.log_adj_err_bins.push(r.adj_err_bins.unwrap_or([0u64; 8]));

                                    publish(&r, at as f64 * progress_scale);

                                    if abort_check() {
                                        abort_record(&mut r, &mut cap_buf);
                                        continue 'main;
                                    }
                                }
                            }
                        }
                    },

                    // =============================================== DYNAMIC
                    record::RecordType::Dynamic => {

                        // ---- Phase 1: park until beam, without burning a core.
                        //
                        // The old version ran the full per-byte loop with
                        // have_started == false, testing the GPIO and the
                        // elapsed timer on every iteration of a loop that did
                        // no memory work at all.
                        let mut b_last_low = Instant::now();
                        let mut parked = true;

                        while parked {
                            let b = beam::is_beam_active();

                            if !b {
                                b_last_low = Instant::now();
                                if !get_beam_request() {
                                    set_beam_request(true);
                                }
                            } else if b_last_low.elapsed() > SIGNAL_HOLD_REQUIREMENT {
                                parked = false;
                                break;
                            }

                            if last_update.elapsed() > UPDATE_FREQUENCY {
                                last_update = Instant::now();

                                r.log_timestamp.push(start.elapsed().as_secs_f64());
                                r.log_beam_active.push(beam::is_beam_active());
                                r.log_beam_request.push(beam::get_beam_request());
                                r.log_pmic.push(power::read_sodimm_power());
                                r.log_sefi_detected.push(false);

                                publish(&r, 0.0);

                                if abort_check() {
                                    abort_record(&mut r, &mut cap_buf);
                                    continue 'main;
                                }
                            }

                            thread::sleep(BEAM_WAIT_POLL);
                        }

                        // ---- Phase 2: the test proper.
                        let beam_start_instant = Instant::now();
                        let mut b_last_high = Instant::now();
                        let mut last_below_threshold = Instant::now();

                        let mut samples_total: u64 = 0;
                        let mut samples_bad: u64 = 0;

                        let mut correct: u64 = r.correct_bits;
                        let mut incorrect: u64 = r.incorrect_bits;
                        let mut err_bins = r.err_bins.unwrap_or([0u64; 9]);
                        let mut adj_bins = r.adj_err_bins.unwrap_or([0u64; 8]);

                        let constant = pattern_is_constant(pattern);
                        if constant {
                            fill_expected(pattern, 0, &mut expected);
                        }

                        'dynamic: loop {
                            for blk_ind in (0..num_blocks).step_by(factor) {

                                let blk_start = blk_ind * blk_size;
                                let blk_end = blk_start + blk_size;

                                let mut at = blk_start;
                                while at < blk_end {
                                    let n = CHUNK.min((blk_end - at) as usize);

                                    if !constant {
                                        fill_expected(pattern, at, &mut expected[..n]);
                                    }

                                    // Write the chunk, then read it back. See
                                    // the retention-window note below — this is
                                    // the one place chunking changes the physics
                                    // of the measurement, not just its speed.
                                    let put = write_chunk(&config, at, &expected[..n]);
                                    let got = read_chunk(&config, at, &mut actual[..put.min(n)]);

                                    if got < put {
                                        samples_bad += (put - got) as u64;
                                    }
                                    samples_total += put as u64;

                                    let words = got / 8;
                                    for w in 0..words {
                                        let i = w * 8;
                                        let a = u64::from_ne_bytes(actual[i..i + 8].try_into().unwrap());
                                        let e = u64::from_ne_bytes(expected[i..i + 8].try_into().unwrap());
                                        let x = a ^ e;

                                        if x == 0 {
                                            correct += 64;
                                            continue;
                                        }

                                        for b in 0..8 {
                                            let m = ((x >> (b * 8)) & 0xFF) as usize;
                                            if m == 0 {
                                                correct += 8;
                                                continue;
                                            }
                                            samples_bad += 1;
                                            score_mask(m, &mut correct, &mut incorrect,
                                                       &mut err_bins, &mut adj_bins);
                                        }
                                    }

                                    for i in (words * 8)..got {
                                        let m = (actual[i] ^ expected[i]) as usize;
                                        if m == 0 {
                                            correct += 8;
                                            continue;
                                        }
                                        samples_bad += 1;
                                        score_mask(m, &mut correct, &mut incorrect,
                                                   &mut err_bins, &mut adj_bins);
                                    }

                                    if cap_buf.is_some() && got > 0 {
                                        for i in 0..got {
                                            expected[i] ^= actual[i];
                                        }
                                        if !cap_write(&mut cap_buf, &expected[..got]) {
                                            break 'dynamic;
                                        }
                                        if constant {
                                            fill_expected(pattern, 0, &mut expected[..got]);
                                        }
                                    }

                                    at += n as u64;

                                    // Beam poll, once per chunk. At 4 KB this is
                                    // still far finer than the 200 ms debounce.
                                    let b = beam::is_beam_active();
                                    if b {
                                        b_last_high = Instant::now();
                                    }

                                    if last_update.elapsed() > UPDATE_FREQUENCY {
                                        last_update = Instant::now();

                                        // Guard the divide. samples_total is 0
                                        // if the first checkpoint lands before
                                        // the first chunk completes, and the old
                                        // code produced NaN there, which then
                                        // went into log_error_rate.
                                        let ratio = if samples_total > 0 {
                                            samples_bad as f64 / samples_total as f64
                                        } else {
                                            0.0
                                        };

                                        r.exposure_time =
                                            Some(beam_start_instant.elapsed().as_secs_f64());

                                        if ratio >= config.sefi_threshold {
                                            if last_below_threshold.elapsed() > SIGNAL_HOLD_REQUIREMENT
                                                && r.time_to_sefi.is_none()
                                            {
                                                // Latch on FIRST detection. The
                                                // old code reassigned every
                                                // checkpoint while the ratio
                                                // stayed high, so time_to_sefi
                                                // reported the last sample above
                                                // threshold rather than the
                                                // onset — which is the number the
                                                // cross-section fit needs.
                                                r.time_to_sefi =
                                                    Some(beam_start_instant.elapsed().as_secs_f64());

                                                //Disable beam (prevent latch up)
                                                set_beam_request(false);
                                            }
                                        } else {
                                            last_below_threshold = Instant::now();
                                        }

                                        r.correct_bits = correct;
                                        r.incorrect_bits = incorrect;
                                        r.processed_bits = correct + incorrect;
                                        r.err_bins = Some(err_bins);
                                        r.adj_err_bins = Some(adj_bins);

                                        r.log_timestamp.push(start.elapsed().as_secs_f64());
                                        r.log_error_rate.push(ratio);
                                        r.log_bits_incorrect.push(incorrect);
                                        r.log_bits_correct.push(correct);
                                        r.log_bits_processed.push(r.processed_bits);
                                        r.log_beam_active.push(beam::is_beam_active());
                                        r.log_beam_request.push(beam::get_beam_request());
                                        r.log_pmic.push(power::read_sodimm_power());
                                        r.log_current_address.push(at as u32);
                                        r.log_err_bins.push(err_bins);
                                        r.log_adj_err_bins.push(adj_bins);
                                        r.log_sefi_detected.push(r.time_to_sefi.is_some());

                                        publish(&r, at as f64 * progress_scale);

                                        if abort_check() {
                                            abort_record(&mut r, &mut cap_buf);
                                            continue 'main;
                                        }
                                    }

                                    //Check if test should be ended
                                    if !b && b_last_high.elapsed() > TERMINATION_DELAY_FOR_SEFI_TEST {
                                        break 'dynamic;
                                    }
                                }
                            }
                        }

                        r.correct_bits = correct;
                        r.incorrect_bits = incorrect;
                        r.processed_bits = correct + incorrect;
                        r.err_bins = Some(err_bins);
                        r.adj_err_bins = Some(adj_bins);

                        // Check if we should conclude the test or reboot the system to perform another (special mode)
                        if config.automated_sefi_mode {

                            //Request a reboot
                            PM.write().unwrap().power_request = 2;


                        }

                    },

                    // ================================================ EXPOSE
                    record::RecordType::Expose => {

                        //Initialize timing variables
                        let mut active_last_low = Instant::now();
                        let mut active_last_high = Instant::now();

                        // Accumulated beam-on time, and the instant the current
                        // beam-on interval began.
                        //
                        // The old version accumulated in the beam-ABSENT branch
                        // using a timer it reset in that same branch, so
                        // exposure_total summed the loop period while beam was
                        // off plus each on-interval — converging on total
                        // wall-clock runtime rather than exposure. A record that
                        // sat 10 minutes with 30 s of beam reported ~10 minutes.
                        let mut exposure_total: Duration = Duration::new(0, 0);

                        //Delayed exit timer (used to allow signals to settle before ending the record)
                        let mut delayed_exit: Option<Instant> = None;

                        // We are ready to request beam!
                        beam::set_beam_request(true);

                        //Keep looping
                        loop {

                            //Update timers
                            if beam::is_beam_active() {
                                active_last_high = Instant::now();

                            } else {
                                active_last_low = Instant::now();
                            }

                            // Live total including the interval in progress.
                            let exposure_time = (exposure_total + active_last_low.elapsed());


                            //Check if we need to update
                            if last_update.elapsed() > UPDATE_FREQUENCY {

                                //Reset watchdog
                                last_update = Instant::now();

                                //Record datapoints
                                r.log_timestamp.push(start.elapsed().as_secs_f64());
                                r.log_beam_active.push(beam::is_beam_active());
                                r.log_beam_request.push(beam::get_beam_request());
                                r.log_pmic.push(power::read_sodimm_power());

                                // Percent, and against a millisecond limit.
                                // The old expression divided seconds by
                                // milliseconds and did not scale to 100, so the
                                // bar read 1000x low and as a fraction.
                                let progress = if config.exposure_time_limit_enabled
                                    && config.exposure_time_limit_ms > 0.0
                                {
                                    (exposure_time.as_secs_f64() * 1000.0
                                        / config.exposure_time_limit_ms as f64 * 100.0)
                                        .min(100.0)
                                } else {
                                    50.0
                                };

                                publish(&r, progress);

                                if abort_check() {
                                    abort_record(&mut r, &mut cap_buf);
                                    continue 'main;
                                }

                            }

                            //End the test if beam goes low
                            if active_last_high.elapsed() > SIGNAL_HOLD_REQUIREMENT && delayed_exit.is_none() {

                                //Halt beam
                                beam::set_beam_request(false);

                                //Finish test
                                delayed_exit = Some(Instant::now());

                            }

                            //End the test if exposure time limit is present and exceeded
                            if config.exposure_time_limit_enabled
                                && delayed_exit.is_none()
                                && config.exposure_time_limit_ms as f64
                                    <= exposure_total.as_secs_f64() * 1000.0
                            {                              

                                //Request clamp
                                set_beam_request(false);

                                //Set Exit
                                delayed_exit = Some(Instant::now());
                            }

                            //Check if ready to terminate record
                            if let Some(t) = delayed_exit {
                                if t.elapsed() > SIGNAL_HOLD_REQUIREMENT {
                                    //Exit
                                    break;
                                }
                            }

                            // This loop is pure signal watching; without a yield
                            // it spins a core at 100% for the whole exposure and
                            // starves the TCP thread on a 4-core board that is
                            // also running the status pushes.
                            thread::sleep(BEAM_WAIT_POLL);
                        }


                        r.exposure_time = Some((exposure_total + active_last_low.elapsed()).as_secs_f64());
                    },

                }

                //Mark finish times
                r.time_ended = get_epoch();
                r.time_duration = r.time_ended - r.time_started;
                r.completed = true;

                // Flush BEFORE submitting. The old order submitted the record
                // and then flushed, so a record could be published as complete
                // while up to a full buffer of its capture was still unwritten —
                // and the flush used .unwrap(), so a full card panicked the
                // engine thread right after claiming success.
                if let Some(mut c) = cap_buf.take() {
                    if let Err(e) = c.flush() {
                        eprintln!("[!] capture flush failed for {:04X}: {}", r.rid, e);
                        r.completed = false;
                    }
                }

                //Submit to archive
                archive::submit_record(r);

            },

            None => {},
        }

    }
}