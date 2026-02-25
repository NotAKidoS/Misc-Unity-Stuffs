using System;
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine.Jobs;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;

namespace NAK.Contacts
{
    public static class ContactLimits
    {
        public const int MaxContacts = 4096;
        public const int MaxPairsPerFrame = 512;
        public const int MaxTags = 16;
    }
    
    public enum ShapeType : byte { Sphere = 0, Capsule = 1 }

    public enum ReceiverType : byte
    {
        // Set as 1 while there is any contact
        Constant = 0, 
        // Set to 1 for one frame and reset the next frame
        OnEnter = 1, 
        // Measures from the sender surface to the receiver center
        ProximitySenderToReceiver = 2, 
        // Measures from the receiver surface to the senders center
        ProximityReceiverToSender = 3, 
        // Measures from the receivers center to the senders center
        ProximityCenterToCenter = 4, 
        // Copies the value set on the sender
        CopyValueFromSender = 5,
        // The velocity of the receiver during the contact
        VelocityReceiver = 6,
        // The velocity of the sender during the contact
        VelocitySender = 7,
        // The velocity of both contacts during the contact
        VelocityMagnitude = 8,
    }
    
    [Flags] public enum ContentType : byte { World = 1, Avatar = 2, Prop = 4 }
    
    [BurstCompile]
    [StructLayout(LayoutKind.Explicit)]
    public struct LocalToWorldMatrix
    {
        [FieldOffset(0)]  private float3x4 data;
        [FieldOffset(36)] public float3 position;
        [FieldOffset(0)]  private readonly float3 c0;
        [FieldOffset(12)] private readonly float3 c1;
        [FieldOffset(24)] private readonly float3 c2;

        public quaternion rotation => quaternion.LookRotationSafe(math.normalizesafe(c2), math.normalizesafe(c1));
        public float uniformScale => (SafeLength(c0) + SafeLength(c1) + SafeLength(c2)) * 0.33333334f;

        private LocalToWorldMatrix(float4x4 m) : this()
        {
            data.c0 = m.c0.xyz;
            data.c1 = m.c1.xyz;
            data.c2 = m.c2.xyz;
            data.c3 = m.c3.xyz;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float SafeLength(float3 v)
        {
            float sq = math.lengthsq(v);
            if (sq == 0f) return 0f;
            if (sq > 0.998f && sq < 1.002f) return 1f;
            return math.sqrt(sq);
        }

        public static implicit operator float4x4(LocalToWorldMatrix m) => 
            new(new float4(m.data.c0, 0f), new float4(m.data.c1, 0f), new float4(m.data.c2, 0f), new float4(m.data.c3, 1f));
        public static implicit operator LocalToWorldMatrix(Matrix4x4 m) => new(m);
    }
    
    [BurstCompile]
    [StructLayout(LayoutKind.Sequential)]
    public struct ContactData
    {
        // Identity
        public int contactId;
        public int ownerId;
        
        // Flags packed into single byte
        private byte flags;
        public bool Exists      { get => GetFlag(0); set => SetFlag(0, value); }
        public bool Enabled     { get => GetFlag(1); set => SetFlag(1, value); }
        public bool IsSender    { get => GetFlag(2); set => SetFlag(2, value); }
        public bool AllowSelf   { get => GetFlag(3); set => SetFlag(3, value); }
        public bool AllowOthers { get => GetFlag(4); set => SetFlag(4, value); }
        public bool IsSphere    { get => GetFlag(5); set => SetFlag(5, value); }
        
        // Shape configuration
        public byte shapeType;
        public byte contentType;
        public byte receiverType;
        public float3 localPosition;
        public quaternion localRotation;
        public float radius;
        public float height;
        public float contactValue;
        
        // World-space computed data
        public LocalToWorldMatrix localToWorld;
        public float3 worldPosition;
        public float3 capsuleP0;
        public float3 capsuleP1;
        public float worldRadius;
        public float velocity;
        public float4 bounds;
        public float3 prevPosition;
        
        // Tags
        public int tagHash0, tagHash1, tagHash2, tagHash3;
        public int tagHash4, tagHash5, tagHash6, tagHash7;
        public int tagHash8, tagHash9, tagHash10, tagHash11;
        public int tagHash12, tagHash13, tagHash14, tagHash15;
        public int tagCount;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool GetFlag(int bit) => (flags & (1 << bit)) != 0;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetFlag(int bit, bool value)
        {
            if (value) flags |= (byte)(1 << bit);
            else flags &= (byte)~(1 << bit);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetTagHash(int index)
        {
            switch (index)
            {
                case 0: return tagHash0;   case 1: return tagHash1;
                case 2: return tagHash2;   case 3: return tagHash3;
                case 4: return tagHash4;   case 5: return tagHash5;
                case 6: return tagHash6;   case 7: return tagHash7;
                case 8: return tagHash8;   case 9: return tagHash9;
                case 10: return tagHash10; case 11: return tagHash11;
                case 12: return tagHash12; case 13: return tagHash13;
                case 14: return tagHash14; case 15: return tagHash15;
                default: return 0;
            }
        }
        
        public void SetTags(string[] tags)
        {
            tagCount = math.min(tags?.Length ?? 0, ContactLimits.MaxTags);
            tagHash0 = tagHash1 = tagHash2 = tagHash3 = 0;
            tagHash4 = tagHash5 = tagHash6 = tagHash7 = 0;
            tagHash8 = tagHash9 = tagHash10 = tagHash11 = 0;
            tagHash12 = tagHash13 = tagHash14 = tagHash15 = 0;
            
            int count = tagCount;
            int writeIndex = 0;
            
            for (int i = 0; i < count; i++)
            {
                string tag = tags![i];
                if (string.IsNullOrEmpty(tag)) continue;
                int hash = Animator.StringToHash(tag.ToLowerInvariant());
                switch (writeIndex++)
                {
                    case 0: tagHash0 = hash; break;   case 1: tagHash1 = hash; break;
                    case 2: tagHash2 = hash; break;   case 3: tagHash3 = hash; break;
                    case 4: tagHash4 = hash; break;   case 5: tagHash5 = hash; break;
                    case 6: tagHash6 = hash; break;   case 7: tagHash7 = hash; break;
                    case 8: tagHash8 = hash; break;   case 9: tagHash9 = hash; break;
                    case 10: tagHash10 = hash; break; case 11: tagHash11 = hash; break;
                    case 12: tagHash12 = hash; break; case 13: tagHash13 = hash; break;
                    case 14: tagHash14 = hash; break; case 15: tagHash15 = hash; break;
                }
            }
        }
    }
    
    [BurstCompile]
    [StructLayout(LayoutKind.Sequential)]
    public struct CollisionPair
    {
        public int senderContactId;
        public int receiverContactId;
        public int senderIndex;
        public int receiverIndex;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long GetPairKey(int receiverContactId, int senderContactId) => 
            ((long)receiverContactId << 32) | (uint)senderContactId;
    }
    
    [BurstCompile]
    [StructLayout(LayoutKind.Sequential)]
    public struct CollisionResult
    {
        public int senderContactId;
        public int receiverContactId;
        public int senderIndex;
        public int receiverIndex;
        public float targetValue;
        public float senderValue;
        public byte isColliding;
        public byte velocityMet;
        public float wasColliding;
    }
    
    public struct ContactCollisionInfo
    {
        public int senderContactId;
        public float targetValue;

        public override string ToString()
        {
            StringBuilder sb = new();
            sb.Append("senderContactId");
            sb.Append(senderContactId);
            sb.Append(" proximityValue");
            sb.Append(targetValue);
            return sb.ToString();
        }
    }
    
    [BurstCompile]
    public struct UpdateTransformsJob : IJobParallelForTransform
    {
        public NativeArray<ContactData> contacts;
        [ReadOnly] public float deltaTime;
        
        public void Execute(int index, TransformAccess transform)
        {
            ContactData contact = contacts[index];
            if (!contact.Exists || !contact.Enabled) return;
            
            contact.prevPosition = contact.worldPosition;
            contact.localToWorld = transform.localToWorldMatrix;
            
            float3 transformPos = contact.localToWorld.position;
            float scale = contact.localToWorld.uniformScale;
            
            // Compute world position
            float3 worldPos = transformPos;
            if (math.lengthsq(contact.localPosition) > 0.001f)
            {
                float4 localH = new float4(contact.localPosition, 1f);
                worldPos = math.mul(contact.localToWorld, localH).xyz;
            }
            
            contact.worldPosition = worldPos;
            contact.worldRadius = contact.radius * scale;
            
            // Velocity
            contact.velocity = math.length(worldPos - contact.prevPosition) / math.max(deltaTime, 0.0001f);
            
            // Capsule endpoints
            quaternion worldRot = math.mul(transform.rotation, contact.localRotation);
            if (contact.shapeType == (byte)ShapeType.Capsule)
            {
                float3 up = math.mul(worldRot, new float3(0, 1, 0));
                float halfHeight = math.max(0, (contact.height * scale * 0.5f) - contact.worldRadius);
                
                contact.IsSphere = halfHeight <= 0;
                if (contact.IsSphere)
                {
                    contact.capsuleP0 = contact.capsuleP1 = worldPos;
                }
                else
                {
                    contact.capsuleP0 = worldPos - up * halfHeight;
                    contact.capsuleP1 = worldPos + up * halfHeight;
                }
            }
            else
            {
                contact.IsSphere = true;
                contact.capsuleP0 = contact.capsuleP1 = worldPos;
            }
            
            // Bounds
            float extent = contact.worldRadius * 2f;
            if (!contact.IsSphere) extent = math.max(extent, contact.height * scale);
            contact.bounds = new float4(worldPos, extent * 0.5f);
            
            contacts[index] = contact;
        }
    }
    
    [BurstCompile]
    public struct BroadphaseJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> senderIndices;
        [ReadOnly] public NativeArray<int> receiverIndices;
        [ReadOnly] public NativeArray<ContactData> contacts;
        [WriteOnly] public NativeStream.Writer pairWriter;
        
        // This currently has every receiver check against every sender.
        // It can be done in a smarter way to cull things even before this loop, but
        // at the scale this is at, I don't think it would help too much.
        // At least, there is probably lower-hanging fruit elsewhere.
        
        public void Execute(int receiverJobIndex)
        {
            pairWriter.BeginForEachIndex(receiverJobIndex);
            
            int receiverIdx = receiverIndices[receiverJobIndex];
            ContactData receiver = contacts[receiverIdx];
            
            if (!receiver.Exists || !receiver.Enabled)
            {
                pairWriter.EndForEachIndex();
                return;
            }
            
            int senderCount = senderIndices.Length;
            for (int s = 0; s < senderCount; s++)
            {
                int senderIdx = senderIndices[s];
                ContactData sender = contacts[senderIdx];
                
                if (!sender.Exists || !sender.Enabled) continue;
                
                // Content type
                if ((sender.contentType & receiver.contentType) == 0) continue;
                
                // Bounds
                if (!BoundsIntersect(sender.bounds, receiver.bounds)) continue;
                
                // Owner checks
                if (sender.ownerId != 0 && receiver.ownerId != 0)
                {
                    bool sameOwner = sender.ownerId == receiver.ownerId;
                    if (sameOwner && !receiver.AllowSelf) continue;
                    if (!sameOwner && !receiver.AllowOthers) continue;
                }
                
                // Tags
                if (!HasMatchingTag(sender, receiver)) continue;
                
                pairWriter.Write(new CollisionPair
                {
                    senderContactId = sender.contactId,
                    receiverContactId = receiver.contactId,
                    senderIndex = senderIdx,
                    receiverIndex = receiverIdx
                });
            }
            
            pairWriter.EndForEachIndex();
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HasMatchingTag(ContactData sender, ContactData receiver)
        {
            int sCount = sender.tagCount;
            int rCount = receiver.tagCount;
            if (sCount == 0 || rCount == 0) return true;
            for (int i = 0; i < sCount; i++)
            {
                int sTag = sender.GetTagHash(i);
                for (int j = 0; j < rCount; j++)
                {
                    if (sTag == receiver.GetTagHash(j)) return true;
                }
            }
            return false;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool BoundsIntersect(float4 a, float4 b)
        {
            float maxDist = a.w + b.w;
            return math.distancesq(a.xyz, b.xyz) <= (maxDist * maxDist);
        }
    }
    
    [BurstCompile]
    public struct CollectPairsJob : IJob
    {
        [ReadOnly] public NativeStream.Reader pairReader;
        public NativeList<CollisionPair> pairs;
        public NativeParallelHashSet<long> pairSet;
        [ReadOnly] public NativeParallelHashMap<long, float> prevCollisionStates;
        [ReadOnly] public NativeParallelHashMap<int, int> contactIdToIndex;
        [ReadOnly] public NativeArray<ContactData> contacts;
        
        public void Execute()
        {
            pairs.Clear();
            pairSet.Clear();
            
            // Add all previous collision pairs to ensure we detect exits
            var prevKeys = prevCollisionStates.GetKeyArray(Allocator.Temp);
            int prevCount = prevKeys.Length;
            for (int i = 0; i < prevCount; i++)
            {
                long key = prevKeys[i];
                int receiverContactId = (int)(key >> 32);
                int senderContactId = (int)(key & 0xFFFFFFFF);
                
                // Resolve current indices from contact IDs
                if (!contactIdToIndex.TryGetValue(receiverContactId, out int receiverIdx)) continue;
                if (!contactIdToIndex.TryGetValue(senderContactId, out int senderIdx)) continue;
                
                ContactData receiver = contacts[receiverIdx];
                ContactData sender = contacts[senderIdx];
                if (!receiver.Exists || !sender.Exists) continue;
                
                if (!pairSet.Contains(key) && pairs.Length < ContactLimits.MaxPairsPerFrame)
                {
                    pairs.Add(new CollisionPair
                    {
                        senderContactId = senderContactId,
                        receiverContactId = receiverContactId,
                        senderIndex = senderIdx,
                        receiverIndex = receiverIdx
                    });
                    pairSet.Add(key);
                }
            }
            prevKeys.Dispose();
            
            // Collect new pairs from broadphase
            int foreachCount = pairReader.ForEachCount;
            for (int i = 0; i < foreachCount; i++)
            {
                int itemCount = pairReader.BeginForEachIndex(i);
                for (int j = 0; j < itemCount; j++)
                {
                    CollisionPair pair = pairReader.Read<CollisionPair>();
                    long key = CollisionPair.GetPairKey(pair.receiverContactId, pair.senderContactId);
                    
                    if (!pairSet.Contains(key) && pairs.Length < ContactLimits.MaxPairsPerFrame)
                    {
                        pairs.Add(pair);
                        pairSet.Add(key);
                    }
                }
                pairReader.EndForEachIndex();
            }
        }
    }
    
    [BurstCompile]
    public struct NarrowphaseJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<CollisionPair> pairs;
        [ReadOnly] public NativeArray<ContactData> contacts;
        [ReadOnly] public NativeParallelHashMap<long, float> prevCollisionStates;
        [WriteOnly] public NativeArray<CollisionResult> results;
        
        public void Execute(int index)
        {
            CollisionPair pair = pairs[index];
            ContactData sender = contacts[pair.senderIndex];
            ContactData receiver = contacts[pair.receiverIndex];
            
            long pairKey = CollisionPair.GetPairKey(pair.receiverContactId, pair.senderContactId);
            byte wasColliding = prevCollisionStates.TryGetValue(pairKey, out float _) ? (byte)1 : (byte)0;
            
            CollisionResult result = new CollisionResult
            {
                senderContactId = pair.senderContactId,
                receiverContactId = pair.receiverContactId,
                senderIndex = pair.senderIndex,
                receiverIndex = pair.receiverIndex,
                isColliding = 0,
                targetValue = 0f,
                velocityMet = 1,
                wasColliding = wasColliding,
                senderValue = sender.contactValue
            };
            
            bool colliding = CheckCollision(sender, receiver);
            
            if (colliding)
            {
                result.isColliding = 1;
                
                ReceiverType rType = (ReceiverType)receiver.receiverType;
                float totalVelocity = sender.velocity + receiver.velocity;
                
                switch (rType)
                {
                    case ReceiverType.Constant:
                        result.targetValue = receiver.contactValue;
                        break;
                    case ReceiverType.OnEnter:
                        result.targetValue = wasColliding == 0 ? 1f : 0f;
                        result.velocityMet = (byte)(totalVelocity >= receiver.contactValue ? 1 : 0);
                        break;
                    case ReceiverType.ProximitySenderToReceiver:
                        result.targetValue = CalcProximitySenderToReceiver(sender, receiver);
                        break;
                    case ReceiverType.ProximityReceiverToSender:
                        result.targetValue = CalcProximityReceiverToSender(sender, receiver);
                        break;
                    case ReceiverType.ProximityCenterToCenter:
                        result.targetValue = CalcProximityCenterToCenter(sender, receiver);
                        break;
                    case ReceiverType.CopyValueFromSender:
                        result.targetValue = sender.contactValue;
                        result.velocityMet = (byte)(totalVelocity >= receiver.contactValue ? 1 : 0);
                        break;
                    case ReceiverType.VelocityReceiver:
                        result.targetValue = receiver.velocity;
                        result.velocityMet = (byte)(totalVelocity >= receiver.contactValue ? 1 : 0);
                        break;
                    case ReceiverType.VelocitySender:
                        result.targetValue = sender.velocity;
                        result.velocityMet = (byte)(totalVelocity >= receiver.contactValue ? 1 : 0);
                        break;
                    case ReceiverType.VelocityMagnitude:
                        result.targetValue = totalVelocity;
                        result.velocityMet = (byte)(totalVelocity >= receiver.contactValue ? 1 : 0);
                        break;
                }
            }
            
            results[index] = result;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CheckCollision(ContactData sender, ContactData receiver)
        {
            if (sender.IsSphere && receiver.IsSphere)
                return math.distance(sender.worldPosition, receiver.worldPosition) <= (sender.worldRadius + receiver.worldRadius);
            
            if (sender.IsSphere)
                return CheckSphereCapsule(sender.worldPosition, sender.worldRadius, receiver.capsuleP0, receiver.capsuleP1, receiver.worldRadius);
            
            if (receiver.IsSphere)
                return CheckSphereCapsule(receiver.worldPosition, receiver.worldRadius, sender.capsuleP0, sender.capsuleP1, sender.worldRadius);
            
            return CheckCapsuleCapsule(sender.capsuleP0, sender.capsuleP1, sender.worldRadius, receiver.capsuleP0, receiver.capsuleP1, receiver.worldRadius);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CheckSphereCapsule(float3 spherePos, float sphereR, float3 capP0, float3 capP1, float capR)
        {
            float3 closest = ClosestPointOnSegment(capP0, capP1, spherePos);
            return math.distance(spherePos, closest) <= (sphereR + capR);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CheckCapsuleCapsule(float3 aP0, float3 aP1, float aR, float3 bP0, float3 bP1, float bR)
        {
            ClosestPointsBetweenSegments(aP0, aP1, bP0, bP1, out float3 closestA, out float3 closestB);
            return math.distance(closestA, closestB) <= (aR + bR);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float CalcProximitySenderToReceiver(ContactData sender, ContactData receiver)
        {
            float3 surfacePt = GetClosestSurfacePoint(sender, receiver.worldPosition);
            float dist = math.distance(surfacePt, receiver.worldPosition);
            float maxDist = GetMaxExtent(receiver);
            return 1f - math.saturate(dist / maxDist);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float CalcProximityReceiverToSender(ContactData sender, ContactData receiver)
        {
            float3 surfacePt = GetClosestSurfacePoint(receiver, sender.worldPosition);
            float dist = math.distance(surfacePt, sender.worldPosition);
            float maxDist = GetMaxExtent(sender);
            return 1f - math.saturate(dist / maxDist);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float CalcProximityCenterToCenter(ContactData sender, ContactData receiver)
        {
            float dist = math.distance(sender.worldPosition, receiver.worldPosition);
            float maxDist = GetMaxExtent(sender) + GetMaxExtent(receiver);
            return 1f - math.saturate(dist / maxDist);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetMaxExtent(ContactData c) => c.worldRadius;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float3 GetClosestSurfacePoint(ContactData c, float3 target)
        {
            float3 center = c.IsSphere ? c.worldPosition : ClosestPointOnSegment(c.capsuleP0, c.capsuleP1, target);
            float3 dir = target - center;
            float len = math.length(dir);
            if (len < math.EPSILON) return center + new float3(c.worldRadius, 0, 0);
            return center + (dir / len) * c.worldRadius;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float3 ClosestPointOnSegment(float3 a, float3 b, float3 point)
        {
            float3 ab = b - a;
            float abLenSq = math.lengthsq(ab);
            if (abLenSq < math.EPSILON) return a;
            float t = math.saturate(math.dot(point - a, ab) / abLenSq);
            return a + t * ab;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ClosestPointsBetweenSegments(float3 a1, float3 a2, float3 b1, float3 b2, out float3 closestA, out float3 closestB)
        {
            float3 d1 = a2 - a1, d2 = b2 - b1, r = a1 - b1;
            float a = math.lengthsq(d1), e = math.lengthsq(d2), f = math.dot(d2, r);
            
            if (a <= math.EPSILON && e <= math.EPSILON) { closestA = a1; closestB = b1; return; }
            
            float s, t;
            if (a <= math.EPSILON) { s = 0f; t = math.saturate(f / e); }
            else
            {
                float c = math.dot(d1, r);
                if (e <= math.EPSILON) { t = 0f; s = math.saturate(-c / a); }
                else
                {
                    float b = math.dot(d1, d2);
                    float denom = a * e - b * b;
                    s = denom != 0f ? math.saturate((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = math.saturate(-c / a); }
                    else if (t > 1f) { t = 1f; s = math.saturate((b - c) / a); }
                }
            }
            closestA = a1 + s * d1;
            closestB = b1 + t * d2;
        }
    }
    
    [BurstCompile]
    public struct AggregateResultsJob : IJob
    {
        [ReadOnly] public NativeArray<CollisionResult> results;
        public NativeParallelHashMap<long, float> collisionStates;
        public NativeParallelHashMap<int, CollisionResult> bestResultPerReceiver;
        
        public void Execute()
        {
            collisionStates.Clear();
            bestResultPerReceiver.Clear();
            
            int count = results.Length;
            for (int i = 0; i < count; i++)
            {
                CollisionResult result = results[i];
                if (result.isColliding != 1) continue;
                
                long pairKey = CollisionPair.GetPairKey(result.receiverContactId, result.senderContactId);
                collisionStates.TryAdd(pairKey, 1);
                
                if (bestResultPerReceiver.TryGetValue(result.receiverContactId, out CollisionResult existing))
                {
                    if (result.targetValue > existing.targetValue)
                        bestResultPerReceiver[result.receiverContactId] = result;
                }
                else
                {
                    bestResultPerReceiver.TryAdd(result.receiverContactId, result);
                }
            }
        }
    }
    
    public class ContactManager : MonoBehaviour
    {
        private static ContactManager _instance;
        public static ContactManager Instance => _instance ? _instance : CreateInstance();
        public static bool Exists => _instance != null;
        
        private static ContactManager CreateInstance()
        {
            var go = new GameObject("ContactManager");
            _instance = go.AddComponent<ContactManager>();
            DontDestroyOnLoad(go);
            return _instance;
        }
        
        // Native collections
        private NativeArray<ContactData> contacts;
        private TransformAccessArray transforms;
        private NativeParallelHashMap<int, int> contactIdToIndex;
        private NativeArray<int> senderIndices;
        private NativeArray<int> receiverIndices;
        private NativeList<CollisionPair> pairs;
        private NativeParallelHashSet<long> pairSet;
        private NativeParallelHashMap<long, float> collisionStates;
        private NativeParallelHashMap<long, float> prevCollisionStates;
        private NativeParallelHashMap<int, CollisionResult> bestResults;
        private NativeArray<CollisionResult> narrowResults;
        private NativeStream broadphaseStream;
        
        // Managed tracking
        private readonly List<ContactBase> contactList = new();
        private readonly Dictionary<int, ContactReceiver> receivers = new();
        private readonly Dictionary<int, ContactSender> senders = new();
        private readonly HashSet<ContactBase> pendingAdd = new();
        private readonly HashSet<ContactBase> pendingRemove = new();
        private readonly Dictionary<int, bool> pendingState = new();
        private readonly HashSet<int> pendingDirty = new();
        
        private int contactCount;
        private int senderCount;
        private int receiverCount;
        private bool needsRebuild;
        
        // Stats
        public int ManagedContacts => contactCount;
        public int SenderCount => senderCount;
        public int ReceiverCount => receiverCount;
        public int TotalPairs => pairs.IsCreated ? pairs.Length : 0;
        public float ProcessTimeMs { get; private set; }
        
        private void Awake()
        {
            if (_instance != null 
                && _instance != this) 
            { 
                Destroy(this); 
                return;
            }
            _instance = this;
            
            contacts = new NativeArray<ContactData>(ContactLimits.MaxContacts, Allocator.Persistent);
            transforms = new TransformAccessArray(ContactLimits.MaxContacts);
            contactIdToIndex = new NativeParallelHashMap<int, int>(ContactLimits.MaxContacts, Allocator.Persistent);
            senderIndices = new NativeArray<int>(0, Allocator.Persistent);
            receiverIndices = new NativeArray<int>(0, Allocator.Persistent);
            pairs = new NativeList<CollisionPair>(ContactLimits.MaxPairsPerFrame, Allocator.Persistent);
            pairSet = new NativeParallelHashSet<long>(ContactLimits.MaxPairsPerFrame, Allocator.Persistent);
            collisionStates = new NativeParallelHashMap<long, float>(ContactLimits.MaxPairsPerFrame, Allocator.Persistent);
            prevCollisionStates = new NativeParallelHashMap<long, float>(ContactLimits.MaxPairsPerFrame, Allocator.Persistent);
            bestResults = new NativeParallelHashMap<int, CollisionResult>(ContactLimits.MaxContacts, Allocator.Persistent);
            narrowResults = new NativeArray<CollisionResult>(ContactLimits.MaxPairsPerFrame, Allocator.Persistent);
        }
        
        private void OnDestroy()
        {
            _instance = null;
            if (contacts.IsCreated) contacts.Dispose();
            if (transforms.isCreated) transforms.Dispose();
            if (contactIdToIndex.IsCreated) contactIdToIndex.Dispose();
            if (senderIndices.IsCreated) senderIndices.Dispose();
            if (receiverIndices.IsCreated) receiverIndices.Dispose();
            if (pairs.IsCreated) pairs.Dispose();
            if (pairSet.IsCreated) pairSet.Dispose();
            if (collisionStates.IsCreated) collisionStates.Dispose();
            if (prevCollisionStates.IsCreated) prevCollisionStates.Dispose();
            if (bestResults.IsCreated) bestResults.Dispose();
            if (narrowResults.IsCreated) narrowResults.Dispose();
            if (broadphaseStream.IsCreated) broadphaseStream.Dispose();
        }
        
        public void Register(ContactBase contact) => pendingAdd.Add(contact);
        public void Unregister(ContactBase contact) => pendingRemove.Add(contact);
        public void SetEnabled(int id, bool setEnabled) => pendingState[id] = setEnabled;
        public void MarkDirty(int id) => pendingDirty.Add(id);
        
        private void LateUpdate()
        {
            float startTime = Time.realtimeSinceStartup;
            
            ProcessPendingChanges();
            
            if (contactCount != 0)
            {
                // Swap collision states
                (prevCollisionStates, collisionStates) = (collisionStates, prevCollisionStates);
                collisionStates.Clear();

                // Job 1: Update transforms
                var transformJob = new UpdateTransformsJob
                {
                    contacts = contacts,
                    deltaTime = Time.deltaTime
                };
                JobHandle transformHandle = transformJob.Schedule(transforms);

                // Job 2: Broadphase (parallel per receiver)
                if (broadphaseStream.IsCreated) broadphaseStream.Dispose();
                broadphaseStream = new NativeStream(receiverCount, Allocator.TempJob);

                var broadphaseJob = new BroadphaseJob
                {
                    senderIndices = senderIndices,
                    receiverIndices = receiverIndices,
                    contacts = contacts,
                    pairWriter = broadphaseStream.AsWriter()
                };
                JobHandle broadphaseHandle = broadphaseJob.Schedule(receiverCount, 1, transformHandle);

                // Job 3: Collect pairs
                var collectJob = new CollectPairsJob
                {
                    pairReader = broadphaseStream.AsReader(),
                    pairs = pairs,
                    pairSet = pairSet,
                    prevCollisionStates = prevCollisionStates,
                    contactIdToIndex = contactIdToIndex,
                    contacts = contacts
                };
                JobHandle collectHandle = collectJob.Schedule(broadphaseHandle);
                collectHandle.Complete();

                int pairCount = pairs.Length;
                if (pairCount == 0)
                {
                    ProcessTimeMs = (Time.realtimeSinceStartup - startTime) * 1000f;
                    return;
                }

                // Job 4: Narrowphase
                var narrowphaseJob = new NarrowphaseJob
                {
                    pairs = pairs.AsArray().GetSubArray(0, pairCount),
                    contacts = contacts,
                    prevCollisionStates = prevCollisionStates,
                    results = narrowResults
                };
                JobHandle narrowHandle = narrowphaseJob.Schedule(pairCount, 32);

                // Job 5: Aggregate
                var aggregateJob = new AggregateResultsJob
                {
                    results = narrowResults.GetSubArray(0, pairCount),
                    collisionStates = collisionStates,
                    bestResultPerReceiver = bestResults
                };
                JobHandle aggregateHandle = aggregateJob.Schedule(narrowHandle);
                aggregateHandle.Complete();

                FireCallbacks();
            }

            ProcessTimeMs = (Time.realtimeSinceStartup - startTime) * 1000f;
        }
        
        private void ProcessPendingChanges()
        {
            // Remove
            int removeCount = pendingRemove.Count;
            if (removeCount > 0)
            {
                foreach (var contact in pendingRemove)
                {
                    pendingAdd.Remove(contact);
                    RemoveInternal(contact);
                }
                pendingRemove.Clear();
                needsRebuild = true;
            }
            
            // Add
            int addCount = pendingAdd.Count;
            if (addCount > 0)
            {
                foreach (var contact in pendingAdd)
                    AddInternal(contact);
                pendingAdd.Clear();
                needsRebuild = true;
            }
            
            // State changes
            foreach (var kvp in pendingState)
            {
                if (contactIdToIndex.TryGetValue(kvp.Key, out int idx))
                {
                    ContactData data = contacts[idx];
                    data.Enabled = kvp.Value;
                    contacts[idx] = data;
                }
            }
            pendingState.Clear();
            
            // Dirty
            foreach (int id in pendingDirty)
            {
                if (contactIdToIndex.TryGetValue(id, out int idx))
                {
                    ContactData data = contacts[idx];
                    UpdateContactData(contactList[idx], ref data);
                    contacts[idx] = data;
                }
            }
            pendingDirty.Clear();
            
            if (needsRebuild)
            {
                RebuildIndexArrays();
                needsRebuild = false;
            }
        }
        
        private void AddInternal(ContactBase contact)
        {
            if (!contact || !contact.transform) return;
            if (contactCount >= ContactLimits.MaxContacts) return;
            
            int id = contact.ContactId;
            if (contactIdToIndex.ContainsKey(id)) return;
            
            int idx = contactCount++;
            contactIdToIndex.TryAdd(id, idx);
            contactList.Add(contact);
            transforms.Add(contact.transform);
            
            ContactData data = new ContactData { contactId = id, Exists = true, Enabled = true };
            UpdateContactData(contact, ref data);
            contacts[idx] = data;
            
            if (contact is ContactReceiver r) receivers[id] = r;
            else if (contact is ContactSender s) senders[id] = s;
        }
        
        private void RemoveInternal(ContactBase contact)
        {
            if (!contact) return;
            
            int id = contact.ContactId;
            if (!contactIdToIndex.TryGetValue(id, out int idx)) return;
            
            int lastIdx = contactCount - 1;
            if (idx != lastIdx)
            {
                // Swap with last
                ContactData lastData = contacts[lastIdx];
                contacts[idx] = lastData;
                contactIdToIndex[lastData.contactId] = idx;
                
                contactList[idx] = contactList[lastIdx];
                transforms.RemoveAtSwapBack(idx);
            }
            else
            {
                transforms.RemoveAtSwapBack(idx);
            }
            
            contactList.RemoveAt(lastIdx);
            contactIdToIndex.Remove(id);
            
            // Clear the last slot
            contacts[lastIdx] = default;
            
            contactCount--;
            receivers.Remove(id);
            senders.Remove(id);
        }
        
        private void UpdateContactData(ContactBase contact, ref ContactData data)
        {
            // We could pass a flag to signal it was animated to only update keyable things here to make cheaper,
            // but that means making this method ugly >:(
            data.IsSender = contact is ContactSender;
            data.AllowSelf = contact.allowSelf;
            data.AllowOthers = contact.allowOthers;
            data.shapeType = (byte)contact.shapeType;
            data.ownerId = contact.OwnerId;
            data.localPosition = contact.localPosition;
            data.localRotation = contact.localRotation;
            data.radius = contact.radius;
            data.height = contact.height;
            data.contactValue = contact.contactValue;
            
            if (contact is ContactSender sender)
            {
                data.contentType = (byte)sender.SourceContentType;
            }
            else if (contact is ContactReceiver receiver)
            {
                data.contentType = (byte)contact.contentTypes;
                data.receiverType = (byte)receiver.receiverType;
            }
            
            data.SetTags(contact.collisionTags);
        }

        private void RebuildIndexArrays()
        {
            if (senderIndices.IsCreated) senderIndices.Dispose();
            if (receiverIndices.IsCreated) receiverIndices.Dispose();
            
            senderCount = 0;
            receiverCount = 0;
            
            int count = contactCount;
            for (int i = 0; i < count; i++)
            {
                if (contacts[i].IsSender) senderCount++;
                else receiverCount++;
            }
            
            senderIndices = new NativeArray<int>(senderCount, Allocator.Persistent);
            receiverIndices = new NativeArray<int>(receiverCount, Allocator.Persistent);
            
            int sIdx = 0, rIdx = 0;
            for (int i = 0; i < count; i++)
            {
                if (contacts[i].IsSender) senderIndices[sIdx++] = i;
                else receiverIndices[rIdx++] = i;
            }
        }
        
        private void FireCallbacks()
        {
            // Get previous collision keys to detect exits
            var prevKeys = prevCollisionStates.GetKeyArray(Allocator.Temp);
            int prevCount = prevKeys.Length;
            
            // Fire exits
            for (int i = 0; i < prevCount; i++)
            {
                long key = prevKeys[i];
                if (!collisionStates.ContainsKey(key))
                {
                    int receiverId = (int)(key >> 32);
                    int senderId = (int)(key & 0xFFFFFFFF);
                    
                    if (receivers.TryGetValue(receiverId, out var receiver))
                    {
                        var info = new ContactCollisionInfo
                        {
                            senderContactId = senderId,
                            targetValue = 0f, // Maybe expose some way for a value to "stick"
                        };
                        
                        receiver.OnContactExit?.Invoke(info);
                        // Debug.Log($"OnContactExit: {info}");
                    }
                }
            }
            prevKeys.Dispose();
            
            // Fire enters and updates
            var currentKeys = collisionStates.GetKeyArray(Allocator.Temp);
            int currentCount = currentKeys.Length;
            
            for (int i = 0; i < currentCount; i++)
            {
                long key = currentKeys[i];
                int receiverId = (int)(key >> 32);
                int senderId = (int)(key & 0xFFFFFFFF);
                
                if (!receivers.TryGetValue(receiverId, out var receiver)) continue;
                if (!bestResults.TryGetValue(receiverId, out var result)) continue;
                if (result.senderContactId != senderId) continue; // Only fire for best sender
                if (result.velocityMet == 0) continue;
                
                var info = new ContactCollisionInfo
                {
                    senderContactId = senderId,
                    targetValue = result.targetValue,
                };
                
                bool wasColliding = prevCollisionStates.TryGetValue(key, out float prevValue);

                if (!wasColliding)
                {
                    receiver.OnContactEnter?.Invoke(info);
                    // Debug.Log($"OnContactEnter: {info}");
                }
                else if (!Mathf.Approximately(prevValue, info.targetValue))
                {
                    receiver.OnContactUpdate?.Invoke(info);
                    // Debug.Log($"OnContactUpdate: {info}");
                }
            }
            currentKeys.Dispose();
        }
    }
}