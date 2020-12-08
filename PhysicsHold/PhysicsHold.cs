using Expansions.Serenity;
using KSP.UI.Screens.Flight;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using KSP.UI.Screens;
using Debug = UnityEngine.Debug;
using UnityEngine.UI;
using System.Text;
using TMPro;
using KSP.UI.TooltipTypes;

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
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class PhysicsHoldManager : MonoBehaviour
    {
        private static StringBuilder sb = new StringBuilder();

        public static PhysicsHoldManager Instance { get; private set; }

        private List<PhysicsHold> physicsHoldInstances;

        private ApplicationLauncherButton launcher;
        private static Texture2D launcherTexture;

        private static bool texturesAcquired = false;
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

        private static Tooltip_Text tooltipPrefab;

        private PopupDialog currentDialog;
        private static Vector2 lastPopupPos = Vector2.zero;

        public static void AddInstance(PhysicsHold instance)
        {
            if (Instance == null)
                return;

            if (Instance.physicsHoldInstances.Contains(instance))
                return;

            Instance.physicsHoldInstances.Add(instance);
            if (Instance.currentDialog != null)
            {
                Instance.DismissDialog();
                Instance.ToggleDialog();
            }
        }

        public static void RemoveInstance(PhysicsHold instance)
        {
            if (Instance == null)
                return;

            if (Instance.physicsHoldInstances.Remove(instance) && Instance.currentDialog != null)
            {
                Instance.DismissDialog();
                Instance.ToggleDialog();
            }
        }

        private void Awake()
        {
            GetVesselTypeIconReferences();

            if (tooltipPrefab == null)
            {
                tooltipPrefab = AssetBase.GetPrefab<Tooltip_Text>("Tooltip_Text");
            }

            if (Instance != null)
                Destroy(Instance);

            Instance = this;
            physicsHoldInstances = new List<PhysicsHold>();

            if (launcherTexture == null)
            {
                launcherTexture = GameDatabase.Instance.GetTexture("PhysicsHold/icons8-strength-64", false);
            }
        }

        private void Start()
        {
            if (lastPopupPos == Vector2.zero)
            {
                lastPopupPos = new Vector2(0.1f, 0.9f);
            }

            launcher = ApplicationLauncher.Instance.AddModApplication(null, null, null, null, null, null, ApplicationLauncher.AppScenes.FLIGHT, launcherTexture);
            //launcher.VisibleInScenes = ApplicationLauncher.AppScenes.FLIGHT;
            launcher.onLeftClick += ToggleDialog;

            AddTooltipToObject(launcher.gameObject, "Physics hold");
        }

        private void OnDestroy()
        {
            if (launcher != null)
            {
                launcher.onLeftClick -= ToggleDialog;
                ApplicationLauncher.Instance.RemoveModApplication(launcher);
                launcher = null;
            }

            DismissDialog();
        }

        private bool DismissDialog()
        {
            if (currentDialog != null)
            {
                float xPos = (currentDialog.RTrf.position.x / (Screen.width)) + 0.5f;
                float yPos = (currentDialog.RTrf.position.y / (Screen.height)) + 0.5f;

                lastPopupPos = new Vector2(xPos, yPos);
                currentDialog.Dismiss();
                currentDialog = null;
                return true;
            }

            return false;
        }

        private void ToggleDialog()
        {
            if (DismissDialog())
                return;

            List<Vessel> itemVessels = new List<Vessel>();

            List<DialogGUILabel> vesselTitles = new List<DialogGUILabel>();

            List<DialogGUIButton> deformationButtons = new List<DialogGUIButton>();

            List<DialogGUIButton> switchToButtons = new List<DialogGUIButton>();

            List<DialogGUIBase> dialog = new List<DialogGUIBase>();

            List<DialogGUIBox> vesselItems = new List<DialogGUIBox>();

            for (int i = 0; i < physicsHoldInstances.Count; i++)
            {
                PhysicsHold instance = physicsHoldInstances[i];

                DialogGUILabel vesselTitle = new DialogGUILabel(() => GetVesselName(instance), 270f);
                vesselTitles.Add(vesselTitle);

                DialogGUIToggle holdToggle = new DialogGUIToggle(() => instance.physicsHold, "Physics hold", instance.OnToggleHold, -1f, 24f);
                holdToggle.OptionInteractableCondition += instance.CanTogglePhysicsHold;

                DialogGUIButton deformationButton = new DialogGUIButton("Make physics deformation permanent", () => instance.OnApplyDeformation(), false);
                deformationButton.OptionInteractableCondition += instance.CanApplyDeformation;
                deformationButton.size = new Vector2(270f, 22f);
                deformationButtons.Add(deformationButton);

                DialogGUIVerticalLayout vesselInfo = new DialogGUIVerticalLayout(
                    vesselTitle,
                    new DialogGUILabel(() => GetVesselState(instance)));
                vesselInfo.padding = new RectOffset(5, 0, 0, 0);

                DialogGUIButton switchToButton = new DialogGUIButton(string.Empty, () => FlightGlobals.SetActiveVessel(instance.Vessel), 28f, 28f, false);
                switchToButton.OptionInteractableCondition += () => instance.Vessel.loaded && !instance.Vessel.isActiveVessel;
                switchToButtons.Add(switchToButton);

                DialogGUIHorizontalLayout boxTopSection = new DialogGUIHorizontalLayout(
                    switchToButton,
                    vesselInfo);
                boxTopSection.anchor = TextAnchor.MiddleLeft;

                DialogGUIVerticalLayout boxContent = new DialogGUIVerticalLayout(
                    boxTopSection,
                    holdToggle,
                    deformationButton);

                boxContent.padding = new RectOffset(5, 5, 5, 0);

                DialogGUIBox vesselItem = new DialogGUIBox("", 280f, 105f, null, boxContent);

                if (instance.Vessel.isActiveVessel)
                {
                    vesselItems.Insert(0, vesselItem);
                    itemVessels.Insert(0, instance.Vessel);
                }
                else
                {
                    vesselItems.Add(vesselItem);
                    itemVessels.Add(instance.Vessel);
                }
            }

            DialogGUIBase[] scrollList = new DialogGUIBase[vesselItems.Count + 1];

            scrollList[0] = new DialogGUIContentSizer(ContentSizeFitter.FitMode.Unconstrained, ContentSizeFitter.FitMode.PreferredSize, true);

            for (int i = 0; i < vesselItems.Count; i++)
                scrollList[i + 1] = vesselItems[i];

            DialogGUIScrollList scrollListItem = new DialogGUIScrollList(Vector2.one, false, true,
                new DialogGUIVerticalLayout(10, 100, 4, new RectOffset(6, 24, 10, 10), TextAnchor.UpperLeft, scrollList));

            dialog.Add(scrollListItem);
            dialog.Add(new DialogGUIButton("Close", () => DismissDialog()));

            Rect posAndSize = new Rect(lastPopupPos.x, lastPopupPos.y, 320f, 420f);

            currentDialog = PopupDialog.SpawnPopupDialog(new Vector2(0f, 1f), new Vector2(0f, 1f),
                new MultiOptionDialog("", "Keep physics in check for landed vessels !", "Physics Hold", UISkinManager.defaultSkin, posAndSize, dialog.ToArray()), true, UISkinManager.defaultSkin, false);

            TextMeshProUGUI subText = currentDialog.gameObject.GetChild("UITextPrefab(Clone)")?.GetComponent<TextMeshProUGUI>();
            if (subText != null)
            {
                subText.alignment = TextAlignmentOptions.Center;
                subText.color = Color.white;
                subText.fontStyle = FontStyles.Bold;
            }


            ScrollRect scrollRect = scrollListItem.uiItem.GetComponentInChildren<ScrollRect>();
            scrollRect.content.pivot = new Vector2(0f, 1f);

            foreach (DialogGUILabel vesselTitle in vesselTitles)
            {
                vesselTitle.text.color = Color.white;
                vesselTitle.text.fontStyle = FontStyles.Bold;
            }

            foreach (DialogGUIButton deformationButton in deformationButtons)
            {
                TextMeshProUGUI text = deformationButton.uiItem.GetChild("Text").GetComponent<TextMeshProUGUI>();
                text.fontSizeMin = 12f;
                text.fontSizeMax = 12f;
                text.fontSize = 12f;

                AddTooltipToObject(deformationButton.uiItem,
                    "Move parts position according to the current \ndeformation due to gravity and external forces.\n" +
                    "Reduce joint stress and prevent kraken attacks \nwhen landed on an uneven surface.\n\n" +
                    "WARNING :\nWill permanently deform the vessel on every use !");
            }

            ColorBlock buttonColors = new ColorBlock()
            {
                colorMultiplier = 1f,
                normalColor = Color.white,
                selectedColor = Color.white,
                highlightedColor = new Color(0.9f, 1f, 1f, 1f),
                pressedColor = new Color(0.8f, 1f, 1f, 1f),
                disabledColor = new Color(1f, 1f, 1f, 0.8f)
            };

            for (int i = 0; i < switchToButtons.Count; i++)
            {
                DialogGUIButton switchToButton = switchToButtons[i];
                Vessel vessel = itemVessels[i];

                AddTooltipToObject(switchToButton.uiItem, "Make active");

                Image imgCpnt = switchToButton.uiItem.GetComponent<Image>();
                imgCpnt.type = Image.Type.Simple;
                imgCpnt.preserveAspect = true;
                imgCpnt.color = Color.white;
                imgCpnt.sprite = GetVesselTypeIcon(vessel);

                Button btnCpnt = switchToButton.uiItem.GetComponent<Button>();
                btnCpnt.transition = Selectable.Transition.ColorTint;
                btnCpnt.navigation = new Navigation() { mode = Navigation.Mode.None };
                btnCpnt.colors = buttonColors;
                btnCpnt.image = imgCpnt;
            }
        }

        private string GetVesselName(PhysicsHold instance)
        {
            sb.Clear();
            int length = instance.Vessel.vesselName.Length;
            if (length > 40)
            {
                sb.Append(instance.Vessel.vesselName.Substring(0, 30));
                sb.Append("...");
                sb.Append(instance.Vessel.vesselName.Substring(length - 10, 10));
            }
            else
            {
                sb.Append(instance.Vessel.vesselName);
            }

            return sb.ToString();
        }

        private string GetVesselState(PhysicsHold instance)
        {
            sb.Clear();

            if (instance.Vessel.isActiveVessel)
            {
                sb.Append("ACTIVE VESSEL - ");
            }

            if (instance.Vessel.Landed)
            {
                sb.Append("landed");
            }
            else
            {
                sb.Append("not landed");
            }

            if (instance.physicsHold)
            {
                sb.Append(", physics on hold");
            }
            else if (instance.Vessel.packed)
            {
                sb.Append(", packed");
            }
            else
            {
                sb.Append(", in physics");
            }

            return sb.ToString();
        }


        private Sprite GetVesselTypeIcon(Vessel vessel)
        {
            if (!texturesAcquired)
                return null;

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

        private static void AddTooltipToObject(GameObject obj, string text)
        {
            TooltipController_Text tooltip = obj.GetComponent<TooltipController_Text>();
            if (tooltip == null)
                tooltip = obj.AddComponent<TooltipController_Text>();

            tooltip.prefab = tooltipPrefab;
            tooltip.SetText(text);
        }

        private static void GetVesselTypeIconReferences()
        {
            if (texturesAcquired)
                return;

            try
            {
                texturesAcquired = true;

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
                Debug.LogError($"Could not acquire vessel icons !\n{e}");
                texturesAcquired = false;
            }
        }


    }


    public class PhysicsHold : VesselModule
    {
        private static FieldInfo framesAtStartupFieldInfo;

        private static bool staticInitDone = false;

        [KSPField(isPersistant = true)] public bool physicsHold;

        [KSPField(isPersistant = true)] public bool roboticsOverride;

        private bool isEnabled = true;

        private Vessel lastDecoupledVessel;
        private bool hasNeverBeenUnpacked;
        private bool delayedPhysicsHoldEnableRequest;
        private bool isChangingState;

        private string landedAt;
        private string landedAtLast;
        private string displaylandedAt;


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

            LogDebug($"Starting for {vessel.vesselName}, physicsHold={physicsHold}");

            PhysicsHoldManager.AddInstance(this);

            lastDecoupledVessel = null;
            // vesselPrecalulateLastEasing = null;
            delayedPhysicsHoldEnableRequest = false;
            isChangingState = false;
            landedAt = vessel.landedAt;
            landedAtLast = vessel.landedAtLast;
            displaylandedAt = vessel.displaylandedAt;

            if (physicsHold)
            {
                StartCoroutine(AddCallbacksDelayed());
            }

            hasNeverBeenUnpacked = physicsHold;

            GameEvents.onPartCouple.Add(OnPartCouple); // before docking/coupling
            GameEvents.onPartCoupleComplete.Add(OnPartCoupleComplete); // after docking/coupling

            GameEvents.onPartDeCouple.Add(OnPartDeCouple); // before coupling
            GameEvents.onPartDeCoupleComplete.Add(OnPartDeCoupleComplete); // after coupling

            GameEvents.onPartUndock.Add(OnPartUndock); // before docking
            GameEvents.onVesselsUndocking.Add(OnVesselsUndocking); // after docking
        }

        public override void OnUnloadVessel()
        {
            PhysicsHoldManager.RemoveInstance(this);

            ClearEvents();
            RemoveTimingManagerCallbacks();
        }

        private void ClearEvents()
        {
            GameEvents.onPartCouple.Remove(OnPartCouple);
            GameEvents.onPartCoupleComplete.Remove(OnPartCoupleComplete);

            GameEvents.onPartDeCouple.Remove(OnPartDeCouple);
            GameEvents.onPartDeCoupleComplete.Remove(OnPartDeCoupleComplete);

            GameEvents.onPartUndock.Remove(OnPartUndock);
            GameEvents.onVesselsUndocking.Remove(OnVesselsUndocking);
        }

        public void OnDestroy()
        {
            if (HighLogic.LoadedScene == GameScenes.LOADING)
                return;

            ClearEvents();
            RemoveTimingManagerCallbacks();
        }

        #endregion

        #region UPDATE

        private void AddTimingManagerCallbacks()
        {
            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.Earlyish, PackedUnset);
            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.FashionablyLate, PackedSet);

            TimingManager.UpdateAdd(TimingManager.TimingStage.Earlyish, PackedUnset);
            TimingManager.UpdateAdd(TimingManager.TimingStage.FashionablyLate, PackedSet);

            TimingManager.LateUpdateAdd(TimingManager.TimingStage.Earlyish, PackedUnset);
            TimingManager.LateUpdateAdd(TimingManager.TimingStage.FashionablyLate, PackedSet);
        }

        private void RemoveTimingManagerCallbacks()
        {
            isChangingState = false;

            if (vessel.precalc != null)
                typeof(VesselPrecalculate).GetField("easing", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(vessel.precalc, false);

            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.Earlyish, PackedUnset);
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.FashionablyLate, PackedSet);

            TimingManager.UpdateRemove(TimingManager.TimingStage.Earlyish, PackedUnset);
            TimingManager.UpdateRemove(TimingManager.TimingStage.FashionablyLate, PackedSet);

            TimingManager.LateUpdateRemove(TimingManager.TimingStage.Earlyish, PackedUnset);
            TimingManager.LateUpdateRemove(TimingManager.TimingStage.FashionablyLate, PackedSet);
        }

        private void PackedUnset()
        {
            if (physicsHold)
            {
                // Vessel.checkLanded() is called from various places, notably ModuleWheelBase.FixedUpdate()
                // Normally, when called while vessel.packed is true, it will skip the actual checking code and just
                // return the current Vessel.Landed value. But if packed is false, it will execute some code only
                // meant to be called while unpacked, and it will not only always return false, but also set Vessel.Landed
                // to false, which will break everything. The exact condition of Vessel.checkLanded() is :
                // "if (loaded && !packed && !precalc.isEasingGravity)"
                // precalc.isEasingGravity is a property with a "easing" private backing field. In stock, the property is
                // only used in Vessel.checkLanded(), and VesselPrecalulate read/write the backing field from its FixedUpdate.
                // So the workaround here is to set "VesselPrecalulate.easing" to true during the "Earlyish to FashionablyLate"
                // execution window to make sure Vessel.checkLanded() always work as expected. VesselPrecalulate has an
                // execution order of -102, before Earlyish, so we won't trigger side effects.
                // This is still quite brittle, but outside of harmony-patching Vessel.checkLanded() we haven't got many options...
                // vesselPrecalulateLastEasing = vessel.precalc.isEasingGravity;



                // we can't set the property because it will trigger the vessel easing code in its setter, so we set the backing field
                //typeof(VesselPrecalculate).GetField("easing", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(vessel.precalc, true);


                vessel.Landed = true;
                vessel.landedAt = landedAt;
                vessel.landedAtLast = landedAtLast;
                vessel.displaylandedAt = displaylandedAt;

                if (TimeWarp.WarpMode == TimeWarp.Modes.HIGH && TimeWarp.CurrentRate > TimeWarp.MaxPhysicsRate)
                    return;

                vessel.packed = false;
                foreach (Part part in vessel.parts)
                {
                    part.packed = false;
                }
            }
            else
            {
                // vesselPrecalulateLastEasing = null;
            }
        }

        private void PackedSet()
        {
            if (physicsHold)
            {
                //if (vesselPrecalulateLastEasing != null)
                //{
                //    typeof(VesselPrecalculate).GetField("easing", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(vessel.precalc, (bool)vesselPrecalulateLastEasing);
                //}

                vessel.Landed = true;
                vessel.landedAt = landedAt;
                vessel.landedAtLast = landedAtLast;
                vessel.displaylandedAt = displaylandedAt;

                vessel.packed = true;
                foreach (Part part in vessel.parts)
                {
                    part.packed = true;
                }
            }

            // vesselPrecalulateLastEasing = null;
        }

        private IEnumerator AddCallbacksDelayed()
        {
            LogDebug($"Starting on hold : adding TimingManager callbacks for {vessel.vesselName}, landed={vessel.Landed}, packed={vessel.packed}");
            framesAtStartupFieldInfo.SetValue(vessel, Time.frameCount);

            isChangingState = true;

            // When Start() is called, VesselPrecalculate/OrbitDriver will already have started run their FixedUpdate()
            // while the packed flags were true. But FlightIntegrator/Krakensbane/FloatingOrigin haven't been started yet.
            // If we unset the packed flags right away, they will assume the vessel isn't packed and use the in-physics
            // position calcs. So we delay our un-setting of the packed flags. To be one the safe side, we make sure we skip
            // a few update/fixedUpdate (there is no rush, we are preventing the vessel from being unpacked by resetting
            // "framesAtStartup" every update).
            for (int i = 0; i < 5; i++)
            {
                yield return new WaitForFixedUpdate(); // this is probably unnecessary, but just in case
                yield return new WaitForEndOfFrame();
                LogDebug($"Starting on hold : skipping Frame for {vessel.vesselName}, landed={vessel.Landed}, packed={vessel.packed}");
            }

            LogDebug($"Starting on hold : removing packed flags for {vessel.vesselName}, landed={vessel.Landed}, packed={vessel.packed}");

            AddTimingManagerCallbacks();


            isChangingState = false;
        }

        private void EnablePhysicsHold()
        {
            if (physicsHold || !vessel.Landed)
                return;

            LogDebug($"Enabling physics hold for {vessel.vesselName}");

            physicsHold = true;

            landedAt = vessel.landedAt;
            landedAtLast = vessel.landedAtLast;
            displaylandedAt = vessel.displaylandedAt;

            vessel.GoOnRails();

            StartCoroutine(AddCallbacksDelayed());
        }

        private void DisablePhysicsHold(bool immediate)
        {
            LogDebug($"Disabling physics hold ({vessel.vesselName})");

            RemoveTimingManagerCallbacks();

            physicsHold = false;

            vessel.Landed = true;
            vessel.landedAt = landedAt;
            vessel.landedAtLast = landedAtLast;
            vessel.displaylandedAt = displaylandedAt;

            vessel.packed = true;
            foreach (Part part in vessel.Parts)
            {
                part.packed = true;
            }

            // THIS WORKS, BUT TRYING SOMETHING ELSE
            //if (hasNeverBeenUnpacked)
            //{
            //    hasNeverBeenUnpacked = false;
            //    SetupWheels();
            //    immediate = true;
            //}

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

        public void FixedUpdate()
        {
            if (delayedPhysicsHoldEnableRequest)
            {
                delayedPhysicsHoldEnableRequest = false;
                vessel.Landed = true;
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

            // can't toggle if not landed, or if surface speed is higher than 0.1 m/s
            if (!vessel.Landed || vessel.srfSpeed > 0.1)
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


        #endregion

        #region EVENTS


        // Called when a docking/coupling action is about to happen. Gives access to old and new vessel
        // Remove PAW buttons from the command parts and disable ourselves when the vessel 
        // is about to be removed following a docking / coupling operation.
        private void OnPartCouple(GameEvents.FromToAction<Part, Part> data)
        {
            LogDebug($"OnPartCouple on {vessel.vesselName}, docked vessel : {data.from.vessel.vesselName}, dominant vessel : {data.to.vessel.vesselName}");

            // in the case of KIS-adding parts, from / to vessel are the same : 
            // we ignore the event and just pack the part.
            if (data.from.vessel == data.to.vessel && data.from.vessel == vessel && physicsHold)
            {
                data.from.Pack(); // TODO : test if KIS-adding works, and that the joint is properly created after a scene reload even if the vessel has never been unpacked
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
                    PhysicsHold fromInstance = data.to.vessel.GetComponent<PhysicsHold>();
                    fromInstance.delayedPhysicsHoldEnableRequest = true;
                }

                physicsHold = false;
                ClearEvents();
                RemoveTimingManagerCallbacks();
                isEnabled = false;
            }

            // case A handling
            if (data.to.vessel == vessel && physicsHold)
            {
                SetupWheels();

                foreach (Part part in data.from.vessel.Parts)
                {
                    part.Pack();
                }
            }
        }

        // Called after a docking/coupling action has happend. All parts now belong to the same vessel.
        private void OnPartCoupleComplete(GameEvents.FromToAction<Part, Part> data)
        {
            LogDebug($"OnPartCoupleComplete on {vessel.vesselName} from {data.from.vessel.vesselName} to {data.to.vessel.vesselName}");

            if (data.from.vessel != vessel)
                return;
        }



        // called before a new vessel is created following intentional decoupler use or a joint failure
        // the part.vessel reference is still the old, non separated vessel
        private void OnPartDeCouple(Part part)
        {
            if (part.vessel != vessel)
                return;

            lastDecoupledVessel = vessel; // see why in OnPartDeCoupleComplete

            if (physicsHold)
            {
                DisablePhysicsHold(true);
            }
        }

        // called after a new vessel is created following intentional decoupler use or a joint failure
        // we have no way to identify the "old" vessel from which the part comes from, so we have saved
        // the vessel reference in OnPartCouple. Note that OnPartDeCouple/OnPartDeCoupleComplete are called
        // at the begining/end of Part.decouple() (and it isn't recursive), so it's safe to do.
        // Also, GameEvents.onPartDeCoupleNewVesselComplete with access to both old and new vessel has been 
        // introduced in KSP 1.10 but for the sake of making this work in 1.8 - 1.9 we don't use it
        private void OnPartDeCoupleComplete(Part newVesselPart)
        {
            if (lastDecoupledVessel == null || lastDecoupledVessel != vessel)
                return;

            lastDecoupledVessel = null;
        }

        // called before any undocking code is executed, all parts still belong to the original vessel
        private void OnPartUndock(Part part)
        {
            if (part.vessel != vessel)
                return;

            // I can't identify the exact root cause, but in the following scenario :
            // - Scene was loaded with a on hold dominant vessel and a non-on-hold vessel
            // - the non-on-hold vessel dock to the on-hold vessel
            // - the non-on-hold vessel undock
            // Then, if physics hold is being disabled on the on-hold vessel, the GoOffRails call
            // will result in a bogus position/orbit, with NaN propagation crashing the game
            // So to be on the safe side, we always unpack the vessel before stock does anything,
            // and request an immediate repack in the next fixedupdate.
            if (physicsHold)
            {
                DisablePhysicsHold(true);
                delayedPhysicsHoldEnableRequest = true;
            }
        }

        // called after a new vessel is created following undocking
        private void OnVesselsUndocking(Vessel oldVessel, Vessel newVessel)
        {
            LogDebug($"OnVesselsUndocking called on {vessel.vesselName}, oldVessel {oldVessel.vesselName}, newVessel {newVessel.vesselName}");

            if (vessel != oldVessel)
                return;

            if (delayedPhysicsHoldEnableRequest)
            {
                PhysicsHold newVesselHoldInstance = newVessel.GetComponent<PhysicsHold>();
                newVesselHoldInstance.hasNeverBeenUnpacked = true;
                newVesselHoldInstance.delayedPhysicsHoldEnableRequest = true;
            }
        }

        #endregion

        #region SPECIFIC HACKS

        /// <summary>
        /// Wheels have a wheelSetup() method being called by a coroutine launched from OnStart(). That coroutine is waiting indefinitely 
        /// for part.packed to become false, which won't happen if the vessel is in hold since the scene start. This is an issue if we want
        /// to undock the vessel, as wheels have a onVesselUndocking callback that will nullref if the setup isn't done.
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
                    setupDone = true;
                    wheel.StopAllCoroutines();
                    typeof(ModuleWheelBase).GetMethod("wheelSetup", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(wheel, null);
                }
            }
            return setupDone;
        }

        // ModuleDockingNode FSM hacking, not needed but keeping it for reference in case we want to try enabling
        // docking a packed vessel
        //private IEnumerator SetupDockingNodesHoldState()
        //{
        //    foreach (Part part in vessel.Parts)
        //    {
        //        // part.dockingPorts is populated from Awake()
        //        foreach (PartModule partModule in part.dockingPorts)
        //        {
        //            if (!(partModule is ModuleDockingNode dockingNode))
        //                continue;

        //            while (dockingNode.st_ready == null)
        //            {
        //                yield return null;
        //            }

        //            // we can't remove the existing stock delegate calling "otherNode = FindNodeApproaches()", so 
        //            // we add our own delegate that will be called after the stock one, with our patched method.
        //            dockingNode.st_ready.OnFixedUpdate += delegate { FindNodeApproachesPatched(dockingNode); };
        //        }
        //    }
        //    yield break;
        //}

        /// <summary>
        /// Call the stock ModuleDockingNode.FindNodeApproaches(), and prevent it to ignore the vessels we are putting
        /// on physics hold by temporary setting Vessel.packed to false. Not that there is also a check on the Part.packed
        /// field, but we don't need to handle it since we don't pack parts that have a ModuleDockingNode in a ready state.
        /// </summary>
        //private void FindNodeApproachesPatched(ModuleDockingNode dockingNode)
        //{
        //    if (PhysicsHoldManager.fetch == null || PhysicsHoldManager.fetch.onHoldAndPackedInstances.Count == 0)
        //        return;

        //    foreach (PhysicsHold instance in PhysicsHoldManager.fetch.onHoldAndPackedInstances)
        //    {
        //        instance.vessel.packed = false;
        //    }

        //    try
        //    {
        //        dockingNode.otherNode = dockingNode.FindNodeApproaches();
        //    }
        //    catch (Exception e)
        //    {
        //        Debug.LogError($"ModuleDockingNode threw during FindNodeApproaches\n{e}");
        //    }
        //    finally
        //    {
        //        foreach (PhysicsHold instance in PhysicsHoldManager.fetch.onHoldAndPackedInstances)
        //        {
        //            instance.vessel.packed = true;
        //        }
        //    } 
        //}

        //private void ApplyPackedTweaks()
        //{
        //    foreach (Part part in vessel.Parts)
        //    {
        //        if (ShouldPartIgnorePack(part))
        //        {
        //            part.Unpack();
        //        }

        //        StopRoboticControllers(part);
        //    }
        //}

        //private bool ShouldPartIgnorePack(Part part)
        //{
        //    if (vessel.GetReferenceTransformPart() == part)
        //    {
        //        return false;
        //    }

        //    foreach (PartModule module in part.Modules)
        //    {
        //        if (module is ModuleDeployablePart mdp && mdp.deployState != ModuleDeployablePart.DeployState.BROKEN && (mdp.hasPivot || mdp.panelBreakTransform != null))
        //        {
        //            return true;
        //        }
        //        // mdn.state is persisted FSM state and is synchronized from Update(). A bit brittle, but we need
        //        // to be able to check it from Start() when docking node modules won't have initialized their FSM yet.
        //        else if (module is ModuleDockingNode mdn && mdn.state == "Ready") 
        //        {
        //            return true;
        //        }
        //    }
        //    return false;
        //}

        //private void StopRoboticControllers(Part part)
        //{
        //    foreach (PartModule module in part.Modules)
        //    {
        //        if (module is ModuleRoboticController mrc)
        //        {
        //            mrc.SequenceStop();
        //        }
        //    }
        //}

        //private void EnableRobotics()
        //{
        //    List<Part> roboticParts = new List<Part>();
        //    UnpackRoboticChilds(roboticParts, vessel.rootPart, false);

        //    foreach (Part part in roboticParts)
        //    {
        //        part.Unpack();
        //    }
        //}

        //private void UnpackRoboticChilds(List<Part> roboticParts, Part part, bool parentIsRobotic)
        //{
        //    if (!parentIsRobotic && part.isRobotic())
        //    {
        //        parentIsRobotic = true;
        //    }

        //    if (parentIsRobotic)
        //    {
        //        if (part.vessel.rootPart == part)
        //        {
        //            roboticParts.Clear();
        //            ScreenMessages.PostScreenMessage($"Can't enable robotics, the vessel control part\n{part.partInfo.title}\nis a child of a robotic part");
        //            return;
        //        }

        //        roboticParts.Add(part);
        //    }

        //    foreach (Part child in part.children)
        //    {
        //        UnpackRoboticChilds(roboticParts, child, parentIsRobotic);
        //    }
        //}

        #endregion

        #region UTILS

        [Conditional("Debug")]
        private static void LogDebug(string message)
        {
            Debug.Log($"[PhysicsHold] {message}");
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

            physicsHold = vessel.GetComponent<PhysicsHold>().physicsHold;

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
