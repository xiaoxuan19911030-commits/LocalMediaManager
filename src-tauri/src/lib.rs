use std::{fs::{self, OpenOptions}, net::{SocketAddr, TcpStream}, path::{Path, PathBuf}, process::{Child, Command, Stdio}, sync::Mutex, time::Duration};
use tauri::{AppHandle, Manager, RunEvent};
use uuid::Uuid;

#[cfg(windows)]
use std::os::windows::process::CommandExt;

struct BridgeProcess(Mutex<Option<Child>>);
struct BridgeSessionToken(String);

#[tauri::command]
fn bridge_session_token(token: tauri::State<'_, BridgeSessionToken>) -> String {
    token.0.clone()
}

#[tauri::command]
fn close_local_media_manager(app: AppHandle) {
    app.exit(0);
}

fn bridge_candidates(app: &tauri::App) -> Vec<PathBuf> {
    let mut candidates = Vec::new();
    if let Some(path) = std::env::var_os("LMM_BRIDGE_PATH") {
        candidates.push(PathBuf::from(path));
    }
    if let Ok(resources) = app.path().resource_dir() {
        candidates.push(resources.join("bridge").join("LocalMediaManager.Bridge.exe"));
        candidates.push(resources.join("resources").join("bridge").join("LocalMediaManager.Bridge.exe"));
    }
    candidates.push(
        PathBuf::from(env!("CARGO_MANIFEST_DIR"))
            .join("resources")
            .join("bridge")
            .join("LocalMediaManager.Bridge.exe"),
    );
    candidates.push(
        PathBuf::from(env!("CARGO_MANIFEST_DIR"))
            .join("..")
            .join("backend")
            .join("LocalMediaManager.Bridge")
            .join("bin")
            .join("Debug")
            .join("net8.0")
            .join("LocalMediaManager.Bridge.exe"),
    );
    candidates
}

fn migration_candidates(app: &tauri::App) -> Vec<PathBuf> {
    let mut candidates = Vec::new();
    if let Some(path) = std::env::var_os("LMM_MIGRATION_PATH") { candidates.push(PathBuf::from(path)); }
    if let Ok(resources) = app.path().resource_dir() {
        candidates.push(resources.join("migration").join("LocalMediaManager.Migration.exe"));
        candidates.push(resources.join("resources").join("migration").join("LocalMediaManager.Migration.exe"));
    }
    candidates.push(PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("resources").join("migration").join("LocalMediaManager.Migration.exe"));
    candidates.push(PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("..").join("backend").join("LocalMediaManager.Migration").join("bin").join("Debug").join("net8.0").join("LocalMediaManager.Migration.exe"));
    candidates
}

fn configure_release_process(command: &mut Command, log_path: &Path) -> std::io::Result<()> {
    if cfg!(debug_assertions) { return Ok(()); }
    if let Some(parent) = log_path.parent() { fs::create_dir_all(parent)?; }
    rotate_log(log_path)?;
    let output = OpenOptions::new().create(true).append(true).open(log_path)?;
    command.stdout(Stdio::from(output.try_clone()?)).stderr(Stdio::from(output));
    #[cfg(windows)]
    command.creation_flags(0x0800_0000);
    Ok(())
}

fn rotate_log(path: &Path) -> std::io::Result<()> {
    const MAX_LOG_BYTES: u64 = 5 * 1024 * 1024;
    if path.metadata().map(|metadata| metadata.len()).unwrap_or(0) < MAX_LOG_BYTES { return Ok(()); }
    let rotated = path.with_extension("log.1");
    if rotated.exists() { fs::remove_file(&rotated)?; }
    fs::rename(path, rotated)
}

fn bridge_port_is_in_use() -> bool {
    let address = SocketAddr::from(([127, 0, 0, 1], 47831));
    TcpStream::connect_timeout(&address, Duration::from_millis(250)).is_ok()
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    let app = tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![bridge_session_token, close_local_media_manager])
        .setup(|app| {
            let token = Uuid::new_v4().simple().to_string();
            let bridge_already_running = bridge_port_is_in_use();
            let log_dir = app.path().app_log_dir()?;
            if !bridge_already_running {
                if let Some(path) = migration_candidates(app).into_iter().find(|path| path.is_file()) {
                    let mut command = Command::new(path);
                    command.args(["upgrade", "--confirm"]);
                    configure_release_process(&mut command, &log_dir.join("migration.log"))?;
                    let status = command.status()?;
                    if !status.success() { return Err(format!("数据库升级失败：{status}").into()); }
                }
            }
            let child = if bridge_already_running {
                None
            } else {
                bridge_candidates(app)
                    .into_iter()
                    .find(|path| path.is_file())
                    .and_then(|path| {
                        let mut command = Command::new(path);
                        command.env("LMM_BRIDGE_TOKEN", &token);
                        configure_release_process(&mut command, &log_dir.join("bridge.log")).ok()?;
                        command.spawn().ok()
                    })
            };
            if !bridge_already_running && child.is_none() {
                return Err("Bridge 启动失败，请查看日志目录中的 bridge.log。".into());
            }
            app.manage(BridgeSessionToken(token));
            app.manage(BridgeProcess(Mutex::new(child)));
            Ok(())
        })
        .build(tauri::generate_context!())
        .expect("failed to build Local Media Manager");

    app.run(|handle, event| {
        if matches!(event, RunEvent::Exit | RunEvent::ExitRequested { .. }) {
            if let Some(process) = handle.try_state::<BridgeProcess>() {
                if let Ok(mut child) = process.0.lock() {
                    if let Some(mut running) = child.take() {
                        let _ = running.kill();
                        let _ = running.wait();
                    }
                }
            }
        }
    });
}

#[cfg(test)]
mod tests {
    use super::rotate_log;
    use std::{fs::{self, OpenOptions}, path::PathBuf};
    use uuid::Uuid;

    #[test]
    fn oversized_release_log_is_rotated_without_deleting_evidence() {
        let root: PathBuf = std::env::temp_dir().join(format!("lmm-log-test-{}", Uuid::new_v4()));
        fs::create_dir_all(&root).unwrap();
        let log = root.join("bridge.log");
        let file = OpenOptions::new().create(true).write(true).open(&log).unwrap();
        file.set_len(5 * 1024 * 1024 + 1).unwrap();

        rotate_log(&log).unwrap();

        assert!(!log.exists());
        assert!(root.join("bridge.log.1").exists());
        fs::remove_dir_all(root).unwrap();
    }
}
