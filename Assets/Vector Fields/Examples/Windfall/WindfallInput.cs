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
        public enum Source { KeyboardSpace, KeyboardEnter, GamepadSouth, KeyboardKey }

        [Tooltip("Which device/button drives this player's single button.")]
        public Source source = Source.KeyboardSpace;
        [Tooltip("Which connected gamepad (only used when source is GamepadSouth).")]
        public int gamepadIndex = 0;
        [Tooltip("The keyboard key, when source is KeyboardKey (lets each player pick any key).")]
        public Key key = Key.Space;

        bool _prev;

        /// <summary>True while the button is down (drives catch).</summary>
        public bool Held { get; private set; }
        /// <summary>True on the frame the button went down (consume for discrete taps).</summary>
        public bool PressedThisFrame { get; private set; }
        /// <summary>True on the frame the button was released.</summary>
        public bool ReleasedThisFrame { get; private set; }

        /// <summary>Short human-readable name of this player's button, for the HUD.</summary>
        public string Label {
            get {
                switch (source) {
                    case Source.KeyboardSpace: return "Space";
                    case Source.KeyboardEnter: return "Enter";
                    case Source.KeyboardKey: return key.ToString();
                    case Source.GamepadSouth: return "Pad " + (gamepadIndex + 1);
                    default: return "?";
                }
            }
        }

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
                case Source.KeyboardKey: return Keyboard.current != null && Keyboard.current[key].isPressed;
                case Source.GamepadSouth:
                    var pads = Gamepad.all;
                    if (gamepadIndex < 0 || gamepadIndex >= pads.Count) return false;
                    return pads[gamepadIndex].buttonSouth.isPressed;
                default: return false;
            }
        }
    }
}
