use serde::{Deserialize, Serialize};

use crate::{record::{Record, RecordType}, run::Run, task_engine::TaskRequest};

// NOTE: `#[repr(C)]` controls the *in-memory* layout (for FFI / unsafe casts).
// It does NOT control how serde + bincode lays bytes on the wire — that is
// determined by the bincode configuration used to deserialize (see server.rs).
// The two are independent; keeping `#[repr(C)]` is fine if you also use these
// structs across an FFI boundary, but only the field *order and types* affect
// the bincode wire format.

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct WriteCmd {
    pub uuid: u16
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct VerifyCmd {
    pub uuid: u16
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct SaveRunCmd {
    pub run: Run
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct ReadCmd {
    pub uuid: u16
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct DumpCmd {
    pub uuid: u16,
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct ClampCmd {
    pub state: bool
}




#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct DynamicCmd {
    //UUID
    pub uuid: u16,
    pub auto_reboot_on_sefi: bool,
    pub number_of_trials: u16,
    pub parent_run: u16
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct GetRecordCmd {
    pub uuid: u16,
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct GetCaptureCmd {
    pub uuid: u16,
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct GetRunCmd {
    pub id: u16,
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct SetFPGAImageCmd {
    pub image_name: String,
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct RequestPowerStateCmd {
    pub requested_power_state: u8
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct RequestPowerStateRsp {
    pub success: bool
}


// Configuration Structure

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Copy, Clone)]
pub struct ConfigCmd {

    //Log management
    pub enable_logging: bool,

    // Selection settings
    pub num_blocks: u32,
    pub block_size: u32,
    pub block_factor: u32,

    // SEFI Threshold
    pub sefi_threshold: f64,

    // Data Mode
    pub data_pattern: u8,
    pub pattern_seed: u64,

    // FPGA image settings
    pub is_x16: bool,
    pub enabled_byte_lanes: u8,
    pub fpga_auto_config: bool,

    // Automated Dynamic Mode
    pub automated_sefi_mode: bool,

    //Exposure
    pub exposure_time_limit_enabled: bool,
    pub exposure_time_limit_ms: f64
}


// --- Response structures (unchanged; not deserialized from the wire) -------

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct InfoRsp {
    pub beam_possible: bool,
    pub beam_active: bool,
    pub power_services_provided: bool,
    pub controller_calibrated: bool,
    pub ui_clock: bool,
    pub pl_clock: bool,
    pub fpga_loaded: bool,
    pub clamp_request: bool,
    pub sdram_0: bool,
    pub sdram_1: bool,
    pub sdram_2: bool,
    pub sdram_3: bool,
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct genRecordRsp {
    pub uuid: u16
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct genRunRsp {
    pub id: u16
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct GetRecordRsp {
    pub record: Option<Record>
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct SaveRunRsp {
    pub success: bool,
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct GetRunRsp {
    pub run: Option<Run>
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct GetCaptureRsp {
    pub capture: Option<Vec<u8>>
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct GetPowerModeRsp {
    pub request_poweroff: bool,
    pub request_reboot: bool
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct EngineStatusRsp {
    pub current_request_type: Option<RecordType>,
    pub record_progress: f64,
    pub is_idle: bool,
    pub queue: Vec<TaskRequest>
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct ListRunsRsp {

    // Summary of all runs
    pub id: Vec<u16>,
    pub time_modified: Vec<u64>,
    pub sodimm_id: Vec<String>,
    pub energy: Vec<f64>,
    pub ion: Vec<String>,
    pub LET: Vec<f64>,
    pub description: Vec<String>,
    pub num_records: Vec<u16>,
    pub is_complete: Vec<bool>
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct ListRecordsRsp {

    // Summary of all records
    pub rid: Vec<u16>,
    pub time_started: Vec<u64>,
    pub time_duration: Vec<u64>,
    pub completed: Vec<bool>,
    pub aborted: Vec<bool>,
    pub rtype: Vec<RecordType>

}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct GetFPGAImageRsp {

    // Provide back the loaded FPGA image name
    pub current_image: Option<String>

}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct SetFPGAImageRsp {
    pub status: bool
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct DeleteRecordCmd {
    pub rid: u16
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct DeleteRecordRsp {
    pub success: bool
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct DeleteRunCmd {
    pub id: u16
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct DeleteRunRsp {
    pub success: bool
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct GetFPGAImagesRsp {
    pub images: Vec<String>
}