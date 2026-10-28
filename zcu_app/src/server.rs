// Example single-threaded TCP server implementing a framed binary protocol.
//
// Payload structs live in `types.rs` and are deserialized via serde+bincode.
//
// Required Cargo.toml dependencies:
//
//   [dependencies]
//   serde   = { version = "1", features = ["derive"] }
//   bincode = "1"
//
// Packet layout (all framing integers big-endian / network byte order):
//
//   SYNC   : u32   - fixed marker used to (re)synchronize the connection
//   CMD    : u8    - command word; selects how `payload` is interpreted
//   LENGTH : u16   - number of bytes in `payload`
//   PAYLOAD: [u8; LENGTH]
//   TERM   : u32   - fixed marker terminating the packet
//
// Supported commands:
//   0x01 Write  -> WriteCmd  { pattern: u8, seed: u64, delay: u32 }   (13 bytes)
//   0x02 Verify -> VerifyCmd { pattern: u8, seed: u64, delay: u32 }   (13 bytes)
//   0x03 Dump   -> DumpCmd   { offset: u32, size: u32 }               (8 bytes)

use std::io::{self, Read, Write};
use std::net::{TcpListener, TcpStream, ToSocketAddrs};
use std::thread;

use bincode::Options;
use serde::de::DeserializeOwned;

use crate::record::RecordType;
use crate::task_engine::TaskRequest;
use crate::types::{ClampCmd, ConfigCmd, DeleteRecordCmd, DeleteRunCmd, DumpCmd, DynamicCmd, ExposeCmd, GetCaptureCmd, GetFPGAImageRsp, GetRecordCmd, GetRunCmd, RequestPowerStateCmd, SaveRunCmd, SetFPGAImageCmd, VerifyCmd, WriteCmd};
use crate::{THREADS, archive, config::*, gpio, set_beam_request, task_engine};

const MAX_PAYLOAD: u32 = 1 << 20;

// --- framing helpers -------------------------------------------------------

fn read_n(stream: &mut TcpStream, n: usize) -> io::Result<Vec<u8>> {
    let mut buf = vec![0u8; n];
    stream.read_exact(&mut buf)?;
    Ok(buf)
}

fn read_u32_be(stream: &mut TcpStream) -> io::Result<u32> {
    let mut buf = [0u8; 4];
    stream.read_exact(&mut buf)?;
    Ok(u32::from_be_bytes(buf))
}

fn read_u16_be(stream: &mut TcpStream) -> io::Result<u16> {
    let mut buf = [0u8; 2];
    stream.read_exact(&mut buf)?;
    Ok(u16::from_be_bytes(buf))
}

fn read_u8(stream: &mut TcpStream) -> io::Result<u8> {
    let mut buf = [0u8; 1];
    stream.read_exact(&mut buf)?;
    Ok(buf[0])
}

// --- serde / bincode payload parsing --------------------------------------

/// Bincode options matching our wire format: fixed-width integers, big-endian,
/// reject any leftover bytes (so a payload that's too long is treated as an error).
pub(crate) fn codec() -> impl Options {
    bincode::DefaultOptions::new()
        .with_big_endian()
        .with_fixint_encoding()
        .reject_trailing_bytes()
}

/// Deserialize a payload into any `T` that implements `Deserialize`.
fn parse_payload<T: DeserializeOwned>(payload: &[u8]) -> io::Result<T> {
    codec()
        .deserialize(payload)
        .map_err(|e| io::Error::new(io::ErrorKind::InvalidData, e))
}

// --- response framing ------------------------------------------------------

/// Frame and transmit a response packet to the client.
///
/// The wire format mirrors the incoming protocol:
///
///     [SYNC u32 BE][CMD u8][LENGTH u16 BE][payload bytes][TERM u32 BE]
///
/// The whole packet is built into a single buffer and written with one
/// `write_all` call so it goes out as one contiguous chunk rather than
/// several small writes.
///
/// Returns an error if `payload` is larger than `u16::MAX` (65535) bytes,
/// since the length field can't represent it.
pub fn send_response(stream: &mut TcpStream, cmd: u8, payload: Vec<u8>) -> io::Result<()> {
    let length: u32 = payload.len().try_into().map_err(|_| {
        io::Error::new(
            io::ErrorKind::InvalidInput,
            format!(
                "response payload too large: {} bytes (max {})",
                payload.len(),
                u16::MAX
            ),
        )
    })?;

    // 4 (SYNC) + 1 (CMD) + 2 (LENGTH) + payload + 4 (TERM)
    let mut packet = Vec::with_capacity(13 + payload.len());
    packet.extend_from_slice(&SYNC_MARKER.to_be_bytes());
    packet.push(cmd);
    packet.extend_from_slice(&length.to_be_bytes());
    packet.extend_from_slice(&payload);
    packet.extend_from_slice(&TERM_MARKER.to_be_bytes());

    stream.write_all(&packet)
}

// Connection handler
fn handle_client(mut stream: TcpStream) {
    
    let peer = stream.peer_addr().unwrap();


    println!("[+] Client connected: {}", peer);

    loop {
        // --- SYNC ---
        let sync = read_u32_be(&mut stream).unwrap();
        if sync != SYNC_MARKER {
            eprintln!(
                "[!] {}: bad SYNC 0x{:08X}, expected 0x{:08X} — dropping connection",
                peer, sync, SYNC_MARKER
            );
            return;
        }

        // --- CMD ---
        let cmd = read_u8(&mut stream).unwrap();

        // --- LENGTH ---
        let length = read_u32_be(&mut stream).unwrap();

        // Reject an implausible length before allocating anything. No inbound
        // command needs more than a few KB; a huge value means a desynced frame
        // or a bad client, and vec![0u8; length] would either abort the process
        // on allocation failure or park this thread in read_exact forever.
        if length > MAX_PAYLOAD {
            eprintln!(
                "[!] {}: payload length {} exceeds max {} — dropping connection",
                peer, length, MAX_PAYLOAD
            );
            return;
        }

        // --- PAYLOAD ---
        let payload = read_n(&mut stream, length as usize).unwrap();

        // --- TERM ---
        let term = read_u32_be(&mut stream).unwrap();
        if term != TERM_MARKER {
            eprintln!(
                "[!] {}: bad TERM 0x{:08X}, expected 0x{:08X} — dropping connection",
                peer, term, TERM_MARKER
            );
            return;
        }

        // Dispatch on CMD. Each arm hands `payload` to serde via bincode.
        match cmd {
            CMD_WRITE => match parse_payload::<WriteCmd>(&payload) {
                Ok(w) => {

                    // Lock task engine command
                    let mut tcmd = task_engine::CMD.write().unwrap();

                    // Add command to task engine
                    tcmd.task_queue.push(
                        TaskRequest {
                            rtype: RecordType::Write,
                            rid: w.uuid,
                        }
                    );                    

                }
                Err(e) => eprintln!("[!] {}: invalid Write payload: {}", peer, e),
            },

            CMD_VERIFY => match parse_payload::<VerifyCmd>(&payload) {
                Ok(v) => {

                    // Lock task engine command
                    let mut tcmd = task_engine::CMD.write().unwrap();

                    // Add command to task engine
                    tcmd.task_queue.push(
                        TaskRequest {
                            rtype: RecordType::Verify,
                            rid: v.uuid,
                        }
                    );             

                }
                Err(e) => eprintln!("[!] {}: invalid Verify payload: {}", peer, e),
            },

            CMD_EXPOSE => match parse_payload::<ExposeCmd>(&payload) {
                Ok(v) => {

                    // Lock task engine command
                    let mut tcmd = task_engine::CMD.write().unwrap();

                    // Add command to task engine
                    tcmd.task_queue.push(
                        TaskRequest {
                            rtype: RecordType::Expose,
                            rid: v.uuid,
                        }
                    );             

                }
                Err(e) => eprintln!("[!] {}: invalid Expose payload: {}", peer, e),
            },

            CMD_READ => match parse_payload::<DumpCmd>(&payload) {
                Ok(r) => {

                    // Lock task engine command
                    let mut tcmd = task_engine::CMD.write().unwrap();

                    // Add command to task engine
                    tcmd.task_queue.push(
                        TaskRequest {
                            rtype: RecordType::Read,
                            rid: r.uuid,
                        }
                    );             

                }
                Err(e) => eprintln!("[!] {}: invalid Read payload: {}", peer, e),
            },

            CMD_CONFIG => match parse_payload::<ConfigCmd>(&payload) {
                Ok(c) => {
                    println!(
                        "[{}] Config Updated",
                        peer
                    );

                    //Execute command
                    crate::commands::config_command(&mut stream, c);
                }
                Err(e) => eprintln!("[!] {}: invalid Config payload: {}", peer, e),
            },


            CMD_DYNAMIC => match parse_payload::<DynamicCmd>(&payload) {
                Ok(c) => {

                    // Lock task engine command
                    let mut tcmd = task_engine::CMD.write().unwrap();

                    // Add command to task engine
                    tcmd.task_queue.push(
                        TaskRequest {
                            rtype: RecordType::Dynamic,
                            rid: c.uuid,
                        }
                    );             

                }
                Err(e) => eprintln!("[!] {}: invalid Dynamic payload: {}", peer, e),
            },
            CMD_INFO => {
                crate::commands::info_command(&mut stream);
            },
            CMD_GENRUN => {
                crate::commands::genrun_command(&mut stream);
            },
            CMD_GENRECORD => {
                crate::commands::genrecord_command(&mut stream);
            },
            CMD_SAVERUN => match parse_payload::<SaveRunCmd>(&payload) {
                Ok(c) => {
                    // Save run
                    crate::commands::saverun_command(&mut stream, c);   
                }
                Err(e) => eprintln!("[!] {}: invalid save run payload: {}", peer, e),
            },
            CMD_ABORT => {
                // Lock task engine command
                let mut tcmd = task_engine::CMD.write().unwrap();

                // Set abort signal
                tcmd.abort_task = true;
            },
            CMD_GETRECORD => match parse_payload::<GetRecordCmd>(&payload) {
                Ok(c) => {

                    // Send record to client
                    crate::commands::getrecord_command(&mut stream, c);   

                }
                Err(e) => eprintln!("[!] {}: invalid get record payload: {}", peer, e),
            },
            CMD_GETRUN => match parse_payload::<GetRunCmd>(&payload) {
                Ok(c) => {

                    // Send run to client
                    crate::commands::getrun_command(&mut stream, c);   

                }
                Err(e) => eprintln!("[!] {}: invalid get run payload: {}", peer, e),
            },
            CMD_GETCAPTURE => match parse_payload::<GetCaptureCmd>(&payload) {
                Ok(c) => {

                    crate::commands::getcapture_command(&mut stream, c);   

                }
                Err(e) => eprintln!("[!] {}: invalid get capture params: {}", peer, e),
            },
            CMD_SETFPGAIMAGE => match parse_payload::<SetFPGAImageCmd>(&payload) {
                Ok(c) => {

                    crate::commands::setfpga_command(&mut stream, c);   

                }
                Err(e) => eprintln!("[!] {}: invalid Set FPGA params: {}", peer, e),
            },
            CMD_GETFPGAIMAGE => {
                crate::commands::getfpga_command(&mut stream);   
            },
            CMD_GETFPGAIMAGES => {
                crate::commands::getfpgaimages_command(&mut stream); 
            },
            CMD_ENGINESTATUS => {
                crate::commands::enginestatus_command(&mut stream);
            },
            CMD_GETPOWERMODE => {
                crate::commands::getpowermode_command(&mut stream);
            },
            CMD_LISTRECORDS => {
                crate::commands::listrecords_command(&mut stream);
            },
            CMD_LISTRUNS => {
                crate::commands::listruns_command(&mut stream);
            },
            CMD_DELRECORD => match parse_payload::<DeleteRecordCmd>(&payload) {
                Ok(c) => {

                    crate::commands::deleterecord_command(&mut stream, c);

                }
                Err(e) => eprintln!("[!] {}: invalid delete record params: {}", peer, e),
            },
            CMD_DELRUN => match parse_payload::<DeleteRunCmd>(&payload) {
                Ok(c) => {

                    crate::commands::deleterun_command(&mut stream, c);

                }
                Err(e) => eprintln!("[!] {}: invalid delete run params: {}", peer, e),
            },
            CMD_REQUESTPOWERSTATE => match parse_payload::<RequestPowerStateCmd>(&payload) {
                Ok(c) => {

                    crate::commands::setpowerstate_command(&mut stream, c);

                }
                Err(e) => eprintln!("[!] {}: invalid set power state params: {}", peer, e),
            }, 
            CMD_CLAMPCMD  => match parse_payload::<ClampCmd>(&payload) {
                Ok(c) => {

                    set_beam_request(c.state);

                }
                Err(e) => eprintln!("[!] {}: invalid clamp request params: {}", peer, e),
            }, 
            other => eprintln!("[!] {}: unknown CMD 0x{:02X}", peer, other),
        }
    }
}

// --- entry point -----------------------------------------------------------

/// Bind to `addr` and serve clients one at a time, forever.
pub fn run<A: ToSocketAddrs>(addr: A) -> io::Result<()> {
    let listener = TcpListener::bind(addr)?;
    println!("Server listening on {}", listener.local_addr()?);

    for incoming in listener.incoming() {
        match incoming {
            Ok(stream) => {

                // A client connected! 

                //Spawn a new client handler thread
                let mut threads = THREADS.write().unwrap();

                threads.push(
                    thread::spawn(
                        || {
                            handle_client(stream);
                        }
                    )
                );
                
            }
            Err(e) => eprintln!("[!] accept error: {}", e),
        }
    }
    Ok(())
}