using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace HKFeedbackV2
{
    [CreateAssetMenu(fileName = "ContainerKey", menuName = "HKFeedbackV2/ContainerKey")]
    public sealed partial class ContainerKey : ScriptableObject
    {
        [AutoStaticsCleanup]
        private static ContainerKey anonymous = null;

        public static ContainerKey Anonymous
        {
            get
            {
                if (anonymous == null)
                {
                    anonymous = CreateInstance<ContainerKey>();
                    anonymous.name = "Anonymous";
                }
                return anonymous;
            }
        }
    }
}
