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
            var container = new Container();
            container.Register(ContainerKey.Anonymous, this);
            feedbacks.InvokeAsync(container, default).Forget();
        }
    }
}
