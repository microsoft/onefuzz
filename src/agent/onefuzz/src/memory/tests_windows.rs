use super::available_bytes;

#[test]
fn can_read_available_memory() -> anyhow::Result<()> {
    let available_bytes = available_bytes();
    assert!(available_bytes? > 0);
    Ok(())
}
