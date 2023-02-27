#[derive(Debug, Deserialize, Serialize)]
pub enum IpcMessageKind {
    Shutdown,
}
