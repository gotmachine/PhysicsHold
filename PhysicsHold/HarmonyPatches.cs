using HarmonyLib;

namespace PhysicsHold
{
    [HarmonyPatch(typeof(ModuleWheelBase), "onDockingComplete")]
    static class ModuleWheelBase_onDockingComplete
    {
        static bool Prefix(ModuleWheelBase __instance, GameEvents.FromToAction<Part, Part> FromTo)
        {
            if (FromTo.from.vessel == __instance.vessel || FromTo.to.vessel == __instance.vessel)
            {
                VesselPhysicsHold physicsHold = __instance.vessel.GetComponent<VesselPhysicsHold>();
                if (physicsHold != null && (physicsHold.physicsHold || physicsHold.delayedPhysicsHoldEnableRequest))
                {
                    return false;
                }
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ModuleWheelBase), "onVesselUndocking")]
    static class ModuleWheelBase_onVesselUndocking
    {
        static bool Prefix(ModuleWheelBase __instance, Vessel fromVessel, Vessel toVessel)
        {
            if (fromVessel == __instance.vessel || toVessel == __instance.vessel)
            {
                VesselPhysicsHold toVesselPhysicsHold = fromVessel.GetComponent<VesselPhysicsHold>();
                if (toVesselPhysicsHold != null && (toVesselPhysicsHold.physicsHold || toVesselPhysicsHold.delayedPhysicsHoldEnableRequest))
                {
                    return false;
                }

                VesselPhysicsHold fromVesselPhysicsHold = toVessel.GetComponent<VesselPhysicsHold>();
                if (fromVesselPhysicsHold != null && (fromVesselPhysicsHold.physicsHold || fromVesselPhysicsHold.delayedPhysicsHoldEnableRequest))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
