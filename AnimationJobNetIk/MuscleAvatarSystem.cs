// #define MELON_MOD

#if CVR_CLIENT
using ABI_RC.Core.Player;
using ABI_RC.Systems.GameEventSystem;
using ABI.CCK.Components;
#endif

using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Experiment in using Animation Jobs to apply muscle values to avatars over network.
// Interpolation happens on worker threads over the frame and applies a frame late so it should be
// practically free. This solves a few issues but there are also some more introduced.

// Tbh while there was at least a 20% improvement at scale it was not the magic bullet I thought
// it would be over the current implementation. Animators kind of suck and the graph director
// will always seem to be the biggest hit when profiling.

// The culling mode change though was the big winner. As long as an avatar was off-screen
// it became practically free, and we didn't apply netik due to it being part of the animation stream.
// This is still probably worth using but Zettai's thing is just cooler.

namespace NAK.AnimationJobNetIk
{
    public class MuscleAvatarSystem : MonoBehaviour
    {
        public static MuscleAvatarSystem Instance;

        [SerializeField] private int maxPlayers = 128;

        private PlayableGraph graph;

        private readonly Dictionary<string,int> guidToSlice = new();
        private readonly Dictionary<string,AnimationScriptPlayable> playables = new();
        private readonly Dictionary<string,AnimationPlayableOutput> outputs = new();
        private readonly Dictionary<string,Animator> animators = new(); // Track animators for re-attachment
        private readonly Stack<int> freeSlots = new();

        private bool systemEnabled = true;
        private AnimatorCullingMode currentCullingMode = AnimatorCullingMode.CullCompletely;

        private void Awake()
        {
            if (Instance != null
                && Instance != this) 
            {
                Destroy(this);
                return;
            }
            Instance = this;

            MuscleStore.Init(maxPlayers);

            graph = PlayableGraph.Create("MuscleAvatarSystem.Graph");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            graph.Play();
            
    #if CVR_CLIENT
            CVRGameEventSystem.Player.OnJoinEntity.AddListener(OnPlayerJoin);
            CVRGameEventSystem.Player.OnLeaveEntity.AddListener(OnPlayerLeft);
            CVRGameEventSystem.Avatar.OnRemoteAvatarLoad.AddListener(OnRemoteAvatarLoad);
            CVRGameEventSystem.Avatar.OnRemoteAvatarClear.AddListener(OnRemoteAvatarClear);
    #endif

    #if MELON_MOD
            
            // Subscribe to mod settings
            NAK.PlayableNetIk.PlayableNetIkMod.EntryEnablePlayableNetIk.OnEntryValueChanged.Subscribe(
                (oldValue, newValue) => SetSystemEnabled(newValue)
            );

            // Subscribe to culling mode changes
            NAK.PlayableNetIk.PlayableNetIkMod.EntryEnableFullAnimatorCulling.OnEntryValueChanged.Subscribe(
                (oldValue, newValue) => SetCullingMode(newValue 
                    ? AnimatorCullingMode.CullCompletely 
                    : AnimatorCullingMode.CullUpdateTransforms)
            );
            
            // Apply initial culling mode
            SetCullingMode(NAK.PlayableNetIk.PlayableNetIkMod.EntryEnableFullAnimatorCulling.Value 
                ? AnimatorCullingMode.CullCompletely 
                : AnimatorCullingMode.CullUpdateTransforms);
            
            // Apply initial enabled state
            SetSystemEnabled(NAK.PlayableNetIk.PlayableNetIkMod.EntryEnablePlayableNetIk.Value);
    #endif
        }
        
        public void SetSystemEnabled(bool settingEnabled)
        {
            if (systemEnabled == settingEnabled) return;
            systemEnabled = settingEnabled;

            // Hacky shit so can toggle the mod for profiling
            if (settingEnabled)
            {
                // Re-attach all registered avatars
                List<KeyValuePair<string, Animator>> animatorsToAttach = new List<KeyValuePair<string, Animator>>(animators);
                foreach (var kvp in animatorsToAttach)
                    if (kvp.Value) AttachAvatar(kvp.Key, kvp.Value);
            }
            else
            {
                // Detach all avatars
                List<string> guidsToDetach = new List<string>(playables.Keys);
                foreach (string guid in guidsToDetach) DetachAvatarInternal(guid);
            }
        }

        // This is where the majority of perf seems to come from, and tbh,
        // your avatar should survive being culled if distance hiding is a thing already.
        public void SetCullingMode(AnimatorCullingMode mode)
        {
            currentCullingMode = mode;
            foreach (var kvp in animators)
                if (kvp.Value) kvp.Value.cullingMode = currentCullingMode;
        }
        
    #if CVR_CLIENT
        private void OnPlayerJoin(CVRPlayerEntity entity) 
            => RegisterPlayer(entity.Uuid);
        private void OnPlayerLeft(CVRPlayerEntity entity) 
            => UnregisterPlayer(entity.Uuid);
        private void OnRemoteAvatarLoad(CVRPlayerEntity entity, CVRAvatar avatar) 
            => AttachAvatar(entity.Uuid, avatar.GetComponent<Animator>());
        private void OnRemoteAvatarClear(CVRPlayerEntity entity, CVRAvatar avatar)
            => DetachAvatar(entity.Uuid);
    #endif

        private void Update()
        {
            if (!systemEnabled) return;

            int n = MuscleStore.HighWaterMark;
            if (n == 0) return;

            MuscleStore.Job = new InterpJob
            {
                PrevM = MuscleStore.PrevM,
                TarM = MuscleStore.TarM,
                OutM = MuscleStore.OutM,
                PrevP = MuscleStore.PrevP,
                TarP = MuscleStore.TarP,
                OutP = MuscleStore.OutP,
                PrevR = MuscleStore.PrevR,
                TarR = MuscleStore.TarR,
                OutR = MuscleStore.OutR,
                LastTime = MuscleStore.LastTime,
                PrevTime = MuscleStore.PrevTime,
                CurrentTime = Time.time,
                ActiveSlots = MuscleStore.ActiveSlots
            }.Schedule(n, 32);
        }

        private void LateUpdate()
        {
            if (!systemEnabled) return;
            MuscleStore.Job.Complete();
        }

        public void RegisterPlayer(string guid)
        {
            if (guidToSlice.ContainsKey(guid)) return;
            int slot = freeSlots.Count > 0 ? freeSlots.Pop() : MuscleStore.Register();
            MuscleStore.ActivateSlot(slot);
            guidToSlice[guid] = slot;
        }

        public void UnregisterPlayer(string guid)
        {
            if (!guidToSlice.Remove(guid, out int s)) return;
            MuscleStore.DeactivateSlot(s);
            freeSlots.Push(s);
            DetachAvatar(guid);
            animators.Remove(guid); // Remove from tracked animators
        }

        public void OnPacket(string guid, Vector3 pos, Vector3 rotEuler, float[] muscles)
        {
            if (!guidToSlice.TryGetValue(guid, out int s)) return;
            MuscleStore.WritePacket(s, pos, rotEuler, muscles);
        }

        public void AttachAvatar(string guid, Animator animator)
        {
            if (!guidToSlice.TryGetValue(guid, out int s)) return;
            if (!animator || !animator.isHuman) return;

            // Always track the animator
            animators[guid] = animator;

            // Only attach if system is enabled
            if (!systemEnabled) return;

            MuscleWriteJob job = new()
            {
                M = MuscleStore.OutM,  // Read from out buffer
                P = MuscleStore.OutP,
                R = MuscleStore.OutR,
                H = MuscleStore.Handles,
                Hip = animator.BindStreamTransform(animator.GetBoneTransform(HumanBodyBones.Hips)),
                s = s
            };
            
            AnimatorControllerPlayable controller = AnimatorControllerPlayable.Create(graph, animator.runtimeAnimatorController);
            animator.runtimeAnimatorController = null; // disable the Animator's built-in controller
            
            AnimationScriptPlayable playable = AnimationScriptPlayable.Create(graph, job);
            playable.SetProcessInputs(true);
            playable.SetInputCount(1);
            
            graph.Connect(controller, 0, playable, 0);
            playable.SetInputWeight(0, 1f);
            
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, $"Avatar_{guid}", animator);
            output.SetSourcePlayable(playable);

            graph.Play();
            
            playables[guid] = playable;
            outputs[guid] = output;

            animator.cullingMode = currentCullingMode;
        }

        public void DetachAvatar(string guid)
        {
            DetachAvatarInternal(guid);
            animators.Remove(guid);
        }

        private void DetachAvatarInternal(string guid)
        {
            if (outputs.TryGetValue(guid, out AnimationPlayableOutput o))
            {
                if (graph.IsValid()) graph.DestroyOutput(o);
                outputs.Remove(guid);
            }

            if (playables.TryGetValue(guid, out AnimationScriptPlayable p))
            {
                if (p.IsValid()) p.Destroy();
                playables.Remove(guid);
            }
        }

        private void OnDestroy()
        {
            MuscleStore.Shutdown();
            if (graph.IsValid()) graph.Destroy();
            Instance = null;
        }
    }

    public static class MuscleStore
    {
        public const int Body = 55;
        public const int Fingers = 40;
        public const int MCount = Body + Fingers;
        
        public static NativeArray<MuscleHandle> Handles;

        // Previous packet (source)
        public static NativeArray<float> PrevM;
        public static NativeArray<float3> PrevP;
        public static NativeArray<quaternion> PrevR;
        
        // Current packet (target)
        public static NativeArray<float> TarM;
        public static NativeArray<float3> TarP;
        public static NativeArray<quaternion> TarR;
        
        // Interpolated output (fed to animation)
        public static NativeArray<float> OutM;
        public static NativeArray<float3> OutP;
        public static NativeArray<quaternion> OutR;

        public static NativeArray<float> LastTime;
        public static NativeArray<float> PrevTime;
        public static NativeArray<bool> ActiveSlots;

        public static int HighWaterMark;
        public static JobHandle Job;

        public static void Init(int max)
        {
            Handles = new NativeArray<MuscleHandle>(HumanTrait.MuscleCount, Allocator.Persistent);
            var tmp = new MuscleHandle[HumanTrait.MuscleCount];
            MuscleHandle.GetMuscleHandles(tmp);
            for (int i = 0; i < tmp.Length; i++) Handles[i] = tmp[i];
            
            PrevM = new NativeArray<float>(max * MCount, Allocator.Persistent);
            TarM = new NativeArray<float>(max * MCount, Allocator.Persistent);
            OutM = new NativeArray<float>(max * MCount, Allocator.Persistent);

            PrevP = new NativeArray<float3>(max, Allocator.Persistent);
            PrevR = new NativeArray<quaternion>(max, Allocator.Persistent);

            TarP = new NativeArray<float3>(max, Allocator.Persistent);
            TarR = new NativeArray<quaternion>(max, Allocator.Persistent);

            OutP = new NativeArray<float3>(max, Allocator.Persistent);
            OutR = new NativeArray<quaternion>(max, Allocator.Persistent);

            LastTime = new NativeArray<float>(max, Allocator.Persistent);
            PrevTime = new NativeArray<float>(max, Allocator.Persistent);
            ActiveSlots = new NativeArray<bool>(max, Allocator.Persistent);
            
            HighWaterMark = 0;
        }

        public static int Register()
        {
            return HighWaterMark++;
        }

        public static void ActivateSlot(int s)
        {
            ActiveSlots[s] = true;
            float t = Time.time;
            LastTime[s] = t;
            PrevTime[s] = t;
        }

        public static void DeactivateSlot(int s)
        {
            ActiveSlots[s] = false;
            
            // Clear the slot data
            int o = s * MCount;
            for (int i = 0; i < MCount; i++)
            {
                PrevM[o + i] = 0f;
                TarM[o + i] = 0f;
                OutM[o + i] = 0f;
            }
            PrevP[s] = 0;
            PrevR[s] = quaternion.identity;
            TarP[s] = 0;
            TarR[s] = quaternion.identity;
            OutP[s] = 0;
            OutR[s] = quaternion.identity;
            LastTime[s] = 0;
            PrevTime[s] = 0;
        }

        public static void WritePacket(int s, Vector3 p, Vector3 r, float[] m)
        {
            if (!ActiveSlots[s]) return;
            
            // Copy current target to previous before overwriting
            unsafe
            {
                float* prevPtr = (float*)PrevM.GetUnsafePtr() + s * MCount;
                float* tarPtr = (float*)TarM.GetUnsafePtr() + s * MCount;
                UnsafeUtility.MemCpy(prevPtr, tarPtr, MCount * sizeof(float));
            }
            PrevP[s] = TarP[s];
            PrevR[s] = TarR[s];
            
            PrevTime[s] = LastTime[s];
            LastTime[s] = Time.time;

            // Write new packet to target
            TarP[s] = p;
            TarR[s] = Quaternion.Euler(r);

            unsafe
            {
                fixed (float* src = m)
                {
                    float* dst = (float*)TarM.GetUnsafePtr() + s * MCount;
                    UnsafeUtility.MemCpy(dst, src, MCount * sizeof(float));
                }
            }
        }

        public static void Shutdown()
        {
            Handles.Dispose();
            
            Job.Complete();

            PrevM.Dispose();
            PrevP.Dispose();
            PrevR.Dispose();

            TarM.Dispose();
            TarP.Dispose();
            TarR.Dispose();

            OutM.Dispose();
            OutP.Dispose();
            OutR.Dispose();

            LastTime.Dispose();
            PrevTime.Dispose();
            ActiveSlots.Dispose();
        }
    }

    [BurstCompile]
    public struct InterpJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> PrevM;
        [ReadOnly] public NativeArray<float> TarM;
        [NativeDisableParallelForRestriction] public NativeArray<float> OutM;
        
        [ReadOnly] public NativeArray<float3> PrevP;
        [ReadOnly] public NativeArray<float3> TarP;
        [NativeDisableParallelForRestriction] public NativeArray<float3> OutP;
        
        [ReadOnly] public NativeArray<quaternion> PrevR;
        [ReadOnly] public NativeArray<quaternion> TarR;
        [NativeDisableParallelForRestriction] public NativeArray<quaternion> OutR;
        
        [ReadOnly] public NativeArray<float> LastTime;
        [ReadOnly] public NativeArray<float> PrevTime;
        [ReadOnly] public NativeArray<bool> ActiveSlots;
        public float CurrentTime;

        public void Execute(int s)
        {
            if (!ActiveSlots[s]) return;
            
            float lastUpdate = LastTime[s];
            float previousUpdate = PrevTime[s];
            
            float updateGap = lastUpdate - previousUpdate;
            float timeSinceLastUpdate = CurrentTime - lastUpdate;
            
            // Avoid division by zero by assuming 10Hz (0.1s) if no data yet
            if (updateGap < 0.001f) updateGap = 0.1f;
            
            // Calculate how far through the next update interval we are
            float t = math.min(timeSinceLastUpdate / updateGap, 1f);
            
            int o = s * MuscleStore.MCount;

            // Interpolate from prev packet to target packet, write to out
            
            for (int i = 0; i < MuscleStore.MCount; i++)
                OutM[o + i] = math.lerp(PrevM[o + i], TarM[o + i], t);
            OutP[s] = math.lerp(PrevP[s], TarP[s], t);
            OutR[s] = math.slerp(PrevR[s], TarR[s], t);
        }
    }

    [BurstCompile]
    public struct MuscleWriteJob : IAnimationJob
    {
        [ReadOnly] public NativeArray<float> M;
        [ReadOnly] public NativeArray<float3> P;
        [ReadOnly] public NativeArray<quaternion> R;
        [ReadOnly] public NativeArray<MuscleHandle> H;
        public TransformStreamHandle Hip;
        public int s;

        public void ProcessAnimation(AnimationStream st)
        {
            AnimationHumanStream h = st.AsHuman();
            if (!h.isValid) return;

            // TODO: Need a blend weight. Needs to skip finger muscles
            // and whatever else we are driving externally.
            int o = s * MuscleStore.MCount;
            for (int i = 0; i < MuscleStore.MCount; i++)
                h.SetMuscle(H[i], M[o + i]);

            Hip.SetPosition(st, P[s]);
            Hip.SetRotation(st, R[s]);
        }

        public void ProcessRootMotion(AnimationStream st) { }
    }
}