using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PhysicsHold
{
    public class DialogGuiVesselWidget
    {
        private static StringBuilder sb = new StringBuilder();

        public DialogGUIBox widgetBox;

        public VesselPhysicsHold instance;
        public DialogGUILabel vesselTitle;
        public DialogGUIButton switchToButton;
        public DialogGUIToggle holdToggle;
        public DialogGUIToggle roboticsToggle;
        public DialogGUIButton deformationButton;
        public DialogGUILabel deformationButtonLabel;

        public DialogGuiVesselWidget(VesselPhysicsHold instance)
        {
            this.instance = instance;

            vesselTitle = new DialogGUILabel(() => GetVesselName(instance), 270f);

            DialogGUIVerticalLayout vesselInfo = new DialogGUIVerticalLayout(
                vesselTitle,
                new DialogGUILabel(() => GetVesselState(instance)));
            vesselInfo.padding = new RectOffset(5, 0, 0, 0);

            switchToButton = new DialogGUIButton(string.Empty, () => FlightGlobals.SetActiveVessel(instance.Vessel), 28f, 28f, false);
            switchToButton.OptionInteractableCondition += () => instance.Vessel.loaded && !instance.Vessel.isActiveVessel;

            DialogGUIHorizontalLayout boxTopSection = new DialogGUIHorizontalLayout(switchToButton, vesselInfo);
            boxTopSection.anchor = TextAnchor.MiddleLeft;

            holdToggle = new DialogGUIToggle(() => instance.physicsHold, "Physics hold", instance.OnToggleHold, 80f, 32f);
            holdToggle.OptionInteractableCondition += instance.CanTogglePhysicsHold;

            roboticsToggle = new DialogGUIToggle(() => instance.roboticsOverride, "Exclude robotics", instance.OnToggleRobotics, 80f, 32f);
            roboticsToggle.OptionInteractableCondition += instance.CanToggleRobotics;

            deformationButton = new DialogGUIButton("", () => instance.OnApplyDeformation(), false);
            deformationButton.OptionInteractableCondition += instance.CanApplyDeformation;
            deformationButton.size = new Vector2(32f, 32f);
            deformationButton.AddChild(new DialogGUIImage(new Vector2(32f, 32f), new Vector2(0f, 0f), Color.white, Lib.DeformTexture));

            deformationButtonLabel = new DialogGUILabel("Apply deformation", 80f, 32f);
            deformationButtonLabel.OptionInteractableCondition += instance.CanApplyDeformation;

            DialogGUIHorizontalLayout buttonsSection = new DialogGUIHorizontalLayout(holdToggle, roboticsToggle, deformationButton, deformationButtonLabel);

            DialogGUIVerticalLayout boxContent = new DialogGUIVerticalLayout(boxTopSection, buttonsSection);
            boxContent.padding = new RectOffset(5, 5, 5, 0);

            widgetBox = new DialogGUIBox("", 280f, 80f, null, boxContent);
        }

        public void TweakAfterCreation()
        {
            ColorBlock buttonColorTintColors = new ColorBlock()
            {
                colorMultiplier = 1f,
                normalColor = Color.white,
                selectedColor = Color.white,
                highlightedColor = new Color(0.75f, 1f, 0.65f, 1f),
                pressedColor = new Color(0.75f, 1f, 0.65f, 1f),
                disabledColor = new Color(0.75f, 1f, 0.65f, 1f)
            };

            // vessel title label tweaks

            vesselTitle.text.color = Color.white;
            vesselTitle.text.fontStyle = FontStyles.Bold;

            // switchTo button tweaks

            Lib.AddTooltipToObject(switchToButton.uiItem, "Make active");

            Image imgCpnt = switchToButton.uiItem.GetComponent<Image>();
            imgCpnt.type = Image.Type.Simple;
            imgCpnt.preserveAspect = true;
            imgCpnt.color = Color.white;
            imgCpnt.sprite = Lib.GetVesselTypeIcon(instance.Vessel);

            Button btnCpnt = switchToButton.uiItem.GetComponent<Button>();
            btnCpnt.transition = Selectable.Transition.ColorTint;
            btnCpnt.navigation = new Navigation() { mode = Navigation.Mode.None };
            btnCpnt.colors = buttonColorTintColors;
            btnCpnt.image = imgCpnt;

            // prevent the button from capturing the scroll events
            switchToButton.uiItem.GetComponent<EventTrigger>().enabled = false;

            // hold toggle tweaks

            // left-center label
            RectTransform holdToggleLabel = holdToggle.uiItem.GetChild("Label").GetComponent<RectTransform>();
            holdToggleLabel.anchorMin = new Vector2(0f, 0f);
            holdToggleLabel.anchorMax = new Vector2(1f, 1f);
            holdToggleLabel.sizeDelta = new Vector2(-29f, 0f);
            holdToggleLabel.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Left;

            holdToggle.tooltipText = "Disable joints, force and torque \ninteractions for this vessel. \nReduce lag and prevent \nKraken attacks.";
            Lib.AddTooltipToObject(holdToggle.uiItem, null);

            // robotics toggle tweaks

            // left-center label
            RectTransform roboticsToggleLabel = roboticsToggle.uiItem.GetChild("Label").GetComponent<RectTransform>();
            holdToggleLabel.anchorMin = new Vector2(0f, 0f);
            holdToggleLabel.anchorMax = new Vector2(1f, 1f);
            holdToggleLabel.sizeDelta = new Vector2(-29f, 0f);
            roboticsToggleLabel.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Left;

            roboticsToggle.tooltipText = "Always allow physics on all\nchild parts of robotic parts.";
            Lib.AddTooltipToObject(roboticsToggle.uiItem, null);

            // deformation button tweaks

            // left-center label
            TextMeshProUGUI deformationText = deformationButtonLabel.uiItem.GetComponent<TextMeshProUGUI>();
            deformationText.alignment = TextAlignmentOptions.Left;
            deformationText.color = Color.white;

            // set correct initial interactable state by forcing the current state to be inverted
            try
            {
                FieldInfo fi = typeof(DialogGUIBase).GetField("lastInteractibleState", BindingFlags.Instance | BindingFlags.NonPublic);
                fi.SetValue(deformationButtonLabel, !instance.CanApplyDeformation());
            }
            catch { }
            deformationButtonLabel.Update();

            // tweak button image
            RectTransform deformationButtonImage = deformationButton.children[0].uiItem.GetComponent<RectTransform>();
            deformationButtonImage.anchorMin = new Vector2(0f, 0f);
            deformationButtonImage.anchorMax = new Vector2(1f, 1f);
            deformationButtonImage.anchoredPosition = new Vector2(0f, 0f);
            deformationButtonImage.sizeDelta = new Vector2(0f, 0f);

            Lib.AddTooltipToObject(deformationButton.uiItem,
                "Move parts position according to the current \ndeformation due to gravity and external forces.\n" +
                "Reduce joint stress and prevent kraken attacks \nwhen landed on an uneven surface.\n\n" +
                "WARNING :\nWill permanently deform the vessel on every use !");

            // prevent the button from capturing the scroll events
            deformationButton.uiItem.GetComponent<EventTrigger>().enabled = false;


        }

        private string GetVesselName(VesselPhysicsHold instance)
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

        private string GetVesselState(VesselPhysicsHold instance)
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
    }
}
