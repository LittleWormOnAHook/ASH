using System;
using System.Collections;
using System.Collections.Generic;
using ASH.Companion;
using ASH.Core;
using ASH.Gameplay;
using ASH.Inventory;
using ASH.Opening;
using ASH.Survival;
using ASH.UI;
using ASH.Vehicles;
using Invector.vCharacterController;
using UnityEngine;
using UnityEngine.UI;

namespace ASH.Prologue
{
    /// <summary>
    /// M1 prologue (P00-P03) on top of the existing opening (ASHEruptionSequence), objective tracker and save system:
    /// P00 arrive in the car with the dog and park at the cabin; P01 find Grandpa's journal (+ supplies); first cabin exit after
    /// that writes the prologue checkpoint and starts the warning (P02, SEEK SHELTER NOW, saves locked until the quake ends);
    /// anyone outside a structural shelter at the quake peak dies (Retry = checkpoint); the tree falls on the car; P03 inspect the
    /// car, find Grandpa's truck, repair it (parts count whenever installed). Settings: Resources/ASH_PrologueSettings.
    /// Scene pieces are created by ASH/Systems/Prologue/Set Up M1 Opening. Saved ("prologue").
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHPrologueDirector : MonoBehaviour, IASHSaveable
    {
        public enum Phase { NotStarted, Arrival, HomeAgain, AwaitExit, Warning, Quake, CheckCar, FindWayOut, RepairTruck, TruckReady }

        [Header("Scene pieces (made by the setup menu; move them freely)")]
        public Transform arrivalSpawn;
        public Transform parkingZone;
        public ASHPrologueFallingTree fallingTree;
        public Transform cinematicCamera;
        public ASHGrandpaSupplies supplies;
        public ASHShelterZone cabinShelter;
        [Header("Debug")]
        public bool logPhases = true;

        public static ASHPrologueDirector Instance { get; private set; }
        public Phase Current { get; private set; } = Phase.NotStarted;
        public bool TreeFelled { get; private set; }
        public bool TruckFound { get; private set; }

        public const string IdHome = "p00_home", IdHomeAgain = "p01_home_again", IdShelter = "p02_shelter", IdCar = "p03_car", IdWayOut = "p03_wayout", IdTruck = "p03_truck";

        static ASHPrologueSettings S => ASHPrologueSettings.I;
        /// <summary>The prologue owns new-game starts (car start). Off = the old cabin opening runs (dev/debug).</summary>
        public static bool TakesOverOpening { get { var d = Instance; var s = S; return d && d.isActiveAndEnabled && s && s.startInCar; } }
        /// <summary>The legacy slice chain is debug-only while the prologue runs.</summary>
        public static bool BlocksLegacySlice { get { var s = S; return TakesOverOpening && !s.useLegacySliceChain; } }
        public bool SavesLocked => Current == Phase.Warning || Current == Phase.Quake;

        ASHObjectiveTracker _tracker; vThirdPersonController _cc; ASHVehicle _car, _truck; ASHEruptionSequence _seq; ASHSurvivalStats _stats;
        float _outsideT, _slowT, _warnStart = -1f; bool _killChecked, _treeStarted, _carInspected, _arrivalDone, _homeAgainDone;
        int _tiresNeeded = 2; bool _refirePending; Quaternion _treePose = Quaternion.identity; bool _deadThisRun;
        Func<bool> _autosaveBlocker; Canvas _bannerCanvas; Text _banner, _bannerSub;

        // ------------------------------------------------------------------ lifecycle
        void Awake() { Instance = this; if (!GetComponent<ASHObjectiveChainRunner>()) gameObject.AddComponent<ASHObjectiveChainRunner>(); } // t440 chain (runtime only)
        void OnEnable()
        {
            ASHSaveSystem.Register(this);
            ASHSaveSystem.SaveGuard = Guard;
            _autosaveBlocker = () => SavesLocked; ASHSaveSystem.AutosaveBlockers.Add(_autosaveBlocker);
            ASHSaveSystem.Restored += OnRestored;
            ASHVehicle.DrivingChanged += OnDrivingChanged;
            ASHVehicle.Inspected += OnInspected;
            ASHGrandpaJournal.PageFound += OnPageFound;
            ASHDeathScreen.RetryAvailable = () => SavesLocked && ASHSaveSystem.Read(S ? S.checkpointSlot : "prologue_checkpoint") != null;
            ASHDeathScreen.Retry = Retry;
            ASHInventory.SuppressStartingItems = () => TakesOverOpening && S.prologueKitOnly && ASHSaveSystem.PendingLoad == null && !ASHSceneFlow.ContinuingRun;
        }
        void OnDisable()
        {
            ASHSaveSystem.Unregister(this);
            if (ASHSaveSystem.SaveGuard == (Func<string, string>)Guard) ASHSaveSystem.SaveGuard = null;
            ASHSaveSystem.AutosaveBlockers.Remove(_autosaveBlocker);
            ASHSaveSystem.Restored -= OnRestored;
            ASHVehicle.DrivingChanged -= OnDrivingChanged;
            ASHVehicle.Inspected -= OnInspected;
            ASHGrandpaJournal.PageFound -= OnPageFound;
            ASHDeathScreen.RetryAvailable = null; ASHDeathScreen.Retry = null; ASHInventory.SuppressStartingItems = null;
            if (_stats) _stats.Died -= OnDied;
        }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            Cache();
            if (_stats) _stats.Died += OnDied;
            if (!S || !S.startInCar) { if (logPhases) Debug.Log("[ASH] Prologue: off (Start In Car = false, old cabin opening)"); return; }
            if (ASHSaveSystem.PendingLoad != null) return; // a load restores the phase (RestoreJson)
            StartCoroutine(NewGameRoutine());
        }

        void Cache()
        {
            if (!_tracker) _tracker = FindAnyObjectByType<ASHObjectiveTracker>();
            if (!_cc) _cc = FindAnyObjectByType<vThirdPersonController>();
            if (!_seq) _seq = ASHEruptionSequence.Instance ? ASHEruptionSequence.Instance : FindAnyObjectByType<ASHEruptionSequence>();
            if (!_stats) _stats = FindAnyObjectByType<ASHSurvivalStats>();
            if (!_car || !_truck) foreach (var v in ASHVehicle.All) { if (!v) continue; if (S && v.vehicleId == S.arrivalVehicleId) _car = v; if (S && v.vehicleId == S.truckVehicleId) _truck = v; }
            if (!_car || !_truck) foreach (var v in FindObjectsByType<ASHVehicle>()) { if (S && v.vehicleId == S.arrivalVehicleId) _car = v; if (S && v.vehicleId == S.truckVehicleId) _truck = v; }
            if (_truck && _truck.Condition) _tiresNeeded = Mathf.Max(_truck.Condition.MissingTireCount, _truck.Condition.startMissingTires != null ? _truck.Condition.startMissingTires.Count : 0, 1);
        }

        // ------------------------------------------------------------------ P00 arrival (new game)
        float _perfStart = -1f; int _perfFrames; float _perfMax, _perfSum; string _perfLabel;
        void PerfBegin(string label) { _perfLabel = label; _perfStart = Time.unscaledTime; _perfFrames = 0; _perfMax = 0f; _perfSum = 0f; }
        void PerfTick(float window)
        {
            if (_perfStart < 0f) return;
            float ms = Time.unscaledDeltaTime * 1000f; _perfFrames++; _perfSum += ms; if (ms > _perfMax) _perfMax = ms;
            if (Time.unscaledTime - _perfStart >= window)
            {
                Debug.Log($"[ASH] Perf {_perfLabel}: {_perfFrames} frames, avg {_perfSum / Mathf.Max(1, _perfFrames):F1} ms, worst {_perfMax:F1} ms");
                _perfStart = -1f;
            }
        }

        IEnumerator NewGameRoutine()
        {
            var cp = S.checkpointSlot; try { var p = ASHSaveSystem.SlotPath(cp); if (System.IO.File.Exists(p)) System.IO.File.Delete(p); } catch { }
            yield return null; yield return null; // every Start ran (vehicles made their seats, the dog is placed)
            Cache();
            SetPhase(Phase.Arrival);
            if (_car && arrivalSpawn)
            {
                var rb = _car.GetComponent<Rigidbody>();
                var pos = arrivalSpawn.position; var rot = arrivalSpawn.rotation;
                if (rb) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.position = pos; rb.rotation = rot; }
                _car.transform.SetPositionAndRotation(pos, rot);
                Physics.SyncTransforms();
                var door = _car.exitPoint ? _car.exitPoint.position : pos;
                if (_cc) { var prb = _cc.GetComponent<Rigidbody>(); if (prb) { prb.linearVelocity = Vector3.zero; prb.position = door; } _cc.transform.position = door; }
                var dog = ASHDogCompanion.Active;
                if (dog) { dog.TeleportNearPlayer(); dog.AllowInstantRide(); ASHDogCompanion.ForcePassenger = true; }
                yield return null;
                _car.holdBrakeUntilInput = true;
                _car.Enter(true);
                float wait = 0f; while (ASHVehicle.Driving != _car && wait < 4f) { wait += Time.deltaTime; yield return null; }
                if (dog && !dog.InVehicle) SeatDogInCar(dog);
                PerfBegin("opening (first 20 s)");
                if (logPhases) Debug.Log($"[ASH] Prologue: seated in '{_car.displayName}' at {pos:F1}, dog {(dog ? (dog.InPassengerSeat ? "in the passenger seat" : "NOT seated") : "missing")}");
            }
            else Debug.LogWarning("[ASH] Prologue: no arrival car or spawn; starting on foot.");
            StartCoroutine(ArrivalRadio());
        }

        /// <summary>The car has no bed/dogSeat, so its own Enter does not seat the dog: put the dog on the passenger seat (mirror of the driver seat).</summary>
        void SeatDogInCar(ASHDogCompanion dog)
        {
            var ps = _car.passengerSeat; var pd = _car.passengerDoor;
            if (!ps && _car.seat) { var lp = _car.transform.InverseTransformPoint(_car.seat.position); lp.x = -lp.x; ps = new GameObject("ASH_PassengerSeat").transform; ps.SetParent(_car.transform, false); ps.localPosition = lp; ps.localRotation = Quaternion.identity; _car.passengerSeat = ps; }
            if (!pd && _car.exitPoint) { var lp = _car.transform.InverseTransformPoint(_car.exitPoint.position); lp.x = -lp.x; pd = new GameObject("ASH_PassengerDoor").transform; pd.SetParent(_car.transform, false); pd.localPosition = lp; _car.passengerDoor = pd; }
            if (!ps) { Debug.LogWarning("[ASH] Prologue: car has no seat point; dog stays outside."); return; }
            if (!pd) pd = ps;
            dog.AllowInstantRide(); ASHDogCompanion.ForcePassenger = true;
            dog.BeginRide(ps, pd, ps, pd, 999f, true);
        }

        IEnumerator ArrivalRadio()
        {
            if (!S || S.arrivalRadio == null) yield break;
            yield return new WaitForSeconds(S.arrivalRadioDelay);
            var hud = FindAnyObjectByType<ASHHud>();
            foreach (var line in S.arrivalRadio)
            {
                if (Current != Phase.Arrival) yield break;
                if (hud && !string.IsNullOrEmpty(line)) hud.ShowSubtitle(line, S.arrivalRadioInterval - 0.5f);
                yield return new WaitForSeconds(S.arrivalRadioInterval);
            }
        }

        bool CarParked() => _car && parkingZone && Flat(_car.transform.position - parkingZone.position).magnitude <= S.parkingRadius;
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        Vector3 PlayerPos() => _cc ? _cc.transform.position : transform.position;
        bool InCabin() => ASHVehicle.Driving == null && (cabinShelter ? cabinShelter.Contains(PlayerPos()) : ASHShelterZone.StructuralAt(PlayerPos()));
        public bool PlayerSafe() => ASHVehicle.Driving == null && ASHShelterZone.StructuralAt(PlayerPos());

        void OnDrivingChanged(ASHVehicle v)
        {
            if (v == null && _car && !_car.carriesDog) { var dg = ASHDogCompanion.Active; if (dg && dg.Riding && _cc) dg.EndRide(_cc.transform.position); }
            if (v == null && Current == Phase.Arrival && CarParked()) CompleteArrival("You're home.");
            if (v && v == _truck) MarkTruckFound();
        }

        void CompleteArrival(string msg)
        {
            if (_arrivalDone) return; _arrivalDone = true;
            if (_tracker) _tracker.Complete(IdHome, msg);
            SetPhase(Phase.HomeAgain);
        }

        // ------------------------------------------------------------------ phases / objectives
        void SetPhase(Phase p)
        {
            Current = p;
            if (logPhases) Debug.Log($"[ASH] Prologue phase -> {p}");
            Register();
        }

        bool Done(string id) { var o = _tracker ? _tracker.Find(id) : null; return o != null && o.Completed; }
        void Reg(string id, string title, Func<IEnumerable<(string, bool)>> lines, Func<Vector3?> target = null)
        {
            if (!_tracker || Done(id)) return;
            _tracker.Register(id, title, lines, true, true, target); // takes the focus when new (no Track: it toggles a pick off)
        }

        /// <summary>t440: state flags for ASHObjectiveChain steps (reveal / complete).</summary>
        public bool ChainFlag(string f)
        {
            switch (f)
            {
                case "arrived": return _arrivalDone;
                case "homeAgain": return _homeAgainDone;
                case "journal": return JournalFound();
                case "supplies": return supplies && supplies.Taken;
                case "warning": return Current >= Phase.Warning;
                case "sheltered": return Current >= Phase.CheckCar;
                case "carInspected": return _carInspected;
                case "truckFound": return TruckFound;
                case "truckReady": return Current == Phase.TruckReady;
                default: return false;
            }
        }
        /// <summary>t440: HUD lines + compass target of the prologue main steps (the chain shows them; null = the chain's own lines).</summary>
        public void ChainContent(string id, out Func<IEnumerable<(string, bool)>> lines, out Func<Vector3?> target)
        {
            lines = null; target = null;
            switch (id)
            {
                case IdHome: lines = () => new[] { ("Drive to Grandpa's cabin and park by it", CarParked()), ("Get out of the car", _arrivalDone) }; target = () => parkingZone ? parkingZone.position : (Vector3?)null; break;
                case IdHomeAgain: lines = () => new[] { ("Find Grandpa's journal", JournalFound()), ("Take Grandpa's supplies", supplies && supplies.Taken) }; target = () => !JournalFound() && FindJournalPickup() is Transform t ? t.position : (supplies && !supplies.Taken ? supplies.transform.position : (Vector3?)null); break;
                case IdShelter: lines = () => new[] { ("Get inside the cabin, now", PlayerSafe()) }; target = () => cabinShelter ? cabinShelter.transform.position : (Vector3?)null; break;
                case IdCar: lines = () => new[] { ("Check on the car", _carInspected) }; target = () => _car ? _car.transform.position : (Vector3?)null; break;
                case IdWayOut: lines = () => new[] { ("Find another way to drive out", TruckFound) }; break;
                case IdTruck: lines = TruckLines; target = () => _truck ? _truck.transform.position : (Vector3?)null; break;
            }
        }

        void Register()
        {
            Cache(); if (!_tracker) return;
            if (ASHObjectiveChainRunner.Drives) { var r = ASHObjectiveChainRunner.Instance; if (r && !ASHSaveSystem.IsRestoring) r.Tick(true); return; } // t440: the chain asset owns the order
            switch (Current)
            {
                case Phase.Arrival:
                    Reg(IdHome, "Returning Home", () => new[] { ("Drive to Grandpa's cabin and park by it", CarParked()), ("Get out of the car", _arrivalDone) },
                        () => parkingZone ? parkingZone.position : (Vector3?)null);
                    break;
                case Phase.HomeAgain:
                case Phase.AwaitExit:
                    Reg(IdHomeAgain, "Home Again", () => new[] { ("Find Grandpa's journal", JournalFound()), ("Take Grandpa's supplies", supplies && supplies.Taken) },
                        () => !JournalFound() && FindJournalPickup() is Transform t ? t.position : (supplies && !supplies.Taken ? supplies.transform.position : (Vector3?)null));
                    break;
                case Phase.Warning:
                case Phase.Quake:
                    Reg(IdShelter, "SEEK SHELTER", () => new[] { ("Get inside the cabin, now", PlayerSafe()) }, () => cabinShelter ? cabinShelter.transform.position : (Vector3?)null);
                    break;
                case Phase.CheckCar:
                    Reg(IdCar, "No Way Back", () => new[] { ("Check on the car", _carInspected) }, () => _car ? _car.transform.position : (Vector3?)null);
                    break;
                case Phase.FindWayOut:
                    Reg(IdWayOut, "Find a way out of the wilderness", () => new[] { ("Find another way to drive out", TruckFound) }, null);
                    break;
                case Phase.RepairTruck:
                    if (_tracker && !Done(IdTruck)) { var fo = _tracker.Register(IdTruck, "Repair Grandpa's truck", TruckLines, false, true, () => _truck ? _truck.transform.position : (Vector3?)null); fo.Final = true; } // t438: last main quest, other open tasks first
                    break;
            }
        }

        Transform _journalPickup; bool _journalSearched;
        Transform FindJournalPickup()
        {
            if (!_journalSearched || (_journalPickup == null && !_journalSearched)) { _journalSearched = true; var g = GameObject.Find("ASH_Pickup_GrandpasJournal"); _journalPickup = g ? g.transform : null; }
            return _journalPickup && _journalPickup.gameObject.activeInHierarchy ? _journalPickup : null;
        }
        static bool JournalFound() { var m = ASHFieldManual.Instance; return m && (m.JournalFound || m.IsRead); }

        IEnumerable<(string, bool)> TruckLines()
        {
            var c = _truck ? _truck.Condition : null;
            if (!c) { yield return ("Find the truck's parts", false); yield break; }
            int fitted = Mathf.Clamp(_tiresNeeded - c.MissingTireCount, 0, _tiresNeeded);
            yield return ($"Fit the missing tires ({fitted}/{_tiresNeeded})", c.MissingTireCount == 0);
            yield return ("Install a battery", c.batteryInstalled);
            yield return ("Install a fan belt", c.beltInstalled);
            var f = _truck.Fuel; yield return (f ? $"Fuel it up ({f.litres:F0} L)" : "Fuel it up", !f || f.litres >= 10f);
        }
        /// <summary>t453: truck parts already fitted / fuelled (objective item counts).</summary>
        public int PartInstalled(string itemId)
        {
            var c = _truck ? _truck.Condition : null; var f = _truck ? _truck.Fuel : null;
            switch (itemId)
            {
                case "tire": return c ? Mathf.Clamp(_tiresNeeded - c.MissingTireCount, 0, _tiresNeeded) : 0;
                case "carbattery": return c && c.batteryInstalled ? 1 : 0;
                case "fanbelt": return c && c.beltInstalled ? 1 : 0;
                case "fuel_can": return f && f.litres >= 10f ? 1 : 0;
            }
            return 0;
        }
        public int PartNeeded(string itemId, int fallback) => itemId == "tire" ? _tiresNeeded : fallback;
        bool TruckReady() { var c = _truck ? _truck.Condition : null; var f = _truck ? _truck.Fuel : null; return c && c.MissingTireCount == 0 && c.batteryInstalled && c.beltInstalled && (!f || f.litres >= 10f); }

        // ------------------------------------------------------------------ update
        void Update()
        {
            if (!S || !S.startInCar || Current == Phase.NotStarted || _refirePending) return;
            PerfTick(Current == Phase.Quake ? 8f : 20f);
            HoldNoAsh();
            if (_seq == null) Cache();
            _slowT -= Time.deltaTime; bool slow = _slowT <= 0f; if (slow) _slowT = 0.25f;
            if (slow && !TruckFound && _truck && Flat(_truck.transform.position - PlayerPos()).magnitude <= S.truckFoundRadius) MarkTruckFound();
            switch (Current)
            {
                case Phase.Arrival:
                    if (slow && S.cabinEntryCompletesArrival && InCabin()) CompleteArrival("You're home.");
                    break;
                case Phase.HomeAgain:
                    if (JournalFound()) { _homeAgainDone = true; if (_tracker) _tracker.Complete(IdHomeAgain, "Grandpa's journal. His handwriting."); Current = Phase.AwaitExit; if (logPhases) Debug.Log("[ASH] Prologue phase -> AwaitExit"); }
                    break;
                case Phase.AwaitExit:
                    if (InCabin()) _outsideT = 0f;
                    else { _outsideT += Time.deltaTime; if (_outsideT >= S.exitConfirmSeconds) StartWarning(true); }
                    break;
                case Phase.Warning:
                case Phase.Quake:
                    TickWarning();
                    break;
                case Phase.RepairTruck:
                    if (slow && TruckReady()) { if (_tracker) _tracker.Complete(IdTruck, "Grandpa's truck is ready. (End of M1)"); SetPhase(Phase.TruckReady); }
                    break;
            }
        }

        // ------------------------------------------------------------------ t442: no ash before the first quake
        bool _noAshLogged; float _noAshT;
        void HoldNoAsh()
        {
            if (!S.noAshBeforeQuake || Current >= Phase.Quake) return;
            _noAshT -= Time.deltaTime; if (_noAshT > 0f) return; _noAshT = 0.25f;
            var acc = ASH.World.Accumulation.ASHAccumulationController.Instance; float g = acc ? acc.ashAmount : 0f;
            if (acc && acc.ashAmount > 0f) acc.ashAmount = 0f;
            var wd = ASH.World.Weather.ASHWeatherDirector.Instance; var w = wd ? wd.Current : default;
            bool ashy = wd && (w.ashfall > 0.001f || w.skyAshTint > 0.001f);
            if (ashy) { wd.SetScheduler(false); wd.Snap(wd.Profile(S.preQuakeWeather) ? S.preQuakeWeather : ASH.World.Weather.ASHWeatherKind.Clear); }
            if (!_noAshLogged || ashy || g > 0f) { _noAshLogged = true; Debug.Log($"[ASH] Prologue no-ash lock ({Current}): ground ash {g:F2}->0, weather ash {w.ashfall:F2} tint {w.skyAshTint:F2}{(ashy ? " -> snapped to " + S.preQuakeWeather : "")}"); }
        }

        // ------------------------------------------------------------------ P02 warning, quake, death, checkpoint
        void StartWarning(bool writeCheckpoint)
        {
            Cache();
            SetPhase(Phase.Warning); // phase is part of the checkpoint (restore re-fires the warning)
            _killChecked = false; _treeStarted = false; _warnStart = Time.time;
            if (writeCheckpoint && ASHSaveSystem.Instance)
            {
                bool ok = ASHSaveSystem.Instance.Save(S.checkpointSlot);
                if (logPhases) Debug.Log($"[ASH] Prologue checkpoint '{S.checkpointSlot}' {(ok ? "written" : "FAILED")}");
            }
            if (_seq) _seq.BeginFromWarning(S.warningSeconds);
            var hud = FindAnyObjectByType<ASHHud>(); if (hud) hud.ShowSubtitle(S.warningSubtitle, 8f);
            ShowBanner(true);
            PerfBegin("warning");
            if (logPhases) Debug.Log($"[ASH] Prologue: SEEK SHELTER warning, quake in {S.warningSeconds:F0} s");
        }

        void TickWarning()
        {
            float left = _warnStart >= 0f ? S.warningSeconds - (Time.time - _warnStart) : 0f;
            if (_banner && Current == Phase.Warning) { _bannerSub.text = left > 0f ? $"{Mathf.CeilToInt(left)} s" : ""; _banner.color = new Color(0.95f, 0.2f, 0.15f, 0.65f + 0.35f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f))); }
            if (!_seq) return;
            var q = _seq.quake;
            if (_seq.Current == ASHEruptionSequence.Stage.Quake)
            {
                if (Current == Phase.Warning) { Current = Phase.Quake; ShowBanner(false); if (logPhases) Debug.Log("[ASH] Prologue phase -> Quake"); }
                bool peak = q ? q.Intensity >= S.killAtIntensity * Mathf.Max(0.01f, q.maxIntensity) : _seq.StageTime > 8f;
                if (peak && !_killChecked) PeakCheck();
            }
            else if (_seq.Current > ASHEruptionSequence.Stage.Quake && _seq.Current != ASHEruptionSequence.Stage.Inactive)
            {
                if (!_killChecked) PeakCheck();
                if (_stats && _stats.IsDead) return;
                ShowBanner(false);
                if (_tracker) _tracker.Complete(IdShelter, "The shaking stops.");
                SetPhase(Phase.CheckCar);
            }
        }

        void PeakCheck()
        {
            _killChecked = true;
            bool safe = PlayerSafe();
            Debug.Log($"[ASH] Prologue quake peak: player {(safe ? "SAFE in a structural shelter" : "OUTSIDE -> dies")} at {PlayerPos():F1}");
            if (!safe && _stats && !_stats.IsDead)
            {
                var hud = FindAnyObjectByType<ASHHud>(); if (hud) hud.Toast(S.deathOutsideToast);
                _stats.Damage(_stats.MaxHealth * 100f);
                return;
            }
            if (!_treeStarted) StartCoroutine(TreeRoutine());
        }

        IEnumerator TreeRoutine()
        {
            _treeStarted = true;
            if (!fallingTree || !_car || TreeFelled) yield break;
            yield return new WaitForSeconds(S.treeDelay);
            if (_stats && _stats.IsDead) yield break;
            if (!CarParked() && parkingZone && ASHVehicle.Driving != _car) // moved while the player is indoors
            {
                var rb = _car.GetComponent<Rigidbody>();
                if (rb) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.position = parkingZone.position; rb.rotation = parkingZone.rotation; }
                _car.transform.SetPositionAndRotation(parkingZone.position, parkingZone.rotation); Physics.SyncTransforms();
                Debug.Log("[ASH] Prologue: car was not in the parking zone; moved there out of sight");
            }
            _car.SetDestroyed(true); // frozen now (kinematic), the crushed state lands with the tree
            float top = CarTop() - fallingTree.transform.position.y;
            Camera main = null; Camera cut = null; Coroutine fall;
            if (S.cinematicSeconds > 0f && cinematicCamera)
            {
                main = Camera.main;
                cut = cinematicCamera.GetComponent<Camera>(); if (!cut) cut = cinematicCamera.gameObject.AddComponent<Camera>();
                cut.nearClipPlane = 0.1f; cut.farClipPlane = 1500f; if (main) cut.fieldOfView = main.fieldOfView;
                var look = (_car.transform.position + fallingTree.transform.position) * 0.5f + Vector3.up * 2f;
                cinematicCamera.rotation = Quaternion.LookRotation(look - cinematicCamera.position);
                cut.enabled = true; if (main) main.enabled = false;
                ASHUIState.SetOpen("cinematic", true); Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
                PerfBegin("tree cinematic");
            }
            fall = StartCoroutine(fallingTree.Fall(top, S.treeFallSeconds, () =>
            {
                TreeFelled = true; _treePose = fallingTree.transform.rotation;
                if (_seq && _seq.quake) _seq.quake.PlayFall(_car.transform.position);
                Debug.Log("[ASH] Prologue: the tree landed on the car");
            }));
            if (cut)
            {
                yield return new WaitForSeconds(S.cinematicSeconds);
                cut.enabled = false; if (main) main.enabled = true;
                ASHUIState.SetOpen("cinematic", false);
            }
        }

        float CarTop()
        {
            float top = _car.transform.position.y + 1.5f; bool any = false;
            foreach (var r in _car.GetComponentsInChildren<Renderer>()) { if (!r.enabled || r is ParticleSystemRenderer) continue; top = any ? Mathf.Max(top, r.bounds.max.y) : r.bounds.max.y; any = true; }
            return top;
        }

        void OnDied()
        {
            _deadThisRun = true;
            if (logPhases) Debug.Log($"[ASH] Prologue: player died in phase {Current}{(SavesLocked ? " (checkpoint retry offered)" : "")}");
        }

        string Guard(string slot)
        {
            if (!SavesLocked || S == null || slot == S.checkpointSlot) return null;
            var hud = FindAnyObjectByType<ASHHud>(); if (hud && slot != ASHSaveSystem.AutoSlot) hud.Toast(S.saveBlockedToast);
            return "prologue warning/quake";
        }

        void Retry()
        {
            var d = ASHSaveSystem.Read(S.checkpointSlot);
            if (d == null) { Debug.LogWarning("[ASH] Prologue: no checkpoint to retry"); ASHSceneFlow.NewGame(); return; }
            Debug.Log("[ASH] Prologue: retry from checkpoint");
            ASHSaveSystem.Load(d);
        }

        void OnRestored()
        {
            if (Current == Phase.Warning || Current == Phase.Quake) StartCoroutine(Refire());
        }
        IEnumerator Refire()
        {
            _refirePending = true; // nothing ticks until the warning re-fires (no double trigger, saves stay locked)
            yield return new WaitForSeconds(S.retryRefireDelay);
            _refirePending = false;
            Debug.Log("[ASH] Prologue: checkpoint loaded -> warning re-fires");
            StartWarning(false);
        }

        // ------------------------------------------------------------------ P03
        void OnInspected(ASHVehicle v)
        {
            if (v != _car) return;
            var hud = FindAnyObjectByType<ASHHud>(); if (hud) hud.ShowSubtitle(S.carInspectText, 5f);
            if (Current != Phase.CheckCar || _carInspected) return;
            _carInspected = true;
            if (_tracker) _tracker.Complete(IdCar, "The car is finished.");
            SetPhase(Phase.FindWayOut);
            if (TruckFound) AdvanceToRepair();
        }

        void OnPageFound(string col, string id) { if (col == "grandpa" && id == "gj_02") MarkTruckFound(); }

        void MarkTruckFound()
        {
            if (!TruckFound) { TruckFound = true; if (logPhases) Debug.Log("[ASH] Prologue: Grandpa's truck found"); }
            if (Current == Phase.FindWayOut) AdvanceToRepair();
        }

        void AdvanceToRepair()
        {
            if (_tracker) _tracker.Complete(IdWayOut, "Grandpa's old truck. If it ran once, it can run again.");
            SetPhase(Phase.RepairTruck);
            if (S.endAftershocksOnTruckObjective && _seq) _seq.EndAftershocks();
        }

        // ------------------------------------------------------------------ banner
        void ShowBanner(bool on)
        {
            if (on && !_bannerCanvas)
            {
                var go = new GameObject("ASH_PrologueBanner", typeof(Canvas), typeof(CanvasScaler)); go.transform.SetParent(transform, false);
                _bannerCanvas = go.GetComponent<Canvas>(); _bannerCanvas.renderMode = RenderMode.ScreenSpaceOverlay; _bannerCanvas.sortingOrder = 150;
                var sc = go.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080);
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                _banner = MakeText(go.transform, font, 64, new Vector2(0, -150), FontStyle.Bold);
                _bannerSub = MakeText(go.transform, font, 30, new Vector2(0, -215), FontStyle.Normal);
            }
            if (_bannerCanvas) _bannerCanvas.gameObject.SetActive(on);
            if (on && _banner) { _banner.text = S.warningBanner; }
        }
        static Text MakeText(Transform parent, Font f, int size, Vector2 pos, FontStyle st)
        {
            var go = new GameObject("Text", typeof(RectTransform)); go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(1400, 90);
            var t = go.AddComponent<Text>(); t.font = f; t.fontSize = size; t.fontStyle = st; t.alignment = TextAnchor.MiddleCenter; t.color = new Color(0.95f, 0.85f, 0.8f);
            go.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.8f);
            return t;
        }

        // ------------------------------------------------------------------ debug
        public string Describe() => $"phase={Current} car={(CarParked() ? "parked" : "away")} inCabin={InCabin()} safe={PlayerSafe()} tree={(TreeFelled ? "felled" : "standing")} truckFound={TruckFound} supplies={(supplies ? (supplies.Taken ? "taken" : supplies.Touched ? "partly" : "full") : "none")} journal={JournalFound()} seq={(_seq ? _seq.Current.ToString() : "none")} locked={SavesLocked} dead={(_stats && _stats.IsDead)}";
        /// <summary>Dev: inspect the car as if E was pressed on it.</summary>
        public void DevInspectCar() { Cache(); if (_car) OnInspected(_car); }
        /// <summary>Dev: jump to the warning (as if leaving the cabin after P01, checkpoint included).</summary>
        public void DevStartWarning() { if (Current < Phase.Warning) { _arrivalDone = _homeAgainDone = true; if (_tracker) { _tracker.Complete(IdHome); _tracker.Complete(IdHomeAgain); } StartWarning(true); } }

        // ------------------------------------------------------------------ save
        [Serializable] sealed class State
        {
            public int phase; public bool arrivalDone, homeAgainDone, treeFelled, carInspected, truckFound, suppliesTouched;
            public Quaternion treePose; public List<int> suppliesLeft = new List<int>();
        }
        public string SaveKey => "prologue";
        public int RestoreOrder => 45; // after the vehicles (40): the car's destroyed flag is already back
        public string CaptureJson()
        {
            var s = new State { phase = (int)Current, arrivalDone = _arrivalDone, homeAgainDone = _homeAgainDone, treeFelled = TreeFelled, treePose = _treePose, carInspected = _carInspected, truckFound = TruckFound };
            if (supplies) { s.suppliesTouched = supplies.Touched; var l = supplies.Capture(); if (l != null) s.suppliesLeft = l; }
            return JsonUtility.ToJson(s);
        }
        public void RestoreJson(string json)
        {
            var s = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<State>(json); if (s == null) return;
            Cache();
            StopAllCoroutines(); ShowBanner(false);
            _arrivalDone = s.arrivalDone; _homeAgainDone = s.homeAgainDone; _carInspected = s.carInspected; TruckFound = s.truckFound;
            TreeFelled = s.treeFelled; _treePose = s.treePose; _killChecked = false; _treeStarted = false; _outsideT = 0f;
            if (fallingTree) { if (TreeFelled) fallingTree.SetFallen(_treePose); else fallingTree.ResetUpright(); }
            if (supplies) supplies.Restore(s.suppliesLeft, s.suppliesTouched);
            Current = (Phase)Mathf.Clamp(s.phase, 0, (int)Phase.TruckReady);
            if (logPhases) Debug.Log($"[ASH] Prologue restored at {Current}");
            _refirePending = false;
            if (Current != Phase.Warning && Current != Phase.Quake) Register(); // the warning registers itself when it re-fires
        }
    }
}
