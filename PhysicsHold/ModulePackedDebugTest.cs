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
