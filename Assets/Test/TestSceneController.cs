using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace HKFeedbackV2.Test
{
    public partial class TestSceneController : MonoBehaviour
    {
        [SerializeReference, SubclassSelector]
        private IAction[] feedbacks;

        private void Start()
        {
            var container = new Container();
            container.Register(ContainerKey.Anonymous, this);
            feedbacks.InvokeAsync(container, default).Forget();
        }

        [FeedbackAction]
        public void FeedbackActionTest()
        {

        }

        [FeedbackAction]
        private UniTask TestUniTaskAsync(CancellationToken cancellationToken)
        {
            return UniTask.CompletedTask;
        }
    }
}
