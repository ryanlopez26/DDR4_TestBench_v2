use serde::{Deserialize, Serialize};

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Copy, Clone)]
pub enum RecordType {
    Read,
    Verify,
    Write,
    Dynamic
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct Record {

    // Time created
    pub time_started: u64,

    // Time finished
    pub time_ended: u64,

    // Total time [sec]
    pub time_duration: u64,

    // Record ID
    pub rid: u16,

    // Record Type
    pub rtype: RecordType,

    // Status of the record
    pub completed: bool,
    pub aborted: bool,

    //Bit counts
    pub correct_bits: u64,
    pub incorrect_bits: u64,
    pub processed_bits: u64,


    // ================ Specifics (Not all apply to every record) =============

    // Distribution of # bit errors 
    pub adj_err_bins: Option<[u64; 8]>,
    pub err_bins: Option<[u64; 9]>,

    // For SEFI test
    pub time_to_sefi: Option<f64>,
    pub exposure_time: Option<f64>,


    // ============================= Data Logs ================================

    pub log_timestamp: Vec<f64>,
    pub log_bits_processed: Vec<u64>,
    pub log_bits_incorrect: Vec<u64>,
    pub log_bits_correct: Vec<u64>,
    pub log_error_rate: Vec<f64>,
    pub log_current_address: Vec<u32>,
    pub log_beam_signal: Vec<bool>,
    pub log_sefi_detected: Vec<bool>,
    pub log_adj_err_bins: Vec<[u64; 8]>,
    pub log_err_bins: Vec<[u64; 9]>,

}

       