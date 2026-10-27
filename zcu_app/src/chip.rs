// Abstracts the PL DDR4 memory as a "chip" with a simple read/write interface.
// Allows individual chips to be addressed and focused on apart from the overall
// memory map, which is useful for testing and debugging.

use crate::types::ConfigCmd;
use std::fmt;


pub enum ChipError {
    /// A field in `ConfigCmd` is structurally invalid.
    InvalidConfig(&'static str),
    /// The requested virtual offset is past the end of the chip.
    OffsetOutOfBounds { offset: u32, chip_size: u32 },
    /// The computed physical offset would not fit in a u32.
    AddressOverflow,
}

impl fmt::Display for ChipError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            ChipError::InvalidConfig(why) => write!(f, "invalid config: {why}"),
            ChipError::OffsetOutOfBounds { offset, chip_size } =>
                write!(f, "offset {offset:#x} is past chip end ({chip_size:#x})"),
            ChipError::AddressOverflow =>
                write!(f, "computed physical offset overflows u32"),
        }
    }
}



// /// Translate a virtual per-chip offset into the physical PL-DDR4 byte offset.
// /// All validation happens here so `read` and `write` can't diverge.
// fn map_offset(config: &ConfigCmd, offset: u32) -> Result<u32, ChipError> {
//     let bbpc = config.bus_bytes_per_chip as u32;
//     let bus  = config.bus_size_in_bytes;
//     let chip = config.chip_index as u32;

//     // --- Structural validation of the config ----------------------------
//     if bbpc == 0 {
//         return Err(ChipError::InvalidConfig("bus_bytes_per_chip must be > 0"));
//     }
//     if bus == 0 {
//         return Err(ChipError::InvalidConfig("bus_size_in_bytes must be > 0"));
//     }
//     if !bus.is_multiple_of(bbpc) {
//         return Err(ChipError::InvalidConfig(
//             "bus_size_in_bytes must be a multiple of bus_bytes_per_chip",
//         ));
//     }
//     let num_chips = bus / bbpc;
//     if chip >= num_chips {
//         return Err(ChipError::InvalidConfig(
//             "chip_index out of range for current bus geometry",
//         ));
//     }
//     if config.chip_size_bytes == 0 {
//         return Err(ChipError::InvalidConfig("chip_size_bytes must be > 0"));
//     }

//     // --- Validate the offset against the chip size ---------------------
//     if offset >= config.chip_size_bytes {
//         return Err(ChipError::OffsetOutOfBounds {
//             offset,
//             chip_size: config.chip_size_bytes,
//         });
//     }

//     // --- Compute with overflow checks ----------------------------------
//     let group_index  = offset / bbpc;
//     let group_offset = (offset % bbpc) + (bbpc * chip); // always < bus, no overflow

//     let group_byte_offset = group_index
//         .checked_mul(bus)
//         .ok_or(ChipError::AddressOverflow)?;
//     let true_offset = group_byte_offset
//         .checked_add(group_offset)
//         .ok_or(ChipError::AddressOverflow)?;

//     Ok(true_offset)
// }

/// Write a byte to the virtual address space of the configured chip.
pub fn write(_config: &ConfigCmd, offset: u32, value: u8) -> Result<(), ChipError> {

    let true_offset = offset;

    // //Check if chip select is enabled
    // if config.enable_chip_select {true_offset = map_offset(config, offset)?;}
    
    //Check if simulated mode is enabled
    if crate::config::SIMULATION_MODE {
        crate::vram::write(true_offset, value);
    } else {
        crate::ram::write(true_offset, value);
    }

    Ok(())
}

/// Read a byte from the virtual address space of the configured chip.
pub fn read(_config: &ConfigCmd, offset: u32) -> Result<u8, ChipError> {


    let true_offset = offset;

    // //Check if chip select is enabled
    // if config.enable_chip_select {true_offset = map_offset(config, offset)?;}
    
    Ok(    
        //Check if simulated mode is enabled
        if crate::config::SIMULATION_MODE {
            crate::vram::read(true_offset)
        } else {
            crate::ram::read(true_offset)
        }
    )
}