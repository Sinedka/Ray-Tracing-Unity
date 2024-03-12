using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static UnityEngine.Mathf;
using Random = System.Random;

[ExecuteAlways, ImageEffectAllowedInSceneView]
public class RayTracingManager : MonoBehaviour
{
    private Random rnd = new Random();
    [SerializeField] private bool useShaderInSceneView;
    [SerializeField] private Shader rayTracingShader;
    [SerializeField] private Shader accumulateShader;
    [SerializeField] private int RaysPerFrame = 10;

    private ComputeBuffer _sphereBuffer;
    private ComputeBuffer _MeshesBuffer;
    private ComputeBuffer _TrianglesBuffer;
    private ComputeBuffer _VerticesBuffer;
    
    
    private Material _rayTracingMaterial;
    private Material accumulateMaterial;
    private int frame = 0;

    RenderTexture resultTexture;

    void Start()
    {
        frame = 0;
    }

    private void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (Camera.current.name != "SceneCamera" && Application.isPlaying)
        {

            ShaderHelper.InitMaterial(rayTracingShader, ref _rayTracingMaterial);
            ShaderHelper.CreateRenderTexture(ref resultTexture, Screen.width, Screen.height, FilterMode.Bilinear,
                ShaderHelper.RGBA_SFloat, "Result");
            UpdateCameraParams(Camera.current);
            CreateSpheres();
            CreateMeshes();
            UpdateShaderParams();
            _rayTracingMaterial.SetInt("Frame", frame);
            
            RenderTexture prevFrameCopy = RenderTexture.GetTemporary(src.width, src.height, 0, ShaderHelper.RGBA_SFloat);
            Graphics.Blit(resultTexture, prevFrameCopy);
            
            RenderTexture currentFrame = RenderTexture.GetTemporary(src.width, src.height, 0, ShaderHelper.RGBA_SFloat);
            Graphics.Blit(null, currentFrame, _rayTracingMaterial);
            ShaderHelper.InitMaterial(accumulateShader, ref accumulateMaterial);
            accumulateMaterial.SetInt("_Frame", frame);
            accumulateMaterial.SetTexture("_PrevFrame", prevFrameCopy);
            Graphics.Blit(currentFrame, resultTexture, accumulateMaterial); 

            // Draw result to screen
            Graphics.Blit(resultTexture, dest);
            if (frame > 100)
            {
                Debug.Log("Screenshot1.png");
                ScreenCapture.CaptureScreenshot("Screenshot1.png");
                Application.Quit();
            }


            RenderTexture.ReleaseTemporary(currentFrame);
            RenderTexture.ReleaseTemporary(prevFrameCopy);
            RenderTexture.ReleaseTemporary(currentFrame);

            // if (frame == -1)
            // {
            //     Debug.Log("Screen");
            //     ScreenCapture.CaptureScreenshot(
            //         "C:\\Users\\SinedKa\\Desktop\\Progects Unity\\Ray Tracing\\Assets\\ScreenShot.png");
            // }

            frame += 1;
        }
        else if(Camera.current.name != "SceneCamera" || useShaderInSceneView)
        {
             
            ShaderHelper.InitMaterial(rayTracingShader, ref _rayTracingMaterial);
            ShaderHelper.InitMaterial(accumulateShader, ref accumulateMaterial);
            ShaderHelper.CreateRenderTexture(ref resultTexture, Screen.width, Screen.height, FilterMode.Bilinear,
                ShaderHelper.RGBA_SFloat, "Result");
            UpdateCameraParams(Camera.current);
            CreateSpheres();
            CreateMeshes();
            UpdateShaderParams();
            if (Camera.current.name != "SceneCamera")
            {
                _rayTracingMaterial.SetInt("Frame", frame);
            }
            else
            {
               _rayTracingMaterial.SetInt("Frame", rnd.Next());
            }
            RenderTexture currentFrame = RenderTexture.GetTemporary(src.width, src.height, 0, ShaderHelper.RGBA_SFloat);
            Graphics.Blit(null, currentFrame, _rayTracingMaterial);

            RenderTexture prevFrameCopy =
                RenderTexture.GetTemporary(src.width, src.height, 0, ShaderHelper.RGBA_SFloat);
            Graphics.Blit(resultTexture, prevFrameCopy);
            ShaderHelper.InitMaterial(accumulateShader, ref accumulateMaterial);
            accumulateMaterial.SetInt("_Frame", frame);
            accumulateMaterial.SetTexture("_PrevFrame", prevFrameCopy);
            Graphics.Blit(currentFrame, resultTexture, accumulateMaterial);

            // Draw result to screen
            Graphics.Blit(resultTexture, dest);

            RenderTexture.ReleaseTemporary(currentFrame);
            RenderTexture.ReleaseTemporary(prevFrameCopy);
            RenderTexture.ReleaseTemporary(currentFrame);
        }
        else
        {
            Graphics.Blit(src, dest);
        }
    }

    void UpdateCameraParams(Camera cam)
    {
        float planeHeight = cam.nearClipPlane * Tan(cam.fieldOfView * 0.5f * Deg2Rad) * 2;
        float planeWidth = planeHeight * cam.aspect;
        // Send data to shader
        _rayTracingMaterial.SetVector("ViewParams", new Vector3(planeWidth, planeHeight, cam.nearClipPlane));
        _rayTracingMaterial.SetMatrix("CamLocalToWorldMatrix", cam.transform.localToWorldMatrix);
    }

    void UpdateShaderParams()
    {
        _rayTracingMaterial.SetInt("NumRaysPerPixel", RaysPerFrame);
        _rayTracingMaterial.SetInt("Frame", frame);
    }

    void UpdateAcumulateParams()
    {
        
    }
    
    void CreateSpheres()
    {
        // Create sphere data from the sphere objects in the scene
        RayTracedSphere[] sphereObjects = FindObjectsOfType<RayTracedSphere>();
        Sphere[] spheres = new Sphere[sphereObjects.Length];

        for (int i = 0; i < sphereObjects.Length; i++)
        {
            spheres[i] = new Sphere()
            {
                position = sphereObjects[i].transform.position,
                radius = sphereObjects[i].transform.localScale.x * 0.5f,
                material = sphereObjects[i].material
            };
        }

        // Create buffer containing all sphere data, and send it to the shader
        ShaderHelper.CreateStructuredBuffer(ref _sphereBuffer, spheres);
        _rayTracingMaterial.SetBuffer("Spheres", _sphereBuffer);
        _rayTracingMaterial.SetInt("NumSpheres", sphereObjects.Length);
    }

    void CreateMeshes()
    {
        RayTracedMesh[] rayTracedMeshes = FindObjectsOfType<RayTracedMesh>();
        List<MeshInfo> meshInfos = new List<MeshInfo>();
        List<int> triangles = new List<int>();
        List<Vector3> vertices = new List<Vector3>();
        meshInfos.Clear();
        triangles.Clear();
        vertices.Clear();
        

        for (int i = 0; i < rayTracedMeshes.Length; i++)
        {
            meshInfos.Add(new MeshInfo(triangles.Count, rayTracedMeshes[i].triangles.Length, rayTracedMeshes[i].material, rayTracedMeshes[i].boundsMin, rayTracedMeshes[i].boundsMax));
            for (int j = 0; j < rayTracedMeshes[i].triangles.Length; j++)
            {
                int newTriangle = rayTracedMeshes[i].triangles[j] + vertices.Count;
                triangles.Add(newTriangle);
            }
            vertices.AddRange(rayTracedMeshes[i].vertices);
            
           
        }
        
        ShaderHelper.CreateStructuredBuffer(ref _MeshesBuffer, meshInfos );
        ShaderHelper.CreateStructuredBuffer(ref _VerticesBuffer, vertices );
        ShaderHelper.CreateStructuredBuffer(ref _TrianglesBuffer, triangles );
        
        _rayTracingMaterial.SetBuffer("meshInfos" ,_MeshesBuffer);
        _rayTracingMaterial.SetInt("NumMeshInfos", meshInfos.Count );
        _rayTracingMaterial.SetBuffer("vertices", _VerticesBuffer);
        _rayTracingMaterial.SetInt("NumVertices", vertices.Count);
        _rayTracingMaterial.SetBuffer("triangles", _TrianglesBuffer);
        _rayTracingMaterial.SetInt("NumTriangles", triangles.Count);

    }
}
