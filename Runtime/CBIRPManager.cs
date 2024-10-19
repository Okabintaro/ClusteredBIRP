using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using UnityEngine.SceneManagement;
using System.IO;

#if UNITY_EDITOR && !COMPILER_UDONSHARP
using UnityEditor;
using System.Collections.Generic;
#endif

namespace CBIRP
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class CBIRPManager : UdonSharpBehaviour
    {
        public float cullFar = 100f;
        public CustomRenderTexture clustering;
        public RenderTexture uniforms;
        public Texture2D shadowmask;
        //public Texture2D generatedIesLut;
        public CubemapArray reflectionProbeArray;
        //public Cubemap skyProbe;
        [SerializeField] private Camera _trackingCamera;
        [Tooltip("Enable updates to any of the light or probe variables at runtime (Position, Rotation, Color, Range etc). Disable to skip the additional camera used to track them")]
        [SerializeField] private bool _dynamicUpdates = true;
        [Range(0, 5)] public int probeBounces = 1;
        public int probeResolution = 128;
        //public int probeMultiSampling = 1;

        private int _cbirpPlayerPositionID;
        private VRCPlayerApi _localPlayer;
        private void Start()
        {
            SetGlobalUniforms();
            SetDynamicUpdatesState(_dynamicUpdates);
            _localPlayer = Networking.LocalPlayer;
            _cbirpPlayerPositionID = VRCShader.PropertyToID("_Udon_CBIRP_PlayerPosition");
        }
        
        private void Update()
        {
            // an update loop had to be added just in case it breaks in the future as its broken in editor in 2022
            // getting the main camera position is not very accurate in custom render textures
            // also saves 1 extra texture sample previously used
            Vector4 pos = _localPlayer.GetPosition();
            pos.w = cullFar;
            VRCShader.SetGlobalVector(_cbirpPlayerPositionID, pos);
        }

        public void SetGlobalUniforms()
        {
            VRCShader.SetGlobalTexture(VRCShader.PropertyToID("_Udon_CBIRP_Uniforms"), uniforms);
            VRCShader.SetGlobalTexture(VRCShader.PropertyToID("_Udon_CBIRP_Clusters"), clustering);
            VRCShader.SetGlobalTexture(VRCShader.PropertyToID("_Udon_CBIRP_ShadowMask"), shadowmask);
            VRCShader.SetGlobalTexture(VRCShader.PropertyToID("_Udon_CBIRP_ReflectionProbes"), reflectionProbeArray);
            //VRCShader.SetGlobalTexture(VRCShader.PropertyToID("_Udon_CBIRP_IES"), generatedIesLut);

            //VRCShader.SetGlobalTexture(VRCShader.PropertyToID("_Udon_CBIRP_SkyProbe"), skyProbe);
        }

        public void SetDynamicUpdatesState(bool isEnabled)
        {
            SendCustomEventDelayedFrames(
                isEnabled ? nameof(EnableDynamicUpdates) : nameof(DisableDynamicUpdates),
                2, VRC.Udon.Common.Enums.EventTiming.Update);
        }
        public void DisableDynamicUpdates() => _trackingCamera.gameObject.SetActive(false);
        public void EnableDynamicUpdates() => _trackingCamera.gameObject.SetActive(true);

#if UNITY_EDITOR && !COMPILER_UDONSHARP
        public void OnValidate()
        {
            SetGlobalUniforms();
        }
#endif
    }


#if UNITY_EDITOR && !COMPILER_UDONSHARP
    [CustomEditor(typeof(CBIRPManager))]
    class CBIRPManagerInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            if (GUILayout.Button("Bake And Pack Reflection Probes"))
            {
                // TODO: Specular toggle
                sceneLights = FindObjectsOfType<Light>();
                PlaceEmissives();
                var m = (CBIRPManager)target;
                CBIRPManagerEditor.ClearProbes(m);
                CBIRPManagerEditor.BakeAndPackProbes(m, m.probeBounces, m.probeResolution);
                DestroyEmissives();
            }
        }

        // Bake specular probes
        // Code mostly taken and ported from Specular Probes by frostbone25:
        // https://github.com/frostbone25/Unity-Specular-Probes
        //
        //        MIT License
        //
        // Copyright (c) 2022 David Matos
        //
        // Permission is hereby granted, free of charge, to any person obtaining a copy
        // of this software and associated documentation files (the "Software"), to deal
        // in the Software without restriction, including without limitation the rights
        // to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
        // copies of the Software, and to permit persons to whom the Software is
        // furnished to do so, subject to the following conditions:
        //
        // The above copyright notice and this permission notice shall be included in all
        // copies or substantial portions of the Software.
        //
        // THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
        // IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
        // FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
        // AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
        // LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
        // OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
        // SOFTWARE.

        private Light[] sceneLights;

        //options
        private bool includeRealtimeLights = false;
        private bool includeMixedLights = false;
        private bool includeBakedLights = true;

        private bool includeAreaLights = true;
        private bool includePointLights = true;
        private bool includeSpotLights = true;

        private float point_lightSize = 0.1f;
        private float point_lightIntensityMultiplier = 1.0f;

        private float spot_lightSize = 0.1f;
        private float spot_lightIntensityMultiplier = 1.0f;

        private bool area_doubleSided = false;
        private float area_lightThickness = 0.01f;
        private float area_lightIntensityMultiplier = 1.0f;

        //main logic
        private List<GameObject> specularObjects = new List<GameObject>();

        /// <summary>
        /// Spawns an emissive mesh object for each light.
        /// </summary>
        private void PlaceEmissives()
        {
            //iterate through all scene lights in the scene
            //note: sceneLights is filled in OnGUI
            foreach (Light light in sceneLights)
            {
                //get our cases for the lights that we are including in the specular probe bakes.
                bool case1 = light.lightmapBakeType == LightmapBakeType.Baked && includeBakedLights;
                bool case2 = light.lightmapBakeType == LightmapBakeType.Mixed && includeMixedLights;
                bool case3 = light.lightmapBakeType == LightmapBakeType.Realtime && includeRealtimeLights;

                //if the current light passes any of the cases
                if ((case1 || case2 || case3) && light.intensity > 0.0f)
                {
                    //spawn a corresponding emissive to match its light source and add it to 'specularObjects'.

                    if (light.type == UnityEngine.LightType.Point && includePointLights)
                        GetPointLightMesh(light);

                    if (light.type == UnityEngine.LightType.Spot && includeSpotLights)
                        GetSpotLightMesh(light);

                    if (light.type == UnityEngine.LightType.Area && includeAreaLights)
                        GetAreaLightMesh(light);
                }
            }
        }

        /// <summary>
        /// Destroys all emissive mesh objects.
        /// </summary>
        private void DestroyEmissives()
        {
            //destroy all specular objects in the array that were spawned.
            for (int i = 0; i < specularObjects.Count; i++)
                DestroyImmediate(specularObjects[i]);

            //clear the array.
            specularObjects.Clear();
        }

        private void GetPointLightMesh(Light light)
        {
            GameObject sphereMeshObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            SetupMesh(sphereMeshObject, light, point_lightIntensityMultiplier, new Vector3(point_lightSize, point_lightSize, point_lightSize));
        }

        private void GetSpotLightMesh(Light light)
        {
            GameObject spotMeshObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            SetupMesh(spotMeshObject, light, spot_lightIntensityMultiplier, new Vector3(spot_lightSize, spot_lightSize, spot_lightSize));
        }

        private void GetAreaLightMesh(Light light)
        {
            GameObject areaMeshObject = GameObject.CreatePrimitive(area_doubleSided ? PrimitiveType.Cube : PrimitiveType.Quad);
            SetupMesh(areaMeshObject, light, area_lightIntensityMultiplier, new Vector3(light.areaSize.x, light.areaSize.y, area_doubleSided ? area_lightThickness : -1.0f));
        }

        /// <summary>
        /// Create an emissive material to emulate the light source.
        /// </summary>
        /// <param name="color"></param>
        /// <param name="intensity"></param>
        /// <returns></returns>
        private static Material GetLightMaterial(Color color, float intensity)
        {
            Material material = new Material(Shader.Find("Custom/EmissiveOnly"));
            material.SetColor("_EmissionColor", color);
            material.SetFloat("_EmissionIntensity", intensity);
            return material;
        }


        /// <summary>
        /// Spawn a gameobject with the light emissive material, and the mesh to match the shape of the light and add it to specularOjects.
        /// </summary>
        /// <param name="meshObject"></param>
        /// <param name="light"></param>
        /// <param name="multiplier"></param>
        /// <param name="size"></param>
        private void SetupMesh(GameObject meshObject, Light light, float multiplier, Vector3 size)
        {
            MeshRenderer meshRenderer = meshObject.GetComponent<MeshRenderer>();

            meshRenderer.sharedMaterial = GetLightMaterial(light.color, light.intensity * multiplier);
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.allowOcclusionWhenDynamic = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            meshObject.transform.localScale = size;
            meshObject.transform.position = light.transform.position;
            meshObject.transform.rotation = light.transform.rotation;

            GameObjectUtility.SetStaticEditorFlags(meshObject, StaticEditorFlags.ReflectionProbeStatic);

            specularObjects.Add(meshObject);
        }

    }
#endif
}