using UnityEngine;

public struct MeshInfo
{
    public int triangleStartIndex;
    public int triangleCount;
    public RayTracingMaterial material;
    public Vector3 boundsMin;
    public Vector3 boundsMax;
    
    
    public MeshInfo(int triangleStartIndex, int triangleCount, RayTracingMaterial material, Vector3 boundsMin, Vector3 boundsMax)
    {
        this.triangleStartIndex = triangleStartIndex;
        this.triangleCount = triangleCount;
        this.material = material;
        this.boundsMin = boundsMin;
        this.boundsMax = boundsMax;
    }
}
