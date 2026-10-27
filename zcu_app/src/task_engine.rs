use std::{path::Path, sync::RwLock, time::Instant};
use serde::{Deserialize, Serialize};

use crate::{PM, archive, gpio, hibernate, record::{self, Record}, tools::{self, get_epoch}, types::ConfigCmd};
use std::{thread, time::Duration};
use std::fs::File;
use std::io::{BufWriter, Write};


// PARAMS
const UPDATE_FREQUENCY: Duration = Duration::from_millis(200);
const SIGNAL_HOLD_REQUIREMENT: Duration = Duration::from_millis(200);
const TERMINATION_DELAY_FOR_SEFI_TEST: Duration = Duration::from_millis(3000);

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


// Main of the engine thread
pub fn engine_main(){

    // Active record to work on
    let mut rec: Option<Record> = None;

    // Last abort check
    let mut last_update = Instant::now();

    // Local copy of the config
    let mut config: ConfigCmd;

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

            // Umount disks
            tools::unmount_dvr();

            // Hibernate the queue
            hibernate::save();

            // Mark the system as safe now
            PM.write().unwrap().safe = true;

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
                log_beam_signal: Vec::new(),
                log_sefi_detected: Vec::new(),
                correct_bits: 0,
                incorrect_bits: 0,
                processed_bits: 0,
                log_bits_processed: Vec::new(),
                log_bits_incorrect: Vec::new(),
                log_bits_correct: Vec::new(),
                log_adj_err_bins: Vec::new(),
                log_err_bins: Vec::new(),
            });

            //Remove the task from the queue
            cmd.task_queue.remove(0);

        }

        // Begin task 
        match rec {
            Some(mut r) => {


                //We are now busy
                STATUS.write().unwrap().is_idle = false;
                
                // Create capture file for dump 
                let cap_path = Path::new("/dvr/captures").join(format!("{:04X}.cap", r.rid)); 
                let cap_file = File::create(&cap_path).unwrap();
                let mut cap_buf = BufWriter::with_capacity(config.block_size as usize, cap_file);

                // Record start time
                r.time_started = get_epoch();

                // Start instant
                let start = Instant::now();

                //Split record based on operation that needs to be performed
                match r.rtype {
                    record::RecordType::Read => {

                        //Iterate over every block in the dataset
                        for blk_ind in (0..config.num_blocks).step_by(config.block_factor as usize) {

                            // Compute virtual memory addresses 
                            let block_start = blk_ind * config.block_size ;
                            let block_end   = block_start + config.block_size;

                            for addr in block_start..block_end {
                              
                               //Copy into capture buffer
                                match crate::chip::read(&config, addr) {
                                    Ok(byte) => {
                                        cap_buf.write_all(&[byte]).unwrap();
                                    },
                                    Err(_) => {
                                        cap_buf.write_all(&[0xFE]).unwrap();
                                        r.incorrect_bits += 8;
                                    }
                                }

                                //Check if we need to update
                                if last_update.elapsed() > UPDATE_FREQUENCY {

                                    //Reset watchdog
                                    last_update = Instant::now();

                                    //Record datapoints
                                    r.log_timestamp.push(start.elapsed().as_secs_f64());
                                    r.log_bits_incorrect.push(r.incorrect_bits);
                                    r.log_beam_signal.push(gpio::get_beam_signal());
                                    r.log_current_address.push(block_end);
                                    r.log_bits_correct.push(r.correct_bits);

                                    //Lock the status struct
                                    let mut s = STATUS.write().unwrap();

                                    //Update the status struct
                                    s.active_record = Some(r.clone());
                                    s.task_progress = blk_ind as f64 / config.num_blocks as f64 * 100.0;
                                    s.task_runtime = (get_epoch() - r.time_started) as f64;

                                    //Check if we must abort
                                    if abort_check() {

                                        // Must abort right now
                                        r.time_ended = get_epoch();
                                        r.time_duration = r.time_ended - r.time_started;
                                        r.aborted = true;

                                        // Submit whatever we have
                                        archive::submit_record(r.clone());

                                        //Reset the FSM
                                        continue 'main;
                                    }

                                }
                                
                            }

                        }
                    
                    },
                    record::RecordType::Verify => {

                        // Iterate over number of blocks
                        for blk_ind in (0..config.num_blocks).step_by(config.block_factor as usize) {

                            //Iterate over bytes in block
                            for addr in (blk_ind * config.block_size)..((blk_ind + 1) * config.block_size){

                                //Expected value
                                let expected = match config.data_pattern {
                                    0 => 0, // All zeros
                                    1 => 0xFF, // All ones
                                    2 => crate::rand::rand(addr), // Pseudorandom pattern based on seed
                                    _ => {
                                        0
                                    }
                                };                              

                                //Verify the actual contents against the expected
                                match crate::chip::read(&config, addr) {
                                    Ok(actual) => {

                                        // Save the XOR'd version to the capture file
                                        cap_buf.write_all(&[actual ^ expected]).unwrap();


                                        if actual != expected {
                                            
                                            let diff_mask        = actual ^ expected;
                                            let diff_bits: usize = (diff_mask).count_ones() as usize;
                                            let adj_bits         = diff_mask & (diff_mask >> 1);


                                            //Overall metrics
                                            r.correct_bits += 8 - diff_bits as u64;
                                            r.incorrect_bits += diff_bits as u64;

                                            //Collect statistics
                                            r.err_bins.as_mut().unwrap()[diff_bits] += 1;
                                            
                                            //Detect multi-bit upset
                                            if diff_bits > 1 {

                                                //Counter 
                                                let mut c: usize = 0;

                                                //We need to count more carefully, this is CPU intensive though :(
                                                for i in 0..8 {
                                                    
                                                    //Check if bit is set
                                                    if adj_bits & (1 << i) != 0 {
                                                        
                                                        //Bit is set
                                                        c += 1;

                                                    } else {
                                                        
                                                        //Bit is not set

                                                        //Commit result if there is one
                                                        if c > 0 { 
                                                            r.adj_err_bins.as_mut().unwrap()[c] += 1; 
                                                            c = 0;
                                                        }
                                                    }
                                                    
                                                }
                                                
                                                //Commit just in case any pending result
                                                if c > 0 { 
                                                    r.adj_err_bins.as_mut().unwrap()[c] += 1; 
                                                }

                                            } else {
                                                //Optimization trick to prevent extensive check
                                                r.adj_err_bins.unwrap()[0] += 1;
                                            }

                                        } else {
                                            r.correct_bits += 8;
                                        }
                                    },
                                    Err(_e) => {
                                        
                                    }
                                };

                                //Check if we need to update
                                if last_update.elapsed() > UPDATE_FREQUENCY {

                                    //Reset watchdog
                                    last_update = Instant::now();

                                    //Record datapoints
                                    r.log_timestamp.push(start.elapsed().as_secs_f64());
                                    r.log_bits_incorrect.push(r.incorrect_bits);
                                    r.log_bits_correct.push(r.correct_bits);
                                    r.log_beam_signal.push(gpio::get_beam_signal());
                                    r.log_current_address.push(blk_ind * config.block_size);
                                    r.log_err_bins.push(r.err_bins.unwrap());
                                    r.log_adj_err_bins.push(r.adj_err_bins.unwrap());

                                    //Lock the status struct
                                    let mut s = STATUS.write().unwrap();

                                    //Update the status struct
                                    s.active_record = Some(r.clone());
                                    s.task_progress = blk_ind as f64 / config.num_blocks as f64 * 100.0;
                                    s.task_runtime = (get_epoch() - r.time_started) as f64;

                                    //Check if we must abort
                                    if abort_check() {

                                        // Must abort right now
                                        r.time_ended = get_epoch();
                                        r.time_duration = r.time_ended - r.time_started;
                                        r.aborted = true;

                                        // Submit whatever we have
                                        archive::submit_record(r.clone());

                                        //Reset the FSM
                                        continue 'main;
                                    }

                                }
                                

                            }

                        }

                    },
                    record::RecordType::Write => {

                        // Iterate over number of blocks
                        for blk_ind in (0..config.num_blocks).step_by(config.block_factor as usize) {

                            //Iterate over bytes in block
                            for addr in (blk_ind * config.block_size)..((blk_ind + 1) * config.block_size){

                                //Determine the required contents to write
                                match match config.data_pattern {
                                    0 => {
                                        // All zeros
                                        crate::chip::write(&config, addr, 0)
                                    },
                                    1 => {
                                        // All ones
                                        crate::chip::write(&config, addr, 0xFF)
                                    },
                                    2 => {
                                        // Pseudorandom pattern based on seed
                                        crate::chip::write(&config, addr, crate::rand::rand(addr))
                                    },
                                    _ => {
                                        eprintln!("[!] Invalid pattern in WriteCmd: {}", config.data_pattern);
                                        return;       
                                    }
                                }
                                {
                                    Ok(_) => {
                                        r.processed_bits += 8;
                                    },
                                    Err(_e) => {
                                        r.incorrect_bits += 8;
   
                                    }
                                }

                                //Check if we need to update
                                if last_update.elapsed() > UPDATE_FREQUENCY {

                                    //Reset watchdog
                                    last_update = Instant::now();

                                    //Record datapoints
                                    r.log_timestamp.push(start.elapsed().as_secs_f64());
                                    r.log_beam_signal.push(gpio::get_beam_signal());
                                    r.log_bits_incorrect.push(r.incorrect_bits);
                                    r.log_bits_processed.push(r.processed_bits);
                                    r.log_current_address.push(blk_ind * config.block_size);
                                    r.log_err_bins.push(r.err_bins.unwrap());
                                    r.log_adj_err_bins.push(r.adj_err_bins.unwrap());

                                    //Lock the status struct
                                    let mut s = STATUS.write().unwrap();

                                    //Update the status struct
                                    s.active_record = Some(r.clone());
                                    s.task_progress = blk_ind as f64 / config.num_blocks as f64 * 100.0;
                                    s.task_runtime = (get_epoch() - r.time_started) as f64;

                                    //Check if we must abort
                                    if abort_check() {

                                        // Must abort right now
                                        r.time_ended = get_epoch();
                                        r.time_duration = r.time_ended - r.time_started;
                                        r.aborted = true;

                                        // Submit whatever we have
                                        archive::submit_record(r.clone());

                                        //Reset the FSM
                                        continue 'main;
                                    }
                                }
                                
                            }


                        }

                    },
                    record::RecordType::Dynamic => {

                        // Wait for beam
                        let mut have_started = false;

                        // Beam timers (used to debounce)
                        let mut b_last_low = Instant::now();
                        let mut b_last_high = Instant::now();

                        // Threshold counters
                        let mut samples_total: u64 = 0;
                        let mut samples_bad: u64 = 0;

                        // Beam start time
                        let mut beam_start_instant = Instant::now();
                     
                        // Main process loop for dynamic process
                        'dynamic: loop {

                            // Iterate over number of blocks
                            for blk_ind in (0..config.num_blocks).step_by(config.block_factor as usize) {

                                //Iterate over bytes in block
                                for addr in (blk_ind * config.block_size)..((blk_ind + 1) * config.block_size){


                                    // Get beam signal 
                                    let b = gpio::get_beam_signal();

                                    // Update timers
                                    match b {
                                        true =>  {b_last_high = Instant::now();},
                                        false => {b_last_low  = Instant::now();},
                                    }

                                    // Check if we are waiting for beam capture
                                    if !have_started {

                                        // Wait for beam capture
                                        if b && b_last_low.elapsed() > SIGNAL_HOLD_REQUIREMENT {
                                            
                                            //Start the dynamic test
                                            have_started = true;
                                            beam_start_instant = Instant::now();

                                        }

                                    } else {

                                        // We are currently running the test

                                        //Expected value
                                        let expected = match config.data_pattern {
                                            0 => 0, // All zeros
                                            1 => 0xFF, // All ones
                                            2 => crate::rand::rand(addr), // Pseudorandom pattern based on seed
                                            _ => {
                                                0
                                            }
                                        };            


                                        //Write the contents to the chip
                                        _ = crate::chip::write(&config, addr, expected);

                                        //Verify the actual contents against the expected
                                        match crate::chip::read(&config, addr) {
                                            Ok(actual) => {

                                                // Save the XOR'd version to the capture file
                                                _ = cap_buf.write_all(&[actual ^ expected]);

                                                if actual != expected {
                                                    
                                                    let diff_mask        = actual ^ expected;
                                                    let diff_bits: usize = (diff_mask).count_ones() as usize;
                                                    let adj_bits         = diff_mask & (diff_mask >> 1);

                                                    //Something went wrong
                                                    samples_bad += 1;

                                                    //Overall metrics
                                                    r.correct_bits += 8 - diff_bits as u64;
                                                    r.incorrect_bits += diff_bits as u64;

                                                    //Collect statistics
                                                    r.err_bins.as_mut().unwrap()[diff_bits] += 1;
                                                    
                                                    //Detect multi-bit upset
                                                    if diff_bits > 1 {

                                                        //Counter 
                                                        let mut c: usize = 0;

                                                        //We need to count more carefully, this is CPU intensive though :(
                                                        for i in 0..8 {
                                                            
                                                            //Check if bit is set
                                                            if adj_bits & (1 << i) != 0 {
                                                                
                                                                //Bit is set
                                                                c += 1;

                                                            } else {
                                                                
                                                                //Bit is not set

                                                                //Commit result if there is one
                                                                if c > 0 { 
                                                                    r.adj_err_bins.as_mut().unwrap()[c] += 1; 
                                                                    c = 0;
                                                                }
                                                            }
                                                            
                                                        }
                                                        
                                                        //Commit just in case any pending result
                                                        if c > 0 { 
                                                            r.adj_err_bins.as_mut().unwrap()[c] += 1; 
                                                        }

                                                    } else {
                                                        //Optimization trick to prevent extensive check
                                                        r.adj_err_bins.unwrap()[0] += 1;
                                                    }

                                                } else {
                                                    r.correct_bits += 8;
                                                }
                                            },
                                            Err(_e) => {
                                                samples_bad += 1;
                                            }
                                        };

                                        //Increase the total byte count for the sample
                                        samples_total += 1;


                                    }
                                    

                                    //Check if we need to update
                                    if last_update.elapsed() > UPDATE_FREQUENCY {

                                        //Reset watchdog
                                        last_update = Instant::now();


                                        //Only if beam
                                        if have_started {

                                            // Additional processing since we have beam

                                            //Calculate error ratio
                                            let ratio = (samples_bad as f64) / (samples_total as f64);

                                            r.exposure_time = Some(beam_start_instant.elapsed().as_secs_f64());

                                            //Check if ratio exceeds limit
                                            if ratio >= config.sefi_threshold &&
                                               r.time_to_sefi.is_none()
                                            {

                                                //SEFI Occurred
                                                r.time_to_sefi = Some(beam_start_instant.elapsed().as_secs_f64());

                                            }

                                        }                                      

                                        //Record datapoints
                                        r.log_timestamp.push(start.elapsed().as_secs_f64());
                                        r.log_bits_incorrect.push(r.incorrect_bits);
                                        r.log_bits_correct.push(r.correct_bits);
                                        r.log_beam_signal.push(gpio::get_beam_signal());
                                        r.log_current_address.push(blk_ind * config.block_size);
                                        r.log_err_bins.push(r.err_bins.unwrap());
                                        r.log_adj_err_bins.push(r.adj_err_bins.unwrap());
                                        
                                        match r.time_to_sefi {
                                            Some(_) => {r.log_sefi_detected.push(true);},
                                            None =>  {r.log_sefi_detected.push(false);},
                                        }

                                        //Lock the status struct
                                        let mut s = STATUS.write().unwrap();

                                        //Update the status struct
                                        s.active_record = Some(r.clone());
                                        s.task_progress = blk_ind as f64 / config.num_blocks as f64 * 100.0;
                                        s.task_runtime = (get_epoch() - r.time_started) as f64;
                                                                                //Check if we must abort
                                        if abort_check() {

                                            // Must abort right now
                                            r.time_ended = get_epoch();
                                            r.time_duration = r.time_ended - r.time_started;
                                            r.aborted = true;

                                            // Submit whatever we have
                                            archive::submit_record(r.clone());

                                            //Reset the FSM
                                            continue 'main;
                                        }

                                    }

                                    //Check if test should be ended
                                    if !b && b_last_high.elapsed() > TERMINATION_DELAY_FOR_SEFI_TEST && have_started {

                                        //Beam is now gone and termination time has expired
                                        break 'dynamic;

                                    }

                                }
                            }
                        }

                        // Check if we should conclude the test or reboot the system to perform another (special mode)
                        if config.automated_sefi_mode {

                            //Request a reboot
                            PM.write().unwrap().power_request = 2;


                        }

                    }


                }

                //Mark finish times
                r.time_ended = get_epoch();
                r.time_duration = r.time_ended - r.time_started;
                r.completed = true;

                //Submit to archive
                archive::submit_record(r);

                //Close all files
                cap_buf.flush().unwrap();



            },
                        
            None => {},
        }

    }
}