use std::{path::PathBuf, process::{Child, Command}, sync::Mutex};
use tauri::{Manager, RunEvent};
use uuid::Uuid;

struct BridgeProcess(Mutex<Option<Child>>);
struct BridgeSessionToken(String);

#[tauri::command]
fn bridge_session_token(token: tauri::State<'_, BridgeSessionToken>) -> String {
    token.0.clone()
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

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    let app = tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![bridge_session_token])
        .setup(|app| {
            let token = Uuid::new_v4().simple().to_string();
            if let Some(path) = migration_candidates(app).into_iter().find(|path| path.is_file()) {
                let status = Command::new(path).args(["upgrade", "--confirm"]).status()?;
                if !status.success() { return Err(format!("database upgrade failed with {status}").into()); }
            }
            let child = bridge_candidates(app)
                .into_iter()
                .find(|path| path.is_file())
                .and_then(|path| Command::new(path).env("LMM_BRIDGE_TOKEN", &token).spawn().ok());
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
                    }
                }
            }
        }
    });
}
