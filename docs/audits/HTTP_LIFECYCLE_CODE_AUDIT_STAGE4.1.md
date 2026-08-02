# Stage 4.1 HTTP Lifecycle Code Audit

## Scope

Static audit only. No Provider, formal database, NAS, MediaStorage, or long-running validation was started.

## Findings

| File | Method | Object / creation | Release | Risk | Level |
| --- | --- | --- | --- | --- | --- |
| `MetadataSyncWorkflow.cs` | `ImageDownloadService.DownloadCoreAsync` | `HttpClient` from factory; per-image `HttpRequestMessage`; `SendAsync(...ResponseHeadersRead)` response; response stream; temp `FileStream` | `using` request/response, `await using` streams, `finally` deletes temp root | Main movie image path is streaming, has no `ReadAsByteArrayAsync`, `MemoryStream`, ArrayPool, or image-level retry. Response remains scoped through file validation/write unless validation-only early-dispose mode is enabled. | P2, measured separately |
| `MovieImageImporter.cs` | `ImportActorImageAsync` | Factory `HttpClient`, `GetAsync(...ResponseHeadersRead)`, response stream, temp file | `using` client/response; `await using` source/target; `catch` deletes temp file | Same sound stream ownership. Response scope includes validation and DB registration after source closes. | P2, audit only |
| `MetadataProviders.cs` | `MdcNgProvider.ReadJsonElementAsync` | Response, response stream, `JsonDocument` | `using` response/document, `await using` stream; root is cloned | No retained response/content. Cloned JSON intentionally outlives stream. | P3 |
| `MetadataProviders.cs` | `MetaTubeProvider.GetJsonAsync` | Response, stream, returned `JsonDocument`; retry loop | `using` response, `await using` stream on every attempt | Safe retry ownership; returned document is caller-owned and must be disposed by caller. | P2, caller audit recommended |
| `MetadataProviders.cs` | `JavBusProvider.GetHtmlAsync` | Response, complete HTML string; retry loop | `using` response per attempt | Response is released before retry. Full HTML buffer is necessary for parser but bounded only by remote response behavior. | P2 buffer-size risk |
| `WebMetadataProviders.cs` | `GetAsync` | Response, complete HTML string; retry loop | `using` response per attempt | Retry does not retain old response. Same unbounded HTML-string risk. | P2 buffer-size risk |
| `ActorProfileProviders.cs` | `GetTextAsync` | Factory client, response, complete text string | `using` response | Response ownership is correct; text is held only by parser caller. | P3 |
| `ProviderNetworkDiagnostics.cs` | `ProbeAsync` / `ReadSampleAsync` | Explicit handler/client/request/response, 16 KiB byte buffer, response stream | `using` handler/client/request/response; `await using` stream | Bounded 16 KiB sample. Handler is created only for an explicit diagnostic probe and disposed. | P3 |
| `SystemFeatureServices.cs` | `UpdateCheckService.CheckAsync` | Factory client, request, response, JSON DTO | `using` request/response | No byte-array or stream retention. | P3 |

## Global Searches

- `ReadAsByteArrayAsync`: no production Bridge call sites.
- `MemoryStream`: only validation-only `ImagePipelineValidationDiagnostics`; disposed with `using`.
- `ArrayPool` / `MemoryPool`: no Bridge call sites.
- `HttpContent`: not returned or cached by audited methods.
- Main image download has no image-level retry; task-level retry is separate and creates a new workflow execution.

## Ownership Notes

`using HttpResponseMessage` covers `EnsureSuccessStatusCode` throws and retry exceptions. `await using Stream` covers cancellation and validation failures. Temporary movie image directories are cleaned in `finally`; actor-image temporary files are cleaned in `catch`.

## Static Assessment

No static Level 1 leak was found for `HttpResponseMessage`, `HttpContent`, response streams, `MemoryStream`, or pooled buffers in the main image-download path. The remaining static risk is **P2**: response objects in the movie and actor image paths live through validation and final file work after their stream closes. Stage 4 early-dispose evidence showed balanced request/response/stream counters and no >=30% memory reduction, so static audit alone does not elevate this to a root cause.

Provider HTML/text reads intentionally materialize complete strings. They are not part of the image streaming chain, but have a separate boundedness risk if a Provider returns unusually large content. This is not evidence of retained HTTP lifetime objects.
