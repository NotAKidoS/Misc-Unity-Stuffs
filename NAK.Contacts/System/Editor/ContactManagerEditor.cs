using System.Globalization;
using UnityEditor;

namespace NAK.Contacts
{
    [CustomEditor(typeof(ContactManager))]
    public class ContactManagerEditor : Editor
    {
        private ContactManager _manager;
                
        private void OnEnable()
        {
            if (!target) return; // Occurs on domain reload
            _manager = target as ContactManager;
        }
        
        public override void OnInspectorGUI()
        {
            if (!_manager) return; // Occurs on domain reload
            
            // draw stats
            EditorGUILayout.LabelField("Managed Contacts", _manager.ManagedContacts.ToString(), EditorStyles.boldLabel);
            EditorGUILayout.LabelField("- Senders", _manager.SenderCount.ToString(), EditorStyles.boldLabel);
            EditorGUILayout.LabelField("- Receivers", _manager.ReceiverCount.ToString(), EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Total Pairs", _manager.TotalPairs.ToString(), EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Processing Time Ms", _manager.ProcessTimeMs.ToString(CultureInfo.InvariantCulture), EditorStyles.boldLabel);
            
            // force repaint
            Repaint();
        }
    }
}