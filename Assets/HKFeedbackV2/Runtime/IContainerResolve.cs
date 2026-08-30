namespace HKFeedbackV2
{
    public interface IContainerResolve
    {
        T Resolve<T>();

        T Resolve<T>(ContainerKey key);
    }
}
