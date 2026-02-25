using UnityEngine;

namespace NAK.Contacts
{
    public class ContactAnimator : MonoBehaviour
    {
        public Animator animator;
        public string parameter;

        private ContactReceiver _contactReceiver;
        private AnimatorControllerParameterType _parameterType;
        private int _parameterId;
        
        private void Start()
        {
            // Validate component setup
            if (!TryGetComponent(out _contactReceiver)) return;
            if (string.IsNullOrEmpty(parameter)) return;
            if (!animator) return;
            
            _parameterId = Animator.StringToHash(parameter);

            bool found = false;
            foreach (AnimatorControllerParameter p in animator.parameters)
            {
                if (p.nameHash == _parameterId)
                {
                    _parameterType = p.type;
                    found = true;
                    break;
                }
            }
            if (!found) return;
            
            _contactReceiver.OnContactEnter += OnContactEnter;
            _contactReceiver.OnContactUpdate += OnContactUpdate;
            _contactReceiver.OnContactExit += OnContactExit;
        }

        private void OnDestroy()
        {
            _contactReceiver.OnContactEnter -= OnContactEnter;
            _contactReceiver.OnContactUpdate -= OnContactUpdate;
            _contactReceiver.OnContactExit -= OnContactExit;
        }

        private void OnContactEnter(ContactCollisionInfo info) => ApplyValue(info.targetValue);
        private void OnContactUpdate(ContactCollisionInfo info) => ApplyValue(info.targetValue);
        private void OnContactExit(ContactCollisionInfo info) => ApplyValue(info.targetValue);

        private void ApplyValue(float value)
        {
            switch (_parameterType)
            {
                case AnimatorControllerParameterType.Float:
                    animator.SetFloat(_parameterId, value);
                    break;
                case AnimatorControllerParameterType.Int:
                    animator.SetInteger(_parameterId, Mathf.RoundToInt(value));
                    break;
                case AnimatorControllerParameterType.Bool:
                case AnimatorControllerParameterType.Trigger: // Triggers are just special bools
                    animator.SetBool(_parameterId, value > 0f);
                    break;
            }
        }
    }
}