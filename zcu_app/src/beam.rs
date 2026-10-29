use std::time::Duration;
use std::time::Instant;
use std::sync::RwLock;


// Beam services
pub static REQUEST_BEAM: RwLock<bool> = RwLock::new(false);
pub static BEAM_ON: RwLock<bool> = RwLock::new(false);
pub static BEAM_SERVICES: RwLock<Option<Instant>> = RwLock::new(None);

// Beam modifier 
pub fn set_beam_request(req: bool){
    *REQUEST_BEAM.write().unwrap() = req;
}

pub fn get_beam_request() -> bool {
    *REQUEST_BEAM.read().unwrap()
}

pub fn is_beam_active() -> bool {
    *BEAM_ON.read().unwrap()
}

pub fn beam_services_active() -> bool {

    match *BEAM_SERVICES.read().unwrap() {
        Some(i) => {
            if i.elapsed() > Duration::from_millis(200) {
                false
            } else {
                true
            }
        },
        None => false,
    }


}