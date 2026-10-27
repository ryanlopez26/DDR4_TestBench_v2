use std::fs;

use serde::{Deserialize, Serialize};

use crate::{task_engine::{CMD, TaskRequest}};


// Test Hibernator - Resumes a test (or many tests) upon startup
#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct Hibernator {

    // Task requests to be processed (loaded into engine upon startup)
    pub frozen_requests: Vec<TaskRequest>

}


pub fn save(){

    //Get write lock for the engine task queue
    let c = CMD.write().unwrap();

    //Check if there is anything to hibernate
    if c.task_queue.is_empty() { return; }

    //Create a hibernation structure with the pending requests
    let hib = Hibernator {
        frozen_requests: c.task_queue.clone()
    };

    //Write the hibernation structure to a file
    match fs::write("/dvr/hibernate.bin", bincode::serialize::<Hibernator>(&hib).unwrap()) {
        Ok(_) => {
            println!("Hibernated task queue!");
        },
        Err(e) => {
            println!("Failed to hibernate task queue: {:?}", e);
        },
    }

}

pub fn load(){

    //Check if there is a hibernation file

    match fs::read("/dvr/hibernate.bin") {

        Ok(contents) => {

            //Attempt to convert bytes to struct config
            match bincode::deserialize::<Hibernator>(&contents) {
                Ok(data ) => {

                    //Lock the task queue
                    let mut c = CMD.write().unwrap();

                    //Load the queue in from the hibernation file
                    c.task_queue = data.frozen_requests;
                    
                    //Attempt to load configuration
                    println!("Hibernated requests restored!");

                    //Delete hibernation file
                    fs::remove_file("hibernate.bin").unwrap();
                },
                Err(_) => {println!("Hibernation file is invalid. Not loaded.")},
            }


        },
        Err(_) => {
            //Silent fail, no hibernation file present
        },
    }


}