using Cysharp.Threading.Tasks;
using UnityEngine;

namespace HKFeedbackV2.Test
{
    public class TestSceneController : MonoBehaviour
    {
        [SerializeReference, SubclassSelector]
        private IFeedback[] feedbacks;

        private void Start()
        {
            var container = new ContainerBuilder()
                .Register(this)
                .Build();
            feedbacks.InvokeAsync(container, default).Forget();
        }
    }
}
