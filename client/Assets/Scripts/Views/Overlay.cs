using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fts.Views
{
    /// <summary>
    /// Themed overlay visuals (task 6.2): a modal confirm dialog and a transient toast, built from
    /// the same UiKit design tokens as the rest of the UI. Pure builders — they take callbacks and
    /// return a <see cref="VisualElement"/>; the OverlayHost (Services) owns adding/removing them
    /// from the UI root, and the Presenters' Dialogs helper wires the two together.
    /// </summary>
    public static class Overlay
    {
        /// <summary>
        /// A full-screen modal: a dimmed backdrop that captures input plus a centred card with a
        /// title, message and cancel/confirm buttons. Clicking the backdrop cancels; clicks on the
        /// card are swallowed so they don't dismiss it.
        /// </summary>
        public static VisualElement BuildConfirm(
            string title, string message, string confirmText, string cancelText,
            Action onConfirm, Action onCancel)
        {
            var backdrop = new VisualElement();
            backdrop.style.position = Position.Absolute;
            backdrop.style.left = 0;
            backdrop.style.top = 0;
            backdrop.style.right = 0;
            backdrop.style.bottom = 0;
            backdrop.style.backgroundColor = new Color(0f, 0f, 0f, 0.62f);
            backdrop.style.alignItems = Align.Center;
            backdrop.style.justifyContent = Justify.Center;
            backdrop.style.paddingLeft = UiKit.SpaceMd;
            backdrop.style.paddingRight = UiKit.SpaceMd;
            backdrop.RegisterCallback<ClickEvent>(_ => onCancel?.Invoke());

            var card = UiKit.Card();
            card.style.maxWidth = 440;
            card.style.minWidth = 300;
            // Don't let clicks inside the card fall through to the backdrop (which cancels).
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());

            var titleLabel = UiKit.Header(title);
            titleLabel.style.whiteSpace = WhiteSpace.Normal;
            card.Add(titleLabel);

            var messageLabel = new Label(message);
            messageLabel.style.fontSize = UiKit.FontBody;
            messageLabel.style.color = UiKit.TextMuted;
            messageLabel.style.whiteSpace = WhiteSpace.Normal;
            messageLabel.style.marginBottom = UiKit.SpaceMd;
            card.Add(messageLabel);

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.justifyContent = Justify.FlexEnd;

            var cancel = UiKit.MenuButton(cancelText, () => onCancel?.Invoke());
            cancel.style.width = 130;
            cancel.style.height = 46;
            cancel.style.marginRight = UiKit.SpaceSm;
            buttons.Add(cancel);

            var confirm = UiKit.PrimaryButton(confirmText, () => onConfirm?.Invoke());
            confirm.style.width = 150;
            confirm.style.height = 46;
            buttons.Add(confirm);

            card.Add(buttons);
            backdrop.Add(card);

            // Desktop keyboard niceties (task 6.5): Enter confirms, Esc cancels. The backdrop is made
            // focusable and the confirm button is focused once attached, so the bubbling key events land
            // here. (The OverlayHost flags the dialog as modal, so the global Esc-back is suppressed and
            // doesn't fight this handler.)
            backdrop.focusable = true;
            backdrop.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Escape)
                {
                    onCancel?.Invoke();
                    e.StopPropagation();
                }
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    onConfirm?.Invoke();
                    e.StopPropagation();
                }
            });
            backdrop.RegisterCallback<AttachToPanelEvent>(_ => confirm.schedule.Execute(() => confirm.Focus()));
            return backdrop;
        }

        /// <summary>
        /// A transient bottom-centred pill. Non-interactive (it never blocks the screen beneath); the
        /// OverlayHost schedules its removal.
        /// </summary>
        public static VisualElement BuildToast(string message)
        {
            var layer = new VisualElement();
            layer.style.position = Position.Absolute;
            layer.style.left = 0;
            layer.style.top = 0;
            layer.style.right = 0;
            layer.style.bottom = 0;
            layer.style.alignItems = Align.Center;
            layer.style.justifyContent = Justify.FlexEnd;
            layer.style.paddingBottom = 48;
            layer.pickingMode = PickingMode.Ignore;

            var pill = new Label(message);
            pill.pickingMode = PickingMode.Ignore;
            pill.style.fontSize = UiKit.FontBody;
            pill.style.unityFontStyleAndWeight = FontStyle.Bold;
            pill.style.color = UiKit.TextOnAccent;
            pill.style.backgroundColor = UiKit.Accent;
            pill.style.paddingLeft = UiKit.SpaceLg;
            pill.style.paddingRight = UiKit.SpaceLg;
            pill.style.paddingTop = UiKit.SpaceSm;
            pill.style.paddingBottom = UiKit.SpaceSm;
            pill.style.unityTextAlign = TextAnchor.MiddleCenter;
            UiKit.Round(pill, UiKit.RadiusLg);
            layer.Add(pill);
            return layer;
        }
    }
}
