# Monitor Mode (Desktop Observer)

Lets an experimenter **without a headset** join the same Normcore room as the VR
firefighters and watch / listen to them from a bird's-eye view. The monitor has no
avatar, is invisible to participants, and never takes ownership of any fire, so the
fire simulation always stays on a VR client.

## Choosing VR or Monitor

Each person chooses on their own machine. The choice is not stored in any scene or
committed file, so it never affects anyone else.

| Want to join as | How |
|---|---|
| VR participant (default) | Leave **Tools → Monitor Mode (Desktop Observer)** unchecked and press Play |
| Desktop monitor | Check **Tools → Monitor Mode (Desktop Observer)** and press Play |
| Monitor from a build | Launch the executable with `-monitor` |

**Open the same scenario scene the VR users are in.** Every scenario joins the same
room ("Test Room") and the scenarios share most of their networked object IDs, so a
different scene would silently bind to the wrong fires instead of failing. The
overlay shows the current scene name. In particular, the improved smoke (room smoke,
window plume, heat vignette) only exists in
`SCENARIOS/Smoke-VFX-Test/Scenario1-ImprovedSmoke_Test0`, which shares every fire ID
with Scenario 1 — pick the one the VR users actually loaded.

## What the monitor does

- Joins the room without spawning an avatar; the XR rig and XR are switched off.
- Never claims fire authority (`NetworkedFireState.TryClaimAuthority`), even if it
  joins before the VR users.
- Bird's-eye camera that follows the VR users and clips away ceilings above them.
- Plays every VR user's voice as 2D audio, so everyone is audible wherever the camera
  is. Name labels turn green and the panel shows "speaking" while someone talks.
- The monitor sends no audio.

## Controls

| Key | Action |
|---|---|
| Tab / 0-9 | Follow next user / everyone (0) / user N |
| WASD or arrows (Shift = faster) | Pan |
| Q / E | Rotate |
| Mouse wheel | Zoom |
| `[` / `]` | Lower / raise the ceiling cut (e.g. to see the 2nd floor in Scenario 2) |
| R | Reset view |
| F1 | Hide the overlay |

## Notes

- **Stop the monitor between trials.** Scene state is reset when the last client
  leaves the room; a monitor that stays connected keeps burnt / extinguished fires
  into the next trial.
- The monitor uses one of the room's 4 connection slots.
- Connecting can take ~20 s because each scenario has ~360 networked fires.
- The console warning "local avatar prefab is null" on connect is expected.
- **Don't press Space** on the monitor: `UniversalHazardController` uses it for a
  local reset of hazard materials and temperatures.

## What the monitor sees

- Fire, room smoke and window plumes follow the same synced fire lifecycle as the VR
  clients. Window plumes are given a longer fade distance on the monitor (60 / 120 m
  instead of 12 / 25 m) so they stay visible from above.
- Dense room smoke is drawn on top of the fire and can hide users from a top-down
  view; lower the ceiling cut with `[` if smoke near the ceiling gets in the way.
- Not visible: each VR user's heat vignette and thermal camera (TIC) image, which
  are local to their headset.
