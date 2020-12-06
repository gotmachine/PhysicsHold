using Expansions.Serenity;
using KSP.UI.Screens.Flight;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using Debug = UnityEngine.Debug;

/*
This add a "Landed physics hold" PAW button on all command parts (and root part if no command part found), 
available when the vessel is landed and has a surface speed less than 1 m/s (arbitrary, could more/less).
When enabled, all rigibodies on the vessel are made kinematic by forcing the stock "packed" (or "on rails")
state normally used during "physics easing" and non-physics timewarp.

When enabled, all joint/force/torque physics are disabled, making the vessel an unmovable object fixed at
a given altitude/longitude/latitude. You can still collide with it, but it will not react to collisions.

Working and tested :
  - Docking : docking to a on hold vessel will seamlessly put the merged vessel on hold
  - Undocking : the dominant vessel will stay on physics hold, the undocking vessel will be physics enabled
  - Grabbing (Klaw) to a on hold vessel work, same behavior as a docking port.
  - Decoupling : works by insta-restoring physics, so far no issues detected
  - Collisions will destroy on-hold parts if crash tolerance is exceeded (and trigger decoupling events)
  - Breakable parts : solar panels, radiators and antennas will stay non-kinematic and can break.
  - Initizalization issue with wheels taken care of
  - EVAing / boarding (hack in place to enable the kerbal portraits UI)
  - Control input (rotation, translation, throttle...) is forced to zero by the stock vessel.packed check
  - KIS attaching parts *seems* to work but they currently aren't made kinematic until a scene reload

Not working / Known issues :
  - Same vessel multi-node docking/undocking works, but using the "make primary node" button when multiple 
    nodes are engaged doesn't (it works in a single-node docked case). Not sure exactly what is going on.
  - Stock robotic parts won't be able to move.
  - KAS errors out as soon as a KAS connection exists on a on-hold vessel, resulting in vessels being 
    immediately deleted. It probably can work at least partially since it is able to handle things in 
    timewarp, but that would likely require quite a bit of extra handling on its side.

Untested / likely to have issues :
  - USI Konstruction things are reported to work, but I'm a bit skeptical and haven't done any test.
  - Infernal Robotics : untested, will probably have issues
*/

namespace PhysicsHold
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class PhysicsHoldManager : MonoBehaviour
    {
        public List<PhysicsHold> activeInstances = new List<PhysicsHold>();
        public List<PhysicsHold> onHoldAndPackedInstances = new List<PhysicsHold>();

        public static PhysicsHoldManager fetch { get; private set; }

        private void Awake()
        {
            fetch = this;
        }

        private void FixedUpdate()
        {
            onHoldAndPackedInstances.Clear();
            foreach (PhysicsHold instance in activeInstances)
            {
                if (instance.Vessel.packed && instance.physicsHold)
                {
                    onHoldAndPackedInstances.Add(instance);
                }
            }
        }

        private void OnDestroy()
        {
            fetch = null;
        }
    }


    public class PhysicsHold : VesselModule
    {
        private static string cacheAutoLOC_459494;
        private static FieldInfo framesAtStartupFieldInfo;
        private static MethodInfo KerbalPortrait_CanEVA;
        private static FieldInfo pawToggleHoldField;
        private static FieldInfo roboticsOverrideField;


        private static bool initDone = false;

        [KSPField(isPersistant = true)] public bool physicsHold;
        public bool pawToggleHold;

        [KSPField(isPersistant = true)] public bool roboticsOverride;

        private List<CommandPart> commandParts;
        private Vessel lastDecoupledPartVessel;
        private bool hasNeverBeenUnpacked;
        private bool isEnabled = true;

        private List<PartModule> roboticModules;

        public override bool ShouldBeActive()
        {
            bool shouldBeActive =
                HighLogic.LoadedSceneIsFlight
                && vessel.loaded
                && !vessel.isEVA
                && vessel.id != Guid.Empty // exclude flags
                && isEnabled;

            if (HighLogic.LoadedSceneIsFlight && isEnabled && !shouldBeActive && !ReferenceEquals(PhysicsHoldManager.fetch, null))
            {
                PhysicsHoldManager.fetch.activeInstances.Remove(this);
            }

            return shouldBeActive;
        }

        #region LIFECYCLE

        protected override void OnAwake()
        {
            if (!initDone)
            {
                initDone = true;

                pawToggleHoldField = GetType().GetField(nameof(pawToggleHold));
                roboticsOverrideField = GetType().GetField(nameof(roboticsOverride));

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
                    framesAtStartupFieldInfo = typeof(Vessel).GetField("framesAtStartup", BindingFlags.Instance | BindingFlags.NonPublic);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Cant find the Vessel.framesAtStartup field\n{e}");
                    initDone = false;
                }

                try
                {
                    KerbalPortrait_CanEVA = typeof(KerbalPortrait).GetMethod("CanEVA", BindingFlags.Instance | BindingFlags.NonPublic);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Cant find the KerbalPortrait.CanEVA method\n{e}");
                    initDone = false;
                }

                if (!initDone)
                {
                    isEnabled = enabled = false;
                }
            }
        }

        protected override void OnStart()
        {
            // not allowed for EVA kerbals and flags (note : vessel.IsEVA isn't always set in OnStart())
            if (vessel.isEVA || vessel.id == Guid.Empty)
            {
                isEnabled = enabled = false;
                return;
            }

            PhysicsHoldManager.fetch.activeInstances.Add(this);

            hasNeverBeenUnpacked = physicsHold;

            SetupCommandParts();

            roboticModules = new List<PartModule>();

            StartCoroutine(SetupDockingNodesHoldState());

            if (physicsHold)
            {
                ApplyPackedTweaks();
            }

            GameEvents.onPartCouple.Add(OnPartCouple); // before docking/coupling
            GameEvents.onPartCoupleComplete.Add(OnPartCoupleComplete); // after docking/coupling

            GameEvents.onPartDeCouple.Add(OnPartDeCouple); // before coupling
            GameEvents.onPartDeCoupleComplete.Add(OnPartDeCoupleComplete); // after coupling

            GameEvents.onPartUndock.Add(OnPartUndock); // before docking
            GameEvents.onVesselsUndocking.Add(OnVesselsUndocking); // after docking

            GameEvents.onPartDestroyed.Add(OnPartDestroyed);
        }

        private void ClearEvents()
        {
            GameEvents.onPartCouple.Remove(OnPartCouple);
            GameEvents.onPartCoupleComplete.Remove(OnPartCoupleComplete);

            GameEvents.onPartDeCouple.Remove(OnPartDeCouple);
            GameEvents.onPartDeCoupleComplete.Remove(OnPartDeCoupleComplete);

            GameEvents.onPartUndock.Remove(OnPartUndock);
            GameEvents.onVesselsUndocking.Remove(OnVesselsUndocking);

            GameEvents.onPartDestroyed.Remove(OnPartDestroyed);
        }

        public void OnDestroy()
        {
            ClearEvents();
            PhysicsHoldManager.fetch?.activeInstances.Remove(this);
        }

        #endregion

        #region UPDATE

        // could use BaseField.OnValueModified instead, but a 
        // polling pattern is easier.
        public void FixedUpdate()
        {
            if (physicsHold)
            {
                if (vessel.Landed)
                {
                    if (!vessel.packed)
                    {
                        vessel.GoOnRails();
                        ApplyPackedTweaks();
                    }
                }
                else
                {
                    physicsHold = false;
                    hasNeverBeenUnpacked = false;
                }
            }
        }

        public void Update()
        {
            pawToggleHold = physicsHold;

            // physics holding is only allowed while landed, and moving at less than 1 m/s
            bool holdAllowed = vessel.Landed && vessel.srfSpeed < 1.0;
            foreach (CommandPart commandPart in commandParts)
            {
                commandPart.holdField.guiActive = holdAllowed;
                commandPart.roboticsField.guiActive = physicsHold;
            }

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

        #endregion

        #region EVENTS

        private void OnPAWToggleHold(object field)
        {
            physicsHold = pawToggleHold;
        }

        // Called when a docking/coupling action is about to happen. Gives access to old and new vessel
        // Remove PAW buttons from the command parts and disable ourselves when the vessel 
        // is about to be removed following a docking / coupling operation.
        private void OnPartCouple(GameEvents.FromToAction<Part, Part> data)
        {
            LogDebug($"OnPartCouple on {vessel.vesselName}, docked vessel : {data.from.vessel.vesselName}, dominant vessel : {data.to.vessel.vesselName}");

            // in the case of KIS-adding parts, from / to vessel are the same : we ignore the event
            if (data.from.vessel == data.to.vessel)
            {
                if (data.from.vessel == vessel && physicsHold && !ShouldPartIgnorePack(data.from))
                {
                    data.from.Pack(); // TODO : test if KIS-adding works, and that the joint is properly created after a scene reload even if the vessel has never been unpacked
                }
                return;
            }
                
            // "from" is the part on the vessel that will be removed following coupling/docking
            // when docking to a packed vessel, depending on the stock choice for which one is the dominant vessel, we have two cases :
            // A. the dominant vessel is the packed vessel -> DOCKING PORT DOESNT WORK !
            //    - non-packed parts will be transferred to the resulting (already packed) vessel, we have to pack them
            // B. the dominant vessel is the non-packed vessel :
            //    - the resulting vessel isn't packed, and physicsHold is false
            //    - transferred parts will be packed, the others won't

            // from : docking vessel that will be removed
            if (data.from.vessel == vessel)
            {
                // case B handling
                if (physicsHold)
                {
                    ForceImmediateUnpack();
                    PhysicsHold fromInstance = data.to.vessel.FindVesselModuleImplementing<PhysicsHold>();
                    fromInstance.physicsHold = true; // assuming data.from.vessel.Landed stays true after docking (probably wont), this should be enough to trigger a GoOnRails() on it
                }

                foreach (CommandPart commandPart in commandParts)
                {
                    commandPart.ClearBaseField();
                }
                commandParts.Clear();
                ClearEvents();
                PhysicsHoldManager.fetch.activeInstances.Remove(this);
                isEnabled = enabled = false;
            }

            // case A handling
            if (data.to.vessel == vessel && physicsHold)
            {
                if (hasNeverBeenUnpacked)
                {
                    SetupWheels();
                }

                // pack the docking port
                if (!ShouldPartIgnorePack(data.from))
                {
                    data.to.Pack();
                }

                // pack every part on the other vessel
                // 
                foreach (Part part in data.from.vessel.Parts)
                {
                    if (!ShouldPartIgnorePack(part))
                    {
                        part.Pack();
                    }
                }
            }
        }

        // Called after a docking/coupling action has happend. All parts now belong to the same vessel.
        private void OnPartCoupleComplete(GameEvents.FromToAction<Part, Part> data)
        {
            LogDebug($"OnPartCoupleComplete on {vessel.vesselName} from {data.from.vessel.vesselName} to {data.to.vessel.vesselName}");

            if (data.from.vessel != vessel)
                return;

            // add any command part we don't already know about
            foreach (Part part in vessel.Parts)
            {
                if (part.HasModuleImplementing<ModuleCommand>() && !commandParts.Exists(p => p.part == part))
                {
                    commandParts.Add(new CommandPart(this, part));
                }
            }
        }

        // called before a new vessel is created following intentional decoupler use or a joint failure
        // the part.vessel reference is still the old, non separated vessel
        private void OnPartDeCouple(Part part)
        {
            if (part.vessel != vessel)
                return;

            foreach (CommandPart commandPart in commandParts)
            {
                commandPart.ClearBaseField();
            }
            commandParts.Clear();
            lastDecoupledPartVessel = vessel; // see why in OnPartDeCoupleComplete

            if (physicsHold)
            {
                physicsHold = false;
                ForceImmediateUnpack();
            }
        }

        // called after a new vessel is created following intentional decoupler use or a joint failure
        // we have no way to identify the "old" vessel from which the part comes from, so we have saved
        // the vessel reference in OnPartCouple. Note that OnPartDeCouple/OnPartDeCoupleComplete are called
        // at the begining/end of Part.decouple(), so it's safe to do.
        // Also, GameEvents.onPartDeCoupleNewVesselComplete with access to both old and new vessel has been 
        // introduced in KSP 1.10 but for the sake of making this work in 1.8 - 1.9 we don't use it
        private void OnPartDeCoupleComplete(Part newVesselPart)
        {
            if (lastDecoupledPartVessel == null || lastDecoupledPartVessel != vessel)
                return;

            lastDecoupledPartVessel = null;
            SetupCommandParts();
        }

        // called before undocking, all parts still belong to the original vessel
        private void OnPartUndock(Part part)
        {
            if (part.vessel != vessel)
                return;

            if (physicsHold)
            {
                ForceImmediateUnpack();
            }
        }

        // called after a new vessel is created following undocking
        // here we do have to do the huge mess we do for uncoupling. Since we have access to the old vessel, we can
        // just remove all parts that are now on the new vessel.
        private void OnVesselsUndocking(Vessel oldVessel, Vessel newVessel)
        {
            LogDebug($"OnVesselsUndocking called on {vessel.vesselName}, oldVessel {oldVessel.vesselName}, newVessel {newVessel.vesselName}");

            if (vessel != oldVessel)
                return;

            foreach (Part part in newVessel.Parts)
            {
                int commandPartIndex = commandParts.FindIndex(p => p.part == part);
                if (commandPartIndex >= 0)
                {
                    commandParts[commandPartIndex].ClearBaseField();
                    commandParts.RemoveAt(commandPartIndex);
                }
            }

            // Force the dominant vessel to stay packed.
            // Landed has been reset by the GoOffRails() call in OnPartUndock(), but by forcing Landed, since we didn't 
            // set physicsHold to false, the next FixedUpdate() will call GoOnRails() and immediately re-pack the vessel.
            if (physicsHold)
            {
                vessel.Landed = true;
            }
        }

        private void OnPartDestroyed(Part part)
        {
            int partIndex = commandParts.FindIndex(p => p.part == part);
            if (partIndex >= 0)
            {
                commandParts[partIndex].ClearBaseField();
                commandParts.RemoveAt(partIndex);
            }
        }

        #endregion

        #region PAW UI BUTTONS

        private void OnToggleRobotics(object field)
        {
            if (roboticsOverride)
            {
                EnableRobotics();
            }
        }

        /// <summary>
        /// Add our PAW button to every command part, or to the root part if no command part is found.
        /// </summary>
        private void SetupCommandParts()
        {
            if (commandParts == null)
                commandParts = new List<CommandPart>();

            foreach (Part part in vessel.Parts)
            {
                if (part.HasModuleImplementing<ModuleCommand>())
                {
                    commandParts.Add(new CommandPart(this, part));
                }
            }

            if (commandParts.Count == 0)
            {
                commandParts.Add(new CommandPart(this, vessel.rootPart));
            }
        }

        private class CommandPart
        {
            public Part part;
            public BaseField holdField;
            public BaseField roboticsField;

            public CommandPart(PhysicsHold instance, Part part)
            {
                this.part = part;

                holdField = new BaseField(new UI_Toggle(), pawToggleHoldField, instance);
                part.Fields.Add(holdField);
                holdField.uiControlFlight = new UI_Toggle();
                holdField.guiName = "Landed physics hold";
                holdField.guiActive = part.vessel.Landed; // don't really care, this is updated in Update()
                holdField.guiActiveUnfocused = true;
                holdField.guiUnfocusedRange = 500f;
                holdField.uiControlFlight.requireFullControl = false;
                holdField.OnValueModified += instance.OnPAWToggleHold;

                roboticsField = new BaseField(new UI_Toggle(), roboticsOverrideField, instance);
                part.Fields.Add(roboticsField);
                roboticsField.uiControlFlight = new UI_Toggle();
                roboticsField.guiName = "Enable robotics";
                roboticsField.guiActive = part.vessel.Landed; // don't really care, this is updated in Update()
                roboticsField.guiActiveUnfocused = true;
                roboticsField.guiUnfocusedRange = 500f;
                roboticsField.uiControlFlight.requireFullControl = false;
                roboticsField.OnValueModified += instance.OnToggleRobotics;
            }

            public void ClearBaseField()
            {
                bool hasRemovedFields = false;
                try
                {
                    List<BaseField> fields = (List<BaseField>)typeof(BaseFieldList).GetField("_fields", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(part.Fields);
                    for (int i = fields.Count - 1; i >= 0; i--)
                    {
                        if (fields[i] == holdField || fields[i] == roboticsField)
                        {
                            fields.RemoveAt(i);
                            hasRemovedFields = true;
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Error removing basefield {holdField.name} on part {part.name}\n{e}");
                    hasRemovedFields = true;
                }
                finally
                {
                    if (hasRemovedFields && UIPartActionController.Instance != null)
                    {
                        // Ideally we should remove the UI item corresponding to the basefield, 
                        // but that isn't so easy. Destroying the PAWs is good enough given 
                        // how unfrequently this is called.
                        for (int i = UIPartActionController.Instance.windows.Count - 1; i >= 0; i--)
                        {
                            if (UIPartActionController.Instance.windows[i].part == part)
                            {
                                UIPartActionController.Instance.windows[i].gameObject.DestroyGameObject();
                                UIPartActionController.Instance.windows.RemoveAt(i);
                            }
                        }

                        for (int i = UIPartActionController.Instance.hiddenWindows.Count - 1; i >= 0; i--)
                        {
                            if (UIPartActionController.Instance.hiddenWindows[i].part == part)
                            {
                                UIPartActionController.Instance.hiddenWindows[i].gameObject.DestroyGameObject();
                                UIPartActionController.Instance.hiddenWindows.RemoveAt(i);
                            }
                        }
                    }
                }
            }
        }

        #endregion

        #region SPECIFIC HACKS

        private void ForceImmediateUnpack()
        {
            if (hasNeverBeenUnpacked)
            {
                hasNeverBeenUnpacked = false;
                SetupWheels();
            }

            framesAtStartupFieldInfo.SetValue(vessel, Time.frameCount - 100);
            vessel.GoOffRails();
        }

        /// <summary>
        /// Wheels have a wheelSetup() method being called by a coroutine launched from OnStart(). That coroutine is waiting indefinitely 
        /// for part.packed to become false, which won't happen if the vessel is in hold since the scene start. This is an issue if we want
        /// to undock the vessel, as wheels have a onVesselUndocking callback that will nullref if the setup isn't done.
        /// So, when we undock a packed vessel, if that vessel has never been unpacked, we manually call wheelSetup(), and cancel the
        /// coroutine (wheelSetup() will nullref if called twice).
        /// </summary>
        private void SetupWheels()
        {
            foreach (ModuleWheelBase wheel in vessel.FindPartModulesImplementing<ModuleWheelBase>())
            {
                // TODO : cache the fieldinfo / methodinfo
                if (!((bool)typeof(ModuleWheelBase).GetField("setup", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(wheel)))
                {
                    wheel.StopAllCoroutines();
                    typeof(ModuleWheelBase).GetMethod("wheelSetup", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(wheel, null);
                }
            }
        }

        // ModuleDockingNode FSM hacking, not needed but keeping it for reference in case we want to try enabling
        // docking a packed vessel
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

                    // we can't remove the existing stock delegate calling "otherNode = FindNodeApproaches()", so 
                    // we add our own delegate that will be called after the stock one, with our patched method.
                    dockingNode.st_ready.OnFixedUpdate += delegate { FindNodeApproachesPatched(dockingNode); };
                }
            }
            yield break;
        }

        /// <summary>
        /// Call the stock ModuleDockingNode.FindNodeApproaches(), and prevent it to ignore the vessels we are putting
        /// on physics hold by temporary setting Vessel.packed to false. Not that there is also a check on the Part.packed
        /// field, but we don't need to handle it since we don't pack parts that have a ModuleDockingNode in a ready state.
        /// </summary>
        private void FindNodeApproachesPatched(ModuleDockingNode dockingNode)
        {
            if (PhysicsHoldManager.fetch == null || PhysicsHoldManager.fetch.onHoldAndPackedInstances.Count == 0)
                return;

            foreach (PhysicsHold instance in PhysicsHoldManager.fetch.onHoldAndPackedInstances)
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
                foreach (PhysicsHold instance in PhysicsHoldManager.fetch.onHoldAndPackedInstances)
                {
                    instance.vessel.packed = true;
                }
            } 
        }

        private void ApplyPackedTweaks()
        {
            foreach (Part part in vessel.Parts)
            {
                if (ShouldPartIgnorePack(part))
                {
                    part.Unpack();
                }

                StopRoboticControllers(part);
            }
        }

        private bool ShouldPartIgnorePack(Part part)
        {
            if (vessel.GetReferenceTransformPart() == part)
            {
                return false;
            }

            foreach (PartModule module in part.Modules)
            {
                if (module is ModuleDeployablePart mdp && mdp.deployState != ModuleDeployablePart.DeployState.BROKEN && (mdp.hasPivot || mdp.panelBreakTransform != null))
                {
                    return true;
                }
                // mdn.state is persisted FSM state and is synchronized from Update(). A bit brittle, but we need
                // to be able to check it from Start() when docking node modules won't have initialized their FSM yet.
                else if (module is ModuleDockingNode mdn && mdn.state == "Ready") 
                {
                    return true;
                }
            }
            return false;
        }

        private void StopRoboticControllers(Part part)
        {
            foreach (PartModule module in part.Modules)
            {
                if (module is ModuleRoboticController mrc)
                {
                    mrc.SequenceStop();
                }
            }
        }

        private void EnableRobotics()
        {
            List<Part> roboticParts = new List<Part>();
            UnpackRoboticChilds(roboticParts, vessel.rootPart, false);

            foreach (Part part in roboticParts)
            {
                part.Unpack();
            }
        }

        private void UnpackRoboticChilds(List<Part> roboticParts, Part part, bool parentIsRobotic)
        {
            if (!parentIsRobotic && part.isRobotic())
            {
                parentIsRobotic = true;
            }

            if (parentIsRobotic)
            {
                if (part.vessel.rootPart == part)
                {
                    roboticParts.Clear();
                    ScreenMessages.PostScreenMessage($"Can't enable robotics, the vessel control part\n{part.partInfo.title}\nis a child of a robotic part");
                    return;
                }

                roboticParts.Add(part);
            }

            foreach (Part child in part.children)
            {
                UnpackRoboticChilds(roboticParts, child, parentIsRobotic);
            }
        }

        #endregion

        #region UTILS

        [Conditional("DEBUG")]
        private static void LogDebug(string message)
        {
            Debug.Log(message);
        }

        #endregion
    }

    public class ModulePackedDebugTest : PartModule
    {
        [UI_Label(scene = UI_Scene.Flight, requireFullControl = false)]
        [KSPField(guiActive = true, guiActiveUnfocused = true, unfocusedRange = 500f, groupName = "debug", groupDisplayName = "DEBUG", groupStartCollapsed = false)]
        bool partPacked;

        [UI_Label(scene = UI_Scene.Flight, requireFullControl = false)]
        [KSPField(guiActive = true, guiActiveUnfocused = true, unfocusedRange = 500f, groupName = "debug", groupDisplayName = "DEBUG", groupStartCollapsed = false)]
        bool vesselPacked;

        [UI_Label(scene = UI_Scene.Flight, requireFullControl = false)]
        [KSPField(guiActive = true, guiActiveUnfocused = true, unfocusedRange = 500f, groupName = "debug", groupDisplayName = "DEBUG", groupStartCollapsed = false)]
        bool vesselLanded;

        [UI_Label(scene = UI_Scene.Flight, requireFullControl = false)]
        [KSPField(guiActive = true, guiActiveUnfocused = true, unfocusedRange = 500f, groupName = "debug", groupDisplayName = "DEBUG", groupStartCollapsed = false)]
        bool physicsHold;

        [UI_Label(scene = UI_Scene.Flight, requireFullControl = false)]
        [KSPField(guiActive = true, guiActiveUnfocused = true, unfocusedRange = 500f, groupName = "debug", groupDisplayName = "DEBUG", groupStartCollapsed = false)]
        string orbitDriver = string.Empty;

        [UI_Label(scene = UI_Scene.Flight, requireFullControl = false)]
        [KSPField(guiActive = true, guiActiveUnfocused = true, unfocusedRange = 500f, groupName = "debug", groupDisplayName = "DEBUG", groupStartCollapsed = false)]
        string dockFSMState = string.Empty;

        [UI_Label(scene = UI_Scene.Flight, requireFullControl = false)]
        [KSPField(guiActive = true, guiActiveUnfocused = true, unfocusedRange = 500f, groupName = "debug", groupDisplayName = "DEBUG", groupStartCollapsed = false)]
        string dockOtherNode = string.Empty;

        private ModuleDockingNode dockingNode;
        bool hasDockingNode;

        public void Start()
        {
            dockingNode = part.FindModuleImplementing<ModuleDockingNode>();
            hasDockingNode = dockingNode != null;

            Fields["dockFSMState"].guiActive = Fields["dockFSMState"].guiActiveUnfocused = hasDockingNode;
            Fields["dockOtherNode"].guiActive = Fields["dockOtherNode"].guiActiveUnfocused = hasDockingNode;
        }

        public void Update()
        {
            if (!HighLogic.LoadedSceneIsFlight)
            {
                return;
            }

            partPacked = part.packed;
            vesselPacked = vessel.packed;
            vesselLanded = vessel.Landed;
            
            physicsHold = vessel.FindVesselModuleImplementing<PhysicsHold>().physicsHold;

            if (vessel.orbitDriver != null)
            {
                orbitDriver = vessel.orbitDriver.updateMode.ToString();
            }

            if (hasDockingNode)
            {
                dockFSMState = dockingNode.state;
                if (dockingNode.otherNode != null)
                {
                    dockOtherNode = "found on " + dockingNode.otherNode.part.vessel.vesselName;
                }
                else
                {
                    dockOtherNode = "none";
                }
            }
        }
    }
}
