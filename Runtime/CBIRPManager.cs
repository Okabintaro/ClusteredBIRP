﻿using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

#if UNITY_EDITOR && !COMPILER_UDONSHARP
using UnityEditor;
#endif

namespace CBIRP
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class CBIRPManager : UdonSharpBehaviour
    {
        public float cullFar = 100f;
        public CustomRenderTexture clustering;
        public CustomRenderTexture uniforms;
        public Texture2D shadowmask;
        public CubemapArray reflectionProbeArray;
        //public Cubemap skyProbe;
        [SerializeField] private Camera _trackingCamera;
        [Tooltip("Enable updates to any of the light or probe variables at runtime (Position, Rotation, Color, Range etc). Disable to skip the additional camera used to track them")]
        [SerializeField] private bool _dynamicUpdates = true;

        private void Start()
        {
            SetGlobalUniforms();
            SetDynamicUpdatesState(_dynamicUpdates);
        }

        public void SetGlobalUniforms()
        {
            uniforms.material.SetFloat("_Far", cullFar);
            VRCShader.SetGlobalTexture(VRCShader.PropertyToID("_Udon_CBIRP_Uniforms"), uniforms);
            VRCShader.SetGlobalTexture(VRCShader.PropertyToID("_Udon_CBIRP_Culling"), clustering);
            VRCShader.SetGlobalTexture(VRCShader.PropertyToID("_Udon_CBIRP_ShadowMask"), shadowmask);
            VRCShader.SetGlobalTexture(VRCShader.PropertyToID("_Udon_CBIRP_ReflectionProbes"), reflectionProbeArray);
            //VRCShader.SetGlobalTexture(VRCShader.PropertyToID("_Udon_CBIRP_SkyProbe"), skyProbe);
        }

        public void SetDynamicUpdatesState(bool isEnabled)
        {
            SendCustomEventDelayedFrames(
                isEnabled ? nameof(EnableDynamicUpdates) : nameof(DisableDynamicUpdates),
                5, VRC.Udon.Common.Enums.EventTiming.Update);
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
            if (GUILayout.Button("Pack Probes"))
            {
                CBIRPManagerEditor.PackProbes((CBIRPManager)target);
            }
        }
    }

#endif
}