/*
This add a "Landed physics hold" PAW button on all command parts (and root part if no command part found), 
available when the vessel is landed and has a surface speed less than 1 m/s (arbitrary, could more/less).
When enabled, all rigibodies on the vessel are made kinematic by forcing the stock "packed" (or "on rails")
state normally used during "physics easing" and non-physics timewarp.

When enabled, all joint/force/torque physics are disabled, making the vessel an unmovable object fixed at
a given altitude/longitude/latitude. You can still collide with it, but it will not react to collisions.

Working and tested :
  - Docking : docking to a on hold vessel will seamlessly put the merged vessel on hold
  - Undocking : if the initial vessel is on hold, both resulting vessels will be on hold after undocking
  - Grabbing/ungrabbing (Klaw) will ahve the same behavior as docking/undocking
  - Decoupling : will insta-restore physics. Note that editor-docked docking ports are considered as decoupler
  - Collisions will destroy on-hold parts if crash tolerance is exceeded (and trigger decoupling events)
  - Breakable parts : solar panels, radiators and antennas will stay non-kinematic and can break.
  - EVAing / boarding (hack in place to enable the kerbal portraits UI)
  - Control input (rotation, translation, throttle...) is forced to zero by the stock vessel.packed check
  - KIS attaching parts work as expected

Not working / Known issues :
  - Vessels using multi-node docking sometimes throw errors on undocking or when using the "make primary node"
    button. Not sure exactly what is going on, but the errors don't seem to cause major issues and messing
    around with the "make primary node" or in last resort reloading the scene seems to fix it.
  - Stock robotic parts won't be able to move.
  - The stock "EVA ladder drift compensation" is disabled when the ladder is on a on-hold vessel
  - KAS errors out as soon as a KAS connection exists on a on-hold vessel, resulting in vessels being 
    immediately deleted. It probably can work at least partially since it is able to handle things in 
    timewarp, but that would likely require quite a bit of extra handling on its side.
   
Untested / likely to have issues :
  - USI Konstruction things are reported to work, but I'm a bit skeptical and haven't done any test.
  - Infernal Robotics : untested, will probably have issues
*/

namespace PhysicsHold
{
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
        string isKinematic = "no RigidBody";

        [UI_Label(scene = UI_Scene.Flight, requireFullControl = false)]
        [KSPField(guiActive = true, guiActiveUnfocused = true, unfocusedRange = 500f, groupName = "debug", groupDisplayName = "DEBUG", groupStartCollapsed = false)]
        bool physicsHold;

        [UI_Label(scene = UI_Scene.Flight, requireFullControl = false)]
        [KSPField(guiActive = true, guiActiveUnfocused = true, unfocusedRange = 500f, groupName = "debug", groupDisplayName = "DEBUG", groupStartCollapsed = false)]
        string orbitDriver = string.Empty;

        [UI_Label(scene = UI_Scene.Flight, requireFullControl = false)]
        [KSPField(guiActive = true, guiActiveUnfocused = true, unfocusedRange = 500f, groupName = "debug", groupDisplayName = "DEBUG", groupStartCollapsed = false)]
        string vSituation = string.Empty;

        [UI_Label(scene = UI_Scene.Flight, requireFullControl = false)]
        [KSPField(guiActive = true, guiActiveUnfocused = true, unfocusedRange = 500f, groupName = "debug", groupDisplayName = "DEBUG", groupStartCollapsed = false)]
        string vHeightFromTerrain = string.Empty;

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
            vSituation = vessel.situation.ToString();
            vHeightFromTerrain = vessel.heightFromTerrain.ToString("F1");

            physicsHold = vessel.GetComponent<VesselPhysicsHold>().physicsHold;

            if (part.rb != null)
            {
                isKinematic = part.rb.isKinematic.ToString();
            }

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
