// ======================= Performs the commands ========================

use std::fs::File;
use std::io::{self, Write};
use std::net::TcpStream;
use std::path::Path;
use std::time::{Duration, Instant};

use bincode::Options;

use crate::archive::{RECORD_LOG, RUN_LOG};
use crate::{PM, archive, config, fpga, get_beam_request, gpio, task_engine, types::*};

use crate::server::send_response;
use crate::config::*;

pub fn config_command(stream: &mut TcpStream, cmd: ConfigCmd){


    //Update active configuration
    let mut c = CONFIG.write().unwrap();
    *c = cmd;
    drop(c);

    //Save configuration to persistent storage
    config::save();

    //Status response 
    let payload: Vec<u8> = vec![0];

    //Send ACK response
    send_response(stream, CMD_CONFIG, payload).unwrap();
    
}

pub fn genrun_command(stream: &mut TcpStream){


    //Check UUID
    let rsp = genRunRsp {
        id: archive::gen_run_number(),
    };

    //Send ACK response
    send_response(stream, CMD_GENRUN, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
    
}

pub fn setpowerstate_command(stream: &mut TcpStream, cmd: RequestPowerStateCmd){

    let mut rsp = RequestPowerStateRsp {
        success: false
    };

    //Check if possible
    if cmd.requested_power_state >= 0 && cmd.requested_power_state <= 2 { 

        rsp.success = true;

        //Perform power change
        PM.write().unwrap().power_request = cmd.requested_power_state;

    }


    //Send ACK response
    send_response(stream, CMD_REQUESTPOWERSTATE, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
    
}

pub fn genrecord_command(stream: &mut TcpStream){


    //Check UUID
    let rsp = genRecordRsp {
        uuid: archive::gen_record_number(),
    };

    //Send ACK response
    send_response(stream, CMD_GENRECORD, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
    
}


pub fn info_command(stream: &mut TcpStream){

    //Create info response struct
    let payload = crate::server::codec().serialize(&InfoRsp {
        controller_calibrated: gpio::get_calibration_signal(),
        ui_clock: gpio::get_ui_clock_signal(),
        pl_clock: gpio::get_pl_clock_signal(),
        fpga_loaded: gpio::get_fpga_loaded_status(),
        beam_possible: gpio::get_beam_possible(),
        beam_present: gpio::get_beam_present(),
        power_services_provided: {
                //Check if power services are present
                match PM.read().unwrap().last_power_check {
                    Some(last_check) => {
                        if last_check.elapsed() < Duration::from_millis(250) {
                            true
                        } else {
                            false
                        }
                    },
                    None => false,
                }
        },
        clamp_request: gpio::get_clamp_request(),
        sdram_0: gpio::get_sdram_select(0),
        sdram_1: gpio::get_sdram_select(1),
        sdram_2: gpio::get_sdram_select(2),
        sdram_3: gpio::get_sdram_select(3),
    }).unwrap();



    //Send ACK response
    send_response(stream, CMD_INFO, payload).unwrap();
    
}

pub fn getrecord_command(stream: &mut TcpStream, cmd: GetRecordCmd){

    // Lock records archive
    let reclog = archive::RECORD_LOG.read().unwrap();

    // Response struct
    let mut rsp: GetRecordRsp = GetRecordRsp {
        record: None,
    };

    //Iterate over all records
    for r in &(*reclog) {
        if r.rid == cmd.uuid {
            //Found the record
            rsp.record = Some(r.clone());
        }
    }

    //Send response
    send_response(stream, CMD_GETRECORD, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
    
}

pub fn saverun_command(stream: &mut TcpStream, cmd: SaveRunCmd){


    let rsp = SaveRunRsp {
        success: archive::submit_run(cmd.run),
    };    

    //Send response
    send_response(stream, CMD_SAVERUN, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
    
}

pub fn setfpga_command(stream: &mut TcpStream, cmd: SetFPGAImageCmd){


    let rsp = SetFPGAImageRsp {
        status: fpga::flash_fpga(&cmd.image_name)
    };    

    //Send response
    send_response(stream, CMD_SETFPGAIMAGE, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
    
}

pub fn getfpga_command(stream: &mut TcpStream){

    let rsp = GetFPGAImageRsp {
        current_image: fpga::current_image.read().unwrap().clone()
    };    

    //Send response
    send_response(stream, CMD_GETFPGAIMAGE, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
    
}


pub fn getrun_command(stream: &mut TcpStream, cmd: GetRunCmd){

    // Lock records archive
    let runlog = archive::RUN_LOG.read().unwrap();

    // Response struct
    let mut rsp: GetRunRsp = GetRunRsp {
        run: None,
    };

    //Iterate over all records
    for r in &(*runlog) {
        if r.id == cmd.id {
            //Found the run
            rsp.run = Some(r.clone());
        }
    }

    //Send response
    send_response(stream, CMD_GETRUN, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
    
}

pub fn getcapture_command(stream: &mut TcpStream, cmd: GetCaptureCmd) {
    let path = Path::new("/dvr/captures").join(format!("{:04X}.cap", cmd.uuid));

    // Safe mode means /dvr is unmounted — report "no capture" rather than touching it.
    let file = if PM.read().unwrap().safe {
        None
    } else {
        match File::open(&path) {
            Ok(f) => Some(f),
            Err(e) => {
                eprintln!("capture {} unavailable: {e}", path.display());
                None
            }
        }
    };

    // Length up front: 0 means "not found", which replaces the Option tag.
    let (mut file, length) = match file {
        Some(f) => {
            let len = match f.metadata().map(|m| m.len()) {
                Ok(n) => n,
                Err(e) => {
                    eprintln!("capture {} stat failed: {e}", path.display());
                    0
                }
            };
            match u32::try_from(len) {
                Ok(n) => (Some(f), n),
                Err(_) => {
                    eprintln!("capture {} is {len} bytes, too large to frame", path.display());
                    (None, 0)
                }
            }
        }
        None => (None, 0),
    };

    let mut hdr = Vec::with_capacity(9);
    hdr.extend_from_slice(&SYNC_MARKER.to_be_bytes());
    hdr.push(CMD_GETCAPTURE);
    hdr.extend_from_slice(&length.to_be_bytes());

    if let Err(e) = stream.write_all(&hdr) {
        eprintln!("capture header write failed: {e}");
        return;
    }

    if let Some(f) = file.as_mut() {
        // Streams in ~8 KB chunks; peak memory is the chunk, not the file.
        match io::copy(&mut io::BufReader::new(f), stream) {
            Ok(n) if n == length as u64 => {}
            Ok(n) => {
                // File shrank or grew under us — the frame is now wrong and the
                // connection is unrecoverable. Drop it so the client reconnects.
                eprintln!("capture short write: sent {n} of {length}");
                return;
            }
            Err(e) => {
                eprintln!("capture body write failed: {e}");
                return;
            }
        }
    }

    let _ = stream.write_all(&TERM_MARKER.to_be_bytes());
}

pub fn enginestatus_command(stream: &mut TcpStream) {

    //Lock engine status and command structures
    let eng_status = task_engine::STATUS.read().unwrap();
    let eng_cmd = task_engine::CMD.read().unwrap();

    // Response struct
    let rsp = EngineStatusRsp {
        current_request_type: match &eng_status.active_record {
            Some(r) => Some(r.rtype),
            None => None,
        },
        record_progress: eng_status.task_progress,
        is_idle: eng_status.is_idle,
        queue: eng_cmd.task_queue.clone(),
    };

    //Send response
    send_response(stream, CMD_ENGINESTATUS, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
}

pub fn getpowermode_command(stream: &mut TcpStream) {

    //Lock power manager
    let mut pm = PM.write().unwrap();

    //Update last check
    pm.last_power_check = Some(Instant::now());

    let mut rsp = GetPowerModeRsp {
        request_poweroff: false,
        request_reboot: false,
        request_beam: get_beam_request()
    };

    //Check if we are in safe mode
    if pm.safe {

        //Good, we don't have to worry about corruption now
        //Check if a state change is desired

        // Poweroff request
        if pm.power_request == 1 {
            rsp.request_poweroff = true;
        }

        // Reboot request
        if pm.power_request == 2 {
            rsp.request_reboot = true;
        }
        
    }

    //Send response
    send_response(stream, CMD_GETPOWERMODE, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
}

pub fn listrecords_command(stream: &mut TcpStream) {

    //Records for reading
    let list = RECORD_LOG.read().unwrap();

    let mut rsp = ListRecordsRsp {
        rid: vec![],
        time_started: vec![],
        time_duration: vec![],
        completed: vec![],
        aborted: vec![],
        rtype: vec![],
    };

    //Iterate over records
    for r in &(*list) {

        //Append to response structure
        rsp.rid.push(r.rid);
        rsp.time_started.push(r.time_started);
        rsp.completed.push(r.completed);
        rsp.aborted.push(r.aborted);
        rsp.rtype.push(r.rtype);
        rsp.time_duration.push(r.time_duration);

    } 

    //Send response
    send_response(stream, CMD_LISTRECORDS, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
}

pub fn listruns_command(stream: &mut TcpStream) {

    //Runs for reading
    let list = RUN_LOG.read().unwrap();

    let mut rsp = ListRunsRsp {
        id: vec![],
        time_modified:vec![],
        sodimm_id: vec![],
        energy: vec![],
        ion: vec![],
        LET: vec![],
        description: vec![],
        num_records: vec![],
        is_complete: vec![],
    };

    //Iterate over runs
    for r in &(*list) {

        //Append to response structure
        rsp.id.push(r.id);
        rsp.time_modified.push(r.time_modified);
        rsp.sodimm_id.push(r.sodimm_id.clone());
        rsp.energy.push(r.energy);
        rsp.ion.push(r.ion.clone());
        rsp.LET.push(r.LET);
        rsp.description.push(r.description.clone());
        rsp.num_records.push(0);
        rsp.is_complete.push(false);
    } 

    //Send response
    send_response(stream, CMD_LISTRUNS, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
}



pub fn deleterun_command(stream: &mut TcpStream, c: DeleteRunCmd) {

    // Response struct
    let rsp = DeleteRunRsp {
        success: archive::delete_run(c.id),
    };

    //Send response
    send_response(stream, CMD_DELRUN, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
}


pub fn getfpgaimages_command(stream: &mut TcpStream) {

    // Response struct
    let rsp = GetFPGAImagesRsp {
        images: fpga::list_fpga_images()
    };

    //Send response
    send_response(stream, CMD_GETFPGAIMAGES, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
}

pub fn deleterecord_command(stream: &mut TcpStream, c: DeleteRecordCmd) {

    // Response struct
    let rsp = DeleteRecordRsp {
        success: archive::delete_record(c.rid),
    };

    //Send response
    send_response(stream, CMD_DELRECORD, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
}
