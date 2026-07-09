using System;
using System.Collections.Generic;
using UnityEngine;

// Temporary harness for comparing Unity's built-in enum/flags drawers against the UnityX attribute
// drawers (EnumButtons, EnumButtonGroup, EnumFlagsButtonGroup) before deleting the latter.
// Each section shows the same field drawn by the default drawer and by the UnityX drawers.
// Things to poke at while comparing:
//  - Flags: does the default mask dropdown handle None / Everything / composite values the same way?
//  - Composite members (Horizontal = Left|Right): shown as checked when both bits set? Selectable?
//  - Byte/long backing types: do values above the backing type's range or bit 31 survive serialization?
//  - Undefined raw values: what does each drawer display, and does clicking destroy the value?
//  - Lists: toggle flags on one element, confirm others don't change (drawer instance reuse).
public class EnumDrawerComparisonTest : MonoBehaviour {

    public enum Simple { A, B, C, D }

    public enum CustomValues { Negative = -1, Zero = 0, Five = 5, Hundred = 100 }

    [Flags]
    public enum SimpleFlags {
        None = 0,
        A = 1 << 0,
        B = 1 << 1,
        C = 1 << 2,
        D = 1 << 3,
    }

    [Flags]
    public enum CompositeFlags {
        None = 0,
        Left = 1 << 0,
        Right = 1 << 1,
        Horizontal = Left | Right,
        Up = 1 << 2,
        Down = 1 << 3,
        Vertical = Up | Down,
        All = Horizontal | Vertical,
    }

    [Flags]
    public enum ByteFlags : byte {
        None = 0,
        A = 1,
        B = 2,
        C = 4,
        HighBit = 128,
    }

    [Header("Simple enum")]
    public Simple simpleDefault = Simple.B;
    [EnumButtons] public Simple simpleEnumButtons = Simple.B;

    [Header("Custom-valued enum (negative + non-sequential)")]
    public CustomValues customDefault = CustomValues.Five;
    [EnumButtons] public CustomValues customEnumButtons = CustomValues.Five;

    [Header("Flags — simple power-of-two")]
    public SimpleFlags flagsDefault = SimpleFlags.A | SimpleFlags.C;
    [EnumFlagsButtonGroup] public SimpleFlags flagsButtonGroup = SimpleFlags.A | SimpleFlags.C;

    [Header("Flags — with composite members")]
    public CompositeFlags compositeDefault = CompositeFlags.Horizontal | CompositeFlags.Up;
    [EnumFlagsButtonGroup] public CompositeFlags compositeButtonGroup = CompositeFlags.Horizontal | CompositeFlags.Up;

    [Header("Flags — byte-backed")]
    public ByteFlags byteDefault = ByteFlags.A | ByteFlags.HighBit;
    [EnumFlagsButtonGroup] public ByteFlags byteButtonGroup = ByteFlags.A | ByteFlags.HighBit;

    // Long-backed flags enums were tried here: Unity rejects them outright at the serialization layer
    // ("Unsupported enum type"), so they never reach ANY drawer — not a point of comparison.

    [Header("Undefined raw values (no matching name)")]
    public Simple undefinedSimple = (Simple)7;
    [EnumButtons] public Simple undefinedSimpleButtons = (Simple)7;
    public SimpleFlags undefinedFlags = (SimpleFlags)(1 << 6);
    [EnumFlagsButtonGroup] public SimpleFlags undefinedFlagsButtonGroup = (SimpleFlags)(1 << 6);

    [Header("Lists (drawer-instance reuse across elements)")]
    public List<SimpleFlags> flagsListDefault = new List<SimpleFlags> { SimpleFlags.A, SimpleFlags.B | SimpleFlags.C, SimpleFlags.None };
    [EnumFlagsButtonGroup] public List<SimpleFlags> flagsListButtonGroup = new List<SimpleFlags> { SimpleFlags.A, SimpleFlags.B | SimpleFlags.C, SimpleFlags.None };
}
