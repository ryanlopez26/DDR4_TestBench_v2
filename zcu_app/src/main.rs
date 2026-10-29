use std::{sync::RwLock, thread::{self, JoinHandle}, time::Instant};

mod server;
mod types;
mod rand;
mod ram;
mod commands;
mod mount;
mod config;
mod fpga;
mod chip;
mod vram;
mod gpio;
mod record;
mod run;
mod task_engine;
mod hibernate;
mod archive;
mod beam;
mod tools;
mod power;

static THREADS: RwLock<Vec<JoinHandle<()>>> = RwLock::new(Vec::new()); 
static PM: RwLock<PowerManager> = RwLock::new(PowerManager { power_request: 0, safe: false, last_power_check: None});


// External power control
pub struct PowerManager {

    // Run request (0 = active, 1 = shutdown, 2 = reboot)
    pub power_request: u8,

    // Safeguard status - Whether or not it is safe to request power change
    pub safe: bool,

    // Last power check
    pub last_power_check: Option<Instant>
}


fn main() {

    println!("ZCU Server V3 - NSRL Edition");

    //Attempt to mount the dvr
    mount::mount_dvr();

    //Attempt to load configuration file
    config::load();

    //Attempt to load any hibernated requests
    hibernate::load();
    
    //Load the memory provider
    if crate::config::SIMULATION_MODE {
        //Initalize simulated ram
        vram::init().unwrap();

        println!("WARNING: SIMULATION MODE IS ACTIVE");

        
    } else {
        //Initialize SODIMM card on the PL 
        ram::init().unwrap();

        //Initalize GPIO 
        gpio::init().unwrap();
    }

    //Initialize data recorder
    archive::init();

    //Task Engine
    {
        //Spawn a new thread for the task engine
        let mut threads = THREADS.write().unwrap();

        threads.push(
            thread::spawn(
                || {
                    task_engine::engine_main();
                }
            )
        );
    }

    //Start ZCU server
    server::run("0.0.0.0:8080").unwrap();    

    
}
