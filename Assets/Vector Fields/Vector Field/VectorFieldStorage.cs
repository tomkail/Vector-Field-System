using UnityEngine;

// Project-wide choice of how a DrawableVectorFieldComponent serializes its painted grid INTO THE SCENE/PREFAB. The
// data always lives on the component (it's a scene object, never an asset) — only the on-disk representation changes:
//  - Vector2Array: human-readable YAML, but one {x,y} line per cell, so a 128x128 field is ~16k lines. Big scenes,
//    slow save/load, noisy and merge-hostile diffs.
//  - ByteArray: the same floats packed into one compact base64 blob. Tiny scenes and clean diffs; not human-readable.
//
// `format` is the value the component reads at serialize time. It's a plain static (safe to read from a serialization
// callback, unlike a ScriptableSingleton) that the editor's Project Settings > Vector Fields > Storage page persists
// and pushes here on load; at runtime it's unused (players only deserialize, detecting the format from the data).
public static class VectorFieldStorage {
    public enum Format { Vector2Array, ByteArray }

    public static Format format = Format.Vector2Array;

    // Pack a vector grid into raw little-endian float bytes (x0,y0,x1,y1,...). Serializes as a single base64 string.
    public static byte[] Pack(Vector2[] values) {
        if (values == null || values.Length == 0) return System.Array.Empty<byte>();
        var floats = new float[values.Length * 2];
        for (int i = 0; i < values.Length; i++) { floats[2 * i] = values[i].x; floats[2 * i + 1] = values[i].y; }
        var bytes = new byte[floats.Length * sizeof(float)];
        System.Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    // Inverse of Pack. `count` is the expected cell count (grid width*height); the result is always that length so a
    // truncated/oversized blob degrades gracefully rather than throwing.
    public static Vector2[] Unpack(byte[] bytes, int count) {
        var values = new Vector2[Mathf.Max(0, count)];
        if (bytes == null || bytes.Length < sizeof(float) * 2) return values;
        var floats = new float[bytes.Length / sizeof(float)];
        System.Buffer.BlockCopy(bytes, 0, floats, 0, floats.Length * sizeof(float));
        for (int i = 0; i < values.Length && 2 * i + 1 < floats.Length; i++)
            values[i] = new Vector2(floats[2 * i], floats[2 * i + 1]);
        return values;
    }
}
