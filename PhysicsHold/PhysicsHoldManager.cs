using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using KSP.UI.Screens;
using UnityEngine.UI;
using TMPro;
using HarmonyLib;

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

            Harmony harmony = new Harmony("PhysicsHold");
            harmony.PatchAll();

        }

        private void Start()
        {
            if (lastPopupPos == Vector2.zero)
            {
                lastPopupPos = new Vector2(0.1f, 0.9f);
            }

            launcher = ApplicationLauncher.Instance.AddModApplication(null, null, null, null, null, null, ApplicationLauncher.AppScenes.FLIGHT, Lib.LauncherTexture);
            launcher.onLeftClick += ToggleDialog;

            Lib.AddTooltipToObject(launcher.gameObject, "Physics hold");
        }

        private void FixedUpdate()
        {
            OnHoldAndPackedInstances.Clear();

            for (int i = physicsHoldInstances.Count - 1; i >= 0; i--)
            {
                if (physicsHoldInstances[i].Vessel.state == Vessel.State.DEAD)
                {
                    physicsHoldInstances.RemoveAt(i);
                    continue;
                }

                if (physicsHoldInstances[i].physicsHold && physicsHoldInstances[i].Vessel.packed)
                {
                    OnHoldAndPackedInstances.Add(physicsHoldInstances[i]);
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

            DialogGUILabel kasInstalledLabel = null;
            if (isKASInstalled)
            {
                kasInstalledLabel = new DialogGUILabel("<color=orange><b>WARNING</b></color> : KAS is installed. \nHaving a KAS connection on a on-hold vessel <b>won't work</b> and will cause it to disappear from the game", true, false);
                dialog.Add(kasInstalledLabel);
            }

            Rect posAndSize = new Rect(lastPopupPos.x, lastPopupPos.y, 320f, isKASInstalled ? 365f : 320f);

            currentDialog = PopupDialog.SpawnPopupDialog(new Vector2(0f, 1f), new Vector2(0f, 1f),
                new MultiOptionDialog("PhysicsHoldDialog", "", "", UISkinManager.defaultSkin, posAndSize, dialog.ToArray()), true, UISkinManager.defaultSkin, false);


            // wteak title text style
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

            if (kasInstalledLabel != null)
            {
                kasInstalledLabel.uiItem.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Top;
            }
        }
    }
}
