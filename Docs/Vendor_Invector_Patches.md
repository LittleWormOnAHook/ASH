# Invector local patches (Assets/Invector-3rdPersonController is gitignored)

The Invector folder is not in git, so its local edits are recorded here. Re-apply them after reimporting or updating Invector.
The earlier DM (Genesis) patches - `vThirdPersonCamera.DMFollowInLateUpdate` / `DMLateTick` / `DMCamDt` and
`vThirdPersonMotor.DMRotateViaRigidbody` - came with the copied Invector folder. ASH code depends on them.

## 2026-11-15 - camera pose written directly in LateUpdate (walk/run character jitter)

`Basic Locomotion/Scripts/Camera/vThirdPersonCamera.cs`

**Cause:** the camera moves its own kinematic, non-interpolated Rigidbody with `MovePosition`/`MoveRotation`. Those only take
effect at the next physics step. With the camera driven in LateUpdate (ASHCameraLateFollow), it moved in physics-rate steps one
frame late (0, 1 or 2 steps per rendered frame), while the interpolated player moved every frame. On screen the character
jittered back and forth against the camera, and it was worse when running.

**Patch:** add these lines above `DMLateTick()`:

```csharp
public static bool DMCameraDirect = true;
protected void DMSetPosition(Vector3 p) { if (DMFollowInLateUpdate && DMCameraDirect) { selfRigidbody.position = p; transform.position = p; } else selfRigidbody.MovePosition(p); }
protected void DMSetRotation(Quaternion r) { if (DMFollowInLateUpdate && DMCameraDirect) { selfRigidbody.rotation = r; transform.rotation = r; } else selfRigidbody.MoveRotation(r); }
```

Then, in `CameraMovement()`, replace `selfRigidbody.MovePosition(...)` with `DMSetPosition(...)` and
`selfRigidbody.MoveRotation(...)` with `DMSetRotation(...)`.

**Measured** with `ASH.DebugTools.ASHWalkJitterProbe`: the player's position as the camera sees it, measured as the
frame-to-frame second difference. Lower is smoother.

| | median before | median after |
|---|---|---|
| walk | 9.0 mm | 1.0 mm |
| run | 42.7 mm | 1.6 mm |

## Related ASH-side setting (versioned)

`ASHCameraLateFollow.OnEnable` now sets `vThirdPersonMotor.DMRotateViaRigidbody = true`, which Genesis enabled and ASH never did.
With it on, the free-locomotion turn uses `MoveRotation` instead of writing `transform.rotation` on the interpolated player body
every physics step.
