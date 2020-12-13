using Expansions.Serenity;
using KSP.UI.Screens.Flight;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace PhysicsHold
{
    public class VesselPhysicsHold : VesselModule
    {
        private static FieldInfo framesAtStartupFieldInfo;
        private static string cacheAutoLOC_459494;
        private static MethodInfo KerbalPortrait_CanEVA;

        private static bool staticInitDone = false;

        [KSPField(isPersistant = true)] public bool physicsHold;

        [KSPField(isPersistant = true)] public bool roboticsOverride;

        private bool isEnabled = true;

        public bool delayedPhysicsHoldEnableRequest;
        private bool isChangingState;

        public bool HasRobotics { get; private set; }

        public override bool ShouldBeActive()
        {
            return
                HighLogic.LoadedSceneIsFlight
                && vessel.loaded
                && !vessel.isEVA
                && vessel.id != Guid.Empty // exclude flags
                && isEnabled;
        }

        #region LIFECYCLE

        protected override void OnAwake()
        {
            if (!staticInitDone)
            {
                staticInitDone = true;

                try
                {
                    framesAtStartupFieldInfo = typeof(Vessel).GetField("framesAtStartup", BindingFlags.Instance | BindingFlags.NonPublic);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Cant find the Vessel.framesAtStartup field\n{e}");
                    staticInitDone = false;
                }

                try
                {
                    cacheAutoLOC_459494 = (string)typeof(KerbalPortrait).GetField("cacheAutoLOC_459494", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Cant find the EVA available tooltip AUTOLOC\n{e}");
                    cacheAutoLOC_459494 = string.Empty;
                }

                try
                {
                    KerbalPortrait_CanEVA = typeof(KerbalPortrait).GetMethod("CanEVA", BindingFlags.Instance | BindingFlags.NonPublic);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Cant find the KerbalPortrait.CanEVA method\n{e}");
                    staticInitDone = false;
                }

                if (!staticInitDone)
                {
                    isEnabled = false;
                }
            }
        }

        public override void OnLoadVessel()
        {
            // not allowed for EVA kerbals and flags (note : vessel.IsEVA isn't always set in OnStart())
            if (vessel.isEVA || vessel.id == Guid.Empty)
            {
                isEnabled = false;
                return;
            }

            Lib.LogDebug($"Starting for {vessel.vesselName}, physicsHold={physicsHold}");

            delayedPhysicsHoldEnableRequest = false;
            isChangingState = false;

            StartCoroutine(SetupDockingNodesHoldState());
            StartCoroutine(WaitForVesselInitDoneOnLoad());

            GameEvents.onPartCouple.Add(OnPartCouple); // before docking/coupling
            GameEvents.onPartDeCouple.Add(OnPartDeCouple); // before coupling
            GameEvents.onVesselsUndocking.Add(OnVesselsUndocking); // after docking
        }

        private IEnumerator WaitForVesselInitDoneOnLoad()
        {
            while (!FlightGlobals.VesselsLoaded.Contains(vessel) || vessel.vesselName == null || !vessel.parts[0].started)
            {
                yield return new WaitForFixedUpdate();
            }

            PhysicsHoldManager.AddInstance(this);

            if (physicsHold)
            {
                DoPostOnGoOnRailsTweaks(false, true);
            }
        }

        public override void OnUnloadVessel()
        {
            PhysicsHoldManager.RemoveInstance(this);
            ClearEvents();
        }

        private void ClearEvents()
        {
            GameEvents.onPartCouple.Remove(OnPartCouple);
            GameEvents.onPartDeCouple.Remove(OnPartDeCouple);
            GameEvents.onVesselsUndocking.Remove(OnVesselsUndocking);
        }

        public void OnDestroy()
        {
            if (HighLogic.LoadedScene == GameScenes.LOADING)
                return;

            ClearEvents();
        }

        #endregion

        #region UPDATE


        private void EnablePhysicsHold()
        {
            if (physicsHold || !vessel.Landed)
                return;

            Lib.LogDebug($"Enabling physics hold for {vessel.vesselName}");

            physicsHold = true;

            vessel.GoOnRails();

            DoPostOnGoOnRailsTweaks(false, true);
        }

        private void DisablePhysicsHold(bool immediate)
        {
            Lib.LogDebug($"Disabling physics hold ({vessel.vesselName})");

            physicsHold = false;

            if (SetupWheels())
            {
                immediate = true;
            }

            if (immediate)
            {
                framesAtStartupFieldInfo.SetValue(vessel, Time.frameCount - 100);
                vessel.GoOffRails();
            }
            else
            {
                framesAtStartupFieldInfo.SetValue(vessel, Time.frameCount);
            }
        }

        public void Update()
        {
            if (physicsHold)
            {
                // keep the vessel forever in "physics hold" mode by resetting the "last off rails" frame to the current one.
                framesAtStartupFieldInfo.SetValue(vessel, Time.frameCount);

                // remove the "physics hold" control lock
                // note that we don't set the private Vessel.physicsHoldLock field to false, resulting in the Vessel.HoldPhysics property staying true
                // The only impact (in stock) is that g-force / dynamic pressure checks for breaking deployable parts (solar panels/radiators/antennas)
                // won't run, which is good.
                if (vessel.isActiveVessel)
                {
                    InputLockManager.RemoveControlLock("physicsHold");

                }
            }
        }

        // KerbalPortrait will prevent the "go to EVA" button from working if Part.packed is true (no such limitation on the "crew transfer" PAW UI)
        // This is done in Update(), so we un-do it in LateUpdate()
        public void LateUpdate()
        {
            if (!vessel.isActiveVessel || !physicsHold || KerbalPortraitGallery.Instance == null)
                return;

            foreach (KerbalPortrait kp in KerbalPortraitGallery.Instance.Portraits)
            {
                if (kp.hoverArea.Hover && !kp.evaButton.interactable && kp.crewMember != null)
                {
                    kp.crewMember.InPart.packed = false;

                    try
                    {
                        kp.evaButton.interactable = (bool)KerbalPortrait_CanEVA.Invoke(kp, null);
                    }
                    catch (Exception) { }

                    kp.crewMember.InPart.packed = true;

                    if (kp.evaButton.interactable)
                        kp.evaTooltip.textString = cacheAutoLOC_459494;
                }
            }
        }

        public void FixedUpdate()
        {
            if (delayedPhysicsHoldEnableRequest && vessel.Landed)
            {
                delayedPhysicsHoldEnableRequest = false;
                EnablePhysicsHold();
            }
        }

        #endregion

        #region UI EVENTS

        public bool CanTogglePhysicsHold()
        {
            // can't toggle if callbacks are not in place, if not landed, or if surface speed is higher than 1 m/s
            if (isChangingState || !vessel.Landed || vessel.srfSpeed > 1.0)
                return false;

            // Can't toggle during timewarp
            if (TimeWarp.WarpMode == TimeWarp.Modes.HIGH && TimeWarp.CurrentRate > TimeWarp.MaxPhysicsRate)
                return false;

            if (physicsHold)
            {
                // can't toggle if vessel is outside of unpacking range
                if (!vessel.isActiveVessel)
                {
                    float distanceFromActive = Vector3.Distance(vessel.transform.position, FlightGlobals.ActiveVessel.transform.position);
                    if (distanceFromActive > vessel.vesselRanges.GetSituationRanges(vessel.situation).unpack)
                        return false;
                }
            }
            else
            {
                // can't toggle during stock physics easing
                if (vessel.packed)
                    return false;
            }

            return true;
        }

        public void OnToggleHold(bool enabled)
        {
            if (enabled && !physicsHold)
            {
                EnablePhysicsHold();
            }
            else if (!enabled && physicsHold)
            {
                DisablePhysicsHold(false);
            }
        }

        public bool CanApplyDeformation()
        {
            // Can't toggle during timewarp
            if (TimeWarp.WarpMode == TimeWarp.Modes.HIGH && TimeWarp.CurrentRate > TimeWarp.MaxPhysicsRate)
                return false;

            // can't toggle during physics hold or stock physics easing
            if (physicsHold || vessel.packed)
                return false;

            // can't toggle if not landed, or if surface speed is higher than 0.05 m/s
            if (!vessel.Landed || vessel.srfSpeed > 0.05)
                return false;

            return true;
        }

        public void OnApplyDeformation()
        {
            if (!CanApplyDeformation())
                return;

            Vector3 rootPos = vessel.rootPart.transform.position;
            Quaternion rootInvRot = Quaternion.Inverse(vessel.rootPart.transform.rotation);

            foreach (Part part in vessel.Parts)
            {
                part.orgPos = rootInvRot * (part.transform.position - rootPos);
                part.orgRot = rootInvRot * part.transform.rotation;
            }

            framesAtStartupFieldInfo.SetValue(vessel, Time.frameCount);
            vessel.GoOnRails();
        }

        public bool CanToggleRobotics()
        {
            // Can't toggle during timewarp
            if (TimeWarp.WarpMode == TimeWarp.Modes.HIGH && TimeWarp.CurrentRate > TimeWarp.MaxPhysicsRate)
                return false;

            // can't toggle during stock physics easing or if vessel is too far away
            if (!physicsHold && vessel.packed)
                return false;

            if (isChangingState || !HasRobotics)
                return false;

            return true;
        }

        public void OnToggleRobotics(bool enabled)
        {
            if (!enabled && roboticsOverride)
            {
                roboticsOverride = false;
                if (physicsHold)
                {
                    DoPostOnGoOnRailsTweaks(true, true);
                }
            }
            else if (enabled && !roboticsOverride)
            {
                roboticsOverride = true;
                if (physicsHold)
                {
                    DoPostOnGoOnRailsTweaks(false, true);
                }
            }
        }

        #endregion

        #region EVENTS


        // Called when a docking/coupling action is about to happen. Gives access to old and new vessel
        // Remove PAW buttons from the command parts and disable ourselves when the vessel 
        // is about to be removed following a docking / coupling operation.
        private void OnPartCouple(GameEvents.FromToAction<Part, Part> data)
        {
            Lib.LogDebug($"OnPartCouple on {vessel.vesselName}, docked vessel : {data.from.vessel.vesselName}, dominant vessel : {data.to.vessel.vesselName}");

            // in the case of KIS-adding parts, from / to vessel are the same : 
            // we ignore the event and just pack the part.
            if (data.from.vessel == data.to.vessel && data.from.vessel == vessel && physicsHold)
            {
                OnPackPartTweaks(data.from, true, false, true);
                return;
            }
                
            // "from" is the part on the vessel that will be removed following coupling/docking
            // when docking to a packed vessel, depending on the stock choice for which one is the dominant vessel, we have two cases :
            // A. the dominant vessel is the on-hold vessel
            //    - non-packed parts will be transferred to the resulting (already packed) vessel, we have to pack them
            // B. the dominant vessel is the not-on-hold vessel :
            //    - the resulting vessel isn't packed, and physicsHold is false
            //    - transferred parts will be packed, the others won't

            // from : docking vessel that will be removed
            if (data.from.vessel == vessel)
            {
                PhysicsHoldManager.RemoveInstance(this);

                // case B handling
                if (physicsHold)
                {
                    DisablePhysicsHold(true);
                    VesselPhysicsHold fromInstance = data.to.vessel.GetComponent<VesselPhysicsHold>();
                    fromInstance.delayedPhysicsHoldEnableRequest = true;
                }

                physicsHold = false;
                ClearEvents();
                isEnabled = false;
            }

            // case A handling
            if (data.to.vessel == vessel && physicsHold)
            {
                foreach (Part part in data.from.vessel.Parts)
                {
                    OnPackPartTweaks(part, true, false, true);
                }
            }
        }

        // called before a new vessel is created following intentional decoupler use or a joint failure
        // the part.vessel reference is still the old, non separated vessel
        private void OnPartDeCouple(Part part)
        {
            if (part.vessel != vessel)
                return;

            if (physicsHold)
            {
                DisablePhysicsHold(true);
            }
        }

        // called after a new vessel is created following undocking
        private void OnVesselsUndocking(Vessel oldVessel, Vessel newVessel)
        {
            Lib.LogDebug($"OnVesselsUndocking called on {vessel.vesselName}, oldVessel {oldVessel.vesselName}, newVessel {newVessel.vesselName}");

            if (vessel != oldVessel)
                return;

            if (physicsHold)
            {
                VesselPhysicsHold newVesselHoldInstance = newVessel.GetComponent<VesselPhysicsHold>();
                newVesselHoldInstance.delayedPhysicsHoldEnableRequest = true;
            }
        }

        #endregion

        #region SPECIFIC HACKS

        /// <summary>
        /// Wheels have a wheelSetup() method being called by a coroutine launched from OnStart(). That coroutine is waiting indefinitely 
        /// for part.packed to become false, which won't happen if the vessel is in hold since the scene start. This is an issue if we want
        /// to dock/undock, as wheels have a onVesselUndocking/onVesselDock callback that will nullref if the setup isn't done.
        /// So, when we undock a packed vessel, if that vessel has never been unpacked, we manually call wheelSetup(), and cancel the
        /// coroutine (wheelSetup() will nullref if called twice).
        /// </summary>
        private bool SetupWheels()
        {
            bool setupDone = false;
            foreach (ModuleWheelBase wheel in vessel.FindPartModulesImplementing<ModuleWheelBase>())
            {
                // TODO : cache the fieldinfo / methodinfo
                if (!((bool)typeof(ModuleWheelBase).GetField("setup", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(wheel)))
                {
                    wheel.StopAllCoroutines();
                    setupDone = true;
                    typeof(ModuleWheelBase).GetMethod("wheelSetup", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(wheel, null);
                }
            }

            return setupDone;
        }


        /// <summary>
        /// Patch every ModuleDockingNode FSM to trick them into believing on-hold vessels aren't packed
        /// </summary>
        private IEnumerator SetupDockingNodesHoldState()
        {
            foreach (Part part in vessel.Parts)
            {
                // part.dockingPorts is populated from Awake()
                foreach (PartModule partModule in part.dockingPorts)
                {
                    if (!(partModule is ModuleDockingNode dockingNode))
                        continue;

                    while (dockingNode.st_ready == null)
                    {
                        yield return null;
                    }

                    if (dockingNode.st_ready.OnFixedUpdate.GetInvocationList().Length > 1)
                        continue;

                    // we can't remove the existing stock delegate, so we add our own that will be called 
                    // after the stock one, with our patched method.
                    dockingNode.st_ready.OnFixedUpdate += delegate { FindNodeApproachesPatched(dockingNode); };
                }
            }
            yield break;
        }

        /// <summary>
        /// Call the stock ModuleDockingNode.FindNodeApproaches(), and prevent it to ignore the vessels we are putting
        /// on physics hold by temporary setting Vessel.packed to false. Not that there is also a check on the Part.packed
        /// field, but we don't need to handle it since we always unpack parts that have a ModuleDockingNode.
        /// </summary>
        private void FindNodeApproachesPatched(ModuleDockingNode dockingNode)
        {
            if (PhysicsHoldManager.Instance == null || PhysicsHoldManager.Instance.OnHoldAndPackedInstances.Count == 0)
                return;

            foreach (VesselPhysicsHold instance in PhysicsHoldManager.Instance.OnHoldAndPackedInstances)
            {
                instance.vessel.packed = false;
            }

            try
            {
                dockingNode.otherNode = dockingNode.FindNodeApproaches();
            }
            catch (Exception e)
            {
                Debug.LogError($"ModuleDockingNode threw during FindNodeApproaches\n{e}");
            }
            finally
            {
                foreach (VesselPhysicsHold instance in PhysicsHoldManager.Instance.OnHoldAndPackedInstances)
                {
                    instance.vessel.packed = true;
                }
            }
        }

        private void DoPostOnGoOnRailsTweaks(bool doPack, bool doUnpack)
        {
            HashSet<uint> roboticChildParts = new HashSet<uint>();
            GetRoboticChilds(roboticChildParts, vessel.rootPart, false);

            HasRobotics = roboticChildParts.Count > 0;

            if (!HasRobotics || !roboticsOverride)
            {
                foreach (Part part in vessel.parts)
                    OnPackPartTweaks(part, doPack, doUnpack, true);
            }
            else
            {
                foreach (Part part in vessel.parts)
                    OnPackPartTweaks(part, doPack, doUnpack, false, roboticChildParts.Contains(part.flightID));
            }
        }

        private static void OnPackPartTweaks(Part part, bool doPack, bool doUnpack, bool stopRoboticsController, bool dontPackOverride = false)
        {
            // root part can never be unpacked as the orbit/floating origin/krakensbane code
            // are checking the root part packed flag to select their handling mode
            if (part.vessel.rootPart == part)
            {
                if (doPack)
                    part.Pack();

                return;
            }

            bool dontPack = dontPackOverride;
            foreach (PartModule module in part.Modules)
            {
                if (part.children.Count == 0 
                    && module is ModuleDeployablePart mdp 
                    && mdp.deployState != ModuleDeployablePart.DeployState.BROKEN 
                    && (mdp.hasPivot || mdp.panelBreakTransform != null))
                {
                    dontPack |= true;
                }
                else if (module is ModuleDockingNode || module is ModuleGrappleNode)
                {
                    dontPack |= true;
                }
                // wheels should never be unpacked, no exceptions. Otherwise, vessel go boom.
                else if (module is ModuleWheelBase)
                {
                    dontPack = false;
                    break;
                }
                else if (stopRoboticsController && module is ModuleRoboticController mrc)
                {
                    mrc.SequenceStop();
                }
            }

            if (doUnpack && dontPack)
            {
                part.Unpack();
                return;
            }

            if (doPack && !dontPack)
            {
                part.Pack();
            }

        }

        private void GetRoboticChilds(HashSet<uint> roboticParts, Part parentPart, bool parentIsRobotic)
        {
            if (!parentIsRobotic && parentPart.isRobotic())
            {
                parentIsRobotic = true;
            }

            if (parentIsRobotic)
            {
                if (parentPart.vessel.rootPart == parentPart)
                {
                    roboticParts.Clear();
                    ScreenMessages.PostScreenMessage($"PhysicsHold\nCan't enable robotics, the vessel root part\n{parentPart.partInfo.title}\nis a child of a robotic part");
                    return;
                }

                roboticParts.Add(parentPart.flightID);
            }

            foreach (Part child in parentPart.children)
            {
                GetRoboticChilds(roboticParts, child, parentIsRobotic);
            }
        }

        #endregion
    }
}
