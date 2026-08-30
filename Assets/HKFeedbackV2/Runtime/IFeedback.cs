using System.Threading;
using Cysharp.Threading.Tasks;

namespace HKFeedbackV2
{
    public interface IFeedback
    {
        UniTask InvokeAsync(IContainer container, CancellationToken cancellationToken);
    }
}
