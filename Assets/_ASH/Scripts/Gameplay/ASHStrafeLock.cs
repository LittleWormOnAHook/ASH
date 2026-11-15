using System.Collections.Generic;
using Invector.vCharacterController;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>Shared aim-strafe owner list for one controller (weapon aim, light aim). The FIRST owner saves the controller's
    /// own locomotion/strafe settings and the LAST owner to let go restores exactly those, so overlapping aims (switching
    /// torch -> rifle -> pistol -> bow while RMB is held) can never leave isStrafing stuck on. A stuck strafe keeps Invector on
    /// its "Strafing" camera state (higher than Default), which showed up as the camera creeping up after switching items.</summary>
    public static class ASHStrafeLock
    {
        sealed class Saved { public vThirdPersonMotor.LocomotionType loco; public bool strafe, rotCam; public float rotSpeed; public readonly HashSet<Object> owners = new HashSet<Object>(); }
        static readonly Dictionary<vThirdPersonController, Saved> _by = new Dictionary<vThirdPersonController, Saved>();

        public static void Acquire(vThirdPersonController cc, Object owner, float rotationSpeed)
        {
            if (!cc || !owner) return;
            if (!_by.TryGetValue(cc, out var s) || s.owners.Count == 0)
            {
                s = new Saved { loco = cc.locomotionType, strafe = cc.isStrafing, rotCam = cc.strafeSpeed.rotateWithCamera, rotSpeed = cc.strafeSpeed.rotationSpeed };
                _by[cc] = s;
            }
            s.owners.Add(owner);
            cc.locomotionType = vThirdPersonMotor.LocomotionType.FreeWithStrafe; cc.isStrafing = true;
            cc.strafeSpeed.rotateWithCamera = true; cc.strafeSpeed.rotationSpeed = rotationSpeed;
        }

        public static void Release(vThirdPersonController cc, Object owner)
        {
            if (!cc || !_by.TryGetValue(cc, out var s) || !s.owners.Remove(owner)) return;
            s.owners.RemoveWhere(o => !o);
            if (s.owners.Count > 0) return;
            cc.locomotionType = s.loco; cc.isStrafing = s.strafe; cc.strafeSpeed.rotateWithCamera = s.rotCam; cc.strafeSpeed.rotationSpeed = s.rotSpeed;
            _by.Remove(cc);
        }

        public static int Owners(vThirdPersonController cc) => cc && _by.TryGetValue(cc, out var s) ? s.owners.Count : 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() => _by.Clear();
    }
}
