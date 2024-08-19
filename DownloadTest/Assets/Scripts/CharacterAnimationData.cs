using System;

[Serializable]
public class CharacterAnimationData
{
    public int frames_per_second { get; set; }
    public int horizontal_images { get; set; }
    public int vertical_images { get; set; }
    public int total_frames_count { get; set; }
    public string[] joints_names { get; set; }
    public int[] joints_parents { get; set; }
    public float[][][] positions { get; set; }
    public float[][][] vertices_xy { get; set; }
    public float[][] vertices_uv { get; set; }
    public float[][] triangles { get; set; }
    public int[][][] rendering_orders { get; set; }
}