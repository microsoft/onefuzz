#[cfg(not(target_os = "macos"))]
fn main() {
    let bytes = onefuzz::memory::available_bytes().unwrap();
    let gb = (bytes as f64) * 1e-9;
    println!("available bytes: {} ({:.1} GB)", bytes, gb);
}

#[cfg(target_os = "macos")]
fn main() {
    unimplemented!()
}