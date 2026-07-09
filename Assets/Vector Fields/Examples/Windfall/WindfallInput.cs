using UnityEngine;
using UnityEngine.InputSystem;

namespace Windfall {
    /// <summary>
    /// One-button input for a single player (GAME_DESIGN.md §7): launch aim/power locking and the
    /// catch button are all the SAME button. Abstracted per-player so local multiplayer is just more
    /// input sources. Poll() once per Update; read <see cref="Held"/> for catch and consume
    /// <see cref="PressedThisFrame"/> for discrete taps (lock direction / fire / re-launch).
    /// Uses the new Input System via direct device polling — no .inputactions asset needed yet.
    /// </summary>
    [System.Serializable]
    public class WindfallInput {
        public enum Source { KeyboardSpace, KeyboardEnter, GamepadSouth }

        [Tooltip("Which device/button drives this player's single button.")]
        public Source source = Source.KeyboardSpace;
        [Tooltip("Which connected gamepad (only used when source is GamepadSouth).")]
        public int gamepadIndex = 0;

        bool _prev;

        /// <summary>True while the button is down (drives catch).</summary>
        public bool Held { get; private set; }
        /// <summary>True on the frame the button went down (consume for discrete taps).</summary>
        public bool PressedThisFrame { get; private set; }
        /// <summary>True on the frame the button was released.</summary>
        public bool ReleasedThisFrame { get; private set; }

        public void Poll() {
            bool now = ReadRaw();
            PressedThisFrame = now && !_prev;
            ReleasedThisFrame = !now && _prev;
            Held = now;
            _prev = now;
        }

        bool ReadRaw() {
            switch (source) {
                case Source.KeyboardSpace: return Keyboard.current != null && Keyboard.current.spaceKey.isPressed;
                case Source.KeyboardEnter: return Keyboard.current != null && Keyboard.current.enterKey.isPressed;
                case Source.GamepadSouth:
                    var pads = Gamepad.all;
                    if (gamepadIndex < 0 || gamepadIndex >= pads.Count) return false;
                    return pads[gamepadIndex].buttonSouth.isPressed;
                default: return false;
            }
        }
    }
}
