using UnityEngine;

// Project-wide choice of how a DrawableVectorFieldComponent serializes its painted grid INTO THE SCENE/PREFAB. The
// data always lives on the component (it's a scene object, never an asset) — only the on-disk representation changes:
//  - Vector2Array: human-readable YAML, but one {x,y} line per cell, so a 128x128 field is ~16k lines. Big scenes,
//    slow save/load, noisy and merge-hostile diffs.
//  - ByteArray: the floats packed compactly as one base64 string per grid ROW. Small scenes AND locally diffable —
//    an edit rewrites only its row's line, not the whole field (a single blob would change entirely on any edit).
//    Not human-readable.
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
    // truncated/oversized blob degrades gracefully rather than throwing. Kept for migrating fields saved with the
    // old single-blob byte format; the current ByteArray format packs per row (PackRows) instead.
    public static Vector2[] Unpack(byte[] bytes, int count) {
        var values = new Vector2[Mathf.Max(0, count)];
        if (bytes == null || bytes.Length < sizeof(float) * 2) return values;
        var floats = new float[bytes.Length / sizeof(float)];
        System.Buffer.BlockCopy(bytes, 0, floats, 0, floats.Length * sizeof(float));
        for (int i = 0; i < values.Length && 2 * i + 1 < floats.Length; i++)
            values[i] = new Vector2(floats[2 * i], floats[2 * i + 1]);
        return values;
    }

    // Pack the grid ONE ROW PER STRING (base64 of that row's floats). Unity serializes a string[] as one line per
    // element, so editing a cell only rewrites its row's line — compact (≈height lines, not width*height) AND
    // locally diffable/mergeable, unlike a single blob where any edit rewrites the whole thing.
    public static string[] PackRows(Vector2[] values, Point size) {
        int w = size.x, h = size.y;
        if (values == null || w <= 0 || h <= 0 || values.Length < w * h) return System.Array.Empty<string>();
        var rows = new string[h];
        var floats = new float[w * 2];
        var bytes = new byte[floats.Length * sizeof(float)];
        for (int y = 0; y < h; y++) {
            int baseIdx = y * w;
            for (int x = 0; x < w; x++) { floats[2 * x] = values[baseIdx + x].x; floats[2 * x + 1] = values[baseIdx + x].y; }
            System.Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
            rows[y] = System.Convert.ToBase64String(bytes);
        }
        return rows;
    }

    // Inverse of PackRows. Always returns a width*height array; missing/short rows stay zero rather than throwing.
    public static Vector2[] UnpackRows(string[] rows, Point size) {
        int w = size.x, h = size.y;
        var values = new Vector2[Mathf.Max(0, w * h)];
        if (rows == null || w <= 0 || h <= 0) return values;
        var floats = new float[w * 2];
        for (int y = 0; y < h && y < rows.Length; y++) {
            if (string.IsNullOrEmpty(rows[y])) continue;
            var bytes = System.Convert.FromBase64String(rows[y]);
            System.Buffer.BlockCopy(bytes, 0, floats, 0, Mathf.Min(bytes.Length, floats.Length * sizeof(float)));
            int baseIdx = y * w;
            for (int x = 0; x < w; x++) values[baseIdx + x] = new Vector2(floats[2 * x], floats[2 * x + 1]);
        }
        return values;
    }
}
