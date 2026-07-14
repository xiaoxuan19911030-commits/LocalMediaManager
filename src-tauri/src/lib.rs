use std::{path::PathBuf, process::{Child, Command}, sync::Mutex};
use tauri::{Manager, RunEvent};

struct BridgeProcess(Mutex<Option<Child>>);

fn bridge_candidates(app: &tauri::App) -> Vec<PathBuf> {
    let mut candidates = Vec::new();
    if let Some(path) = std::env::var_os("LMM_BRIDGE_PATH") {
        candidates.push(PathBuf::from(path));
    }
    if let Ok(resources) = app.path().resource_dir() {
        candidates.push(resources.join("bridge").join("LocalMediaManager.Bridge.exe"));
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

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    let app = tauri::Builder::default()
        .setup(|app| {
            let child = bridge_candidates(app)
                .into_iter()
                .find(|path| path.is_file())
                .and_then(|path| Command::new(path).spawn().ok());
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

