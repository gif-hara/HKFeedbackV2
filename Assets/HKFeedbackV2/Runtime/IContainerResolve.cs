namespace HKFeedbackV2
{
    public interface IContainerResolve
    {
        T Resolve<T>();

        T Resolve<T>(ContainerKey key);

        bool TryResolve<T>(out T value);

        bool TryResolve<T>(ContainerKey key, out T value);
    }
}
