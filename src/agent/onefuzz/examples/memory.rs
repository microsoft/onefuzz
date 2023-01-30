fn main() {
    let bytes = onefuzz::memory::available_bytes().unwrap();
    let gb = (bytes as f64) * 1e-9;
    println!("available bytes: {bytes} ({gb:.1} GB)");
}
