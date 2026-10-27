use std::{fs, sync::{LazyLock, RwLock}};

use crate::{rand, types::ConfigCmd};


pub const SIMULATION_MODE: bool = false; // If true, the app will simulate read/write operations instead of performing real hardware access. Useful for testing and development without hardware.

pub const SYNC_MARKER: u32 = 0xDEAD_BEEF;
pub const TERM_MARKER: u32 = 0xCAFE_BABE;

pub const CMD_WRITE: u8 = 0x01;
pub const CMD_VERIFY: u8 = 0x02;
pub const CMD_READ: u8 = 0x03;
pub const CMD_CONFIG: u8 = 0x04;
pub const CMD_DYNAMIC: u8 = 0x05;
pub const CMD_INFO: u8 = 0x06;


pub const CMD_GENRUN: u8 = 0x07;
pub const CMD_GENRECORD: u8 = 0x08;

//Individual Entry Operations
pub const CMD_GETRECORD: u8 = 0x0B;
pub const CMD_GETRUN: u8 = 0x0C;
pub const CMD_GETCAPTURE: u8 = 0x0D;

//Entry Summary Operations
pub const CMD_LISTRECORDS: u8 = 0x10;
pub const CMD_LISTRUNS: u8 = 0x11;

pub const CMD_SAVERUN: u8 = 0x09;

pub const CMD_ABORT: u8 = 0x0A;

pub const CMD_ENGINESTATUS: u8 = 0x0E;
pub const CMD_GETPOWERMODE: u8 = 0x0F;


pub const CMD_GETFPGAIMAGE: u8 = 0x12;
pub const CMD_SETFPGAIMAGE: u8 = 0x13;

pub const CMD_DELRUN: u8 = 0x14;
pub const CMD_DELRECORD: u8 = 0x15;

pub const CMD_GETFPGAIMAGES: u8 = 0x16;
pub const CMD_REQUESTPOWERSTATE: u8 = 0x17;


pub const PAGE_SIZE: usize = 1024; // Size of data pages for dump responses

pub const UPDATE_FREQUENCY_MS: f32 = 100.0; // Frequency of progress updates during long operations

//Global configuration variable, protected by a RwLock for concurrent access. Initialized with default values.
pub static CONFIG: LazyLock<RwLock<ConfigCmd>> = LazyLock::new(|| {
    RwLock::new(ConfigCmd {         // x16 default
        block_size :    0x0010_0000, //  1MB default
        block_factor :  1,
        num_blocks :    0x0000_0100, // Test 256MB
        enable_logging: true,
        is_x16: false,
        enabled_byte_lanes: 0xFF, 
        fpga_auto_config: false,
        pattern_seed: 0,
        data_pattern: 0,
        sefi_threshold: 0.0,
        automated_sefi_mode: false,
    })
});

pub fn save(){

    //Get read lock
    let c = CONFIG.read().unwrap();

    //Write the structure to a file
    match fs::write("config.bin", bincode::serialize::<ConfigCmd>(&c).unwrap()) {
        Ok(_) => {
            println!("Saved configuration!");
        },
        Err(e) => {
            println!("Failed to save configuration: {:?}", e);
        },
    }

    //Reload RNG
    rand::set_seed(c.pattern_seed);
}

pub fn load(){

    //Get read lock
    let mut c = CONFIG.write().unwrap();

    match fs::read("config.bin") {

        Ok(contents) => {

            //Attempt to convert bytes to struct config
            match bincode::deserialize::<ConfigCmd>(&contents) {
                Ok(data ) => {
                    *c = data;
                    
                    //Attempt to load configuration
                    println!("Configuration loaded!");
                },
                Err(_) => {println!("Configuration file is invalid. Not loaded.")},
            }


        },
        Err(e) => {
            println!("Failed to locate configuration file: {:?}", e);
        },
    }

    //Reload RNG
    rand::set_seed(c.pattern_seed);

}