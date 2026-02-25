using System;
using UnityEngine;

namespace NAK.Contacts
{
    public abstract class ContactBase : MonoBehaviour
    {
        // Client-only deterministic ID representing the owner content.
        // For example, assign GetHashCode of the Avatar/Prop/World descriptor during loading phase.
        public int OwnerId { get; set; }
        
        // The type of content this contact is on. This only matters for Senders, but may be
        // useful info for receivers as well when implementing per-content limits.
        public ContentType SourceContentType { get; set; } = ContentType.Avatar;
        
        // Local client-only ID for the contact. (Using GetHashCode over GetInstanceID as GetHashCode returns 
        // the cached m_InstanceID on UnityEngine.Object without a needless main thread check)
        public int ContactId => _contactId != 0 ? _contactId : _contactId = GetHashCode();
        private int _contactId;
        
        public ShapeType shapeType = ShapeType.Sphere;
        public Vector3 localPosition = Vector3.zero;
        public Quaternion localRotation = Quaternion.identity;
        
        public float radius = 0.5f;
        public float height = 1.0f;
        
        public bool allowSelf = true;
        public bool allowOthers = true;
        
        public ContentType contentTypes = ContentType.World | ContentType.Avatar | ContentType.Prop;
        public string[] collisionTags = Array.Empty<string>();
        
        // The value associated with this contact. Receivers can copy these from Senders, and Receivers can
        // define it to have a static value applied on collision.
        public float contactValue = 1f;
        
        // Serialized as in-game gizmos can utilize the color :)
        public bool drawGizmos = true;
        public Color gizmoColor = Color.green;
        
        private void Awake() => ContactManager.Instance.Register(this);
        private void OnEnable() => ContactManager.Instance.SetEnabled(ContactId, true);
        private void OnDisable() { if (ContactManager.Exists) ContactManager.Instance.SetEnabled(ContactId, false); }
        private void OnDestroy() { if (ContactManager.Exists) ContactManager.Instance.Unregister(this); }
        private void OnDidApplyAnimationProperties() => ContactManager.Instance.MarkDirty(ContactId);
        private void OnValidate()
        {
            // May be nice to also include a property drawer for the tags to enforce this.
            if (collisionTags.Length > ContactLimits.MaxTags)
                Array.Resize(ref collisionTags, ContactLimits.MaxTags);
        }
        
        private void OnDrawGizmos() => DrawContactGizmo(false);
        private void OnDrawGizmosSelected() => DrawContactGizmo(true);

        private void DrawContactGizmo(bool selected)
        {
            if (!drawGizmos) return;

            Color color = selected ? new Color(1f, 1f, 0f, 0.9f) : gizmoColor;
            Gizmos.color = color;

            Matrix4x4 oldMatrix = Gizmos.matrix;

            Vector3 worldPos = transform.TransformPoint(localPosition);
            Quaternion worldRot = transform.rotation * localRotation;

            Gizmos.matrix = Matrix4x4.TRS(worldPos, worldRot, Vector3.one);

            switch (shapeType)
            {
                case ShapeType.Sphere:
                    DrawWireSphere(Vector3.zero, radius);
                    break;

                case ShapeType.Capsule:
                    DrawWireCapsuleSimple(Vector3.zero, radius, height);
                    break;
            }

            Gizmos.matrix = oldMatrix;

#if UNITY_EDITOR
            var style = new GUIStyle();
            style.normal.textColor = color;
            style.fontSize = selected ? 11 : 10;
            style.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;

            string label = this is ContactSender ? "Sender" : "Receiver";
            if (collisionTags != null && collisionTags.Length > 0)
                label += $"\nTags: {string.Join(", ", collisionTags)}";

            UnityEditor.Handles.Label(worldPos + Vector3.up * (radius + 0.2f), label, style);
#endif
        }

        private void DrawWireSphere(Vector3 center, float r)
        {
            DrawCircle(center, Vector3.up, r);
            DrawCircle(center, Vector3.right, r);
            DrawCircle(center, Vector3.forward, r);
        }

        private void DrawWireCapsuleSimple(Vector3 center, float r, float h)
        {
            float half = Mathf.Max(r, h * 0.5f);
            float cylinderHalf = Mathf.Max(0f, half - r);

            Vector3 top = center + Vector3.up * cylinderHalf;
            Vector3 bottom = center - Vector3.up * cylinderHalf;

            // end spheres
            DrawWireSphere(top, r);
            DrawWireSphere(bottom, r);

            // connect like a stretched sphere
            Gizmos.DrawLine(top + Vector3.right * r, bottom + Vector3.right * r);
            Gizmos.DrawLine(top - Vector3.right * r, bottom - Vector3.right * r);
            Gizmos.DrawLine(top + Vector3.forward * r, bottom + Vector3.forward * r);
            Gizmos.DrawLine(top - Vector3.forward * r, bottom - Vector3.forward * r);
        }

        private void DrawCircle(Vector3 center, Vector3 normal, float r)
        {
            Vector3 forward = Vector3.Slerp(normal, -normal, 0.5f);
            Vector3 right = Vector3.Cross(normal, forward).normalized;
            Vector3 up = Vector3.Cross(right, normal).normalized;

            Vector3 prev = center + right * r;

            for (int i = 1; i <= 32; i++)
            {
                float angle = (i / 32f) * Mathf.PI * 2f;
                Vector3 next = center + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * r;
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}