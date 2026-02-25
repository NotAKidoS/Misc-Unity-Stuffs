/*
MIT License

Copyright (c) 2025 NotAKidoS

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

// Originally hosted:
// https://gist.github.com/NotAKidoS/662e3b0160461866b4b62eaf911b147a/

// Skinned mesh will be cloned to this dedicated layer. Any camera which can see this layer will trigger head hiding.
// This will disable the unhiding by distance feature as the camera will see the clones regardless of distance.
// When this is disabled the clones will need to be iterated over when restoring source renderers to hide them again.
// #define USE_DEDICATED_CLONE_LAYER

// The above limitation can be fixed pretty easily if you wanted to have dedicated layer and distance based hiding,
// but I just don't care to bother. It would mean you iterate the clones only when unhiding by distance is needed.

// Cameras must be explicitly registered to be considered for head hiding.
// If this is disabled the main camera will be used by default. **Not compatible with dedicated clone layer mode.**
// #define USE_REGISTER_HEAD_HIDING_CAMERAS

// Unity things which I have hit in the past:
// https://issuetracker.unity3d.com/issues/changes-to-meshrenderer-dot-shadowcastingmode-dont-take-effect-immediately-when-being-made-in-onprerender
// https://issuetracker.unity3d.com/issues/kshadowcastingshadowsonly-objects-are-rendered-by-projectors-built-in-renderer

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

public class VisualClone : MonoBehaviour
{
#if USE_DEDICATED_CLONE_LAYER
    private const int VISUAL_CLONE_LAYER = 9;
#endif

#if USE_REGISTER_HEAD_HIDING_CAMERAS
    private static readonly HashSet<Camera> _headHiddenCameras = new();
    public static void RegisterHeadHidingCamera(Camera cam) => _headHiddenCameras.Add(cam);
    public static void UnregisterHeadHidingCamera(Camera cam) => _headHiddenCameras.Remove(cam);
#endif

#if !USE_DEDICATED_CLONE_LAYER
    public float HideHeadMinDistance = 1f;
#endif
    
    private struct RendererEntry
    {
        public Renderer r;                      
        public SkinnedMeshRenderer skinned;     
        public SkinnedMeshRenderer clone;       
        public GameObject go;             
        
        public float[] blendShapeWeights;       
        public Material[] localMaterials;       
        public int blendShapeCount;

        public bool castShadow;
        public bool isRendererToBeHidden;
        public bool isHeadHidden;
    }

    private bool _initialized;
    
    private RendererEntry[] _entries;
    private Transform _shrinkBone;
    private List<Renderer> _renderers;
    private HashSet<int> _hiddenTransforms;
    private List<Material> _workingSharedMaterials;
    private MaterialPropertyBlock _workingPropertyBlock;
    private bool _isInHeadHiddenState;
    private bool _needsCopyAnimatableProperties;
    
#if USE_REGISTER_HEAD_HIDING_CAMERAS
    private void Awake() => _headHiddenCameras.Add(Camera.main);
#endif

    private void Start()
    {
        if (!TryInitializeComponents())
        {
            enabled = false; // Disable if we cannot function
            return;
        }
        
        _initialized = true;

        BuildEntriesAndCreateClones();
        Camera.onPreCull += OnCameraPreCull;
    }

    private void OnEnable()
    {
        if (_initialized) Camera.onPreCull += OnCameraPreCull;
    }
    
    private void OnDisable() // Make sure to call RestoreHeadVisibleState if purposefully disabled
    {
        if (_initialized) Camera.onPreCull -= OnCameraPreCull;
    }

    private void LateUpdate()
    {
        // This flag prevents cameras rendering early in the frame triggering blendshape/material copying
        // before an animator or script has had a chance to update them for this frame.
        _needsCopyAnimatableProperties = true;
    }

    private void OnCameraPreCull(Camera cam)
    {
    #if !USE_DEDICATED_CLONE_LAYER && !USE_REGISTER_HEAD_HIDING_CAMERAS
        bool shouldEnter = cam.CompareTag("MainCamera");
    #elif USE_DEDICATED_CLONE_LAYER
        bool shouldEnter = (cam.cullingMask & (1 << VISUAL_CLONE_LAYER)) != 0;
    #elif USE_REGISTER_HEAD_HIDING_CAMERAS
        bool shouldEnter = _headHiddenCameras.Contains(cam);
    #endif

    #if !USE_DEDICATED_CLONE_LAYER
        if (_shrinkBone)
        {
            Vector3 diff = cam.transform.position - _shrinkBone.position;
            shouldEnter &= diff.sqrMagnitude <= HideHeadMinDistance * HideHeadMinDistance;
        }
    #endif

        if (!_needsCopyAnimatableProperties
            && shouldEnter == _isInHeadHiddenState)
            return;
        
        RendererEntry[] entries = _entries;
        int count = entries.Length;

        bool copyState = _needsCopyAnimatableProperties;
        MaterialPropertyBlock workingBlock = _workingPropertyBlock;
        List<Material> workingMaterials = _workingSharedMaterials;

        if (shouldEnter)
        {
            Profiler.BeginSample(nameof(VisualClone) + ".SetHeadHiddenState");

            for (int i = 0; i < count; i++)
            {
                ref RendererEntry e = ref entries[i];
                if (!e.isRendererToBeHidden) continue;
                
                GameObject go = e.go;
                if (!go || !go.activeInHierarchy) continue;
                
                Renderer r = e.r;
                if (!r.enabled) continue;
                
                e.isHeadHidden = true;
                
                if (e.castShadow) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly; 
                else r.forceRenderingOff = true;

                SkinnedMeshRenderer sk = e.skinned;
                if (sk is not null)
                {
                    SkinnedMeshRenderer clone = e.clone;

                    if (copyState)
                    {
                        CopyBlendShapes(sk, clone, e.blendShapeWeights, e.blendShapeCount);
                        CopyMaterialsAndProperties(r, clone, e.localMaterials, workingMaterials, workingBlock);
                    }

    #if !USE_DEDICATED_CLONE_LAYER
                    clone.forceRenderingOff = false;
    #endif
                }
            }

            _isInHeadHiddenState = true;
            _needsCopyAnimatableProperties = false;
            
            Profiler.EndSample();
        }
        else if (_isInHeadHiddenState)
        {
            Profiler.BeginSample(nameof(VisualClone) + ".RestoreHeadVisibleState");

            for (int i = 0; i < count; i++)
            {
                ref RendererEntry e = ref entries[i];
                
                if (!e.isHeadHidden) continue;
                e.isHeadHidden = false;
                
                if (e.castShadow) e.r.shadowCastingMode = ShadowCastingMode.On; 
                else e.r.forceRenderingOff = false;

    #if !USE_DEDICATED_CLONE_LAYER
                SkinnedMeshRenderer clone = e.clone;
                if (clone is not null) clone.forceRenderingOff = true;
    #endif
            }
            
            _isInHeadHiddenState = false;

            Profiler.EndSample();
        }
    }

    // This is public in case you need to touch the source renderers outside of camera culling events.
    // E.g. In CVR our clone system needs to force restore head hiding to properly copy the avatar renderers.
    // It will not incur any visible defect calling this outside of camera events, just a small performance cost.
    public void RestoreHeadVisibleState()
    {
        RendererEntry[] entries = _entries;
        int count = entries.Length;

        for (int i = 0; i < count; i++)
        {
            ref RendererEntry e = ref entries[i];

            if (!e.isHeadHidden) continue;
            e.isHeadHidden = false;

            if (e.castShadow) e.r.shadowCastingMode = ShadowCastingMode.On;
            else e.r.forceRenderingOff = false;

#if !USE_DEDICATED_CLONE_LAYER
            SkinnedMeshRenderer clone = e.clone;
            if (clone is not null) clone.forceRenderingOff = true;
#endif
        }
    }

    private bool TryInitializeComponents()
    {
        if (!TryGetComponent(out Animator animator))
            return false;

        Transform headBone = animator.GetBoneTransform(HumanBodyBones.Head);
        if (!headBone) return false;

        _renderers = new List<Renderer>();
        _workingPropertyBlock = new MaterialPropertyBlock();
        _workingSharedMaterials = new List<Material>();

        _shrinkBone = new GameObject(headBone.name + "_ShrinkBone").transform;
        _shrinkBone.SetParent(headBone, false); // passing false to keep zeroed local transform
        _shrinkBone.localScale = Vector3.zero;
        
        Transform[] headBoneChildren = headBone.GetComponentsInChildren<Transform>(true);
        _hiddenTransforms = new HashSet<int>(headBoneChildren.Length);
        foreach (Transform t in headBoneChildren) _hiddenTransforms.Add(t.GetHashCode());

        return true;
    }

    private void BuildEntriesAndCreateClones()
    {
        GetComponentsInChildren(true, _renderers);
        int rendererCount = _renderers.Count;
        _entries = new RendererEntry[rendererCount];

        for (int i = 0; i < rendererCount; i++)
        {
            Renderer render = _renderers[i];
            GameObject renderGameObject = render.gameObject;
            
            RendererEntry e = default;
            e.r = render;
            e.go = renderGameObject;
            
            e.castShadow = render.shadowCastingMode != ShadowCastingMode.Off;

            bool entirelyHidden = _hiddenTransforms.Contains(renderGameObject.transform.GetHashCode());
            if (entirelyHidden)
            {
                e.isRendererToBeHidden = true;
            }
            else if (render is SkinnedMeshRenderer smr)
            {
                bool needsHiding = false;
                
                Transform[] bones = smr.bones;
                int bonesCount = bones.Length;
                for (int b = 0; b < bonesCount; b++)
                {
                    Transform bone = bones[b];
                    if (bone is not null && _hiddenTransforms.Contains(bone.GetHashCode()))
                    {
                        bones[b] = _shrinkBone;
                        needsHiding = true;
                    }
                }

                e.isRendererToBeHidden = needsHiding;

                if (needsHiding)
                {
                    GameObject cloneObject = new(render.name + "_VisualClone")
                    {
#if USE_DEDICATED_CLONE_LAYER
                        layer = VISUAL_CLONE_LAYER
#else
                        layer = renderGameObject.layer
#endif
                    };

                    Transform t = cloneObject.transform;
                    t.SetParent(render.transform, false); // passing false to keep zeroed local transform

                    SkinnedMeshRenderer cloneMesh = cloneObject.AddComponent<SkinnedMeshRenderer>();

                    Mesh sharedMesh = smr.sharedMesh;
                    int blendShapeCount = sharedMesh ? sharedMesh.blendShapeCount : 0;
                    if (blendShapeCount > 0)
                    {
                        e.blendShapeCount = blendShapeCount;
                        e.blendShapeWeights = new float[blendShapeCount];
                    }
                    
                    cloneMesh.rootBone = smr.rootBone;
                    cloneMesh.bones = bones;
                    cloneMesh.quality = smr.quality;
                    cloneMesh.updateWhenOffscreen = smr.updateWhenOffscreen;
                    cloneMesh.localBounds = smr.localBounds;
                    cloneMesh.shadowCastingMode = ShadowCastingMode.Off;
                    cloneMesh.probeAnchor = smr.probeAnchor;
                    cloneMesh.sharedMesh = sharedMesh; // Setting after bones & bounds as I think it may skip some validations?
                    
                    Material[] sharedMaterials = smr.sharedMaterials;
                    cloneMesh.sharedMaterials = sharedMaterials;
                    
                    e.localMaterials = sharedMaterials;
                    e.skinned = smr;
                    e.clone = cloneMesh;
                }
            }

            _entries[i] = e;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CopyMaterialsAndProperties(
        Renderer source,
        Renderer target,
        Material[] localMaterials,
        List<Material> workingSharedMaterials,
        MaterialPropertyBlock workingPropertyBlock)
    {
        Profiler.BeginSample(nameof(VisualClone) + "." + nameof(CopyMaterialsAndProperties));

        source.GetSharedMaterials(workingSharedMaterials);

        bool hasChanged = false;
        int count = workingSharedMaterials.Count;
        for (int i = 0; i < count; i++)
        {
            Material m = workingSharedMaterials[i];
            if (ReferenceEquals(m, localMaterials[i])) continue;
            localMaterials[i] = m;
            hasChanged = true;
        }

        if (hasChanged) target.sharedMaterials = localMaterials;

        source.GetPropertyBlock(workingPropertyBlock);
        target.SetPropertyBlock(workingPropertyBlock);

        Profiler.EndSample();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CopyBlendShapes(
        SkinnedMeshRenderer source,
        SkinnedMeshRenderer target,
        float[] blendShapeWeights,
        int blendShapeCount)
    {
        Profiler.BeginSample(nameof(VisualClone) + "." + nameof(CopyBlendShapes));

        for (int i = 0; i < blendShapeCount; i++)
        {
            float weight = source.GetBlendShapeWeight(i);
            if (weight == blendShapeWeights[i]) continue; // halves the work
            target.SetBlendShapeWeight(i, blendShapeWeights[i] = weight);
        }

        Profiler.EndSample();
    }

    /*
    // Touching RendererEntry in the inspector causes C# nulls to be replaced with fake Unity nulls.
    // (temp made RendererEntry public and serializable for validating stuff)
    private void OnValidate()
    {
        RendererEntry[] entries = _entries;
        int count = entries.Length;
        for (int i = 0; i < count; i++)
        {
            ref RendererEntry e = ref entries[i];

            if (!e.r) e.r = null;
            if (!e.skinned) e.skinned = null;
            if (!e.clone) e.clone = null;
            if (!e.go) e.go = null;
        }
    }
    */
}