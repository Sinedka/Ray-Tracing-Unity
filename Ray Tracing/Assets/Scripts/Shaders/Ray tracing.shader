Shader "Custom/RayTracing"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            struct RayTracingMaterial
            {
				float4 colour;
				float4 emissionColour;
				float4 specularColour;
				float emissionStrength;
				float smoothness;
				float specularProbability;
				int flag;
            };

            struct MeshInfo
            {
            	int triangleStartIndex;
				int triangleCount;
            	RayTracingMaterial material;
            	float3 boundsMin;
				float3 boundsMax;
            };
            
            struct Sphere
			{
				float3 position;
				float radius;
            	RayTracingMaterial material;
			};

            struct HitInfo
			{
				bool didHit;
				float dst;
				float3 hitPoint;
				float3 normal;
            	RayTracingMaterial material;
			};

            struct Ray
            {
                float3 origin;
                float3 dir;
            };

            float3 ViewParams;
			float4x4 CamLocalToWorldMatrix;
            StructuredBuffer<Sphere> Spheres;
            int NumSpheres;
            StructuredBuffer<MeshInfo> meshInfos;
            int NumMeshInfos;
            StructuredBuffer<float3> vertices;
            int NumVertices;
            StructuredBuffer<int> triangles;
            int NumTriangles;
            
            int Frame;
            int NumRaysPerPixel;

            uint NextRandom(inout uint state)
			{
				state = state * 747796405 + 2891336453;
				uint result = ((state >> ((state >> 28) + 4)) ^ state) * 277803737;
				result = (result >> 22) ^ result;
				return result;
			}

			float RandomValue(inout uint state)
			{
				return NextRandom(state) / 4294967295.0; // 2^32 - 1
			}

            float RandomValueNormalDistribution(inout uint state)
			{
				// Thanks to https://stackoverflow.com/a/6178290
				float theta = 2 * 3.1415926 * RandomValue(state);
				float rho = sqrt(-2 * log(RandomValue(state)));
				return rho * cos(theta);
			}

			// Calculate a random direction
			float3 RandomDirection(inout uint state)
			{
				// Thanks to https://math.stackexchange.com/a/1585996
				float x = RandomValueNormalDistribution(state);
				float y = RandomValueNormalDistribution(state);
				float z = RandomValueNormalDistribution(state);
				return normalize(float3(x, y, z));
			}

            float3 RandomHemisphereDirection(float3 normal, inout uint rngState)
            {
	            float3 dir = RandomDirection(rngState);
            	return dir * sign(dot(normal, dir));
            }

            // Thanks to https://gist.github.com/DomNomNom/46bb1ce47f68d255fd5d
			bool RayBoundingBox(Ray ray, float3 boxMin, float3 boxMax)
			{
				float3 invDir = 1 / ray.dir;
				float3 tMin = (boxMin - ray.origin) * invDir;
				float3 tMax = (boxMax - ray.origin) * invDir;
				float3 t1 = min(tMin, tMax);
				float3 t2 = max(tMin, tMax);
				float tNear = max(max(t1.x, t1.y), t1.z);
				float tFar = min(min(t2.x, t2.y), t2.z);
				return tNear <= tFar;
			};

            HitInfo RaySphere(Ray ray, float3 sphereCentre, float sphereRadius)
			{
				HitInfo hitInfo = (HitInfo)0;
				float3 offsetRayOrigin = ray.origin - sphereCentre;
				// From the equation: sqrLength(rayOrigin + rayDir * dst) = radius^2
				// Solving for dst results in a quadratic equation with coefficients:
				float a = dot(ray.dir, ray.dir); // a = 1 (assuming unit vector)
				float b = 2 * dot(offsetRayOrigin, ray.dir);
				float c = dot(offsetRayOrigin, offsetRayOrigin) - sphereRadius * sphereRadius;
				// Quadratic discriminant
				float discriminant = b * b - 4 * a * c; 

				// No solution when d < 0 (ray misses sphere)
				if (discriminant >= 0) {
					// Distance to nearest intersection point (from quadratic formula)
					float dst = (-b - sqrt(discriminant)) / (2 * a);

					// Ignore intersections that occur behind the ray
					if (dst >= 0) {
						hitInfo.didHit = true;
						hitInfo.dst = dst;
						hitInfo.hitPoint = ray.origin + ray.dir * dst;
						hitInfo.normal = normalize(hitInfo.hitPoint - sphereCentre);
					}
				}
				return hitInfo;
			}

            HitInfo Raytriangle(Ray ray,float3 p1, float3 p2,float3 p3)
            {
				float3 edgeAB = p2 - p1;
				float3 edgeAC = p3 - p1;
				float3 normalVector = cross(edgeAB, edgeAC);
				float3 ao = ray.origin - p1;
				float3 dao = cross(ao, ray.dir);

				float determinant = -dot(ray.dir, normalVector);
				float invDet = 1 / determinant;
				// Calculate dst to triangle & barycentric coordinates of intersection point
				float dst = dot(ao, normalVector) * invDet;
				float u = dot(edgeAC, dao) * invDet;
				float v = -dot(edgeAB, dao) * invDet;
				float w = 1 - u - v;
				
				// Initialize hit info
				HitInfo hitInfo;
				hitInfo.didHit = determinant >= 1E-6 && dst >= 0 && u >= 0 && v >= 0 && w >= 0;
				hitInfo.hitPoint = ray.origin + ray.dir * dst;
            	hitInfo.normal = float3(edgeAB.y * edgeAC.z - edgeAB.z * edgeAC.y, edgeAB.z * edgeAC.x - edgeAB.x * edgeAC.z, edgeAB.x * edgeAC.y - edgeAB.y * edgeAC.x);
				hitInfo.dst = dst;
				return hitInfo;
            }
            
            HitInfo RayMesh(Ray ray, MeshInfo mesh)
            {
	            HitInfo hitInfo = (HitInfo)0;
            	hitInfo.dst = 1.#INF;
            	
            	for(int i = mesh.triangleStartIndex; i < mesh.triangleStartIndex + mesh.triangleCount; i += 3)
            	{
            		HitInfo hitInfo1 = Raytriangle(ray, vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]]);
            		if (hitInfo1.didHit && hitInfo1.dst < hitInfo.dst)
					{
						hitInfo = hitInfo1;
					}
            	}

            	hitInfo.material = mesh.material;
            	
            	return hitInfo;
            }
            
            HitInfo calculateRayCollision(Ray ray)
            {
            	HitInfo closestHit = (HitInfo)0;
				// We haven't hit anything yet, so 'closest' hit is infinitely far away
				closestHit.dst = 1.#INF;

				// Raycast against all spheres and keep info about the closest hit
				for (int i = 0; i < NumSpheres; i ++)
				{
					Sphere sphere = Spheres[i];
					HitInfo hitInfo = RaySphere(ray, sphere.position, sphere.radius);

					if (hitInfo.didHit && hitInfo.dst < closestHit.dst)
					{
						closestHit = hitInfo;
						closestHit.material = sphere.material;
					}
				}

            	for (int i = 0; i < NumMeshInfos; i++)
            	{
            		MeshInfo meshInfo = meshInfos[i];
            		if (!RayBoundingBox(ray, meshInfo.boundsMin, meshInfo.boundsMax)) {
						continue;
					}
            		HitInfo hitInfo = RayMesh(ray, meshInfo);
            		if (hitInfo.didHit && hitInfo.dst < closestHit.dst)
					{
						closestHit = hitInfo;
					}
            	}

            	return closestHit;
            }

            float3 Trace(Ray ray, inout uint rngState)
			{
				float3 incomingLight = 0;
            	float3 rayColour = 1;

				for (int bounceIndex = 0; bounceIndex <= 10; bounceIndex ++)
				{
					HitInfo hitInfo = calculateRayCollision(ray);

					if (hitInfo.didHit)
					{
						RayTracingMaterial Mat = hitInfo.material;
						bool isSpecularBounce = Mat.specularProbability >= RandomValue(rngState);
						ray.origin = hitInfo.hitPoint;
						float3 diffuseDir = normalize(hitInfo.normal + RandomDirection(rngState));
						//float3 diffuseDir = normalize(hitInfo.normal + RandomDirection(rngState));
						//float3 diffuseDir = RandomHemisphereDirection(hitInfo.normal, rngState);
						float3 specularDir = reflect(ray.dir, hitInfo.normal);
						ray.dir = normalize(lerp(diffuseDir, specularDir, Mat.smoothness * isSpecularBounce));
						
						float3 emittedLight = Mat.emissionColour * Mat.emissionStrength;
						incomingLight += emittedLight * rayColour;
						rayColour *= lerp(Mat.colour, Mat.specularColour, isSpecularBounce);
					}
					else
					{
						incomingLight += 0 * rayColour;
						break;
					}
				}

				return incomingLight;
			}

			float4 frag (v2f i) : SV_Target
			{
				// Create seed for random number generator
				uint2 numPixels = _ScreenParams.xy;
				uint2 pixelCoord = i.uv * numPixels;
				uint pixelIndex = pixelCoord.y * numPixels.x + pixelCoord.x;
				uint rngState = pixelIndex + Frame * 719393;

				// Calculate focus point
				float3 viewPointLocal = float3(i.uv - 0.5, 1) * ViewParams;
				float3 viewPoint = mul(CamLocalToWorldMatrix, float4(viewPointLocal, 1));

				// Trace a bunch of rays and average the result
				Ray ray;
				ray.origin = _WorldSpaceCameraPos;
				ray.dir = normalize(viewPoint - ray.origin);

				float3 totalIncomingLight = 0;
				
				for (int j = 0; j < NumRaysPerPixel; j++)
				{
					totalIncomingLight += Trace(ray, rngState);
				}
				
				float3 pixelCol = totalIncomingLight / NumRaysPerPixel;
				
				return float4(pixelCol, 1);
			}
            ENDCG
        }
    }
}
