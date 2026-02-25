using UnityEditor;
using UnityEngine;

namespace NAK.Contacts
{
    [CustomEditor(typeof(ContactBase), true)]
    public class ContactBaseEditor : Editor
    {
        private ContactBase _contactBase;
        
        private SerializedProperty shapeType;
        private SerializedProperty localPosition;
        private SerializedProperty localRotation;
        private SerializedProperty radius;
        private SerializedProperty height;

        private SerializedProperty allowSelf;
        private SerializedProperty allowOthers;
        private SerializedProperty contentTypes;
        private SerializedProperty collisionTags;

        private SerializedProperty drawGizmos;
        private SerializedProperty gizmoColor;
        
        private SerializedProperty contactValue;

        // Receiver
        private SerializedProperty receiverType;
        
        // TODO: these should be stored on the component wrapped in UNITY_EDITOR
        private static bool foldShape = true;
        private static bool foldFiltering = true;
        private static bool foldRole = true;
        private static bool foldGizmos;

        private void OnEnable()
        {
            if (!target) return; // Occurs on domain reload
            _contactBase = (ContactBase)target;
            
            // Contact Base
            shapeType = serializedObject.FindProperty(nameof(ContactBase.shapeType));
            localPosition = serializedObject.FindProperty(nameof(ContactBase.localPosition));
            localRotation = serializedObject.FindProperty(nameof(ContactBase.localRotation));
            radius = serializedObject.FindProperty(nameof(ContactBase.radius));
            height = serializedObject.FindProperty(nameof(ContactBase.height));

            allowSelf = serializedObject.FindProperty(nameof(ContactBase.allowSelf));
            allowOthers = serializedObject.FindProperty(nameof(ContactBase.allowOthers));
            contentTypes = serializedObject.FindProperty(nameof(ContactBase.contentTypes));
            collisionTags = serializedObject.FindProperty(nameof(ContactBase.collisionTags));

            drawGizmos = serializedObject.FindProperty(nameof(ContactBase.drawGizmos));
            gizmoColor = serializedObject.FindProperty(nameof(ContactBase.gizmoColor));

            contactValue = serializedObject.FindProperty(nameof(ContactReceiver.contactValue));
            
            // Receiver
            receiverType = serializedObject.FindProperty(nameof(ContactReceiver.receiverType));
        }

        public override void OnInspectorGUI()
        {
            if (!_contactBase) return; // Occurs on domain reload
            
            serializedObject.Update();

            foldShape = EditorGUILayout.Foldout(foldShape, "Shape", true, EditorStyles.foldoutHeader);
            if (foldShape)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(shapeType);
                EditorGUILayout.PropertyField(localPosition);
                EditorGUILayout.PropertyField(localRotation);
                EditorGUILayout.PropertyField(radius);

                if ((ShapeType)shapeType.enumValueIndex == ShapeType.Capsule)
                    EditorGUILayout.PropertyField(height);
                
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);
            
            foldFiltering = EditorGUILayout.Foldout(foldFiltering, "Filtering", true, EditorStyles.foldoutHeader);
            if (foldFiltering)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(allowSelf);
                EditorGUILayout.PropertyField(allowOthers);
                if (_contactBase is ContactReceiver) EditorGUILayout.PropertyField(contentTypes);
                EditorGUILayout.PropertyField(collisionTags, true);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);
            
            foldRole = EditorGUILayout.Foldout(
                foldRole,
                _contactBase is ContactReceiver ? "Receiver" : "Sender",
                true, EditorStyles.foldoutHeader
            );
            if (foldRole)
            {
                EditorGUI.indentLevel++;
                if (target is ContactReceiver)
                {
                    EditorGUILayout.PropertyField(receiverType);
                    ReceiverType type = (ReceiverType)receiverType.enumValueIndex;
                    switch (type)
                    {
                        case ReceiverType.Constant:
                            EditorGUILayout.PropertyField(contactValue, new GUIContent("Value"));
                            EditorGUILayout.HelpBox("Returns this value when there is any contact.", MessageType.Info);
                            break;
                        case ReceiverType.OnEnter:
                            EditorGUILayout.PropertyField(contactValue, new GUIContent("Min Velocity"));
                            EditorGUILayout.HelpBox("Returns 1 for one frame if the initial contact velocity is above the set min velocity.", MessageType.Info);
                            break;
                        case ReceiverType.CopyValueFromSender:
                            EditorGUILayout.PropertyField(contactValue, new GUIContent("Min Velocity"));
                            EditorGUILayout.HelpBox("Returns the Sender value if the contact velocity is above the set min velocity.", MessageType.Info);
                            break;
                        case ReceiverType.ProximitySenderToReceiver:
                            EditorGUILayout.HelpBox("Returns 0 to 1 measured from the Receivers center to the Senders surface.", MessageType.Info);
                            break;
                        case ReceiverType.ProximityReceiverToSender:
                            EditorGUILayout.HelpBox("Returns 0 to 1 measured from the Receivers surface to the Senders center.", MessageType.Info);
                            break;
                        case ReceiverType.ProximityCenterToCenter:
                            EditorGUILayout.HelpBox("Returns 0 to 1 measured from the Receivers center to the Senders center.", MessageType.Info);
                            break;
                        case ReceiverType.VelocityReceiver:
                            EditorGUILayout.HelpBox("Returns the velocity of the Receiver when there is any contact.", MessageType.Info);
                            break;
                        case ReceiverType.VelocitySender:
                            EditorGUILayout.HelpBox("Returns the velocity of the fastest Sender making contact.", MessageType.Info);
                            break;
                        case ReceiverType.VelocityMagnitude:
                            EditorGUILayout.HelpBox("Returns the combined velocity of the Receiver and fastest Sender making contact.", MessageType.Info);
                            break;
                    }
                }
                else if (target is ContactSender)
                {
                    EditorGUILayout.PropertyField(contactValue);
                    EditorGUILayout.HelpBox("The value for a Receiver to copy if configured as CopyValueFromSender. If unsure, leave as 1.", MessageType.Info);
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);
            foldGizmos = EditorGUILayout.Foldout(foldGizmos, "Gizmos", true, EditorStyles.foldoutHeader);
            if (foldGizmos)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(drawGizmos);
                EditorGUILayout.PropertyField(gizmoColor);
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}