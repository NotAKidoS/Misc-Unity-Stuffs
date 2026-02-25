using UnityEngine;

namespace NAK.Contacts
{
    public class ContactReceiver : ContactBase
    {
        public ReceiverType receiverType = ReceiverType.Constant;
        
        public System.Action<ContactCollisionInfo> OnContactEnter;
        public System.Action<ContactCollisionInfo> OnContactUpdate;
        public System.Action<ContactCollisionInfo> OnContactExit;
        
        private void Reset()
        {
            gizmoColor = new Color(1f, 0f, 1f, 0.7f);
        }
    }
}