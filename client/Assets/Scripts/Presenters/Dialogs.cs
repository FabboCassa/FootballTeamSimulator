using System;
using Fts.Services;
using Fts.Services.Localization;
using Fts.Views;
using UnityEngine.UIElements;

namespace Fts.Presenters
{
    /// <summary>
    /// Presenter-layer convenience for overlays (task 6.2): builds the themed visual (Views.Overlay)
    /// and shows it through the <see cref="OverlayHost"/> (Services), wiring the dismiss handle into
    /// the dialog's buttons. This is the seam where the two layers meet — presenters reference both.
    /// </summary>
    public static class Dialogs
    {
        /// <summary>Default on-screen time for a toast.</summary>
        private const int ToastMs = 1800;

        /// <summary>
        /// Pops a modal confirm dialog. <paramref name="onConfirm"/> runs only if the user confirms;
        /// cancel (or a backdrop tap) just dismisses. Titles/labels are localization keys;
        /// <paramref name="messageArgs"/> (optional) format the message template.
        /// </summary>
        public static void Confirm(
            OverlayHost host, ILocalizationService loc,
            string titleKey, string messageKey, string confirmKey, Action onConfirm,
            params object[] messageArgs)
        {
            string message = (messageArgs != null && messageArgs.Length > 0)
                ? loc.Tr(messageKey, messageArgs)
                : loc.Tr(messageKey);

            Action dismiss = null;
            VisualElement dialog = Overlay.BuildConfirm(
                loc.Tr(titleKey),
                message,
                loc.Tr(confirmKey),
                loc.Tr("common.cancel"),
                onConfirm: () => { dismiss?.Invoke(); onConfirm?.Invoke(); },
                onCancel: () => dismiss?.Invoke());

            dismiss = host.Show(dialog);
        }

        /// <summary>Shows a transient success/info toast.</summary>
        public static void Toast(OverlayHost host, ILocalizationService loc, string key, params object[] args)
        {
            string text = (args != null && args.Length > 0) ? loc.Tr(key, args) : loc.Tr(key);
            host.ShowTimed(Overlay.BuildToast(text), ToastMs);
        }
    }
}
