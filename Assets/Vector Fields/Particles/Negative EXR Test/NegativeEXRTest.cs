using UnityEngine;
using System.Collections;
using System.IO;
 
public class NegativeEXRTest : MonoBehaviour {
    public int resolutionX = 16;
    public int resolutionY = 16;
    public TextureFormat tex2DFormat = TextureFormat.RGBAHalf;
    public RenderTextureFormat rtFormat = RenderTextureFormat.ARGBHalf;
    public Texture2D.EXRFlags exrFlags = Texture2D.EXRFlags.None;
    public Texture2D texToRead;
    public Texture3D tex3DToRead;
    public ParticleSystemForceField vectorFieldAffector;

    [ContextMenu("Do Read3D Test")]
    public void DoRead3DTest() {
        var colors = tex3DToRead.GetPixels();
        DebugX.ListAsString(colors);
    }
    
    [ContextMenu("Do Read Test")]
    public void DoReadTest() {
        var colors = texToRead.GetPixels();
        DebugX.ListAsString(colors);
    }
    [ContextMenu("Do Test")]
    public void DoTest()
    {
        // Create an array of colors that has RGB values from -1.0 to 1.0
        Color[] colors = new Color[resolutionX * resolutionY];
        int numPixels = resolutionX * resolutionY;
        for (int i=0; i<numPixels; i++)
        {
            float a = ((float)i / (float)(numPixels - 1)) * 2f - 1f;
            colors[i] = new Color(a, a, a, 1f);
        }
 
        // Create Texture2D and set pixels colors
        var tex = new Texture2D(resolutionX, resolutionY, tex2DFormat, false, true);
        tex.SetPixels(colors, 0);
        tex.Apply();
        var colorsSet = tex.GetPixels();
        Debug.Log(DebugX.ListAsString(colorsSet));
 
        // Create RenderTexture
        RenderTexture rt = new RenderTexture(resolutionX, resolutionY, 0, rtFormat, RenderTextureReadWrite.Linear);
        rt.Create();
 
        // Copy Texture2D to RenderTexture
        // It would be faster to use CopyTexture, but Blit() works by rendering the source texture into the destination
        // render texture with a simple unlit shader.
        Graphics.Blit(tex, rt);
 
        // Read RenderTexture contents into a new Texture2D using ReadPixels
        var texReadback = new Texture2D(resolutionX, resolutionY, tex2DFormat, false, true);
        Graphics.SetRenderTarget(rt);
        texReadback.ReadPixels(new Rect(0, 0, resolutionX, resolutionY), 0, 0, false);
        Graphics.SetRenderTarget(null);
        texReadback.Apply();
        colorsSet = texReadback.GetPixels();
        Debug.Log(DebugX.ListAsString(colorsSet));
 
        // Save out EXR file to project's root folder (outside of assets)
        byte[] bytes = tex.EncodeToEXR(exrFlags);
        File.WriteAllBytes(Application.dataPath + "/../Negative EXR Test.exr", bytes);
        Debug.Log("Saved texture to: " + Application.dataPath + "/../Negative EXR Test.exr");
 
        // Destroy texture objects
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(texReadback);
        Object.DestroyImmediate(rt);
        
        // Create Texture2D and set pixels colors
        var tex3D = new Texture3D(resolutionX, resolutionY, 1, tex2DFormat, false);
        tex3D.SetPixels(colors, 0);
        tex3D.Apply();
        colorsSet = tex3D.GetPixels();
        Debug.Log(DebugX.ListAsString(colorsSet));

        vectorFieldAffector.vectorField = tex3D;
    }
}