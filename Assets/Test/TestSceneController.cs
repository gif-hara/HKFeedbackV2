using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace HKFeedbackV2.Test
{
    public partial class TestSceneController : MonoBehaviour, IContainerRegister
    {
        [SerializeReference]
#if HK_FEEDBACK_V2_SERIALIZE_REFERENCE_EXTENSIONS
        [SubclassSelector]
#endif
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

        [FeedbackAction]
        private void TestArgumentAction(int[] hoge, float fuga)
        {
            Debug.Log($"TestArgumentAction: {hoge}, {fuga}");
        }

        public void Register(ContainerKey key, IContainerBuilder builder)
        {
            builder.Register(key, this);
            builder.Register(key, transform);
            builder.Register(key, gameObject);
            builder.Register<object>(key, this);
        }
    }
}
