# Page 12 Front-Camera Face Tracking — Crash Investigation & Fix

## Goal

Page 12 scans a marker with the back camera (world tracking, same as every other page), then
switches to the front camera for a live face filter (a crown model tracked to the user's face)
using AR Foundation's `ARFaceManager` / ARCore's Augmented Faces.

## Symptom

The app reliably crashed (hard native crash, app closed entirely) a couple seconds after
switching the camera to front-facing / enabling face tracking. No managed C# exception — Unity's
own exception handling never saw anything; it was a native-level crash.

## Environment

- Device: Google Pixel 7 Pro, Android 16
- Unity 6000.4.5f1, IL2CPP, ARM64, Development Build
- AR Foundation 6.x, Google ARCore XR Plugin, ARCore Services **1.48** on-device

## Investigation timeline

### Attempt 1 — live camera switch in place

Initial implementation: on scanning page12, disable `ARTrackedImageManager`, set
`ARCameraManager.requestedFacingDirection = User`, enable `ARFaceManager` — all within the same
already-running scene/session (the one that had been actively tracking pages 1–11's markers).

**Result:** crashed, 100% reproducible.

### Getting a real crash log

`adb logcat -b crash -d` captured the actual native crash:

```
Fatal signal 11 (SIGSEGV), code 1 (SEGV_MAPERR), fault addr 0x1e0
Cause: null pointer dereference
...
#12 ArSession_resume+144  (libarcore_c.so)
#15 ArPresto_setEnabled+120  (libarpresto_api.so)
#19 UnityARCore_session_update+788  (libUnityARCore.so)
```

Identical stack and fault address (`0x1e0`) across every crash captured — 12 in total by the end
of the investigation. This was a deterministic bug, not flakiness.

### Ruled out: `ARSession.Reset()`, and step ordering/timing

Several hypotheses tested and disproven in turn:

- Suspected `ARSession.Reset()` (called mid-switch) — removed every call to it. Crash persisted,
  identical signature.
- Suspected the *order* of enabling `ARFaceManager` vs. disabling `ARTrackedImageManager` —
  staged every step with explicit delays between them. Crash persisted, identical signature.
- Found and fixed a **real, separate** bug along the way: `ARPlaneManager`/`ARRaycastManager`
  left enabled were silently blocking the config switch from ever being attempted at all (visible
  directly in ARCore's own session log: `Requested features not satisfied: User Facing Camera,
  Face Tracking`). Fixing this didn't fix the crash — it just let the crash actually happen
  instead of being pre-empted by the switch never landing.

### Root cause of the device's config conflict

ARCore prints its session configuration negotiation to logcat at startup. The device exposes
exactly two mutually exclusive configuration families:

| Config | Camera | Features |
|---|---|---|
| 0x1 / 0x2 | World-facing | Image Tracking, Plane Tracking, Raycast, full Position/Rotation |
| 0x3 | User-facing | Face Tracking, **Rotation Only** — no Image/Plane Tracking, no Raycast |

No config supports both. As long as any world-only feature stays requested, ARCore can't even
attempt config 0x3 — it silently stays on the world config instead.

### Attempt 2 — two-scene architecture (cold start)

Reasoning: if the crash is about *reconfiguring* an already-running session, a genuine cold start
(a brand-new scene with its own brand-new `ARSession`, never having been in world-tracking mode)
might avoid it. Built:

- **Ar1** — the existing pages 1–11 scene, unchanged.
- **Ar2** — new, minimal scene: its own XR Origin, `ARSession`, `ARCameraManager` (Facing
  Direction = `User` from the start), `ARFaceManager`.
- Scanning page12 in Ar1 called `SceneManager.LoadScene("Ar2")`.

**Result:** still crashed. Config 0x3 was reached this time (confirmed in the log — the session
genuinely transitioned, unlike Attempt 1), but the app crashed ~0.36s later, identical native
signature.

### The actual breakthrough — process lifetime, not scene lifetime

A Unity scene reload tears down and recreates *managed* C# objects, but the underlying Android OS
process keeps running the whole time — and so does any internal state inside ARCore's own
compiled native plugin (`ArPresto`). Ar1's session, even though cleanly unloaded from Unity's
perspective, leaves the native layer in a state that a scene reload can't reset, and Ar2's session
inherits it.

Tested directly: launched straight into Ar2 as the app's *only* scene (no Ar1 ever running in
that process). **It worked** — front camera + face tracking activated, no crash.

## Root cause (confirmed)

ARCore's native session layer segfaults inside `ArSession_resume` when a **second** `ARSession`
initializes in the same OS process after a first one has already run, specifically when that
second session reaches the front-facing/face-tracking config (0x3). Specific to this
device/ARCore build (1.48) — a native-level bug, not reachable or fixable from Unity script alone.
A plain scene reload cannot clear it, because the OS process — and the native plugin state living
inside it — never actually restarts during a scene reload.

## The fix that worked — force a genuine app process restart

Built a mechanism ensuring Ar1 and Ar2 are *always* the first and only ARCore session in their
process, in both directions:

- **`AppRestarter.cs`** — saves which scene to boot into (`PlayerPrefs`, since it has to survive a
  full process kill) and forces an actual Android process kill-and-relaunch via
  `AndroidJavaObject` calls into `Intent` / `Process.killProcess`.
- **`Bootstrap.cs`** — a new, minimal scene with **no AR components at all**, made the literal
  first scene in Build Settings. Its only job: read the pending boot target and redirect to Ar1
  (normal launch) or Ar2 (post-restart). Has to be a separate AR-free scene — if Ar1 loaded first
  and then redirected, Ar1's own `ARSession` would still initialize as session #1 before the
  redirect, recreating the exact bug being avoided.
- **`PendantManager.cs`** — added `PlayerPrefs`-based persistence for collected spark progress,
  since a full process kill wipes all in-memory state.

Both directions (Ar1→Ar2 and Ar2→Ar1) go through the restart — only the front-facing-config-as-
second-session case was directly confirmed to crash; the reverse direction was never tested
standalone, so the fix stays conservative rather than assuming it's safe.

## Trade-off accepted

The transition is no longer a smooth in-app scene change — it's a full app close-and-reopen
(visible black screen / app icon flash), in both directions. That's the real cost: functionally
correct, but visually indistinguishable from the app crashing and relaunching, because at the OS
level that's literally what's happening.
