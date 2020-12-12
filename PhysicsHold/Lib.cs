using KSP.UI.Screens;
using KSP.UI.TooltipTypes;
using System;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;

namespace PhysicsHold
{
    public static class Lib
    {
        [Conditional("DEBUG")]
        public static void LogDebug(string message)
        {
            UnityEngine.Debug.Log($"[PhysicsHold] {message}");
        }

        private static Tooltip_Text tooltipPrefab;

        public static void AddTooltipToObject(GameObject obj, string text)
        {
            if (tooltipPrefab == null)
            {
                tooltipPrefab = AssetBase.GetPrefab<Tooltip_Text>("Tooltip_Text");
            }

            TooltipController_Text tooltip = obj.GetComponent<TooltipController_Text>();
            if (tooltip == null)
                tooltip = obj.AddComponent<TooltipController_Text>();

            tooltip.prefab = tooltipPrefab;
            tooltip.SetText(text);
        }

        private static bool vesselTypeTexturesAcquired = false;
        private static Sprite aircraftTex;
        private static Sprite baseTex;
        private static Sprite commsRelayText;
        private static Sprite debrisTex;
        private static Sprite deployScienceTex;
        private static Sprite landerTex;
        private static Sprite probeTex;
        private static Sprite roverTex;
        private static Sprite shipTex;
        private static Sprite spaceObjTex;
        private static Sprite stationTex;

        public static Sprite GetVesselTypeIcon(Vessel vessel)
        {
            if (!vesselTypeTexturesAcquired)
                GetVesselTypeIconReferences();

            switch (vessel.vesselType)
            {
                case VesselType.Debris:
                    return debrisTex;
                case VesselType.SpaceObject:
                    return spaceObjTex;
                case VesselType.Probe:
                    return probeTex;
                case VesselType.Relay:
                    return commsRelayText;
                case VesselType.Rover:
                    return roverTex;
                case VesselType.Lander:
                    return landerTex;
                case VesselType.Ship:
                    return shipTex;
                case VesselType.Plane:
                    return aircraftTex;
                case VesselType.Station:
                    return stationTex;
                case VesselType.Base:
                    return baseTex;
                case VesselType.DeployedScienceController:
                case VesselType.DeployedSciencePart:
                    return deployScienceTex;
                default:
                    return shipTex;
            }
        }


        private static void GetVesselTypeIconReferences()
        {
            if (vesselTypeTexturesAcquired)
                return;

            try
            {
                vesselTypeTexturesAcquired = true;

                VesselRenameDialog prefab = AssetBase.GetPrefab("VesselRenameDialog").GetComponent<VesselRenameDialog>();
                Type rdType = typeof(VesselRenameDialog);
                BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;

                aircraftTex = ((TypeButton)rdType.GetField("toggleAircraft", flags).GetValue(prefab)).icon.sprite;
                baseTex = ((TypeButton)rdType.GetField("toggleBase", flags).GetValue(prefab)).icon.sprite;
                commsRelayText = ((TypeButton)rdType.GetField("toggleCommunicationsRelay", flags).GetValue(prefab)).icon.sprite;
                debrisTex = ((TypeButton)rdType.GetField("toggleDebris", flags).GetValue(prefab)).icon.sprite;
                deployScienceTex = ((TypeButton)rdType.GetField("toggleDeployedScience", flags).GetValue(prefab)).icon.sprite;
                landerTex = ((TypeButton)rdType.GetField("toggleLander", flags).GetValue(prefab)).icon.sprite;
                probeTex = ((TypeButton)rdType.GetField("toggleProbe", flags).GetValue(prefab)).icon.sprite;
                roverTex = ((TypeButton)rdType.GetField("toggleRover", flags).GetValue(prefab)).icon.sprite;
                shipTex = ((TypeButton)rdType.GetField("toggleShip", flags).GetValue(prefab)).icon.sprite;
                spaceObjTex = ((TypeButton)rdType.GetField("toggleSpaceObj", flags).GetValue(prefab)).icon.sprite;
                stationTex = ((TypeButton)rdType.GetField("toggleStation", flags).GetValue(prefab)).icon.sprite;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"Could not acquire vessel icons !\n{e}");
                vesselTypeTexturesAcquired = false;
            }
        }

        private static Texture2D launcherTexture;
        public static Texture2D LauncherTexture 
        { 
            get
            {
                if (launcherTexture == null)
                {
                    launcherTexture = GameDatabase.Instance.GetTexture("PhysicsHold/icons8-strength-64", false);
                }
                return launcherTexture;
            }
        }

        private static Texture2D deformTexture;
        public static Texture2D DeformTexture
        {
            get
            {
                if (deformTexture == null)
                {
                    deformTexture = GameDatabase.Instance.GetTexture("PhysicsHold/icons8-merge-documents-64", false);
                }
                return deformTexture;
            }
        }

        private static Sprite closeSprite;
        public static Sprite CloseSprite
        {
            get
            {
                if (closeSprite == null)
                {
                    Texture2D closeTexture = GameDatabase.Instance.GetTexture("PhysicsHold/icons8-close-window-32", false);
                    closeSprite = Sprite.Create(closeTexture, new Rect(0f, 0f, 32f, 32f), new Vector2(0f, 0f), 1f);
                }
                return closeSprite;
            }
        }
    }
}
