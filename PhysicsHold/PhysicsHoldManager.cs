using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using KSP.UI.Screens;
using Debug = UnityEngine.Debug;
using UnityEngine.UI;
using System.Text;
using TMPro;
using KSP.UI.TooltipTypes;
using UnityEngine.EventSystems;

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
        public static PhysicsHoldManager Instance { get; private set; }

        private List<VesselPhysicsHold> physicsHoldInstances;

        public List<VesselPhysicsHold> OnHoldAndPackedInstances { get; private set; }

        private ApplicationLauncherButton launcher;

        private PopupDialog currentDialog;
        private static Vector2 lastPopupPos = Vector2.zero;

        private static bool isKASInstalled;

        public static void AddInstance(VesselPhysicsHold instance)
        {
            if (Instance == null)
                return;

            if (Instance.physicsHoldInstances.Contains(instance))
                return;

            Lib.LogDebug($"Adding VesselPhysicsHold instance for {instance.Vessel.vesselName}");

            Instance.physicsHoldInstances.Add(instance);
            if (Instance.currentDialog != null)
            {
                Instance.DismissDialog();
                Instance.ToggleDialog();
            }
        }

        public static void RemoveInstance(VesselPhysicsHold instance)
        {
            if (Instance == null)
                return;

            Lib.LogDebug($"Removing VesselPhysicsHold instance for {instance.Vessel.vesselName}");

            if (Instance.physicsHoldInstances.Remove(instance) && Instance.currentDialog != null)
            {
                Instance.DismissDialog();
                Instance.ToggleDialog();
            }
        }

        private void Awake()
        {
            OnHoldAndPackedInstances = new List<VesselPhysicsHold>();

            if (Instance != null)
                Destroy(Instance);

            Instance = this;
            physicsHoldInstances = new List<VesselPhysicsHold>();

            isKASInstalled = false;
            AssemblyName assemblyName;
            foreach (AssemblyLoader.LoadedAssembly loadedAssembly in AssemblyLoader.loadedAssemblies)
            {
                assemblyName = new AssemblyName(loadedAssembly.assembly.FullName);
                if (assemblyName.Name == "KAS")
                {
                    isKASInstalled = true;
                    break;
                }
            }

        }

        private void Start()
        {
            if (lastPopupPos == Vector2.zero)
            {
                lastPopupPos = new Vector2(0.1f, 0.9f);
            }

            launcher = ApplicationLauncher.Instance.AddModApplication(null, null, null, null, null, null, ApplicationLauncher.AppScenes.FLIGHT, Lib.LauncherTexture);
            //launcher.VisibleInScenes = ApplicationLauncher.AppScenes.FLIGHT;
            launcher.onLeftClick += ToggleDialog;

            Lib.AddTooltipToObject(launcher.gameObject, "Physics hold");
        }

        private void FixedUpdate()
        {
            OnHoldAndPackedInstances.Clear();

            foreach (VesselPhysicsHold instance in physicsHoldInstances)
            {
                if (instance.physicsHold && instance.Vessel.packed)
                {
                    OnHoldAndPackedInstances.Add(instance);
                }
            }
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


            DialogGUILabel title = new DialogGUILabel("Physics Hold", true);
            DialogGUILabel subTitle = new DialogGUILabel("Keep physics in check for landed vessels !", true);
            DialogGUIVerticalLayout titleLayout = new DialogGUIVerticalLayout(true, false);
            titleLayout.AddChild(title);
            titleLayout.AddChild(subTitle);

            DialogGUIButton closeButton = new DialogGUIButton(string.Empty, () => DismissDialog(), 24f, 24f, false);
            DialogGUIHorizontalLayout headerLayout = new DialogGUIHorizontalLayout(titleLayout, closeButton);

            List<DialogGuiVesselWidget> widgets = new List<DialogGuiVesselWidget>();

            for (int i = 0; i < physicsHoldInstances.Count; i++)
            {
                DialogGuiVesselWidget widget = new DialogGuiVesselWidget(physicsHoldInstances[i]);
                widgets.Add(widget);
            }

            Vector3 activeVesselPos = FlightGlobals.ActiveVessel.transform.position;
            widgets.Sort((x, y) => Vector3.Distance(activeVesselPos, x.instance.Vessel.transform.position) < Vector3.Distance(activeVesselPos, y.instance.Vessel.transform.position) ? -1 : 1);

            DialogGUIBase[] scrollList = new DialogGUIBase[widgets.Count + 1];

            scrollList[0] = new DialogGUIContentSizer(ContentSizeFitter.FitMode.Unconstrained, ContentSizeFitter.FitMode.PreferredSize, true);

            for (int i = 0; i < widgets.Count; i++)
                scrollList[i + 1] = widgets[i].widgetBox;

            DialogGUIScrollList scrollListItem = new DialogGUIScrollList(Vector2.one, false, true,
                new DialogGUIVerticalLayout(10, 100, 4, new RectOffset(6, 24, 10, 10), TextAnchor.UpperLeft, scrollList));

            List<DialogGUIBase> dialog = new List<DialogGUIBase>();
            dialog.Add(headerLayout);
            dialog.Add(scrollListItem);

            if (isKASInstalled)
            {
                dialog.Add(new DialogGUILabel("<color=orange><b>WARNING</b></color> : KAS is installed. \nHaving a KAS connection on a on-hold vessel <b>won't work</b> and will cause it to disappear from the game", true, false));
            }

            Rect posAndSize = new Rect(lastPopupPos.x, lastPopupPos.y, 320f, isKASInstalled ? 365f : 320f);

            currentDialog = PopupDialog.SpawnPopupDialog(new Vector2(0f, 1f), new Vector2(0f, 1f),
                new MultiOptionDialog("PhysicsHoldDialog", "", "", UISkinManager.defaultSkin, posAndSize, dialog.ToArray()), true, UISkinManager.defaultSkin, false);


            //Destroy(currentDialog.gameObject.GetChild("Title"));
            TextMeshProUGUI titleText = title.uiItem.GetComponent<TextMeshProUGUI>();
            titleText.alignment = TextAlignmentOptions.Top;
            titleText.color = Color.white;
            titleText.fontSize = 16f;
            titleText.fontStyle = FontStyles.Bold;

            TextMeshProUGUI subTitleText = subTitle.uiItem.GetComponent<TextMeshProUGUI>();
            subTitleText.alignment = TextAlignmentOptions.Top;

            // top-left align widgets in the scroll area
            ScrollRect scrollRect = scrollListItem.uiItem.GetComponentInChildren<ScrollRect>();
            scrollRect.content.pivot = new Vector2(0f, 1f);

            foreach (DialogGuiVesselWidget widget in widgets)
            {
                widget.TweakAfterCreation();
            }

            // close button use custom sprite 

            ColorBlock buttonColorTintColors = new ColorBlock()
            {
                colorMultiplier = 1f,
                normalColor = Color.white,
                selectedColor = Color.white,
                highlightedColor = new Color(0.75f, 1f, 0.65f, 1f),
                pressedColor = new Color(0.75f, 1f, 0.65f, 1f),
                disabledColor = new Color(1f, 1f, 1f, 1f)
            };

            Image imgCpnt = closeButton.uiItem.GetComponent<Image>();
            imgCpnt.type = Image.Type.Simple;
            imgCpnt.preserveAspect = true;
            imgCpnt.color = Color.white;
            imgCpnt.sprite = Lib.CloseSprite;

            Button btnCpnt = closeButton.uiItem.GetComponent<Button>();
            btnCpnt.transition = Selectable.Transition.ColorTint;
            btnCpnt.navigation = new Navigation() { mode = Navigation.Mode.None };
            btnCpnt.colors = buttonColorTintColors;
            btnCpnt.image = imgCpnt;
        }
    }
}
