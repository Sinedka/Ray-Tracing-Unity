using System;
using Unity.VisualScripting;
using UnityEngine;

[ExecuteAlways]

public class RayTracedMesh : MonoBehaviour
{
	[Header("Settings")]
	public RayTracingMaterial material;
	[Header("Info")]
	//public MeshRenderer meshRenderer;
	[SerializeField] MeshFilter meshFilter;
	public int[] triangles;
	public Vector3[] vertices;
	[SerializeField] Mesh mesh;
	public Vector3 boundsMin;
	public Vector3 boundsMax;

	private void OnValidate()
	{
		//meshRenderer = GetComponent<MeshRenderer>();
		meshFilter = GetComponent<MeshFilter>();
		mesh = meshFilter.sharedMesh;
		triangles = mesh.triangles;
		vertices = localToWorldVertices(mesh.vertices);
		bounds();

	}

	private Vector3[] localToWorldVertices(Vector3[] LocalVertices)
	{
		Vector3[] ans = LocalVertices;
		for(int i = 0; i < ans.Length; i++)
		{
			ans[i] = localToWorldVector(ans[i]);
		}
		
		return ans;

	}

	private Vector3 localToWorldVector(Vector3 vec)
	{
		vec = transform.rotation * vec;
		Vector3 scale = transform.localScale;
		vec = new Vector3(scale.x * vec.x, scale.y * vec.y, scale.z * vec.z);
		vec += transform.position;
		return vec;
	}

	private void bounds()
	{
		foreach (Vector3 vertex in vertices)
		{
			boundsMin = new Vector3(Math.Min(boundsMin.x, vertex.x), Math.Min(boundsMin.y, vertex.y),
				Math.Min(boundsMin.z, vertex.z));
			boundsMax = new Vector3(Math.Max(boundsMax.x, vertex.x), Math.Max(boundsMax.y, vertex.y),
				Math.Max(boundsMax.z, vertex.z));
		}
	}
	
}
