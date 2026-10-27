use serde::{Deserialize, Serialize};

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Copy, Clone)]
pub enum RunType {
    Static,
    Dynamic
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct Run{

    // Time created
    pub time_started: u64,

    // Time last modified
    pub time_modified: u64,

    // ======================== Run Parameters =================

    // ID of the run
    pub id: u16,

    // Fluence of the run
    pub fluence: f64,

    // Temperature of the run
    pub temperature: f64,

    // SODIMM ID
    pub sodimm_id: String,

    // Energy of the Cocktail 
    pub energy: f64,

    // Ion used
    pub ion: String,

    // LET 
    pub LET: f64,

    // Angle
    pub angle: f64,

    // Flux
    pub flux: f64,

    // Air Gap
    pub air_gap: f64,

    // Description
    pub description: String,

    // ===================== Test Results ========================

    // Preliminary operations (before beam)
    pub preliminary_records: Vec<u16>,

    // Primary operations (the main operation of study)
    pub primary_records: Vec<u16>,

    // Post operation (after main study, for comparison of preliminary)
    pub post_records: Vec<u16>

}

